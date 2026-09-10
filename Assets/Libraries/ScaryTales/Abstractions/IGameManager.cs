using Assets.Libraries.ScaryTales.Abstractions;
using ScaryTales.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScaryTales.Abstractions
{
    public interface IGameManager
    {
        /// <summary>
        /// Вывод сообщения пользователю
        /// </summary>
        /// <param name="message"></param>
        public void PrintMessage(string message);
        /// <summary>
        /// Пытается вытянуть карту из колоды, если она не пуста.
        /// </summary>
        /// <returns>Карта (БЕЗ ВЛАДЕЛЬЦА) или null.</returns>
        public Card? TryDrawCardFromDeck();
        /// <summary>
        /// Взять 1 карту из колоды и передать игроку
        /// </summary>
        /// <param name="player"></param>
        public void DrawCard(Player player);
        /// <summary>
        /// Разыгрывание игрком карты
        /// </summary>
        public Task PlayCard(Card card);
        /// <summary>
        /// Активирует все постоянные эффекты активных карт игрока
        /// </summary>
        public Task ActivateAllPlayerPermanentCardEffects(Player player);

        /// <summary>
        /// Правила начала хода от карт, лежащих перед игроком: сброс тех, чьё
        /// условие выполнено, и выдача права на дополнительные розыгрыши.
        /// Зовётся до обязательного взятия — см. <c>Card.ShouldDiscardAtTurnStart</c>.
        /// </summary>
        public void ApplyStartOfTurnBoardRules(Player player);
        /// <summary>
        /// Активируется мгновенный эффект карты
        /// </summary>
        public Task ActivateInstantCardEffect(Card card);
        /// <summary>
        /// Активируется постоянный эффект карты
        /// </summary>
        public Task ActivatePermanentCardEffect(Card card);
        /// <summary>
        /// Активируется эффект правила игры
        /// </summary>
        public Task ActivateRuleEffect(IRuleEffect effect);
        /// <summary>
        /// Присвоение пользователю ПО
        /// </summary>
        /// <param name="player">Кому присвоить</param>
        /// <param name="points">Сколько ПО присвоить</param>
        public void AddPointsToPlayer(Player player, int points);
        /// <summary>
        /// Перемещает карту, согласно ее позиции после разыгрывания
        /// </summary>
        public void MoveCardToItsPosition(Card card);
        /// <summary>
        /// Положить карту в колоду сброса
        /// </summary>
        public void PutCardToDiscardPile(Card card);
        /// <summary>
        /// Положить карту на поле
        /// </summary>
        /// <param name="card"></param>
        public void PutCardOnBoard(Card card);
        /// <summary>
        /// Положить карту перед игроком
        /// </summary>
        public void PutCardBeforePlayer(Card card);
        /// <summary>
        /// Положить карту в руку игроку
        /// </summary>
        /// <param name="player">Игрок, в руку которому будет плолжена карта</param>
        public void PutCardInPlayerHand(Card card, Player player);
        /// <summary>
        /// Положить карту в руку игроку из колоды сброса
        /// </summary>
        /// <param name="player">Игрок, в руку которому будет плолжена карта</param>
        public void PutCardInPlayerHandFromDiscardPile(Card card, Player player);
        /// <summary>
        /// Положить карту в слот День/Ночь
        /// </summary>
        public void PutCardInTimeOfDaySlot(Card card);
        /// <summary>
        /// Убрать карту из партии совсем — ни в руке, ни на столе, ни в сбросе.
        ///
        /// <para>Отличается от сброса тем, что вернуть карту нечем: из сброса
        /// карты возвращаются (Дракон, правила), отсюда — никак. Своей зоны у
        /// удалённой карты нет, она просто никуда не кладётся.</para>
        ///
        /// <para>Карту, всё ещё лежащую в колоде, метод снимает сам: Тайная
        /// помощница смотрит верх колоды, не снимая карт, и удаление лишней —
        /// часть этого хода, а не отдельный шаг.</para>
        /// </summary>
        public void RemoveCardFromGame(Card card);
        /// <summary>
        /// Поместить предмет в интвентарь игрока
        /// </summary>
        public void PutItemInPlayerItemBag(Item item, Player player);
        /// <summary>
        /// Удалить предмет конкретного типа из инвентаря игрока (с уведомлением).
        /// </summary>
        public void RemoveItemFromPlayerItemBag(ItemType type, Player player);
        /// <summary>
        /// Закончить игру
        /// </summary>
        public void EndGame();

        /// <summary>
        /// Все игроки показывают карты на руках — или прячут обратно.
        ///
        /// <para>Ничего не меняет в состоянии игры — это чистый показ. Клиенты
        /// и так знают все карты во всех руках (карта взятия рассылается всей
        /// комнате), чужие просто рисуются рубашкой; событие говорит им, когда
        /// эту рубашку на время убрать.</para>
        ///
        /// <para>Раскрытие и возврат — два вызова, а не один с паузой внутри:
        /// между ними сервер начисляет очки и выдаёт карты, и пока это идёт,
        /// руки должны оставаться открытыми. Сколько именно их держать, решает
        /// клиент — темп показа его дело.</para>
        /// </summary>
        /// <param name="by">Кто раскрыл — тот, чья карта это сделала.</param>
        /// <param name="revealed">Открыть или вернуть как было.</param>
        public void RevealAllHands(Player by, bool revealed);

        /// <summary>
        /// Одна карта выступает вперёд из раскрытой руки — или возвращается.
        ///
        /// <para>Так показывают, за что именно даются очки и карты: каждое
        /// место выезжает, приносит очко и уезжает; каждая цыганка выезжает,
        /// приносит карту и уезжает. Выезд и заезд — два вызова, а не один с
        /// паузой внутри: между ними сервер начисляет очко или выдаёт карту, и
        /// порядок этих событий у клиента и есть порядок показа.</para>
        /// </summary>
        public void ShowcaseCard(Card card, bool stepOut);

        /// <summary>
        /// Игрок смотрит верх колоды: столько-то рубашек приподнимается над ней.
        ///
        /// <para>Чистый показ, состояние партии он не меняет — карты остаются в
        /// колоде, пока игрок не ответил. Нужен ради тех, кто за столом: сам
        /// смотрящий видит окно выбора, а остальные без этого видели бы паузу
        /// непонятно из-за чего.</para>
        ///
        /// <para><b>Номера карт не передаются намеренно.</b> Показываются
        /// рубашки, и какие именно карты за ними — знать никому не нужно.
        /// Убирать рубашки отдельным вызовом не требуется: их разбирают
        /// последствия выбора (одна улетает из игры, вторая уходит в руку), а
        /// застрявшие снимает смена хода.</para>
        /// </summary>
        /// <param name="by">Кто смотрит — над чьим ходом идёт пауза.</param>
        /// <param name="count">Сколько карт он смотрит.</param>
        public void ShowPeekedCards(Player by, int count);

        /// <summary>
        /// Дать текущему игроку разыграть ещё одну карту в этом ходу.
        ///
        /// <para>Вход для эффектов вида «можно разыграть 1 дополнительную
        /// карту». Право <b>предлагается, а не навязывается</b>: игрок вправе
        /// закончить ход, не тратя его. Нерастраченное сгорает со сменой хода.</para>
        ///
        /// <para>Сколько именно карт получится разыграть, решает игровой цикл
        /// (<c>Room.RunGameLoopAsync</c>): пустая рука обрывает предложение
        /// сама собой.</para>
        /// </summary>
        public void GrantExtraPlay();


        public event Action<Card, Player>? OnCardAddedToHand;
        public event Action<Card, Player>? OnCardAddedToHandFromDiscardPile;
        public event Action<Card>? OnCardPlayed;
        public event Action<Card>? OnCardMovedToDiscardPile;
        public event Action<Card>? OnCardMovedToBoard;
        public event Action<Card>? OnCardMovedToBeforePlayer;
        public event Action<Card>? OnCardMovedToTimeOfDaySlot;
        public event Action<Card>? OnCardRemovedFromGame;
        public event Action<Item, Player>? OnItemAddToPlayer;
        public event Action<Item, Player>? OnItemRemovedFromPlayer;
        public event Action<Player>? OnAddPointsToPlayer;
        public event Action<string>? OnMessagePrinted;
        public event Action<Player, bool>? OnHandsRevealed;
        public event Action<Card, bool>? OnCardShowcased;
        public event Action<Player, int>? OnCardsPeeked;
    }
}
