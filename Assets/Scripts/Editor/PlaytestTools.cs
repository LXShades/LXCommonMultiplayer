using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityAssembly = UnityEditor.Compilation.Assembly;

/// <summary>
/// The Playtest menu and tools for quickly building & playtesting networked games
/// </summary>
[InitializeOnLoad]
public class PlaytestTools : MonoBehaviour
{
    private struct AsmDefInfo
    {
        public string[] includePlatforms;
        public string[] excludePlatforms;
    }

    private enum EditorRole
    {
        None = 0,
        Host = 1,
        Server = 2,
        Client = 3
    }

    private enum BuildType
    {
        AllScenes = 0,
        CurrentScene = 1,
        ScriptsOnly = 2,
        AutoCompile = 3
    }

    public static int numTestPlayers
    {
        get => Mathf.Clamp(EditorPrefs.GetInt("playtestNumTestPlayers"), 1, 4);
        set => EditorPrefs.SetInt("playtestNumTestPlayers", value);
    }

    private static BuildType buildType
    {
        get => (BuildType)EditorPrefs.GetInt("playtestBuildType", (int)BuildType.AllScenes);
        set => EditorPrefs.SetInt("playtestBuildType", (int)value);
    }

    private static bool isWin64
    {
        get => EditorPrefs.GetBool("playtestIsWin64", true);
        set => EditorPrefs.SetBool("playtestIsWin64", value);
    }

    public static string playtestBuildPath => $"{Application.dataPath.Substring(0, Application.dataPath.LastIndexOf('/'))}/Builds/Playtest/{Application.productName}";
    public static string playtestBuildDataPath => $"{Application.dataPath.Substring(0, Application.dataPath.LastIndexOf('/'))}/Builds/Playtest/{Application.productName}/{Application.productName}_Data";

    public static string webGlBuildPath => $"{Application.dataPath.Substring(0, Application.dataPath.LastIndexOf('/'))}/Builds/WebGL/{Application.productName}";
    public static string linuxBuildPath => $"{Application.dataPath.Substring(0, Application.dataPath.LastIndexOf('/'))}/Builds/Linux/{Application.productName}";

    private static EditorRole editorRole
    {
        get { return (EditorRole)EditorPrefs.GetInt("editorRole"); }
        set { EditorPrefs.SetInt("editorRole", (int)value); }
    }

    private static BuildTarget playtestBuildTarget => isWin64 ? BuildTarget.StandaloneWindows64 : BuildTarget.StandaloneWindows;

    private static string playtestBuildTargetAsString => isWin64 ? "WindowsStandalone64" : "WindowsStandalone32"; // must be compatible with AssemblyDefinition info

    private static string[] pendingAssembliesForCompile
    {
        get => SessionState.GetString("playtestTools_pendingAssembliesForCompile", "").Split(new char[] { '¬' }, System.StringSplitOptions.RemoveEmptyEntries);
        set => SessionState.SetString("playtestTools_pendingAssembliesForCompile", string.Join("¬", value));
    }

    private const string kMultiplayerMenu = "Multiplayer/Playtest/";
    private const string kEditorRoleMenu = "Multiplayer/Playtest/";
    private const string kPlayerCountMenu = "Multiplayer/Playtest/";
    private const string kBuildTypeMenu = "Multiplayer/Playtest/";
    private const string kBuildPlatformMenu = "Multiplayer/Build Platform/";
    private const string kFinalBuildMenu = "Multiplayer/Final Build/";

    private const int kMultiplayerPrio = 10;
    private const int kPlayerCountPrio = 30;
    private const int kEditorRolePrio = 50;
    private const int kBuildTypePrio = 70;
    private const int kBuildPlatformPrio = 110;
    private const int kFinalBuildPrio = 130;


    [InitializeOnLoadMethod]
    public static void InitializePlaytestTools()
    {
        UnityEditor.Compilation.CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompiled;

        EditorApplication.update += OnEditorUpdate;
    }

    private static void OnEditorUpdate()
    {
        // Handle auto-compile functionality
        if (buildType != BuildType.AutoCompile || EditorApplication.isCompiling || !Directory.Exists(playtestBuildPath))
            return;

        if (pendingAssembliesForCompile.Length > 0)
        {
            UnityAssembly[] assemblies = UnityEditor.Compilation.CompilationPipeline.GetAssemblies();
            UnityAssembly targetAsm = System.Array.Find(assemblies,
                a => string.Compare(Path.GetFullPath(a.outputPath).TrimEnd('\\'), Path.GetFullPath(pendingAssembliesForCompile[0]).TrimEnd('\\'), System.StringComparison.InvariantCultureIgnoreCase) == 0);
            bool canRemoveAssemblyFromPending = true;

            try
            {
                if (targetAsm != null && (targetAsm.flags & UnityEditor.Compilation.AssemblyFlags.EditorAssembly) == 0) // don't include editor assemblies
                {
                    System.Reflection.Assembly loadedAsm = null;
                    loadedAsm = System.Array.Find(System.AppDomain.CurrentDomain.GetAssemblies(), a =>
                        string.Compare(Path.GetFullPath(a.Location).TrimEnd('\\'), Path.GetFullPath(pendingAssembliesForCompile[0]).TrimEnd('\\'), System.StringComparison.InvariantCultureIgnoreCase) == 0);

                    if (loadedAsm == null)
                        loadedAsm = System.Reflection.Assembly.ReflectionOnlyLoad(File.ReadAllBytes(targetAsm.outputPath)); // we'd prefer to use the one we've already loaded, but we'll just do this if not

                    // ===Send this assembly to the build, or if it contains editor references, recompile it and then send it to the build===
                    bool doesAssemblyContainEditorReferences = System.Array.Find(loadedAsm.GetReferencedAssemblies(), a => a.FullName.Contains("UnityEditor")) != null;
                    if (doesAssemblyContainEditorReferences)
                    {
                        var builder = new UnityEditor.Compilation.AssemblyBuilder($"{playtestBuildDataPath}/Managed/{targetAsm.name}.dll", targetAsm.sourceFiles);

                        builder.flags = UnityEditor.Compilation.AssemblyBuilderFlags.DevelopmentBuild;
                        builder.referencesOptions = UnityEditor.Compilation.ReferencesOptions.UseEngineModules;
                        builder.buildFinished += (string str, UnityEditor.Compilation.CompilerMessage[] messages) =>
                        {
                            int numErrors = 0;

                            for (int i = 0; i < messages.Length; i++)
                            {
                                if (messages[i].type == UnityEditor.Compilation.CompilerMessageType.Error)
                                    numErrors++;
                            }

                            if (numErrors == 0)
                            {
                                Debug.Log($"[PlaytestTools] Compiled {targetAsm.name} for playtest build!");

                                if (pendingAssembliesForCompile.Length > 1)
                                {
                                    Debug.Log($"[PlaytestTools]: {pendingAssembliesForCompile.Length} assemblies remaining... (note some may be skipped)");
                                }
                                else
                                {
                                    Debug.Log($"[PlaytestTools]: Playtest build is ready!");
                                }
                            }
                            else
                                Debug.LogWarning($"[PlaytestTools] Compiling {targetAsm.name} failed with {numErrors} error(s). This assembly might not be build-ready and possibly contains UnityEditor references.");
                        };

                        if (builder.Build())
                        {
                            Debug.Log($"[PlaytestTools] Recompiling {targetAsm.name} to the playtest build due to UnityEditor references in the editor-compiled version. Remove these if you'd like it to be copied directly and quickly.");
                        }
                        else
                        {
                            Debug.LogError($"[PlaytestTools] Cannot start building {targetAsm.name} due to unknown error. This can be caused by the editor already compiling.");
                            canRemoveAssemblyFromPending = false; // wait until we can build
                        }
                    }
                    else
                    {
                        Debug.Log($"[PlaytestTools]: Copying {targetAsm.name} to playtest build! (No editor references existed, so no recompile was required)");

                        File.Copy(targetAsm.outputPath, $"{playtestBuildDataPath}/Managed/{targetAsm.name}.dll", true);

                        if (pendingAssembliesForCompile.Length > 1)
                        {
                            Debug.Log($"[PlaytestTools]: {pendingAssembliesForCompile.Length} assemblies remaining... (note some may be skipped)");
                        }
                        else
                        {
                            Debug.Log($"[PlaytestTools]: Playtest build is ready!");
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }

            if (canRemoveAssemblyFromPending)
            {
                // assembly might have been deleted or something
                List<string> pending = new List<string>(pendingAssembliesForCompile);
                pending.RemoveAt(0);
                pendingAssembliesForCompile = pending.ToArray();
            }
        }
    }

    private static void OnAssemblyCompiled(string asmPath, UnityEditor.Compilation.CompilerMessage[] messages)
    {
        if (buildType != BuildType.AutoCompile)
            return;
        if (System.Array.FindIndex(messages, a => a.type == UnityEditor.Compilation.CompilerMessageType.Error) != -1)
            return; // compilation failed, don't do anything with it

        if (System.Array.IndexOf(pendingAssembliesForCompile, asmPath) == -1)
        {
            List<string> pending = new List<string>(pendingAssembliesForCompile);
            pending.Add(asmPath);
            pendingAssembliesForCompile = pending.ToArray();
        }
    }

    [MenuItem(kMultiplayerMenu+"Build", priority = kMultiplayerPrio)]
    public static bool Build()
    {
        // Big TODO here
        /*if (buildType == BuildType.CopyAssemblies)
        {
            List<string> skippedAsms = new List<string>();
            List<string> copiedAsms = new List<string>();

            foreach (string assetGuid in AssetDatabase.FindAssets("a:assets t:assemblydefinitionasset"))
            {
                AssemblyDefinitionAsset asmDef = (AssemblyDefinitionAsset)AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(assetGuid), typeof(AssemblyDefinitionAsset));

                if (asmDef != null)
                {
                    // Load the asmdef's info 
                    AsmDefInfo defInfo = JsonUtility.FromJson<AsmDefInfo>(asmDef.text);

                    if ((defInfo.includePlatforms.Length > 0 && System.Array.IndexOf(defInfo.includePlatforms, playtestBuildTargetAsString) == -1)
                        || (System.Array.IndexOf(defInfo.excludePlatforms, playtestBuildTargetAsString) != -1))
                        goto Skip; // no need to load the ASM, it's not needed in the build
                    else
                    {
                        string dllPath = $"{Application.dataPath}/../Library/ScriptAssemblies/{asmDef.name}.dll";

                        if (File.Exists(dllPath))
                        {
                            System.Reflection.Assembly loadedAsm = System.Reflection.Assembly.ReflectionOnlyLoadFrom(dllPath);
                            var referenced = loadedAsm.GetReferencedAssemblies();

                            foreach (System.Reflection.AssemblyName str in referenced)
                            {
                                if (str.FullName.Contains("UnityEditor"))
                                    goto Skip;
                            }

                            File.Copy(dllPath, $"{playtestBuildDataPath}/Managed/{asmDef.name}.dll", true);
                            copiedAsms.Add(asmDef.name);
                            continue;
                        }
                    }
                }

                Skip:;
                skippedAsms.Add(asmDef.name);
            }

            copiedAsms.Sort();
            skippedAsms.Sort();

            string copiedReport = "", skippedReport = "";
            foreach (string str in copiedAsms)
                copiedReport += $"{str}\n";
            foreach (string str in skippedAsms)
                skippedReport += $"{str}\n";

            EditorUtility.DisplayDialog("AssemblyCopy Report", $"===Copied===:\n{copiedReport}\n===Skipped===:\n{skippedReport}\nAssemblies are skipped when they contain UnityEditor references or are not needed for the playtest platform.", "Continue");

            return true;
        }
        else*/
        {
            // Add the open scene to the list if it's not already in there
            List<string> levels = new List<string>();
            string activeScenePath = EditorSceneManager.GetActiveScene().path;

            if (buildType == BuildType.CurrentScene)
            {
                if (EditorBuildSettings.scenes.Length > 0)
                    levels.Add(EditorBuildSettings.scenes[0].path); // add the Boot scene

                levels.Add(activeScenePath);
            }
            else
            {
                bool addOpenScene = true;
                for (int i = 0; i < EditorBuildSettings.scenes.Length; i++)
                {
                    if (EditorBuildSettings.scenes[i].enabled)
                    {
                        levels.Add(EditorBuildSettings.scenes[i].path);

                        if (EditorBuildSettings.scenes[i].path == activeScenePath)
                            addOpenScene = false;
                    }
                }

                // we haven't added this scene in the build settings, but we probably want to test it!
                if (addOpenScene)
                    levels.Add(activeScenePath);
            }

            // Build and run the player, preserving the open scene
            string originalScene = EditorSceneManager.GetActiveScene().path;

            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                string buildName = $"{playtestBuildPath}/{Application.productName}.exe";
                BuildOptions buildOptions = BuildOptions.Development
                    | (buildType == BuildType.ScriptsOnly ? BuildOptions.BuildScriptsOnly : 0);

                UnityEditor.Build.Reporting.BuildReport buildReport = BuildPipeline.BuildPlayer(levels.ToArray(), buildName, playtestBuildTarget, buildOptions);

                EditorSceneManager.OpenScene(originalScene);

                if (buildReport.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                {
                    EditorUtility.DisplayDialog("Someone goofed", $"Build failed ({buildReport.summary.totalErrors} errors)", "OK");
                    return false;
                }
                else
                {
                    return true;
                }
            }
            else
            {
                return false;
            }
        }
    }

    [MenuItem(kMultiplayerMenu+"Build && Run", priority = kMultiplayerPrio+1)]
    public static void BuildAndRun()
    {
        if (Build())
            Run();
    }


    [MenuItem(kMultiplayerMenu+"Run", priority = kMultiplayerPrio+2)]
    public static void Run()
    {
        int playerIndex = 0;
        int numWindowsTotal = numTestPlayers;

        switch (editorRole)
        {
            case EditorRole.Client:
                CommandLine.editorCommands = "-connect 127.0.0.1";
                RunBuild($"-host -scene \"{EditorSceneManager.GetActiveScene().path}\" {MakeDimensionParam(CalculateWindowDimensionsForPlayer(playerIndex++, numWindowsTotal))}");
                break;
            case EditorRole.Server:
                CommandLine.editorCommands = $"-server -scene \"{EditorSceneManager.GetActiveScene().path}\"";
                RunBuild($"-connect 127.0.0.1 {MakeDimensionParam(CalculateWindowDimensionsForPlayer(playerIndex++, numWindowsTotal))}");
                break;
            case EditorRole.Host:
                CommandLine.editorCommands = $"-host 127.0.0.1 -scene \"{EditorSceneManager.GetActiveScene().path}\"";
                numWindowsTotal = numTestPlayers - 1;
                break;
            case EditorRole.None:
                numWindowsTotal = numTestPlayers + 1;
                RunBuild($"-host -scene \"{EditorSceneManager.GetActiveScene().path}\" {MakeDimensionParam(CalculateWindowDimensionsForPlayer(playerIndex++, numWindowsTotal))}");
                RunBuild($"-connect 127.0.0.1 {MakeDimensionParam(CalculateWindowDimensionsForPlayer(playerIndex++, numWindowsTotal))}");
                break;
        }

        // Connect the remaining players
        for (int i = 0; i < numTestPlayers - 1; i++)
            RunBuild($"-connect 127.0.0.1 {MakeDimensionParam(CalculateWindowDimensionsForPlayer(playerIndex++, numWindowsTotal))}");

        // Start the editor if applicable
        if (editorRole != EditorRole.None)
            EditorApplication.isPlaying = true;
    }

    [MenuItem(kEditorRoleMenu + "Standalone", priority = kEditorRolePrio)]
    private static void StandaloneOnly() { editorRole = EditorRole.None; }

    [MenuItem(kEditorRoleMenu + "Standalone", true)]
    private static bool StandaloneOnlyValidate() { Menu.SetChecked(kEditorRoleMenu + "Standalone", editorRole == EditorRole.None); return true; }

    [MenuItem(kEditorRoleMenu + "Editor is Host", priority = kEditorRolePrio+1)]
    private static void EditorIsHost() { editorRole = EditorRole.Host; }

    [MenuItem(kEditorRoleMenu + "Editor is Host", true)]
    private static bool EditorIsHostValidate() { Menu.SetChecked(kEditorRoleMenu + "Editor is Host", editorRole == EditorRole.Host); return true; }

    [MenuItem(kEditorRoleMenu + "Editor is Server", priority = kEditorRolePrio+2)]
    private static void EditorIsServer() { editorRole = EditorRole.Server; }

    [MenuItem(kEditorRoleMenu + "Editor is Server", true)]
    private static bool EditorIsServerValidate() { Menu.SetChecked(kEditorRoleMenu + "Editor is Server", editorRole == EditorRole.Server); return true; }

    [MenuItem(kEditorRoleMenu + "Editor is Client", priority = kEditorRolePrio+3)]
    private static void EditorIsClient() { editorRole = EditorRole.Client; }

    [MenuItem(kEditorRoleMenu + "Editor is Client", true)]
    private static bool EditorIsClientValidate() { Menu.SetChecked(kEditorRoleMenu + "Editor is Client", editorRole == EditorRole.Client); return true; }

    [MenuItem(kPlayerCountMenu + "1 player", priority = kPlayerCountPrio)]
    private static void OneTestPlayer() { numTestPlayers = 1; }

    [MenuItem(kPlayerCountMenu + "1 player", true)]
    private static bool OneTestPlayerValidate() { Menu.SetChecked(kPlayerCountMenu + "1 player", numTestPlayers == 1); return true; }


    [MenuItem(kPlayerCountMenu + "2 players", priority = kPlayerCountPrio+1)]
    private static void TwoTestPlayers() { numTestPlayers = 2; }

    [MenuItem(kPlayerCountMenu + "2 players", true)]
    private static bool TwoTestPlayersValidate() { Menu.SetChecked(kPlayerCountMenu + "2 players", numTestPlayers == 2); return true; }


    [MenuItem(kPlayerCountMenu + "3 players", priority = kPlayerCountPrio+2)]
    private static void ThreeTestPlayers() { numTestPlayers = 3; }

    [MenuItem(kPlayerCountMenu + "3 players", true)]
    private static bool ThreeTestPlayersValidate() { Menu.SetChecked(kPlayerCountMenu + "3 players", numTestPlayers == 3); return true; }


    [MenuItem(kPlayerCountMenu + "4 players", priority = kPlayerCountPrio+3)]
    private static void FourTestPlayers() { numTestPlayers = 4; }

    [MenuItem(kPlayerCountMenu + "4 players", true)]
    private static bool FourTestPlayersValidate() { Menu.SetChecked(kPlayerCountMenu + "4 players", numTestPlayers == 4); return true; }


    [MenuItem(kBuildTypeMenu + "BuildType: All scenes", priority = kBuildTypePrio)]
    private static void BuildTypeFull() { buildType = BuildType.AllScenes; }

    [MenuItem(kBuildTypeMenu + "BuildType: All scenes", true)]
    private static bool BuildTypeFullValidate() { Menu.SetChecked(kBuildTypeMenu + "BuildType: All scenes", buildType == BuildType.AllScenes); return true; }

    [MenuItem(kBuildTypeMenu + "BuildType: Current scene", priority = kBuildTypePrio+1)]
    private static void BuildTypeScripts() { buildType = BuildType.CurrentScene; }

    [MenuItem(kBuildTypeMenu + "BuildType: Current scene", true)]
    private static bool BuildTypeScriptsValidate() { Menu.SetChecked(kBuildTypeMenu + "BuildType: Current scene", buildType == BuildType.CurrentScene); return true; }

    [MenuItem(kBuildTypeMenu + "BuildType: Scripts only", priority = kBuildTypePrio+2)]
    private static void BuildTypeCurrentScene() { buildType = BuildType.ScriptsOnly; }

    [MenuItem(kBuildTypeMenu + "BuildType: Scripts only", true)]
    private static bool BuildTypeCurrentSceneValidate() { Menu.SetChecked(kBuildTypeMenu + "BuildType: Scripts only", buildType == BuildType.ScriptsOnly); return true; }

    [MenuItem(kBuildTypeMenu + "BuildType: Autocompile", priority = kBuildTypePrio+3)]
    private static void BuildTypeAutoCompile()
    {
        EditorUtility.DisplayDialog("AutoCompile mode note", "AutoCompile attempts to compile and transfer new binaries on the fly and send them to a pre-built playtest build folder. This must exist first!\n\n" +
            "This is EXPERIMENTAL and may not always produce the correct results or function at all.\n\nAuto-compiling may also hang your editor for slightly longer so is not recommended during rapid single-player iteration.", "I understand");
        buildType = BuildType.AutoCompile;
    }

    [MenuItem(kBuildTypeMenu + "BuildType: Autocompile", true)]
    private static bool BuildTypeAutoCompileValidate() { Menu.SetChecked(kBuildTypeMenu + "BuildType: Autocompile", buildType == BuildType.AutoCompile); return true; }


    [MenuItem(kBuildPlatformMenu + "BuildPlatform: Win64", priority = kBuildPlatformPrio)]
    private static void BuildPlatform64() { isWin64 = true; }

    [MenuItem(kBuildPlatformMenu + "BuildPlatform: Win64", true)]
    private static bool BuildPlatform64Validate() { Menu.SetChecked(kBuildPlatformMenu + "BuildPlatform: Win64", isWin64); return true; }

    [MenuItem(kBuildPlatformMenu + "BuildPlatform: Win32", priority = kBuildPlatformPrio+1)]
    private static void BuildPlatform32() { isWin64 = false; }

    [MenuItem(kBuildPlatformMenu + "BuildPlatform: Win32", true)]
    private static bool BuildPlatform32Validate() { Menu.SetChecked(kBuildPlatformMenu + "BuildPlatform: Win32", !isWin64); return true; }

    private static void RunBuild(string arguments = "")
    {
        // Run another instance of the game
        System.Diagnostics.Process process = new System.Diagnostics.Process();

        process.StartInfo.FileName = $"{playtestBuildPath}/{Application.productName}.exe";
        process.StartInfo.WorkingDirectory = playtestBuildPath;
        process.StartInfo.Arguments = arguments;

        process.Start();
    }

    [MenuItem(kFinalBuildMenu + "Server Build", priority = kFinalBuildPrio)]
    public static void BuildFinalServer()
    {
        string originalScene = EditorSceneManager.GetActiveScene().path;

        EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();

        List<string> scenes = new List<string>();

        foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
        {
            if (scene.enabled)
                scenes.Add(scene.path);
        }

        BuildPlayerOptions buildOptions = new BuildPlayerOptions()
        {
            target = BuildTarget.StandaloneLinux64,
            scenes = scenes.ToArray(),
            locationPathName = $"{linuxBuildPath}/build.x86_64",
#if UNITY_2021_2_OR_NEWER
            subtarget = (int)StandaloneBuildSubtarget.Server, // int???
#else
            options = BuildOptions.EnableHeadlessMode
#endif
        };

        UnityEditor.Build.Reporting.BuildReport buildReport = BuildPipeline.BuildPlayer(buildOptions);

        EditorSceneManager.OpenScene(originalScene);

        if (buildReport.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            EditorUtility.DisplayDialog("Someone goofed", $"Build failed ({buildReport.summary.totalErrors} errors)", "OK");
        }
    }

    [MenuItem(kFinalBuildMenu + "WebGL Build", priority = kFinalBuildPrio + 1)]
    public static void BuildFinalWebGL()
    {
        string originalScene = EditorSceneManager.GetActiveScene().path;

        EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();

        UnityEditor.Build.Reporting.BuildReport buildReport = BuildPipeline.BuildPlayer(EditorBuildSettings.scenes, $"{webGlBuildPath}/", BuildTarget.WebGL, BuildOptions.None);

        EditorSceneManager.OpenScene(originalScene);

        if (buildReport.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            EditorUtility.DisplayDialog("Someone goofed", $"Build failed ({buildReport.summary.totalErrors} errors)", "OK");
        }
    }

    private static string MakeDimensionParam(RectInt dimensions) => $"" +
        $"-pos {dimensions.x} {dimensions.y} " +
        $"-screen-fullscreen 0 -screen-width {dimensions.width} -screen-height {dimensions.height}";

    private static RectInt CalculateWindowDimensionsForPlayer(int playerIndex, int numPlayers)
    {
        RectInt screen = new RectInt(0, 0, Screen.currentResolution.width, Screen.currentResolution.height);

        if (numPlayers == 1 || numPlayers > 4)
            return new RectInt(screen.width / 4, screen.height / 4, screen.width / 2, screen.height / 2);
        else if (numPlayers == 2)
            return new RectInt(screen.width / 2 * playerIndex, screen.height / 4, screen.width / 2, screen.height / 2);
        else if (numPlayers <= 4)
            return new RectInt(screen.width / 2 * (playerIndex % 2), screen.height / 2 * (playerIndex / 2), screen.width / 2, screen.height / 2);
        return default;
    }
}