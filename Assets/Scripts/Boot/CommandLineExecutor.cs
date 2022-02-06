using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityMultiplayerEssentials.Examples
{
    /// <summary>
    /// Executes command line params for quicktesting and any other desired startup behaviours.
    /// 
    /// You can inherit from this class to define exactly how to interact with your network manager
    /// 
    /// Default options:
    /// 
    /// * Open scene (-scene)
    /// * Connect to IP (-connect)
    /// * Host game (-host)
    /// * Set a custom port (-port)
    /// * Position all windows for playtests (-pos)
    /// </summary>
    public abstract class CommandLineExecutor : MonoBehaviour
    {
        [Tooltip("Disables default commands in release builds, if desired")]
        public bool disableInReleaseBuilds = true;

        [Tooltip("Whether to load a default scene if -scene overrides don't exist")]
        public bool loadDefaultScene;
        public string defaultScene = "1";

        public const string kSceneParam = "-scene";
        public const string kConnectParam = "-connect";
        public const string kHostParam = "-host";
        public const string kServerParam = "-server";
        public const string kPortParam = "-port";

        /// <summary>
        /// By default, Start() loads a scene if desired and then runs the command line
        /// </summary>
        protected virtual void Start()
        {
            if (disableInReleaseBuilds && !Debug.isDebugBuild && loadDefaultScene)
            {
                LoadScene(defaultScene);
                return; // commands ignored in release builds
            }

            Debug.Log($"Running command line: {CommandLine.GetAllCommandsAsString()}");

            // Load the scene first
            AsyncOperation op = null;
            if (CommandLine.GetCommand(kSceneParam, 1, out string[] sceneParams))
                op = LoadScene(sceneParams[0]);
            else if (SceneManager.GetActiveScene().buildIndex == 0 && loadDefaultScene)
                op = LoadScene(defaultScene);

            // Execute remaining command line commands
            if (op != null)
                op.completed += (AsyncOperation operation) => ExecuteCommandLine();
        }

        /// <summary>
        /// Executes the command line parameters
        /// </summary>
        protected virtual void ExecuteCommandLine()
        {
            if (CommandLine.GetCommand(kPortParam, 1, out string[] port))
            {
                if (int.TryParse(port[0], out int portInt))
                {
                    Debug.Log($"Setting host port to {portInt}");
                    SetPort(portInt);
                }
            }

            if (CommandLine.GetCommand(kConnectParam, 1, out string[] ip))
            {
                Debug.Log($"Connecting to {ip[0]}");
                Connect(ip[0]);
            }
            else if (CommandLine.HasCommand(kHostParam))
            {
                Debug.Log($"Running host server");
                StartServer(true);
            }
            else if (CommandLine.HasCommand(kServerParam))
            {
                Debug.Log($"Running dedicated server");
                StartServer(false);
            }
            else
            {
                Debug.LogWarning("No -connect, -host or -server parameter was specified.");
            }
        }

        /// <summary>
        /// Called when command line wants to connect to an IP
        /// </summary>
        public abstract void Connect(string ip);

        /// <summary>
        /// Called when command line wants to start a server
        /// </summary>
        public abstract void StartServer(bool includeHostPlayer);

        /// <summary>
        /// Overrides the default hosting/connecting port, if desired
        /// </summary>
        public abstract void SetPort(int port);

        /// <summary>
        /// Loads a scene by name/path or build index
        /// </summary>
        protected AsyncOperation LoadScene(string sceneNameOrIndex)
        {
            bool isInt = int.TryParse(sceneNameOrIndex, out int sceneIndex);
            int activeSceneIndex = SceneManager.GetActiveScene().buildIndex;

            Debug.Log($"Loading scene {sceneNameOrIndex}...");

            if ((isInt && sceneIndex == activeSceneIndex)
                || SceneManager.GetSceneByName(sceneNameOrIndex).IsValid() && SceneManager.GetSceneByName(sceneNameOrIndex).buildIndex == activeSceneIndex
                || SceneManager.GetSceneByPath(sceneNameOrIndex).IsValid() && SceneManager.GetSceneByPath(sceneNameOrIndex).buildIndex == activeSceneIndex)
            {
                Debug.LogError($"Infinite loop detected: we appear to be loading the scene we're already in. Stopping.");
                return null;
            }

            if (isInt)
                return SceneManager.LoadSceneAsync(sceneIndex);
            else
                return SceneManager.LoadSceneAsync(sceneNameOrIndex);
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR // why does !UNITY_EDITOR need to be here, shouldn't standalone be standalone? Oh well, doesn't seem to work that way
        // Window management functions
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern System.IntPtr GetActiveWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(System.IntPtr hwnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
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
                int.TryParse(posParams[0], out windowX);
                int.TryParse(posParams[1], out windowY);
            }

            SetForegroundWindow(GetActiveWindow());
            SetWindowPos(GetActiveWindow(), System.IntPtr.Zero, windowX, windowY, 1280, 720, SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_NOREDRAW | SWP_NOSIZE | SWP_NOZORDER | SWP_ASYNCWINDOWPOS);
        }
#endif
    }
}