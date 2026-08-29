using Assets.Scripts.Network.Messages;
using Mirror;
using ScaryTales.Decisions;
using ScaryTales.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace Assets.Scripts.Network
{
    /// <summary>
    /// Thrown into an effect that is awaiting a decision when that decision
    /// can no longer be answered — today, because the deciding player left.
    ///
    /// Derives from OperationCanceledException so callers can catch it with
    /// the ordinary cancellation idiom, but carries a reason and its own
    /// type so an abandoned decision is distinguishable from any other
    /// cancellation. Nothing in Assets/Libreries catches anything, so this
    /// propagates cleanly out of an effect and up to the server turn loop.
    /// </summary>
    public class DecisionAbandonedException : OperationCanceledException
    {
        public int PlayerId { get; }

        public DecisionAbandonedException(int playerId, string reason)
            : base($"Decision abandoned for player {playerId}: {reason}")
        {
            PlayerId = playerId;
        }
    }

    /// <summary>
    /// Server-only IDecisionRouter. Replaces PlayerInputAdapterRouter when
    /// the engine moves off the clients (Phase 3 cutover).
    ///
    /// Each PickX call:
    ///   1. Generates a unique RequestId.
    ///   2. Parks a TaskCompletionSource keyed by RequestId.
    ///   3. Broadcasts a DecisionRequestedEvent to all clients.
    ///   4. Returns the awaitable; the calling effect on the server suspends
    ///      until a matching ResolveDecisionIntent arrives from the deciding
    ///      client's UI.
    ///
    /// On resolution: the parked TCS is completed (effect resumes) and a
    /// DecisionResolvedEvent is broadcast so non-deciding clients can dismiss
    /// any "waiting on player X" indicator.
    ///
    /// Phase 6.1: a parked decision is no longer immortal. CancelForPlayer /
    /// CancelAll fault every matching TCS with a DecisionAbandonedException,
    /// which unwinds the suspended effect and lets the room end instead of
    /// wedging. PendingDecisionCount stays exposed so a stuck room is
    /// visible in logs.
    /// </summary>
    public class NetworkDecisionRouter : IDecisionRouter, IDisposable
    {
        /// <summary>
        /// One suspended decision. Holds the request verbatim alongside the
        /// TCS: cancelling is all we do with it today, but re-sending that
        /// same DecisionRequestedEvent to a returning connection is exactly
        /// what a future reconnect flow needs, and keeping it costs nothing.
        /// </summary>
        private sealed class PendingDecision
        {
            public int PlayerId;
            public DecisionRequestedEvent Request;
            // Faults the underlying TaskCompletionSource<T> without this
            // class having to know T. Closed over at Park time.
            public Action<Exception> Fail;
            // Hands back the TaskCompletionSource<T> for a typed claim.
            public object Tcs;
        }

        private readonly Dictionary<int, PendingDecision> _parked = new();
        private readonly RoomChannel _channel;
        private int _nextRequestId = 1;
        private bool _disposed;

        public int PendingDecisionCount => _parked.Count;

        public NetworkDecisionRouter(RoomChannel channel)
        {
            _channel = channel;
            NetworkServer.RegisterHandler<ResolveCardPickIntent>(OnResolveCardPick);
            NetworkServer.RegisterHandler<ResolveItemPickIntent>(OnResolveItemPick);
            NetworkServer.RegisterHandler<ResolveRuleEffectPickIntent>(OnResolveRuleEffectPick);
            NetworkServer.RegisterHandler<ResolveConfirmIntent>(OnResolveConfirm);
        }

        public Task<CardPick> PickCard(int playerId, PickCardRequest request)
        {
            return Ask<CardPick>(playerId, DecisionKind.PickCard,
                request.CandidateCardIds.ToArray(), string.Empty);
        }

        public Task<ItemPick> PickItem(int playerId, PickItemRequest request)
        {
            return Ask<ItemPick>(playerId, DecisionKind.PickItem,
                request.CandidateItemTypes.Select(t => (int)t).ToArray(), string.Empty);
        }

        public Task<RuleEffectPick> PickRuleEffect(int playerId, PickRuleEffectRequest request)
        {
            return Ask<RuleEffectPick>(playerId, DecisionKind.PickRuleEffect,
                request.CandidateRuleEffectIds.ToArray(), string.Empty);
        }

        public Task<ConfirmPick> Confirm(int playerId, ConfirmRequest request)
        {
            return Ask<ConfirmPick>(playerId, DecisionKind.Confirm,
                Array.Empty<int>(), request.Prompt ?? string.Empty);
        }

        /// <summary>
        /// Parks a typed TCS, broadcasts the matching request, and hands the
        /// effect an awaitable. All four PickX methods differ only in the
        /// resolution type and the candidate-id namespace, so they share
        /// this body.
        /// </summary>
        private Task<T> Ask<T>(int playerId, DecisionKind kind, int[] candidateIds, string prompt)
        {
            var id = _nextRequestId++;
            var request = new DecisionRequestedEvent
            {
                RequestId = id,
                PlayerId = playerId,
                Kind = (int)kind,
                CandidateIds = candidateIds,
                Prompt = prompt,
            };

            // RunContinuationsAsynchronously: completion happens on the
            // network handler thread (Mirror runs handlers on the main
            // thread, but it's a defensive choice that decouples the
            // resumption from the handler stack).
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _parked[id] = new PendingDecision
            {
                PlayerId = playerId,
                Request = request,
                Tcs = tcs,
                Fail = e => tcs.TrySetException(e),
            };

            _channel.SendToRoom(request);
            return tcs.Task;
        }

        // ---- Cancellation (Phase 6.1) ----

        /// <summary>
        /// Abandons every decision this player still owes an answer to.
        /// Each suspended effect resumes by throwing
        /// DecisionAbandonedException, which unwinds to the server turn
        /// loop. Returns how many were cancelled.
        /// </summary>
        public int CancelForPlayer(int playerId, string reason)
        {
            var doomed = _parked
                .Where(kv => kv.Value.PlayerId == playerId)
                .Select(kv => kv.Key)
                .ToList();
            return CancelRequests(doomed, reason);
        }

        /// <summary>
        /// Abandons every parked decision, whoever owes it. Used when the
        /// room is being torn down.
        /// </summary>
        public int CancelAll(string reason)
        {
            return CancelRequests(_parked.Keys.ToList(), reason);
        }

        private int CancelRequests(List<int> requestIds, string reason)
        {
            foreach (var requestId in requestIds)
            {
                if (!_parked.TryGetValue(requestId, out var pending)) continue;
                _parked.Remove(requestId);

                Debug.Log($"[NetworkDecisionRouter] Abandoning request {requestId} " +
                          $"({(DecisionKind)pending.Request.Kind}, {pending.Request.CandidateIds?.Length ?? 0} candidates) " +
                          $"owed by player {pending.PlayerId}: {reason}");

                // Fault rather than complete: an effect that gets a fake
                // answer would mutate canonical state on its way out, and
                // the room is ending anyway.
                pending.Fail(new DecisionAbandonedException(pending.PlayerId, reason));

                // Clients drop any prompt or "waiting on X" indicator for it.
                BroadcastResolved(requestId);
            }
            return requestIds.Count;
        }

        /// <summary>
        /// Releases every parked decision and takes this router's handlers
        /// off NetworkServer. Mirror keeps one handler per message type
        /// process-wide, so a dead router left registered would swallow the
        /// next game's resolutions.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            CancelAll("router disposed");

            if (NetworkServer.active)
            {
                NetworkServer.UnregisterHandler<ResolveCardPickIntent>();
                NetworkServer.UnregisterHandler<ResolveItemPickIntent>();
                NetworkServer.UnregisterHandler<ResolveRuleEffectPickIntent>();
                NetworkServer.UnregisterHandler<ResolveConfirmIntent>();
            }
        }

        // ---- Resolution handlers ----

        private void OnResolveCardPick(NetworkConnectionToClient conn, ResolveCardPickIntent msg)
        {
            if (TryClaim<CardPick>(conn, msg.RequestId, out var tcs))
            {
                tcs.TrySetResult(new CardPick(msg.CardId));
                BroadcastResolved(msg.RequestId);
            }
        }

        private void OnResolveItemPick(NetworkConnectionToClient conn, ResolveItemPickIntent msg)
        {
            if (TryClaim<ItemPick>(conn, msg.RequestId, out var tcs))
            {
                tcs.TrySetResult(new ItemPick((ItemType)msg.ItemType));
                BroadcastResolved(msg.RequestId);
            }
        }

        private void OnResolveRuleEffectPick(NetworkConnectionToClient conn, ResolveRuleEffectPickIntent msg)
        {
            if (TryClaim<RuleEffectPick>(conn, msg.RequestId, out var tcs))
            {
                int? picked = msg.HasPick ? msg.RuleEffectId : (int?)null;
                tcs.TrySetResult(new RuleEffectPick(picked));
                BroadcastResolved(msg.RequestId);
            }
        }

        private void OnResolveConfirm(NetworkConnectionToClient conn, ResolveConfirmIntent msg)
        {
            if (TryClaim<ConfirmPick>(conn, msg.RequestId, out var tcs))
            {
                tcs.TrySetResult(new ConfirmPick(msg.Confirmed));
                BroadcastResolved(msg.RequestId);
            }
        }

        private bool TryClaim<T>(NetworkConnectionToClient conn, int requestId, out TaskCompletionSource<T> tcs)
        {
            tcs = null;
            if (!_parked.TryGetValue(requestId, out var pending))
            {
                // Also the normal path for a resolution that raced an
                // abandonment — the client answered a prompt we already
                // gave up on.
                Debug.LogWarning($"[NetworkDecisionRouter] Unknown requestId {requestId}");
                return false;
            }
            if (pending.Tcs is TaskCompletionSource<T> typed)
            {
                if (!_channel.IsSeatedAt(pending.PlayerId, conn))
                {
                    Debug.LogWarning($"[NetworkDecisionRouter] requestId {requestId}: resolution from wrong connection (expected player {pending.PlayerId})");
                    return false;
                }
                _parked.Remove(requestId);
                tcs = typed;
                return true;
            }
            Debug.LogError($"[NetworkDecisionRouter] requestId {requestId}: resolution shape mismatch (expected {typeof(T).Name})");
            return false;
        }

        private void BroadcastResolved(int requestId)
        {
            _channel.SendToRoom(new DecisionResolvedEvent { RequestId = requestId });
        }
    }
}
