using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Assets.Libraries.ScaryTales.CardEffects;
using NUnit.Framework;
using ScaryTales;
using ScaryTales.CardEffects;
using ScaryTales.Cards;
using ScaryTales.Enums;
using ScaryTales.Tests.Fakes;

namespace ScaryTales.Tests.Cards
{
    /// <summary>
    /// Эффекты карт против настоящего движка и записанного заранее игрока.
    /// Спецификация — docs/CARDS.md.
    /// </summary>
    [TestFixture]
    public class CardEffectTests
    {
        private static void RunSync(Task task) => task.GetAwaiter().GetResult();

        private static List<Card> Filler(int count = 20) =>
            new List<Card> { new TestCard(name: "Пустышка", countInDeck: count) };

        // ================= Тайная помощница =================

        [Test]
        public void Помощница_даёт_розыгрыш_до_всякого_вопроса()
        {
            // Карта обещает розыгрыш словом «можно», а не в обмен на удачный
            // просмотр. Пустая колода отменяет просмотр, но не подарок.
            var game = GameFixture.Start(deck: Filler());
            game.DrainDeck();

            RunSync(new SecretHelperEffect().ApplyEffect(game.Context));

            Assert.That(game.State.ExtraPlays, Is.EqualTo(1));
            Assert.That(game.Router.AskCount, Is.Zero, "спрашивать при пустой колоде нечего");
        }

        [Test]
        public void Помощница_спрашивает_про_карты_из_колоды_а_не_со_стола()
        {
            // У карты из колоды нет представления на столе. Не пометь мы
            // запрос как fromDeck, кандидат выпал бы из подсветки, ожидание
            // клиента повисло бы, а вместе с ним и вся комната.
            var game = GameFixture.Start(deck: Filler());
            game.Router.PickFirstCard();

            RunSync(new SecretHelperEffect().ApplyEffect(game.Context));

            var request = game.Router.CardRequests.Single();
            Assert.That(request.FromDeck, Is.True);
            Assert.That(request.CandidateCardIds.Count, Is.EqualTo(2));
        }

        [Test]
        public void Помощница_забирает_выбранную_а_вторую_убирает_из_игры()
        {
            var game = GameFixture.Start(deck: Filler());
            var top = game.Deck.PeekTop(2);
            var wanted = top[1];
            var doomed = top[0];
            game.Router.PickCardId(wanted.Id);

            int deckBefore = game.Deck.CardsRemaining;
            RunSync(new SecretHelperEffect().ApplyEffect(game.Context));

            Assert.That(game.Current.Hand, Does.Contain(wanted));
            Assert.That(game.Deck.GetCardIds(), Does.Not.Contain(doomed.Id));
            Assert.That(game.Board.GetCardsFromDiscardPile(), Does.Not.Contain(doomed),
                "лишняя карта покидает игру, а не уходит в сброс");
            Assert.That(game.Deck.CardsRemaining, Is.EqualTo(deckBefore - 2));
        }

        [Test]
        public void Помощница_не_верит_поддельному_выбору_и_берёт_первую()
        {
            // Единственный эффект, где защита от подделки уже написана.
            var game = GameFixture.Start(deck: Filler());
            var top = game.Deck.PeekTop(2);
            game.Router.PickForgedCardId();

            Assert.DoesNotThrow(() => RunSync(new SecretHelperEffect().ApplyEffect(game.Context)));
            Assert.That(game.Current.Hand, Does.Contain(top[0]));
        }

        [Test]
        public void Помощница_с_одной_картой_в_колоде_не_спрашивает()
        {
            // Выбирать не из чего, удалять нечего: окно с одной кнопкой — это
            // не выбор.
            var game = GameFixture.Start(deck: new List<Card> { new TestCard(name: "Единственная") });

            RunSync(new SecretHelperEffect().ApplyEffect(game.Context));

            Assert.That(game.Router.AskCount, Is.Zero);
            Assert.That(game.Current.HandCardCount, Is.EqualTo(1));
            Assert.That(game.Deck.CardsRemaining, Is.Zero);
        }

        [Test]
        public void Помощница_показывает_рубашки_до_вопроса()
        {
            // Пока смотрящий выбирает в своём окне, остальные за столом должны
            // видеть, из-за чего пауза.
            var game = GameFixture.Start(deck: Filler());
            int peekedBeforeAsk = -1;
            game.Manager.OnCardsPeeked += (_, count) => peekedBeforeAsk = game.Router.AskCount;
            game.Router.PickFirstCard();

            RunSync(new SecretHelperEffect().ApplyEffect(game.Context));

            Assert.That(peekedBeforeAsk, Is.Zero, "рубашки показаны после вопроса — стол видел пустую паузу");
        }

        // ================= Цыганка =================

        [Test]
        public void Цыганка_даёт_очко_за_каждое_место_в_раскрытых_руках()
        {
            var game = GameFixture.Start(playerCount: 2, deck: Filler());
            game.InHand(new TestCard(name: "Пещера", type: CardType.Place));
            game.InHand(new TestCard(name: "Замок", type: CardType.Place), game.Players[1]);
            game.InHand(new TestCard(name: "Фея", type: CardType.Woman), game.Players[1]);

            RunSync(new GypsyEffect().ApplyEffect(game.Context));

            Assert.That(game.Current.Score, Is.EqualTo(2), "места считаются во всех руках, а не только в своей");
        }

        [Test]
        public void Цыганка_даёт_карту_за_каждую_раскрытую_цыганку()
        {
            var game = GameFixture.Start(playerCount: 2, deck: Filler());
            game.InHand(new GypsyCard { Id = 901 });
            game.InHand(new GypsyCard { Id = 902 }, game.Players[1]);

            RunSync(new GypsyEffect().ApplyEffect(game.Context));

            // Две цыганки в руках — две взятые карты, плюс те, что уже лежали.
            Assert.That(game.Current.HandCardCount, Is.EqualTo(3));
        }

        [Test]
        public void Взятая_цыганка_не_порождает_ещё_одно_взятие()
        {
            // Списки снимаются один раз, до всякой выдачи. Считаются те карты,
            // что были раскрыты, а не те, что появились из-за раскрытия, —
            // иначе колода из цыганок вычерпалась бы за один эффект.
            var game = GameFixture.Start(playerCount: 1,
                deck: new List<Card> { new GypsyCard(), new GypsyCard() });
            game.InHand(new GypsyCard { Id = 901 });

            RunSync(new GypsyEffect().ApplyEffect(game.Context));

            Assert.That(game.Current.HandCardCount, Is.EqualTo(2), "цепная реакция: взятая цыганка сработала");
        }

        [Test]
        public void Цыганка_раскрывает_руки_до_начисления_и_прячет_после()
        {
            // Порядок вызовов здесь и есть порядок того, что игроки увидят.
            // Обратный порядок показал бы результат раньше причины.
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            game.InHand(new TestCard(name: "Пещера", type: CardType.Place));

            var log = new List<string>();
            game.Manager.OnHandsRevealed += (_, revealed) => log.Add(revealed ? "раскрыть" : "спрятать");
            game.Manager.OnAddPointsToPlayer += _ => log.Add("очки");

            RunSync(new GypsyEffect().ApplyEffect(game.Context));

            Assert.That(log, Is.EqualTo(new[] { "раскрыть", "очки", "спрятать" }));
        }

        [Test]
        public void Цыганка_на_пустых_руках_ничего_не_делает()
        {
            var game = GameFixture.Start(playerCount: 2, deck: Filler());

            RunSync(new GypsyEffect().ApplyEffect(game.Context));

            Assert.That(game.Current.Score, Is.Zero);
            Assert.That(game.Current.HandCardCount, Is.Zero);
        }

        [Test]
        public void Цыганка_узнаётся_по_типу_карты_а_не_по_имени()
        {
            // Сравнение по имени тихо ломается при переименовании — компилятор
            // о нём не знает. Здесь проверяется, что подделка по имени не
            // считается цыганкой.
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            game.InHand(new TestCard(name: "Цыганка", type: CardType.Woman));

            RunSync(new GypsyEffect().ApplyEffect(game.Context));

            Assert.That(game.Current.HandCardCount, Is.EqualTo(1), "самозванка по имени получила карту");
        }

        // ================= Зачарованный лес =================

        [Test]
        public void Лес_днём_даёт_карту_всем()
        {
            var game = GameFixture.Start(playerCount: 2, deck: Filler());
            game.State.SetPhase(isNight: false);

            RunSync(new EnchantedForestEffect().ApplyEffect(game.Context));

            // Ходящий берёт своё «возьмите 1» плюс общее «все берут».
            Assert.That(game.Current.HandCardCount, Is.EqualTo(2));
            Assert.That(game.Players[1].HandCardCount, Is.EqualTo(1));
        }

        [Test]
        public void Лес_ночью_заставляет_всех_сбросить_карту()
        {
            var game = GameFixture.Start(playerCount: 2, deck: Filler());
            game.State.SetPhase(isNight: true);
            var mine = game.InHand(new TestCard(name: "Моя"));
            var theirs = game.InHand(new TestCard(name: "Чужая"), game.Players[1]);
            game.Router.PickCardId(mine.Id).PickCardId(theirs.Id);

            RunSync(new EnchantedForestEffect().ApplyEffect(game.Context));

            Assert.That(game.Board.GetCardsFromDiscardPile(), Does.Contain(mine).And.Contain(theirs));
            Assert.That(game.Players[1].HandCardCount, Is.Zero);
        }

        [Test]
        public void Лес_ночью_работает_на_пустой_колоде()
        {
            // Раньше здесь стоял досрочный выход по пустой колоде, и он убивал
            // ночную половину эффекта. А ночью колода не нужна вовсе — игроки
            // сбрасывают из руки. На исходе партии, когда колода как раз и
            // пустеет, лес переставал работать ровно тогда, когда сброс руки
            // решает исход.
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            game.State.SetPhase(isNight: true);
            var card = game.InHand(new TestCard(name: "Последняя"));
            game.DrainDeck();
            game.Router.PickCardId(card.Id);

            RunSync(new EnchantedForestEffect().ApplyEffect(game.Context));

            Assert.That(game.Board.GetCardsFromDiscardPile(), Does.Contain(card));
        }

        [Test]
        public void Лес_не_спрашивает_у_того_у_кого_пустая_рука()
        {
            var game = GameFixture.Start(playerCount: 2, deck: Filler());
            game.State.SetPhase(isNight: true);
            var mine = game.InHand(new TestCard(name: "Моя"));
            game.Router.PickCardId(mine.Id);

            RunSync(new EnchantedForestEffect().ApplyEffect(game.Context));

            Assert.That(game.Router.AskCount, Is.EqualTo(1), "спросили того, кому нечего сбрасывать");
        }

        [Test]
        public void Лес_не_сбрасывает_карту_игрока_ушедшего_посреди_опроса()
        {
            // Снимок руки сделан до ожидания, а за это время игрок мог выйти и
            // его карты вернуться в колоду. Сбросить такую карту значило бы
            // положить в сброс то, что лежит в колоде: один экземпляр в двух
            // зонах сразу.
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            game.State.SetPhase(isNight: true);
            var card = game.InHand(new TestCard(name: "Уехала"));
            game.Router.PickCardId(card.Id);

            // Игрок «вышел»: рука опустела до того, как пришёл ответ.
            game.Current.RemoveCardFromHand(card);

            RunSync(new EnchantedForestEffect().ApplyEffect(game.Context));

            Assert.That(game.Board.GetCardsFromDiscardPile(), Does.Not.Contain(card));
        }

        // ================= Короли =================

        [Test]
        public void Мудрость_короля_удваивает_очки_днём()
        {
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            game.State.SetPhase(isNight: false);
            game.OnBoard(new TestCard(name: "Пещера", type: CardType.Place));
            game.OnBoard(new TestCard(name: "Замок", type: CardType.Place));

            RunSync(new KingEffect(isWisdom: true).ApplyEffect(game.Context));

            Assert.That(game.Current.Score, Is.EqualTo(4));
        }

        [Test]
        public void Мудрость_короля_ночью_даёт_обычные_очки()
        {
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            game.State.SetPhase(isNight: true);
            game.OnBoard(new TestCard(name: "Пещера", type: CardType.Place));
            game.OnBoard(new TestCard(name: "Замок", type: CardType.Place));

            RunSync(new KingEffect(isWisdom: true).ApplyEffect(game.Context));

            Assert.That(game.Current.Score, Is.EqualTo(2));
        }

        [Test]
        public void Сумасбродство_короля_удваивает_очки_ночью()
        {
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            game.State.SetPhase(isNight: true);
            game.OnBoard(new TestCard(name: "Пещера", type: CardType.Place));

            RunSync(new KingEffect(isWisdom: false).ApplyEffect(game.Context));

            Assert.That(game.Current.Score, Is.EqualTo(2));
        }

        [Test]
        public void Король_всегда_даёт_карту_даже_без_мест_на_столе()
        {
            var game = GameFixture.Start(playerCount: 1, deck: Filler());

            RunSync(new KingEffect(isWisdom: true).ApplyEffect(game.Context));

            Assert.That(game.Current.HandCardCount, Is.EqualTo(1));
            Assert.That(game.Current.Score, Is.Zero);
        }

        // ================= День и Ночь =================

        [TestCase(true)]
        [TestCase(false)]
        public void Смена_времени_суток_задаёт_фазу_а_не_переключает(bool toNight)
        {
            // Задаёт, а не переключает: две карты «Ночь» подряд должны
            // оставить ночь, а не вернуть день.
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            var effect = new SwapGameTimeEffect(toNight);

            RunSync(effect.ApplyEffect(game.Context));
            RunSync(effect.ApplyEffect(game.Context));

            Assert.That(game.State.IsNight, Is.EqualTo(toNight));
        }

        // ================= Пассивная добыча =================

        [Test]
        public void Пассивная_добыча_срабатывает_только_как_постоянный_эффект()
        {
            var effect = new PassiveFixPointsFarmEffect(2);

            Assert.That(effect.Type, Is.EqualTo(CardEffectTimeType.PermanentAtTheEnd));
        }

        [Test]
        public void Дитя_ночи_приносит_очки_ночью_и_молчит_днём()
        {
            // Условие дня и ночи живёт в КАРТЕ, а не в эффекте: обе карты
            // делят один и тот же PassiveFixPointsFarmEffect(2).
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            var child = game.OnBoard(new NightChildCard());

            game.State.SetPhase(isNight: true);
            RunSync(game.Manager.ActivatePermanentCardEffect(child));
            Assert.That(game.Current.Score, Is.EqualTo(2));

            game.State.SetPhase(isNight: false);
            RunSync(game.Manager.ActivatePermanentCardEffect(child));
            Assert.That(game.Current.Score, Is.EqualTo(2), "Дитя ночи начислило днём");
        }

        [Test]
        public void Старый_мудрец_приносит_очки_днём_и_молчит_ночью()
        {
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            var master = game.OnBoard(new OldMasterCard());

            game.State.SetPhase(isNight: false);
            RunSync(game.Manager.ActivatePermanentCardEffect(master));
            Assert.That(game.Current.Score, Is.EqualTo(2));

            game.State.SetPhase(isNight: true);
            RunSync(game.Manager.ActivatePermanentCardEffect(master));
            Assert.That(game.Current.Score, Is.EqualTo(2), "Старый мудрец начислил ночью");
        }

        // ================= Купец =================

        [Test]
        public void Купец_берёт_две_карты_и_даёт_очки_за_каждого_купца_на_столе()
        {
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            game.OnBoard(new MerchantCard());
            game.OnBoard(new MerchantCard());

            RunSync(new MerchantEffect().ApplyEffect(game.Context));

            Assert.That(game.Current.HandCardCount, Is.EqualTo(2));
            Assert.That(game.Current.Score, Is.EqualTo(4));
        }

        [Test]
        public void Купец_ищет_себе_подобных_по_литералу_имени()
        {
            // MerchantEffect сравнивает строку "Купец". Компилятор о таком
            // сравнении не знает, и переименование карты сломало бы эффект
            // молча. Этот тест — единственное, что об этом скажет.
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            game.OnBoard(new MerchantCard());

            RunSync(new MerchantEffect().ApplyEffect(game.Context));

            Assert.That(game.Current.Score, Is.EqualTo(2),
                "Купец перестал находить себя по имени — вероятно, карту переименовали");
        }

        [Test]
        public void Купец_без_купцов_на_столе_очков_не_даёт()
        {
            var game = GameFixture.Start(playerCount: 1, deck: Filler());

            RunSync(new MerchantEffect().ApplyEffect(game.Context));

            Assert.That(game.Current.Score, Is.Zero);
            Assert.That(game.Current.HandCardCount, Is.EqualTo(2));
        }

        // ================= Кража карт =================

        [Test]
        public void Огр_забирает_выбранную_карту_со_стола_в_руку()
        {
            var game = GameFixture.Start(playerCount: 2, deck: Filler());
            var prey = game.OnBoard(new TestCard(name: "Фея", type: CardType.Woman) { Id = 11 },
                game.Players[1]);
            game.Router.PickCardId(prey.Id);

            var effect = new CardStealEffect(new List<CardType> { CardType.Woman });
            RunSync(effect.ApplyEffect(game.Context));

            Assert.That(game.Current.Hand, Does.Contain(prey));
            Assert.That(game.Board.GetCardsOnBoard(), Does.Not.Contain(prey));
        }

        [Test]
        public void Огр_может_украсть_чужую_ведьму()
        {
            // У CardStealEffect нет фильтра по владельцу. Ведьма — карта типа
            // «Женщина», и украсть её вместе со всеми её правилами можно.
            //
            // Вопрос числится открытым в docs/OPEN_QUESTIONS.md. Тест
            // закрепляет факт, чтобы решение принималось осознанно.
            var game = GameFixture.Start(playerCount: 2, deck: Filler());
            var witch = game.OnBoard(new WitchCard { Id = 11 }, game.Players[1]);
            game.Router.PickCardId(witch.Id);

            var effect = new CardStealEffect(new List<CardType> { CardType.Woman });
            RunSync(effect.ApplyEffect(game.Context));

            Assert.That(game.Current.Hand, Does.Contain(witch),
                "поведение изменилось — проверьте, не появился ли фильтр владельца");
        }

        [Test]
        public void Кража_не_спрашивает_когда_красть_нечего()
        {
            var game = GameFixture.Start(playerCount: 1, deck: Filler());

            var effect = new CardStealEffect(new List<CardType> { CardType.Woman });
            RunSync(effect.ApplyEffect(game.Context));

            Assert.That(game.Router.AskCount, Is.Zero);
            Assert.That(game.Current.HandCardCount, Is.Zero);
        }

        [Test]
        public void Кража_спрашивает_отдельно_про_каждый_тип()
        {
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            var woman = game.OnBoard(new TestCard(name: "Фея", type: CardType.Woman) { Id = 11 });
            var place = game.OnBoard(new TestCard(name: "Пещера", type: CardType.Place) { Id = 12 });
            game.Router.PickCardId(woman.Id).PickCardId(place.Id);

            var effect = new CardStealEffect(new List<CardType> { CardType.Woman, CardType.Place });
            RunSync(effect.ApplyEffect(game.Context));

            Assert.That(game.Router.AskCount, Is.EqualTo(2));
            Assert.That(game.Current.Hand, Does.Contain(woman).And.Contain(place));
        }

        // ================= Тёмный владыка и Дракон =================

        [Test]
        public void Тёмный_владыка_даёт_по_два_очка_за_злодея_и_сбрасывает_место()
        {
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            game.OnBoard(new TestCard(name: "Дракон", type: CardType.Monster));
            game.OnBoard(new TestCard(name: "Огр", type: CardType.Monster));
            var place = game.OnBoard(new TestCard(name: "Пещера", type: CardType.Place) { Id = 11 });
            game.Router.PickCardId(place.Id);

            RunSync(new DarkLordEffect().ApplyEffect(game.Context));

            Assert.That(game.Current.Score, Is.EqualTo(4));
            Assert.That(game.Board.GetCardsFromDiscardPile(), Does.Contain(place));
        }

        [Test]
        public void Тёмный_владыка_без_мест_на_столе_только_начисляет()
        {
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            game.OnBoard(new TestCard(name: "Дракон", type: CardType.Monster));

            RunSync(new DarkLordEffect().ApplyEffect(game.Context));

            Assert.That(game.Current.Score, Is.EqualTo(2));
            Assert.That(game.Router.AskCount, Is.Zero);
        }

        [Test]
        public void Дракон_сбрасывает_место_и_мужчину_по_два_очка_за_каждого()
        {
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            var place = game.OnBoard(new TestCard(name: "Пещера", type: CardType.Place) { Id = 11 });
            var man = game.OnBoard(new TestCard(name: "Герой", type: CardType.Man) { Id = 12 });
            game.Router.PickCardId(place.Id).PickCardId(man.Id);

            RunSync(new DragonEffect().ApplyEffect(game.Context));

            Assert.That(game.Current.Score, Is.EqualTo(4));
            Assert.That(game.Board.GetCardsFromDiscardPile(), Does.Contain(place).And.Contain(man));
        }

        [Test]
        public void Дракон_на_пустом_столе_ничего_не_спрашивает()
        {
            var game = GameFixture.Start(playerCount: 1, deck: Filler());

            RunSync(new DragonEffect().ApplyEffect(game.Context));

            Assert.That(game.Router.AskCount, Is.Zero);
            Assert.That(game.Current.Score, Is.Zero);
        }

        [Test]
        public void Дракон_работает_и_с_одним_видом_карт_на_столе()
        {
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            var man = game.OnBoard(new TestCard(name: "Герой", type: CardType.Man) { Id = 12 });
            game.Router.PickCardId(man.Id);

            RunSync(new DragonEffect().ApplyEffect(game.Context));

            Assert.That(game.Router.AskCount, Is.EqualTo(1));
            Assert.That(game.Current.Score, Is.EqualTo(2));
        }

        // ================= Выдача предметов =================

        [Test]
        public void Предмет_выдаётся_из_запаса_в_сумку()
        {
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            game.Router.PickItemType(ItemType.Sword);

            var effect = new ItemDistributor(new List<ItemType> { ItemType.Sword, ItemType.Coin });
            RunSync(effect.ApplyEffect(game.Context));

            Assert.That(game.Current.ItemAmount(ItemType.Sword), Is.EqualTo(1));
            Assert.That(game.Items.CountItemByType(ItemType.Sword), Is.EqualTo(3));
        }

        [Test]
        public void Кончившийся_тип_не_предлагается()
        {
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            game.DrainSupply(ItemType.Sword);
            game.Router.PickFirstItem();

            var effect = new ItemDistributor(new List<ItemType> { ItemType.Sword, ItemType.Coin });
            RunSync(effect.ApplyEffect(game.Context));

            Assert.That(game.Router.ItemRequests.Single().CandidateItemTypes,
                Is.EqualTo(new[] { ItemType.Coin }));
        }

        [Test]
        public void Пустой_запас_не_спрашивает_вовсе()
        {
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            game.DrainSupply(ItemType.Sword);

            var effect = new ItemDistributor(new List<ItemType> { ItemType.Sword });
            RunSync(effect.ApplyEffect(game.Context));

            Assert.That(game.Router.AskCount, Is.Zero);
            Assert.That(game.Notifier.Said("Нет доступных предметов"));
        }

        // ================= Волшебник =================

        [Test]
        public void Волшебник_вытягивает_карту_и_сразу_её_разыгрывает()
        {
            var game = GameFixture.Start(playerCount: 1,
                deck: new List<Card> { new TestCard(name: "Добыча", points: 3, countInDeck: 5) });

            RunSync(new WizardEffect().ApplyEffect(game.Context));

            Assert.That(game.Current.HandCardCount, Is.Zero, "карта осела в руке вместо стола");
            Assert.That(game.Board.CardsOnBoardCount(), Is.EqualTo(1));
            Assert.That(game.Current.Score, Is.EqualTo(3));
        }

        [Test]
        public void Волшебник_на_пустой_колоде_ничего_не_делает()
        {
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            game.DrainDeck();

            RunSync(new WizardEffect().ApplyEffect(game.Context));

            Assert.That(game.Board.CardsOnBoardCount(), Is.Zero);
            Assert.That(game.Current.HandCardCount, Is.Zero);
        }
    }
}
