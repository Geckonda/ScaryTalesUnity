using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Знает, играет ли сейчас хоть одна анимация.
///
/// <para>Нужен затем, чтобы очередь событий клиента
/// (<see cref="Assets.Scripts.Network.ClientGameView"/>) применяла следующее
/// событие только после того, как отыграло предыдущее. Без этого сервер шлёт
/// события на полной скорости, а карты раздаются поверх ещё летящей карты
/// дня/ночи.</para>
/// </summary>
public class AnimationManager : MonoBehaviour
{
    // Анимации, которых очередь событий обязана дождаться: карта дня/ночи,
    // уход в сброс, выкладывание на стол. Пока такая играет, следующее
    // событие не применяется.
    private readonly List<Task> _blocking = new();

    // Фоновые: их видно, но никто их не ждёт. Прежде всего прилёт карт в
    // руку — пять карт при раздаче ничем друг другу не мешают и должны
    // лететь одновременно, как и до появления очереди.
    private readonly List<Task> _background = new();

    public static AnimationManager Instance { get; private set; }

    // До этого момента очередь не выпускает следующее событие. Нужно, чтобы
    // разредить череду однотипных анимаций, которые сами по себе никого не
    // ждут — см. staggerSeconds в Register.
    private float _holdUntil;

    /// <summary>Можно ли очереди применять следующее событие прямо сейчас.</summary>
    public bool IsBusy => _blocking.Count > 0 || Time.unscaledTime < _holdUntil;

    /// <summary>Сколько анимаций в полёте всего. Для диагностики.</summary>
    public int ActiveCount => _blocking.Count + _background.Count;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>
    /// Берёт анимацию под учёт. Задача должна быть уже запущена — здесь
    /// только слежение за тем, когда она закончится.
    /// </summary>
    /// <param name="blocksEventQueue">
    /// Ждать ли её перед применением следующего события. По умолчанию да:
    /// пропустить нужное ожидание хуже, чем лишний раз подождать.
    /// </param>
    /// <param name="staggerSeconds">
    /// Не выпускать следующее событие раньше, чем через столько секунд —
    /// даже если эта анимация никого не блокирует.
    ///
    /// <para>Нужно для раздачи. Пока карта дня/ночи летит в слот, сервер
    /// продолжает раздавать, и события копятся в очереди; после разблокировки
    /// насос выпускал их по одному за кадр — пять карт за восемьдесят
    /// миллисекунд, «как из пушки». Разрежение возвращает каскад, но задаёт
    /// его клиент: темп показа — его дело, а не игрового цикла на сервере.</para>
    /// </param>
    public void Register(Task animationTask, bool blocksEventQueue = true, float staggerSeconds = 0f)
    {
        if (animationTask == null || animationTask.IsCompleted) return;
        (blocksEventQueue ? _blocking : _background).Add(animationTask);

        if (staggerSeconds > 0f)
            _holdUntil = Mathf.Max(_holdUntil, Time.unscaledTime + staggerSeconds);
    }

    /// <summary>
    /// Чистка завершённых — именно здесь, а не в <c>ContinueWith</c>.
    ///
    /// Раньше удаление висело на <c>animationTask.ContinueWith(...)</c>, а он
    /// по умолчанию выполняется на потоке пула: список менялся из чужого
    /// потока, пока главный его читал. На List это гонка, которая проявляется
    /// редко и загадочно.
    /// </summary>
    private void Update()
    {
        Prune(_blocking);
        Prune(_background);
    }

    private static void Prune(List<Task> tasks)
    {
        for (int i = tasks.Count - 1; i >= 0; i--)
        {
            var task = tasks[i];
            if (!task.IsCompleted) continue;

            // Упавшая анимация тоже "завершена". Молча выбросить её — значит
            // потерять исключение целиком: анимации запускаются из async void,
            // так что больше о нём никто не узнает.
            if (task.IsFaulted)
                Debug.LogError($"[AnimationManager] Анимация упала: {task.Exception}");

            tasks.RemoveAt(i);
        }
    }
}
