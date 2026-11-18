using System.Reflection;
using System.Runtime.CompilerServices;
using ETS2LA.Controls;
using MautAssistent;

public static class CruiseOptionsIntegration
{
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    public static void Cruise(Action<bool,string> assert)
    {
        var reference=new RollingStartIntegration.Fixture();reference.F8();reference.Probe();reference.Fresh();reference.Plugin.Tick();
        var currentPath=reference.Get<PathLib.PlannedPathData>("path");
        assert((bool)reference.Call("NativeCoastPathKnown",currentPath)!&&!(bool)reference.Call("NativeCoastPathKnown",reference.Plan())!,
            "Only the actual current route instance may authorize a native no-limit result, not a geometrically identical old route");
        reference.Set("pathAt",-100d);
        reference.Call("SafetyWatchdog",reference.Now);
        assert(!(bool)reference.Call("NativeCoastPathKnown",currentPath)!,"An old same-reference route also loses coasting eligibility");
        reference.F9();reference.Call("StopDiagnostics");
        var f=new RollingStartIntegration.Fixture();f.State.DesiredSpeed=80/3.6f;f.F8();f.Probe();
        f.Call("ApplyCruiseStartPreference");
        assert(f.State.DesiredSpeed<23,"Short unverified preview cannot immediately set 100 km/h");
        var trace=new RouteTrace(Enumerable.Range(0,1601).Select(i=>f.Point+new System.Numerics.Vector3(0,0,i)),null,Enumerable.Repeat("road:901:902:lane1",1600));
        f.Set("safetyTrace",trace);f.Call("RequestCurvePreview",trace);
        var preview=f.Get<CurvePreview>("curvePreview");
        assert(SpinWait.SpinUntil(()=>preview.Read(trace,f.Point,420)!=null,TimeSpan.FromSeconds(3)),"Wait for the genuinely sufficient preview");
        f.Call("ApplyCruiseStartPreference");
        assert(Math.Abs(f.State.DesiredSpeed*3.6-100)<.01,"A verified open corridor can adopt the requested target");
        preview.Clear();f.Fresh(25);f.Plugin.Tick();
        var own=f.Get<TollPlugin.LongitudinalSnapshot>("longitudinalSnapshot");
        assert(own.Input.TargetSpeed<=80/3.6+.01&&own.Output.TargetAcceleration<=0&&own.Output.TargetAcceleration>=-1.20001,
            "Lost preview authorization falls back to the original cruise target with bounded own-regulator slowing");
        f.F9();assert(!f.State.EnableAssists&&!f.Get<bool>("preferredCruiseOwned"),"Manual takeover clears preset ownership");

        var off=new RollingStartIntegration.Fixture();off.Set("options",new TollOptions{BriskOpenRoads=false,ComfortableDeceleration=.5});
        off.State.DesiredSpeed=70/3.6f;off.F8();off.Probe();off.Call("ApplyCruiseStartPreference");
        assert(Math.Abs(off.State.DesiredSpeed*3.6-70)<.01,"Brisk disabled preserves the host target even with cautious valid settings");
        var set=new RollingStartIntegration.Fixture();set.State.DesiredSpeed=72/3.6f;set.State.EnableAssists=true;
        set.Call("OnSetStart",null,new ControlChangeEventArgs(true,null!));set.Probe();set.Call("ApplyCruiseStartPreference");
        assert(Math.Abs(set.State.DesiredSpeed*3.6-72)<.01,"F10 SET does not become the preferred 100 km/h preset");
        var pending=new RollingStartIntegration.Fixture();pending.F8();pending.Probe();
        pending.Call("RecordCruiseStart",new ControlChangeEventArgs(true,null!),false);
        pending.Set("safetyTrace",trace);pending.Call("RequestCurvePreview",trace);
        var pendingPreview=pending.Get<CurvePreview>("curvePreview");
        assert(SpinWait.SpinUntil(()=>pendingPreview.IsIdle,TimeSpan.FromSeconds(3)),"Wait for pending manual speed test");
        pending.State.DesiredSpeed=75/3.6f;pending.Call("ApplyCruiseStartPreference");
        assert(Math.Abs(pending.State.DesiredSpeed*3.6-75)<.01,"A later explicit F10 cancels a pending automatic preset before driving begins");
        RollingStartIntegration.Fixture? canceled=null;bool cancelAtRead=false;
        canceled=new(readCruiseLimits:()=>{if(cancelAtRead){cancelAtRead=false;canceled!.F9();}return new(0,false);});
        canceled.State.DesiredSpeed=80/3.6f;canceled.F8();canceled.Probe();
        canceled.Set("safetyTrace",trace);canceled.Call("RequestCurvePreview",trace);
        assert(SpinWait.SpinUntil(()=>canceled.Get<CurvePreview>("curvePreview").IsIdle,TimeSpan.FromSeconds(3)),"Wait for cancellation test preview");
        cancelAtRead=true;canceled.Call("ApplyCruiseStartPreference");
        assert(!canceled.State.EnableAssists&&!canceled.Get<bool>("preferredCruiseOwned")&&Math.Abs(canceled.State.DesiredSpeed*3.6-80)<.01,
            "F9 during host-cap lookup cannot be followed by an old desired-speed or ownership commit");
    }
    public static void UiRoundTrip(Action<bool,string> assert)
    {
        var f=new RollingStartIntegration.Fixture();f.Fresh(0);
        var settings=new TollOptions{BriskOpenRoads=false,RoadCruiseSpeedKph=90,CoastOnOpenRoads=false,ExitDistanceMetres=57,ComfortableDeceleration=.7};
        f.Set("options",settings);
        string file=Path.Combine(Path.GetTempPath(),"maut-ui-"+Guid.NewGuid()+".json");f.Set("preferences",new PreferenceStore(file));
        var current=typeof(TollPlugin).GetProperty("Current")!;object? previous=current.GetValue(null);
        try {
            current.SetValue(null,f.Plugin);
            var componentType=typeof(TollPlugin).Assembly.GetTypes().Single(t=>t.Name=="Adjustments");
            var component=RuntimeHelpers.GetUninitializedObject(componentType);
            void Set(string name,object value)=>componentType.GetField(name,Hidden)!.SetValue(component,value);
            Set("approach",18d);Set("passage",10d);Set("offset",8d);Set("delay",2d);
            Set("curvePreviewEnabled",true);Set("brisk",false);Set("roadCruise",90d);
            Set("coast",false);
            componentType.GetMethod("Apply",Hidden)!.Invoke(component,null);
            var saved=new PreferenceStore(file);saved.Load();
            assert(saved.Options==settings with{ApproachSpeedKph=18},
                "Actual Razor Apply preserves all hidden options while changing one displayed toll field");
        } finally {current.SetValue(null,previous);if(File.Exists(file))File.Delete(file);}
    }
}
