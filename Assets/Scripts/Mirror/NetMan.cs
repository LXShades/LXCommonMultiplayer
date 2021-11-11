using Mirror;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

public class NetMan : NetworkManager
{
    public static new NetMan singleton { get; private set; }

    public delegate void ConnectionEvent(NetworkConnection connection);
    public delegate void BasicEvent();

    [Header("UnityMultiplayerEssentials")]
    /// <summary>
    /// Auto-created object that can hold server state and settings
    /// </summary>
    public ServerStateBase serverStatePrefab;

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

    public override void OnClientConnect(NetworkConnection conn)
    {
        base.OnClientConnect(conn);
        onClientConnect?.Invoke(conn);
    }

    public override void OnClientDisconnect(NetworkConnection conn)
    {
        base.OnClientDisconnect(conn);
        onClientDisconnect?.Invoke(conn);

        StopClient();
    }

    public override void OnServerConnect(NetworkConnection conn)
    {
        base.OnServerConnect(conn);
        onServerConnect?.Invoke(conn);
    }

    public override void OnServerDisconnect(NetworkConnection conn)
    {
        onServerDisconnect?.Invoke(conn);
        base.OnServerDisconnect(conn);
    }

    public override void OnStartHost()
    {
        base.OnStartHost();
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        if (serverStatePrefab != null)
        {
            GameObject serverState = Instantiate(serverStatePrefab).gameObject;
            NetworkServer.Spawn(serverState);
        }

        onServerStarted?.Invoke();
    }

    public override void OnStopServer()
    {
        if (ServerStateBase.instance != null)
        {
            NetworkServer.UnSpawn(ServerStateBase.instance.gameObject);
            Destroy(ServerStateBase.instance.gameObject);
        }

        base.OnStopServer();
    }

    public override void OnStopHost()
    {
        if (ServerStateBase.instance != null)
        {
            NetworkServer.UnSpawn(ServerStateBase.instance.gameObject);
            Destroy(ServerStateBase.instance.gameObject);
        }

        base.OnStopHost();
    }

    public override void OnServerAddPlayer(NetworkConnection conn)
    {
        base.OnServerAddPlayer(conn);
        onServerAddPlayer?.Invoke(conn);
    }
}
