using Assets.Scripts.Network.Messages;
using Mirror;
using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// The way into a game (Phase 6.6).
///
/// <para>Connecting and joining are now two separate steps. This connects to
/// the server, waits for the link, then asks it for a room — either a new one
/// (and the server answers with a code to read out to friends) or an existing
/// one by code. Nobody types an IP.</para>
/// </summary>
public class MenuManager : MonoBehaviour
{
    [Tooltip("Room name when creating, room code when joining.")]
    [SerializeField] private TMP_InputField roomNameInput;

    [SerializeField] private NetworkManager networkManager;

    [Tooltip("Address of the game server. 'localhost' for testing against your own machine; the VM's address once it is up.")]
    [SerializeField] private string serverAddress = "localhost";

    [Tooltip("Run the server in this process when creating a room. Turn OFF once there is a real dedicated server to connect to — then creating a room is an ordinary client action like joining.")]
    [SerializeField] private bool hostLocallyOnCreate = true;

    [Tooltip("TESTING ONLY. Code used by Join when the input is left empty, so you don't have to type one every run. Must match the server's Fixed Room Code. Leave EMPTY to require a typed code.")]
    [SerializeField] private string fallbackRoomCode = "LOCALHOST";

    [Tooltip("Give up on a connection attempt after this many seconds.")]
    [SerializeField] private float connectTimeout = 5f;

    [Tooltip("Optional. Shows connection and join errors.")]
    [SerializeField] private TMP_Text statusText;

    private Coroutine _pending;

    private void Awake()
    {
        // Registered here rather than in LobbyManager so a refusal is still
        // reported when the join never gets far enough to show a lobby.
        NetworkClient.RegisterHandler<RoomJoinFailedEvent>(OnRoomJoinFailed);
    }

    public void CreateRoom()
    {
        var name = roomNameInput != null ? roomNameInput.text : string.Empty;
        // An empty name is fine — the server falls back to the code. What
        // matters is that the player gets a room.
        Connect(() => NetworkClient.Send(new CreateRoomIntent { RoomName = name }),
                hostLocallyOnCreate);
    }

    public void JoinRoom()
    {
        var code = roomNameInput != null ? roomNameInput.text : string.Empty;
        if (string.IsNullOrWhiteSpace(code))
        {
            // Testing convenience: an empty box means "the usual room".
            // With fallbackRoomCode cleared this goes back to demanding a
            // typed code.
            code = fallbackRoomCode;
            if (string.IsNullOrWhiteSpace(code))
            {
                Report("Введите код комнаты.");
                return;
            }
            Report($"Код не введён — подключаюсь к {code}.");
        }
        // Normalization happens on the server, so what the player typed goes
        // over the wire as-is — including any mistake, which comes back as
        // "unknown code" rather than being silently rewritten into some other
        // room's code.
        Connect(() => NetworkClient.Send(new JoinRoomIntent { Code = code }), asHost: false);
    }

    /// <summary>
    /// Brings the network up if it isn't already, then runs <paramref name="onConnected"/>.
    /// Both buttons are re-entrant-safe: a second click while a connection is
    /// pending replaces the attempt rather than stacking another one.
    /// </summary>
    private void Connect(System.Action onConnected, bool asHost)
    {
        if (_pending != null) StopCoroutine(_pending);

        if (NetworkClient.isConnected)
        {
            // Already in the lobby — no need to reconnect, just ask again.
            onConnected();
            return;
        }

        if (!NetworkClient.active && !NetworkServer.active)
        {
            networkManager.networkAddress = ResolveAddress();
            if (asHost) networkManager.StartHost();
            else networkManager.StartClient();
        }

        _pending = StartCoroutine(SendWhenConnected(onConnected));
    }

    private string ResolveAddress()
    {
        var address = string.IsNullOrWhiteSpace(serverAddress) ? "localhost" : serverAddress.Trim();
        // KCP wants an address it can resolve; "localhost" works, but the
        // literal has been a source of confusion before, so be explicit.
        return address == "localhost" ? "127.0.0.1" : address;
    }

    private IEnumerator SendWhenConnected(System.Action onConnected)
    {
        Report("Соединение...");
        float deadline = Time.unscaledTime + connectTimeout;
        while (!NetworkClient.isConnected)
        {
            if (Time.unscaledTime > deadline)
            {
                Report("Не удалось подключиться к серверу.");
                _pending = null;
                yield break;
            }
            yield return null;
        }
        Report(string.Empty);
        onConnected();
        _pending = null;
    }

    private void OnRoomJoinFailed(RoomJoinFailedEvent evt)
    {
        Report(DescribeFailure((RoomJoinFailure)evt.Reason));
    }

    private static string DescribeFailure(RoomJoinFailure reason)
    {
        // The server sends a code, not a sentence, so the wording stays here
        // where the player is.
        switch (reason)
        {
            case RoomJoinFailure.UnknownCode: return "Комната с таким кодом не найдена.";
            case RoomJoinFailure.RoomFull: return "В комнате нет свободных мест.";
            case RoomJoinFailure.GameInProgress: return "Игра в этой комнате уже началась.";
            case RoomJoinFailure.AlreadyInRoom: return "Вы уже в комнате.";
            case RoomJoinFailure.ServerFull: return "Сервер занят, попробуйте позже.";
            default: return "Не удалось войти в комнату.";
        }
    }

    private void Report(string message)
    {
        if (statusText != null) statusText.text = message;
        if (!string.IsNullOrEmpty(message)) Debug.Log($"[Menu] {message}");
    }
}
