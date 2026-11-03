using System.Numerics;
using System.Runtime.CompilerServices;
using ETS2LA.Game.Data;
using MautAssistent;
using PathLib;
using TruckLib.Models.Ppd;
using TruckLib.ScsMap;

public static class ExitHandoffIntegration
{
    public static void Run(Action<bool,string> assert)
    {
        foreach(bool backward in new[]{false,true})
            foreach(bool grouped in new[]{false,true}) RunCase(backward,grouped,assert);
    }

    static void RunCase(bool backward,bool grouped,Action<bool,string> assert)
    {
        var rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI);
        var start=new Node {Uid=401,Position=Vector3.Zero,Rotation=rotation};
        var end=new Node {Uid=402,Position=new(0,0,100),Rotation=rotation};
        var raw=new Road {Node=start,ForwardNode=end,Length=100};
        var road=(ParsedRoad)RuntimeHelpers.GetUninitializedObject(typeof(ParsedRoad));
        road.Road=raw;road.StartNode=start;road.EndNode=end;
        road.RightLaneOffsetsStart=road.RightLaneOffsetsEnd=[2,6];
        road.LeftLaneOffsetsStart=road.LeftLaneOffsetsEnd=[-2,-6];
        IParsedItem item=grouped?new ParsedRoadList([road]):road;
        var side=backward?Side.Left:Side.Right;
        int sign=backward?-1:1;
        float entryFactor=backward?1:0;
        var entry=road.InterpolateLane(entryFactor,side,0).Position;
        var nextPoint=road.InterpolateLane(entryFactor+(backward?-.01f:.01f),side,0).Position;
        var forward=Vector3.Normalize(nextPoint-entry);
        var tollRotation=backward?Quaternion.Identity:rotation;
        var curve=new NavCurve {StartPosition=entry-forward*20,EndPosition=entry,StartRotation=tollRotation,EndRotation=tollRotation,Length=20,SemaphoreId=12};
        var prefab=(ParsedPrefab)RuntimeHelpers.GetUninitializedObject(typeof(ParsedPrefab));
        var tollStart=new Node {Uid=400};
        var junction=backward?end:start;
        var tollPath=PrefabRefinement.Refine(new PrefabPath(prefab,tollStart,junction,[curve],Direction.Forward,Matrix4x4.Identity,Vector3.Zero));
        var toll=new PlannedItem {item=prefab,prefabPath=tollPath,startNode=tollStart,endNode=junction,direction=Direction.Forward,laneIndex=0};
        PlannedItem Following()=>new() {item=item,startNode=junction,endNode=backward?start:end,direction=backward?Direction.Backward:Direction.Forward,laneIndex=sign*2,targetLaneIndex=sign,lastItem=toll,allowedToLaneChange=true};
        var original=Following();
        var hostList=new List<PlannedItem>{toll,original};
        // Match the host: a copied event list containing the same mutable items.
        var data=new PlannedPathData {PlannedPath=hostList.ToList(),HasError=false};
        float sampleEntry=backward?.99999f:.00001f;
        float gap=Vector3.Distance(tollPath.Interpolate(.99999f,0)!.Value.Position,original.Interpolate(sampleEntry,0,false)!.Value.Position);
        assert(gap>3.5f,"Reproduce wrong entry lane despite an already correct target lane");
        assert(ExitHandoff.Align(data,0,entry-forward*10,out var error)&&error==null,"Align road to toll exit");
        assert(data.PlannedPath[1].laneIndex==sign && data.PlannedPath[1].targetLaneIndex==0,"Correct current lane, not just target lane");
        assert(hostList[1].laneIndex==sign && ReferenceEquals(hostList[1],data.PlannedPath[1]),"Correction must reach the host's retained planning item");
        assert(road.RightLaneOffsetsStart.SequenceEqual(new float[]{2,6})&&road.LeftLaneOffsetsStart.SequenceEqual(new float[]{-2,-6}),"Do not alter road map geometry");
        var trace=RouteMatcher.Sample(data);
        assert(trace.Project(entry+forward*5).Offset<.05,"Route remains connected and tracked after toll exit");
        var nativeRoadPoint=hostList[1].Interpolate(hostList[1].GetFactorFor(entry+forward*5),0,false);
        assert(nativeRoadPoint.HasValue&&Vector3.Distance(nativeRoadPoint.Value.Position,entry+forward*5)<.05,"Native planner sees the corrected exit lane on the next frame");
        assert(ExitHandoff.Align(data,0,entry+forward*5,out _),"Repeated frames preserve aligned route");
        data.PlannedPath[1]=Following();
        var occupied=road.InterpolateLane(backward?.9f:.1f,side,1).Position;
        assert(!ExitHandoff.Align(data,0,occupied,out error)&&error!=null,"Never jump lanes after entering the other road lane");
        assert(data.PlannedPath[1].laneIndex==sign*2,"Rejected handoff leaves lane unchanged");
        data.PlannedPath[1]=Following();data.PlannedPath[1].startNode=new Node {Uid=999};
        assert(!ExitHandoff.Align(data,0,entry-forward*10,out error),"Never connect a different road node");
        data.PlannedPath[1]=Following();data.PlannedPath[1].laneIndex=-sign;
        assert(!ExitHandoff.Align(data,0,entry-forward*10,out error),"Never cross to the opposite side to repair a route");
        data.PlannedPath[1]=Following();
        if(backward)road.LeftLaneOffsetsStart=road.LeftLaneOffsetsEnd=[-2,-2.3f];
        else road.RightLaneOffsetsStart=road.RightLaneOffsetsEnd=[2,2.3f];
        assert(!ExitHandoff.Align(data,0,entry-forward*10,out error),"Reject ambiguous adjoining lanes");
        data.PlannedPath=[toll];
        assert(ExitHandoff.Align(data,0,entry-forward*10,out error),"Do not invent a road before the host extends the route");
    }
}
