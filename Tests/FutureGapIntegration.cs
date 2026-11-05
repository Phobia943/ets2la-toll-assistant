using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using ETS2LA.Game.Data;
using ETS2LA.Game.Output;
using ETS2LA.Game.Telemetry;
using ETS2LA.State;
using MautAssistent;
using PathLib;
using TruckLib.ScsMap;

public static class FutureGapIntegration
{
    public static void Run(Action<bool,string> assert)
    {
        PlannedItem RoadItem(float x,float z,ulong uid) {
            var rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI);
            var a=new Node {Uid=uid,Position=new(x,0,z),Rotation=rotation};
            var b=new Node {Uid=uid+1,Position=new(x,0,z+100),Rotation=rotation};
            var road=(ParsedRoad)RuntimeHelpers.GetUninitializedObject(typeof(ParsedRoad));
            road.Road=new Road {Node=a,ForwardNode=b,Length=100};road.StartNode=a;road.EndNode=b;
            road.LeftLaneOffsetsStart=road.LeftLaneOffsetsEnd=[-2];road.RightLaneOffsetsStart=road.RightLaneOffsetsEnd=[2];
            return new PlannedItem {item=new ParsedRoadList([road]),startNode=a,endNode=b,direction=Direction.Forward,laneIndex=1};
        }
        var first=RoadItem(0,0,600);
        var next=RoadItem(4.5f,100,602);
        var route=new PlannedPathData {HasError=false,PlannedPath=[first,next]};
        var truck=first.Interpolate(.015f,0,false)!.Value.Position+new Vector3(.015f,0,0);
        const double speed=52.75971565246582/3.6;
        bool strictFailed=false;
        try {RouteMatcher.Sample(route);}catch(InvalidOperationException){strictFailed=true;}
        assert(strictFailed,"Reproduce the old whole-route 4.5 m gap failure despite a 1.5 cm current offset");
        var prefix=RouteMatcher.SampleForTracking(route,truck,speed);
        assert(prefix.EndsAtGap&&prefix.Length<101&&prefix.Project(truck).Offset<.02,"Use only the connected prefix, never bridge the gap");
        var guard=new TrackingGuard();
        assert(guard.Check(prefix,truck,.5,speed)==null,"Distant unresolved connection must not stop current tracking");
        assert(RouteMatcher.Select(route,truck,[],null,out var sampled,speed)==null&&sampled!.EndsAtGap,"Gate evaluation must use the same horizon even outside a toll plaza");
        bool fastFailed=false;
        try {RouteMatcher.SampleForTracking(route,truck,90/3.6);}catch(InvalidOperationException){fastFailed=true;}
        assert(fastFailed,"Higher speed must retain a longer stopping reserve");
        var near=first.Interpolate(.6f,0,false)!.Value.Position;
        assert(guard.Check(prefix,near,.5,speed)!=null,"Previously accepted prefix must stop when the gap enters the horizon");
        bool nearFailed=false;
        try {RouteMatcher.SampleForTracking(route,near,speed);}catch(InvalidOperationException){nearFailed=true;}
        assert(nearFailed,"Fresh samples must also reject a nearby unresolved gap");
        assert(guard.Check(prefix,truck+new Vector3(5,0,0),.5,speed)!=null,"Do not relax actual lane departure detection");
        route.PlannedPath[1]=RoadItem(0,100,602);
        assert(!RouteMatcher.SampleForTracking(route,near,speed).EndsAtGap,"A corrected future plan becomes usable without stitching unrelated points");
        route.PlannedPath[1]=next;

        const BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        var state=(ApplicationState)RuntimeHelpers.GetUninitializedObject(typeof(ApplicationState));
        state.DrivingMode=DrivingMode.FullSelfDriving;
        var sent=new List<ControlEvent>();
        var plugin=new TollPlugin(()=>state,(_,_)=>{},sent.Add);
        void Set(string name,object value)=>typeof(TollPlugin).GetField(name,hidden)!.SetValue(plugin,value);
        object? Call(string name,params object[] args)=>typeof(TollPlugin).GetMethod(name,hidden)!.Invoke(plugin,args);
        var telemetry=new GameTelemetryData {sdkActive=true,time=1};
        telemetry.truckFloat.speed=(float)speed;telemetry.truckPlacement.rotation=new(.5,0,0);
        telemetry.truckPlacement.coordinate=new(truck.X,truck.Y,truck.Z);
        Call("OnTelemetry",telemetry);Set("active",true);state.EnableAssists=true;
        ((DriveSupervisor)typeof(TollPlugin).GetField("supervisor",hidden)!.GetValue(plugin)!).Step(
            new(0,speed,true,false,true,RouteFeed.Valid,0,true,true));
        Call("OnPath",route);
        assert(plugin.Active&&state.EnableAssists&&!plugin.FaultBraking&&sent.Count==0,"Live route callback must accept the distant gap");
        telemetry.truckPlacement.coordinate=new(near.X,near.Y,near.Z);
        Call("OnTelemetry",telemetry);
        assert(plugin.FaultBraking&&!state.EnableAssists&&sent[^1].Variables.abackward==-1,"Approaching the same gap must still request braking");
        plugin.SetActive(false);
    }
}
