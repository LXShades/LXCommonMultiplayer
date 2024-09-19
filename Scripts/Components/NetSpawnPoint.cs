using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A spawn point for players, 
/// </summary>
public class NetSpawnPoint : MonoBehaviour
{
    /// <summary>
    /// All spawn points currently available, by index. May contain null gaps
    /// </summary>
    public static SortedDictionary<int, NetSpawnPoint> spawnPoints = new SortedDictionary<int, NetSpawnPoint>();

    /// <summary>
    /// Per-level spawner index
    /// </summary>
    public int spawnerIndex;

    // Temporary precise hierarchy position of this spawner used to sort the spawn points consistently
    private float spawnerHierarchyPosition;

    private void OnEnable()
    {
        // Note: Additive scene support would require some kind of scene ID to be included along with the ID, perhaps not too difficult
        if (spawnPoints.TryGetValue(spawnerIndex, out NetSpawnPoint currentSpawnPointAtIndex))
            Debug.LogWarning($"[MultiplayerEssentials] {gameObject.name} is replacing old spawn point at {spawnerIndex}: {currentSpawnPointAtIndex.gameObject.name}. This shouldn't normally happen and could be caused by additive scenes (currently unsupported here).");

        spawnPoints[spawnerIndex] = this;
    }

    private void OnDisable()
    {
        if (spawnPoints.TryGetValue(spawnerIndex, out NetSpawnPoint currentSpawnPointAtIndex) && currentSpawnPointAtIndex == this)
            spawnPoints.Remove(spawnerIndex);
        else
            Debug.LogWarning($"[MultiplayerEssentials] {gameObject.name} was not in the spawn point list, expected index {spawnerIndex}. This shouldn't normally happen and could be caused by additive scenes (currently unsupported here).");

    }

    /// <summary>
    /// Tries to find a spawn point for the given index, or the next closest spawn point.
    /// This corresponds to the ascending order of the spawner indexes, but might not correspond with the actual spawner index itself.
    /// </summary>
    public static NetSpawnPoint FindSpawnPointForOrderedIndex(int index)
    {
        int numValidIndexes = 0;
        foreach (var kvp in spawnPoints)
        {
            if (kvp.Value != null)
                numValidIndexes++;
        }

        if (numValidIndexes > 0)
        {
            index %= numValidIndexes;

            int searchIndex = 0;
            foreach (var kvp in spawnPoints)
            {
                if (kvp.Value != null)
                {
                    if (searchIndex == index)
                        return kvp.Value;

                    searchIndex++;
                }
            }
        }

        return null;
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

        for (int i = 0; i < spawnPoints.Length; i++)
            spawnPoints[i].RecalculateHierarchyPosition();

        System.Array.Sort(spawnPoints, (a, b) => (a.spawnerHierarchyPosition > b.spawnerHierarchyPosition ? 1 : -1));

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

    private static List<Transform> transformChain = new List<Transform>();
    private void RecalculateHierarchyPosition()
    {
        transformChain.Clear();

        for (Transform t = transform; t != null;  t = t.parent)
            transformChain.Add(t);

        float lastMultiplier = 1f;

        spawnerHierarchyPosition = 0f;

        // Set our position in such a way that its floating-point value corresponds to its position in the hierarchy with increasing depth corresponding to increasing precision
        for (int i = transformChain.Count - 1; i >= 0; i--)
        {
            float nextMultiplier = i < transformChain.Count - 1 ? 1f / transformChain[i + 1].GetSiblingIndex() : 1f;

            nextMultiplier *= lastMultiplier;
            lastMultiplier = nextMultiplier;

            spawnerHierarchyPosition += transformChain[i].GetSiblingIndex() * lastMultiplier;
        }
    }
#endif
}
