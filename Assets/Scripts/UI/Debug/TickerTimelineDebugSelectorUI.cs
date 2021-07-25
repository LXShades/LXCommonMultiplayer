using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Extra optional tools for the Ticker timeline visualiser, with a ticker selection dropdown box and optional play/pause button
/// 
/// * Populates the dropdown with a selection of tickers in the game and reassigns the tickerTimeline's target when it changes.
/// * If isControllable is enabled, the target ticker may be played, paused, and seeked.
/// </summary>
[RequireComponent(typeof(Event))]
public class TickerTimelineDebugSelectorUI : MonoBehaviour
{
    public Dropdown dropdown;
    public TickerTimelineDebugUI tickerTimeline;

    [Header("Controllable")]
    [Tooltip("Whether the mouse and a play/pause button can be used to play, pause andor seek the selected ticker")]
    public bool isControllable = false;
    public Button playPauseButton;
    public Text playPauseButtonText;

    private readonly List<ITickableBase> selectableTickers = new List<ITickableBase>(32);

    private void Start()
    {
        // repopulate list when mouse interacts with the dropdown
        EventTrigger events = dropdown.gameObject.AddComponent<EventTrigger>();
        EventTrigger.Entry eventHandler = new EventTrigger.Entry()
        {
            eventID = EventTriggerType.PointerEnter
        };
        eventHandler.callback.AddListener(OnDropdownEvent);

        events.triggers.Add(eventHandler);

        // handle when dropdown selection is changed
        dropdown.onValueChanged.AddListener(OnDropdownSelectionChanged);

        // play/pause feature
        if (playPauseButton)
            playPauseButton.onClick.AddListener(OnPlayPauseClicked);

        // give dropdown initial values
        RepopulateDropdown();
        // initial play/pause text
        UpdatePlayPauseButtonText();
    }

    private void RepopulateDropdown()
    {
        // preserve last selected item
        int newSelectedItemIndex = -1;
        string currentlySelectedItemName = "";

        if (dropdown.value > -1 && dropdown.value < dropdown.options.Count)
            currentlySelectedItemName = dropdown.options[dropdown.value].text;

        List<Dropdown.OptionData> options = new List<Dropdown.OptionData>();
        dropdown.ClearOptions();
        selectableTickers.Clear();

        // add new items
        foreach (MonoBehaviour tickableComponent in FindObjectsOfType<MonoBehaviour>())
        {
            if (tickableComponent is ITickableBase tickable)
            {
                selectableTickers.Add(tickable);
                options.Add(new Dropdown.OptionData(tickableComponent.gameObject.name));

                if (tickableComponent.gameObject.name == currentlySelectedItemName)
                    newSelectedItemIndex = options.Count - 1;
            }
        }

        // try to select last selected item if possible
        dropdown.options = options;
        dropdown.value = newSelectedItemIndex;
    }

    private void OnDropdownSelectionChanged(int value)
    {
        if (value > -1 && value < selectableTickers.Count)
            tickerTimeline.targetTicker = selectableTickers[value].GetTicker();
    }

    private void OnDropdownEvent(BaseEventData data)
    {
        RepopulateDropdown();
    }

    private void OnPlayPauseClicked()
    {
        if (isControllable && tickerTimeline.targetTicker != null)
            tickerTimeline.targetTicker.SetDebugPaused(!tickerTimeline.targetTicker.isDebugPaused);

        UpdatePlayPauseButtonText();
    }

    private void UpdatePlayPauseButtonText()
    {
        if (playPauseButtonText && tickerTimeline.targetTicker != null)
            playPauseButtonText.text = tickerTimeline.targetTicker.isDebugPaused ? ">" : "II";
    }

    private void OnValidate()
    {
        if (dropdown == null)
            dropdown = GetComponentInChildren<Dropdown>();

        if (tickerTimeline == null)
            tickerTimeline = GetComponentInChildren<TickerTimelineDebugUI>();

        if (isControllable && playPauseButton == null)
        {
            playPauseButton = GetComponentInChildren<Button>();
            if (playPauseButton)
                playPauseButtonText = playPauseButton.GetComponentInChildren<Text>();
        }
    }
}
