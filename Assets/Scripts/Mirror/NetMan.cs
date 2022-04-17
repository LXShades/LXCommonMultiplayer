using Mirror;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

public class NetMan : NetworkManager
{
    public static new NetMan singleton { get; private set; }

    public delegate void ConnectionEvent(NetworkConnection connection);
    public delegate void BasicEvent();

    [Header("Multiplayer Essentials Extensions")]
    /// <summary>
    /// Auto-created networked object that can hold server/game state and settings
    /// </summary>
    public GameState gameStatePrefab;

    /// <summary>
    /// Logs when connecting, disconnecting, etc
    /// </summary>
    public bool enableVerboseLogging;

    /// <summary>
    /// Connected to the server as a client
    /// </summary>
    public ConnectionEvent onClientConnect;

    /// <summary>
    /// Disconnected from the server as a client
    /// </summary>
    public ConnectionEvent onClientDisconnect;

    /// <summary>
    /// Client has connected to my server
    /// </summary>
    public ConnectionEvent onServerConnect;

    /// <summary>
    /// Client has disconnected from my server
    /// </summary>
    public ConnectionEvent onServerDisconnect;

    /// <summary>
    /// Server has been started
    /// </summary>
    public BasicEvent onServerStarted;

    /// <summary>
    /// Player was added on the server
    /// </summary>
    public ConnectionEvent onServerAddPlayer;

    public int transportPort
    {
        get
        {
            switch (transport)
            {
                case kcp2k.KcpTransport asKcp:
                    return asKcp.Port;
                case TelepathyTransport asTelepathy:
                    return asTelepathy.port;
            }

            // other known transports, we can't access these directly without introducing more dependencies... it's horrible. Oh well
            // this works for Ignorance (http://github.com/SoftwareGuy/Ignorance)
            FieldInfo portFieldIgnorance = transport.GetType().GetField("port");
            if (portFieldIgnorance != null && portFieldIgnorance.FieldType == typeof(int))
            {
                return (int)portFieldIgnorance.GetValue(transport);
            }

            // unknown, or not supported. lol, sup port
            return -1;
        }
        set
        {
            switch (transport)
            {
                case kcp2k.KcpTransport asKcp:
                    asKcp.Port = (ushort)value;
                    break;
                case TelepathyTransport asTelepathy:
                    asTelepathy.port = (ushort)value;
                    break;
            }

            FieldInfo portFieldIgnorance = transport.GetType().GetField("port");
            if (portFieldIgnorance != null && portFieldIgnorance.FieldType == typeof(int))
            {
                portFieldIgnorance.SetValue(transport, value);
            }
        }
    }

    public override void Awake()
    {
        base.Awake();

        singleton = this;
        transform.SetParent(null, false);
        DontDestroyOnLoad(gameObject);
    }

    public void Host(bool withLocalPlayer)
    {
        bool useNullOnlineScene = false;
        if (string.IsNullOrEmpty(onlineScene))
        {
            // we basically need to do this for the command line-based level selection to work effectively
            // bugs will occur if we don't inform ourselves somehow that we're starting in this scene
            onlineScene = SceneManager.GetActiveScene().path;
            useNullOnlineScene = true;
        }

        if (withLocalPlayer)
            StartHost();
        else
            StartServer();

        if (useNullOnlineScene)
        {
            onlineScene = null;
            networkSceneName = SceneManager.GetActiveScene().path;
        }
    }

    public void Connect(string ip)
    {
        if (ip.Contains(":"))
        {
            networkAddress = ip.Substring(0, ip.IndexOf(":"));
            int port;

            if (int.TryParse(ip.Substring(ip.IndexOf(":") + 1), out port))
            {
                transportPort = port;
            }
            else
            {
                Debug.LogWarning($"Could not read port {ip.Substring(ip.IndexOf(":") + 1)}, using default of {transportPort}.");
            }
        }
        else
        {
            networkAddress = ip;
        }
        
        StartClient();
    }

    public override void OnClientConnect()
    {
        base.OnClientConnect();
        onClientConnect?.Invoke(NetworkClient.connection);

        if (enableVerboseLogging)
            Debug.Log("[NetMan] Connected to server!");
    }

    public override void OnClientDisconnect()
    {
        base.OnClientDisconnect();
        onClientDisconnect?.Invoke(NetworkClient.connection);

        StopClient();

        if (enableVerboseLogging)
            Debug.Log("[NetMan] Disconnected from server!");
    }

    public override void OnServerConnect(NetworkConnection conn)
    {
        base.OnServerConnect(conn);
        onServerConnect?.Invoke(conn);

        if (enableVerboseLogging)
            Debug.Log($"[NetMan] Client {conn.address} has connected!");
    }

    public override void OnServerDisconnect(NetworkConnection conn)
    {
        onServerDisconnect?.Invoke(conn);

        if (enableVerboseLogging)
            Debug.Log($"[NetMan] Client {conn.address} has disconnected!");

        base.OnServerDisconnect(conn);
    }

    public override void OnStartHost()
    {
        base.OnStartHost();

        if (enableVerboseLogging)
            Debug.Log("[NetMan] Started host!");
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        if (gameStatePrefab != null)
        {
            GameObject serverState = Instantiate(gameStatePrefab).gameObject;
            NetworkServer.Spawn(serverState);
        }

        onServerStarted?.Invoke();

        if (enableVerboseLogging)
            Debug.Log("[NetMan] Started server!");
    }

    public override void OnStopServer()
    {
        if (GameState.singleton != null)
        {
            NetworkServer.UnSpawn(GameState.singleton.gameObject);
            Destroy(GameState.singleton.gameObject);
        }

        base.OnStopServer();

        if (enableVerboseLogging)
            Debug.Log("[NetMan] Stopped server!");
    }

    public override void OnStopHost()
    {
        if (GameState.singleton != null)
        {
            NetworkServer.UnSpawn(GameState.singleton.gameObject);
            Destroy(GameState.singleton.gameObject);
        }

        base.OnStopHost();

        if (enableVerboseLogging)
            Debug.Log("[NetMan] Stopped host!");
    }

    public override void OnServerAddPlayer(NetworkConnection conn)
    {
        base.OnServerAddPlayer(conn);
        onServerAddPlayer?.Invoke(conn);

        if (enableVerboseLogging)
            Debug.Log($"[NetMan] Added player for {conn.address}");
    }
}
