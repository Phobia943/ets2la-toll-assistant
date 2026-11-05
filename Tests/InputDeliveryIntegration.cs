using System.Reflection;
using ETS2LA.Game.SDK;
using ETS2LA.Game.Telemetry;
using MautAssistent;

public static class InputDeliveryIntegration
{
    public static void Run(Action<bool,string> assert)
    {
        var f=new RollingStartIntegration.Fixture();f.F8();f.Probe();
        var router=f.Get<TollRouter>("router");
        typeof(TollRouter).GetProperty("StopBoundary")!.SetValue(router,f.Point+new System.Numerics.Vector3(0,0,260));
        var sync=typeof(TollPlugin).GetField("sync",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(f.Plugin)!;
        using var stop=new CancellationTokenSource();
        int deliveries=0;
        Task? producer=null;
        bool entered=false;
        try
        {
            Monitor.Enter(sync);entered=true;
            producer=Task.Run(()=> {
                ulong stamp=100;
                while(!stop.IsCancellationRequested)
                {
                    var telemetry=new GameTelemetryData {sdkActive=true,time=stamp++};
                    telemetry.truckFloat.speed=15;
                    telemetry.truckPlacement.coordinate=new(f.Point.X,f.Point.Y,f.Point.Z+.2);
                    telemetry.truckPlacement.rotation=new(.5,0,0);
                    f.Call("OnTelemetry",telemetry);
                    f.Call("OnGates",new SemaphoreData {semaphores=[]});
                    Interlocked.Increment(ref deliveries);
                    Thread.Sleep(15);
                }
            });
            assert(SpinWait.SpinUntil(()=>Volatile.Read(ref deliveries)>=5,TimeSpan.FromMilliseconds(300)),
                "SDK producers must continue delivering while a toll calculation holds the plugin lock");
            Thread.Sleep(650); // longer than the telemetry watchdog threshold
            f.Call("OnPath",f.Plan());
            f.Call("Evaluate");
            assert(!f.Plugin.FaultBraking&&f.State.EnableAssists,"A slow calculation cannot disguise delivered SDK frames as telemetry loss");
            assert(f.Get<double>("speed")==15&&f.Get<System.Numerics.Vector3>("position").Z>f.Point.Z,
                "The route must validate the latest coherent pose, not just refresh a heartbeat");
            assert(f.Now-f.Get<double>("telemetryAt")<.2,"Arrival time must retain the source delivery age");
        }
        finally
        {
            stop.Cancel();
            if(entered)Monitor.Exit(sync);
            if(producer!=null&&!producer.Wait(TimeSpan.FromSeconds(3)))throw new Exception("SDK test producer did not exit.");
        }
        f.Set("telemetryAt",-100d);
        f.Call("Evaluate");
        assert(f.Plugin.FaultBraking&&!f.State.EnableAssists,"Real telemetry loss must still latch the independent fault brake");
    }

    public static void BrakeWhileBusy(Action<bool,string> assert)
    {
        var f=new RollingStartIntegration.Fixture();f.F8();f.Probe();
        var sync=typeof(TollPlugin).GetField("sync",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(f.Plugin)!;
        Task? brake=null;
        try
        {
            Monitor.Enter(sync);
            brake=Task.Run(()=>f.Call("OnBrakeInput",null,new ETS2LA.Controls.ControlChangeEventArgs(.5f,TollPlugin.BrakeControl)));
            assert(SpinWait.SpinUntil(()=>!f.State.EnableAssists&&f.Outputs.Count>=3,TimeSpan.FromMilliseconds(300)),
                "The physical brake must disable assists and release residual outputs before waiting for route bookkeeping");
        }
        finally
        {
            Monitor.Exit(sync);
            if(brake!=null&&!brake.Wait(TimeSpan.FromSeconds(3)))throw new Exception("Brake test callback did not exit.");
        }
        assert(!f.State.EnableAssists&&!f.Plugin.Active&&!f.Plugin.FaultBraking,"Busy-route takeover must settle in manual control");
    }
}
