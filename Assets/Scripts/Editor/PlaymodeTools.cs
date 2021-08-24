using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools to decide how the editor loads in playmode
/// 
/// Typically this loads the Boot scene - defined as scene 0 - and passes a fake command line for CommandLine to pick up in play mode, 
/// which changes the scene and potentially hosts or connects.
/// </summary>
[InitializeOnLoad]
public static class PlaymodeTools
{
    private enum PlayModeCommands
    {
        Disabled,
        Host,
        Connect,
        Custom
    }

    private static PlayModeCommands playModeCommandType
    {
        get => (PlayModeCommands)EditorPrefs.GetInt("_playModeCommands", (int)PlayModeCommands.Disabled);
        set => EditorPrefs.SetInt("_playModeCommands", (int)value);
    }

    private static string playModeCommandLine
    {
        get => EditorPrefs.GetString("_playModeCommandLineParms", "");
        set => EditorPrefs.SetString("_playModeCommandLineParms", value);
    }

    [InitializeOnLoadMethod]
    private static void OnInit()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        ReloadBootSceneAsStartScene();

        EditorSceneManager.sceneOpened += (Scene scn, OpenSceneMode mode) =>
        {
            ReloadBootSceneAsStartScene();

            // Change editor commands to load this current scene
            UpdateEditorCommands();
        };

        // Be prepared to set editor commands on play mode
        EditorApplication.playModeStateChanged += OnPlayStateChanged;

        // To cover when user changes the boot scene. The boot scene is always assumed to be scene 0.
        EditorBuildSettings.sceneListChanged += ReloadBootSceneAsStartScene;

        // Set default command parameters
        UpdateEditorCommands();
    }
    
    private static void ReloadBootSceneAsStartScene()
    {
        // before starting, make sure the boot scene loads first
        if (playModeCommandType != PlayModeCommands.Disabled && EditorBuildSettings.scenes.Length > 0)
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(EditorBuildSettings.scenes[0].path);
        else
            EditorSceneManager.playModeStartScene = null;
    }

    private static void OnPlayStateChanged(PlayModeStateChange change)
    {
        // when finished playing, do not let previous editor commands persist to the next test
        if (change == PlayModeStateChange.ExitingPlayMode)
            CommandLine.editorCommands = new string[0];
    }

    private static void UpdateEditorCommands()
    {
        string[] editorCommands = new string[0];

        switch (playModeCommandType)
        {
            case PlayModeCommands.Host:
                int currentSceneIndex = SceneManager.GetActiveScene().buildIndex;

                if (currentSceneIndex == -1)
                {
                    Debug.LogError($"Cannot Host in this scene: it is not in the build settings, so we can't load Boot and transfer to this one. Add {SceneManager.GetActiveScene().name} to the build settings to continue.");
                    EditorApplication.isPlaying = false;
                }

                editorCommands = new string[] { "-host", "-scene", currentSceneIndex.ToString() };
                break;
            case PlayModeCommands.Connect:
                editorCommands = new string[] { "-connect", "127.0.0.1" };
                break;
            case PlayModeCommands.Custom:
                editorCommands = playModeCommandLine.Split(' ');
                break;
            case PlayModeCommands.Disabled:
                break;
        }

        if ((EditorSceneManager.playModeStartScene == null) != (playModeCommandType == PlayModeCommands.Disabled))
            ReloadBootSceneAsStartScene();

        Debug.Log($"Setting PlayMode command line: {string.Join(" ", editorCommands)}");
        CommandLine.editorCommands = editorCommands;
    }

    [MenuItem("Playtest/Autohost in Playmode", false, 100)]
    static void AutoHostOutsideBoot()
    {
        playModeCommandType = PlayModeCommands.Host;
        UpdateEditorCommands();
    }

    [MenuItem("Playtest/Autohost in Playmode", validate = true)]
    static bool AutoHostOutsideBootValidate()
    {
        Menu.SetChecked("Playtest/Autohost in Playmode", playModeCommandType == PlayModeCommands.Host);
        return true;
    }


    [MenuItem("Playtest/Autoconnect in Playmode", false, 101)]
    static void AutoConnectOutsideBoot()
    {
        playModeCommandType = PlayModeCommands.Connect;
        UpdateEditorCommands();
    }

    [MenuItem("Playtest/Autoconnect in Playmode", validate = true)]
    static bool AutoConnectOutsideBootValidate()
    {
        Menu.SetChecked("Playtest/Autoconnect in Playmode", playModeCommandType == PlayModeCommands.Connect);
        return true;
    }

    [MenuItem("Playtest/Custom Playmode Commands...", false, 102)]
    static void SetCustomCommands()
    {
        DefaultCommandLineBox window = DefaultCommandLineBox.CreateInstance<DefaultCommandLineBox>();
        window.position = new Rect(Screen.width / 2, Screen.height / 2, 250, 150);
        window.tempCommands = playModeCommandLine;
        playModeCommandType = PlayModeCommands.Custom;
        window.ShowUtility();
        UpdateEditorCommands();
    }

    [MenuItem("Playtest/Custom Playmode Commands...", validate = true)]
    static bool SetCustomCommandsValidate()
    {
        Menu.SetChecked("Playtest/Custom Playmode Commands...", playModeCommandType == PlayModeCommands.Custom);
        return true;
    }

    [MenuItem("Playtest/No commands (Unity default)", false, 103)]
    static void DontUseCommands()
    {
        playModeCommandType = PlayModeCommands.Disabled;
        UpdateEditorCommands();
    }

    [MenuItem("Playtest/No commands (Unity default)", validate = true)]
    static bool DontUseCommandsValidate()
    {
        Menu.SetChecked("Playtest/No commands (Unity default)", playModeCommandType == PlayModeCommands.Disabled);
        return true;
    }

    /// <summary>
    /// Window to let the user assign custom command line parameters
    /// </summary>
    private class DefaultCommandLineBox : EditorWindow
    {
        public string tempCommands = "";

        void OnGUI()
        {
            GUILayout.Space(10);
            EditorGUILayout.LabelField("Type commands here!\n-host: Hosts a server with a local player\n-server: Hosts a server only\n-connect [ip]: Connects to the given IP address", EditorStyles.wordWrappedLabel);
            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            tempCommands = EditorGUILayout.TextField("Commands:", tempCommands);
            GUILayout.EndHorizontal();
            GUILayout.Space(20);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Done"))
            {
                PlaymodeTools.playModeCommandLine = tempCommands;
                Close();
            }
            if (GUILayout.Button("Cancel"))
            {
                Close();
            }
            EditorGUILayout.EndHorizontal();
        }
    }
}