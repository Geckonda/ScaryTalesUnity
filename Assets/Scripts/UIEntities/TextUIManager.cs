using Assets.Scripts.Network;
using Assets.Scripts.UIEntities;
using ScaryTales;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class TextUIManager : MonoBehaviour
{
    private ClientGameView _view;
    private SeatLayout _seatLayout;

    public TMP_Text NotifierText;
    public TMP_Text CurrentPlayerText;

    private Dictionary<Player, TMP_Text> _playerScorePanels = new();

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

        var localSeat = _seatLayout?.LocalSeat;
        if (localSeat != null)
        {
            if (localSeat.NameText != null)
                localSeat.NameText.text = _view.LocalPlayer.Name;
            if (localSeat.ScoreText != null)
            {
                _playerScorePanels[_view.LocalPlayer] = localSeat.ScoreText;
                // Seed it now; otherwise the scene placeholder shows until
                // this player first scores.
                localSeat.ScoreText.text = ScorePrefix + _view.LocalPlayer.Score;
            }
        }

        for (int i = 0; i < _view.Opponents.Count; i++)
        {
            var seat = _seatLayout?.GetOpponentSeat(i);
            if (seat == null) continue;
            if (seat.NameText != null)
                seat.NameText.text = _view.Opponents[i].Name;
            if (seat.ScoreText != null)
            {
                _playerScorePanels[_view.Opponents[i]] = seat.ScoreText;
                seat.ScoreText.text = ScorePrefix + _view.Opponents[i].Score;
            }
        }

        _view.OnAddPointsToPlayer += HandleAddPointsToPlayer;

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
            panel.text = ScorePrefix + player.Score;
        }
    }

    public void UpdateCurrentPlayerText()
    {
        if (CurrentPlayerText != null && _view?.CurrentPlayer != null)
        {
            CurrentPlayerText.text = $"Текущий игрок: {_view.CurrentPlayer.Name}";
        }
    }
}
