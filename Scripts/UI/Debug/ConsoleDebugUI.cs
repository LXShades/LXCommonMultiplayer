using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Debug UI for displaying the full Unity console in-game as a UI element
/// </summary>
public class ConsoleDebugUI : MonoBehaviour
{
    [Header("Debug")]
    [Tooltip("Textbox to contain the console text. Recommend avoiding using auto-sizing text and scrollbars.")]
    public Text debugLogText;

    [Tooltip("Whether to report messages in reverse chronical order (newest at top)")]
    public bool useReverseChronologicalOrder = false;

    [Tooltip("Whether to show non-warning logs")]
    public bool showHarmlessLogs = true;

    private string debugLogContents;

    private bool doRefreshLogNextFrame = false;

    private bool hasInitiallyCleared = false;

    private void OnEnable()
    {
        Application.logMessageReceived += OnLogMessageReceived;

        if (!hasInitiallyCleared)
        {
            hasInitiallyCleared = true;
            debugLogText.text = "";
        }
    }

    private void OnDisable()
    {
        Application.logMessageReceived -= OnLogMessageReceived;
    }

    private void LateUpdate()
    {
        if (doRefreshLogNextFrame)
        {
            // Generate the text, test its height and trim the log contents to fit
            var settings = debugLogText.GetGenerationSettings(debugLogText.rectTransform.rect.size);
            settings.generateOutOfBounds = true;

            TextGenerator textGen = debugLogText.cachedTextGenerator;
            if (textGen.Populate(debugLogContents, settings))
            {
                int visibleLineCount = (int)textGen.rectExtents.height / textGen.lines[0].height;

                if (visibleLineCount < textGen.lineCount)
                {
                    if (useReverseChronologicalOrder)
                    {
                        int startChar = textGen.lines[visibleLineCount].startCharIdx;
                        debugLogText.text = debugLogContents = debugLogContents.Substring(0, startChar);
                    }
                    else
                    {
                        int startChar = textGen.lines[textGen.lineCount - visibleLineCount].startCharIdx;
                        debugLogText.text = debugLogContents = debugLogContents.Substring(startChar);
                    }
                }
                else
                {
                    debugLogText.text = debugLogContents;
                }
            }

            doRefreshLogNextFrame = false;
        }
    }

    private void OnLogMessageReceived(string condition, string stackTrace, LogType type)
    {
        if (type == LogType.Log && !showHarmlessLogs)
            return; // don't show everything

        // colour warnings/errors differently
        string colorStart = "", colorEnd = "";
        switch (type)
        {
            case LogType.Warning:
                colorStart = "<color=yellow>";
                colorEnd = "</color>";
                break;
            case LogType.Error:
                colorStart = "<color=red>";
                colorEnd = "</color>";
                break;
        }

        // add it to the log contents
        string additionalString;

        additionalString = $"{colorStart}{condition}";

        if (type == LogType.Error || type == LogType.Warning)
        {
            string[] trace = stackTrace.Split('\n');

            // include stack trace where it's important
            additionalString += "\n @ " + (trace.Length > 0 ? (trace[Mathf.Min(1, trace.Length - 1)]) : "") + colorEnd;
        }
        else
        {
            additionalString += colorEnd;
        }

        if (!useReverseChronologicalOrder)
            debugLogContents = $"{debugLogContents}\n{additionalString}";
        else
            debugLogContents = $"{additionalString}\n{debugLogContents}";

        // refresh on next frame
        doRefreshLogNextFrame = true;
    }

    public void ClearLog()
    {
        debugLogText.text = debugLogContents = "";
    }

    private void OnValidate()
    {
        if (debugLogText == null)
        {
            debugLogText = GetComponent<Text>();
        }
    }
}
