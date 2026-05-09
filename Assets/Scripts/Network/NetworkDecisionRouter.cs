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
    /// Per Phase-1 non-goals, no timeout / cancellation: a disconnected
    /// deciding player hangs the game indefinitely. Instrumented via
    /// _parked.Count for visibility.
    /// </summary>
    public class NetworkDecisionRouter : IDecisionRouter
    {
        // RequestId → boxed TaskCompletionSource<T>. Boxed because each
        // request kind has its own resolution type; the typed lookup happens
        // when the matching intent handler fires.
        private readonly Dictionary<int, object> _parked = new();
        // RequestId → playerId we asked, used to reject resolutions from
        // the wrong connection.
        private readonly Dictionary<int, int> _decidingPlayer = new();
        private readonly IReadOnlyDictionary<int, NetworkConnectionToClient> _connections;
        private int _nextRequestId = 1;

        public int PendingDecisionCount => _parked.Count;

        public NetworkDecisionRouter(IReadOnlyDictionary<int, NetworkConnectionToClient> connections)
        {
            _connections = connections;
            NetworkServer.RegisterHandler<ResolveCardPickIntent>(OnResolveCardPick);
            NetworkServer.RegisterHandler<ResolveItemPickIntent>(OnResolveItemPick);
            NetworkServer.RegisterHandler<ResolveRuleEffectPickIntent>(OnResolveRuleEffectPick);
            NetworkServer.RegisterHandler<ResolveConfirmIntent>(OnResolveConfirm);
        }

        public Task<CardPick> PickCard(int playerId, PickCardRequest request)
        {
            var (id, tcs) = Park<CardPick>(playerId);
            NetworkServer.SendToAll(new DecisionRequestedEvent
            {
                RequestId = id,
                PlayerId = playerId,
                Kind = (int)DecisionKind.PickCard,
                CandidateIds = request.CandidateCardIds.ToArray(),
                Prompt = string.Empty,
            });
            return tcs.Task;
        }

        public Task<ItemPick> PickItem(int playerId, PickItemRequest request)
        {
            var (id, tcs) = Park<ItemPick>(playerId);
            NetworkServer.SendToAll(new DecisionRequestedEvent
            {
                RequestId = id,
                PlayerId = playerId,
                Kind = (int)DecisionKind.PickItem,
                CandidateIds = request.CandidateItemTypes.Select(t => (int)t).ToArray(),
                Prompt = string.Empty,
            });
            return tcs.Task;
        }

        public Task<RuleEffectPick> PickRuleEffect(int playerId, PickRuleEffectRequest request)
        {
            var (id, tcs) = Park<RuleEffectPick>(playerId);
            NetworkServer.SendToAll(new DecisionRequestedEvent
            {
                RequestId = id,
                PlayerId = playerId,
                Kind = (int)DecisionKind.PickRuleEffect,
                CandidateIds = request.CandidateRuleEffectIds.ToArray(),
                Prompt = string.Empty,
            });
            return tcs.Task;
        }

        public Task<ConfirmPick> Confirm(int playerId, ConfirmRequest request)
        {
            var (id, tcs) = Park<ConfirmPick>(playerId);
            NetworkServer.SendToAll(new DecisionRequestedEvent
            {
                RequestId = id,
                PlayerId = playerId,
                Kind = (int)DecisionKind.Confirm,
                CandidateIds = Array.Empty<int>(),
                Prompt = request.Prompt ?? string.Empty,
            });
            return tcs.Task;
        }

        private (int id, TaskCompletionSource<T> tcs) Park<T>(int playerId)
        {
            var id = _nextRequestId++;
            // RunContinuationsAsynchronously: completion happens on the
            // network handler thread (Mirror runs handlers on the main
            // thread, but it's a defensive choice that decouples the
            // resumption from the handler stack).
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _parked[id] = tcs;
            _decidingPlayer[id] = playerId;
            return (id, tcs);
        }

        private void OnResolveCardPick(NetworkConnectionToClient conn, ResolveCardPickIntent msg)
        {
            if (TryClaim<CardPick>(conn, msg.RequestId, out var tcs))
            {
                tcs.SetResult(new CardPick(msg.CardId));
                BroadcastResolved(msg.RequestId);
            }
        }

        private void OnResolveItemPick(NetworkConnectionToClient conn, ResolveItemPickIntent msg)
        {
            if (TryClaim<ItemPick>(conn, msg.RequestId, out var tcs))
            {
                tcs.SetResult(new ItemPick((ItemType)msg.ItemType));
                BroadcastResolved(msg.RequestId);
            }
        }

        private void OnResolveRuleEffectPick(NetworkConnectionToClient conn, ResolveRuleEffectPickIntent msg)
        {
            if (TryClaim<RuleEffectPick>(conn, msg.RequestId, out var tcs))
            {
                int? picked = msg.HasPick ? msg.RuleEffectId : (int?)null;
                tcs.SetResult(new RuleEffectPick(picked));
                BroadcastResolved(msg.RequestId);
            }
        }

        private void OnResolveConfirm(NetworkConnectionToClient conn, ResolveConfirmIntent msg)
        {
            if (TryClaim<ConfirmPick>(conn, msg.RequestId, out var tcs))
            {
                tcs.SetResult(new ConfirmPick(msg.Confirmed));
                BroadcastResolved(msg.RequestId);
            }
        }

        private bool TryClaim<T>(NetworkConnectionToClient conn, int requestId, out TaskCompletionSource<T> tcs)
        {
            tcs = null;
            if (!_parked.TryGetValue(requestId, out var boxed))
            {
                Debug.LogWarning($"[NetworkDecisionRouter] Unknown requestId {requestId}");
                return false;
            }
            if (boxed is TaskCompletionSource<T> typed)
            {
                if (_decidingPlayer.TryGetValue(requestId, out var expectedPlayerId))
                {
                    if (!_connections.TryGetValue(expectedPlayerId, out var expectedConn) || expectedConn != conn)
                    {
                        Debug.LogWarning($"[NetworkDecisionRouter] requestId {requestId}: resolution from wrong connection (expected player {expectedPlayerId})");
                        return false;
                    }
                }
                _parked.Remove(requestId);
                _decidingPlayer.Remove(requestId);
                tcs = typed;
                return true;
            }
            Debug.LogError($"[NetworkDecisionRouter] requestId {requestId}: resolution shape mismatch (expected {typeof(T).Name})");
            return false;
        }

        private static void BroadcastResolved(int requestId)
        {
            NetworkServer.SendToAll(new DecisionResolvedEvent { RequestId = requestId });
        }
    }
}
