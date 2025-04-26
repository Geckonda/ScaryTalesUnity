using Mirror;
using ScaryTales.Interaction_Entities.EnvUnity;
using ScaryTales;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using ScaryTales.Abstractions;
using ScaryTales.Items;
using Assets.Scripts.Utilities;

namespace Assets.Scripts.Network
{
    public class GameNetworkController : NetworkBehaviour
    {
        public static GameNetworkController Instance { get; set; }
        private Dictionary<int, NetworkConnectionToClient> playerConnections = new();

        private IGameContext _gameContext;

        [SyncVar(hook = nameof(OnReadyPlayersChanged))]
        private int readyPlayers = 0;

        private void Awake()
        {
            if (Instance == null)
                Instance = this;
            else
                Destroy(gameObject);
        }


        [TargetRpc]
        private void TargetSetPlayer(NetworkConnection target, PlayerDTO playerDTO, PlayerDTO opponentDTO, List<int> cardsId)
        {
            Debug.Log($"[CLIENT] Start game. My index is: {playerDTO.Id}");

            var localPlayer = new Player(playerDTO.Id, playerDTO.Name, new UnityPlayerInput());
            var localOpponent = new Player(opponentDTO.Id, opponentDTO.Name);
            GameBuilder builder;
            if (playerDTO.IsStartPlayer)
            {
                builder = new GameBuilder(
                   new UnityNotifier(),
                   new GameBoard(),
                   localPlayer,
                   localOpponent);
            }
            else
            {
                builder = new GameBuilder(
                   new UnityNotifier(),
                   new GameBoard(),
                   localOpponent,
                   localPlayer);
            }

            var game = builder.Build();
            game._context.Deck.ShuffleById(cardsId);

            UnGameManager.Instance._context = game._context;
            UnGameManager.Instance._gameManager = game;
            UnGameManager.Instance.SetLocalPlayer(localPlayer);
            UnGameManager.Instance.SetLocalOpponent(localOpponent);
            UnGameManager.Instance.StartGameFromNetwork();
        }


        [Server]
        public void InitializeGame(List<Player> players, Dictionary<int, NetworkConnectionToClient> connectionMap)
        {
            var builder = new GameBuilder(
                new UnityNotifier(),
                new GameBoard(),
                players[0],
                players[1]);

            var game = builder.Build();
            _gameContext = game._context;

            List<int> cardsId = _gameContext.Deck.GetCardIds();

            int startPlayerId = players[0].Id;

            foreach (var player in players)
            {
                if (connectionMap.TryGetValue(player.Id, out var conn))
                {
                    var opponent = players.Find(p => p != player);

                    bool isStartPlayer = player.Id == startPlayerId;
                    bool isOpponentStart = opponent.Id == startPlayerId;

                    TargetSetPlayer(
                        conn,
                        ConverterDTO.PlayerToDTO(player, isStartPlayer),
                        ConverterDTO.PlayerToDTO(opponent, isOpponentStart),
                        cardsId);
                }
                else
                {
                    Debug.LogError($"[InitializeGame]: проблема подключения игрока {player.Id}");
                }
            }
        }
        private void OnReadyPlayersChanged(int oldValue, int newValue)
        {
            Debug.Log($"[SyncVar] Ready players changed: {newValue}");
        }
    }
    public class NetworkPlayerRegistry
    {
        public Dictionary<int, NetworkPlayerInput> PlayerInputs = new();
        public Dictionary<int, Player> GamePlayers = new(); // если нужно
    }

    
    [Serializable]
    public struct PlayerDTO
    {
        public int Id;
        public string Name;
        public bool IsStartPlayer;

        public PlayerDTO(int id, string name, bool isStartPlayer)
        {
            Id = id;
            Name = name;
            IsStartPlayer = isStartPlayer;
        }
    }
}
