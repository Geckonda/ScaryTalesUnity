﻿using ScaryTales.Enums;
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
        /// Запустить игру
        /// </summary>
        public void StartGame();
        /// <summary>
        /// Раздача карт всем игрокам (по 5 карт)
        /// </summary>
        public void DrawCardsToPlayersHand();
        /// <summary>
        /// Пытается вытянуть карту из колоды, если она не пуста.
        /// </summary>
        /// <returns>Карта (БЕЗ ВЛАДЕЛЬЦА) или null.</returns>
        public Card? TryDrawCardFromDeck();
        public void Run();
        /// <summary>
        /// Ход игрока
        /// </summary>
        public void GameCourse();
        /// <summary>
        /// Взять 1 карту из колоды и передать игроку
        /// </summary>
        /// <param name="player"></param>
        public void DrawCard(Player player);
        /// <summary>
        /// Разыгрывание игроком предмета (По желанию)
        /// </summary>
        public void PlayItem(Player player);
        /// <summary>
        /// Разыгрывание игрком карты
        /// </summary>
        public Task PlayCard(Player player);
        /// <summary>
        /// Разыгрывание игрком карты
        /// </summary>
        public Task PlayCard(Card card);
        /// <summary>
        /// Активирует все постоянные эффекты активных карт игрока
        /// </summary>
        public Task ActivateAllPlayerPermanentCardEffects(Player player);
        /// <summary>
        /// Активируется мгновенный эффект карты
        /// </summary>
        public Task ActivateInstantCardEffect(Card card);
        /// <summary>
        /// Активируется постоянный эффект карты
        /// </summary>
        public Task ActivatePermanentCardEffect(Card card);
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
        /// Положить карту в слот День/Ночь
        /// </summary>
        public void PutCardInTimeOfDaySlot(Card card);
        /// <summary>
        /// Поместить предмет в интвентарь игрока
        /// </summary>
        public void PutItemInPlayerItemBag(Item item, Player player);
        /// <summary>
        /// Закончить игру
        /// </summary>
        public void EndGame();


        public event Action<Card, Player>? OnCardAddedToHand;
        public event Action<Card>? OnCardPlayed;
        public event Action<Card>? OnCardMovedToDiscardPile;
        public event Action<Card>? OnCardMovedToBoard;
        public event Action<Card>? OnCardMovedToBeforePlayer;
        public event Action<Card>? OnCardMovedToTimeOfDaySlot;
        public event Action<Item, Player>? OnItemAddToPlayer;
        public event Action<Player>? OnAddPointsToPlayer;
        public event Action<string>? OnMessagePrinted;

    }
}
