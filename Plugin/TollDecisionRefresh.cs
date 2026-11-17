using ETS2LA.State;
namespace MautAssistent;
public sealed partial class TollPlugin
{
    // Caller owns sync and a freshly checked route. This computes constraints;
    // normal pedal publication remains exclusively in the own control Tick.
    void UpdateTollDecision(double now)
    {
        var state = State;
        bool allow = state.EnableAssists && (!active || state.DrivingMode != DrivingMode.LaneAssistOnly);
        if(!router.StopBoundary.HasValue){approachStop.Reset();if(!sceneStopping)ClearPlannedHold();}
        if (!state.EnableAssists) {passage.Reset();controller.Reset();tracked=null;}
        RouteTrace? sampled=null;
        longitudinalGateOrigin=gatesOrigin;longitudinalGateEpoch=gatesSourceEpoch;
        Gate? gate = Fresh(now) && path != null ? RouteMatcher.Select(path, position, gates, tracked,out sampled,speed) : null;
        if(gate==null && tracked!=null && Fresh(now)) {
            var live=RouteMatcher.FindLiveGate(path,gates,tracked,passage.PositionFor(tracked));
            if(live!=null)gate=passage.AfterCrossing(tracked,position,live.GetWorldCoordinates(),
                live.state is >=0 and <=3?(BarrierState)live.state:BarrierState.Unknown,
                sampled!=null&&sampled.ProjectForTracking(position).Offset<=3,decision.Phase=="Langsame Durchfahrt");
            // An observer must be able to reacquire a new gate. An active
            // constraint must instead retain the lost target and disarm.
            if(gate==null && !active) {controller.Reset();tracked=null;passage.Reset();
                gate=RouteMatcher.Select(path!,position,gates,null,out sampled,speed);}
        }
        if(gate!=null && sampled!=null) {
            var live=RouteMatcher.FindLiveGate(path,gates,gate.Key,passage.PositionFor(gate.Key));
            if(live!=null)passage.Capture(gate.Key,live.GetWorldCoordinates(),sampled);
        }
        candidate = gate;
        barrierBinding=ReadBarrierBinding();
        gate=ApplyBarrierBinding(gate,barrierBinding);
        bool manual = router.SelectedGate is int selected && router.PaymentFor(selected)==TollPayment.Manual &&
            (barrierBinding.LiveBarrierBound&&barrierBinding.LiveGate?.Key==gate?.Key||
            gates.Any(g=>g.id==selected && gate?.Key==RouteMatcher.Key(g)));
        decision = controller.Step(new(now, speed, allow, Fresh(now), gate, manual,
            manual?router.ManualStopOffset(options.StopOffsetMetres):null));
        if(allow && Fresh(now) && router.StopBoundary.HasValue)
            decision=EvaluatePlannedStop(path!);
        else if (allow && sampled != null && path != null)
            decision=router.LimitShortChange(path,sampled,position,speed,decision);
        tracked = decision.GateKey;
        if(tracked==null)passage.Reset();
        evaluatedAt = now;
        decisionArmed = state.EnableAssists && Fresh(now);
        if (active && allow && decisionArmed && decision.RequiresTakeover) Disarm(decision.Phase);
    }
}
