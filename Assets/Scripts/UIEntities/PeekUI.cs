using Assets.Scripts.Network;
using DG.Tweening;
using ScaryTales;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Assets.Scripts.UIEntities
{
    /// <summary>
    /// Показ «игрок смотрит верх колоды»: над колодой приподнимаются рубашки,
    /// потом одна из них растворяется (карта ушла из игры), а вторая исчезает
    /// ровно тогда, когда настоящая карта вылетает из колоды в руку.
    ///
    /// <para><b>Зачем.</b> Смотрящий видит окно выбора и понимает, что
    /// происходит. Остальные за столом без этого видели бы только паузу и
    /// подсвеченный ник — а потом карта неизвестно почему улетела бы кому-то в
    /// руку, а колода похудела бы на две.</para>
    ///
    /// <para><b>Рубашки — не карты.</b> Ни одна из них не связана с настоящей
    /// картой: сервер присылает только их число (см. <c>CardsPeekedEvent</c>),
    /// и показывать тут нечего, кроме факта. Поэтому и убирать их можно любую:
    /// они неразличимы.</para>
    ///
    /// <para><b>В сброс удалённая карта не летит, и это не упущение.</b> Из
    /// сброса карты возвращаются в игру (<c>PutCardInPlayerHandFromDiscardPile</c>),
    /// а эта ушла насовсем. Полёт в сброс обещал бы игрокам возврат, которого
    /// не будет, — поэтому она растворяется в воздухе.</para>
    ///
    /// <para>Работ в сцене не требует: рубашки строятся из того же префаба
    /// карты и живут в корне канваса — по образцу <see cref="EndGameScoringUI"/>.</para>
    /// </summary>
    public class PeekUI : MonoBehaviour
    {
        [Tooltip("Сколько рубашки выезжают из колоды, секунд.")]
        [SerializeField] private float _riseSeconds = 0.35f;

        [Tooltip("На сколько рубашки отъезжают от колоды к центру стола, пикселей канваса. Направление считается по сцене, поэтому не зависит от того, где лежит колода.")]
        [SerializeField] private float _riseHeight = 130f;

        [Tooltip("Расстояние между соседними рубашками, пикселей канваса.")]
        [SerializeField] private float _spacing = 150f;

        [Tooltip("Наклон крайних рубашек, градусов. Ноль — ровный ряд.")]
        [SerializeField] private float _tilt = 6f;

        [Tooltip("Сколько удалённая карта растворяется, секунд. Это блокирующая анимация: следующее событие (карта в руку) ждёт её конца.")]
        [SerializeField] private float _dissolveSeconds = 0.75f;

        [Tooltip("На сколько удалённая карта успевает подняться, пока растворяется, пикселей канваса. Небольшой намеренно: она уменьшается и гаснет на месте, а не улетает за край экрана.")]
        [SerializeField] private float _dissolveRise = 90f;

        [Tooltip("Во сколько раз она при этом уменьшается.")]
        [SerializeField] private float _dissolveScale = 0.55f;

        private ClientGameView _view;

        // Рубашки на виду. Порядок не значит ничего — они неразличимы, и
        // разбирают их с конца просто потому, что так дешевле.
        private readonly List<CardView> _ghosts = new();

        public void Initialize(ClientGameView view)
        {
            _view = view;

            view.OnCardsPeeked += HandleCardsPeeked;
            view.OnCardRemovedFromGame += HandleCardRemovedFromGame;
            view.OnCardAddedToHand += HandleCardAddedToHand;
            // Страховка на случай, когда просмотр не довели до конца: игрок
            // вышел посреди выбора, и ни удаления, ни взятия не будет. Рубашки
            // не могут пережить ход, в котором появились.
            view.OnTurnAdvanced += HandleTurnAdvanced;
            view.OnGameAborted += HandleGameAborted;
            // Партия может кончиться прямо на этом ходу — например, просмотром
            // и осушили колоду. Рубашки не должны остаться висеть под окном
            // победителя.
            view.OnGameEnded += HandleGameEnded;
        }

        private void OnDestroy() => ClearGhosts();

        private void HandleTurnAdvanced(int currentPlayerId) => ClearGhosts();

        private void HandleGameAborted(string reason, Player who) => ClearGhosts();

        private void HandleGameEnded(int winnerId) => ClearGhosts();

        // ---- Рубашки выезжают ----

        private async void HandleCardsPeeked(Player by, int count)
        {
            // Прошлый показ мог не разобраться — начинаем с чистого стола.
            ClearGhosts();

            var deck = UnGameManager.Instance != null ? UnGameManager.Instance.Deck : null;
            if (deck == null || count <= 0) return;

            var root = deck.root;
            if (root == deck) return; // колода вне канваса — показывать негде

            var task = RaiseGhosts(deck, root, count);
            // Блокирующая: окно выбора приходит следующим событием, и открыться
            // оно должно после того, как рубашки встали на место.
            AnimationManager.Instance.Register(task);
            await task;
        }

        private async Task RaiseGhosts(Transform deck, Transform root, int count)
        {
            float span = _spacing * (count - 1);

            // Выезжают К ЦЕНТРУ СТОЛА, а не просто вверх: где лежит колода,
            // знает сцена, и у колоды под верхним краем экрана «вверх» увело бы
            // рубашки за него. Тот же приём, что у ряда предметов в разборе
            // конца партии.
            var centre = UnGameManager.Instance != null && UnGameManager.Instance.GameBoardPanel != null
                ? UnGameManager.Instance.GameBoardPanel.position
                : root.position;
            var toCentre = centre - deck.position;
            toCentre = toCentre.sqrMagnitude > 0.0001f ? toCentre.normalized : Vector3.up;

            for (int i = 0; i < count; i++)
            {
                var ghost = CreateGhost(deck, root);
                if (ghost == null) continue;

                _ghosts.Add(ghost);

                var t = ghost.transform;
                // Ряд всегда горизонтальный: рубашки читает зритель, а не
                // сидящий за столом игрок.
                var target = deck.position
                             + toCentre * _riseHeight
                             + Vector3.right * (-span / 2f + i * _spacing);
                float angle = count > 1 ? Mathf.Lerp(_tilt, -_tilt, i / (float)(count - 1)) : 0f;

                t.DOMove(target, _riseSeconds).SetEase(Ease.OutQuad).SetTarget(t);
                t.DORotate(new Vector3(0f, 0f, angle), _riseSeconds).SetTarget(t);
            }

            await AnimationManager.Delay(_riseSeconds);
        }

        /// <summary>
        /// Одна рубашка над колодой.
        ///
        /// <para>Какая именно карта под ней, не важно и намеренно неизвестно:
        /// сервер прислал только число. Представление строится из первой
        /// попавшейся карты каталога и тут же переворачивается рубашкой —
        /// лицо не показывается никогда, поэтому подмена ничем не видна.</para>
        ///
        /// <para>Создаётся <b>в колоде</b>, чтобы унаследовать её размер, и
        /// сразу переезжает в корень канваса, сохраняя мировую позу: летающее
        /// поверх стола не должно жить в чужой раскладке (тот же приём, что в
        /// <see cref="CardFlight"/>).</para>
        /// </summary>
        private CardView CreateGhost(Transform deck, Transform root)
        {
            var sample = _view?.AllCards.Values.FirstOrDefault();
            if (sample == null) return null;

            var factory = CardViewService.Instance.CardViewFactory;
            var ghost = factory.CreateCardView(sample, deck);
            if (ghost == null) return null;

            // Перетаскивать и нажимать тут нечего: рубашка — картинка, а не
            // карта. Заодно она не должна перехватывать клики по столу.
            factory.DisableDrag(ghost);
            foreach (var graphic in ghost.GetComponentsInChildren<Graphic>(true))
                graphic.raycastTarget = false;

            ghost.FaceDown();
            ghost.transform.SetParent(root, true);
            return ghost;
        }

        // ---- Одна растворяется ----

        private async void HandleCardRemovedFromGame(Card card)
        {
            if (_ghosts.Count == 0) return;

            var ghost = TakeGhost();
            if (ghost == null) return;

            var task = Dissolve(ghost);
            // Блокирующая: следом придёт взятие второй карты, и оно не должно
            // начаться, пока эта ещё тает.
            AnimationManager.Instance.Register(task);
            await task;
        }

        private async Task Dissolve(CardView ghost)
        {
            var t = ghost.transform;
            t.DOKill();
            t.DOMove(t.position + Vector3.up * _dissolveRise, _dissolveSeconds).SetEase(Ease.InQuad).SetTarget(t);
            t.DOScale(t.localScale * _dissolveScale, _dissolveSeconds).SetEase(Ease.InQuad).SetTarget(t);

            foreach (var graphic in ghost.GetComponentsInChildren<Graphic>(true))
                graphic.DOFade(0f, _dissolveSeconds).SetTarget(t);

            await AnimationManager.Delay(_dissolveSeconds);

            // Сцену могли перезагрузить, пока карта таяла.
            if (ghost != null) Destroy(ghost.gameObject);
        }

        // ---- Вторая уходит в руку ----

        /// <summary>
        /// Настоящая карта полетела из колоды в руку — оставшейся рубашке
        /// пора исчезнуть.
        ///
        /// <para>Без анимации и немедленно: настоящая карта стартует от той же
        /// колоды и в тот же кадр, взгляд идёт за ней. Дай мы рубашке улететь
        /// самой, в руку прилетели бы две карты подряд — одна из них поддельная.</para>
        ///
        /// <para>Обычное взятие сюда тоже заходит, но там рубашек нет и делать
        /// нечего: они живут только между просмотром и его последствиями.</para>
        /// </summary>
        private void HandleCardAddedToHand(Card card, Player player)
        {
            var ghost = TakeGhost();
            if (ghost != null) Destroy(ghost.gameObject);
        }

        // ---- Уборка ----

        private CardView TakeGhost()
        {
            while (_ghosts.Count > 0)
            {
                int last = _ghosts.Count - 1;
                var ghost = _ghosts[last];
                _ghosts.RemoveAt(last);
                if (ghost != null) return ghost;
            }
            return null;
        }

        private void ClearGhosts()
        {
            foreach (var ghost in _ghosts)
            {
                if (ghost == null) continue;
                ghost.transform.DOKill();
                Destroy(ghost.gameObject);
            }
            _ghosts.Clear();
        }
    }
}
