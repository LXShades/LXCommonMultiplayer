using System;
using System.Collections.Generic;
using UnityEngine;

public class TimelineListBase
{
    /// <summary>
    /// Number of items in the timeline
    /// </summary>
    public virtual int Count { get; }

    /// <summary>
    /// The time of the latest item in the timeline
    /// </summary>
    public virtual float LatestTime { get; }

    /// <summary>
    /// Clears all items
    /// </summary>
    public virtual void Clear() { }

    /// <summary>
    /// Returns the index of the item at the given time, within the tolerance, or -1 if not available
    /// </summary>
    public virtual int IndexAt(float time, float tolerance = 0.01f) => -1;

    /// <summary>
    /// Returns the time of the item at the given index
    /// </summary>
    public virtual float TimeAt(int index) => -1f;

    /// <summary>
    /// Returns the nearest item after the given time, or within the tolerance range
    /// If there are multiple items within the tolerance, the closest one after the time is returned
    /// </summary>
    public virtual int ClosestIndexAfter(float time, float tolerance = 0.01f) => -1;

    /// <summary>
    /// Returns the nearest item before the given time, or within the tolerance range
    /// If there are multiple items within the tolerance, the closest one before the time is returned
    /// </summary>
    public virtual int ClosestIndexBefore(float time, float tolerance = 0.01f) => -1;

    /// <summary>
    /// Returns the nearest item before the given time, or within the tolerance range
    /// If there are multiple items within the tolerance, the closest one before the time is returned.
    /// If there are no items within or before the tolerance, the earliest index in the timeline is returned
    /// </summary>
    public virtual int ClosestIndexBeforeOrEarliest(float time, float tolerance = 0.01f) => -1;

    /// <summary>
    /// Removes the item at the given index
    /// </summary>
    public virtual void RemoveAt(int index) { }

    /// <summary>
    /// Clears all items before the given time
    /// </summary>
    public virtual void TrimBefore(float minTime) { }

    /// <summary>
    /// Clears all items after the given time
    /// </summary>
    public virtual void TrimAfter(float maxTime) { }

    /// <summary>
    /// Trims all items outside the given time range, inclusive
    /// </summary>
    public virtual void Trim(float minTime, float maxTime) { }
}

/// <summary>
/// A list of items arranged along a floating-point timeline. Items can be searched, inserted, replaced, etc and quickly retrieved from certain times.
/// 
/// Items are arranged from latest item (0) to earliest item (Count - 1) order.
/// </summary>
[Serializable]
public class TimelineList<T> : TimelineListBase
{
    [Serializable]
    public struct TimelineItem
    {
        public float time;
        public T item;
    }

    [SerializeField] private List<TimelineItem> items = new List<TimelineItem>();

    public T this[int index]
    {
        get => items[index].item;
        set => items[index] = new TimelineItem()
        {
            time = items[index].time,
            item = value
        };
    }

    public override int Count => items.Count;

    public T Latest => items.Count > 0 ? items[0].item : default;

    public override float LatestTime => items.Count > 0 ? items[0].time : 0.0f;

    public T ItemAt(float time, float tolerance = 0.01f)
    {
        return items.Find(a => a.time >= time - tolerance && a.time <= time + tolerance).item;
    }

    public override int IndexAt(float time, float tolerance = 0.01f)
    {
        return items.FindIndex(a => a.time >= time - tolerance && a.time <= time + tolerance);
    }

    public override float TimeAt(int index)
    {
        if (index == -1) return -1;

        return items[index].time;
    }

    public override int ClosestIndexAfter(float time, float tolerance = 0.01f)
    {
        if (items.Count > 0)
        {
            for (int index = items.Count - 1; index >= 0; index--)
            {
                if (items[index].time > time - tolerance)
                    return index;
            }
        }

        return -1;
    }

    public override int ClosestIndexBefore(float time, float tolerance = 0.01f)
    {
        for (int index = 0; index < items.Count; index++)
        {
            if (items[index].time < time + tolerance)
                return index;
        }

        return -1;
    }

    public override int ClosestIndexBeforeOrEarliest(float time, float tolerance = 0.01f)
    {
        int index = ClosestIndexBefore(time, tolerance);
        
        if (index == -1)
        {
            return items.Count - 1;
        }
        else
        {
            return index;
        }
    }

    public void Set(float time, T item, float tolerance = 0.01f)
    {
        for (int index = 0; index < items.Count; index++)
        {
            if (time - tolerance <= items[index].time && time + tolerance >= items[index].time)
            {
                items[index] = new TimelineItem() { item = item, time = time };
                return;
            }
        }

        // none to replace, insert instead
        Insert(time, item);
    }

    public void Insert(float time, T item)
    {
        int index;
        for (index = 0; index < items.Count; index++)
        {
            if (time >= items[index].time)
                break;
        }

        items.Insert(index, new TimelineItem() { item = item, time = time });
    }

    public override void Clear()
    {
        items.Clear();
    }

    public override void RemoveAt(int index)
    {
        items.RemoveAt(index);
    }

    public override void TrimBefore(float minTime)
    {
        // start newest, end oldest
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].time < minTime)
            {
                items.RemoveRange(i, items.Count - i);
                break;
            }
        }
    }

    public override void TrimAfter(float maxTime)
    {
        // start oldest, end newest
        for (int i = items.Count - 1; i >= 0; i--)
        {
            if (items[i].time > maxTime)
            {
                items.RemoveRange(0, i + 1);
                break;
            }
        }
    }

    public override void Trim(float minTime, float maxTime)
    {
        TrimBefore(minTime);
        TrimAfter(maxTime);
    }
}