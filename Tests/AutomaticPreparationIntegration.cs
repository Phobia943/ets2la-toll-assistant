using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using ETS2LA.Controls;
using ETS2LA.Game.Output;
using ETS2LA.Shared;
using ETS2LA.State;
using MautAssistent;
using PathLib;

public static class AutomaticPreparationIntegration
{
    public static void Run(Action<bool,string> assert)
    {
        const BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        var reference=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../References/AdaptiveCruiseControl.dll"));
        var assembly=AssemblyLoadContext.Default.LoadFromAssemblyPath(reference);
        var type=assembly.GetType("AdaptiveCruiseControl.AdaptiveCruiseControlPlugin",true)!;
        var acc=(IPlugin)RuntimeHelpers.GetUninitializedObject(type);
        var field=type.GetField("constraints")!;
        var native=(IList)Activator.CreateInstance(field.FieldType)!;
        var neutral=AccBridge.CreateConstraint(assembly.GetType("AdaptiveCruiseControl.Constraints.Constraint",true)!,()=>float.NaN,()=>"test");
        native.Add(neutral);native.Add(neutral);native.Add(neutral);field.SetValue(acc,native);
        IPlugin? availableAcc=null;
        var state=(ApplicationState)RuntimeHelpers.GetUninitializedObject(typeof(ApplicationState));
        state.DrivingMode=DrivingMode.FullSelfDriving;
        var sent=new List<ControlEvent>();
        var plugin=new TollPlugin(()=>state,(_,_)=>{},sent.Add,()=>availableAcc);
        void Set(string name,object value)=>typeof(TollPlugin).GetField(name,hidden)!.SetValue(plugin,value);
        object? Call(string name,params object?[] args)=>typeof(TollPlugin).GetMethod(name,hidden)!.Invoke(plugin,args);
        var clock=(Stopwatch)typeof(TollPlugin).GetField("clock",hidden)!.GetValue(plugin)!;
        var automatic=(AutomaticPreparation)typeof(TollPlugin).GetField("automaticPreparation",hidden)!.GetValue(plugin)!;
        void Fresh(double speed=0) {
            double now=clock.Elapsed.TotalSeconds;
            foreach(var name in new[]{"telemetryAt","gatesAt","pathAt","gameTimeChangedAt"})Set(name,now);
            Set("sdk",true);Set("paused",false);Set("speed",speed);
        }
        void ReadyWindow() { automatic.Request();automatic.ShouldAttempt(clock.Elapsed.TotalSeconds-2,true); }
        Set("subscribed",true);Set("lastWrite",double.MaxValue);plugin._IsRunning=true;
        Set("path",new PlannedPathData {HasError=true,ErrorMessage="Assists are disabled.",PlannedPath=[]});
        plugin.Tick();assert(!plugin.Active,"No automatic preparation without current telemetry");
        Fresh();plugin.Tick();assert(!plugin.Active,"Wait for stable standstill before preparing");
        ReadyWindow();Fresh();plugin.Tick();assert(!plugin.Active,"Wait for ACC to load");
        availableAcc=acc;ReadyWindow();Fresh(10);plugin.Tick();
        assert(!plugin.Active&&sent.Count==0,"Never prepare while rolling or send outputs");
        ReadyWindow();Fresh();Set("paused",true);plugin.Tick();assert(!plugin.Active,"Do not prepare in a paused simulation");
        ReadyWindow();Fresh();Set("pathAt",-100d);plugin.Tick();assert(!plugin.Active,"Stale route provider prevents preparation");
        ReadyWindow();Fresh();Set("path",new PlannedPathData {HasError=true,ErrorMessage="No starting road",PlannedPath=[]});
        plugin.Tick();assert(!plugin.Active,"An explicit pathfinding error cannot be hidden by automatic preparation");
        Set("path",new PlannedPathData {HasError=true,ErrorMessage="Assists are disabled.",PlannedPath=[]});
        field.SetValue(acc,Activator.CreateInstance(field.FieldType)!);
        ReadyWindow();Fresh();plugin.Tick();assert(!plugin.Active,"Wait until ACC has installed its native constraints");
        field.SetValue(acc,native);
        ReadyWindow();Fresh();plugin.Tick();
        assert(plugin.Active&&!state.EnableAssists&&!plugin.FaultBraking&&sent.Count==0,"Ready automatically without pressing F8 or emitting vehicle output");
        assert(((IList)field.GetValue(acc)!).Count==4,"Actual ACC receives one limiter");
        var limiter=((IList)field.GetValue(acc)!)[3]!;
        assert(float.IsNaN((float)limiter.GetType().GetMethod("GetMaxAcceleration")!.Invoke(limiter,null)!),"Prepared limiter stays neutral until user starts F8");
        Fresh();plugin.Tick();assert(((IList)field.GetValue(acc)!).Count==4,"Repeated ticks do not duplicate limiter");

        state.EnableAssists=true;Call("Disarm","Synthetic tracking fault");
        Fresh();plugin.Tick();
        assert(plugin.FaultBraking&&!plugin.Active&&!automatic.Requested&&!state.EnableAssists,"Fault never rearms itself or releases brake");
        automatic.Request();Fresh();plugin.Tick();
        assert(plugin.FaultBraking&&!plugin.Active,"Even a pending preparation cannot release a fault brake");
        Call("OnCancel",null,new ControlChangeEventArgs(false,null!));
        assert(!plugin.FaultBraking&&!plugin.Active&&automatic.Requested,"F9 releases fault but does not start driving");
        ReadyWindow();Fresh(5);plugin.Tick();assert(!plugin.Active,"Still wait for standstill after F9");
        ReadyWindow();Fresh();plugin.Tick();
        assert(plugin.Active&&!state.EnableAssists&&((IList)field.GetValue(acc)!).Count==4,"After F9 and standstill prepare without a second UI action");
        plugin.SetActive(false);Fresh();plugin.Tick();
        assert(!plugin.Active&&!automatic.Requested&&((IList)field.GetValue(acc)!).Count==3,"Explicit observation pauses automatic preparation and detaches limiter");
        Call("OnCancel",null,new ControlChangeEventArgs(false,null!));
        ReadyWindow();Fresh();Set("preview",true);plugin.Tick();assert(!plugin.Active,"Route-only preview never attaches driving output");
        Set("preview",false);plugin._IsRunning=false;ReadyWindow();Fresh();plugin.Tick();assert(!plugin.Active,"Disabled plugin cannot prepare");
    }
}
