using UnityEngine;

/// <summary>
/// Marks a networked object as Predictable for Spawner.Spawn on clients. The object can be spawned temporarily and replaced by the real object when/if the server subsequently spawns it.
/// </summary>
public class Predictable : MonoBehaviour
{
    public bool isPrediction { get; set; }

    public bool wasPredicted { get; set; }

    public float spawnTime { get; set; }

    public System.Action onPredictionSuccessful;

    [Tooltip("How many seconds until this predictable object expires, if not confirmed")]
    public float expiryTime = 0.5f;

    private void Awake()
    {
        spawnTime = Time.unscaledTime;
    }

    private void Update()
    {
        if (isPrediction && Time.unscaledTime - spawnTime > expiryTime)
        {
            Debug.Log($"Prediction \"{gameObject}\" expired, bye!");
            Spawner.Despawn(gameObject);
        }
    }
}
