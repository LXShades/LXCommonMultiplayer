using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

public class NetMan : NetworkManager
{
    public static new NetMan singleton { get; private set; }

    private int defaultPort;

    public delegate void ConnectionEvent(NetworkConnection connection);
    public delegate void BasicEvent();

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

    private int transportPort
    {
        get
        {
            switch (transport)
            {
                case IgnoranceTransport.Ignorance asIgnorance:
                    return asIgnorance.port;
                case kcp2k.KcpTransport asKcp:
                    return asKcp.Port;
                case TelepathyTransport asTelepathy:
                    return asTelepathy.port;
            }
            return -1;
        }
        set
        {
            switch (transport)
            {
                case IgnoranceTransport.Ignorance asIgnorance:
                    asIgnorance.port = value;
                    break;
                case kcp2k.KcpTransport asKcp:
                    asKcp.Port = (ushort)value;
                    break;
                case TelepathyTransport asTelepathy:
                    asTelepathy.port = (ushort)value;
                    break;
            }
        }
    }

    public override void Awake()
    {
        base.Awake();

        singleton = this;
        defaultPort = transportPort;
        transform.SetParent(null, false);
        DontDestroyOnLoad(gameObject);
    }

    public void Host(bool withLocalPlayer, int port = -1)
    {
        if (port != -1)
            transportPort = port;
        else
            transportPort = defaultPort;

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
                Debug.LogWarning($"Could not read port {ip.Substring(ip.IndexOf(":") + 1)}, using default of {defaultPort}.");
                transportPort = defaultPort;
            }
        }
        else
        {
            networkAddress = ip;
            transportPort = defaultPort;
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
        onServerStarted?.Invoke();
    }

    public override void OnServerAddPlayer(NetworkConnection conn)
    {
        base.OnServerAddPlayer(conn);
        onServerAddPlayer?.Invoke(conn);
    }
}
