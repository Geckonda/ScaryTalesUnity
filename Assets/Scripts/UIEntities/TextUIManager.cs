using Assets.Libraries.ScaryTales;
using Assets.Scripts.Network;
using Assets.Scripts.UIEntities;
using DG.Tweening;
using ScaryTales;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Имена и очки на местах игроков, плюс подсветка того, чей сейчас ход.
///
/// <para>Чей ход, показывается цветом ника прямо на месте, а не отдельной
/// строкой: строка «Текущий игрок: …» требовала отдельной панели и всё равно
/// заставляла искать глазами, о ком речь. Той же подсветкой отмечается игрок,
/// от которого стол ждёт решения — раньше для этого нужен был текст, а теперь
/// это тот же самый жест.</para>
/// </summary>
public class TextUIManager : MonoBehaviour
{
    private ClientGameView _view;
    private SeatLayout _seatLayout;

    // Здесь были поля NotifierText и CurrentPlayerText. Оба лежали в
    // выключенной панели MessagePanel и не читались никаким кодом: чей ход и
    // от кого ждут решения показывается цветом ника прямо на месте игрока.
    // Убраны 2026-08-31; строки под них в сцене отпадут при следующем
    // сохранении.

    [Header("Подсветка хода")]
    [Tooltip("Цвет ника игрока, чей сейчас ход.")]
    [SerializeField] private Color _activeNameColor = new Color(1f, 0.84f, 0f); // золотой

    [Tooltip("Цвет ника остальных игроков.")]
    [SerializeField] private Color _idleNameColor = Color.white;

    [Tooltip("Цвет ника игрока, от которого стол ждёт решения (Дракон выбирает, что сбросить). Обычно это тот же, чей ход, но не всегда: Зачарованный лес ночью заставляет сбрасывать всех.")]
    [SerializeField] private Color _decidingNameColor = new Color(1f, 0.45f, 0.1f); // янтарный

    [Tooltip("Цвет ника игрока, вышедшего посреди партии. Место остаётся на экране, но гаснет.")]
    [SerializeField] private Color _leftNameColor = new Color(0.5f, 0.5f, 0.5f); // серый

    [Header("Обводка (необязательно)")]
    [Tooltip("Обводить подсвеченный ник. Качество зависит от отступов в атласе шрифта: если он собран с малым padding, обводка обрежется. Включайте и смотрите — цвета хватает и без неё.")]
    [SerializeField] private bool _useOutline = false;

    [SerializeField, Range(0f, 0.5f)] private float _outlineWidth = 0.2f;
    [SerializeField] private Color _outlineColor = new Color(0.4f, 0.28f, 0f);

    [Header("Счёт")]
    [Tooltip("Показывать ли счёт оппонентов всю партию. Выключено: свои очки игрок видит, чужие — нет. На разборе конца партии чужой счёт откроется в любом случае — иначе разбор и победителя смотреть нечем.")]
    [SerializeField] private bool _showOpponentScores = false;

    [Header("Всплеск очков")]
    [Tooltip("Во сколько раз счёт подскакивает в момент начисления. Единица — всплеска нет.")]
    [SerializeField] private float _scorePulseScale = 1.35f;

    [Tooltip("Сколько длится всплеск целиком, секунд: половина на рост, половина на возврат. Держать коротким: очки приходят подряд (Цыганка даёт по очку за каждое раскрытое место), и длинный всплеск слипся бы в мельтешение.")]
    [SerializeField] private float _scorePulseSeconds = 0.3f;

    [Tooltip("Каким цветом загорается счёт на время всплеска.")]
    [SerializeField] private Color _scorePulseColor = new Color(1f, 0.84f, 0f); // золотой, как подсветка хода

    private readonly Dictionary<Player, TMP_Text> _playerScorePanels = new();
    private readonly Dictionary<Player, TMP_Text> _playerNameTexts = new();

    /// <summary>
    /// Исходные цвет и масштаб счёта — то, к чему возвращается всплеск.
    ///
    /// <para>Снимаются один раз, при привязке места, а НЕ в начале всплеска.
    /// Очки приходят подряд, и второй всплеск, начатый посреди первого, принял
    /// бы за исходное уже подскочившее значение — счёт так и остался бы
    /// раздутым и жёлтым.</para>
    /// </summary>
    private readonly Dictionary<TMP_Text, (Color Color, Vector3 Scale)> _scoreBase = new();

    // Спрятанные на время партии таблички чужого счёта. Держим списком, чтобы
    // открыть их все разом в конце.
    private readonly List<TMP_Text> _hiddenScores = new();

    // Ожидаемых может быть НЕСКОЛЬКО: Зачарованный лес спрашивает всех
    // сразу. Пока это был один игрок, подсвечивался последний спрошенный, а
    // остальные выглядели так, будто от них ничего не ждут.
    private readonly HashSet<Player> _deciding = new();

    // Shared so Initialize and HandleAddPointsToPlayer can't drift apart.
    private const string ScorePrefix = "ПО: ";

    /// <summary>
    /// Wires this text UI to the client mirror and the seat layout.
    /// Each seat carries its own NameText and ScoreText; this class just
    /// fills them in and watches OnAddPointsToPlayer to keep scores live.
    /// </summary>
    public void Initialize(ClientGameView view, SeatLayout seatLayout)
    {
        _view = view;
        _seatLayout = seatLayout;

        _playerScorePanels.Clear();
        _playerNameTexts.Clear();
        _scoreBase.Clear();
        _hiddenScores.Clear();
        _deciding.Clear();

        var localSeat = _seatLayout?.LocalSeat;
        if (localSeat != null)
            BindSeat(_view.LocalPlayer, localSeat.NameText, localSeat.ScoreText);

        for (int i = 0; i < _view.Opponents.Count; i++)
        {
            var seat = _seatLayout?.GetOpponentSeat(i);
            if (seat == null) continue;
            BindSeat(_view.Opponents[i], seat.NameText, seat.ScoreText);

            // Табличку прячем, но привязку оставляем: счёт под ней продолжает
            // обновляться и к концу партии будет верным — открыть её тогда
            // достаточно одним SetActive.
            if (_showOpponentScores || seat.ScoreText == null) continue;
            seat.ScoreText.gameObject.SetActive(false);
            _hiddenScores.Add(seat.ScoreText);
        }

        _view.OnAddPointsToPlayer += HandleAddPointsToPlayer;
        // Конец партии открывает чужой счёт: разбор правила и победителя иначе
        // смотреть нечем. Два входа, потому что разбора может и не быть —
        // финального правила у партии может не оказаться.
        _view.OnFinalScoringStarted += HandleFinalScoringStarted;
        _view.OnGameEnded += HandleGameEnded;

        RefreshTurnHighlight();
    }

    private void HandleFinalScoringStarted(Rule rule) => RevealAllScores();
    private void HandleGameEnded(int[] winnerIds) => RevealAllScores();

    /// <summary>
    /// Открывает счёт всех игроков. Идемпотентна: зовётся и из разбора правила,
    /// и из конца партии, а случиться может и то, и другое подряд.
    /// </summary>
    public void RevealAllScores()
    {
        foreach (var text in _hiddenScores)
            if (text != null) text.gameObject.SetActive(true);
        _hiddenScores.Clear();
    }

    private void BindSeat(Player player, TMP_Text nameText, TMP_Text scoreText)
    {
        if (player == null) return;

        if (nameText != null)
        {
            nameText.text = player.Name;
            _playerNameTexts[player] = nameText;
        }
        if (scoreText != null)
        {
            _playerScorePanels[player] = scoreText;
            // Исходное состояние снимаем здесь, до первого всплеска, — см.
            // пояснение у _scoreBase.
            _scoreBase[scoreText] = (scoreText.color, scoreText.transform.localScale);
            // Seed it now; otherwise the scene placeholder shows until
            // this player first scores.
            scoreText.text = ScorePrefix + player.Score;
        }
    }

    private void HandleAddPointsToPlayer(Player player)
    {
        if (!_playerScorePanels.TryGetValue(player, out TMP_Text panel)) return;

        panel.text = ScorePrefix + player.Score;

        // Спрятанный счёт обновляем молча: всплеск на невидимой табличке —
        // пустая работа, а её значение всё равно верно к моменту, когда её
        // откроют в конце партии.
        if (panel.gameObject.activeInHierarchy) PulseScore(panel);
    }

    /// <summary>
    /// Счёт подскакивает, загорается и возвращается на место.
    ///
    /// <para>Нарочно ничем не блокирует очередь событий: это украшение, и
    /// партия не должна его ждать. Живёт само по себе на двух твинах —
    /// масштаб на трансформе, цвет на тексте; они пишут разные свойства и не
    /// дерутся.</para>
    /// </summary>
    private void PulseScore(TMP_Text panel)
    {
        if (panel == null || _scorePulseSeconds <= 0f) return;
        if (!_scoreBase.TryGetValue(panel, out var start)) return;

        var t = panel.transform;

        // Предыдущий всплеск мог не доиграть: очки приходят подряд. Гасим его
        // и возвращаем исходное — иначе следующий начал бы с уже подскочившего
        // и накапливал бы масштаб.
        t.DOKill();
        DOTween.Kill(panel);
        t.localScale = start.Scale;
        panel.color = start.Color;

        // Yoyo из двух проходов: половина времени вверх, половина обратно.
        // Возврат — часть самого твина, поэтому даже прерванный всплеск не
        // оставит счёт раздутым: следующий вызов начнёт с восстановления выше.
        float half = _scorePulseSeconds / 2f;
        t.DOScale(start.Scale * _scorePulseScale, half)
            .SetEase(Ease.OutQuad).SetLoops(2, LoopType.Yoyo);
        panel.DOColor(_scorePulseColor, half)
            .SetEase(Ease.OutQuad).SetLoops(2, LoopType.Yoyo);
    }

    // ---- Подсветка ----

    /// <summary>
    /// Перекрашивает ники под текущее состояние: чей ход и от кого ждут
    /// решения. Зовётся на каждой смене хода и на каждом запросе решения —
    /// дешевле пересчитать всё, чем следить, что именно изменилось.
    /// </summary>
    public void RefreshTurnHighlight()
    {
        if (_view == null) return;
        var current = _view.CurrentPlayer;

        foreach (var pair in _playerNameTexts)
        {
            bool isDeciding = _deciding.Contains(pair.Key);
            bool isCurrent = current != null && pair.Key == current;

            // Ожидание решения важнее, чем «чей ход»: обычно это один и тот
            // же игрок, но не всегда — Зачарованный лес ночью спрашивает всех.
            var color = isDeciding ? _decidingNameColor
                      : isCurrent ? _activeNameColor
                      : _idleNameColor;

            pair.Value.color = color;
            ApplyOutline(pair.Value, _useOutline && (isDeciding || isCurrent));
        }
    }

    /// <summary>
    /// Игрок вышел, партия идёт дальше. Его место остаётся на экране —
    /// пересобирать раскладку посреди партии значило бы переселять уже
    /// лежащие карты, — но подписывается так, чтобы его не ждали.
    ///
    /// <para>Очки не стираем: он их заработал, и в итоговом счёте они
    /// участвуют.</para>
    /// </summary>
    public void MarkPlayerLeft(Player player)
    {
        if (player == null) return;

        if (_playerNameTexts.TryGetValue(player, out var nameText) && nameText != null)
        {
            nameText.text = $"{player.Name} (вышел)";
            nameText.color = _leftNameColor;
            ApplyOutline(nameText, false);
        }

        // Ник больше не участвует в подсветке хода: очередь его не касается.
        _playerNameTexts.Remove(player);
        _deciding.Remove(player);

        RefreshTurnHighlight();
    }

    /// <summary>Стол ждёт решения ещё и от этого игрока.</summary>
    public void SetDeciding(int playerId)
    {
        var player = _view?.FindPlayer(playerId);
        if (player == null) return;
        _deciding.Add(player);
        RefreshTurnHighlight();
    }

    /// <summary>Этот игрок ответил — снять с него ожидание.</summary>
    public void ClearDeciding(int playerId)
    {
        var player = _view?.FindPlayer(playerId);
        if (player == null) return;
        _deciding.Remove(player);
        RefreshTurnHighlight();
    }

    /// <summary>Ждать больше некого: партия кончилась или прервана.</summary>
    public void ClearAllDeciding()
    {
        if (_deciding.Count == 0) return;
        _deciding.Clear();
        RefreshTurnHighlight();
    }

    /// <summary>
    /// Обводка глифов через материал текста.
    ///
    /// <c>fontMaterial</c> отдаёт копию материала на этот объект, поэтому
    /// правка не задевает остальные тексты. Работает только если атлас шрифта
    /// собран с достаточным padding — иначе обводке некуда лечь и её обрежет.
    /// Поэтому она и необязательная.
    /// </summary>
    private void ApplyOutline(TMP_Text text, bool enabled)
    {
        if (text == null) return;
        var material = text.fontMaterial;
        if (material == null) return;

        if (enabled)
        {
            material.EnableKeyword(ShaderUtilities.Keyword_Outline);
            material.SetFloat(ShaderUtilities.ID_OutlineWidth, _outlineWidth);
            material.SetColor(ShaderUtilities.ID_OutlineColor, _outlineColor);
        }
        else
        {
            material.SetFloat(ShaderUtilities.ID_OutlineWidth, 0f);
            material.DisableKeyword(ShaderUtilities.Keyword_Outline);
        }
    }
}
