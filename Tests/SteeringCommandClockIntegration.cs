using System.Diagnostics;
using System.Reflection;
using ETS2LA.Game.Output;
using MautAssistent;

public static class SteeringCommandClockIntegration
{
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    static object Command(double localAt,long issued,float value=0)
    {
        var type=typeof(TollPlugin).GetNestedType("SteeringCommand",BindingFlags.NonPublic)!;
        var result=Activator.CreateInstance(type,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance,null,[localAt,value],null)!;
        type.GetProperty("IssuedTicks")!.SetValue(result,issued);
        type.GetProperty("ReceivedAt")!.SetValue(result,localAt);
        type.GetProperty("ReceivedTicks")!.SetValue(result,Stopwatch.GetTimestamp());
        return result;
    }
    static RollingStartIntegration.Fixture Start()
    {
        var f=new RollingStartIntegration.Fixture(true,writeDiagnostic:(_,_)=>{});
        f.Set("steeringMonitoringLive",true);f.F8();f.Probe();
        f.Call("OnControlOutput",new ControlEvent {ChannelDefinition=new(){Id="LaneAssist.SteeringCorrection"},Variables=new(){steering=0}});
        f.Call("CheckSteeringOutput");return f;
    }
    public static void RawAuthority(Action<bool,string> assert)
    {
        var f=Start();long tick=Stopwatch.GetTimestamp();
        f.Set("lastSteeringCommand",Command(f.Now-10,tick));f.Call("CheckSteeringOutput");
        assert(!f.Plugin.FaultBraking&&f.Get<SteeringFeedState>("steeringFeedState")==SteeringFeedState.Healthy,
            "Native origin age, not an unrelated old local-clock value, controls steering freshness");
        var newest=Command(f.Now,Stopwatch.GetTimestamp(),.2f);f.Set("lastSteeringCommand",newest);
        f.Call("StoreSteering",Command(f.Now+100,tick-1,.8f));
        assert(ReferenceEquals(newest,f.Get<object>("lastSteeringCommand")),"Later delivery cannot replace a newer producer stamp with older output");
        f.Set("lastSteeringCommand",Command(f.Now,Stopwatch.GetTimestamp()-(long)(Stopwatch.Frequency*.17)));
        f.Call("CheckSteeringOutput");
        assert(f.Plugin.FaultBraking&&!f.State.EnableAssists,"Fresh delivery never rejuvenates a truly expired native steering command");
        var future=Start();future.Set("lastSteeringCommand",Command(future.Now,Stopwatch.GetTimestamp()+Stopwatch.Frequency));future.Call("CheckSteeringOutput");
        assert(future.Plugin.FaultBraking,"Future native command timestamps cannot establish steering authority");
    }
    public static void RecheckBeforeTrip(Action<bool,string> assert)
    {
        var f=Start();f.Set("lastSteeringCommand",Command(f.Now,Stopwatch.GetTimestamp()-(long)(Stopwatch.Frequency*.17)));
        var brake=f.Get<FaultBrake>("faultBrake");var gate=typeof(FaultBrake).GetField("sync",Hidden)!.GetValue(brake)!;
        Task? check=null;bool reached=false;
        try {
            Monitor.Enter(gate);
            check=Task.Run(()=>f.Call("CheckSteeringOutput"));
            reached=SpinWait.SpinUntil(()=>f.Get<SteeringFeedState>("steeringFeedState")==SteeringFeedState.Lost,TimeSpan.FromSeconds(2));
            if(reached)f.Call("StoreSteering",Command(f.Now,Stopwatch.GetTimestamp()));
        } finally {Monitor.Exit(gate);}
        assert(check!=null&&check.Wait(TimeSpan.FromSeconds(2)),"Watchdog settles after output-authority contention");
        assert(reached&&!f.Plugin.FaultBraking&&f.State.EnableAssists,
            "A verified fresh command arriving before trip commit defeats the earlier stale snapshot without extending any timeout");
        f.Set("lastSteeringCommand",Command(f.Now,Stopwatch.GetTimestamp()-(long)(Stopwatch.Frequency*.17)));f.Call("CheckSteeringOutput");
        assert(f.Plugin.FaultBraking,"A genuinely expired command still trips after the successful contention recovery");
    }
}
