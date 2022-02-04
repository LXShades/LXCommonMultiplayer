namespace UnityMultiplayerEssentials.Examples.Mirror
{
    public class MirrorCommandLineExecutor : CommandLineExecutor
    {
        public override void Connect(string ip) => NetMan.singleton.Connect(ip);
        public override void StartServer(bool includeHostPlayer) => NetMan.singleton.Host(includeHostPlayer);
        public override void SetPort(int port) => NetMan.singleton.transportPort = port;
    }
}