using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Handles Ticker timeline debuggers with a ticker selection dropdown box
/// </summary>
[RequireComponent(typeof(Event))]
public class TickerTimelineDebugSelectorUI : MonoBehaviour
{
    public Dropdown dropdown;
    public TickerTimelineDebugUI tickerTimeline;

    private readonly List<ITickableBase> selectableTickers = new List<ITickableBase>(32);

    private void Start()
    {
        // repopulate list when mouse interacts with the dropdown
        EventTrigger events = dropdown.gameObject.AddComponent<EventTrigger>();
        EventTrigger.Entry eventHandler = new EventTrigger.Entry()
        {
            eventID = EventTriggerType.PointerClick
        };
        eventHandler.callback.AddListener(OnDropdownEvent);

        events.triggers.Add(eventHandler);

        // handle dropsdown selection actually changing
        dropdown.onValueChanged.AddListener(OnDropdownSelectionChanged);

        // give dropdown initial values
        RepopulateDropdown();
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

    private void OnValidate()
    {
        if (dropdown == null)
            dropdown = GetComponentInChildren<Dropdown>();

        if (tickerTimeline == null)
            tickerTimeline = GetComponentInChildren<TickerTimelineDebugUI>();
    }
}
