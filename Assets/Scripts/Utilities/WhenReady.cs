using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Call WhenReady&lt;ObjType&gt;.Execute(x => ...) to run some code / get something on an object that might not have been spawned yet
/// 
/// Example:
/// Player.Start()
/// {
///     WhenReady&lt;PlayerManager&gt;.Execute(this, x => x.AddPlayer(this))
/// }
/// 
/// If the object is spawned it'll happen immediately, otherwise it'll wait until it's spawned
/// </summary>
public static class WhenReady<T>
{
    public struct Caller
    {
        public object action;
        public Component requester; // we can null-check this before calling
        public bool onlyForFirstInstance; // useful for things intended as singletons
    }

    private static Dictionary<System.Type, List<Caller>> deferredCallers = new Dictionary<Type, List<Caller>>();
    private static Dictionary<System.Type, List<object>> instances = new Dictionary<Type, List<object>>();

    /// <summary>
    /// Calls actionToExecute when an instance of type T is created, or instantly if it is already created. Great for working with singleton order-of-operations issues.
    /// 
    /// If shouldOnlyCallForFirstInstance is false, this action will be called for all instances of that object and whenever one is spawned.
    /// </summary>
    public static void Execute(Component requester, Action<T> actionToExecute, bool shouldOnlyCallForFirstInstance = true)
    {
        // Execute on existing instance(s)
        if (instances.TryGetValue(typeof(T), out List<object> instanceList) && instanceList.Count > 0)
        {
            if (shouldOnlyCallForFirstInstance)
            {
                InvokeSafely(actionToExecute, (T)instanceList[0]);
                return; // <----- EARLY OUT - we called what we wanted
            }
            else
            {
                foreach (object instance in instanceList)
                    InvokeSafely(actionToExecute, (T)instance);
                // We _don't_ return here because it wants to be called for every instance, including future ones
            }
        }

        // Create caller to be called later
        if (!deferredCallers.TryGetValue(typeof(T), out List<Caller> callerList))
            deferredCallers[typeof(T)] = callerList = new List<Caller>();

        callerList.Add(new Caller() { action = actionToExecute, requester = requester, onlyForFirstInstance = shouldOnlyCallForFirstInstance });
    }

    /// <summary>
    /// Called when an object is spawned to inform subscribers we're available now and execute the code. For stability you must call OnUnready when this object is no longer ready or available.
    /// </summary>
    public static void Register(T availableObject)
    {
        // Record this instance for later subscribers
        if (!instances.TryGetValue(typeof(T), out List<object> instanceList))
            instances[typeof(T)] = instanceList = new List<object>();

        instanceList.Add(availableObject);

        // Call deferred callers now for this instance
        if (deferredCallers.TryGetValue(typeof(T), out List<Caller> callerList))
        {
            for (int i = 0, e = callerList.Count; i < e; i++)
            {
                if (callerList[i].requester != null)
                    InvokeSafely(callerList[i].action as Action<T>, availableObject);

                // Remove it if we no longer want to call this caller in the future
                if (callerList[i].onlyForFirstInstance || callerList[i].requester == null)
                {
                    callerList.RemoveAt(i);
                    i--; e--;
                }
            }
        }
    }

    /// <summary>
    /// Called when an object that was previously ready is no longer ready
    /// </summary>
    public static void Unregister(T unavailableObject)
    {
        if (instances.TryGetValue(typeof(T), out List<object> instanceList))
        {
            if (instanceList.Contains(unavailableObject))
            {
                instanceList.Remove(unavailableObject);
                return; // <------ Early out / success
            }
        }

        Debug.LogError($"{nameof(WhenReady<T>)}.{nameof(WhenReady<T>.Unregister)} called for {unavailableObject} but this object was never reportedly ready!");
    }
    
    private static void InvokeSafely(Action<T> action, T parameter)
    {
        try
        {
            action.Invoke(parameter);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }
}