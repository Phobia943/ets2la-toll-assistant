using MautAssistent;
using PathLib;

public static class RouteDecisionIntegration
{
    public static void RecordedDelay(Action<bool,string> check)
    {
        var f=new RollingStartIntegration.Fixture(withoutAcc:true,writeDiagnostic:(_,_)=>{});
        f.Set("options",new TollOptions{CurvePreviewEnabled=false,BriskOpenRoads=false});
        f.F8();f.Probe();f.State.DesiredSpeed=25;f.Fresh(20);
        f.Set("pathAt",f.Now-.301); // observed 18 Nov: current corridor, delayed planner
        f.Plugin.Tick();
        var result=f.Get<TollPlugin.LongitudinalSnapshot>("longitudinalSnapshot");
        check(f.Get<DriveAction>("driveAction")==DriveAction.Drive&&result.Input.RouteKnown,
            "The actual Tick must use the supervisor's accepted 301 ms corridor");
        check(result.Published&&result.Output.Brake==0&&!result.Output.Fault,
            "That route delay alone cannot replace a clear-road target with a stopping command");

        f.Fresh(20);f.Set("pathAt",f.Now-.301);f.Traffic=()=>new(-2,false,"Stopped vehicle",15,"vehicle");
        f.Plugin.Tick();result=f.Get<TollPlugin.LongitudinalSnapshot>("longitudinalSnapshot");
        check(result.Output.Brake>0&&result.Output.Throttle==0,
            "A currently observed hazard still brakes on the same accepted route");

        f.Call("OnPath",new PlannedPathData{HasError=true,ErrorMessage="No current path",PlannedPath=[]});
        check(f.Plugin.FaultBraking&&!(bool)f.Call("CurrentRouteUsable",f.Now)!,
            "A new rejected path immediately invalidates the previous decision");
        f.F9();check(!(bool)f.Call("CurrentRouteUsable",f.Now)!,"Manual takeover cannot reuse the accepted route sample");
        f.Call("StopDiagnostics");
    }

    public static void Deadlines(Action<bool,string> check)
    {
        var frame=new DriveFrame(10,20,true,false,true,RouteFeed.Valid,.301,true,true);
        var sample=new DriveSample(frame,DriveAction.Drive);
        check(DriveSupervisor.RouteUsable(sample,10),"Recorded delay is usable on a checked open corridor");
        check(!DriveSupervisor.RouteUsable(sample,10.201),"Reading a sample cannot renew its decision deadline");
        check(!DriveSupervisor.RouteUsable(sample with {Frame=frame with{RouteAge=.499}},10.002),
            "Open-road route age still expires at its original half-second boundary");
        check(!DriveSupervisor.RouteUsable(sample with {Frame=frame with{CanRecover=false,RouteAge=.999}},10.002),
            "A critical approach retains its original one-second route boundary");
        check(DriveSupervisor.RouteUsable(new(frame with{RouteAge=.7},DriveAction.Recover),10),
            "Bounded recovery can use the corridor with its separate no-acceleration constraint");
        foreach(var action in new[]{DriveAction.Idle,DriveAction.Paused,DriveAction.AwaitRoute,DriveAction.RejectStart,DriveAction.EmergencyStop})
            check(!DriveSupervisor.RouteUsable(new(frame,action),10),"Only accepted drive/recovery decisions authorize a route");
        foreach(var altered in new[]{frame with{Route=RouteFeed.Rejected},frame with{TelemetryFresh=false},frame with{Paused=true},
            frame with{Enabled=false},frame with{RouteAge=-.1},frame with{RouteAge=double.NaN}})
            check(!DriveSupervisor.RouteUsable(new(altered,DriveAction.Drive),10),"Bad or revoked route evidence cannot become usable");
    }
}
