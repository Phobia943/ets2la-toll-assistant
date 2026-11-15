using ETS2LA.Game.Output;
using MautAssistent;

public static class FaultLeaseIntegration
{
    public static void Run(Action<bool,string> assert)
    {
        var values=new List<float>();var brake=new FaultBrake(e=>values.Add(e.Variables.abackward!.Value));
        using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();Task old;
        using(brake.BeginOperation())old=Task.Run(()=>{entered.Set();release.Wait(TimeSpan.FromSeconds(3));brake.Trip();});
        assert(entered.Wait(TimeSpan.FromSeconds(2)),"Old route operation must be suspended before its fault commit");
        brake.Release();brake.Trip();brake.Release();int count=values.Count;release.Set();
        assert(old.Wait(TimeSpan.FromSeconds(2))&&!brake.Latched&&values.Count==count&&values[^1]==0,
            "An old operation cannot relatch after takeover and a newer fault generation");
        var sent=new List<float>();using var refreshEntered=new ManualResetEventSlim();using var allowRefresh=new ManualResetEventSlim();int negative=0;
        var serialized=new FaultBrake(e=>{
            sent.Add(e.Variables.abackward!.Value);
            if(e.Variables.abackward<0&&Interlocked.Increment(ref negative)==2) {
                refreshEntered.Set();if(!allowRefresh.Wait(TimeSpan.FromSeconds(3)))throw new TimeoutException();
            }
        });
        serialized.Trip();var refresh=Task.Run(serialized.Refresh);
        assert(refreshEntered.Wait(TimeSpan.FromSeconds(2)),"Hold an already publishing brake refresh");
        var cancel=Task.Run(serialized.Release);
        try {assert(!cancel.Wait(TimeSpan.FromMilliseconds(50)),"Takeover acknowledgement must follow completion of the already publishing refresh");}
        finally {allowRefresh.Set();}
        assert(Task.WaitAll([refresh,cancel],TimeSpan.FromSeconds(2))&&!serialized.Latched&&sent[^1]==0,
            "The last acknowledged output is neutral, never a late old refresh");
        int finalCount=sent.Count;serialized.Refresh();assert(sent.Count==finalCount,"An unlatched refresh publishes nothing");
        var f=new RollingStartIntegration.Fixture(true);f.Set("steeringMonitoringLive",true);f.F8();f.Probe();
        f.Call("OnControlOutput",new ControlEvent {ChannelDefinition=new(){Id="LaneAssist.SteeringCorrection"},Variables=new(){steering=0}});
        f.Call("CheckSteeringOutput");Thread.Sleep(190);f.Fresh();
        var pluginBrake=f.Get<FaultBrake>("faultBrake");
        var gate=typeof(FaultBrake).GetField("sync",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(pluginBrake)!;
        Task? watchdog=null;
        try {
            Monitor.Enter(gate);watchdog=Task.Run(()=>f.Call("CheckSteeringOutput"));
            assert(SpinWait.SpinUntil(()=>f.Get<SteeringFeedState>("steeringFeedState")==SteeringFeedState.Lost,TimeSpan.FromSeconds(2)),"Suspend the actual steering fault immediately before its state/output commit");
            pluginBrake.Release();f.State.EnableAssists=true;
        } finally {Monitor.Exit(gate);}
        assert(watchdog.Wait(TimeSpan.FromSeconds(2))&&f.State.EnableAssists&&!pluginBrake.Latched,
            "An old actual steering watchdog cannot disable a new session after its fault commit was invalidated");
    }
}
