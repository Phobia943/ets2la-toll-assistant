using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Numerics;
using ETS2LA.Controls;
using ETS2LA.Game.Output;
using ETS2LA.Shared;
using ETS2LA.State;
using MautAssistent;
using PathLib;
using ETS2LA.Game.Data;
using ETS2LA.Game.SDK;
using TruckLib.ScsMap;

public static class AutomaticPreparationIntegration
{
    sealed class Planner : Plugin
    {
        NavigationData navigationData = new() { entries=[] };
        public override PluginInformation Info => new() { Id="test.pathfinding",Name="Test planner",Version="1.0.0",Description="Isolated test provider" };
        public void Route(params ulong[] nodes) => navigationData=new() {
            entries=nodes.Select(id=>new NavigationEntry {nodeUid=id}).ToArray()
        };
    }

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
        var planner=new Planner();
        planner.Route(11,12);
        var rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI);
        var start=new Node {Uid=801,Position=Vector3.Zero,Rotation=rotation};
        var end=new Node {Uid=802,Position=new(0,0,200),Rotation=rotation};
        var road=(ParsedRoad)RuntimeHelpers.GetUninitializedObject(typeof(ParsedRoad));
        road.Road=new Road {Node=start,ForwardNode=end,Length=200};
        road.StartNode=start;road.EndNode=end;
        road.RightLaneOffsetsStart=road.RightLaneOffsetsEnd=[2];
        road.LeftLaneOffsetsStart=road.LeftLaneOffsetsEnd=[-2];
        var point=road.InterpolateLane(.1f,Side.Right,0).Position;
        var validRoute=new PlannedPathData {HasError=false,PlannedPath=[new PlannedItem {
            item=new ParsedRoadList([road]),startNode=start,endNode=end,direction=Direction.Forward,laneIndex=1
        }]};
        PlannedPathData? nextProbe=validRoute;
        int probes=0;
        var plugin=new TollPlugin(()=>state,(_,_)=>{},sent.Add,()=>availableAcc,readPlanner:()=>planner,
            probeRoute:(_,_,_,_)=> {probes++;return nextProbe;});
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
        Set("position",point);Set("headingTurns",.5d);
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
        assert(!state.EnableAssists&&plugin.Status.Contains("Routenprobe"),"Do not report ready before a route check");
        Fresh();Call("OnPath",new PlannedPathData {HasError=true,ErrorMessage="Assists are disabled.",PlannedPath=[]});
        field.SetValue(acc,native);
        ReadyWindow();Fresh();plugin.Tick();
        assert(plugin.Active&&!state.EnableAssists&&!plugin.FaultBraking&&sent.Count==0,"Ready automatically without pressing F8 or emitting vehicle output");
        assert(((IList)field.GetValue(acc)!).Count==4,"Actual ACC receives one limiter");
        var limiter=((IList)field.GetValue(acc)!)[3]!;
        assert(float.IsNaN((float)limiter.GetType().GetMethod("GetMaxAcceleration")!.Invoke(limiter,null)!),"Prepared limiter stays neutral until user starts F8");
        Fresh();plugin.Tick();assert(((IList)field.GetValue(acc)!).Count==4,"Repeated ticks do not duplicate limiter");
        planner.Route(11,12);
        Fresh();plugin.Tick();assert(plugin.Active,"Repeated telemetry for the same navigation nodes retains readiness");
        planner.Route(11,13);
        Fresh();plugin.Tick();
        assert(!plugin.Active&&!state.EnableAssists&&!plugin.FaultBraking,"Changed navigation nodes invalidate the parked start");
        ReadyWindow();Fresh();plugin.Tick();
        Fresh();Call("OnPath",new PlannedPathData {HasError=true,ErrorMessage="Assists are disabled.",PlannedPath=[]});
        ReadyWindow();Fresh();plugin.Tick();
        assert(plugin.Active,"A new route check can restore readiness for the changed destination");
        Set("position",point+new Vector3(0,0,4));
        state.EnableAssists=true;
        Call("RejectUnpreparedStart");
        assert(!state.EnableAssists&&!plugin.Active&&!plugin.FaultBraking,"Manual movement cannot reuse an old start permit");
        ReadyWindow();Fresh();plugin.Tick();
        Fresh();Call("OnPath",new PlannedPathData {HasError=true,ErrorMessage="Assists are disabled.",PlannedPath=[]});
        ReadyWindow();Fresh();plugin.Tick();
        assert(plugin.Active,"Standing at a newly checked location can prepare again");
        Fresh();
        assert(plugin.PreviewRoute().StartsWith("Routenprobe läuft"),"Request a probe with ACC still attached");
        assert(plugin.Active&&!state.EnableAssists&&sent.Count==0&&((IList)field.GetValue(acc)!).Count==4,
            "Probe cannot activate assists or detach ACC");
        nextProbe=null;
        Call("OnPath",new PlannedPathData {HasError=true,ErrorMessage="Assists are disabled.",PlannedPath=[]});
        assert(!plugin.Active&&!state.EnableAssists&&!plugin.FaultBraking&&sent.Count==0&&((IList)field.GetValue(acc)!).Count==4,
            "A failed route check blocks starting while keeping driving modules intact");
        int checkedCount=probes;
        ReadyWindow();Fresh();plugin.Tick();
        assert(!plugin.Active&&probes==checkedCount,"A failed parked location must not be scanned again every tick");
        nextProbe=validRoute;
        assert(plugin.PreviewRoute().StartsWith("Routenprobe läuft"),"An explicit retry remains possible");
        Fresh();Call("OnPath",new PlannedPathData {HasError=true,ErrorMessage="Assists are disabled.",PlannedPath=[]});
        ReadyWindow();Fresh();plugin.Tick();
        assert(plugin.Active&&!state.EnableAssists,"A successful retry prepares F8 without starting the truck");

        state.EnableAssists=true;Call("Disarm","Synthetic tracking fault");
        Fresh();plugin.Tick();
        assert(plugin.FaultBraking&&!plugin.Active&&!automatic.Requested&&!state.EnableAssists,"Fault never rearms itself or releases brake");
        automatic.Request();Fresh();plugin.Tick();
        assert(plugin.FaultBraking&&!plugin.Active,"Even a pending preparation cannot release a fault brake");
        Call("OnCancel",null,new ControlChangeEventArgs(false,null!));
        assert(!plugin.FaultBraking&&!plugin.Active&&automatic.Requested,"F9 releases fault but does not start driving");
        ReadyWindow();Fresh(5);plugin.Tick();assert(!plugin.Active,"Still wait for standstill after F9");
        Fresh();ReadyWindow();plugin.Tick();
        Fresh();Call("OnPath",new PlannedPathData {HasError=true,ErrorMessage="Assists are disabled.",PlannedPath=[]});
        ReadyWindow();Fresh();plugin.Tick();
        assert(plugin.Active&&!state.EnableAssists&&((IList)field.GetValue(acc)!).Count==4,"After F9 and standstill prepare without a second UI action");
        plugin.SetActive(false);Fresh();plugin.Tick();
        assert(!plugin.Active&&!automatic.Requested&&((IList)field.GetValue(acc)!).Count==3,"Explicit observation pauses automatic preparation and detaches limiter");
        Call("OnCancel",null,new ControlChangeEventArgs(false,null!));
        ReadyWindow();Fresh();Set("preview",true);plugin.Tick();assert(!plugin.Active,"Route-only preview never attaches driving output");
        Set("preview",false);plugin._IsRunning=false;ReadyWindow();Fresh();plugin.Tick();assert(!plugin.Active,"Disabled plugin cannot prepare");
    }
}
