using System.Numerics;

namespace MautAssistent;

// A confirmed drivable map lane is not an observation of the barrier's state.
public sealed record TollBarrierPlan(string PlazaKey,ulong PrefabUid,int GateId,Vector3 Anchor,Vector3 Forward,
    string? RouteIdentity=null)
{
    public string HoldKey=>FormattableString.Invariant($"mapped-barrier:{PlazaKey}:{GateId}:{Anchor.X:F3}:{Anchor.Y:F3}:{Anchor.Z:F3}");
}

public sealed record TollBarrierLimit(TollDecision Decision,bool Holding,bool WithinHorizon);

/// <summary>Normal approach to a selected map barrier until real SDK binding exists.</summary>
public static class TollBarrierBinding
{
    public static double Horizon(double speed,TollOptions options)
    {
        double v=double.IsFinite(speed)?Math.Max(0,speed):0;
        return Math.Max(200,v*v/(2*options.ComfortableDeceleration)+v*1.5+40);
    }

    public static TollBarrierLimit Evaluate(double speed,double? distance,bool liveBarrierBound,TollOptions options)
    {
        // A positive binding removes only this extra unbound-map constraint.
        // The existing TollController still consumes the REAL state (Unknown
        // remains not-open), retains its opening delay and crossing memory.
        if(liveBarrierBound)return new(TollDecision.Idle("Mautschranke live zugeordnet"),false,false);
        double v=double.IsFinite(speed)&&speed>=0?speed:0;
        if(distance is double far&&double.IsFinite(far)&&far>Horizon(v,options))
            return new(TollDecision.Idle("Mautspur geplant; Schranke noch außerhalb der Anfahrt"),false,false);
        if(distance is not double d||!double.IsFinite(d)||!double.IsFinite(speed)||speed<-.1)
            return new(new("Mautanker noch nicht sicher verfolgbar – geplanter Halt",0,-(float)Math.Max(.35,Math.Min(options.ComfortableDeceleration,v)),null),true,true);
        // No emergency/lost-target latch here: absence of the short-range SDK
        // feed is expected before a toll plaza and must plan a normal stop.
        double remaining=Math.Max(0,d-options.StopOffsetMetres-v*1.5);
        double cap=remaining<=.5?0:Math.Min(Math.Sqrt(2*options.ComfortableDeceleration*remaining),.6*remaining);
        float acceleration=cap==0?-(float)Math.Max(.35,Math.Min(options.ComfortableDeceleration,v)):
            (float)Math.Clamp((cap-v)*.8,-options.ComfortableDeceleration,.35);
        return new(new(cap==0?"Warten auf Live-Zuordnung der Mautschranke":"Geplante Anfahrt zur noch nicht live zugeordneten Mautschranke",cap,acceleration,null),cap==0,true);
    }
}
