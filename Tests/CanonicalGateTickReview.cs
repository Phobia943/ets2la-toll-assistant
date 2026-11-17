using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using ETS2LA.Controls;
using ETS2LA.Game.Data;
using ETS2LA.Game.SDK;
using ETS2LA.Game.Output;
using ETS2LA.State;
using ETS2LA.Shared;
using MautAssistent;
using PathLib;
using TruckLib.Models.Ppd;
using TruckLib.ScsMap;
using SdkSemaphore=ETS2LA.Game.SDK.Semaphore;
using SdkSemaphoreType=ETS2LA.Game.SDK.SemaphoreType;

// Synthetic map objects use actual PPD/SDK DTOs. There are no game readers,
// native ACC instances, assets, timers, or output singletons in this fixture.
public static class CanonicalGateTickReview
{
    sealed class Planner:ETS2LA.Shared.Plugin
    {
        NavigationData navigationData=new() {entries=[new NavigationEntry {nodeUid=840011}]};
        public override ETS2LA.Shared.PluginInformation Info=>new() {Id="test.gate-review-planner",Name="Fixture planner",Version="1",Description="Read-only synthetic navigation"};
    }
    public sealed record Model(PlannedPathData Plan,Vector3 Truck,SdkSemaphore Own,SdkSemaphore Opposite);

    public static Model Build()
    {
        var forward=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI);
        NavCurve Line(Vector3 start,Vector3 end,int gate,Quaternion rotation)=>new() {
            StartPosition=start,EndPosition=end,StartRotation=rotation,EndRotation=rotation,
            Length=Vector3.Distance(start,end),SemaphoreId=gate,AllowedVehicles=AllowedVehicles.AllVehicles};
        var own=Line(new(0,0,-20),new(0,0,30),10,forward);
        var tail=Line(new(0,0,30),new(0,0,150),-1,forward);own.NextLines.Add(1);tail.PreviousLines.Add(0);
        var opposite=Line(new(3.7975f,0,45),new(3.7975f,0,0),15,Quaternion.Identity);
        var descriptor=new PrefabDescriptor {NavCurves=[own,tail,opposite]};
        var ownAnchor=new Vector3(2.2764f,0,29.6f);var oppositeAnchor=new Vector3(3.7975f,0,21.6487f);
        descriptor.Semaphores.Add(new() {SemaphoreId=10,Position=ownAnchor,Type=TruckLib.Models.Ppd.SemaphoreType.BarrierAutomatic});
        descriptor.Semaphores.Add(new() {SemaphoreId=15,Position=oppositeAnchor,Type=TruckLib.Models.Ppd.SemaphoreType.BarrierAutomatic});
        var parsed=(ParsedPrefab)RuntimeHelpers.GetUninitializedObject(typeof(ParsedPrefab));
        parsed.Prefab=new() {Uid=840010};parsed.Descriptor=descriptor;
        var path=new PrefabPath(parsed,new() {Uid=840011},new() {Uid=840012},[own,tail],Direction.Forward,Matrix4x4.Identity,Vector3.Zero);
        var plan=new PlannedPathData {HasError=false,PlannedPath=[new() {item=parsed,prefabPath=path,
            startNode=path.StartNode,endNode=path.EndNode,direction=Direction.Forward,laneIndex=0}]};
        return new(plan,new(0,0,15),
            new() {id=10,position=ownAnchor,type=SdkSemaphoreType.GATE,state=3},
            new() {id=15,position=oppositeAnchor,type=SdkSemaphoreType.GATE,state=1});
    }

    public sealed class Fixture
    {
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        public readonly Model Map=Build();
        public readonly ApplicationState State=(ApplicationState)RuntimeHelpers.GetUninitializedObject(typeof(ApplicationState));
        public readonly TollPlugin Plugin;
        public readonly List<ControlEvent> Outputs=[];
        public readonly List<string> Notices=[];
        readonly Planner planner=new();
        public Fixture(Action<ControlEvent>? sink=null,bool useOwnedTraffic=false)
        {
            State.DrivingMode=DrivingMode.FullSelfDriving;State.DesiredSpeed=25;
            Plugin=new(()=>State,(title,body)=>Notices.Add(title+": "+body),o=>{Outputs.Add(o);sink?.Invoke(o);},
                readAcc:()=>throw new Exception("The own toll controller must never query native ACC"),
                readPayments:_=>new Dictionary<int,TollPayment>{{10,TollPayment.Automatic},{15,TollPayment.Automatic}},
                readPlanner:()=>planner,readLaneAssist:()=>null,writeDiagnostic:(_,_)=>{},readClearance:_=>true,
                readCruiseLimits:()=>new(0,false),readLongitudinalTraffic:useOwnedTraffic?null:
                    ()=>new(float.NaN,true,"Synthetic fresh empty traffic"));
            Call("StopDiagnostics");Plugin._IsRunning=true;
            Set("options",new TollOptions {CurvePreviewEnabled=false,BriskOpenRoads=false,OpenDelaySeconds=1});
            Set("controller",new TollController(new TollOptions {CurvePreviewEnabled=false,BriskOpenRoads=false,OpenDelaySeconds=1}));
            Set("subscribed",true);Set("lastWrite",double.MaxValue);Set("lastMotionAt",double.MaxValue);
            Set("active",true);Set("position",Map.Truck);Set("headingTurns",.5d);
            Set("path",Map.Plan);Set("lastAcceptedPlan",Map.Plan);Set("safetyTrace",RouteMatcher.Sample(Map.Plan));
            Set("decisionArmed",true);Set("startCheck",new TollPlugin.StartCheck(Map.Truck,.5,ConnectorStart.NavigationKey(planner),null));
            State.EnableAssists=true;
            var router=Get<TollRouter>("router");router.Discover(Map.Plan,Map.Truck,[Map.Own,Map.Opposite],0);
            if(!router.Apply(Map.Plan,Map.Truck,out var error,0)||error!=null||router.SelectedGate!=10)
                throw new InvalidOperationException("The synthetic canonical route failed its actual router setup: "+error);
            Set("safetyTrace",RouteMatcher.Sample(Map.Plan));Fresh(0);
        }
        public void Set(string field,object value)=>typeof(TollPlugin).GetField(field,Hidden)!.SetValue(Plugin,value);
        public T Get<T>(string field)=>(T)typeof(TollPlugin).GetField(field,Hidden)!.GetValue(Plugin)!;
        public object? Call(string name,params object?[] args)=>typeof(TollPlugin).GetMethod(name,Hidden)!.Invoke(Plugin,args);
        public double Now=>Get<Stopwatch>("clock").Elapsed.TotalSeconds;
        public void Fresh(double speed)
        {
            foreach(var field in new[]{"telemetryAt","gatesAt","pathAt","gameTimeChangedAt","lastAcceptedPlanAt","evaluatedAt"})Set(field,Now);
            Set("sdk",true);Set("paused",false);Set("speed",speed);Set("gates",new[]{Map.Own,Map.Opposite});
        }
        public void Tick(double speed) {Fresh(speed);Plugin.Tick();}
        public void Stop()=>Call("StopDiagnostics");
    }

    public static void Run(Action<bool,string> check)
    {
        var f=new Fixture();
        check(Vector3.Distance(f.Map.Truck,f.Map.Opposite.GetWorldCoordinates())<Vector3.Distance(f.Map.Truck,f.Map.Own.GetWorldCoordinates()),
            "The wrong-direction post is physically closer than the canonical open gate");
        var until=Stopwatch.StartNew();
        while(until.Elapsed.TotalSeconds<1.2) {f.Tick(0);Thread.Sleep(20);}
        var open=f.Get<TollDecision>("decision");
        var command=f.Outputs.LastOrDefault(o=>o.ChannelDefinition.Id==TollPlugin.LongitudinalChannelId);
        check(f.State.EnableAssists&&!f.Plugin.FaultBraking&&open.Phase=="Langsame Durchfahrt"&&open.TargetSpeed==10/3.6,
            "Actual Tick derives release from own gate10 Open although closer opposite15 remains Closed");
        check(command?.Variables.aforward>0&&command.Variables.abackward==0,
            "Actual canonical open-gate Tick emits throttle through the own longitudinal channel");
        f.Stop();
        var closed=new Fixture();closed.Map.Own.state=1;closed.Map.Opposite.state=3;
        closed.Tick(0);Thread.Sleep(20); // Establish actual standstill start before a moving approach.
        closed.Tick(10/3.6);
        var decision=closed.Get<TollDecision>("decision");
        var braking=closed.Outputs.LastOrDefault(o=>o.ChannelDefinition.Id==TollPlugin.LongitudinalChannelId);
        check(!closed.Plugin.FaultBraking&&decision.Phase!="Langsame Durchfahrt"&&decision.MaxAcceleration<0,
            "Opposite gate15 Open never releases the canonical own Closed gate10: "+decision.Phase+" / "+decision.MaxAcceleration);
        check(braking?.Variables.aforward==0&&braking.Variables.abackward<0,
            "Actual own-closed gate Tick requests service braking despite the closer open opposing post");
        closed.Stop();
    }
}
