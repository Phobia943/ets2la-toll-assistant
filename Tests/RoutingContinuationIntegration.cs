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
    public static void SlowExit(Action<bool,string> assert)
    {
        // Deliberately synthetic policy geometry. Native lane-change shape and
        // telemetry-driven state transitions are exercised only by SlowExitNative
        // in the hermetic HostCompatibility replay, never this default group.
        RouteTrace PolicyTrace(Fixture f,PlannedItem? currentRoad=null) {
            var entry=f.Toll.prefabPath!.Interpolate(1,0)!.Value.Position;
            var origin=f.Toll.prefabPath.Interpolate(0,0)!.Value.Position;
            bool reverse=f.RoadItem.direction==Direction.Backward;
            var exit=f.Road.InterpolateLane(reverse?0:1,Side.Right,1).Position;
            var after=f.Next.prefabPath!.Interpolate(1,0)!.Value.Position;
            return new RouteTrace(new[]{origin,entry,exit,after},null,new[]{RouteMatcher.ItemIdentity(f.Toll),
                RouteMatcher.ItemIdentity(currentRoad??f.RoadItem),RouteMatcher.ItemIdentity(f.Next)});
        }
        foreach(bool backward in new[]{false,true}) {
            var f=Make(backward,118.76f);var plan=f.Plan();var truck=f.Entry-f.Heading*10;
            assert(!ExitHandoff.Align(plan,0,truck,out _),"Unowned short exits keep the original conservative limit");
            assert(ExitHandoff.Align(plan,0,truck,20/3.6,out var slow,out var error)&&error==null&&
                slow.Length==1&&ReferenceEquals(slow[0],f.RoadItem)&&f.RoadItem.targetLaneIndex==2,
                "A proven119m future adjacent exit returns the actual native road with a required slow-speed owner: "+error+" backwards="+backward+" factor="+f.RoadItem.GetFactorFor(truck));
            var limits=new ExitSpeedLimits();limits.Remember(slow[0]);
            var trace=PolicyTrace(f);
            var free=new TollDecision("Frei",null,float.PositiveInfinity,null);
            var first=limits.Limit(plan,trace,truck,20/3.6,free);
            assert(first.TargetSpeed is >=TollEntry.ShortChangeSpeed and <20,"Short-exit cap anticipates its own future road entry");
            assert(ExitHandoff.Align(plan,0,truck,20/3.6,out var repeated,out _)&&repeated.Length==1&&limits.Active,
                "An existing short target without caller ownership must return its freshly reproven speed owner");
            assert(ExitHandoff.Align(plan,0,truck,20/3.6,out var owned,out _,limits.Owns)&&owned.Length==0,
                "A matching active speed owner retains the existing short target without creating another owner");
            var refreshed=RouteSnapshot.Capture(plan);refreshed.PlannedPath.RemoveAt(0);
            var road=refreshed.PlannedPath[0];
            // Simulate host metadata completion before actual physical completion.
            road.laneIndex=2;road.targetLaneIndex=0;
            var middle=Vector3.Lerp(f.Entry,f.Road.InterpolateLane(backward?0:1,Side.Right,1).Position,.5f);
            var ownedTrace=PolicyTrace(f,road);
            var cap=limits.Limit(refreshed,ownedTrace,middle,8,free);
            assert(limits.Active&&cap.TargetSpeed==TollEntry.ShortChangeSpeed&&cap.MaxAcceleration<0,
                "Toll pruning, wrapper refresh and early target=0 cannot release an unfinished physical exit speed cap");
            var connected=RouteSnapshot.Capture(plan);connected.PlannedPath.RemoveRange(0,2);
            var finished=f.Next.prefabPath!.Interpolate(.5f,0)!.Value.Position;
            var tail=new RouteTrace(new[]{f.Next.prefabPath!.Interpolate(0,0)!.Value.Position,
                f.Next.prefabPath.Interpolate(1,0)!.Value.Position},null,new[]{RouteMatcher.ItemIdentity(f.Next)});
            var neverEntered=new ExitSpeedLimits();neverEntered.Remember(f.RoadItem);
            neverEntered.Limit(connected,tail,finished,5,free);
            assert(neverEntered.Active,"A nearby pruned connector cannot retire a future road that was never actually entered");
            var wrongConnector=RouteSnapshot.Capture(connected);wrongConnector.PlannedPath[0].startNode=new(){Uid=++uid};
            limits.Limit(wrongConnector,tail,finished,5,free);
            assert(limits.Active,"Geometrically nearby continuation with the wrong canonical start node cannot release the cap");
            var done=limits.Limit(connected,tail,finished,5,free);
            assert(!limits.Active&&done==free,"Actual directed continuation beyond the road end releases the exit speed cap");
            var loopOwner=new ExitSpeedLimits();loopOwner.Remember(f.RoadItem);
            var roadExit=f.Road.InterpolateLane(backward?0:1,Side.Right,1).Position;
            var loopTrace=new RouteTrace(new[]{roadExit-f.Heading*10,roadExit+f.Heading*20,
                f.Entry+Vector3.UnitX*100,f.Entry,roadExit,f.Next.prefabPath.Interpolate(1,0)!.Value.Position},null,
                new[]{"earlier-unrelated-road","earlier-loop","return-connector",RouteMatcher.ItemIdentity(f.RoadItem),RouteMatcher.ItemIdentity(f.Next)});
            loopOwner.Limit(plan,loopTrace,roadExit+f.Heading*10,5,free);
            assert(loopOwner.Active,"An earlier loop passing the same world exit cannot retire the exact future road identity");
            var tooShort=Make(backward,44);var denied=tooShort.Plan();
            assert(!ExitHandoff.Align(denied,0,tooShort.Entry-tooShort.Heading*10,0,out var none,out _)&&none.Length==0&&tooShort.RoadItem.targetLaneIndex==0,
                "Under45m never becomes a short exit through a lower speed");
            var fast=Make(backward,118);var fastTruck=fast.Road.InterpolateLane(backward?.99f:.01f,Side.Right,0).Position;
            assert(!ExitHandoff.Align(fast.Plan(),0,fastTruck,40/3.6,out _,out _),"Already on the short road above20kmh cannot start a late change");
            var fastFuture=Make(backward,118);
            assert(!ExitHandoff.Align(fastFuture.Plan(),0,fastFuture.Entry-fastFuture.Heading*10,80/3.6,out _,out _),
                "Future short road without braking distance cannot be granted an optimistic speed owner");
            var lateTarget=Make(backward,118.76f);var latePlan=lateTarget.Plan();
            lateTarget.RoadItem.targetLaneIndex=2;
            var lateTruck=lateTarget.Road.InterpolateLane(backward?.2f:.8f,Side.Right,0).Position;
            assert(!ExitHandoff.Align(latePlan,0,lateTruck,5,out var unownedLate,out _)&&unownedLate.Length==0,
                "A restart with under45m remaining cannot borrow an old short target as fresh lane-change permission");
            var liveOwner=new ExitSpeedLimits();liveOwner.Remember(lateTarget.RoadItem);
            assert(ExitHandoff.Align(latePlan,0,lateTruck,5,out var alreadyOwned,out _,liveOwner.Owns)&&alreadyOwned.Length==0,
                "A matching currently owned native short change may finish without inventing a new45m start");
            liveOwner.Reset();
            assert(!ExitHandoff.Align(latePlan,0,lateTruck,5,out _,out _,liveOwner.Owns),
                "Reset removes the old speed owner and restores strict restart reachability");
            var reset=new ExitSpeedLimits();reset.Remember(f.RoadItem);reset.Reset();assert(!reset.Active,"Explicit reset clears every short-exit owner");
        }
    }

    // Not registered in ordinary Tests: PathLib's real GenerateLaneChange
    // reads GameTelemetry.Current even when its start factor is initialized.
    // The dedicated HostCompatibility runner proves both getters are replaced
    // by private DTO fields before invoking this method.
    public static void SlowExitNative(Action<bool,string> assert,Action<Vector3,double> place,
        ETS2LA.State.ApplicationState state)
    {
        if(typeof(ETS2LA.Game.Telemetry.GameTelemetry).GetField("ReplayData",BindingFlags.Public|BindingFlags.Static)==null)
            throw new InvalidOperationException("Native short exit requires the audited hermetic telemetry host");
        foreach(bool backward in new[]{false,true}) {
            var f=Make(backward,118.76f);float first=backward?1:0,last=1-first;
            var entry=f.Road.InterpolateLane(first,Side.Right,1).Position;
            var shift=entry-f.Entry;
            f.Toll.prefabPath!.Curves[0].StartPosition+=shift;f.Toll.prefabPath.Curves[0].EndPosition+=shift;
            f.RoadItem.laneIndex=2;
            var target=f.Road.InterpolateLane(last,Side.Right,0).Position;
            var oldStart=f.Next.prefabPath!.Curves[0].StartPosition;
            f.Next.prefabPath.Curves[0].StartPosition=target;
            f.Next.prefabPath.Curves[0].EndPosition+=target-oldStart;
            var plan=f.Plan();var before=entry-f.Heading*10;place(before,20/3.6);
            assert(ExitHandoff.Align(plan,0,before,20/3.6,out var slow,out var error)&&slow.Length==1&&
                f.RoadItem.laneIndex==2&&f.RoadItem.targetLaneIndex==1,
                "Actual native119m road receives lane2-to1 as a slow owned future change: "+error);
            place(entry,20/3.6);
            var points=new List<Vector3>();bool finite=true;double largestStep=0;
            for(int i=0;i<=500;i++) {
                float factor=first+(last-first)*(i/500f);
                var p=f.RoadItem.Interpolate(factor,0,false)?.Position;
                if(p is not Vector3 v||!float.IsFinite(v.LengthSquared())){finite=false;break;}
                if(points.Count>0)largestStep=Math.Max(largestStep,Vector3.Distance(points[^1],v));
                points.Add(v);
            }
            assert(finite&&points.Count==501&&Vector3.Distance(points[0],entry)<.01&&Vector3.Distance(points[^1],target)<.01,
                "Real native interpolation preserves the initial lane,501finite intermediate poses and exact target endpoint: "+backward);
            assert(largestStep<.5&&Vector3.Distance(points[250],f.Road.InterpolateLane(.5f,Side.Right,1).Position)>1,
                "The native short change actually crosses lanes continuously, rather than retaining the source lane or teleporting");
            Console.WriteLine($"NATIVE_SHORT_EXIT authored=true backward={backward} length={f.RoadItem.GetLength():R} samples={points.Count} maxStep={largestStep:R} from=2 to=1");
            foreach(int i in new[]{0,50,125,250,375,450,500})
                Console.WriteLine($"NATIVE_SHORT_POSE progress={i/500d:R} x={points[i].X:R} y={points[i].Y:R} z={points[i].Z:R}");
            assert(f.RoadItem.laneIndex==2&&f.RoadItem.targetLaneIndex==1,
                "Read-only native preview never completes the live target metadata");
            var trace=RouteMatcher.SampleForTracking(plan,before,20/3.6);
            assert(!trace.EndsAtGap&&trace.IdentityKnown,
                "The actual native lane-change route reaches its following prefab without a fabricated seam");
            state.DrivingMode=ETS2LA.State.DrivingMode.FullSelfDriving;
            var plugin=new TollPlugin(()=>state,(_,_)=>{},_=>{},readAcc:()=>null,readPlanner:()=>null,readLaneAssist:()=>null,
                readPayments:_=>new Dictionary<int,TollPayment>{{12,TollPayment.Automatic}},readClearance:_=>true,
                writeDiagnostic:(_,_)=>{});
            var type=typeof(TollPlugin);const BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic;
            type.GetMethod("StopDiagnostics",hidden)!.Invoke(plugin,null);
            var router=(TollRouter)type.GetField("router",hidden)!.GetValue(plugin)!;
            var pruned=RouteSnapshot.Capture(plan);pruned.PlannedPath.RemoveAt(0);
            var early=f.Road.InterpolateLane(backward?.98f:.02f,Side.Right,1).Position;place(early,20/3.6);
            assert(router.Apply(pruned,early,out error,30/3.6)&&!router.HasShortChange&&
                pruned.PlannedPath[0].laneIndex==2&&pruned.PlannedPath[0].targetLaneIndex==1,
                "A normal native target unknown to this router is left unchanged, without taking over its speed at30kmh");
            object?[] admission=[plan,0,before,20/3.6,null];place(before,20/3.6);
            assert((bool)typeof(TollRouter).GetMethod("AlignExit",hidden)!.Invoke(router,admission)!&&router.HasShortChange,
                "Actual router exit admission records the proven short-exit identity and its active speed owner");
            place(early,20/3.6);
            assert(router.Apply(pruned,early,out error,20/3.6)&&router.HasShortChange,
                "Actual road-first Apply adopts the existing native short target and owns its20kmh cap: "+error);
            var capped=router.LimitShortChange(pruned,RouteMatcher.SampleForTracking(pruned,early,20/3.6),early,8,
                new TollDecision("Frei",null,float.PositiveInfinity,null));
            assert(capped.TargetSpeed==TollEntry.ShortChangeSpeed&&capped.MaxAcceleration<0,
                "The real router enforces its short-exit speed limit without the previous toll prefab");
            type.GetMethod("OnCancel",hidden)!.Invoke(plugin,[null,new ETS2LA.Controls.ControlChangeEventArgs(false,null!)]);
            assert(!router.HasShortChange&&!state.EnableAssists,"Actual F9/ResetDriving removes the plugin's existing exit speed owner");
            var late=f.Road.InterpolateLane(backward?.15f:.85f,Side.Right,1).Position;place(late,5);
            assert(!router.Apply(pruned,late,out _,5)&&!router.HasShortChange,
                "F9 then late road-first restart cannot reuse the native target with under45m remaining");
            place(early,20/3.6);
            assert(router.Apply(pruned,early,out error,20/3.6)&&router.HasShortChange,
                "F9 then sufficiently early slow road-first restart reacquires current cap ownership: "+error);
            type.GetMethod("OnCancel",hidden)!.Invoke(plugin,[null,new ETS2LA.Controls.ControlChangeEventArgs(false,null!)]);
            assert(!router.HasShortChange,"The reacquired native exit cap is cleared by the next real cancel too");
        }
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
