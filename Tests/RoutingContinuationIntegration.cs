using System.Numerics;
using System.Runtime.CompilerServices;
using System.Reflection;
using ETS2LA.Game.Data;
using MautAssistent;
using PathLib;
using TruckLib.Models.Ppd;
using TruckLib.ScsMap;

public static class RoutingContinuationIntegration
{
    static ulong uid=91000;
    sealed record Fixture(ParsedRoad Road,PlannedItem Toll,PlannedItem RoadItem,PlannedItem Next,Vector3 Entry,Vector3 Heading)
    { public PlannedPathData Plan()=>new(){HasError=false,PlannedPath=[Toll,RoadItem,Next]}; }
    static PlannedItem Link(Node from,Node to,Vector3 start,Vector3 end,int gate=-1)
    {
        var q=Quaternion.CreateFromAxisAngle(Vector3.UnitY,end.Z>=start.Z?MathF.PI:0);
        var curve=new NavCurve{StartPosition=start,EndPosition=end,StartRotation=q,EndRotation=q,Length=Vector3.Distance(start,end),
            AllowedVehicles=AllowedVehicles.AllVehicles,SemaphoreId=gate};
        var parsed=(ParsedPrefab)RuntimeHelpers.GetUninitializedObject(typeof(ParsedPrefab));
        parsed.Prefab=new(){Uid=++uid};parsed.Descriptor=new(){NavCurves=[curve]};
        if(gate>=0)parsed.Descriptor.Semaphores.Add(new(){SemaphoreId=(uint)gate,Position=(start+end)/2+new Vector3(2.5f,0,0)});
        var path=new PrefabPath(parsed,from,to,[curve],Direction.Forward,Matrix4x4.Identity,Vector3.Zero);
        return new(){item=parsed,prefabPath=path,otherPrefabPaths=[],startNode=from,endNode=to,laneIndex=0,direction=Direction.Forward};
    }
    static Fixture Make(bool backward=false,float length=400,bool curved=false)
    {
        var q=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI);
        var a=new Node{Uid=++uid,Position=Vector3.Zero,Rotation=q};var b=new Node{Uid=++uid,Position=new(0,0,length),Rotation=curved?Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI+.8f):q};
        var road=(ParsedRoad)RuntimeHelpers.GetUninitializedObject(typeof(ParsedRoad));
        road.Road=new(){Node=a,ForwardNode=b,Length=length,Uid=++uid};road.StartNode=a;road.EndNode=b;
        road.RightLaneOffsetsStart=road.RightLaneOffsetsEnd=[2,6.5f];road.LeftLaneOffsetsStart=road.LeftLaneOffsetsEnd=[-2,-6.5f];
        float entry=backward?1:0,end=1-entry;var start=road.InterpolateLane(entry,Side.Right,0).Position;
        var heading=Vector3.Normalize(road.InterpolateLane(entry+(backward?-.01f:.01f),Side.Right,0).Position-start);
        var tollStart=new Node{Uid=++uid};var junction=backward?b:a;var finish=backward?a:b;var after=new Node{Uid=++uid};
        var toll=Link(tollStart,junction,start-heading*20,start,12);
        var following=Link(finish,after,road.InterpolateLane(end,Side.Right,1).Position,road.InterpolateLane(end,Side.Right,1).Position+heading*20);
        var roadItem=new PlannedItem{item=new ParsedRoadList([road]),startNode=junction,endNode=finish,
            laneIndex=1,targetLaneIndex=0,direction=backward?Direction.Backward:Direction.Forward,allowedToLaneChange=true,lastItem=toll};
        following.lastItem=roadItem;return new(road,toll,roadItem,following,start,heading);
    }
    public static void RoadExit(Action<bool,string> assert)
    {
        foreach(bool backward in new[]{false,true}) {
            var f=Make(backward);var plan=f.Plan();var truck=f.Entry-f.Heading*10;
            assert(ExitHandoff.Align(plan,0,truck,out var error)&&error==null,"A future road gets a verified adjacent end-lane target for the next prefab");
            assert(f.RoadItem.laneIndex==1&&f.RoadItem.targetLaneIndex==2,"Road entry/current lane is preserved; only a future target is planned");
            assert(f.Road.RightLaneOffsetsStart.SequenceEqual(new float[]{2,6.5f}),"A4.5m lane gap is never closed by altering map geometry");
            assert(ExitHandoff.Align(plan,0,truck,out _),"Repeated exit frames retain the same future route instead of resetting it");
            var current=Make(backward);var currentPlan=current.Plan();currentPlan.PlannedPath.RemoveAt(0);
            var mid=current.Road.InterpolateLane(.5f,Side.Right,0).Position;
            assert(ExitHandoff.RepairRoadConnections(currentPlan,mid)==1&&current.RoadItem.laneIndex==1&&current.RoadItem.targetLaneIndex==2,
                "A pruned plan beginning on the road can still plan its physically reachable future end-lane change");
            var late=Make(backward);var latePlan=late.Plan();latePlan.PlannedPath.RemoveAt(0);
            var lateTruck=late.Road.InterpolateLane(backward?.0375f:.9625f,Side.Right,0).Position;
            assert(ExitHandoff.RepairRoadConnections(latePlan,lateTruck)==0&&late.RoadItem.targetLaneIndex==0&&late.RoadItem.laneIndex==1,
                "Fifteen metres before the next prefab is too late to invent the missing lane change");
            var denied=Make(backward);denied.RoadItem.allowedToLaneChange=false;
            assert(!ExitHandoff.Align(denied.Plan(),0,denied.Entry-denied.Heading*10,out _)&&denied.RoadItem.targetLaneIndex==0,
                "A forbidden lane change cannot be converted into an exit repair");
            var node=Make(backward);node.Next.startNode=new(){Uid=++uid};
            assert(!ExitHandoff.Align(node.Plan(),0,node.Entry-node.Heading*10,out _)&&node.RoadItem.targetLaneIndex==0,
                "Even coincident endpoints with different canonical nodes are rejected transactionally");
        }
    }
    public static void ConnectorAlternatives(Action<bool,string> assert)
    {
        var f=Make();var start=f.Road.InterpolateLane(1,Side.Right,0).Position;
        var valid=Link(f.Next.startNode,f.Next.endNode,start,start+f.Heading*20);
        // Same parsed descriptor as the actual host alternative contract.
        var original=f.Next.prefabPath!;var d=original.Prefab.Descriptor!;var c=valid.prefabPath!.Curves[0];d.NavCurves.Add(c);
        var path=new PrefabPath(original.Prefab,original.StartNode,original.EndNode,[c],Direction.Forward,Matrix4x4.Identity,Vector3.Zero);
        f.Next.otherPrefabPaths=[path];
        original.Curves[0].SemaphoreId=22;
        assert(ExitHandoff.Align(f.Plan(),0,f.Entry-f.Heading*10,out _)&&f.RoadItem.targetLaneIndex==2&&
            PrefabRefinement.OriginalFor(f.Next.prefabPath!).Curves.Contains(original.Curves[0]),
            "A following semaphore binding cannot be bypassed by selecting a signal-free connector");
        original.Curves[0].SemaphoreId=-1;f.RoadItem.targetLaneIndex=0;
        c.TrafficRule="stop";
        assert(ExitHandoff.Align(f.Plan(),0,f.Entry-f.Heading*10,out _)&&f.RoadItem.targetLaneIndex==2&&
            PrefabRefinement.OriginalFor(f.Next.prefabPath!).Curves.Contains(original.Curves[0]),
            "A different traffic-control token does not authorize replacing the existing directed connector");
        c.TrafficRule=original.Curves[0].TrafficRule;f.RoadItem.targetLaneIndex=0;
        assert(ExitHandoff.Align(f.Plan(),0,f.Entry-f.Heading*10,out _)&&f.RoadItem.targetLaneIndex==0&&
            PrefabRefinement.OriginalFor(f.Next.prefabPath!).Curves.Contains(c),
            "Prefer the unique real matching next connector over an unnecessary road lane change");
        var blocked=Make();blocked.Next.prefabPath!.Curves[0].AllowedVehicles=AllowedVehicles.SmallVehicles;
        assert(!ExitHandoff.Align(blocked.Plan(),0,blocked.Entry-blocked.Heading*10,out _)&&blocked.RoadItem.targetLaneIndex==0,
            "A small-vehicle-only following path cannot receive a truck route by geometry alone");
        var retained=Make();var retainedCurve=retained.Next.prefabPath!.Curves[0];
        retainedCurve.StartPosition=retained.Road.InterpolateLane(1,Side.Right,0).Position;
        retainedCurve.EndPosition=retainedCurve.StartPosition+retained.Heading*20;
        retainedCurve.AllowedVehicles=AllowedVehicles.SmallVehicles;retainedCurve.SemaphoreId=15;
        assert(ExitHandoff.Align(retained.Plan(),0,retained.Entry-retained.Heading*10,out _)&&retained.RoadItem.targetLaneIndex==0&&
            ReferenceEquals(retained.Next.prefabPath!.Curves[0],retainedCurve),
            "An unchanged already-connected Automatic/SmallVehicles host route is retained; seam validation grants no new payment permission");
        var upper=Make();upper.Next.prefabPath!.Curves[0].StartPosition+=Vector3.UnitY*4;upper.Next.prefabPath.Curves[0].EndPosition+=Vector3.UnitY*4;
        assert(!ExitHandoff.Align(upper.Plan(),0,upper.Entry-upper.Heading*10,out _),"A different road level is not a connected continuation");
    }
    public static void StrictContinuation(Action<bool,string> assert)
    {
        var pending=Make(true);pending.RoadItem.targetLaneIndex=2;
        assert(ExitHandoff.Align(pending.Plan(),0,pending.Entry-pending.Heading*10,out _)&&pending.RoadItem.laneIndex==1&&pending.RoadItem.targetLaneIndex==2,
            "A valid existing future target remains a target; backward travel never flips its carriageway sign");
        var conflict=Make();conflict.RoadItem.targetLaneIndex=-1;
        assert(!ExitHandoff.Align(conflict.Plan(),0,conflict.Entry-conflict.Heading*10,out _)&&conflict.RoadItem.targetLaneIndex==-1,
            "An opposite-side pending target is rejected without silently overwriting it");
        var revoked=Make();revoked.RoadItem.targetLaneIndex=2;revoked.RoadItem.allowedToLaneChange=false;
        assert(!ExitHandoff.Align(revoked.Plan(),0,revoked.Entry-revoked.Heading*10,out _)&&revoked.RoadItem.targetLaneIndex==2,
            "A stored target without current lane-change permission cannot prove a physical road-end connection");
        foreach(bool replaceItem in new[]{false,true}) {
            var race=Make();var actual=race.Plan();var staged=RouteSnapshot.Capture(actual);
            var capture=typeof(ExitHandoff).GetMethod("CaptureBasis",BindingFlags.Static|BindingFlags.NonPublic)!;
            var commit=typeof(ExitHandoff).GetMethod("TryCommit",BindingFlags.Static|BindingFlags.NonPublic)!;
            object basis=capture.Invoke(null,[actual])!;staged.PlannedPath[1].targetLaneIndex=2;
            if(replaceItem)actual.PlannedPath[1]=RouteSnapshot.Capture(actual).PlannedPath[1];
            else race.RoadItem.allowedToLaneChange=false;
            object?[] args=[actual,staged,basis,false];
            assert(!(bool)commit.Invoke(null,args)!&&actual.PlannedPath[1].targetLaneIndex==0,
                "Changed original item/permission during staging rejects the whole optimistic batch");
        }
        var shortRoad=Make(false,150);
        assert(!ExitHandoff.Align(shortRoad.Plan(),0,shortRoad.Entry-shortRoad.Heading*10,out _)&&shortRoad.RoadItem.targetLaneIndex==0,
            "A short future road cannot receive an unbounded or accelerated exit lane change");
        var ambiguous=Make();ambiguous.Road.RightLaneOffsetsStart=ambiguous.Road.RightLaneOffsetsEnd=[2,2.2f];
        assert(!ExitHandoff.Align(ambiguous.Plan(),0,ambiguous.Entry-ambiguous.Heading*10,out _),
            "Two geometrically close road entry/end candidates are not made unique by a lane label");
        var transaction=Make();transaction.RoadItem.laneIndex=2;
        var nav=transaction.Next.prefabPath!.Curves[0];nav.StartPosition+=new Vector3(30,0,0);nav.EndPosition+=new Vector3(30,0,0);
        assert(!ExitHandoff.Align(transaction.Plan(),0,transaction.Entry-transaction.Heading*10,out _)&&
            transaction.RoadItem.laneIndex==2&&transaction.RoadItem.targetLaneIndex==0,
            "Failure at the following item cannot partially commit the earlier valid road-entry correction");
        foreach(var mode in new[]{AllowedVehicles.SmallVehicles,AllowedVehicles.PlayerOnly}) {
            var restricted=Make();restricted.Next.prefabPath!.Curves[0].AllowedVehicles=mode;
            assert(!ExitHandoff.Align(restricted.Plan(),0,restricted.Entry-restricted.Heading*10,out _),"Following-route vehicle restrictions remain strict: "+mode);
        }
        var escort=Make();escort.Next.prefabPath!.Curves[0].TrafficRule="escort_only";
        assert(!ExitHandoff.Align(escort.Plan(),0,escort.Entry-escort.Heading*10,out _),"Escort-only following route remains excluded");
    }
    public static void AdvanceHold(Action<bool,string> assert)
    {
        var f=Make(false,332);var road=f.RoadItem;
        // Move the toll after the road for an incoming approach fixture.
        var start=f.Road.InterpolateLane(1,Side.Right,0).Position;
        var toll=Link(road.endNode,new(){Uid=++uid},start,start+f.Heading*20,7);
        road.lastItem=null;var plan=new PlannedPathData{HasError=false,PlannedPath=[road,toll]};
        var router=new TollRouter(new PreferenceStore(Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json")),
            _=>new Dictionary<int,TollPayment>{{7,TollPayment.Automatic}},_=>false);
        var truck=f.Road.InterpolateLane(.2f,Side.Right,0).Position;
        router.Discover(plan,truck,[],20/3.6);
        assert(router.Apply(plan,truck,out _)&&router.StopBoundary.HasValue&&router.PlanningBoundary.HasValue,
            "Pending lane evidence preserves the physical split boundary AND independently anchors an earlier planning stop");
        var anchor=router.PlanningBoundary!.Value;var key=router.PlanningStopKey;
        assert(Math.Abs(Vector3.Distance(anchor,start)-60)<.05,"The early anchor preserves45m lane-change reserve plus15m normal margin");
        double physical=router.UpdateStopDistance(plan,truck,20/3.6)!.Value;
        assert(router.PlanningStopDistance is double early&&Math.Abs(physical-early-45)<.1,
            "Physical emergency distance is unchanged; only the separate normal target is45m earlier");
        var next=truck+f.Heading*5;router.Apply(plan,next,out _);router.UpdateStopDistance(plan,next,20/3.6);
        assert(router.PlanningBoundary==anchor&&router.PlanningStopKey==key,"Repeated pending frames cannot drag the hold anchor along with the truck");
        var partial=new RouteTrace(new[]{f.Road.InterpolateLane(0,Side.Right,0).Position,f.Road.InterpolateLane(.5f,Side.Right,0).Position},
            null,new[]{RouteMatcher.ItemIdentity(road)}){EndsAtGap=true};
        typeof(TollRouter).GetMethod("UpdatePlanningStop",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(router,[plan,1,partial,truck,null]);
        assert(router.PlanningBoundary==null&&router.StopBoundary.HasValue,
            "A partial trace of the same road cannot masquerade as its real approach end and create a shortened60m target");
        router.UpdateStopDistance(plan,truck,20/3.6);
        assert(router.PlanningBoundary==anchor,"The complete proven approach restores exactly its original fixed anchor");
        router.CancelPlannedStop();
        assert(router.PlanningBoundary==null&&router.PlanningStopKey==null&&router.PlanningStopDistance==null,"Manual cancel revokes the independent early-stop owner");
        var late=f.Road.InterpolateLane(317f/332,Side.Right,0).Position;router.Apply(plan,late,out _);
        assert(router.StopBoundary.HasValue&&!router.PlanningBoundary.HasValue,"A late15m stop cannot fabricate a new earlier planning target or move it ahead");
        router.ResetSelection();assert(router.StopBoundary==null&&router.PlanningBoundary==null,"F9/reset clears both distinct boundaries");
    }
    public static void CurvedHold(Action<bool,string> assert)
    {
        var f=Make(false,400,true);var road=f.RoadItem;road.lastItem=null;
        var start=f.Road.InterpolateLane(1,Side.Right,0).Position;
        var toll=Link(road.endNode,new(){Uid=++uid},start,start+new Vector3(0,0,20),7);
        var plan=new PlannedPathData{HasError=false,PlannedPath=[road,toll]};
        var router=new TollRouter(new PreferenceStore(Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json")),
            _=>new Dictionary<int,TollPayment>{{7,TollPayment.Automatic}},_=>false);
        var truck=f.Road.InterpolateLane(.2f,Side.Right,0).Position;
        router.Discover(plan,truck,[]);assert(router.Apply(plan,truck,out _)&&router.PlanningBoundary.HasValue,"Curved approach obtains its own evidenced planning anchor");
        var trace=RouteMatcher.Sample(plan);assert(trace.TryItemRange(RouteMatcher.ItemIdentity(road),out double a,out double b),"The exact curved road identity is present");
        var target=trace.ProjectNearForTracking(router.PlanningBoundary!.Value,(a+b)/2,(b-a)/2);
        assert(Math.Abs(b-target.Along-60)<.02,"Reservation follows60m of directed road arc, not raw road factor or world straight-line distance");
        var key=router.PlanningStopKey;f.Road.Road.Uid=++uid;router.UpdateStopDistance(plan,truck,0);
        assert(router.PlanningStopKey!=key,"Changed actual road provenance cannot reuse an old hold owner");
    }
    public static void PaymentStatus(Action<bool,string> assert)
    {
        var q=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI);
        NavCurve C(Vector3 a,Vector3 b,int gate=-1)=>new(){StartPosition=a,EndPosition=b,StartRotation=q,EndRotation=q,
            Length=Vector3.Distance(a,b),AllowedVehicles=AllowedVehicles.AllVehicles,SemaphoreId=gate};
        var curves=new List<NavCurve>{C(new(0,0,0),new(0,0,20)),C(new(0,0,20),new(0,0,40),4),C(new(0,0,40),new(0,0,80)),
            C(new(0,0,20),new(6,0,40)),C(new(6,0,40),new(6,0,60),12),C(new(6,0,60),new(0,0,80)),C(new(0,0,80),new(0,0,100))};
        foreach(var edge in new[]{(0,1),(0,3),(1,2),(2,6),(3,4),(4,5),(5,6)})curves[edge.Item1].NextLines.Add(edge.Item2);
        var parsed=(ParsedPrefab)RuntimeHelpers.GetUninitializedObject(typeof(ParsedPrefab));parsed.Prefab=new(){Uid=++uid};parsed.Descriptor=new(){NavCurves=curves};
        parsed.Descriptor.Semaphores.Add(new(){SemaphoreId=4,Position=new(2.5f,0,39.6f)});
        parsed.Descriptor.Semaphores.Add(new(){SemaphoreId=12,Position=new(8.5f,0,59.6f)});
        var path=new PrefabPath(parsed,new(){Uid=++uid},new(){Uid=++uid},new[]{0,1,2,6}.Select(i=>curves[i]).ToList(),Direction.Forward,Matrix4x4.Identity,Vector3.Zero);
        var item=new PlannedItem{item=parsed,prefabPath=path,startNode=path.StartNode,endNode=path.EndNode,laneIndex=0,direction=Direction.Forward};
        var plan=new PlannedPathData{HasError=false,PlannedPath=[item]};
        var router=new TollRouter(new PreferenceStore(Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json")),
            _=>new Dictionary<int,TollPayment>{{4,TollPayment.Manual},{12,TollPayment.Automatic}},
            p=>!PrefabRefinement.OriginalFor(p).Curves.Any(c=>c.SemaphoreId==12));
        router.Discover(plan,new(0,0,5),[]);
        assert(router.SelectedGate==4&&router.Status.Contains("automatische Zufahrt derzeit nicht freigegeben"),
            "A reachable manual fallback with unproven automatic access does not classify the whole plaza as manual-only");
        var label=typeof(TollRouter).GetMethod("LaneStatus",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(router,[99])!.ToString()!;
        assert(label.Contains("nicht bestätigt")&&!label.Contains("manueller Zahlung"),
            "Unknown payment can never fall through the status formatter into a manual-payment classification");
    }
    public sealed class LateUpgradeFixture
    {
        public readonly ParsedRoad Road;
        public readonly PlannedItem RoadItem,Toll;
        public readonly ParsedPrefab Parsed;
        public readonly List<NavCurve> Curves;
        public readonly Node Last;
        public readonly PlannedPathData Data;
        public readonly TollRouter Router;
        public readonly Vector3 Heading;
        public bool Ready,AutoClear=true;
        public readonly Func<ParsedPrefab,IReadOnlyDictionary<int,TollPayment>> Payments;
        public readonly Func<PrefabPath,bool> Clearance;
        public readonly Func<PrefabPath,int,TollRouter.RouteSearch> SearchRoute;
        public NavCurve Curve(Vector3 start,Vector3 end,int gate=-1) {
            var q=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI);
            return new(){StartPosition=start,EndPosition=end,StartRotation=q,EndRotation=q,
                Length=Vector3.Distance(start,end),AllowedVehicles=AllowedVehicles.AllVehicles,SemaphoreId=gate};
        }
        public Vector3 At(float rest)=>Road.InterpolateLane(1-rest/400,Side.Right,0).Position;
        internal LateUpgradeFixture()
        {
            var f=Make();Road=f.Road;RoadItem=f.RoadItem;Heading=f.Heading;RoadItem.lastItem=null;
            Vector3 a=Road.InterpolateLane(1,Side.Right,0).Position,b=Road.InterpolateLane(1,Side.Right,1).Position;
            Curves=[Curve(a,a+Heading*20,4),Curve(a+Heading*20,a+Heading*60),
                Curve(b,b+Heading*20,12),Curve(b+Heading*20,a+Heading*60)];
            Curves[0].NextLines.Add(1);Curves[2].NextLines.Add(3);
            Parsed=(ParsedPrefab)RuntimeHelpers.GetUninitializedObject(typeof(ParsedPrefab));Parsed.Prefab=new(){Uid=++uid};Parsed.Descriptor=new(){NavCurves=Curves};
            Parsed.Descriptor.Semaphores.Add(new(){SemaphoreId=4,Position=a+Heading*19.6f+Vector3.UnitX*2.5f});
            Parsed.Descriptor.Semaphores.Add(new(){SemaphoreId=12,Position=b+Heading*19.6f+Vector3.UnitX*2.5f});
            Last=new(){Uid=++uid};
            var manual=new PrefabPath(Parsed,RoadItem.endNode,Last,[Curves[0],Curves[1]],Direction.Forward,Matrix4x4.Identity,Vector3.Zero);
            var automatic=new PrefabPath(Parsed,RoadItem.endNode,Last,[Curves[2],Curves[3]],Direction.Forward,Matrix4x4.Identity,Vector3.Zero);
            Toll=new(){item=Parsed,prefabPath=manual,otherPrefabPaths=[automatic],startNode=RoadItem.endNode,endNode=Last,direction=Direction.Forward,laneIndex=0,lastItem=RoadItem};
            Data=new(){HasError=false,PlannedPath=[RoadItem,Toll]};
            Payments=_=>Ready?new Dictionary<int,TollPayment>{{4,TollPayment.Manual},{12,TollPayment.Automatic}}:new Dictionary<int,TollPayment>();
            Clearance=p=>Ready&&(AutoClear||!PrefabRefinement.OriginalFor(p).Curves.Any(c=>c.SemaphoreId==12));
            SearchRoute=(p,g)=>new(TollRouter.ViaGate(p,g),"Synthetic current directed graph",[]);
            Router=new(new PreferenceStore(Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json")),Payments,Clearance,findClearRoute:SearchRoute);
        }
    }
    public static LateUpgradeFixture CreateLateUpgradeFixture()=>new();
    public static void LateAutomaticUpgrade(Action<bool,string> assert)=>RunLateAutomaticUpgrade(assert);
    public static void AutomaticPreparationPlant(Action<bool,string> assert)=>RunLateAutomaticUpgrade(assert,"plant");
    public static void AutomaticPreparationUnavailable(Action<bool,string> assert)
    {
        foreach(string issue in new[]{"late-fast","blocked-initial","permission-initial","unknown-initial"})RunLateAutomaticUpgrade(assert,issue);
    }
    public static void UpgradeSourceRevocation(Action<bool,string> assert)
    {
        foreach(string mutation in new[]{"placement","model","variant","parts","descriptor","rawprefab","canonical","curve-pose","controlnode","reset","profile","late-entry"})
            RunLateAutomaticUpgrade(assert,mutation);
    }
    static void RunLateAutomaticUpgrade(Action<bool,string> assert,string? mutation=null)
    {
        var fixture=CreateLateUpgradeFixture();var road=fixture.RoadItem;var parsed=fixture.Parsed;
        var curves=fixture.Curves;var last=fixture.Last;var toll=fixture.Toll;var data=fixture.Data;var router=fixture.Router;
        Vector3 At(float rest)=>fixture.At(rest);
        NavCurve C(Vector3 start,Vector3 end,int gate=-1)=>fixture.Curve(start,end,gate);
        void Search(float rest,double speed) {
            typeof(TollRouter).GetField("lastSearch",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(router,double.NegativeInfinity);
            router.Discover(data,At(rest),[],speed);
        }
        Search(220,77/3.6);assert(router.SelectedGate==null,"Pending model source cannot select an unproven automatic lane");
        fixture.Ready=true;
        if(mutation is "late-fast" or "blocked-initial" or "permission-initial" or "unknown-initial") {
            if(mutation=="blocked-initial")fixture.AutoClear=false;
            if(mutation=="permission-initial")road.allowedToLaneChange=false;
            if(mutation=="unknown-initial")fixture.Ready=false;
            float rest=mutation=="late-fast"?75:130;Search(rest,77/3.6);
            assert(router.SelectedGate==(mutation=="unknown-initial"?null:4)&&!router.PreparingAutomaticEntry&&router.PlanningStopDistance==null,
                "Impossible/unproven automatic preparation does not manufacture permission or an endless normal stop: "+mutation);
            assert(router.Apply(data,At(rest),out _,77/3.6)&&road.targetLaneIndex==0&&!router.PreparingAutomaticEntry&&router.PlanningStopDistance==null,
                "Actual Apply preserves the honest available route without forcing an unproved preparation: "+mutation);
            return;
        }
        Search(130,77/3.6);
        assert(router.SelectedGate==4&&router.LaneAttempts.Any(x=>x.Gate==12&&x.Reason=="EntryNotReachable"),
            "At130m/77kmh automatic entry violates the existing160m/high-speed rule; the reachable manual fallback is honest");
        assert(router.Apply(data,At(130),out _,77/3.6)&&router.SelectedGate==4&&road.targetLaneIndex==0,
            "Initial actual manual Apply cannot silently initiate the unavailable automatic lane change");
        var committed=toll.prefabPath;
        assert(toll.otherPrefabPaths.All(p=>!PrefabRefinement.OriginalFor(p).Curves.Any(c=>c.SemaphoreId==12)),
            "Actual manual Apply removes the host's other automatic entry option; later upgrade cannot assume it was republished");
        if(mutation=="plant") {
            assert(router.PreparingAutomaticEntry&&router.PlanningStopDistance is >65 and <75&&router.StopBoundary==null,
                "Known free but currently too-fast automatic entry preserves a separate normal60m preparation target despite manual selection");
            var controller=new LongitudinalController();double rest=130,velocity=77/3.6,acceleration=0;
            float actualThrottle=0,actualBrake=0;bool upgraded=false,exclusive=true,noFault=true;double upgradeSpeed=0,upgradeRest=0;
            for(int i=0;i<600;i++) {
                var point=At((float)rest);
                router.Discover(data,point,[],velocity);
                if(!router.Apply(data,point,out _,velocity))throw new Exception("Closed preparation plant lost the actual route");
                if(router.SelectedGate==12) {upgraded=true;upgradeSpeed=velocity;upgradeRest=rest;break;}
                var output=controller.Step(new((i+1)*.05,1,true,false,true,velocity,acceleration,100/3.6,
                    StopDistance:router.PlanningStopDistance,ActualThrottle:actualThrottle,ActualBrake:actualBrake,StopIdentity:router.PlanningStopKey));
                exclusive&=output.Throttle==0||output.Brake==0;noFault&=!output.Fault&&router.StopBoundary==null;
                float response=(float)(1-Math.Exp(-.05/.15));
                actualThrottle+=(output.Throttle-actualThrottle)*response;actualBrake+=(output.Brake-actualBrake)*response;
                double drag=.06+.00018*velocity*velocity;
                double next=Math.Max(0,velocity+(actualThrottle-3.2*actualBrake-drag)*.05);
                acceleration=(next-velocity)/.05;rest-=(velocity+next)*.025;velocity=next;
                if(rest<44)break;
            }
            assert(upgraded&&upgradeSpeed<=TollEntry.ShortChangeSpeed&&upgradeRest>=TollEntry.MinimumChangeMetres,
                "Closed loaded plant with150ms pedal lag slows77kmh into a legal automatic entry before45m: "+upgradeRest+"m / "+upgradeSpeed*3.6+"kmh");
            assert(exclusive&&noFault&&!router.PreparingAutomaticEntry&&router.PlanningStopDistance==null&&road.targetLaneIndex==2&&router.HasShortChange,
                "Closed loop keeps exclusive normal pedals/no physical fault boundary, then releases preparation exactly for the actual verified short change");
            return;
        }
        if(mutation!=null) {
            switch(mutation) {
                case "placement":
                    var original=PrefabRefinement.OriginalFor(toll.prefabPath!);
                    typeof(PrefabPath).GetField("rotationMatrix",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(original,Matrix4x4.CreateTranslation(.25f,0,0));break;
                case "variant":parsed.Prefab.Variant="changed";break;
                case "model":parsed.Prefab.Model="changed";break;
                case "parts":parsed.Prefab.AdditionalParts.Add("changed");break;
                case "descriptor":
                    var replacementDescriptor=new PrefabDescriptor{NavCurves=parsed.Descriptor!.NavCurves};
                    foreach(var signal in parsed.Descriptor.Semaphores)replacementDescriptor.Semaphores.Add(signal);
                    parsed.Descriptor=replacementDescriptor;break;
                case "rawprefab":parsed.Prefab=new(){Uid=parsed.Prefab.Uid};break;
                case "canonical":parsed.Descriptor!.NavCurves[2]=C(curves[2].StartPosition,curves[2].EndPosition,12);break;
                case "curve-pose":curves[2].StartPosition+=Vector3.UnitX*.25f;break;
                case "controlnode":
                    var replacementNode=new Node{Uid=road.endNode.Uid,Position=road.endNode.Position,Rotation=road.endNode.Rotation};
                    toll.startNode=replacementNode;toll.prefabPath=new(parsed,replacementNode,last,[curves[0],curves[1]],Direction.Forward,Matrix4x4.Identity,Vector3.Zero);break;
                case "reset":router.ResetSelection();break;
                case "profile":fixture.AutoClear=false;break;
            }
            var retainedPath=toll.prefabPath;Search(mutation=="late-entry"?44:65,20/3.6);
            assert(router.SelectedGate==4&&road.targetLaneIndex==0&&ReferenceEquals(toll.prefabPath,retainedPath)&&
                !router.PreparingAutomaticEntry&&router.PlanningStopDistance==null,
                "Retained topology cannot lend old automatic movement permission after source/profile/reset mutation: "+mutation);
            return;
        }
        Search(100,77/3.6);
        assert(router.SelectedGate==4&&road.targetLaneIndex==0&&ReferenceEquals(toll.prefabPath,committed),
            "Still-high speed cannot borrow the future low-speed upgrade permission");
        road.allowedToLaneChange=false;Search(90,20/3.6);
        assert(router.SelectedGate==4&&road.targetLaneIndex==0,"Revoked road lane-change permission preserves the exact manual choice");
        road.allowedToLaneChange=true;fixture.AutoClear=false;Search(85,20/3.6);
        assert(router.SelectedGate==4&&road.targetLaneIndex==0,"A physically blocked automatic candidate cannot upgrade merely because entry distance now fits");
        fixture.AutoClear=true;road.targetLaneIndex=2;Search(80,20/3.6);
        assert(router.SelectedGate==4&&road.targetLaneIndex==2,"An already active native change cannot be overwritten by opportunistic automatic selection");
        road.targetLaneIndex=0;Search(65,20/3.6);
        assert(router.SelectedGate==12&&router.PaymentFor(12)==TollPayment.Automatic&&ReferenceEquals(toll.prefabPath,committed)&&road.targetLaneIndex==0,
            "At65m/20kmh a newly valid45m entry can replace the manual selection without mutating the actual road during discovery");
        assert(router.Apply(data,At(65),out _,20/3.6)&&road.laneIndex==1&&road.targetLaneIndex==2&&router.HasShortChange&&
            PrefabRefinement.OriginalFor(toll.prefabPath!).Curves.Any(c=>c.SemaphoreId==12),
            "Actual Apply revalidates and commits the legal adjacent short change and automatic path together");
    }
}
