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
    /// cancellation. Nothing in Assets/Libraries catches anything, so this
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
    /// Игрок сам отказался от выбора — «передумал».
    ///
    /// <para>Отдельный тип, а не <see cref="DecisionAbandonedException"/>:
    /// брошенное решение это авария (игрок исчез), а отказ — нормальный ход
    /// событий, и в логах их путать не надо. Общий предок
    /// <c>OperationCanceledException</c> означает, что оба одинаково
    /// раскручивают эффект и одинаково ловятся там, где эффект запускали.</para>
    /// </summary>
    public class DecisionDeclinedException : OperationCanceledException
    {
        public int RequestId { get; }

        public DecisionDeclinedException(int requestId)
            : base($"Decision {requestId} declined by the player.")
        {
            RequestId = requestId;
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
            // Отвечает за игрока безобидным выбором по умолчанию. Тоже
            // замкнуто на Park, потому что тип ответа знает только Ask<T>.
            // Возвращает false, если подставить нечего (например, выбрать
            // карту не из чего) — тогда решение отменяется, как раньше.
            public Func<bool> AutoResolve;
            // Hands back the TaskCompletionSource<T> for a typed claim.
            public object Tcs;
        }

        private readonly Dictionary<int, PendingDecision> _parked = new();
        // Места, которых больше нет за столом. Спрашивать их бесполезно, а
        // молча запарковать вопрос — значит подвесить комнату навсегда.
        private readonly HashSet<int> _departed = new();
        private readonly RoomChannel _channel;
        private int _nextRequestId = 1;
        private bool _disposed;

        public int PendingDecisionCount => _parked.Count;

        public NetworkDecisionRouter(RoomChannel channel)
        {
            _channel = channel;
            // Phase 6.3: this router no longer claims Mirror's handlers.
            // ServerIntentDispatcher owns them process-wide and calls the
            // OnResolve* methods below on whichever room the sender is in.
        }

        // Второй аргумент Ask — ответ за игрока, который уже не ответит.
        // Выбран так, чтобы ничего не делать там, где это возможно: правило
        // не применяется, подтверждение отклоняется. Там, где эффект обязан
        // получить выбор, берётся первый кандидат — единственный вариант,
        // не требующий от ядра знать про отключения.

        public Task<CardPick> PickCard(int playerId, PickCardRequest request)
        {
            return Ask(playerId, DecisionKind.PickCard,
                request.CandidateCardIds.ToArray(), string.Empty,
                ids => new CardPick(ids[0]), request.CanCancel);
        }

        public Task<ItemPick> PickItem(int playerId, PickItemRequest request)
        {
            return Ask(playerId, DecisionKind.PickItem,
                request.CandidateItemTypes.Select(t => (int)t).ToArray(), string.Empty,
                ids => new ItemPick((ItemType)ids[0]));
        }

        public Task<RuleEffectPick> PickRuleEffect(int playerId, PickRuleEffectRequest request)
        {
            return Ask(playerId, DecisionKind.PickRuleEffect,
                request.CandidateRuleEffectIds.ToArray(), string.Empty,
                _ => new RuleEffectPick(null));
        }

        public Task<ConfirmPick> Confirm(int playerId, ConfirmRequest request)
        {
            return Ask(playerId, DecisionKind.Confirm,
                Array.Empty<int>(), request.Prompt ?? string.Empty,
                _ => new ConfirmPick(false));
        }

        /// <summary>
        /// Parks a typed TCS, broadcasts the matching request, and hands the
        /// effect an awaitable. All four PickX methods differ only in the
        /// resolution type and the candidate-id namespace, so they share
        /// this body.
        /// </summary>
        private Task<T> Ask<T>(int playerId, DecisionKind kind, int[] candidateIds,
                               string prompt, Func<int[], T> auto, bool canCancel = false)
        {
            // Игрока уже нет за столом — ответа не будет никогда.
            //
            // Случай не надуманный и был бы вечным зависанием: игрок выходит,
            // пока его собственная карта разыгрывается, эффект доходит до
            // следующего вопроса уже после ухода и паркуется навсегда.
            // CancelForPlayer в момент ухода такой вопрос не застаёт — его
            // ещё не задали. Бросаем сразу: эффект раскрутится, а серверный
            // цикл разберёт это как несостоявшийся ход.
            if (_departed.Contains(playerId))
            {
                Debug.Log($"[NetworkDecisionRouter] {kind} for departed player {playerId} — failing immediately.");
                var abandoned = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
                abandoned.TrySetException(new DecisionAbandonedException(playerId, "игрок вышел"));
                return abandoned.Task;
            }

            var id = _nextRequestId++;
            var request = new DecisionRequestedEvent
            {
                RequestId = id,
                PlayerId = playerId,
                Kind = (int)kind,
                CandidateIds = candidateIds,
                Prompt = prompt,
                CanCancel = canCancel,
            };

            // RunContinuationsAsynchronously: completion happens on the
            // network handler thread (Mirror runs handlers on the main
            // thread, but it's a defensive choice that decouples the
            // resumption from the handler stack).
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

            // Выбор карты и предмета обязан на что-то указывать; правило и
            // подтверждение обходятся без кандидатов.
            bool needsCandidate = kind == DecisionKind.PickCard || kind == DecisionKind.PickItem;

            _parked[id] = new PendingDecision
            {
                PlayerId = playerId,
                Request = request,
                Tcs = tcs,
                Fail = e => tcs.TrySetException(e),
                AutoResolve = () =>
                {
                    if (needsCandidate && (candidateIds == null || candidateIds.Length == 0))
                        return false;
                    return tcs.TrySetResult(auto(candidateIds));
                },
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

        /// <summary>
        /// Отвечает за игрока выбором по умолчанию вместо того, чтобы
        /// отменять его решения.
        ///
        /// <para>Нужно, когда игрок ушёл, а партия продолжается без него.
        /// Отмена здесь не годится: она выбрасывает исключение внутрь чужого
        /// эффекта, а тот принадлежит ходу другого игрока — его карта уже
        /// сыграна и очки начислены, так что раскрутить его наполовину
        /// значит оставить стол в середине операции. Подставленный ответ
        /// доводит эффект до конца.</para>
        ///
        /// <para>Решение, для которого ответа не подобрать, отменяется —
        /// прежним путём.</para>
        /// </summary>
        /// <summary>
        /// Запомнить, что игрока больше нет за столом. С этого момента любой
        /// вопрос к нему проваливается сразу, не паркуясь, — см. Ask.
        /// </summary>
        public void MarkPlayerDeparted(int playerId) => _departed.Add(playerId);

        /// <returns>Сколько решений закрыто подстановкой.</returns>
        public int AutoResolveForPlayer(int playerId, string reason)
        {
            var owed = _parked
                .Where(kv => kv.Value.PlayerId == playerId)
                .Select(kv => kv.Key)
                .ToList();

            int resolved = 0;
            var unanswerable = new List<int>();

            foreach (var requestId in owed)
            {
                if (!_parked.TryGetValue(requestId, out var pending)) continue;

                // От решения, которое можно было отклонить, за ушедшего
                // отклоняемся, а не выбираем за него: раз игра допускает
                // «не хочу», это и есть самый безобидный ответ. Подставлять
                // выбор пришлось бы только там, где отказ не предусмотрен.
                if (pending.Request.CanCancel)
                {
                    unanswerable.Add(requestId);
                    continue;
                }

                if (pending.AutoResolve == null || !pending.AutoResolve())
                {
                    unanswerable.Add(requestId);
                    continue;
                }

                _parked.Remove(requestId);
                resolved++;
                Debug.Log($"[NetworkDecisionRouter] Request {requestId} " +
                          $"({(DecisionKind)pending.Request.Kind}) answered on behalf of " +
                          $"player {playerId}: {reason}");
                BroadcastResolved(requestId);
            }

            if (unanswerable.Count > 0)
                CancelRequests(unanswerable, reason);

            return resolved;
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
        /// Releases every parked decision.
        ///
        /// It deliberately does *not* unregister anything from NetworkServer
        /// any more. Under 6.3 the handlers are process-wide and shared by
        /// every room; a finished room that unregistered them would cut off
        /// all the others. Leaving the index is what retires a room now —
        /// see <see cref="ServerIntentDispatcher.UnbindRoom"/>.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            CancelAll("router disposed");
        }

        // ---- Resolution handlers ----
        // Public because ServerIntentDispatcher calls them after resolving
        // which room the sender belongs to. Not registered with Mirror here.

        public void OnResolveCardPick(NetworkConnectionToClient conn, ResolveCardPickIntent msg)
        {
            // Отказ проверяем ДО TryClaim: тот снимает решение с парковки, и
            // отвергнутый после этого отказ оставил бы эффект висеть навсегда.
            if (!msg.HasPick && !CanBeCancelled(conn, msg.RequestId))
                return;

            if (TryClaim<CardPick>(conn, msg.RequestId, out var tcs))
            {
                if (msg.HasPick)
                    tcs.TrySetResult(new CardPick(msg.CardId));
                else
                    tcs.TrySetException(new DecisionDeclinedException(msg.RequestId));

                BroadcastResolved(msg.RequestId);
            }
        }

        /// <summary>
        /// Разрешено ли отказаться от этого решения — и от того ли, кого
        /// спрашивали.
        ///
        /// <para>Проверка серверная и обязательная: без неё клиент мог бы
        /// «передумать» в ответ на любое обязательное решение и бесплатно
        /// пропустить эффект уже разыгранной карты.</para>
        /// </summary>
        private bool CanBeCancelled(NetworkConnectionToClient conn, int requestId)
        {
            if (!_parked.TryGetValue(requestId, out var pending))
            {
                Debug.LogWarning($"[NetworkDecisionRouter] Cancel for unknown requestId {requestId}");
                return false;
            }
            if (!_channel.IsSeatedAt(pending.PlayerId, conn))
            {
                Debug.LogWarning($"[NetworkDecisionRouter] Cancel for requestId {requestId} from the wrong connection.");
                return false;
            }
            if (!pending.Request.CanCancel)
            {
                Debug.LogWarning($"[NetworkDecisionRouter] requestId {requestId} is not cancellable; ignoring.");
                return false;
            }
            return true;
        }

        public void OnResolveItemPick(NetworkConnectionToClient conn, ResolveItemPickIntent msg)
        {
            if (TryClaim<ItemPick>(conn, msg.RequestId, out var tcs))
            {
                tcs.TrySetResult(new ItemPick((ItemType)msg.ItemType));
                BroadcastResolved(msg.RequestId);
            }
        }

        public void OnResolveRuleEffectPick(NetworkConnectionToClient conn, ResolveRuleEffectPickIntent msg)
        {
            if (TryClaim<RuleEffectPick>(conn, msg.RequestId, out var tcs))
            {
                int? picked = msg.HasPick ? msg.RuleEffectId : (int?)null;
                tcs.TrySetResult(new RuleEffectPick(picked));
                BroadcastResolved(msg.RequestId);
            }
        }

        public void OnResolveConfirm(NetworkConnectionToClient conn, ResolveConfirmIntent msg)
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
