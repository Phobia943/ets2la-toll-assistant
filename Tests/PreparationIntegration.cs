using System.Reflection;
using System.Runtime.CompilerServices;
using ETS2LA.Game.Telemetry;
using ETS2LA.State;
using MautAssistent;
using PathLib;

public static class PreparationIntegration
{
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Set(TollPlugin plugin,string name,object value) => typeof(TollPlugin).GetField(name,Hidden)!.SetValue(plugin,value);
    static object? Call(TollPlugin plugin,string name,params object[] args) => typeof(TollPlugin).GetMethod(name,Hidden)!.Invoke(plugin,args);

    public static void Run(Action<bool,string> assert)
    {
        // An isolated state object, with no host singleton, subscriptions, SDK
        // providers or output threads. Exercise the actual plugin entry points.
        var state=(ApplicationState)RuntimeHelpers.GetUninitializedObject(typeof(ApplicationState));
        state.DrivingMode=DrivingMode.FullSelfDriving;
        var warnings=new List<string>();
        var plugin=new TollPlugin(()=>state,(title,message)=>warnings.Add(title+": "+message));
        Set(plugin,"subscribed",true);
        Set(plugin,"lastWrite",double.MaxValue); // No diagnostic files during tick tests.
        state.EnableAssists=true;
        plugin.Tick();
        assert(!state.EnableAssists && !plugin.Active,"Unprepared F8 must be stopped by the live tick");
        assert(warnings.Count==1 && plugin.Status.Contains("F8 gesperrt"),"Explain why starting failed");

        // Fault sequence seen in the field: prepared -> route error -> F8 again.
        Set(plugin,"active",true);
        state.EnableAssists=true;
        Call(plugin,"Disarm","Test: Routenplanung fehlgeschlagen");
        assert(!plugin.Active&&!state.EnableAssists,"Fault must hand back control");
        plugin.SetActive(false); // Explicit manual takeover releases the fault brake.
        foreach(var mode in Enum.GetValues<DrivingMode>()) {
            state.DrivingMode=mode;
            state.EnableAssists=true;
            Call(plugin,"OnPath",new PlannedPathData {PlannedPath=[],HasError=true,ErrorMessage="Assists are disabled."});
            assert(!state.EnableAssists,"Changing mode must not bypass preparation");
            state.EnableAssists=true;
            Call(plugin,"OnTelemetry",new GameTelemetryData());
            assert(!state.EnableAssists,"Fresh telemetry must stop an unprepared restart");
            state.EnableAssists=true;
            var acceleration=(float)Call(plugin,"Evaluate")!;
            assert(!state.EnableAssists&&float.IsNaN(acceleration),"ACC bridge must reject restart and return neutral output");
        }
        var result=plugin.SetActive(true); // _IsRunning is false: preparation fails.
        assert(!plugin.Active,"Failed preparation must not unlock F8");
        state.EnableAssists=true;plugin.Tick();
        assert(!state.EnableAssists,"Repeated F8 must remain blocked after failed preparation");

        // F9 is an ordinary pause, not a fault. An already prepared plugin may resume.
        plugin._IsRunning=true;
        Set(plugin,"active",true);
        Set(plugin,"startCheck",new TollPlugin.StartCheck(System.Numerics.Vector3.Zero,0,0,null));
        double now=((System.Diagnostics.Stopwatch)typeof(TollPlugin).GetField("clock",Hidden)!.GetValue(plugin)!).Elapsed.TotalSeconds;
        foreach(var name in new[]{"telemetryAt","gatesAt","gameTimeChangedAt"})Set(plugin,name,now);
        Set(plugin,"sdk",true);Set(plugin,"paused",false);Set(plugin,"speed",0d);
        state.EnableAssists=false;
        assert(!(bool)Call(plugin,"RejectUnpreparedStart")!,"F9 must remain available");
        state.EnableAssists=true;
        assert(!(bool)Call(plugin,"RejectUnpreparedStart")!&&state.EnableAssists,"Prepared F8 must still work");
        plugin.SetActive(false);
        assert(!state.EnableAssists&&!plugin.Active,"Explicit observation must stop the driving assist");
        state.EnableAssists=true;plugin.Tick();
        assert(!state.EnableAssists,"Observation must not silently become unprotected FSD");

        // An isolated preview has no need to turn on the host assist switch.
        Set(plugin,"preview",true);state.EnableAssists=true;
        assert((bool)Call(plugin,"RejectUnpreparedStart")!&&!state.EnableAssists,"F8 must not activate outputs during the probe");
        Set(plugin,"preview",false);plugin.Tick();
        assert(!state.EnableAssists,"Ending preview restores preparation requirement");
    }
}
