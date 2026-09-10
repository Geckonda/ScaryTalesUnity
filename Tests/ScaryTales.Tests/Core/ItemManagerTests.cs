using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ScaryTales;
using ScaryTales.Enums;
using ScaryTales.Items;

namespace ScaryTales.Tests.Core
{
    [TestFixture]
    public class ItemManagerTests
    {
        private static ItemManager Supply() => new ItemManager(GameBuilder.MakeItemTemplates());

        // ---- Запас ----

        [Test]
        public void Запас_разворачивается_из_шаблонов_по_умолчанию()
        {
            var items = Supply();

            Assert.That(items.CountItemByType(ItemType.Coin), Is.EqualTo(8));
            Assert.That(items.CountItemByType(ItemType.Armor), Is.EqualTo(4));
            Assert.That(items.CountItemByType(ItemType.Sword), Is.EqualTo(4));
            Assert.That(items.CountItemByType(ItemType.MagicStick), Is.EqualTo(4));
        }

        [Test]
        public void Предметы_запаса_получают_разные_id()
        {
            // Id предмета едет на провод так же, как id карты.
            var items = new ItemManager(new List<Item> { new Coin() });
            var taken = new List<int>();

            Item item;
            while ((item = items.GetItemByType(ItemType.Coin)) != null) taken.Add(item.Id);

            Assert.That(taken.Count, Is.EqualTo(8));
            Assert.That(taken.Distinct().Count(), Is.EqualTo(8));
        }

        // ---- Выдача ----

        [Test]
        public void Выдача_уменьшает_запас()
        {
            var items = Supply();

            var sword = items.GetItemByType(ItemType.Sword);

            Assert.That(sword, Is.Not.Null);
            Assert.That(sword.Type, Is.EqualTo(ItemType.Sword));
            Assert.That(items.CountItemByType(ItemType.Sword), Is.EqualTo(3));
        }

        [Test]
        public void Выдача_не_трогает_другие_типы()
        {
            var items = Supply();
            items.GetItemByType(ItemType.Sword);

            Assert.That(items.CountItemByType(ItemType.Coin), Is.EqualTo(8));
            Assert.That(items.CountItemByType(ItemType.Armor), Is.EqualTo(4));
        }

        [Test]
        public void Запас_вычерпывается_и_дальше_отдаёт_null()
        {
            // Пустой запас не отменяет эффект правила — очки всё равно
            // начисляются, — поэтому важно, что здесь null, а не исключение.
            var items = Supply();
            for (int i = 0; i < 4; i++)
                Assert.That(items.GetItemByType(ItemType.Sword), Is.Not.Null, $"меч {i + 1}");

            Assert.That(items.GetItemByType(ItemType.Sword), Is.Null);
            Assert.That(items.CountItemByType(ItemType.Sword), Is.Zero);
        }

        [Test]
        public void Выданный_предмет_это_отдельный_объект()
        {
            var items = Supply();

            var first = items.GetItemByType(ItemType.Coin);
            var second = items.GetItemByType(ItemType.Coin);

            Assert.That(first, Is.Not.SameAs(second));
        }

        // ---- Показ без выдачи ----

        [Test]
        public void Копия_предмета_показывается_не_трогая_запас()
        {
            // Нужно, чтобы показать игроку, что он получит, до того как
            // предмет действительно уйдёт из запаса.
            var items = Supply();

            var preview = items.GetCloneItemByType(ItemType.Armor);

            Assert.That(preview, Is.Not.Null);
            Assert.That(preview.Type, Is.EqualTo(ItemType.Armor));
            Assert.That(items.CountItemByType(ItemType.Armor), Is.EqualTo(4), "показ не должен расходовать запас");
        }

        [Test]
        public void Копия_из_пустого_запаса_это_null()
        {
            var items = Supply();
            for (int i = 0; i < 4; i++) items.GetItemByType(ItemType.MagicStick);

            Assert.That(items.GetCloneItemByType(ItemType.MagicStick), Is.Null);
        }

        // ---- Возврат в использованные ----

        [Test]
        public void Потраченный_предмет_не_возвращается_в_запас()
        {
            // Потраченное уходит в отдельную стопку, а не обратно на стол:
            // иначе одна и та же палочка работала бы бесконечно.
            var items = Supply();
            var stick = items.GetItemByType(ItemType.MagicStick);

            items.PutItemToUsed(stick);

            Assert.That(items.CountItemByType(ItemType.MagicStick), Is.EqualTo(3));
        }

        [Test]
        public void Полный_оборот_предмета_сохраняет_общее_число()
        {
            // Предмет всегда где-то есть: в запасе, у игрока или в
            // использованных. Пропасть он не должен.
            var items = Supply();
            var player = new Player(1, "А");

            var coin = items.GetItemByType(ItemType.Coin);
            player.AddItemToItemBag(coin);
            Assert.That(items.CountItemByType(ItemType.Coin) + player.ItemAmount(ItemType.Coin), Is.EqualTo(8));

            player.RemoveItemFromItemBag(coin);
            items.PutItemToUsed(coin);
            Assert.That(items.CountItemByType(ItemType.Coin), Is.EqualTo(7));
            Assert.That(player.ItemAmount(ItemType.Coin), Is.Zero);
        }
    }
}
