using UnityEngine;

public class ExcludeCameraFromDebugDraws : MonoBehaviour
{
    private Camera ignoredCamera;

    private void OnEnable()
    {
        if (TryGetComponent(out ignoredCamera))
            DebugDraw.SetCameraIgnored(ignoredCamera, true);
    }

    private void OnDisable()
    {
        if (ignoredCamera)
            DebugDraw.SetCameraIgnored(ignoredCamera, false);
    }
}
