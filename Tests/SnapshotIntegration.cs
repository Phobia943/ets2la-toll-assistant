using System.Runtime.CompilerServices;
using ETS2LA.Game.Data;
using PathLib;
using MautAssistent;

public static class SnapshotIntegration
{
    public static void Run(Action<bool,string> assert)
    {
        // No map files, telemetry providers or vehicle output required.
        var road=(ParsedRoad)RuntimeHelpers.GetUninitializedObject(typeof(ParsedRoad));
        road.LeftLaneOffsetsEnd=[-2]; road.RightLaneOffsetsEnd=[2,6];
        var start=new TruckLib.ScsMap.Node {Uid=201};
        var end=new TruckLib.ScsMap.Node {Uid=202};
        var native=new PlannedItem {item=road,startNode=start,endNode=end,direction=Direction.Forward,laneIndex=1,targetLaneIndex=1,allowedToLaneChange=true};
        var next=new PlannedItem {item=road,startNode=end,endNode=start,direction=Direction.Backward,lastItem=native,laneIndex=1};
        var source=new PlannedPathData {PlannedPath=[native,next],HasError=false};
        // Establish the actual PathLib behaviour: false does not mean read-only.
        native.GenerateLaneChange(.5f,false);
        assert(native.targetLaneIndex==0,"Expected PathLib to reset a completed target even with completion=false");
        native.targetLaneIndex=1;
        var snapshot=RouteSnapshot.Capture(source);
        assert(!ReferenceEquals(source,snapshot)&&!ReferenceEquals(source.PlannedPath,snapshot.PlannedPath),"Planning list must be detached");
        assert(!ReferenceEquals(native,snapshot.PlannedPath[0]),"Mutable lane state must be detached");
        assert(ReferenceEquals(snapshot.PlannedPath[1].lastItem,snapshot.PlannedPath[0]),"Previous-item links must stay inside snapshot");
        snapshot.PlannedPath[0].GenerateLaneChange(.5f,false);
        assert(snapshot.PlannedPath[0].targetLaneIndex==0,"Measurement should see normal PathLib behaviour");
        assert(native.targetLaneIndex==1&&native.laneIndex==1,"Measurement must not alter the host lane state");
        snapshot.PlannedPath.Clear();
        assert(source.PlannedPath.Count==2,"Snapshot list mutations must not affect the native plan");
    }
}
