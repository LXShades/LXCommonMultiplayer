using Mirror;
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
    public GameObject character;

    /// <summary>
    /// List of all players currently known in the game
    /// </summary>
    public static Dictionary<int, NetPlayer> players = new Dictionary<int, NetPlayer>();

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

        players[playerId] = this;

        DontDestroyOnLoad(gameObject);
    }

    /// <summary>
    /// Sets up player name, ID and character
    /// </summary>
    public override void OnStartServer()
    {
        base.OnStartServer();

        playerId = FindNewPlayerId();
        playerName = ValidateName(FindNewPlayerName());
        players[playerId] = this;

        DontDestroyOnLoad(gameObject);

        Debug.Log($"[MultiplayerEssentials] Created player {playerName}");

        if (characterPrefab)
        {
            var identity = Instantiate(characterPrefab);
            NetworkServer.Spawn(identity.gameObject, this.gameObject); // note: need to call Spawn before assigning Character, as net ID needs initialising for character SyncVar
            character = identity.gameObject;
        }
        else
        {
            Debug.Log($"[MultiplayerEssentials] No character created for {playerName}, prefab missing.");
        }
    }

    private void OnDestroy()
    {
        if (players.ContainsKey(playerId) && players[playerId] == this)
            players.Remove(playerId);
    }

    public static NetPlayer FindPlayerForCharacter(GameObject characterObject)
    {
        foreach (var kvp in players)
        {
            if (kvp.Value && kvp.Value.character == characterObject)
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
            if (!players.ContainsKey(i) || players[i] == null)
            {
                return i;
            }
        }
    }

    private string FindNewPlayerName()
    {
        int initialId = Random.Range(0, defaultPlayerNames.Length);

        if (DoesPlayerNameExist(defaultPlayerNames[initialId]))
        {
            for (int id = initialId + 1; id != initialId; id = (id + 1) % defaultPlayerNames.Length)
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
        foreach (var kvp in players)
        {
            if (kvp.Value != this && kvp.Value != null && kvp.Value.playerName == inName)
                return true;
        }

        return false;
    }
}