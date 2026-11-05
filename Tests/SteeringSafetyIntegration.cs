using System.Reflection;
using ETS2LA.Game.Output;
using ETS2LA.State;
using MautAssistent;

public static class SteeringSafetyIntegration
{
    static ControlEvent Command(float value,string id="LaneAssist.SteeringCorrection")=>new() {
        ChannelDefinition=new(){Id=id},Variables=new(){steering=value}};
    public static void Run(Action<bool,string> assert)
    {
        var f=new RollingStartIntegration.Fixture(true);
        f.Set("steeringMonitoringLive",true);f.F8();f.Probe();
        var guard=f.Get<SteeringFeedGuard>("steeringGuard");
        f.Call("CheckSteeringOutput");
        assert((float)f.Call("Evaluate")!<0&&!f.Plugin.FaultBraking,"Before the first steering frame a new start must hold, not accelerate or trip immediately");
        f.Call("OnControlOutput",Command(0));f.Call("Evaluate");
        assert(f.Get<SteeringFeedState>("steeringFeedState")==SteeringFeedState.Healthy&&f.State.EnableAssists,
            "A fresh zero steering command is a valid native steering frame");
        f.Call("OnControlOutput",Command(float.NaN));
        f.Call("OnControlOutput",Command(0,"some.other.channel"));
        Thread.Sleep(190);
        // Keep route and telemetry perfectly fresh: route validity alone must
        // not conceal the native lane assist silently ceasing its output.
        f.Fresh();
        var sync=typeof(TollPlugin).GetField("sync",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(f.Plugin)!;
        Task? watchdog=null;
        try {
            Monitor.Enter(sync);
            watchdog=Task.Run(()=>f.Call("CheckSteeringOutput"));
            assert(watchdog.Wait(TimeSpan.FromMilliseconds(300)),"Steering-loss braking must not wait for route work");
            assert(f.Plugin.FaultBraking&&!f.State.EnableAssists&&f.Outputs[^1].Variables.abackward==-1,
                "Missing steering must immediately latch the independent brake despite a valid route");
            int count=f.Outputs.Count;
            f.Call("CheckSteeringOutput");
            assert(f.Outputs.Count>count&&f.Outputs[^1].Variables.abackward==-1,"A latched steering fault must keep its brake channel alive while the route lock stays busy");
        } finally {Monitor.Exit(sync);watchdog?.Wait(TimeSpan.FromSeconds(2));}
        f.Call("Evaluate");
        assert(f.Plugin.FaultBraking&&!f.Plugin.Active,"Deferred route bookkeeping must preserve the steering fault latch");
        f.Call("OnControlOutput",Command(.1f));
        f.Fresh();f.Call("OnPath",f.Plan());
        assert(f.Plugin.FaultBraking,"A recovered steering frame and route cannot silently release braking");
        f.Call("OnBrakeInput",null,new ETS2LA.Controls.ControlChangeEventArgs(.5f,TollPlugin.BrakeControl));
        f.Call("CheckSteeringOutput");
        assert(!f.Plugin.FaultBraking&&!f.State.EnableAssists,"Manual brake takeover must cancel supervision and release the fault");
        f.Call("OnBrakeInput",null,new ETS2LA.Controls.ControlChangeEventArgs(0f,TollPlugin.BrakeControl));
        f.Call("CheckSteeringOutput");
        assert(!f.State.EnableAssists,"Releasing the physical brake cannot restart the assistant");

        var acc=new RollingStartIntegration.Fixture(true);acc.Set("steeringMonitoringLive",true);
        acc.State.DrivingMode=DrivingMode.AdaptiveCruiseControlOnly;acc.F8();acc.Probe();
        Thread.Sleep(370);acc.Fresh();acc.Call("CheckSteeringOutput");
        assert(!acc.Plugin.FaultBraking&&acc.State.EnableAssists,"ACC-only mode must not require a lane-steering output");
        var paused=new RollingStartIntegration.Fixture(true);paused.Set("steeringMonitoringLive",true);paused.F8();paused.Probe();
        paused.Set("paused",true);Thread.Sleep(370);paused.Call("CheckSteeringOutput");
        assert(!paused.Plugin.FaultBraking,"Paused simulation must not trip the steering feed monitor");
    }

    public static void BusyManualRelease(Action<bool,string> assert)
    {
        var f=new RollingStartIntegration.Fixture(true);f.Set("steeringMonitoringLive",true);f.F8();f.Probe();
        f.Call("OnControlOutput",Command(0));f.Call("CheckSteeringOutput");Thread.Sleep(190);f.Call("CheckSteeringOutput");
        assert(f.Plugin.FaultBraking,"Regression requires a pending steering fault");
        var sync=typeof(TollPlugin).GetField("sync",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(f.Plugin)!;
        Task? brake=null;
        try {
            Monitor.Enter(sync);
            int before=f.Outputs.Count;
            brake=Task.Run(()=>f.Call("OnBrakeInput",null,new ETS2LA.Controls.ControlChangeEventArgs(.5f,TollPlugin.BrakeControl)));
            assert(SpinWait.SpinUntil(()=>!f.Plugin.FaultBraking&&f.Outputs.Count>=before+4,TimeSpan.FromMilliseconds(300)),"Manual takeover must release the fault latch before waiting for route bookkeeping");
            f.Call("CheckSteeringOutput");
            assert(!f.Plugin.FaultBraking&&f.Outputs[^1].Variables.abackward==0,"The independent timer cannot reassert a manually acknowledged fault");
        } finally {Monitor.Exit(sync);if(brake!=null&&!brake.Wait(TimeSpan.FromSeconds(3)))throw new Exception("Manual takeover did not settle");}
        f.Fresh(0);f.Call("Evaluate");
        assert(!f.Plugin.FaultBraking&&!f.State.EnableAssists,"Discarded pending diagnostics cannot retrip after manual takeover");
    }

    public static void BusyHealthyFeed(Action<bool,string> assert)
    {
        var f=new RollingStartIntegration.Fixture(true);f.Set("steeringMonitoringLive",true);f.F8();f.Probe();
        f.Call("OnControlOutput",Command(.1f));f.Call("CheckSteeringOutput");
        var sync=typeof(TollPlugin).GetField("sync",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(f.Plugin)!;
        using var stop=new CancellationTokenSource();Task? producer=null;int delivered=0;
        try {
            Monitor.Enter(sync);
            producer=Task.Run(()=>{
                while(!stop.IsCancellationRequested){f.Call("OnControlOutput",Command(.1f));Interlocked.Increment(ref delivered);Thread.Sleep(15);}
            });
            assert(SpinWait.SpinUntil(()=>Volatile.Read(ref delivered)>=5,TimeSpan.FromMilliseconds(300)),"Live steering output ingestion must not wait for route work");
            Thread.Sleep(650);f.Call("CheckSteeringOutput");
            assert(!f.Plugin.FaultBraking&&f.State.EnableAssists&&f.Get<SteeringFeedState>("steeringFeedState")==SteeringFeedState.Healthy,
                "A busy route lock cannot manufacture a missing-steering fault while commands are arriving");
        } finally {stop.Cancel();Monitor.Exit(sync);if(producer!=null&&!producer.Wait(TimeSpan.FromSeconds(3)))throw new Exception("Steering producer did not exit");}
    }

    public static void PauseDeliveryWhileBusy(Action<bool,string> assert)
    {
        var f=new RollingStartIntegration.Fixture(true);f.Set("steeringMonitoringLive",true);f.F8();f.Probe();
        f.Call("OnControlOutput",Command(0));f.Call("CheckSteeringOutput");
        var sync=typeof(TollPlugin).GetField("sync",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(f.Plugin)!;
        try {
            Monitor.Enter(sync);
            var delivery=Task.Run(()=>f.Call("OnTelemetry",new ETS2LA.Game.Telemetry.GameTelemetryData {sdkActive=true,paused=true,time=22}));
            assert(delivery.Wait(TimeSpan.FromMilliseconds(300)),"Pause delivery cannot wait for route work");
            Thread.Sleep(190);f.Call("CheckSteeringOutput");
            assert(!f.Plugin.FaultBraking,"A delivered pause must suppress missing-steering faults before route bookkeeping consumes it");
            f.Call("OnTelemetry",new ETS2LA.Game.Telemetry.GameTelemetryData {sdkActive=true,paused=true,time=23});
            var resume=Task.Run(()=>f.Call("OnTelemetry",new ETS2LA.Game.Telemetry.GameTelemetryData {sdkActive=true,paused=false,time=24}));
            assert(resume.Wait(TimeSpan.FromMilliseconds(300)),"Resume delivery cannot wait for route work");
            f.Call("CheckSteeringOutput");Thread.Sleep(370);f.Call("CheckSteeringOutput");
            assert(f.Plugin.FaultBraking,"A fresh resume must restore steering supervision even if the cached route-side pause is still true");
        } finally {Monitor.Exit(sync);}
    }
}
