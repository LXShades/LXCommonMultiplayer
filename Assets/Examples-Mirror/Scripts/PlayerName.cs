using Mirror;

/// <summary>
/// Holds the player's name and assigns it to the GameObject
/// </summary>
public class PlayerName : NetworkBehaviour
{
    [SyncVar(hook = nameof(AssignName))]
    private string playerName;

    public override void OnStartServer()
    {
        base.OnStartServer();

        if (isLocalPlayer)
            AssignName("", "Host");
        else
            AssignName("", $"{connectionToClient.address}");
    }

    public void AssignName(string oldName, string newName)
    {
        playerName = newName;
        gameObject.name = $"[Player] {newName} {(isLocalPlayer ? "(local)" : "")}";
    }
}
