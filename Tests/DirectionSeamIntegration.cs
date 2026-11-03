using System.Numerics;
using System.Runtime.CompilerServices;
using ETS2LA.Game.Data;
using MautAssistent;
using PathLib;
using TruckLib.ScsMap;

public static class DirectionSeamIntegration
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
        var first=RoadItem(0,0,700);var second=RoadItem(.3f,99.5f,702);
        var end=first.Interpolate(.99999f,0,false)!.Value.Position;
        var start=second.Interpolate(.00001f,0,false)!.Value.Position;
        var truck=(end+start)/2;
        var old=new RouteTrace(new[]{first.Interpolate(.00001f,0,false)!.Value.Position,end,start,second.Interpolate(.99999f,0,false)!.Value.Position});
        assert(new TrackingGuard().Check(old,truck,.5,57/3.6)!=null,"Old seam tangent reproduces a false opposite-heading stop between forward roads");
        var path=new PlannedPathData {HasError=false,PlannedPath=[first,second]};
        var trace=RouteMatcher.SampleForTracking(path,truck,57/3.6);
        assert(trace.Project(truck).Offset<.01,"Test point lies on the connecting seam");
        var guard=new TrackingGuard();
        assert(guard.Check(trace,truck,.5,57/3.6)==null,"Use actual forward road direction across the short seam");
        assert(guard.Check(trace,truck,0,57/3.6)!=null,"Actual opposite truck direction still trips at the same seam");
        assert(guard.LastDirectionFailure!=null,"Direction fault records vectors and local sample context");
        path.PlannedPath[1]=RoadItem(0,97,704);
        bool rejected=false;
        try {RouteMatcher.SampleForTracking(path,truck,57/3.6);}catch(InvalidOperationException){rejected=true;}
        assert(rejected,"A three-metre break cannot be disguised as a short seam");
    }
}
