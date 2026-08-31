using Assets.Scripts.Network;
using Assets.Scripts.UIEntities;
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

    [Tooltip("Не используется. Панель была отладочной; ход и ожидание показываются подсветкой ника.")]
    public TMP_Text NotifierText;

    [Tooltip("Не используется. Оставлено, чтобы не рвать привязку в сцене.")]
    public TMP_Text CurrentPlayerText;

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

    private readonly Dictionary<Player, TMP_Text> _playerScorePanels = new();
    private readonly Dictionary<Player, TMP_Text> _playerNameTexts = new();

    // Игрок, от которого ждут решения прямо сейчас, или null.
    private Player _deciding;

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
        _deciding = null;

        var localSeat = _seatLayout?.LocalSeat;
        if (localSeat != null)
            BindSeat(_view.LocalPlayer, localSeat.NameText, localSeat.ScoreText);

        for (int i = 0; i < _view.Opponents.Count; i++)
        {
            var seat = _seatLayout?.GetOpponentSeat(i);
            if (seat == null) continue;
            BindSeat(_view.Opponents[i], seat.NameText, seat.ScoreText);
        }

        _view.OnAddPointsToPlayer += HandleAddPointsToPlayer;

        RefreshTurnHighlight();
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
            // Seed it now; otherwise the scene placeholder shows until
            // this player first scores.
            scoreText.text = ScorePrefix + player.Score;
        }
    }

    private void HandleAddPointsToPlayer(Player player)
    {
        if (_playerScorePanels.TryGetValue(player, out TMP_Text panel))
        {
            panel.text = ScorePrefix + player.Score;
        }
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
            bool isDeciding = _deciding != null && pair.Key == _deciding;
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
        if (_deciding == player) _deciding = null;

        RefreshTurnHighlight();
    }

    /// <summary>Стол ждёт решения от этого игрока.</summary>
    public void SetDeciding(int playerId)
    {
        _deciding = _view?.FindPlayer(playerId);
        RefreshTurnHighlight();
    }

    /// <summary>Решение принято — вернуть обычную подсветку хода.</summary>
    public void ClearDeciding()
    {
        _deciding = null;
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
