using Mirror;
using UnityEngine;

public class TickerController : NetworkBehaviour
{
    PhysicsTickable physTickable;

    Ticker<PhysicsPlayer.Input, PhysicsTickable.State> physTicker;

    private float timeOnServer;
    private float timeOfLastServerUpdate;

    public float clientExtrapolation = 0.5f;

    public int updatesPerSecond = 30;

    private void Start()
    {
        physTickable = FindObjectOfType<PhysicsTickable>();
        physTicker = physTickable.GetTicker() as Ticker<PhysicsPlayer.Input, PhysicsTickable.State>;
    }

    // Update is called once per frame
    void Update()
    {
        // seek physics
        if (NetworkServer.active)
            physTicker.Seek(Time.time, Time.time);
        else
            physTicker.Seek(timeOnServer + Time.time - timeOfLastServerUpdate + clientExtrapolation, timeOnServer, TickerSeekFlags.IgnoreDeltas);

        // Physics ticker: none of the states get confirmed because we're not adding inputs. We need to do that! A steady flow of inputs is required....

        // send target ticker's state to clients
        if (NetworkServer.active && (int)(Time.time * updatesPerSecond) != (int)((Time.time - Time.deltaTime) * updatesPerSecond))
            RpcState(physTicker.target.MakeState(), physTicker.confirmedStateTime, 0f/*Time.time - physTicker.confirmedStateTime*/);
    }

    [ClientRpc(channel = Channels.Unreliable)]
    private void RpcState(PhysicsTickable.State state, float time, float serverExtrapolation)
    {
        if (!NetworkServer.active)
        {
            physTicker.Reconcile(state, time, TickerSeekFlags.DontConfirm);
            timeOnServer = time + serverExtrapolation;
            timeOfLastServerUpdate = Time.time;
        }
    }
}
