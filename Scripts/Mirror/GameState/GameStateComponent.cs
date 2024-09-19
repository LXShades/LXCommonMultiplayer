using Mirror;

public class GameStateComponent : NetworkBehaviour
{
    protected virtual void OnEnable()
    {
        if (GameState.gameStateComponentAwaiters.TryGetValue(GetType(), out var gameStateAwaiters))
        {
            foreach (var awaiter in gameStateAwaiters)
            {
                if (awaiter.awaiter != null) // lifetime check basically
                    awaiter.onAvailableCallback?.Invoke(this);
            }

            gameStateAwaiters.Clear();
        }
    }

    public virtual void OnAwake() { }

    public virtual void OnUpdate() { }

    public virtual void OnStart() { }

    /// <summary>
    /// Called on server and client in this game mode when a player is created
    /// </summary>
    public virtual void OnPlayerStart(UnityEngine.GameObject player) { }

    /// <summary>
    /// Returns the names of the winning party(s)
    /// </summary>
    public virtual string GetWinners() => "";

}
