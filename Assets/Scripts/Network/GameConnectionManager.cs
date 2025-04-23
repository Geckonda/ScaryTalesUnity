using Mirror;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using ScaryTales;
using ScaryTales.Abstractions;

namespace Assets.Scripts.Network
{
    public class GameConnectionManager : NetworkManager
    {
        private int connectedPlayers = 0;

        private List<Player> _players = new();
        private Dictionary<int, NetworkConnectionToClient> _connectionMap = new();


        [SerializeField] private GameObject gameNetworkControllerPrefab; 

        public override void OnServerAddPlayer(NetworkConnectionToClient conn)
        {
            base.OnServerAddPlayer(conn);
            connectedPlayers++;

            Debug.Log($"[Server] Player connected: {connectedPlayers}");


            // Получаем GameObject игрока, который был заспаунен через base
            var playerGO = conn.identity.gameObject;

            // Получаем NetworkPlayerInput, добавленный на этот объект
            var networkInput = playerGO.GetComponent<NetworkPlayerInput>();

            // Создаем контроллер (временно мок)
            var mockNetworkController = new MockController();

            // Инициализируем
            networkInput.Initialize(conn.connectionId, mockNetworkController);

            // Создаем логического игрока
            IPlayerInput playerInput = networkInput;
            _players.Add(new Player(conn.connectionId, $"Player{connectedPlayers}", playerInput));
            _connectionMap[conn.connectionId] = conn;
            if (connectedPlayers == 2)
            {
                Debug.Log("[Server] Two players connected — starting game!");

                // Спауним контроллер игры
                var controllerObj = Instantiate(gameNetworkControllerPrefab);
                NetworkServer.Spawn(controllerObj);

                // Получаем компонент GameNetworkController и вызываем RPC
                var controller = controllerObj.GetComponent<GameNetworkController>();
                controller.InitializeGameWithInputs(_players, _connectionMap);
            }
        }

    }
}
