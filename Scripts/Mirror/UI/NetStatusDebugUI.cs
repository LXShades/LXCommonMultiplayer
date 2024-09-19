using Mirror;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shows the Mirror connection status
/// </summary>
public class NetStatusDebugUI : MonoBehaviour
{
    public Text statusText;

    NetworkManagerMode lastKnownNetMode;

    private void Update()
    {
        if (NetMan.singleton)
        {
            if (NetMan.singleton.mode != lastKnownNetMode)
            {
                lastKnownNetMode = NetMan.singleton.mode;
                statusText.text = NetMan.singleton.mode.ToString();
            }
        }
    }

    private void OnValidate()
    {
        if (statusText == null)
        {
            statusText = GetComponent<Text>();
        }
    }
}
