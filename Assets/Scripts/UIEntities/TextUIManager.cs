using Assets.Scripts;
using ScaryTales;
using ScaryTales.Abstractions;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class TextUIManager : MonoBehaviour
{
    private GameSession _session;
    private IGameContext _gameContext;
    public TMP_Text Player1Name;
    public TMP_Text Player2Name;
    public TMP_Text Player1ScoreText;
    public TMP_Text Player2ScoreText;
    public TMP_Text NotifierText;
    private Dictionary<Player, TMP_Text> _playerScorePanels = new();

    public TMP_Text CurrentPlayerText;

    /// <summary>
    /// Wires this text UI to a session. Called by UnGameManager.StartNewSession.
    /// </summary>
    public void Initialize(GameSession session)
    {
        _session = session;
        _gameContext = session.Context;

        _playerScorePanels[_session.LocalPlayer] = Player1ScoreText;
        _playerScorePanels[_session.LocalOpponent] = Player2ScoreText;

        Player1Name.text = _session.LocalPlayer.Name;
        Player2Name.text = _session.LocalOpponent.Name;

        _gameContext.GameManager.OnAddPointsToPlayer += HandleAddPointsToPlayer;
        //_gameContext.GameManager.OnMessagePrinted += HandleNotify;

        UpdateCurrentPlayerText();
    }



    private List<string> messages = new();
    private void HandleNotify(string message)
    {
        messages.Add(message);
        if (messages.Count > 5)
        {
            messages.RemoveAt(0);
            NotifierText.text = "";
        }

        NotifierText.text = string.Join("\n", messages);
    }

    private void HandleAddPointsToPlayer(Player player)
    {
        if (_playerScorePanels.TryGetValue(player, out TMP_Text panel))
        {
            panel.text = "��: " + player.Score.ToString();
        }
    }

    public void UpdateCurrentPlayerText()
    {
        if (CurrentPlayerText != null)
        {
            var currentPlayer = _gameContext.GameState.GetCurrentPlayer();
            CurrentPlayerText.text = $"������ �����: {currentPlayer.Name}";
        }
    }
}

