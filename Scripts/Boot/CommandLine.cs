#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

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
