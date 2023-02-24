using Mirror;
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A NetPlayer is a player in the game, but not necessarily its character.
/// 
/// The player can create the character and/or control it. The reason for the separation is persistence, where players aim to retain their information across maps, such as name.
/// </summary>
public class NetPlayer : NetworkBehaviour
{
    [Header("Player setup")]
    [Tooltip("Character to spawn when ready")]
    public NetworkIdentity characterPrefab;

    [Header("Realtime player info")]
    [Tooltip("The name of this player")]
    [SyncVar]
    public string playerName;

    [Tooltip("Unique ID of this player matching its position in the players dictionary")]
    [SyncVar]
    public int playerId;

    [Tooltip("Character owned by this player")]
    [SyncVar]
    public uint characterNetId;

    [Header("Spawn behaviour")]
    [Tooltip("Automatically positions the character when spawning them")]
    public bool automaticallyPositionCharacters = true;

    public GameObject character => NetworkIdentity.spawned.GetValueOrDefault(characterNetId)?.gameObject;

    /// <summary>
    /// Dictionary of all players currently known in the game by ID
    /// </summary>
    public static Dictionary<int, NetPlayer> playerById = new Dictionary<int, NetPlayer>();

    /// <summary>
    /// ALWAYS NULL CHECK EACH PLAYER! List of all players currently known in the game. May contain null gaps and order is not guaranteed
    /// </summary>
    public static List<NetPlayer> players = new List<NetPlayer>();

    /// <summary>
    /// Returns the local NetPlayer if applicable
    /// </summary>
    public static NetPlayer localPlayer;

    /// <summary>
    /// Returns the local NetPlayer's character if available
    /// </summary>
    public static GameObject localCharacter => localPlayer ? localPlayer.character : null;

    /// <summary>
    /// [Server/client] Called whenever the local player character is spawned by a NetPlayer
    /// </summary>
    public static event Action<NetPlayer, GameObject> onAssignedLocalCharacter;

    /// <summary>
    /// [Server] Called whenever any player character is spawned by a NetPlayer
    /// </summary>
    public static event Action<NetPlayer, GameObject> onServerSpawnedCharacter;

    /// <summary>
    /// [Server, client] Called when a player is added and OnStartServer/client called. Use RegisterOnPlayerAdded
    /// </summary>
    private static event Action<NetPlayer> onPlayerAdded;

    /// <summary>
    /// [Server, client] Called when a player is destroyed
    /// </summary>
    public static event Action<NetPlayer> onPlayerDestroyed;

    /// <summary>
    /// Default set of player names
    /// I'm in a pizza mood
    /// </summary>
    private static string[] defaultPlayerNames = { "Pepperoni", "Veggie Supreme", "Nduja", "Meat Feast", "BBQ Chicken", "Hawaiian", "Pineapple", "Margharita", "Buffalo Chicken", "Diavolo", "Pancetta", "American Style", "Sloppy Giuseppe", "Three Cheese" };

    /// <summary>
    /// Extensions used when two player names match
    /// </summary>
    private static string[] defaultPlayerNameExtensions = { "Spicy", "Discount", "Vegan", "1/2", "Leggera", "Extra Hot" };
    // [unused] Military ranks: { "Cadet", "Junior", "Freshman", "Rookie", "Minor", "Baby" };

    /// <summary>
    /// Adds player to the player list
    /// </summary>
    public override void OnStartClient()
    {
        base.OnStartClient();

        if (!NetworkServer.active) // host player will do this in OnStartServer, don't do it twice
        {
            playerById[playerId] = this;
            players.Add(this);

            DontDestroyOnLoad(gameObject);

            onPlayerAdded?.Invoke(this);
        }
    }

    /// <summary>
    /// Sets up player name, ID and character
    /// </summary>
    public override void OnStartServer()
    {
        base.OnStartServer();

        playerId = FindNewPlayerId();
        playerName = ValidateName(FindNewPlayerName());
        playerById[playerId] = this;
        players.Add(this);

        DontDestroyOnLoad(gameObject);

        Debug.Log($"[MultiplayerEssentials] Created player {playerName}");

        if (characterPrefab)
        {
            var identity = Instantiate(characterPrefab);
            NetworkServer.Spawn(identity.gameObject, this.connectionToClient); // note: need to call Spawn before assigning Character, as net ID needs initialising for character SyncVar
            characterNetId = identity.netId;

            NetSpawnPoint spawnPoint = NetSpawnPoint.FindSpawnPointForOrderedIndex(playerId);
            if (spawnPoint != null)
            {
                character.transform.position = spawnPoint.transform.position;
                character.transform.rotation = spawnPoint.transform.rotation;
            }
            else
            {
                Debug.LogWarning("[MultiplayerEssentials] No valid spawn points found, spawning character at 0.");
            }

            if (identity.TryGetComponent(out INetPlayerCharacter netPlayerCharacter))
                netPlayerCharacter.ServerOnFinishedSpawn(this);
            onServerSpawnedCharacter?.Invoke(this, character.gameObject);
        }
        else
        {
            Debug.LogWarning($"[MultiplayerEssentials] No character created for {playerName}, prefab missing.");
        }

        onPlayerAdded?.Invoke(this);
    }

    /// <summary>
    /// Assigns the local player
    /// </summary>
    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();

        localPlayer = this;
        if (character != null)
        {
            if (character.TryGetComponent(out INetPlayerCharacter netPlayerCharacter))
                netPlayerCharacter.OnAssignedLocal(this);
            onAssignedLocalCharacter?.Invoke(this, character.gameObject);
        }
    }

    public override void OnDeserialize(NetworkReader reader, bool initialState)
    {
        base.OnDeserialize(reader, initialState);
    }

    private void OnDestroy()
    {
        if (playerById.ContainsKey(playerId) && playerById[playerId] == this)
            playerById.Remove(playerId);
        players.Remove(this);

        onPlayerDestroyed?.Invoke(this);
    }

    /// <summary>
    /// Calls a callback when a player is added. If callOnExisting is true, the function is called for all players currently existing
    /// Make sure to deregister this when done, as it's a static delegate and could call on your dead object.
    /// </summary>
    public static void RegisterOnPlayerAdded(Action<NetPlayer> functionToCall, bool callOnExisting)
    {
        onPlayerAdded += functionToCall;

        if (callOnExisting)
        {
            foreach (NetPlayer player in NetPlayer.players)
            {
                if (player)
                    functionToCall?.Invoke(player);
            }
        }
    }

    public static void UnregisterOnPlayerAdded(Action<NetPlayer> functionToCall)
    {
        onPlayerAdded -= functionToCall;
    }

    public static NetPlayer FindPlayerForCharacter(GameObject characterObject)
    {
        foreach (var kvp in playerById)
        {
            if (kvp.Value && kvp.Value.character == characterObject)
            {
                return kvp.Value;
            }
        }
        return null;
    }

    public static NetPlayer FindPlayerForConnection(NetworkConnectionToClient connection)
    {
        foreach (var kvp in playerById)
        {
            if (kvp.Value && kvp.Value.connectionToClient == connection)
            {
                return kvp.Value;
            }
        }
        return null;
    }

    private static int FindNewPlayerId()
    {
        for (int i = 0; ; i++)
        {
            if (!playerById.ContainsKey(i) || playerById[i] == null)
            {
                return i;
            }
        }
    }

    private string FindNewPlayerName()
    {
        int initialId = UnityEngine.Random.Range(0, defaultPlayerNames.Length);

        if (DoesPlayerNameExist(defaultPlayerNames[initialId]))
        {
            for (int id = (initialId + 1) % defaultPlayerNames.Length; id != initialId; id = (id + 1) % defaultPlayerNames.Length)
            {
                if (!DoesPlayerNameExist(defaultPlayerNames[id]))
                    return defaultPlayerNames[id];
            }
        }

        // give up, return a random name that already exists and let the validation function modify it
        return defaultPlayerNames[initialId];
    }

    private string ValidateName(string inName)
    {
        string finalName = inName;

        if (DoesPlayerNameExist(finalName))
        {
            // Try adding up to 2 extensions to the name
            for (int i = 0; i < defaultPlayerNameExtensions.Length * defaultPlayerNameExtensions.Length; i++)
            {
                if (i < defaultPlayerNameExtensions.Length)
                    finalName = $"{defaultPlayerNameExtensions[i]} {inName}";
                else
                    finalName = $"{defaultPlayerNameExtensions[i / defaultPlayerNameExtensions.Length]} {defaultPlayerNameExtensions[i]} {inName}";

                if (!DoesPlayerNameExist(finalName))
                    break; // Found one
            }
        }

        return finalName;
    }

    private bool DoesPlayerNameExist(string inName)
    {
        foreach (var kvp in playerById)
        {
            if (kvp.Value != this && kvp.Value != null && kvp.Value.playerName == inName)
                return true;
        }

        return false;
    }
}

/// <summary>
/// Allows spawned characters to receive some events from the owning NetPlayer, if desired
/// </summary>
public interface INetPlayerCharacter
{
    /// <summary>
    /// Called by NetPlayer when finished spawning and positioning the character
    /// </summary>
    public void ServerOnFinishedSpawn(NetPlayer owner);

    /// <summary>
    /// Called by NetPlayer when the local player owner of the character becomes known
    /// </summary>
    public void OnAssignedLocal(NetPlayer owner);
}