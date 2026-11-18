using System.Diagnostics;
using MautAssistent;

public static class BarrierDecisionAgeReviewTests
{
    public static void PlannedStationaryHold(Action<bool,string> check)
    {
        var f=new CanonicalGateTickReview.Fixture();
        try {
            var router=f.Get<TollRouter>("router");
            typeof(TollRouter).GetProperty("StopBoundary")!.SetValue(router,new System.Numerics.Vector3(0,0,32));
            typeof(TollRouter).GetProperty("StopKey")!.SetValue(router,router.CurrentKey);
            f.Tick(0);
            check(f.Get<float>("holdPedalFloor")>=.35f&&f.Get<ApproachStopController>("approachStop").Holding,
                "The actual Tick must establish its protected planned stop before aging the route");
            f.Fresh(0);f.Set("pathAt",f.Now-.301);f.Set("lastAcceptedPlanAt",f.Now-.301);
            f.Plugin.Tick();
            var output=f.Outputs.Last(c=>c.ChannelDefinition.Id==TollPlugin.LongitudinalChannelId);
            check(f.Get<float>("holdPedalFloor")>=.35f&&output.Variables.aforward==0&&output.Variables.abackward<0,
                "A still accepted 301 ms toll corridor must retain the physical hold floor without granting gas");
            f.State.EnableAssists=false;f.Fresh(0);f.Plugin.Tick();
            check(f.Get<float>("holdPedalFloor")==0,
                "Manual authority still releases the planned hold independently of route acceptance");
        } finally {f.Stop();}
    }
    public static void ActualTick(Action<bool,string> check)
    {
        var f=new CanonicalGateTickReview.Fixture();
        try {
            var until=Stopwatch.StartNew();
            while(until.Elapsed.TotalSeconds<1.2){f.Tick(0);Thread.Sleep(20);}
            check(f.Get<TollDecision>("decision").Phase=="Langsame Durchfahrt",
                "The actual fixture must first confirm its own open toll gate");
            f.Fresh(0);f.Set("pathAt",f.Now-.301);f.Set("lastAcceptedPlanAt",f.Now-.301);
            f.Plugin.Tick();
            var bound=f.Get<BarrierBindingResult>("barrierBinding");
            var output=f.Outputs.Last(c=>c.ChannelDefinition.Id==TollPlugin.LongitudinalChannelId);
            check(bound.MapCorridorKnown&&bound.LiveBarrierBound&&output.Variables.aforward>0&&output.Variables.abackward==0,
                "A supervisor-accepted 301 ms corridor cannot invent an unbound-gate stop at an open own booth");

            f.Map.Own.state=1;f.Fresh(10/3.6);f.Set("pathAt",f.Now-.301);f.Set("lastAcceptedPlanAt",f.Now-.301);
            f.Plugin.Tick();bound=f.Get<BarrierBindingResult>("barrierBinding");
            output=f.Outputs.Last(c=>c.ChannelDefinition.Id==TollPlugin.LongitudinalChannelId);
            check(bound.LiveBarrierBound&&output.Variables.aforward==0&&output.Variables.abackward<0,
                "The same older accepted geometry must still obey a newly closed own gate");

            f.Map.Own.state=3;f.Fresh(0);f.Set("pathAt",f.Now-.301);f.Set("lastAcceptedPlanAt",f.Now-.301);
            f.Set("gatesAt",f.Now-.25);f.Plugin.Tick();bound=f.Get<BarrierBindingResult>("barrierBinding");
            check(!bound.LiveBarrierBound,
                "Shared route acceptance does not extend the independent live gate state deadline");

            f.Fresh(0);f.Set("pathAt",f.Now-1.01);f.Set("lastAcceptedPlanAt",f.Now-1.01);
            f.Plugin.Tick();
            check(!f.State.EnableAssists&&f.Plugin.FaultBraking,
                "An actually expired toll corridor still loses driving authority");
        } finally {f.Stop();}
    }
}
