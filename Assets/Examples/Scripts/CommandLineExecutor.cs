using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_STANDALONE_WIN
using System.Runtime.InteropServices;
#endif

/// <summary>
/// Executes command line params for quicktesting and any other desired startup behaviours
/// 
/// Default behaviours:
/// 
/// * Open scene (-scene)
/// * Connect to IP (-connect)
/// * Host game (-host)
/// * Position window for playtests (-pos)
/// </summary>
public class CommandLineExecutor : MonoBehaviour
{
    public bool disableInReleaseBuilds = true;

    void Start()
    {
        if (disableInReleaseBuilds && !Debug.isDebugBuild)
        {
            return; // removed in release builds
        }

        // Set the scene first
        AsyncOperation op = null;
        if (CommandLine.GetCommand("-scene", 1, out string[] sceneParams))
        {
            op = SetScene(sceneParams[0]);
        }
        else if (SceneManager.GetActiveScene().buildIndex == 0)
        {
            op = SetScene("1");
        }

        // execute remaining command line commands
        op.completed += (AsyncOperation operation) => ExecuteCommandLine();
    }

    private void ExecuteCommandLine()
    {
        if (CommandLine.GetCommand("-connect", 1, out string[] ip))
            NetMan.singleton.Connect(ip[0]);
        else if (CommandLine.HasCommand("-host"))
            NetMan.singleton.Host(true);
    }

    private AsyncOperation SetScene(string scene)
    {
        int sceneIndex;
        bool isInt = int.TryParse(scene, out sceneIndex);

        Debug.Log($"Loading scene {scene}...");

        if (isInt)
        {
            return SceneManager.LoadSceneAsync(sceneIndex);
        }
        else
        {
            return SceneManager.LoadSceneAsync(scene);
        }
    }

#if UNITY_STANDALONE_WIN
    // Window management functions
    [DllImport("user32.dll")]
    private static extern System.IntPtr GetActiveWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(System.IntPtr hwnd);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(System.IntPtr hwnd, System.IntPtr hwndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
    private static void PreInitWindowPosition()
    {
        RepositionWindow();
    }

    private static void RepositionWindow()
    {
        const uint SWP_NOACTIVATE = 0x0010;
        const uint SWP_NOOWNERZORDER = 0x0200;
        const uint SWP_NOREDRAW = 0x0008;
        const uint SWP_NOSIZE = 0x0001;
        const uint SWP_NOZORDER = 0x0004;
        const uint SWP_ASYNCWINDOWPOS = 0x4000;

        int windowX = 0, windowY = 0;

        if (CommandLine.GetCommand("-pos", 2, out string[] posParams))
        {
            System.Int32.TryParse(posParams[0], out windowX);
            System.Int32.TryParse(posParams[1], out windowY);
        }

        SetForegroundWindow(GetActiveWindow());
        SetWindowPos(GetActiveWindow(), System.IntPtr.Zero, windowX, windowY, 1280, 720, SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_NOREDRAW | SWP_NOSIZE | SWP_NOZORDER | SWP_ASYNCWINDOWPOS);
    }
#endif
}
