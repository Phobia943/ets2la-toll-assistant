using MautAssistent;
using PathLib;

public static class RouteDecisionReviewTests
{
    public static void StationaryPause(Action<bool,string> assert)
    {
        foreach(bool existingFault in new[]{false,true}) {
            var f=new RollingStartIntegration.Fixture(withoutAcc:true,writeDiagnostic:(_,_)=>{});
            try {
                f.Set("options",new TollOptions {CurvePreviewEnabled=false,BriskOpenRoads=false});
                f.F8();f.Probe();f.State.DesiredSpeed=25;f.Fresh(20);f.Plugin.Tick();
                assert(f.Get<DriveAction>("driveAction")==DriveAction.Drive,"Pause case starts with actual previously active driving");
                if(existingFault)f.Call("Disarm","Existing unrelated failure");
                int before=f.Outputs.Count;
                f.Fresh(.09452793896198274/3.6);f.Set("lastAcceptedPlanAt",f.Now-2.9944345);
                f.Call("OnPath",new PlannedPathData{HasError=true,ErrorMessage="Game is paused.",PlannedPath=[]});
                f.Plugin.Tick();
                assert(!f.State.EnableAssists&&f.Outputs.Skip(before).All(e=>e.Variables.aforward is null or 0),
                    "A stale pre-pause route at standstill cannot supply gas or silently rearm");
                assert(f.Plugin.FaultBraking==existingFault,existingFault?
                    "Stationary pause rejection cannot acknowledge a previously latched independent fault":
                    "The actual stationary pause transition does not invent a new latched emergency brake");
            } finally {f.F9();f.Call("StopDiagnostics");}
        }
    }
    public static void RecoveryUsesCompletedCurve(Action<bool,string> assert)
    {
        using var release=new ManualResetEventSlim();
        var f=new RollingStartIntegration.Fixture(withoutAcc:true,writeDiagnostic:(_,_)=>{},buildCurveProfile:(route,origin)=>{
            if(!release.Wait(TimeSpan.FromSeconds(5)))throw new TimeoutException("Review profile was never released");
            return CurveSpeedProfile.Build(route,origin);
        });
        try {
            f.Set("options",new TollOptions{CurvePreviewEnabled=true,BriskOpenRoads=false});
            f.F8();f.Probe();f.State.DesiredSpeed=25;
            var curve=CurveSpeedTests.Approach(f.Point,approach:40);
            f.Set("safetyTrace",curve);f.Call("RequestCurvePreview",curve);
            f.Fresh(25);f.Plugin.Tick();
            assert(f.Get<DriveAction>("driveAction")==DriveAction.Drive&&f.Get<CurveSpeedDecision?>("curveDecision")==null,
                "The pending curve profile must begin without an inherited bound");
            release.Set();var preview=f.Get<CurvePreview>("curvePreview");
            assert(SpinWait.SpinUntil(()=>preview.Read(curve,f.Point,350)!=null,TimeSpan.FromSeconds(2)),
                "The geometric profile actually completes before the recovery tick");
            var expected=preview.Read(curve,f.Point,350)!.Evaluate(f.Point,25,1.2)!;
            assert(expected.MaxAcceleration<-1,"The completed profile demands more than a gentle recovery slowdown");
            f.Fresh(25);f.Set("pathAt",f.Now-.65);f.Plugin.Tick();
            var sample=f.Get<TollPlugin.LongitudinalSnapshot>("longitudinalSnapshot");
            assert(f.Get<DriveAction>("driveAction")==DriveAction.Recover&&sample.Input.RouteKnown,
                "The new shared route policy is exercised during recovery");
            assert(sample.Input.AccelerationSafetyLimit<=expected.MaxAcceleration&&sample.Input.TargetSpeed<=expected.AllowedSpeed,
                "Recovery must evaluate the newly completed tight-curve constraint, not retain only -.5 deceleration");
        } finally {release.Set();f.F9();f.Call("StopDiagnostics");}
    }
    public static void RecoverAndTakeover(Action<bool,string> assert)
    {
        var f=new RollingStartIntegration.Fixture(withoutAcc:true,writeDiagnostic:(_,_)=>{});
        f.Set("options",new TollOptions {CurvePreviewEnabled=false,BriskOpenRoads=false});
        f.F8();f.Probe();f.State.DesiredSpeed=25;f.Fresh(20);f.Plugin.Tick();
        assert(f.Get<DriveAction>("driveAction")==DriveAction.Drive,"Review must begin in actual driving state");
        f.Fresh(20);f.Set("pathAt",f.Now-.65);f.Plugin.Tick();
        var sample=f.Get<TollPlugin.LongitudinalSnapshot>("longitudinalSnapshot");
        assert(f.Get<DriveAction>("driveAction")==DriveAction.Recover&&sample.Input.RouteKnown,
            "The delayed but checked open corridor enters bounded recovery");
        assert(sample.Input.AccelerationSafetyLimit is <=-.5&&sample.Output.Throttle==0&&sample.Output.Brake>0,
            "Shared route acceptance cannot turn recovery into acceleration or free coasting");
        f.Fresh(20);f.Traffic=()=>new(-3,false,"New hazard during route recovery",9,"review-obstacle");
        f.Set("pathAt",f.Now-.65);f.Plugin.Tick();sample=f.Get<TollPlugin.LongitudinalSnapshot>("longitudinalSnapshot");
        assert(sample.Input.AccelerationSafetyLimit is <=-3&&sample.Output.Throttle==0&&sample.Output.Brake>0,
            "A newly observed traffic hazard must remain stronger than the recovery slowdown");

        f.F9();int count=f.Outputs.Count;f.Fresh(20);f.Plugin.Tick();
        assert(!f.State.EnableAssists&&!f.Plugin.FaultBraking&&
            f.Outputs.Skip(count).All(output=>output.Variables.aforward is null or 0),
            "Manual takeover cannot be undone by the retained route sample");
        f.Call("StopDiagnostics");
    }

    public static void BoundedPause(Action<bool,string> assert)
    {
        var f=new RollingStartIntegration.Fixture(withoutAcc:true,writeDiagnostic:(_,_)=>{});
        f.Set("options",new TollOptions {CurvePreviewEnabled=false,BriskOpenRoads=false});
        f.F8();f.Probe();f.State.DesiredSpeed=25;f.Fresh(20);f.Plugin.Tick();
        f.Fresh(20);f.Set("lastAcceptedPlanAt",f.Now-.05);
        f.Call("OnPath",new PlannedPathData{HasError=true,ErrorMessage="Game is paused.",PlannedPath=[]});
        assert(f.Get<DriveAction>("driveAction")==DriveAction.Recover,
            "A fresh planner-pause transition uses only the short original geometry lease");
        assert((bool)f.Call("CurrentRouteUsable",f.Now)!,"That bounded transition can retain its checked corridor");
        f.Fresh(20);f.Set("lastAcceptedPlanAt",f.Now-.21);
        f.Call("SafetyWatchdog",f.Now);
        assert(f.Plugin.FaultBraking&&!(bool)f.Call("CurrentRouteUsable",f.Now)!,
            "Repeated pause messages cannot renew expired pre-pause geometry");
        f.F9();f.Call("StopDiagnostics");
    }
}
