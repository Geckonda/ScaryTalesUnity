using Assets.Libraries.ScaryTales;
using Assets.Libraries.ScaryTales.Abstractions;
using Assets.Scripts.Menus;
using Assets.Scripts.Network;
using DG.Tweening;
using ScaryTales;
using ScaryTales.Enums;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Assets.Scripts.UIEntities
{
    /// <summary>
    /// Разбор правила конца партии: показывает, что именно правило дало каждому
    /// игроку.
    ///
    /// <para><b>Очерёдность берётся даром.</b> Сервер шлёт по событию на игрока
    /// в порядке ходов, клиент применяет их насосом
    /// (<c>UnGameManager.PumpClientEvents</c>) по одному и только когда не
    /// играет ни одна блокирующая анимация. Значит, достаточно на каждое
    /// событие завести одну блокирующую задачу — игроки пойдут по очереди сами
    /// собой, а окно победителя (<c>GameEndedEvent</c> приходит последним)
    /// откроется только после того, как разбор доиграет. Ни планировщика, ни
    /// корутины-дирижёра для этого не нужно.</para>
    ///
    /// <para><b>Шаги нарочно разнесены по методам</b> со своими ручками в
    /// инспекторе: разбор — то место, которое будут украшать, и менять один
    /// шаг не должно значить переписывать всё.</para>
    /// </summary>
    public class EndGameScoringUI : MonoBehaviour
    {
        [Tooltip("Сколько держать баннер с правилом конца партии, секунд. Прочитать условие надо успеть один раз за партию.")]
        [SerializeField] private float _bannerSeconds = 4f;

        [Tooltip("Сколько предметы игрока лежат на виду, прежде чем улететь в счёт, секунд.")]
        [SerializeField] private float _readSeconds = 1.5f;

        [Tooltip("То же для игрока, которому правило ничего не дало. Короче: смотреть там не на что, но пропускать его нельзя — иначе непонятно, разобрали его или забыли.")]
        [SerializeField] private float _emptyReadSeconds = 0.7f;

        [Tooltip("Сколько предмет летит в табличку счёта, секунд.")]
        [SerializeField] private float _itemFlightDuration = 0.6f;

        [Tooltip("Пауза между вылетами соседних предметов, секунд. Ноль — все летят разом.")]
        [SerializeField] private float _itemStagger = 0.12f;

        [Tooltip("Сколько живёт всплывающее «+N ПО», секунд.")]
        [SerializeField] private float _popupSeconds = 1.2f;

        [Tooltip("На сколько «+N ПО» уезжает вверх за свою жизнь, пикселей канваса.")]
        [SerializeField] private float _popupRise = 70f;

        [Tooltip("Насколько ряд предметов отодвинут от руки игрока к центру стола, пикселей канваса. Иначе предметы легли бы поверх веера.")]
        [SerializeField] private float _itemsOffset = 190f;

        [Tooltip("Расстояние между предметами в ряду, пикселей канваса.")]
        [SerializeField] private float _itemSpacing = 95f;

        [Tooltip("Размер предмета в разборе. Префаб предмета рассчитан на модальное окно и на столе великоват.")]
        [SerializeField] private float _itemScale = 0.8f;

        private ClientGameView _view;
        private SeatLayout _seatLayout;

        // Место каждого игрока — якорь для всего показа: у руки выкладываются
        // предметы, в табличку счёта они улетают.
        private readonly Dictionary<Player, Seat> _seats = new();

        // Всё созданное за разбор, чтобы прибрать за собой при обрыве. Партию
        // может прервать ушедший игрок (GameAbortedEvent идёт мимо очереди и
        // чистит её), и тогда предметы так и остались бы висеть в канвасе.
        private readonly List<GameObject> _spawned = new();

        public void Initialize(ClientGameView view, SeatLayout seatLayout)
        {
            _view = view;
            _seatLayout = seatLayout;

            view.OnFinalScoringStarted += HandleFinalScoringStarted;
            view.OnFinalScoring += HandleFinalScoring;
            view.OnGameAborted += HandleGameAborted;

            _seats.Clear();
            var localSeat = _seatLayout?.LocalSeat;
            if (localSeat != null) _seats[view.LocalPlayer] = localSeat;
            for (int i = 0; i < view.Opponents.Count; i++)
            {
                var seat = _seatLayout?.GetOpponentSeat(i);
                if (seat != null) _seats[view.Opponents[i]] = seat;
            }
        }

        private void OnDestroy() => CleanUp();

        private void HandleGameAborted(string reason, Player who) => CleanUp();

        /// <summary>
        /// Убирает всё, что разбор успел создать. Зовётся и при обрыве партии,
        /// и при выгрузке сцены: предметы живут в корне канваса, и без уборки
        /// они пережили бы конец разбора.
        /// </summary>
        private void CleanUp()
        {
            foreach (var go in _spawned)
                if (go != null) Destroy(go);
            _spawned.Clear();
        }

        // ---- Баннер ----

        private async void HandleFinalScoringStarted(Rule rule)
        {
            var task = ShowRuleBanner(rule);
            AnimationManager.Instance.Register(task);
            await task;
        }

        /// <summary>
        /// Показывает правило, по которому сейчас будут раздавать очки.
        ///
        /// <para>Без него разбор выглядит беспричинным: финальное правило
        /// сегодня не показано игроку нигде — ни до партии, ни во время неё.
        /// Панель правил берём готовую, но с другой подписью: дежурная говорит
        /// про «свой ход», а тут ходов уже нет.</para>
        /// </summary>
        private async Task ShowRuleBanner(Rule rule)
        {
            var container = RuleContainer.Instance;
            if (container == null || rule == null) return;

            container.Show(rule.Effects, interactive: false, isAvailable: null,
                           hint: $"Правило конца игры: {rule.Name}");

            await Delay(_bannerSeconds);

            // Панель могли закрыть крестиком, пока мы ждали, — тогда закрывать
            // нечего, а её Hide() ещё и разбудил бы OnClosed на пустом месте.
            if (container != null && container.IsShowing) container.Hide();
        }

        // ---- Разбор одного игрока ----

        private async void HandleFinalScoring(Player player, IRuleEffect effect,
                                              int points, IReadOnlyList<ItemType> counted)
        {
            var task = ScorePlayer(player, points, counted);
            // Блокирующая: следующее событие — начисление очков этому же
            // игроку, и оно должно примениться, когда предметы долетели.
            AnimationManager.Instance.Register(task);
            await task;
        }

        private async Task ScorePlayer(Player player, int points, IReadOnlyList<ItemType> counted)
        {
            if (player == null || !_seats.TryGetValue(player, out var seat) || seat == null) return;

            var items = player.ShowItemsFromItemBag();
            var views = SpawnItemRow(seat, items, counted);

            // Держим на виду: игроки должны успеть увидеть, из чего сложился
            // результат. Пустому игроку — меньше: смотреть не на что.
            await Delay(points > 0 ? _readSeconds : _emptyReadSeconds);

            // Паузу разбор переживает, а сцену — нет: выход в меню посреди
            // партии перезагружает её вместе с местами. Уничтоженный объект
            // Unity равен null по своей перегрузке ==, но обращение к его полям
            // бросает MissingReferenceException, а бросать его отсюда некуда:
            // задачу ждут из async void.
            if (seat == null) return;

            await FlyItemsToScore(seat, views);
            if (seat == null) return;

            // Всплытие ждём, а не бросаем на самотёк: иначе «+12 ПО» одного
            // игрока висело бы над столом, пока разбирают следующего, а в конце
            // ещё и осталось бы висеть под окном победителя.
            await ShowPointsPopup(seat, points);

            foreach (var view in views)
                if (view.View != null) Destroy(view.View.gameObject);
            _spawned.RemoveAll(go => go == null);
        }

        /// <summary>Предмет разбора: его представление и сыграл ли он.</summary>
        private readonly struct ScoredItemView
        {
            public readonly ItemView View;
            public readonly bool Counted;
            public ScoredItemView(ItemView view, bool counted) { View = view; Counted = counted; }
        }

        /// <summary>
        /// Выкладывает ВСЕ предметы игрока в ряд у его места и подсвечивает те,
        /// что сыграли.
        ///
        /// <para>Все, а не только засчитанные: иначе игрок так и не узнает, чего
        /// ему не хватило до набора, — а мешки за партию не видит вообще никто.</para>
        ///
        /// <para>Ряд живёт в корне канваса, а не в панели места: раскладки сами
        /// пишут своим детям позицию, поворот и масштаб, и ряд бы развалился на
        /// первой же пересборке (тот же урок, что у <see cref="CardFlight"/>).
        /// Направление «от руки к центру стола» считается по месту, поэтому
        /// одинаково годится для любого стула и не требует настройки в сцене.</para>
        /// </summary>
        private List<ScoredItemView> SpawnItemRow(Seat seat, List<Item> items, IReadOnlyList<ItemType> counted)
        {
            var result = new List<ScoredItemView>();
            if (items == null || items.Count == 0) return result;

            var hand = seat.HandPanel;
            var root = hand != null ? hand.root : null;
            if (hand == null || root == null) return result;

            // Сколько предметов каждого типа сыграло. Считаем по типам, а не по
            // конкретным предметам: монеты взаимозаменяемы, а идентификаторы
            // предметов у сервера и клиента расходятся — см. FinalScoringEvent.
            var remaining = new Dictionary<ItemType, int>();
            if (counted != null)
                foreach (var type in counted)
                    remaining[type] = remaining.TryGetValue(type, out var n) ? n + 1 : 1;

            var boardCentre = UnGameManager.Instance != null && UnGameManager.Instance.GameBoardPanel != null
                ? UnGameManager.Instance.GameBoardPanel.position
                : root.position;

            var toCentre = (boardCentre - hand.position);
            toCentre = toCentre.sqrMagnitude > 0.0001f ? toCentre.normalized : Vector3.up;
            var rowCentre = hand.position + toCentre * _itemsOffset;

            // Ряд всегда горизонтальный, даже у верхних мест: их панели
            // повёрнуты на 180 градусов, но предметы читает зритель, а не
            // сидящий за столом игрок.
            float span = _itemSpacing * (items.Count - 1);
            for (int i = 0; i < items.Count; i++)
            {
                var view = ItemViewService.Instance.CreateItemView(items[i], root);
                if (view == null) continue;

                var t = view.transform;
                t.position = rowCentre + Vector3.right * (-span / 2f + i * _itemSpacing);
                t.localRotation = Quaternion.identity;
                t.localScale = Vector3.one * _itemScale;

                bool isCounted = remaining.TryGetValue(items[i].Type, out var left) && left > 0;
                if (isCounted) remaining[items[i].Type] = left - 1;
                view.SetHighlight(isCounted);

                _spawned.Add(view.gameObject);
                result.Add(new ScoredItemView(view, isCounted));
            }

            return result;
        }

        /// <summary>
        /// Засчитанные предметы улетают в табличку счёта, остальные гаснут на
        /// месте: очки дали не они.
        /// </summary>
        private async Task FlyItemsToScore(Seat seat, List<ScoredItemView> views)
        {
            var score = seat.ScoreText != null ? seat.ScoreText.transform : null;
            var flights = new List<Task>();

            for (int i = 0; i < views.Count; i++)
            {
                var view = views[i].View;
                if (view == null) continue;

                if (!views[i].Counted)
                {
                    FadeOut(view, _itemFlightDuration);
                    continue;
                }

                if (score == null) continue;
                flights.Add(FlyOneItem(view.transform, score, i * _itemStagger));
            }

            if (flights.Count == 0)
            {
                // Лететь нечему — но пауза нужна всё равно, иначе гаснущие
                // предметы исчезнут вместе со следующим шагом.
                await Delay(_itemFlightDuration);
                return;
            }

            await Task.WhenAll(flights);
        }

        private async Task FlyOneItem(Transform item, Transform score, float delaySeconds)
        {
            // Разлёт с задержкой: предметы уходят каскадом, а не разом. Пауза
            // переживает перезагрузку сцены, а предмет с табличкой — нет,
            // поэтому проверка стоит ПОСЛЕ ожидания.
            if (delaySeconds > 0f) await Delay(delaySeconds);
            if (item == null || score == null) return;

            await CardFlight.Fly(item, score, _itemFlightDuration, 0f, null);
        }

        private void FadeOut(ItemView view, float duration)
        {
            var image = view.GetComponent<Image>();
            if (image != null) image.DOFade(0f, duration).SetTarget(view.transform);
        }

        /// <summary>
        /// «+N ПО» над табличкой счёта.
        ///
        /// <para>Текст — клон самой таблички: так он наследует шрифт, размер и
        /// цвет места, и для этого не нужен ни новый префаб, ни привязка в
        /// сцене. Ноль не показываем: пустая строка сказала бы то же самое,
        /// что и отсутствие всплытия, но заняла бы экран.</para>
        /// </summary>
        private async Task ShowPointsPopup(Seat seat, int points)
        {
            if (points <= 0 || seat.ScoreText == null) return;

            var root = seat.ScoreText.transform.root;
            var popup = Instantiate(seat.ScoreText.gameObject, root);
            _spawned.Add(popup);
            // Клон наследует и выключенность: счёт оппонента прячется на всю
            // партию (TextUIManager), и хотя к разбору его уже открыли, копия
            // всплытия не должна зависеть от того, кто и когда это сделал.
            popup.SetActive(true);

            var text = popup.GetComponent<TMP_Text>();
            if (text == null) { Destroy(popup); return; }

            text.text = $"+{points} ПО";
            var t = popup.transform;
            t.position = seat.ScoreText.transform.position;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;

            t.DOMoveY(t.position.y + _popupRise, _popupSeconds).SetEase(Ease.OutQuad).SetTarget(t);
            text.DOFade(0f, _popupSeconds).SetEase(Ease.InQuad).SetTarget(t);

            await Delay(_popupSeconds);
            if (popup != null) Destroy(popup);
        }

        // Покадровое ожидание переехало в AnimationManager: им пользуется уже
        // не только разбор, а темпом показа заведует он.
        private static Task Delay(float seconds) => AnimationManager.Delay(seconds);
    }
}
