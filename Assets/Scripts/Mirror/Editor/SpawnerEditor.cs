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
            spawner.Editor_SearchAssetsAndRepopulatePrefabs();
        }

        if (GUILayout.Button("Clear Prefab List"))
            spawner.Editor_Clear();
    }
}
