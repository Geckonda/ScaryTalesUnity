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

    [Tooltip("Optional. What this player wants to be called. Leave unwired and everyone stays Player1..4, so the game works before this exists in the scene.")]
    [SerializeField] private TMP_InputField nicknameInput;

    // Typing your name every launch would be tedious; the last one used is
    // remembered on this machine and offered back. Per-device convenience
    // only — the server never sees it until a room is created or joined.
    private const string NicknamePrefsKey = "ScaryTales.Nickname";

    [Tooltip("Fallback only — the live NetworkManager is found via NetworkManager.singleton. See the Net property.")]
    [SerializeField] private NetworkManager networkManager;

    /// <summary>
    /// The NetworkManager that is actually alive.
    ///
    /// <para><b>Not the serialized reference.</b> Mirror's NetworkManager is
    /// DontDestroyOnLoad (it even reparents itself to the scene root to make
    /// that work), so it survives the scene reload that
    /// <c>GameConnectionManager.ReturnToMenu</c> does at the end of a game.
    /// The reloaded scene then brings its own NetworkManager, which Mirror
    /// destroys as a duplicate — and <c>networkManager</c> above points at
    /// exactly that dead duplicate. Calling StartClient on it threw
    /// NullReferenceException from inside InitializeSingleton, which is why a
    /// second game could not be started without restarting the whole
    /// process.</para>
    ///
    /// <para>The serialized field is kept only as a fallback for the very
    /// first frames, before any singleton exists.</para>
    /// </summary>
    private NetworkManager Net =>
        NetworkManager.singleton != null ? NetworkManager.singleton : networkManager;

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
        // Both outcomes are reported here as well as in LobbyManager, because
        // LobbyManager lives on the lobby panel and only speaks once the panel
        // is up. A create or join that never gets that far has to say so
        // somewhere, or the button looks dead.
        Assets.Scripts.Network.RoomClient.Bind();
        Assets.Scripts.Network.RoomClient.Joined += OnRoomJoined;
        Assets.Scripts.Network.RoomClient.JoinFailed += OnRoomJoinFailed;

        if (nicknameInput != null)
            nicknameInput.text = PlayerPrefs.GetString(NicknamePrefsKey, string.Empty);
    }

    /// <summary>
    /// The nickname to ask for, remembered for next launch.
    ///
    /// Sent as typed — trimming and every other rule live on the server,
    /// because the name is shown to *other* players and the client supplying
    /// it is exactly the party that cannot be trusted with it. Empty is a
    /// valid answer and means "give me a default".
    /// </summary>
    private string CurrentNickname()
    {
        if (nicknameInput == null) return string.Empty;
        var nickname = nicknameInput.text ?? string.Empty;
        PlayerPrefs.SetString(NicknamePrefsKey, nickname);
        PlayerPrefs.Save();
        return nickname;
    }

    private void OnDestroy()
    {
        Assets.Scripts.Network.RoomClient.Joined -= OnRoomJoined;
        Assets.Scripts.Network.RoomClient.JoinFailed -= OnRoomJoinFailed;
    }

    private void OnRoomJoined(RoomJoinedEvent evt)
    {
        Report($"Комната {evt.Code}" + (evt.IsOwner ? " создана." : " — вы вошли."));
    }

    public void CreateRoom()
    {
        var name = roomNameInput != null ? roomNameInput.text : string.Empty;
        // An empty name is fine — the server falls back to the code. What
        // matters is that the player gets a room.
        var nickname = CurrentNickname();
        Connect(() => NetworkClient.Send(new CreateRoomIntent { RoomName = name, PlayerName = nickname }),
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
        var nickname = CurrentNickname();
        Connect(() => NetworkClient.Send(new JoinRoomIntent { Code = code, PlayerName = nickname }), asHost: false);
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
            if (Net == null)
            {
                Report("Сеть недоступна: NetworkManager не найден.");
                return;
            }
            Net.networkAddress = ResolveAddress();
            if (!TryStartNetwork(asHost)) return;
        }

        _pending = StartCoroutine(SendWhenConnected(onConnected));
    }

    /// <summary>
    /// Brings Mirror up, hosting only if that can actually work.
    ///
    /// <para>Hosting binds the transport's port, and if a dedicated server (or
    /// another copy of the game) already holds it, <c>StartHost()</c> throws a
    /// SocketException straight out of the click handler — the button then
    /// does nothing at all, with only a stack trace in a log nobody is
    /// watching. The port is checked first rather than catching afterwards,
    /// because a failed StartHost leaves Mirror half-initialized and
    /// recovering from that is worse than not getting into it.</para>
    ///
    /// <para>Falling back to a client is not a consolation prize: something is
    /// already serving on this address, and connecting to it is what the
    /// player wanted.</para>
    /// </summary>
    private bool TryStartNetwork(bool asHost)
    {
        if (asHost && !IsListenPortFree())
        {
            Debug.LogWarning("[Menu] Port already in use — a server is running here; connecting to it as a client.");
            asHost = false;
        }

        try
        {
            if (asHost) Net.StartHost();
            else Net.StartClient();
            return true;
        }
        catch (System.Exception e)
        {
            Report("Не удалось запустить сеть — подробности в логе.");
            Debug.LogException(e);
            return false;
        }
    }

    private bool IsListenPortFree()
    {
        if (!(Transport.active is PortTransport portTransport)) return true;
        try
        {
            // Binding and immediately releasing is the only reliable way to
            // ask; there is no "is this port taken" that isn't a race, and
            // losing that race just means we host and Mirror throws — which
            // the caller reports rather than swallowing.
            using (new System.Net.Sockets.UdpClient(portTransport.Port)) { }
            return true;
        }
        catch (System.Net.Sockets.SocketException)
        {
            return false;
        }
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

    private void OnRoomJoinFailed(RoomJoinFailure reason)
    {
        Report(DescribeFailure(reason));
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
        // Logged as a warning as well as shown, because statusText is an
        // optional slot: with it unwired this is the only trace a click left,
        // and a pressed button that reports nothing looks broken.
        if (!string.IsNullOrEmpty(message)) Debug.LogWarning($"[Menu] {message}");
    }
}
