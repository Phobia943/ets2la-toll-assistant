using System.Reflection;
using ETS2LA.Game.Telemetry;
using MautAssistent;

public static class ImpactSafetyIntegration
{
    public static void Run(Action<bool,string> assert)
    {
        var f=new RollingStartIntegration.Fixture();f.F8();f.Probe();
        GameTelemetryData Frame(ulong time,float speed,float damage) {
            var t=new GameTelemetryData {sdkActive=true,time=time,simulatedTime=time,renderTime=time};
            t.truckFloat.speed=speed;t.truckFloat.wearCabin=damage;t.truckFloat.wearChassis=.1f;
            t.truckFloat.gameThrottle=.25f;
            t.truckPlacement.coordinate=new(f.Point.X,f.Point.Y,f.Point.Z);
            t.truckPlacement.rotation=new(.5,0,0);return t;
        }
        var sync=typeof(TollPlugin).GetField("sync",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(f.Plugin)!;
        Task? producer=null;
        try {
            Monitor.Enter(sync);
            producer=Task.Run(()=>{
                f.Call("OnTelemetry",Frame(1000000,22,.1f));Thread.Sleep(45);
                f.Call("OnTelemetry",Frame(1060000,.08f,.11f));
            });
            assert(producer.Wait(TimeSpan.FromSeconds(1)),"Impact inspection cannot wait for a busy route lock");
            assert(f.Plugin.FaultBraking&&!f.State.EnableAssists&&f.Outputs[^1].Variables.abackward==-1,
                "Impact must disable assistance and publish the independent brake immediately");
        } finally {Monitor.Exit(sync);producer?.Wait(TimeSpan.FromSeconds(2));}
        f.Call("Evaluate");
        assert(f.Plugin.FaultBraking&&!f.Plugin.Active,"Deferred diagnostics preserve the impact latch");
        f.Call("OnBrakeInput",null,new ETS2LA.Controls.ControlChangeEventArgs(.5f,TollPlugin.BrakeControl));
        f.Call("Evaluate");
        assert(!f.Plugin.FaultBraking&&!f.State.EnableAssists,"Physical takeover cancels impact braking and cannot restart it");
        var clean=new RollingStartIntegration.Fixture();clean.F8();clean.Probe();
        clean.Call("OnTelemetry",Frame(2000000,22,.1f));Thread.Sleep(45);
        clean.Call("OnTelemetry",Frame(2060000,0,.1f));
        assert(!clean.Plugin.FaultBraking,"An abrupt SDK stop without new body damage cannot be called a collision");

        var cancelled=new RollingStartIntegration.Fixture();cancelled.F8();cancelled.Probe();
        var blocked=typeof(TollPlugin).GetField("sync",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(cancelled.Plugin)!;
        Task? acknowledgement=null;
        try {
            Monitor.Enter(blocked);
            var delivery=Task.Run(()=>{
                cancelled.Call("OnTelemetry",Frame(3000000,22,.1f));Thread.Sleep(45);
                cancelled.Call("OnTelemetry",Frame(3060000,0,.11f));
            });
            assert(delivery.Wait(TimeSpan.FromSeconds(1))&&cancelled.Plugin.FaultBraking,"Set up a pending impact report during blocked route work");
            acknowledgement=Task.Run(()=>cancelled.F9());
            assert(SpinWait.SpinUntil(()=>!cancelled.Plugin.FaultBraking,TimeSpan.FromMilliseconds(300)),
                "F9 must release braking before waiting for route bookkeeping");
        } finally {Monitor.Exit(blocked);acknowledgement?.Wait(TimeSpan.FromSeconds(2));}
        cancelled.Call("Evaluate");
        assert(!cancelled.Plugin.FaultBraking&&!cancelled.State.EnableAssists,"A pending collision report cannot rearm braking after F9");
    }
}
