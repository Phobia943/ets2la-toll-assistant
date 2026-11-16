using System.Numerics;
namespace MautAssistent;

public sealed record WheelSample(Vector3 Position,bool Simulated,bool Steerable,bool Liftable,
    bool OnGround,float Lift,float Steering);
public sealed record WheelAxleProof(Vector3 Axle,float HookDistance,int ActiveAxles,string? Issue)
{
    public bool Supported=>Issue==null;
    public bool ContactStable {get;init;}
    public bool LiftTransition {get;init;}
}
public static class WheelAxle
{
    // An effective fixed bogie centre is an explicit no-slip prediction model,
    // not a measured tyre contact constraint. Never derive it from a body box.
    public static WheelAxleProof Trailer(Vector3 hook,IReadOnlyList<WheelSample>? wheels)
    {
        WheelAxleProof Unknown(string reason)=>new(default,float.NaN,0,reason);
        if(!SceneSnapshotProtocol.Finite(hook)||hook.Length()>50||wheels==null||wheels.Count is <2 or >16)
            return Unknown("Echte Kupplungs- und Radkoordinaten fehlen");
        if(wheels.Any(w=>!SceneSnapshotProtocol.Finite(w.Position)||w.Position.Length()>50||
            !float.IsFinite(w.Lift)||w.Lift is <0 or >1||w.Lift>.05f&&!w.Liftable||!float.IsFinite(w.Steering)||Math.Abs(w.Steering)>10))
            return Unknown("Ungültige Radmessung");
        // A steerable trailer requires a different model even if its current
        // angle happens to be zero. Non-simulated decorative wheels are unused.
        if(wheels.Any(w=>w.Simulated&&w.Steerable))return Unknown("Lenkbare Aufliegerachsen noch nicht modelliert");
        var configured=wheels.Where(w=>w.Simulated).OrderBy(w=>w.Position.Z).ThenBy(w=>w.Position.X).ToArray();
        if(configured.Length is <2 or >16||configured.Any(w=>Math.Abs(w.Steering)>.02f))
            return Unknown("Keine stabile gerade Aufliegerachsgruppe gemessen");
        if(configured.Where((w,i)=>configured.Skip(i+1).Any(v=>Vector3.Distance(w.Position,v.Position)<.00001f)).Any())
            return Unknown("Doppelte Radkoordinaten");
        var groups=new List<List<WheelSample>>();
        foreach(var wheel in configured) {
            if(groups.Count==0||wheel.Position.Z-groups[^1][0].Position.Z>.1f)groups.Add([]);
            groups[^1].Add(wheel);
        }
        var axles=new List<Vector3>();bool contactStable=true,liftTransition=false;
        foreach(var group in groups) {
            var left=group.Where(w=>w.Position.X<=-.3f).ToArray();var right=group.Where(w=>w.Position.X>=.3f).ToArray();
            if(left.Length is <1 or >2||left.Length!=right.Length||left.Length+right.Length!=group.Count)
                return Unknown("Unvollständige Aufliegerachsgruppe");
            bool Pair(WheelSample a,WheelSample b)=>Math.Abs(a.Position.X+b.Position.X)<=.2f&&Math.Abs(a.Position.Y-b.Position.Y)<=.2f;
            // At most two tyres on either side. Existence of a complete match,
            // rather than a greedy pairing, is invariant under provider order.
            bool matched=left.Length==1?Pair(left[0],right[0]):
                Pair(left[0],right[0])&&Pair(left[1],right[1])||Pair(left[0],right[1])&&Pair(left[1],right[0]);
            if(!matched)return Unknown("Radpositionen ergeben keine vollständige symmetrische Achse");
            bool raised=group.All(w=>w.Liftable&&w.Lift>=.95f&&!w.OnGround);
            if(raised)continue;
            bool transition=group.Any(w=>w.Lift>.05f);
            liftTransition|=transition;contactStable&=!transition&&group.All(w=>w.OnGround);
            // Contact flicker affects confidence, NOT the configured axle's
            // position. A consumer must monitor this confidence over time.
            axles.Add(group.Aggregate(Vector3.Zero,(a,b)=>a+b.Position)/group.Count);
        }
        if(groups.Count>4||axles.Count is <1 or >4||axles.Max(p=>p.Z)-axles.Min(p=>p.Z)>4||
            axles.Max(p=>p.Y)-axles.Min(p=>p.Y)>.5f)
            return Unknown("Aufliegerachsgruppe außerhalb des Fahrmodells");
        var axle=axles.Aggregate(Vector3.Zero,(a,b)=>a+b)/axles.Count;
        float distance=axle.Z-hook.Z; // SCS model space: forward is -Z.
        if(!float.IsFinite(distance)||distance is <1 or >20||Math.Abs(axle.X-hook.X)>.25f)
            return Unknown("Kupplung liegt nicht vor der festen Aufliegerachsgruppe");
        return new(axle,distance,axles.Count,null){ContactStable=contactStable,LiftTransition=liftTransition};
    }
}
