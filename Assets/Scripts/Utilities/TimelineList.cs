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
    public virtual double LatestTime { get; }

    /// <summary>
    /// The time of the earliest item in the timeline
    /// </summary>
    public virtual double EarliestTime { get; }

    /// <summary>
    /// Clears all items
    /// </summary>
    public virtual void Clear() { }

    /// <summary>
    /// Returns the index of the item at the given time, within the tolerance, or -1 if not available
    /// </summary>
    public virtual int IndexAt(double time, double tolerance = 0f) => -1;

    /// <summary>
    /// Returns the time of the item at the given index
    /// </summary>
    public virtual double TimeAt(int index) => -1f;

    /// <summary>
    /// Returns the nearest item after the given time, or within the tolerance range
    /// If there are multiple items within the tolerance, the closest one after the time is returned
    /// </summary>
    public virtual int ClosestIndexAfter(double time, double tolerance = 0f) => -1;

    /// <summary>
    /// Returns the nearest item after or at the given time, or within the tolerance range
    /// If there are multiple items within the tolerance, the closest one after the time is returned
    /// </summary>
    public virtual int ClosestIndexAfterInclusive(double time, double tolerance = 0f) => -1;

    /// <summary>
    /// Returns the nearest item before the given time, or within the tolerance range
    /// If there are multiple items within the tolerance, the closest one before the time is returned
    /// </summary>
    public virtual int ClosestIndexBefore(double time, double tolerance = 0f) => -1;

    /// <summary>
    /// Returns the nearest item before or at the given time, or within the tolerance range
    /// If there are multiple items within the tolerance, the closest one before the time is returned
    /// </summary>
    public virtual int ClosestIndexBeforeInclusive(double time, double tolerance = 0f) => -1;

    /// <summary>
    /// Returns the nearest item before the given time, or within the tolerance range
    /// If there are multiple items within the tolerance, the closest one before the time is returned.
    /// If there are no items within or before the tolerance, the earliest index in the timeline is returned
    /// </summary>
    public virtual int ClosestIndexBeforeOrEarliest(double time, double tolerance = 0f) => -1;

    /// <summary>
    /// Returns the nearest item before or at the given time, or within the tolerance range
    /// If there are multiple items within the tolerance, the closest one before the time is returned.
    /// If there are no items within or before the tolerance, the earliest index in the timeline is returned
    /// </summary>
    public virtual int ClosestIndexBeforeOrEarliestInclusive(double time, double tolerance = 0f) => -1;

    /// <summary>
    /// Removes the item at the given index
    /// </summary>
    public virtual void RemoveAt(int index) { }

    /// <summary>
    /// Clears all items before the given time
    /// </summary>
    public virtual void TrimBefore(double minTime) { }

    /// <summary>
    /// Clears all items before the given time, except for the latest item in the list - useful for times when you need at least one item in your list
    /// </summary>
    public virtual void TrimBeforeExceptLatest(double minTime) { }

    /// <summary>
    /// Clears all items after the given time
    /// </summary>
    public virtual void TrimAfter(double maxTime) { }

    /// <summary>
    /// Trims all items outside the given time range, inclusive
    /// </summary>
    public virtual void Trim(double minTime, double maxTime) { }
}

/// <summary>
/// A list of items arranged along a double floating-point timeline. Items can be searched, inserted, replaced, etc and quickly retrieved from certain times.
/// 
/// * Items are arranged from latest item (0) to earliest item (Count - 1) order.
/// * Doubles are used because floats are actually quite limiting as the hours add up. After a few hours the discrepency between Time.deltaTime and (Time.time - lastFrameTime) becomes quite noticeable. Doubles are virtually infinite on a per-second basis.
/// </summary>
[Serializable]
public class TimelineList<T> : TimelineListBase
{
    [Serializable]
    public struct TimelineItem
    {
        public double time;
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

    public override double LatestTime => items.Count > 0 ? items[0].time : 0f;

    public override double EarliestTime => items.Count > 0 ? items[items.Count - 1].time : 0f;

    public T ItemAt(double time, double tolerance = 0f)
    {
        return items.Find(a => a.time >= time - tolerance && a.time <= time + tolerance).item;
    }

    public override int IndexAt(double time, double tolerance = 0f)
    {
        return items.FindIndex(a => a.time >= time - tolerance && a.time <= time + tolerance);
    }

    public override double TimeAt(int index)
    {
        if (index == -1) return -1;

        return items[index].time;
    }

    public override int ClosestIndexAfter(double time, double tolerance = 0f)
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

    public override int ClosestIndexAfterInclusive(double time, double tolerance = 0f)
    {
        if (items.Count > 0)
        {
            for (int index = items.Count - 1; index >= 0; index--)
            {
                if (items[index].time >= time - tolerance)
                    return index;
            }
        }

        return -1;
    }

    public override int ClosestIndexBefore(double time, double tolerance = 0f)
    {
        for (int index = 0; index < items.Count; index++)
        {
            if (items[index].time < time + tolerance)
                return index;
        }

        return -1;
    }

    public override int ClosestIndexBeforeInclusive(double time, double tolerance = 0f)
    {
        for (int index = 0; index < items.Count; index++)
        {
            if (items[index].time <= time + tolerance)
                return index;
        }

        return -1;
    }

    public override int ClosestIndexBeforeOrEarliest(double time, double tolerance = 0f)
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

    public override int ClosestIndexBeforeOrEarliestInclusive(double time, double tolerance = 0f)
    {
        int index = ClosestIndexBeforeInclusive(time, tolerance);

        if (index == -1)
        {
            return items.Count - 1;
        }
        else
        {
            return index;
        }
    }

    public bool TryGetPointsAtTime(double time, out int previousIndex, out int nextIndex, out float blend)
    {
        for (int i = 1; i < items.Count; i++)
        {
            if (items[i].time <= time && items[i - 1].time > time)
            {
                previousIndex = i;
                nextIndex = i - 1;
                blend = (float)((time - items[i].time) / (items[i - 1].time - items[i].time));
                return true;
            }
        }

        previousIndex = -1;
        nextIndex = -1;
        blend = 0f;
        return false;
    }

    public void Set(double time, T item, double tolerance = 0f)
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

    public void Insert(double time, T item)
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

    public override void TrimBefore(double minTime)
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

    public override void TrimBeforeExceptLatest(double minTime)
    {
        // start newest, end oldest
        for (int i = 1; i < items.Count; i++)
        {
            if (items[i].time < minTime)
            {
                items.RemoveRange(i, items.Count - i);
                break;
            }
        }
    }

    public override void TrimAfter(double maxTime)
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

    public override void Trim(double minTime, double maxTime)
    {
        TrimBefore(minTime);
        TrimAfter(maxTime);
    }

    /// <summary>
    /// Should be called if using Serializable timelines. Reorders the list ensuring all is correct
    /// </summary>
    public void Validate()
    {
        items.Sort((TimelineItem a, TimelineItem b) => (int)b.time > a.time ? 1 : -1);
    }
}