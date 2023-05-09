using System.Text;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Monitors server/client time as determined by a TickSynchroniser.
/// - Red line is the last server time we received
/// - Yellow line is the predictive time we're running at
/// - Blue is the server's feedback, usually telling us when the server acknowledged our latest inputs. So if it's staying above the red line, great!
/// - Balance line shows how much faster/slower the game is running to keep synchronised with the server
/// </summary>
public class ServerTimeMonitorUI : MonoBehaviour
{
    [Header("Debug target")]
    public bool autoFindTarget = true;
    public TimeSynchroniser target;

    [Header("Speed balance line")]
    public RectTransform balanceLine;
    public float range = 0.2f;

    [Header("Graphs")]
    public GraphGraphic timeGraphs;

    [Header("Graph labels")]
    public Text timeLabel;
    public Text leftLabel;
    public Text rightLabel;

    [Header("Extra")]
    public Text infoBox;

    private double lastServerTime;
    private double lastLocalTime;

    private GraphGraphic.GraphCurve predictedServerTimeCurve;    // time of self, based on predicted server time
    private GraphGraphic.GraphCurve lastReceivedServerTimeCurve; // time last received from server
    private GraphGraphic.GraphCurve serverLocalTimeCurve;        // time of self on server, as last recieved from server

    private double lastAddedServerTickRealtime;

    private void Awake()
    {
        leftLabel.text = $"{Mathf.RoundToInt(-range * 100)}%";
        rightLabel.text = $"{Mathf.RoundToInt(range * 100)}%";

        lastReceivedServerTimeCurve = timeGraphs.AddCurve(Color.red);
        predictedServerTimeCurve = timeGraphs.AddCurve(Color.yellow);
        serverLocalTimeCurve = timeGraphs.AddCurve(Color.blue);
    }

    private void LateUpdate()
    {
        if (target == null && autoFindTarget && (int)Time.time != (int)(Time.time - Time.deltaTime))
            target = FindObjectOfType<TimeSynchroniser>();

        if (target != null)
        {
            UpdateBalanceLine();

            UpdateGraphs();

            UpdateInfoBox();

            lastServerTime = target.timeOnLastUpdate;
            lastLocalTime = Time.timeAsDouble;
        }
    }

    private void UpdateBalanceLine()
    {
        float parentWidth = (balanceLine.transform.parent as RectTransform).sizeDelta.x; // .rect.width maybe? sizeDelta seems to do whatever it wants
        float gameSpeed = (float)((target.timeOnLastUpdate - lastServerTime) / (Time.timeAsDouble - lastLocalTime));

        // Update time
        balanceLine.anchoredPosition = new Vector2((gameSpeed - 1f) * parentWidth / 2f / range, 0f);

        timeLabel.text = $"{((gameSpeed - 1f) * 100).ToString("F1")}%";
    }

    private void UpdateGraphs()
    {
        // we only need to add a point when a server tick comes in really (especially for the server data)
        if (target.timeOfLastServerUpdate > lastAddedServerTickRealtime)
        {
            // Our local predicted time
            predictedServerTimeCurve.data.Insert(Time.realtimeSinceStartupAsDouble, (float)(target.timeOnLastUpdate - Time.realtimeSinceStartupAsDouble));

            // Times on server: server time, and our local time from the server's perspective
            double serverTimeOnGraph = Time.realtimeSinceStartupAsDouble - (target.timeOnLastUpdate - target.timeOnServer);
            lastReceivedServerTimeCurve.data.Insert(serverTimeOnGraph, (float)(target.timeOnServer - Time.realtimeSinceStartupAsDouble));
            serverLocalTimeCurve.data.Insert(serverTimeOnGraph, (float)(target.timeOnServer + target.lastAckedClientOffset - Time.realtimeSinceStartupAsDouble));

            timeGraphs.ClearTimeAfter(Time.realtimeSinceStartup + 2f);

            lastAddedServerTickRealtime = target.timeOfLastServerUpdate;

            timeGraphs.Redraw();
        }
    }

    private static StringBuilder updateInfoBoxStringBuilder = new StringBuilder();

    private void UpdateInfoBox()
    {
        infoBox.text = $"ServerTime: {target.timeOnServer.ToString("F1")}\nClientTime: {target.timeOnLastUpdate.ToString("F1")}\n" +
            $"Effective RTT: {((int)((target.timeOnLastUpdate - target.timeOnServer - (Time.timeAsDouble - target.timeOfLastServerUpdate)) * 1000f)).ToString()}ms\n"
            + $"LastServerInputOffset: {((int)(target.lastAckedClientOffset * 1000f)).ToString()}ms";
    }
}
