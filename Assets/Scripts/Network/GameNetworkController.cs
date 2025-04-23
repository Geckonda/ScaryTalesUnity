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
using Mirror.Examples.Basic;
using Player = ScaryTales.Player;

namespace Assets.Scripts.Network
{
    public class GameNetworkController : NetworkBehaviour
    {
        public static GameNetworkController Instance { get; set; }
        private Dictionary<int, NetworkConnectionToClient> playerConnections = new();


        [SyncVar(hook = nameof(OnReadyPlayersChanged))]
        private int readyPlayers = 0;

        private void Awake()
        {
            if (Instance == null)
                Instance = this;
            else
                Destroy(gameObject);
        }

        //// Вызывается клиентом, когда он готов начать игру
        //[Command(requiresAuthority = false)] // если нет авторити на объекте
        //public void CmdPlayerReady()
        //{
        //    readyPlayers++;

        //    Debug.Log($"[SERVER] Player marked as ready. Total: {readyPlayers}");

        //    if (readyPlayers >= 1) // Можно сделать гибче — по количеству подключений
        //    {
        //        Debug.Log("[SERVER] Both players ready. Starting game...");
        //        RpcStartGame();
        //    }
        //}

        // Отправляется всем клиентам, чтобы они начали игру локально
        [ClientRpc]
        private void RpcStartGame(int localPlayerIndex)
        {
            Debug.Log("[CLIENT] Received start game signal.");
            var localPlayer = UnGameManager.Instance._context.Players[localPlayerIndex];
            UnGameManager.Instance.SetLocalPlayer(localPlayer);
            UnGameManager.Instance.StartGameFromNetwork(); // Метод в твоем менеджере
        }

        [TargetRpc]
        private void TargetStartGame(NetworkConnection target, int localPlayerIndex)
        {
            Debug.Log($"[CLIENT] Start game. My index is: {localPlayerIndex}");

            var localPlayer = UnGameManager.Instance._context.Players[localPlayerIndex];
            UnGameManager.Instance.SetLocalPlayer(localPlayer);
            UnGameManager.Instance.StartGameFromNetwork();
        }


        [Server]
        public void InitializeGameWithInputs(List<Player> players, Dictionary<int, NetworkConnectionToClient> connectionMap)
        {
            var builder = new GameBuilder(
                players[0].PlayerInput,
                players[1].PlayerInput,
                new UnityNotifier(),
                new GameBoard(),
                players[0],
                players[1]);

            var game = builder.Build();

            UnGameManager.Instance._gameManager = game;
            UnGameManager.Instance._context = game._context;

            for (int i = 0; i < players.Count; i++)
            {
                var playerId = players[i].Id;
                if (connectionMap.TryGetValue(playerId, out var conn))
                {
                    TargetStartGame(conn, i); // всё корректно
                }
                else
                {
                    Debug.LogWarning($"[Server] No connection found for player ID {playerId}");
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

}
