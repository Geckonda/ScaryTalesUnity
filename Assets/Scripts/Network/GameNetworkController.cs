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

        private IGameContext _serverGameContext;

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
            var networkInput = NetworkClient.localPlayer.GetComponent<NetworkPlayerInput>();

            var localPlayer = new Player(playerDTO.Id, playerDTO.Name, new UnityPlayerInput());
            var localOpponent = new Player(opponentDTO.Id, opponentDTO.Name, networkInput);
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
            game.LocalPlayer = localPlayer;
            game.LocalOpponent = localOpponent;


            UnGameManager.Instance._context = game._context;
            UnGameManager.Instance._gameManager = game;
            UnGameManager.Instance.SetLocalPlayer(localPlayer);
            UnGameManager.Instance.SetLocalOpponent(localOpponent);
            UnGameManager.Instance.StartGameFromNetwork();

        }

        private void CmdPlayCardTest(int cardId)
        {
            CmdPlayCard(cardId);
        }

        [Command(requiresAuthority = false)]
        public void CmdPlayCard(int cardId)
        {
            // Сервер получает действие
            // 1. Выполняет серверную валидацию (например, проверяет, может ли игрок сейчас ходить)
            // 2. Обновляет игровое состояние
            // 3. Отсылает другим клиентам обновление

            var player = UnGameManager.Instance._context.GameState.GetCurrentPlayer();
            var card = player.Hand.FirstOrDefault(c => c.Id == cardId);

            if (card != null)
            {
                // Обновляем серверное состояние
                _serverGameContext.GameManager.PlayCard(card);

                // Рассылаем клиентам
                RpcOnCardPlayed(cardId);
            }
        }
        [ClientRpc]
        private void RpcOnCardPlayed(int cardId)
        {
            var player = UnGameManager.Instance.CurrentPlayer;
            var card = player.Hand.FirstOrDefault(c => c.Id == cardId);

            if (card != null)
            {
                UnGameManager.Instance.PlayCard(card); // например, триггер анимации, обновление UI и т.п.
            }
        }


        [Command(requiresAuthority = false)]
        public void CmdSelectItem(int itemType)
        {
            // Рассылаем всем нужным клиентам выбранный ID
            RpcNotifyOtherPlayersItemSelected(itemType);
        }

        [ClientRpc]
        private void RpcNotifyOtherPlayersItemSelected(int itemType)
        {
            if (!isLocalPlayer) // Чтобы не вызывалось у того, кто сам выбирал
            {
                var input = (NetworkPlayerInput)UnGameManager.Instance.LocalOpponent.PlayerInput;
                input.OnItemSelectedFromRemote(itemType);
            }
        }

        [Command(requiresAuthority = false)]
        public void CmdSelectCard(int cardId)
        {
            Debug.Log($"Server: Received cardId: {cardId}, type: {cardId.GetType()}");

            if (cardId < 0) // Пример валидации
            {
                Debug.LogError("Invalid cardId!");
                return;
            }
            // Рассылаем всем нужным клиентам выбранный ID
            RpcNotifyOtherPlayersCardSelected(cardId);
        }

        [ClientRpc]
        private void RpcNotifyOtherPlayersCardSelected(int cardId)
        {
            if (!isLocalPlayer) // Чтобы не вызывалось у того, кто сам выбирал
            {
                var input = (NetworkPlayerInput)UnGameManager.Instance.LocalOpponent.PlayerInput;
                input.OnCardSelectedFromRemote(cardId);
            }
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
            _serverGameContext = game._context;

            List<int> cardsId = _serverGameContext.Deck.GetCardIds();

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
