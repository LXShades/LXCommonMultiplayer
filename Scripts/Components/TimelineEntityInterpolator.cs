using System;
using UnityEngine;

/// <summary>
/// Helper for interpolating the corrections on predictive timeline entities
/// 
/// Works by detecting the difference between a predicted state and the final state.
/// We accumulate the difference in interpolationOffset and reapply it to our position, and then we constantly and smoothly reduce the difference to 0.
/// </summary>
public class TimelineEntityInterpolator : MonoBehaviour
{
    private readonly Vector3 InvalidVector = new Vector3(1337.43f, 925.42847f, 234.324f); // awks hack

    public float mispredictionInterpolationSmoothing = 0.3f;

    private Vector3 lastPredictionStatePosition;
    private double lastPredictionStateTime;

    private Vector3 interpolationOffset;
    private Vector3 interpolationOffsetVelocity;

    private bool stateIsBeingRecalculated;

    private Timeline.EntityBase entity;
    private Func<double, Vector3> getPositionAtTime;

    private void LateUpdate()
    {
        if (mispredictionInterpolationSmoothing == 0f)
            return;

        if (stateIsBeingRecalculated)
        {
            // compare the new state at our realtimeish state time compared to the previous predicted state we had at that time
            Vector3 recalculatedPosition = getPositionAtTime(lastPredictionStateTime);
            if (recalculatedPosition != InvalidVector)
                interpolationOffset += lastPredictionStatePosition - recalculatedPosition; // this offset is what we can add to return to our misprediction. We then blend it out towards 0 to match the latest more correct prediction

            stateIsBeingRecalculated = false;
        }

        transform.position += interpolationOffset;
        interpolationOffset = Vector3.SmoothDamp(interpolationOffset, Vector3.zero, ref interpolationOffsetVelocity, mispredictionInterpolationSmoothing);
    }

    public void SetOwningEntity<TState, TInput>(Timeline.Entity<TState, TInput> owningEntity, Func<TState, Vector3> positionGetter) where TState : ITickerState<TState> where TInput : ITickerInput<TInput>
    {
        entity = owningEntity;
        getPositionAtTime = (double time) =>
        {
            if (owningEntity.stateTrack.TryGetPointsAtTime(time, out int index, out _, out _))
            {
                return positionGetter(owningEntity.stateTrack[index]);
            }

            return InvalidVector;
        };
    }

    public void OnAboutToRecalculateLatestState()
    {
        if (mispredictionInterpolationSmoothing == 0f)
            return;

        if (entity != null && getPositionAtTime != null)
        {
            Vector3 positionAtLatestTime = getPositionAtTime(entity.latestStateTime);

            if (positionAtLatestTime != InvalidVector)
            {
                lastPredictionStatePosition = getPositionAtTime(entity.latestStateTime);
                lastPredictionStateTime = entity.latestStateTime;
                stateIsBeingRecalculated = true;
            }
        }
    }
}
