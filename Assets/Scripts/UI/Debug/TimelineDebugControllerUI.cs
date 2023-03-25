using System;
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
public class TimelineDebugControllerUI : MonoBehaviour
{
    public Dropdown dropdown;
    public TimelineDebugViewerUI timelineUI;
    public GameObject popOutArea;

    [Header("Controllable")]
    [Tooltip("Whether the mouse and a play/pause button can be used to play, pause andor seek the selected ticker")]
    public bool isControllable = false;
    public Button playPauseButton;
    public Text playPauseButtonText;
    public Button zoomInButton;
    public Button zoomOutButton;
    public Button popOutButton;

    [Header("Advanced")]
    public int currentZoomLevel = 0;
    public float[] zoomLevelDisplayPeriods = new float[1] { 5f };

    private readonly List<Timeline> selectableTimelines = new List<Timeline>(32);

    private void Start()
    {
        // repopulate list when mouse interacts with the dropdown
        EventTrigger events = dropdown.gameObject.GetComponent<EventTrigger>();

        if (events == null)
            events = dropdown.gameObject.AddComponent<EventTrigger>();

        EventTrigger.Entry eventHandler = new EventTrigger.Entry()
        {
            eventID = EventTriggerType.PointerEnter
        };
        eventHandler.callback.AddListener(OnDropdownEvent);

        events.triggers.Add(eventHandler);

        // handle when mouse clicks on timeline
        events = timelineUI.gameObject.GetComponent<EventTrigger>();

        if (events == null)
            events = timelineUI.gameObject.AddComponent<EventTrigger>();

        eventHandler = new EventTrigger.Entry()
        {
            eventID = EventTriggerType.Drag
        };
        eventHandler.callback.AddListener(OnTimelineDrag);

        events.triggers.Add(eventHandler);

        // handle when dropdown selection is changed
        dropdown.onValueChanged.AddListener(OnDropdownSelectionChanged);

        // play/pause feature
        if (playPauseButton)
            playPauseButton.onClick.AddListener(OnPlayPauseClicked);

        if (zoomInButton)
            zoomInButton.onClick.AddListener(() => OnZoomClicked(1));
        if (zoomOutButton)
            zoomOutButton.onClick.AddListener(() => OnZoomClicked(-1));
        if (popOutButton)
            popOutButton.onClick.AddListener(OnPopOutClicked);

        // give dropdown initial values
        RepopulateDropdown();
        // initial play/pause text
        UpdatePlayPauseButtonText();

        // initial zoom
        timelineUI.displayPeriod = zoomLevelDisplayPeriods[Mathf.Clamp(currentZoomLevel, 0, zoomLevelDisplayPeriods.Length - 1)];
    }

    private void OnEnable()
    {
        RepopulateDropdown();
    }

    private void RepopulateDropdown()
    {
        // preserve last selected item
        int newSelectedItemIndex = -1;

        List<Dropdown.OptionData> options = new List<Dropdown.OptionData>();
        dropdown.ClearOptions();
        selectableTimelines.Clear();

        selectableTimelines.Add(null);
        options.Add(new Dropdown.OptionData("<select timeline>"));

        // add new items
        foreach (WeakReference<Timeline> tickerWeak in Timeline.allTimelines)
        {
            if (tickerWeak.TryGetTarget(out Timeline timeline))
            {
                options.Add(new Dropdown.OptionData(timeline.name));
                selectableTimelines.Add(timeline);

                if (timelineUI.target == timeline)
                    newSelectedItemIndex = options.Count - 1;
            }
        }

        // try to select last selected item if possible
        dropdown.options = options;
        dropdown.value = newSelectedItemIndex;
    }

    private void OnDropdownSelectionChanged(int value)
    {
        if (value > -1 && value < selectableTimelines.Count)
        {
            timelineUI.target = selectableTimelines[value];
            UpdatePlayPauseButtonText();
        }
    }

    private void OnTimelineDrag(BaseEventData eventData)
    {
        if (isControllable && timelineUI.target != null && timelineUI.target.isDebugPaused && eventData is PointerEventData pointerEvent)
        {
            // scroll target time
            if (pointerEvent.button == PointerEventData.InputButton.Left)
            {
                double timeDifference = timelineUI.graphic.timePerScreenX * pointerEvent.delta.x;
                double targetTime = timelineUI.target.playbackTime + timeDifference;

                if (timeDifference != 0f)
                {
                    timelineUI.target.SetDebugPaused(false); // briefly allow seek
                    timelineUI.target.Seek(targetTime);
                    timelineUI.target.SetDebugPaused(true); // briefly allow seek
                }
            }
        }

        if (eventData is PointerEventData pointerEvent2)
        {
            // scroll source time
            if (pointerEvent2.button == PointerEventData.InputButton.Right)
            {
                double sourceTime = timelineUI.graphic.TimeAtScreenX(pointerEvent2.position.x);
                double targetTime = timelineUI.target.playbackTime;

                timelineUI.target.SetDebugPaused(false);
                timelineUI.target.Seek(sourceTime);
                timelineUI.target.DebugTrimStatesAfter(sourceTime); // force reconfirmation
                timelineUI.target.Seek(targetTime);
                timelineUI.target.SetDebugPaused(true);
            }
        }
    }

    private void OnDropdownEvent(BaseEventData data)
    {
        RepopulateDropdown();
    }

    private void OnPlayPauseClicked()
    {
        if (isControllable && timelineUI.target != null)
            timelineUI.target.SetDebugPaused(!timelineUI.target.isDebugPaused);

        UpdatePlayPauseButtonText();
    }
    private void OnZoomClicked(int delta)
    {
        currentZoomLevel = Mathf.Clamp(currentZoomLevel + delta, 0, zoomLevelDisplayPeriods.Length - 1);
        RefreshZoomOnTimeline();
    }

    private void OnPopOutClicked()
    {
        if (popOutArea)
            popOutArea.SetActive(!popOutArea.activeSelf);
    }

    private void RefreshZoomOnTimeline()
    {
        timelineUI.SetDisplayPeriod(zoomLevelDisplayPeriods[currentZoomLevel]);
    }

    private void UpdatePlayPauseButtonText()
    {
        if (playPauseButtonText && timelineUI.target != null)
            playPauseButtonText.text = timelineUI.target.isDebugPaused ? ">" : "II";
    }

    private void OnValidate()
    {
        if (dropdown == null)
            dropdown = GetComponentInChildren<Dropdown>();

        if (timelineUI == null)
            timelineUI = GetComponentInChildren<TimelineDebugViewerUI>();

        if (isControllable && playPauseButton == null)
        {
            playPauseButton = GetComponentInChildren<Button>();
            if (playPauseButton)
                playPauseButtonText = playPauseButton.GetComponentInChildren<Text>();
        }
    }
}
