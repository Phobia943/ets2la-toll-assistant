using ETS2LA.Game.Output;
using MautAssistent;
using PathLib;

public static class PlannerPauseIntegration
{
    static readonly PlannedPathData Pause = new() { HasError=true, ErrorMessage="Game is paused.", PlannedPath=[] };
    static void Steering(RollingStartIntegration.Fixture f) => f.Call("OnControlOutput",new ControlEvent {
        ChannelDefinition=new() { Id="LaneAssist.SteeringCorrection" },Variables=new() { steering=0 }});
    static RollingStartIntegration.Fixture Start(Action<bool,string> assert)
    {
        var f=new RollingStartIntegration.Fixture(true,writeDiagnostic:(_,_)=>{});
        f.Set("steeringMonitoringLive",true);f.F8();f.Probe();f.Fresh(.14);Steering(f);
        f.Call("OnPath",f.Plan());f.Plugin.Tick();f.Call("Evaluate");
        assert(f.State.EnableAssists&&!f.Plugin.FaultBraking&&f.Get<DriveAction>("driveAction")==DriveAction.Drive,
            "Pause regression starts with a real accepted route, healthy steering and driving supervisor");
        return f;
    }
    public static void Handoff(Action<bool,string> assert)
    {
        var f=Start(assert);f.Fresh(.14);Steering(f);
        f.Call("OnPath",Pause);
        assert(!f.Plugin.FaultBraking&&f.State.EnableAssists&&f.Get<DriveAction>("driveAction")==DriveAction.Recover,
            "A planner pause just ahead of SDK pause cannot latch the incident's false emergency stop");
        assert((float)f.Call("Evaluate")! == -.5f,"The bounded handoff suppresses acceleration and requests controlled slowing");
        f.Set("paused",true);f.Call("SafetyWatchdog",f.Now);
        assert(f.Get<DriveAction>("driveAction")==DriveAction.Paused&&!f.Plugin.FaultBraking,"Confirmed SDK pause retains its explicit state");
        f.Fresh(.14);Steering(f);f.Call("OnPath",f.Plan());f.Plugin.Tick();f.Call("Evaluate");
        assert(!f.Plugin.FaultBraking,"A fresh real plan after pause does not inherit a false emergency latch");
        f.F9();f.Call("OnPath",Pause);f.Call("Evaluate");
        assert(!f.State.EnableAssists&&!f.Plugin.FaultBraking,"Late pause events after manual cancellation cannot resume or brake");

        var sdkFirst=Start(assert);sdkFirst.Set("paused",true);sdkFirst.Call("OnPath",Pause);
        assert(!sdkFirst.Plugin.FaultBraking&&sdkFirst.Get<DriveAction>("driveAction")==DriveAction.Paused,
            "SDK-first message ordering stays paused without fault");
    }
    public static void Refusals(Action<bool,string> assert)
    {
        foreach(string problem in new[]{"old-route","repeated-pause","telemetry","opposite","missing-trace","no-steering","ordinary-error","waiting"})
        {
            var f=Start(assert);f.Fresh(.14);Steering(f);
            switch(problem) {
                case "old-route": f.Set("lastAcceptedPlanAt",f.Now-.21);break;
                case "repeated-pause":
                    f.Call("OnPath",Pause);
                    f.Set("lastAcceptedPlanAt",f.Now-.21);f.Fresh(.14);Steering(f);break;
                case "telemetry": f.Set("telemetryAt",f.Now-1);break;
                case "opposite": f.Set("headingTurns",0d);break;
                case "missing-trace": f.Set("safetyTrace",null!);break;
                case "no-steering": f.Set("lastSteeringCommand",null!);break;
            }
            f.Call("OnPath",problem=="ordinary-error"?new PlannedPathData {HasError=true,ErrorMessage="No road found.",PlannedPath=[]}:
                problem=="waiting"?new PlannedPathData {HasError=true,ErrorMessage="Waiting for map data...",PlannedPath=[]}:Pause);
            assert(f.Plugin.FaultBraking&&!f.State.EnableAssists,"Pause grace cannot hide "+problem);
        }
    }
}
