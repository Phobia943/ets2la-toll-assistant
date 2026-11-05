using System.Collections;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using ETS2LA.Controls;
using ETS2LA.Game.Data;
using ETS2LA.Game.Output;
using ETS2LA.Game.SDK;
using ETS2LA.Shared;
using ETS2LA.State;
using MautAssistent;
using PathLib;
using TruckLib.ScsMap;

public static class RollingStartIntegration
{
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    sealed class Planner:Plugin
    {
        NavigationData navigationData=new() {entries=[new NavigationEntry {nodeUid=1}]};
        public override PluginInformation Info=>new() {Id="test.pathfinding",Name="Test",Version="1",Description="Isolated planner"};
        public void ChangeNavigation()=>navigationData=new() {entries=[new NavigationEntry {nodeUid=2}]};
    }
    sealed class Fixture
    {
        public readonly ApplicationState State=(ApplicationState)RuntimeHelpers.GetUninitializedObject(typeof(ApplicationState));
        public readonly TollPlugin Plugin;
        public readonly Planner Planner=new();
        public readonly List<ControlEvent> Outputs=[];
        public readonly List<string> Notices=[];
        public readonly Vector3 Point;
        public Action? DuringProbe;
        public Func<PlannedPathData?> Plan;
        public bool InstallSucceeds=true;
        public int Installs;
        public Fixture()
        {
            var assembly=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../References/AdaptiveCruiseControl.dll")));
            var type=assembly.GetType("AdaptiveCruiseControl.AdaptiveCruiseControlPlugin",true)!;
            var acc=(IPlugin)RuntimeHelpers.GetUninitializedObject(type);
            var field=type.GetField("constraints")!;
            var constraints=(IList)Activator.CreateInstance(field.FieldType)!;
            var neutral=AccBridge.CreateConstraint(assembly.GetType("AdaptiveCruiseControl.Constraints.Constraint",true)!,()=>float.NaN,()=>"test");
            for(int i=0;i<3;i++)constraints.Add(neutral);
            field.SetValue(acc,constraints);
            var rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI);
            var start=new Node {Uid=901,Position=Vector3.Zero,Rotation=rotation};
            var end=new Node {Uid=902,Position=new(0,0,400),Rotation=rotation};
            var road=(ParsedRoad)RuntimeHelpers.GetUninitializedObject(typeof(ParsedRoad));
            road.Road=new Road {Node=start,ForwardNode=end,Length=400};road.StartNode=start;road.EndNode=end;
            road.RightLaneOffsetsStart=road.RightLaneOffsetsEnd=[2];road.LeftLaneOffsetsStart=road.LeftLaneOffsetsEnd=[-2];
            Point=road.InterpolateLane(.1f,Side.Right,0).Position;
            Plan=()=>new() {HasError=false,PlannedPath=[new PlannedItem {
                item=new ParsedRoadList([road]),startNode=start,endNode=end,direction=Direction.Forward,laneIndex=1
            }]};
            State.DrivingMode=DrivingMode.FullSelfDriving;
            Plugin=new(()=>State,(title,message)=>Notices.Add(title+": "+message),Outputs.Add,()=>acc,
                readPlanner:()=>Planner,probeRoute:(_,_,_,_)=> {DuringProbe?.Invoke();return Plan();},
                installStart:(_,planned,published,_,_)=> {
                    Installs++;
                    if(!InstallSucceeds)return false;
                    published.PlannedPath=planned.PlannedPath;published.ErrorMessage=null;published.HasError=false;return true;
                });
            Plugin._IsRunning=true;
            Set("subscribed",true);Set("lastWrite",double.MaxValue);Set("position",Point);Set("headingTurns",.5d);
            Fresh();Set("path",Disabled());
        }
        public void Set(string field,object value)=>typeof(TollPlugin).GetField(field,Hidden)!.SetValue(Plugin,value);
        public T Get<T>(string field)=>(T)typeof(TollPlugin).GetField(field,Hidden)!.GetValue(Plugin)!;
        public object? Call(string name,params object?[] args)=>typeof(TollPlugin).GetMethod(name,Hidden)!.Invoke(Plugin,args);
        public double Now=>Get<Stopwatch>("clock").Elapsed.TotalSeconds;
        public void Fresh(double speed=15)
        {
            foreach(var field in new[]{"telemetryAt","gatesAt","pathAt","gameTimeChangedAt"})Set(field,Now);
            Set("sdk",true);Set("paused",false);Set("speed",speed);
        }
        public static PlannedPathData Disabled()=>new() {HasError=true,ErrorMessage="Assists are disabled.",PlannedPath=[]};
        public void F8() { State.EnableAssists=true;Call("OnStart",null,new ControlChangeEventArgs(true,null!)); }
        public void Probe()=>Call("OnPath",Disabled());
        public void F9()=>Call("OnCancel",null,new ControlChangeEventArgs(false,null!));
    }

    public static void StartAndTakeover(Action<bool,string> assert)
    {
        var f=new Fixture();
        f.F8();
        assert(f.Get<bool>("rollingStartPending")&&!f.State.EnableAssists&&!f.Plugin.Active&&f.Installs==0,"F8 queues a rolling start without driving before route validation");
        assert(float.IsNaN((float)f.Call("Evaluate")!)&&f.Outputs.Count==0,"The pending request emits no throttle, brake or steering");
        f.DuringProbe=()=> {f.Set("position",f.Point+new Vector3(0,0,30));f.Fresh();};
        f.Probe();
        assert(f.Installs==1&&f.State.EnableAssists&&f.Plugin.Active&&!f.Plugin.FaultBraking,"A route covering the latest moving position can start FSD");
        f.Fresh();f.Plugin.Tick();f.Fresh();f.Call("Evaluate");
        assert(f.State.EnableAssists&&!f.Plugin.FaultBraking&&f.Outputs.Count==0,"The first rolling control cycle does not reject the old disabled frame or emergency brake");
        f.F9();
        assert(!f.State.EnableAssists&&!f.Plugin.Active&&!f.Plugin.FaultBraking,"F9 hands a rolling session back to manual control");
        f.Fresh();f.F8();f.DuringProbe=null;f.Probe();
        assert(f.State.EnableAssists&&f.Installs==2,"F8 can resume on the fly after F9 without a parked preparation");
        f.Plugin.SetActive(false);
        f.Fresh();f.F8();
        assert(!f.State.EnableAssists&&!f.Get<bool>("rollingStartPending"),"Explicit observation mode still prevents unprotected FSD");

        var passed=new Fixture();
        passed.Plan=()=> {
            var items=new List<PlannedItem>();
            var rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI);
            foreach(var (from,to) in new[]{(0,20),(20,40),(40,400)}) {
                var start=new Node {Uid=(ulong)(1000+from),Position=new(0,0,from),Rotation=rotation};
                var end=new Node {Uid=(ulong)(1000+to),Position=new(0,0,to),Rotation=rotation};
                var road=(ParsedRoad)RuntimeHelpers.GetUninitializedObject(typeof(ParsedRoad));
                road.Road=new Road {Node=start,ForwardNode=end,Length=to-from};road.StartNode=start;road.EndNode=end;
                road.RightLaneOffsetsStart=road.RightLaneOffsetsEnd=[2];road.LeftLaneOffsetsStart=road.LeftLaneOffsetsEnd=[-2];
                items.Add(new() {item=new ParsedRoadList([road]),startNode=start,endNode=end,direction=Direction.Forward,laneIndex=1});
            }
            return new() {HasError=false,PlannedPath=items};
        };
        passed.F8();passed.DuringProbe=()=> {passed.Set("position",passed.Point+new Vector3(0,0,30));passed.Fresh();};passed.Probe();
        assert(passed.State.EnableAssists&&passed.Get<PlannedPathData>("path").PlannedPath.Count==1,
            "Several items passed during the moving probe are removed before native handoff");
    }

    public static void Refusals(Action<bool,string> assert)
    {
        foreach(var failure in new[]{"offroute","opposite","short","navigation","mode","stale","timeout","install","missing","reverse","pause"})
        {
            var f=new Fixture();f.F8();
            f.DuringProbe=()=> {
                switch(failure)
                {
                    case "offroute": f.Set("position",f.Point+new Vector3(4,0,0));break;
                    case "opposite": f.Set("headingTurns",0d);break;
                    case "short": f.Set("position",f.Point+new Vector3(0,0,340));break;
                    case "navigation": f.Planner.ChangeNavigation();break;
                    case "mode": f.State.DrivingMode=DrivingMode.LaneAssistOnly;break;
                    case "stale": f.Set("telemetryAt",-100d);break;
                    case "timeout": f.Set("rollingStartAt",-100d);break;
                    case "install": f.InstallSucceeds=false;break;
                    case "reverse": f.Set("speed",-2d);break;
                    case "pause": f.Set("paused",true);break;
                }
            };
            if(failure=="missing")f.Plan=()=>null;
            f.Probe();
            assert(!f.State.EnableAssists&&!f.Plugin.Active&&!f.Plugin.FaultBraking&&f.Outputs.Count==0&&!f.Get<bool>("rollingStartPending"),
                "A refused rolling start leaves manual controls and pedals neutral: "+failure);
            assert(f.Installs==(failure=="install"?1:0),"Only a fully validated route may reach native installation: "+failure);
        }
        var timed=new Fixture();timed.F8();timed.Set("rollingStartAt",-100d);timed.Fresh();timed.Plugin.Tick();
        assert(!timed.Get<bool>("rollingStartPending")&&!timed.State.EnableAssists&&timed.Outputs.Count==0,"A missing planner response times out without vehicle output");
        var fault=new Fixture();
        fault.Get<FaultBrake>("faultBrake").Trip();fault.Outputs.Clear();fault.F8();
        assert(!fault.Get<bool>("rollingStartPending")&&fault.Plugin.FaultBraking&&!fault.State.EnableAssists,"F8 cannot release a tracking fault for a rolling restart");
    }

    public static void CancelDuringProbe(Action<bool,string> assert)
    {
        var f=new Fixture();f.F8();
        f.DuringProbe=()=> {
            f.State.DrivingMode=DrivingMode.AdaptiveCruiseControlOnly; // host's mode cycle while assists are off
            f.F9();
        };
        f.Probe();
        assert(f.State.DrivingMode==DrivingMode.FullSelfDriving&&!f.State.EnableAssists&&f.Installs==0&&f.Outputs.Count==0,
            "F9 cancels an in-flight result and retains the driving mode");
        f.DuringProbe=()=>f.Plugin.SetActive(false);f.Fresh();f.F8();f.Probe();
        assert(!f.State.EnableAssists&&f.Installs==0,"A late result cannot reactivate an explicitly stopped plugin");
        var plannerAssembly=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(Environment.GetEnvironmentVariable("EtsRoot")!,"Plugins/tumppi066.pathfinding/Pathfinding.dll"));
        var planner=(IPlugin)RuntimeHelpers.GetUninitializedObject(plannerAssembly.GetType("Pathfinding.PathfindingPlugin",true)!);
        planner._IsRunning=true;
        assert(!ConnectorStart.TryInstallStart(planner,f.Plan()!,Fixture.Disabled(),f.Point,.5),
            "The native installer refuses calls outside the verified planner Tick even on a matching build");
    }

    public static void BrakeOverride(Action<bool,string> assert)
    {
        var f=new Fixture();f.F8();f.Probe();
        assert(f.State.EnableAssists,"Test starts with an active rolling session");
        void Brake(float value)=>f.Call("OnBrakeInput",null,new ControlChangeEventArgs(value,TollPlugin.BrakeControl));
        Brake(float.NaN);Brake(-1);Brake(.04f);
        assert(f.State.EnableAssists&&f.Outputs.Count==0,"Invalid values and small trigger noise do not cancel driving");
        Brake(.2f);
        assert(!f.State.EnableAssists&&!f.Plugin.Active&&!f.Plugin.FaultBraking&&f.Get<bool>("brakeHeld"),"Physical brake cancels steering and ACC");
        assert(f.Outputs.Count==3&&f.Outputs.All(o=>o.Variables.aforward==0&&o.Variables.abackward==0&&o.Variables.steering==0),
            "Takeover clears residual commands without generating a brake pulse");
        f.F8();assert(!f.State.EnableAssists&&!f.Get<bool>("rollingStartPending"),"Held brake blocks F8");
        Brake(.09f);f.F8();assert(!f.Get<bool>("rollingStartPending"),"Hysteresis prevents trigger chatter from enabling a restart");
        Brake(0);f.Set("takeoverUntil",-100d);f.Fresh();f.Plugin.Tick();
        assert(!f.State.EnableAssists,"Releasing the brake never resumes driving automatically");
        f.F8();f.Probe();assert(f.State.EnableAssists,"A new explicit F8 can resume after brake release");

        var pending=new Fixture();pending.F8();pending.DuringProbe=()=>pending.Call("OnBrakeInput",null,new ControlChangeEventArgs(.5f,TollPlugin.BrakeControl));pending.Probe();
        assert(!pending.State.EnableAssists&&pending.Installs==0&&!pending.Get<bool>("rollingStartPending"),"Physical brake invalidates an in-flight route result");
        var fault=new Fixture();fault.Get<FaultBrake>("faultBrake").Trip();fault.Outputs.Clear();
        fault.Call("OnBrakeInput",null,new ControlChangeEventArgs(.5f,TollPlugin.BrakeControl));
        assert(!fault.Plugin.FaultBraking&&!fault.State.EnableAssists&&fault.Outputs.All(o=>o.Variables.abackward>=0),
            "Pressing the physical brake explicitly releases a latched fault into manual control");
    }
}
