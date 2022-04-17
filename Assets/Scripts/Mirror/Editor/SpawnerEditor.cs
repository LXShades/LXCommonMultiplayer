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
                bool isInCorrectFolder = spawner.prefabSearchFolders.Length == 0;

                foreach (string searchFolder in spawner.prefabSearchFolders)
                {
                    if (prefabPath.ToLower().StartsWith(searchFolder.ToLower()))
                    {
                        isInCorrectFolder = true;
                        break;
                    }
                }

                if (isInCorrectFolder)
                {
                    GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

                    if (prefabAsset)
                    {
                        if (prefabAsset.GetComponent<NetworkBehaviour>() != null)
                        {
                            if (!spawner.spawnablePrefabs.Contains(prefabAsset))
                            {
                                if (!isSpawnerDirty)
                                {
                                    isSpawnerDirty = true;
                                    Undo.RecordObject(spawner, "Scan Prefabs");
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
    }
}
