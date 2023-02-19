using Mirror;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(Spawner))]
public class SpawnerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        Spawner spawner = target as Spawner;
        if (GUILayout.Button("Scan and Add Prefabs in Folders"))
        {
            string[] prefabs = AssetDatabase.FindAssets($"t:prefab");
            bool isSpawnerDirty = false;

            foreach (string prefabGuid in prefabs)
            {
                string prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuid);
                string prefabPathLower = prefabPath.ToLower();
                bool isInCorrectFolder = spawner.prefabSearchFolders.Length == 0;

                // Include file if it's in the search folder
                foreach (string searchFolder in spawner.prefabSearchFolders)
                {
                    if (prefabPathLower.StartsWith(searchFolder.ToLower()))
                    {
                        isInCorrectFolder = true;
                        break;
                    }
                }

                // But exclude it if it's it's also in the exclude folder
                if (isInCorrectFolder)
                {
                    foreach (string excludeFolder in spawner.prefabExcludeFolders)
                    {
                        if (prefabPathLower.StartsWith(excludeFolder.ToLower()))
                        {
                            isInCorrectFolder = false;
                            break;
                        }
                    }
                }

                if (isInCorrectFolder)
                {
                    GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

                    if (prefabAsset)
                    {
                        if (prefabAsset.GetComponent<NetworkIdentity>() != null)
                        {
                            if (!spawner.spawnablePrefabs.Contains(prefabAsset))
                            {
                                if (!isSpawnerDirty)
                                {
                                    isSpawnerDirty = true;
                                    Undo.RecordObject(spawner, "Scan spawner prefabs");
                                }

                                spawner.spawnablePrefabs.Add(prefabAsset);
                            }
                        }
                    }
                }
            }

            if (isSpawnerDirty)
            {
                EditorUtility.SetDirty(spawner);
            }
        }

        if (GUILayout.Button("Clear Prefab List"))
        {
            if (spawner.spawnablePrefabs.Count > 0)
            {
                Undo.RecordObject(spawner, "Clear spawner prefabs");
                spawner.spawnablePrefabs.Clear();
                EditorUtility.SetDirty(spawner);
            }
        }
    }
}
