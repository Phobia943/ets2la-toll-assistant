using System.Numerics;
namespace MautAssistent;
// Lifetime is one unresolved set of scene/barrier/proof constraints. Speed
// reserves changing while the truck stops cannot release an established hold.
public sealed class SceneStopHold
{
    readonly SceneHoldBrake brake=new();
    public bool Holding {get;private set;}
    public string? OwnerKey {get;private set;}
    public void Reset(){Holding=false;OwnerKey=null;brake.Reset();}
    public float Step(double time,double speed,Vector3 position,double distance,bool permitted,string ownerKey="scene-constraint")
    {
        if(!permitted||string.IsNullOrWhiteSpace(ownerKey)||!double.IsFinite(distance)||!double.IsFinite(speed)){Reset();return 0;}
        if(OwnerKey!=ownerKey) {
            // Replacing a resolved near constraint with a new far constraint
            // must allow approach again. A new still-near reason preserves the
            // same physical hold and its already established pedal floor.
            if(!Holding||distance>3||Math.Abs(speed)>.5)Reset();
            OwnerKey=ownerKey;
        }
        if(Math.Abs(speed)<=.5&&distance-Math.Max(0,speed)*1.5<=.5)Holding=true;
        return brake.Step("unresolved-scene-stand",time,speed,position,Holding);
    }
    public float Limit(float acceleration)=>Holding?(float.IsFinite(acceleration)?Math.Min(acceleration,-.6f):-.6f):acceleration;
}
