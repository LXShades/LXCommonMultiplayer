using Mirror;

/// <summary>
/// Holds server settings, etc. Can be inherited. Spawned upon host, spawned on clients when available.
/// </summary>
public class ServerStateBase : NetworkBehaviour
{
    /// <summary>
    /// Single instance of the ServerState. You may want to make an inherited version
    /// </summary>
    public static ServerStateBase instance
    {
        get => _instance;
    }
    protected static ServerStateBase _instance;

    public override void OnStartClient()
    {
        base.OnStartClient();

        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        _instance = this;
        DontDestroyOnLoad(gameObject);
    }
}
