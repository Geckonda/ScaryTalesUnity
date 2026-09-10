using System;
using NUnit.Framework;
using ScaryTales;
using ScaryTales.Enums;
using ScaryTales.Items;
using ScaryTales.Tests.Fakes;

namespace ScaryTales.Tests.Core
{
    [TestFixture]
    public class PlayerTests
    {
        // ---- Очки ----

        [Test]
        public void Новый_игрок_начинает_с_нуля()
        {
            var player = new Player(1, "А");

            Assert.That(player.Score, Is.Zero);
            Assert.That(player.HandCardCount, Is.Zero);
            Assert.That(player.ItemsBagCount, Is.Zero);
        }

        [Test]
        public void Очки_накапливаются()
        {
            var player = new Player(1, "А");
            player.AddPoints(2);
            player.AddPoints(3);

            Assert.That(player.Score, Is.EqualTo(5));
        }

        [Test]
        public void Ноль_очков_допустим_и_ничего_не_меняет()
        {
            var player = new Player(1, "А");
            player.AddPoints(4);

            player.AddPoints(0);

            Assert.That(player.Score, Is.EqualTo(4));
        }

        [Test]
        public void Отрицательные_очки_бросают()
        {
            // Отнять очки в этой игре нельзя ни одной картой и ни одним
            // правилом. Отрицательное число здесь — всегда ошибка вызывающего.
            var player = new Player(1, "А");

            Assert.Throws<ArgumentException>(() => player.AddPoints(-1));
            Assert.That(player.Score, Is.Zero);
        }

        // ---- Рука ----

        [Test]
        public void Карта_кладётся_в_руку_и_находится_в_ней()
        {
            var player = new Player(1, "А");
            var card = new TestCard();

            player.AddCardToHand(card);

            Assert.That(player.HandCardCount, Is.EqualTo(1));
            Assert.That(player.HasCard(card), Is.True);
            Assert.That(player.Hand, Does.Contain(card));
        }

        [Test]
        public void Чужая_карта_в_руке_не_находится()
        {
            var player = new Player(1, "А");
            player.AddCardToHand(new TestCard());

            Assert.That(player.HasCard(new TestCard()), Is.False);
        }

        [Test]
        public void Карта_убирается_из_руки()
        {
            var player = new Player(1, "А");
            var card = new TestCard();
            player.AddCardToHand(card);

            player.RemoveCardFromHand(card);

            Assert.That(player.HandCardCount, Is.Zero);
            Assert.That(player.HasCard(card), Is.False);
        }

        [Test]
        public void Удаление_карты_которой_нет_в_руке_ничего_не_ломает()
        {
            // Так бывает после ожидания: игрок мог выйти, и его рука уже
            // вернулась в колоду, пока эффект ждал ответа.
            var player = new Player(1, "А");
            player.AddCardToHand(new TestCard());

            Assert.DoesNotThrow(() => player.RemoveCardFromHand(new TestCard()));
            Assert.That(player.HandCardCount, Is.EqualTo(1));
        }

        [Test]
        public void Размер_руки_ничем_не_ограничен()
        {
            // Предела нет намеренно: партия кончается по пустой колоде, а не
            // по переполнению руки.
            var player = new Player(1, "А");
            for (int i = 0; i < 30; i++) player.AddCardToHand(new TestCard());

            Assert.That(player.HandCardCount, Is.EqualTo(30));
        }

        // ---- Сумка предметов ----

        [Test]
        public void Предмет_кладётся_в_сумку_и_находится_по_типу()
        {
            var player = new Player(1, "А");
            var sword = new Sword();

            player.AddItemToItemBag(sword);

            Assert.That(player.ItemsBagCount, Is.EqualTo(1));
            Assert.That(player.HasItem(sword), Is.True);
            Assert.That(player.HasItem(ItemType.Sword), Is.True);
            Assert.That(player.HasItem(ItemType.Coin), Is.False);
        }

        [Test]
        public void Количество_предметов_считается_по_типу()
        {
            var player = new Player(1, "А");
            player.AddItemToItemBag(new Coin());
            player.AddItemToItemBag(new Coin());
            player.AddItemToItemBag(new Sword());

            Assert.That(player.ItemAmount(ItemType.Coin), Is.EqualTo(2));
            Assert.That(player.ItemAmount(ItemType.Sword), Is.EqualTo(1));
            Assert.That(player.ItemAmount(ItemType.Armor), Is.Zero);
        }

        [Test]
        public void Удаление_по_типу_убирает_ровно_один_предмет()
        {
            // На этом стоят правила: «сбросьте монету» не должно опустошать
            // весь кошелёк.
            var player = new Player(1, "А");
            player.AddItemToItemBag(new Coin());
            player.AddItemToItemBag(new Coin());

            player.RemoveItemFromItemBag(ItemType.Coin);

            Assert.That(player.ItemAmount(ItemType.Coin), Is.EqualTo(1));
        }

        [Test]
        public void Удаление_по_типу_которого_нет_ничего_не_ломает()
        {
            var player = new Player(1, "А");
            player.AddItemToItemBag(new Coin());

            Assert.DoesNotThrow(() => player.RemoveItemFromItemBag(ItemType.Sword));
            Assert.That(player.ItemsBagCount, Is.EqualTo(1));
        }

        [Test]
        public void Удаление_конкретного_предмета_убирает_именно_его()
        {
            var player = new Player(1, "А");
            var first = new Coin();
            var second = new Coin();
            player.AddItemToItemBag(first);
            player.AddItemToItemBag(second);

            player.RemoveItemFromItemBag(first);

            Assert.That(player.HasItem(first), Is.False);
            Assert.That(player.HasItem(second), Is.True);
        }

        [Test]
        public void Показ_сумки_отдаёт_копию_а_не_саму_сумку()
        {
            // Список уходит в UI и в эффекты правил. Отдай мы внутренний —
            // любой перебор с изменением по дороге тихо чистил бы игроку сумку.
            var player = new Player(1, "А");
            player.AddItemToItemBag(new Coin());

            var shown = player.ShowItemsFromItemBag();
            shown.Clear();

            Assert.That(player.ItemsBagCount, Is.EqualTo(1));
        }
    }
}
