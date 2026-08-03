#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using Unity.Multiplayer.PlayMode;
using System.Collections.Generic;

public static class CommandLine
{
    private static string[] commands
    {
        get
        {
            // Lazy init because some requesters run _super_ early
            if (_commands == null)
                ReceiveCommandsFromEditorOrSystem();
            return _commands;
        }
    }
    private static string[] _commands = null;

    public static string MultiplayerPlayModeServerTag = "PlayModeServer";
    public static string MultiplayerPlayModeHostTag = "PlayModeHost";
    public static string MultiplayerPlayModeClientTag = "PlayModeClient";

#if UNITY_EDITOR
    public static string editorCommands
    {
        get => EditorPrefs.GetString("_editorCommandLine", "");
        set => EditorPrefs.SetString("_editorCommandLine", value);
    }
#endif

    private static void ReceiveCommandsFromEditorOrSystem()
    {
#if UNITY_EDITOR
        // Double-quotes should allow spaces
        string[] splitByQuotes = editorCommands.Split(new char[] { '"' }, System.StringSplitOptions.RemoveEmptyEntries);
        System.Collections.Generic.List<string> joinedAsList = new System.Collections.Generic.List<string>();

        for (int i = 0; i < splitByQuotes.Length; i++)
        {
            if ((i & 1) == 1)
                joinedAsList.Add(splitByQuotes[i]);
            else
                joinedAsList.AddRange(splitByQuotes[i].Split(new char[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries));
        }

        _commands = joinedAsList.ToArray();
#else
        _commands = System.Environment.GetCommandLineArgs();
#endif

        // Multiplayer Play Mode support.
        // If this is the main editor, we'll use the default play mode the user has selected via our own menu. Otherwise, if this is an additional instance, we need to make some tweaks.
        if (CurrentPlayer.Tags.Count > 0 && !CurrentPlayer.IsMainEditor)
        {
            List<string> commandsAsList = new(_commands);

            // I'm not fully sure how other instances handle the existing play mode settings - they probably all take a copy of them
            // but differnet players have different roles, so we might actually need to _remove_ some command lines here to cancel out that copy effect
            foreach (string tag in CurrentPlayer.Tags)
            {
                if (tag.Equals(MultiplayerPlayModeClientTag, System.StringComparison.InvariantCultureIgnoreCase))
                {
                    commandsAsList.Add("-connect");
                    commandsAsList.Add("127.0.0.1");
                    commandsAsList.Remove("-host");
                    commandsAsList.Remove("-server");
                }
                else if (tag.Equals(MultiplayerPlayModeServerTag, System.StringComparison.InvariantCultureIgnoreCase))
                {
                    commandsAsList.Add("-server");
                    commandsAsList.Remove("-host");
                    commandsAsList.Remove("-connect");
                }
                else if (tag.Equals(MultiplayerPlayModeHostTag, System.StringComparison.InvariantCultureIgnoreCase))
                {
                    commandsAsList.Add("-host");
                    commandsAsList.Remove("-server");
                    commandsAsList.Remove("-connect");
                }
            }

            _commands = commandsAsList.ToArray();
        }

        UnityEngine.Debug.Log($"[CommandLine] Startup command line: {string.Join(" ", commands)}");
    }

    public static bool HasCommand(string commandName)
    {
        for (int i = 0; i < commands.Length; i++)
        {
            if (commands[i].Equals(commandName, System.StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static bool GetCommand(string commandName, int numParams, out string[] paramsOut)
    {
        paramsOut = null;

        commandName = commandName.ToLower();

        for (int i = 0; i < commands.Length - numParams; i++)
        {
            if (commands[i].ToLower() == commandName)
            {
                paramsOut = new string[numParams];
                System.Array.Copy(commands, i + 1, paramsOut, 0, numParams);
                return true;
            }
        }

        return false;
    }

    public static string GetAllCommandsAsString()
    {
        return string.Join(" ", commands);
    }
}
