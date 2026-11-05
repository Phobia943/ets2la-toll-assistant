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
        assert(ExitHandoff.RepairRoadConnections(data,entry-forward*10)==1 && data.PlannedPath[1].laneIndex==sign,
            "Normal route handoff must repair the connected lane before the gap watchdog");
        assert(ExitHandoff.RepairRoadConnections(data,entry-forward*10)==0,
            "Do not modify an already connected route again");

        var seam = new Node { Uid = 407 };
        PlannedItem Connector(Node from, Node to, Vector3 a, Vector3 b)
        {
            var nav = new NavCurve { StartPosition=a,EndPosition=b,StartRotation=tollRotation,EndRotation=tollRotation,
                Length=Vector3.Distance(a,b),SemaphoreId=-1,AllowedVehicles=AllowedVehicles.AllVehicles };
            return new PlannedItem { item=prefab, startNode=from, endNode=to, direction=Direction.Forward, laneIndex=0,
                prefabPath=new PrefabPath(prefab,from,to,[nav],Direction.Forward,Matrix4x4.Identity,Vector3.Zero) };
        }
        var seed=Connector(tollStart,seam,entry-forward*45,entry-forward*20);
        var link=Connector(seam,junction,entry-forward*20,entry);
        var wrongGuess=Following();
        wrongGuess.laneIndex=-sign;
        var chain=ConnectorStart.BuildPlan(seed,entry-forward*40,items=>items.Count==1?link:wrongGuess);
        assert(chain is { Count: 3 } && chain[^1].laneIndex==sign,"Validate connected prefabs and find the unique legal road lane");
        assert(RouteMatcher.Sample(new() { PlannedPath=chain!, HasError=false }).Project(entry+forward*5).Offset<.05,
            "The chain must connect to the correct physical exit");
        var broken=Connector(seam,junction,entry-forward*14,entry);
        assert(ConnectorStart.BuildPlan(seed,entry-forward*40,items=>items.Count==1?broken:Following())==null,
            "A shared node does not justify crossing a geometric gap");
        broken.otherPrefabPaths=[link.prefabPath!];
        var handoff=new PlannedPathData { PlannedPath=[seed,broken], HasError=false };
        assert(ExitHandoff.Align(handoff,0,entry-forward*30,out _),
            "A toll exit can reconnect to the unique valid continuation through another prefab");
        assert(RouteMatcher.Sample(handoff).Project(entry-forward*18).Offset<.05,
            "The chosen connector must follow the existing exit without a gap");
        link.prefabPath!.Curves[0].AllowedVehicles=AllowedVehicles.SmallVehicles;
        assert(ConnectorStart.BuildPlan(seed,entry-forward*40,items=>items.Count==1?link:Following())==null,
            "Every connector must permit trucks");

        data.PlannedPath[1]=Following();
        var occupied=road.InterpolateLane(backward?.9f:.1f,side,1).Position;
        assert(ExitHandoff.RepairRoadConnections(data,occupied)==0,"Live seam repair must not jump out of an occupied lane");
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
