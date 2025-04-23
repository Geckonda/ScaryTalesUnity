using Mirror;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Assets.Scripts.Network
{
    public class GameNetworkController : NetworkBehaviour
    {
        public static GameNetworkController Instance { get; private set; }

        [SyncVar(hook = nameof(OnReadyPlayersChanged))]
        private int readyPlayers = 0;

        private void Awake()
        {
            if (Instance == null)
                Instance = this;
            else
                Destroy(gameObject);
        }

        // Вызывается клиентом, когда он готов начать игру
        [Command(requiresAuthority = false)] // если нет авторити на объекте
        public void CmdPlayerReady()
        {
            readyPlayers++;

            Debug.Log($"[SERVER] Player marked as ready. Total: {readyPlayers}");

            if (readyPlayers >= 1) // Можно сделать гибче — по количеству подключений
            {
                Debug.Log("[SERVER] Both players ready. Starting game...");
                RpcStartGame();
            }
        }

        // Отправляется всем клиентам, чтобы они начали игру локально
        [ClientRpc]
        public void RpcStartGame()
        {
            Debug.Log("[CLIENT] Received start game signal.");
            UnGameManager.Instance.StartGameFromNetwork(); // Метод в твоем менеджере
        }

        private void OnReadyPlayersChanged(int oldValue, int newValue)
        {
            Debug.Log($"[SyncVar] Ready players changed: {newValue}");
        }
    }
}
