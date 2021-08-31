using MultiplayerToolset.Examples.Mirror;
using UnityEngine;
using UnityEngine.UI;

public class SceneBUI : MonoBehaviour
{
    public Slider extrapolationSlider;
    public Text extrapolationSliderLabel;
    public Toggle autoExtrapolationToggle;

    private TickerController tickerController;

    void Awake()
    {
        extrapolationSlider.onValueChanged.AddListener(OnExtrapolationChanged);
        autoExtrapolationToggle.onValueChanged.AddListener(OnAutoExtrapolationChanged);
    }

    private void Update()
    {
        bool shouldActivateExtrapolationControls = Mirror.NetworkClient.isConnected && !Mirror.NetworkServer.active;
        if (extrapolationSlider.gameObject.activeSelf != shouldActivateExtrapolationControls)
        {
            extrapolationSlider.gameObject.SetActive(shouldActivateExtrapolationControls);
            extrapolationSliderLabel.gameObject.SetActive(shouldActivateExtrapolationControls);
            autoExtrapolationToggle.gameObject.SetActive(shouldActivateExtrapolationControls);
        }

        if (tickerController == null)
            tickerController = FindObjectOfType<TickerController>(); // needed until server provides the object

        if (tickerController != null)
        {
            if (shouldActivateExtrapolationControls && tickerController.useAutomaticClientExtrapolation)
                extrapolationSlider.value = tickerController.autoCalculatedClientExtrapolation;

            if (autoExtrapolationToggle.isOn != tickerController.useAutomaticClientExtrapolation)
                autoExtrapolationToggle.isOn = tickerController.useAutomaticClientExtrapolation;
        }
    }

    void OnExtrapolationChanged(float newValue)
    {
        if (tickerController)
        {
            if (!tickerController.useAutomaticClientExtrapolation) // for automatic extrapolation the slider is a monitor rather than a controller
                tickerController.clientExtrapolation = newValue;

            extrapolationSliderLabel.text = $"Client extrapolation {(int)(newValue * 1000f)}ms";
        }
    }

    void OnAutoExtrapolationChanged(bool newValue)
    {
        if (tickerController != null)
            tickerController.useAutomaticClientExtrapolation = newValue;
    }
}
