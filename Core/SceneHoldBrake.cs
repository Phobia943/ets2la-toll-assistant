using System.Numerics;
namespace MautAssistent;
public sealed class SceneHoldBrake
{
    readonly PlannedHoldBrake brake=new();
    Vector3? origin;string? key;
    public void Reset(){origin=null;key=null;brake.Reset();}
    public float Step(string stopKey,double time,double speed,Vector3 position,bool holding)
    {
        if(!holding||!SceneSnapshotProtocol.Finite(position)){Reset();return 0;}
        if(key!=stopKey){Reset();key=stopKey;origin=position;}
        origin??=position;
        // Changes in an observed obstacle or unknown-scope target cannot be
        // mistaken for physical creep of the stationary truck.
        return brake.Step(stopKey,time,speed,-Vector3.Distance(origin.Value,position),true);
    }
}
