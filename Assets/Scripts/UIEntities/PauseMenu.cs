using Assets.Scripts.Network;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Assets.Scripts.UIEntities
{
    /// <summary>
    /// Меню по Esc: продолжить, выйти из комнаты, выйти из игры.
    ///
    /// <para><b>Почему нет «Сдаться».</b> Сейчас она не смогла бы значить
    /// ничего отличного от выхода: по политике Фазы 6.1 уход игрока посреди
    /// партии завершает комнату для всех. «Сдаться и пусть играют дальше»
    /// требует уметь выкидывать игрока из порядка ходов на ходу — это правки
    /// в ядре, отдельная задача. Кнопка с другим ярлыком и тем же действием
    /// вводила бы в заблуждение.</para>
    /// </summary>
    public class PauseMenu : MonoBehaviour
    {
        [Tooltip("Корень меню. Растяните на весь экран и повесьте Image с Raycast Target, чтобы меню перехватывало клики по столу. Объект должен оставаться ВКЛЮЧЁННЫМ — видимостью управляет CanvasGroup.")]
        [SerializeField] private GameObject _panel;

        [SerializeField] private Button _resumeButton;
        [SerializeField] private Button _leaveRoomButton;
        [SerializeField] private Button _quitButton;

        [Tooltip("Необязательно. Предупреждение под кнопкой выхода из комнаты.")]
        [SerializeField] private TMP_Text _leaveWarningText;

        [Tooltip("Клавиша открытия меню.")]
        [SerializeField] private KeyCode _toggleKey = KeyCode.Escape;

        // Панель прячется прозрачностью, а не выключением объекта.
        //
        // Выключение здесь — ловушка: если компонент висит на самой панели
        // (а это первое, что приходит в голову), то, погасив её, он гасит
        // собственный объект — и Update() больше не вызывается. Слушать Esc
        // становится некому, и меню уже не открыть никогда. CanvasGroup
        // снимает зависимость от того, куда именно повешен компонент.
        private CanvasGroup _group;
        private bool _visible;

        // Меню открывается только в партии. До неё Esc не должен ничего
        // делать: в лобби выходить неоткуда, а «выйти из игры» есть в самом
        // меню комнат. Признак берём из ClientGameView — только он знает,
        // что игра действительно началась.
        private bool _inGame;
        private bool _subscribed;

        public bool IsVisible => _visible;

        private void Awake()
        {
            if (_resumeButton != null) _resumeButton.onClick.AddListener(Hide);
            if (_leaveRoomButton != null) _leaveRoomButton.onClick.AddListener(LeaveRoom);
            if (_quitButton != null) _quitButton.onClick.AddListener(QuitGame);

            if (_leaveWarningText != null)
                _leaveWarningText.text = "Если партия идёт, она завершится для всех.";

            if (_panel != null)
            {
                // Объект остаётся активным — иначе снова останемся без Update.
                _panel.SetActive(true);
                _group = _panel.GetComponent<CanvasGroup>();
                if (_group == null) _group = _panel.AddComponent<CanvasGroup>();
            }
            else
            {
                Debug.LogWarning("[PauseMenu] Панель не привязана — меню по Esc работать не будет.");
            }

            Hide();
        }

        private void Update()
        {
            // UnGameManager создаёт ClientGameView в своём Awake, а порядок
            // Awake между объектами не определён — поэтому подписываемся не
            // один раз, а до первой удачи. Тот же приём, что в LobbyManager.
            if (!_subscribed) TrySubscribe();

            if (!_inGame)
            {
                if (_visible) Hide();
                return;
            }

            if (!Input.GetKeyDown(_toggleKey)) return;

            // Esc сначала означает «назад», и только потом «меню». Если стол
            // ждёт от игрока выбора, от которого можно отказаться, — Esc
            // отказывается от него. Второе нажатие откроет меню как обычно.
            var manager = UnGameManager.Instance;
            if (!_visible && manager != null && manager.TryCancelPendingDecision())
                return;

            Toggle();
        }

        private void TrySubscribe()
        {
            var view = UnGameManager.Instance != null ? UnGameManager.Instance.ClientView : null;
            if (view == null) return;
            view.OnGameStarted += HandleGameStarted;
            _subscribed = true;
        }

        private void HandleGameStarted() => _inGame = true;

        private void OnDestroy()
        {
            if (!_subscribed) return;
            var view = UnGameManager.Instance != null ? UnGameManager.Instance.ClientView : null;
            if (view != null) view.OnGameStarted -= HandleGameStarted;
        }

        public void Toggle()
        {
            if (_visible) Hide();
            else Show();
        }

        public void Show()
        {
            if (_group == null || !_inGame) return;

            // Кнопка активна всегда, пока меню открывается (а открывается оно
            // только в партии). Прежнее условие NetworkClient.isConnected
            // выключало её ровно тогда, когда она нужнее всего: после обрыва
            // связи выход в меню — единственный оставшийся путь, а никакого
            // соединения для него не требуется, это перезагрузка сцены.
            if (_leaveRoomButton != null)
                _leaveRoomButton.interactable = true;

            _visible = true;
            _group.alpha = 1f;
            _group.interactable = true;
            // Именно это перехватывает клики по столу, пока меню открыто.
            _group.blocksRaycasts = true;
        }

        public void Hide()
        {
            _visible = false;
            if (_group == null) return;
            _group.alpha = 0f;
            _group.interactable = false;
            _group.blocksRaycasts = false;
        }

        /// <summary>
        /// Отключиться и вернуться в меню.
        ///
        /// <para>Намеренно не шлёт <c>LeaveRoomIntent</c>, хотя тот есть на
        /// проводе и сервер его обрабатывает: отключение и так проходит через
        /// ту же серверную ветку (<c>ReleaseConnection</c> — освободить место,
        /// завершить партию, уничтожить опустевшую комнату), а отправка
        /// сообщения прямо перед разрывом связи ещё и рискует не успеть
        /// уйти.</para>
        ///
        /// <para><c>LeaveRoomIntent</c> пригодится, когда появится «выйти в
        /// список комнат, не разрывая соединение» — вот там он и нужен.</para>
        /// </summary>
        private void LeaveRoom()
        {
            Hide();
            GameConnectionManager.ReturnToMenu();
        }

        private void QuitGame()
        {
#if UNITY_EDITOR
            // В редакторе Application.Quit() ничего не делает, и кнопка
            // выглядит сломанной.
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
