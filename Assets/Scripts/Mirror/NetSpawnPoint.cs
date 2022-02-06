using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A spawn point for players, 
/// </summary>
public class NetSpawnPoint : MonoBehaviour
{
    /// <summary>
    /// Per-level spawner index
    /// </summary>
    public int spawnerIndex;

    public static Dictionary<int, NetSpawnPoint> spawnPoints = new Dictionary<int, NetSpawnPoint>();

    private void Awake()
    {
        // Note: Additive scene support would require some kind of scene ID to be included along with the ID, perhaps not too difficult
        if (spawnPoints.ContainsKey(spawnerIndex) && spawnPoints[spawnerIndex] != null)
            Debug.LogWarning($"[MultiplayerEssentials] {gameObject.name} is replacing old spawn point at {spawnerIndex}: {spawnPoints[spawnerIndex]}. This won't normally happen (if additive scenes are being used, those aren't supported by this yet).");

        spawnPoints[spawnerIndex] = this;
    }

#if UNITY_EDITOR
    // Make sure spawned IDs are sorted on the next frame during validation
    private void OnValidate()
    {
        UnityEditor.EditorApplication.update -= RefreshSpawnerIds;
        UnityEditor.EditorApplication.update += RefreshSpawnerIds;
    }

    /// <summary>
    /// Refreshes and reorders spawner IDs, should happen during editor time
    /// </summary>
    private static void RefreshSpawnerIds()
    {
        NetSpawnPoint[] spawnPoints = FindObjectsOfType<NetSpawnPoint>();

        System.Array.Sort(spawnPoints, (a, b) => (a.GetInstanceID() - b.GetInstanceID()));

        for (int i = 0; i < spawnPoints.Length; i++)
        {
            if (spawnPoints[i].spawnerIndex != i)
            {
                UnityEditor.EditorUtility.SetDirty(spawnPoints[i]);

                spawnPoints[i].spawnerIndex = i;
            }

            // todo allow user to edit names, strip & append this info to the name
            string label = $"NetSpawnPoint {i}";

            var test = System.Text.RegularExpressions.Regex.Match(spawnPoints[i].name, @"(.*) \[(.*)\]");

            if (test.Groups.Count < 3)
            {
                spawnPoints[i].name += $" [{label}]";
            }
            else if (test.Groups[test.Groups.Count - 1].Value != label)
            {
                spawnPoints[i].name = spawnPoints[i].name.Replace(test.Groups[test.Groups.Count - 1].Value, label);
            }
            continue;
        }

        UnityEditor.EditorApplication.update -= RefreshSpawnerIds;
    }
#endif
}
