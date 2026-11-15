using ETS2LA.State;

namespace MautAssistent;

public sealed partial class TollPlugin
{
    readonly ImpactGuard impactGuard=new();
    const string ImpactReason="Kollisionssignatur: abrupter Geschwindigkeitsverlust mit neuem Kabinen-/Fahrgestellschaden. Bitte manuell übernehmen.";
    sealed record CollisionReport(DateTimeOffset Utc,ImpactEvidence Evidence);
    CollisionReport? pendingImpact,lastImpact;

    void ObserveImpact(TelemetryFrame frame)
    {
        using var operation=faultBrake.BeginOperation();
        // No route lock, model read, map query or disk operation on this path.
        // Manual takeover uses this same short lock and cancels pending reports.
        lock(steeringSync) {
            if(!faultBrake.OperationCurrent)return;
            var state=State;
            var hit=impactGuard.Observe(new(frame.Arrived,frame.Time,frame.Physics.SimulatedTime,
                _IsRunning&&Volatile.Read(ref active)&&state.EnableAssists&&!Volatile.Read(ref preview),
                frame.Paused,frame.Active,frame.Speed,frame.Position.X,frame.Position.Y,frame.Position.Z,
                new(frame.Physics.CabinDamage,frame.Physics.ChassisDamage)));
            if(hit==null)return;
            faultBrake.RunCurrent(()=> {
                state.EnableAssists=false;
                faultBrake.Trip();
                collectingSteeringFailure=pendingSteeringFailure=null;
                pendingImpact=new(DateTimeOffset.UtcNow,hit);
            });
        }
    }

    void DrainImpactFailure()
    {
        CollisionReport? report;
        lock(steeringSync) {report=pendingImpact;pendingImpact=null;}
        if(report==null)return;
        lastImpact=report;
        StopDriving(ImpactReason,DriveAction.EmergencyStop);
    }
}
