using Mirror;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Assets.Scripts.Network
{
    public class GameConnectionManager : NetworkManager
    {
        private int connectedPlayers = 0;

        [SerializeField] private GameObject gameNetworkControllerPrefab; 

        public override void OnServerAddPlayer(NetworkConnectionToClient conn)
        {
            base.OnServerAddPlayer(conn);
            connectedPlayers++;

            Debug.Log($"[Server] Player connected: {connectedPlayers}");

            if (connectedPlayers == 2)
            {
                Debug.Log("[Server] Two players connected — starting game!");

                // Спауним контроллер игры
                var controllerObj = Instantiate(gameNetworkControllerPrefab);
                NetworkServer.Spawn(controllerObj);

                // Получаем компонент GameNetworkController и вызываем RPC
                var controller = controllerObj.GetComponent<GameNetworkController>();
                controller.RpcStartGame();
            }
        }

    }
}
