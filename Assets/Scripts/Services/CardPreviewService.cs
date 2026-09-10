using ScaryTales;
using UnityEngine;
using UnityEngine.UI;

namespace Assets.Scripts.Services
{
    /// <summary>
    /// Крупное превью карты под указателем. Единственный владелец: на экране
    /// не может оказаться больше одного превью, сколько бы карт ни считали
    /// себя наведёнными.
    ///
    /// <para><b>Зачем сервис, а не поле на карте.</b> Раньше каждая карта
    /// хранила своё превью в <c>HoverCardScaler._createdCardView</c>, и это
    /// давало фантомы тремя разными путями сразу:</para>
    ///
    /// <para>1. <b>Превью порождало превью.</b> Клон собирался обычной
    /// фабрикой, то есть из того же префаба и со всеми компонентами — включая
    /// сам <c>HoverCardScaler</c>. Курсор, переехавший с карты на её превью,
    /// создавал второе; уход с исходной карты убивал только первое.</para>
    ///
    /// <para>2. <b>Владельцев было столько же, сколько карт.</b> Два пальца на
    /// двух картах — два превью, и с точки зрения того кода совершенно
    /// законно.</para>
    ///
    /// <para>3. <b>Повторный вход терял предыдущее.</b> <c>OnPointerEnter</c>
    /// просто перезаписывал ссылку. А если карту уничтожили или выключили,
    /// пока она под указателем, <c>OnPointerExit</c> не приходил вовсе — и
    /// клон оставался на экране навсегда.</para>
    ///
    /// <para>Здесь же превью ровно одно, оно инертно к указателю, и уничтожить
    /// его можно откуда угодно, ничего не зная о том, кто его показал.</para>
    /// </summary>
    public class CardPreviewService
    {
        private static CardPreviewService _instance;
        public static CardPreviewService Instance => _instance ??= new CardPreviewService();

        /// <summary>
        /// Сбрасывает кэш вместе с остальными сервисами (см. <c>CardViewService.Reset</c>):
        /// это обычный C#-статик, переживающий перезагрузку сцены.
        /// </summary>
        public static void Reset() => _instance = null;

        /// <summary>Единственность — весь смысл этого класса, так что снаружи его не создать.</summary>
        private CardPreviewService() { }

        /// <summary>
        /// Во сколько раз превью крупнее карты на столе.
        ///
        /// <para>Живёт здесь, а не в компоненте на карте: размер превью —
        /// свойство самого превью, и решаться должен в одном месте. При 2.3
        /// карта 150×250 даёт 345×575 — на 1920×1080 это 53% высоты экрана.
        /// Контейнер в сцене 400×600, так что выше ~2.4 поднимать нельзя без
        /// правки сцены.</para>
        /// </summary>
        private const float Scale = 2.3f;

        private const string ContainerName = "CardScaleContainer";

        private CardView _view;
        private Card _shownFor;
        private Transform _container;

        /// <summary>Показать превью этой карты. Прежнее уничтожается.</summary>
        public void Show(Card card)
        {
            if (card == null) return;
            if (_shownFor == card && _view != null) return;

            Hide();

            var container = Container();
            if (container == null) return;

            var view = CardViewService.Instance.CreateSingleCardViewClone(card, container);
            if (view == null) return;

            MakeInert(view);

            view.transform.localScale = Vector3.one * Scale;
            view.FaceUp();

            _view = view;
            _shownFor = card;
        }

        /// <summary>
        /// Убрать превью, если оно показано именно для этой карты.
        ///
        /// <para>Проверка по карте, а не безусловное скрытие: на тач-вводе
        /// пальцев несколько, и уход с одной карты не должен гасить превью,
        /// которое показала другая.</para>
        /// </summary>
        public void Hide(Card card)
        {
            if (card != null && _shownFor != card) return;
            Hide();
        }

        /// <summary>Убрать превью, чьим бы оно ни было. Идемпотентна.</summary>
        public void Hide()
        {
            if (_view != null) Object.Destroy(_view.gameObject);
            _view = null;
            _shownFor = null;
        }

        /// <summary>
        /// Снимает с превью всё, чем оно могло бы ответить на указатель.
        ///
        /// <para>Это и есть лекарство от фантома, порождающего фантом: без
        /// <c>HoverCardScaler</c> превью не создаст своё превью, а без
        /// raycast-мишеней указатель его попросту не замечает — то есть уход
        /// с исходной карты не превратится в наведение на её же превью.</para>
        /// </summary>
        private static void MakeInert(CardView view)
        {
            foreach (var behaviour in view.GetComponents<MonoBehaviour>())
            {
                // Graphic — это отрисовка (Image, TMP); выключив её, мы бы
                // погасили саму карту.
                if (behaviour is CardView || behaviour is Graphic) continue;
                Object.Destroy(behaviour);
            }

            foreach (var graphic in view.GetComponentsInChildren<Graphic>(true))
                graphic.raycastTarget = false;
        }

        /// <summary>
        /// Контейнер превью из сцены. Ищется заново, если прежний уничтожен
        /// перезагрузкой сцены; отсутствует — превью просто не будет (так
        /// живёт лаборатория карты, где контейнера нет).
        /// </summary>
        private Transform Container()
        {
            if (_container != null) return _container;

            var go = GameObject.Find(ContainerName);
            _container = go != null ? go.transform : null;
            return _container;
        }
    }
}
