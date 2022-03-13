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
        get => SessionState.GetString("_playModeCommandLineParms", "");
        set => SessionState.SetString("_playModeCommandLineParms", value);
    }

    public const string kPlaymodeMenu = "Multiplayer/Playmode/";
    public const int kPlaymodePrio = PlaytestTools.kBuildTypePrio + 10;

    [InitializeOnLoadMethod]
    private static void OnEditorInit()
    {
        EditorSceneManager.activeSceneChangedInEditMode += (Scene scn, Scene scn2) =>
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                ReassignBootScene();
            }
        };
        EditorSceneManager.sceneLoaded += (Scene scn, LoadSceneMode loadMode) =>
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                ReassignBootScene();
            }
        };

        // Be prepared to set editor commands on play mode
        EditorApplication.playModeStateChanged += OnPlayStateChanged;

        // To cover when user changes the boot scene. The boot scene is always assumed to be scene 0.
        EditorBuildSettings.sceneListChanged += ReassignBootScene;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void OnGameInit()
    {
        // Set default command parameters if they haven't been set
        UpdateEditorCommands();
    }
    
    private static void ReassignBootScene()
    {
        // before starting, make sure the boot scene loads first
        if (playModeCommandType != PlayModeCommands.Disabled && EditorBuildSettings.scenes.Length > 0)
        {
            SceneAsset bootScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(EditorBuildSettings.scenes[0].path);

            if (EditorSceneManager.playModeStartScene == null || EditorSceneManager.playModeStartScene.name != bootScene.name)
            {
                Debug.Log($"[PlaymodeTools]: Boot scene set to {bootScene.name}");
                EditorSceneManager.playModeStartScene = bootScene;
            }
        }
        else if (EditorSceneManager.playModeStartScene != null)
        {
            Debug.Log($"[PlaymodeTools]: Cleared boot scene");
            EditorSceneManager.playModeStartScene = null;
        }
    }

    private static void OnPlayStateChanged(PlayModeStateChange change)
    {
        // when finished playing, do not let previous editor commands persist to the next test
        if (change == PlayModeStateChange.ExitingPlayMode)
            CommandLine.editorCommands = "";
        if (change == PlayModeStateChange.ExitingEditMode)
            UpdateEditorCommands();

    }

    /// <summary>
    /// Sets the editor commands before scene load during game run, unless they've already been set by something else
    /// </summary>
    private static void UpdateEditorCommands()
    {
        // Avoid overriding Playtest menu settings
        if (CommandLine.editorCommands == null || CommandLine.editorCommands.Length == 0)
        {
            switch (playModeCommandType)
            {
                case PlayModeCommands.Host:
                    CommandLine.editorCommands = $"-host -scene \"{EditorSceneManager.GetActiveScene().path}\"";
                    break;
                case PlayModeCommands.Connect:
                    CommandLine.editorCommands = $"-connect 127.0.0.1";
                    break;
                case PlayModeCommands.Custom:
                    CommandLine.editorCommands = playModeCommandLine;
                    break;
                case PlayModeCommands.Disabled:
                    CommandLine.editorCommands = "";
                    break;
            }

            Debug.Log($"Running PlayMode command line: {CommandLine.editorCommands}");
        }
    }

    [MenuItem(kPlaymodeMenu + "None", false, kPlaymodePrio)]
    static void DontUseCommands()
    {
        playModeCommandType = PlayModeCommands.Disabled;
        ReassignBootScene();
    }

    [MenuItem(kPlaymodeMenu + "None", validate = true)]
    static bool DontUseCommandsValidate()
    {
        Menu.SetChecked(kPlaymodeMenu + "None", playModeCommandType == PlayModeCommands.Disabled);
        return true;
    }

    [MenuItem(kPlaymodeMenu + "Host", false, kPlaymodePrio+1)]
    static void AutoHostOutsideBoot()
    {
        playModeCommandType = PlayModeCommands.Host;
        ReassignBootScene();
    }

    [MenuItem(kPlaymodeMenu + "Host", validate = true)]
    static bool AutoHostOutsideBootValidate()
    {
        Menu.SetChecked(kPlaymodeMenu + "Host", playModeCommandType == PlayModeCommands.Host);
        return true;
    }


    [MenuItem(kPlaymodeMenu + "Connect", false, kPlaymodePrio+2)]
    static void AutoConnectOutsideBoot()
    {
        playModeCommandType = PlayModeCommands.Connect;
        ReassignBootScene();
    }

    [MenuItem(kPlaymodeMenu + "Connect", validate = true)]
    static bool AutoConnectOutsideBootValidate()
    {
        Menu.SetChecked(kPlaymodeMenu + "Connect", playModeCommandType == PlayModeCommands.Connect);
        return true;
    }

    [MenuItem(kPlaymodeMenu + "Custom command line...", false, kPlaymodePrio+3)]
    static void SetCustomCommands()
    {
        DefaultCommandLineBox window = DefaultCommandLineBox.CreateInstance<DefaultCommandLineBox>();
        window.position = new Rect(Screen.width / 2, Screen.height / 2, 250, 150);
        window.tempCommands = playModeCommandLine;
        playModeCommandType = PlayModeCommands.Custom;
        window.ShowUtility();
        UpdateEditorCommands();
    }

    [MenuItem(kPlaymodeMenu + "Custom command line...", validate = true)]
    static bool SetCustomCommandsValidate()
    {
        Menu.SetChecked(kPlaymodeMenu + "Custom command line...", playModeCommandType == PlayModeCommands.Custom);
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
                PlaymodeTools.UpdateEditorCommands();
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