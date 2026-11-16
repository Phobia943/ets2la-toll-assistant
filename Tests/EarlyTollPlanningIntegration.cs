using System.Numerics;
using System.Runtime.CompilerServices;
using ETS2LA.Game.Data;
using MautAssistent;
using PathLib;
using TruckLib.Models.Ppd;
using TruckLib.ScsMap;

public static class EarlyTollPlanningIntegration
{
    public static void Run(Action<bool,string> assert)
    {
        var rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI);
        NavCurve Curve(Vector3 a,Vector3 b,int gate=-1)=>new(){StartPosition=a,EndPosition=b,StartRotation=rotation,EndRotation=rotation,
            Length=Vector3.Distance(a,b),SemaphoreId=gate,AllowedVehicles=AllowedVehicles.AllVehicles};
        var curves=new List<NavCurve>{Curve(new(0,0,-400),new(0,0,20))};
        foreach(var lane in new[]{(id:4,x:0f),(id:12,x:-6f)}) {
            int i=curves.Count;curves[0].NextLines.Add(i);
            curves.Add(Curve(new(0,0,20),new(lane.x,0,60)));
            curves.Add(Curve(new(lane.x,0,60),new(lane.x,0,80),lane.id));
            curves.Add(Curve(new(lane.x,0,80),new(0,0,120)));
            curves[i].NextLines.Add(i+1);curves[i+1].NextLines.Add(i+2);curves[i+2].NextLines.Add(7);
        }
        curves.Add(Curve(new(0,0,120),new(0,0,180)));
        var descriptor=new PrefabDescriptor{NavCurves=curves};
        descriptor.Semaphores.Add(new(){SemaphoreId=4,Position=new(2.5f,0,79.6f)});
        descriptor.Semaphores.Add(new(){SemaphoreId=12,Position=new(-3.5f,0,79.6f)});
        var parsed=(ParsedPrefab)RuntimeHelpers.GetUninitializedObject(typeof(ParsedPrefab));parsed.Descriptor=descriptor;parsed.Prefab=new(){Uid=7000};
        var source=new PrefabPath(parsed,new(){Uid=7001},new(){Uid=7002},new[]{0,1,2,3,7}.Select(i=>curves[i]).ToList(),Direction.Forward,Matrix4x4.Identity,Vector3.Zero);
        var item=new PlannedItem{item=parsed,prefabPath=source,startNode=source.StartNode,endNode=source.EndNode,direction=Direction.Forward,laneIndex=0};
        var data=new PlannedPathData{HasError=false,PlannedPath=[item]};var truck=new Vector3(0,0,-300);
        var payment=new Dictionary<int,TollPayment>{{4,TollPayment.Manual},{12,TollPayment.Automatic}};
        string file=Path.Combine(Path.GetTempPath(),"early-toll-"+Guid.NewGuid()+".json");
        var prefs=new PreferenceStore(file);
        prefs.Save(prefs.Options,new(){{"7000:7001:7002",4}},new(){{PaymentPolicy.Key("7000:7001:7002",4),TollPayment.Automatic}});
        try {
            var pending=new TollRouter(prefs,_=>new Dictionary<int,TollPayment>(),_=>false);
            pending.Discover(data,truck,[],100/3.6);
            assert(pending.SelectedGate==null&&pending.CurrentKey==null,"A remembered automatic lane cannot replace pending model/payment/static proof");
            bool clearance=true;
            var router=new TollRouter(prefs,_=>payment,_=>clearance,readManualStop:(_,_)=>new ManualStopZone(11,19));
            router.Discover(data,truck,[],100/3.6);
            assert(router.SelectedGate==12&&router.SelectedBarrier is {GateId:12}&&router.PaymentFor(4)==TollPayment.Manual,
                "Typed model+static evidence plans the automatic reachable lane before ANY SDK gate; stale preferences cannot reclassify a manual lane");
            assert(router.Apply(data,truck,out var error,100/3.6)&&error==null&&router.SelectedBarrier?.RouteIdentity!=null,
                "The actual lane topology must be applied before the selected barrier gains a directed trace identity");
            var plan=router.SelectedBarrier!;var trace=RouteMatcher.Sample(data);var options=new TollOptions();
            var applied=item.prefabPath;int laneBefore=item.laneIndex,targetBefore=item.targetLaneIndex;
            clearance=false;
            assert(router.Apply(data,truck,out error,100/3.6)&&error==null&&router.ClearanceHoldRequested&&ReferenceEquals(item.prefabPath,applied)&&
                item.laneIndex==laneBefore&&item.targetLaneIndex==targetBefore&&router.SelectedBarrier==plan&&!router.StopBoundary.HasValue,
                "A pending new envelope certificate stops movement without undoing the already applied validated lane or inventing a branch fault");
            assert(!router.Apply(new PlannedPathData{HasError=true,PlannedPath=[]},truck,out _)&&router.ClearanceHoldRequested,
                "An unrelated early exit cannot clear a latched proof hold without a current positive certificate");
            clearance=true;
            assert(router.Apply(data,truck,out error,100/3.6)&&error==null&&!router.ClearanceHoldRequested&&ReferenceEquals(item.prefabPath,applied),
                "Current geometry proof returning clears its hold automatically while preserving route identity");
            router.CancelPlannedStop();
            assert(router.SelectedBarrier==plan,"Cancelling the uncertain-branch stop cannot cancel the selected barrier's separate binding protection");
            var unbound=BarrierBinding.Check(plan,trace,truck,100/3.6,[],true,options);
            assert(unbound.LanePlanned&&unbound.MapCorridorKnown&&!unbound.LiveBarrierBound&&unbound.LiveGate==null&&float.IsFinite(unbound.Decision.MaxAcceleration),
                "Zero SDK gates within the speed-dependent horizon require a normal finite map-anchor approach constraint");
            ETS2LA.Game.SDK.Semaphore Sample(int id,Vector3 where,int state,ETS2LA.Game.SDK.SemaphoreType type=ETS2LA.Game.SDK.SemaphoreType.GATE)=>new(){id=id,position=where,state=state,type=type};
            foreach(var samples in new[]{new[]{Sample(999,plan.Anchor,3)},new[]{Sample(plan.GateId,plan.Anchor+new Vector3(20,0,0),3)}}) {
                var wrong=BarrierBinding.Check(plan,trace,truck,100/3.6,samples,true,options);
                assert(!wrong.LiveBarrierBound&&wrong.LiveGate==null&&float.IsFinite(wrong.Decision.MaxAcceleration),"Wrong gate identity or world location cannot clear the auxiliary stop");
            }
            var closed=Sample(plan.GateId,plan.Anchor,1);
            var old=BarrierBinding.Check(plan,trace,truck,20,[closed],false,options);
            assert(!old.LiveBarrierBound,"Reading an old SDK sample again cannot bind the selected physical booth");
            var wrongType=BarrierBinding.Check(plan,trace,truck,20,[Sample(plan.GateId,plan.Anchor,3,(ETS2LA.Game.SDK.SemaphoreType)int.MaxValue)],true,options);
            assert(!wrongType.LiveBarrierBound,"An unrelated typed semaphore cannot become a toll-gate observation by sharing its ID and location");
            var bound=BarrierBinding.Check(plan,trace,truck,20,[closed],true,options);
            assert(bound.LiveBarrierBound&&bound.LiveGate is {State:BarrierState.Closed}&&float.IsNaN(bound.Decision.MaxAcceleration),
                "Fresh real closed gate binds its actual identity/state and removes only the extra unbound constraint");
            closed.state=9;var unknown=BarrierBinding.Check(plan,trace,truck,20,[closed],true,options);
            assert(unknown.LiveGate?.State==BarrierState.Unknown,"Invalid typed SDK state is preserved as Unknown, never fabricated Open");
            closed.state=3;var opposite=BarrierBinding.Check(plan with {Forward=-plan.Forward},trace,truck,20,[closed],true,options);
            assert(!opposite.LiveBarrierBound,"A reused ID and position cannot borrow the opposite directed booth identity");
            var otherItem=BarrierBinding.Check(plan with {RouteIdentity="other-prefab-occurrence"},trace,truck,20,[closed],true,options);
            assert(!otherItem.MapCorridorKnown&&!otherItem.LiveBarrierBound,"A nearby parallel or repeated world position cannot borrow another trace item");
            var loop=new RouteTrace([new(-6.1f,0,-300),new(-6.1f,0,-100),new(-6.1f,0,100),new(100,0,100),new(100,0,-100),new(-6,0,-100),new(-6,0,0)],
                null,["plaza","plaza","later","later","later","later"]);
            var crossing=BarrierBinding.Check(plan with {RouteIdentity="plaza"},loop,new(-6,0,-100),20,[closed],true,options);
            assert(!crossing.Passed&&!crossing.MapCorridorKnown&&!crossing.LiveBarrierBound&&float.IsFinite(crossing.Decision.MaxAcceleration),
                "A globally closer later crossing cannot fabricate completed passage or release the selected barrier");
            var lost=BarrierBinding.Check(plan,trace,truck,100/3.6,[],true,options);
            assert(!lost.LiveBarrierBound&&float.IsFinite(lost.Decision.MaxAcceleration),"Prior real binding is not cached as a future permission after live data disappears");
            var after=BarrierBinding.Check(plan,trace,new(0,0,150),0,[],true,options);
            assert(after.Passed&&!after.Holding&&float.IsNaN(after.Decision.MaxAcceleration),"A proven directed anchor behind the complete passage must not become a permanent future stop");
            descriptor.Semaphores.Add(new(){SemaphoreId=12,Position=new(-9.5f,0,79.6f)});
            assert(router.Apply(data,truck,out _,100/3.6),"The physical route must remain valid when the booth has a second distinct arm");
            plan=router.SelectedBarrier!;
            var oneArm=BarrierBinding.Check(plan,trace,truck,20,[Sample(12,new(-3.5f,0,79.6f),3)],true,options);
            assert(oneArm.LiveBarrierBound&&oneArm.LiveGate?.State==BarrierState.Unknown,"A missing twin arm binds only Unknown and cannot make a wide booth look open");
            var bothArms=BarrierBinding.Check(plan,trace,truck,20,[Sample(12,new(-3.5f,0,79.6f),3),Sample(12,new(-9.5f,0,79.6f),3)],true,options);
            assert(bothArms.LiveGate?.State==BarrierState.Open,"Only two actually observed open arms clear the canonical physical booth");
            var conflict=BarrierBinding.Check(plan,trace,truck,20,[Sample(12,new(-3.5f,0,79.6f),3),Sample(12,new(-3.5f,0,79.6f),1),Sample(12,new(-9.5f,0,79.6f),3)],true,options);
            assert(conflict.LiveGate?.State==BarrierState.Unknown,"Conflicting real observations at one arm never inherit its neighbor's Open state");
            parsed.Prefab.Model="newasset";clearance=false;
            assert(router.Apply(data,truck,out error,100/3.6)&&router.ClearanceHoldRequested&&ReferenceEquals(item.prefabPath,applied),
                "An asset/proof change retains topology only under an explicit movement hold, never old-certificate clearance");
            clearance=true;router.Apply(data,truck,out _,100/3.6);
            var freshObject=new PrefabPath(parsed,source.StartNode,source.EndNode,PrefabRefinement.OriginalFor(item.prefabPath!).Curves.ToList(),
                Direction.Forward,Matrix4x4.Identity,Vector3.Zero);
            item.prefabPath=freshObject;clearance=false;
            bool adopted=router.Apply(data,truck,out _,100/3.6);
            assert(adopted&&router.ClearanceHoldRequested&&ReferenceEquals(item.prefabPath,freshObject)&&router.SelectedGate==12&&!router.StopBoundary.HasValue,
                "An exact new wrapper of the applied directed route retains topology only under a current proof hold, without reintroducing a branch fault");
            item.prefabPath=source;
            assert(router.Apply(data,truck,out _,100/3.6)&&router.ClearanceHoldRequested&&ReferenceEquals(item.prefabPath,applied)&&router.SelectedBarrier!=null,
                "A repeated original source-wrapper key restores the physically reachable already applied lane before reporting it as retained");
            var different=new PrefabPath(parsed,source.StartNode,source.EndNode,source.Curves.ToList(),Direction.Forward,Matrix4x4.Identity,Vector3.Zero);
            item.prefabPath=different;
            bool changed=router.Apply(data,truck,out _,100/3.6);
            assert(ReferenceEquals(item.prefabPath,different)&&(!changed||router.StopBoundary.HasValue),
                "A genuinely different new directed route cannot borrow the applied route's topology or commit while its certificate is false");
            item.prefabPath=source;int checks=0;bool evaluating=false;
            // A callable proof can disappear between topology validation and
            // final commit. Use a fresh router with that exact boundary.
            var volatileProof=new TollRouter(prefs,_=>payment,_=>!evaluating||++checks!=2);
            volatileProof.Discover(data,truck,[]);evaluating=true;
            assert(volatileProof.SelectedGate==12,"Revocation regression starts with the genuinely different automatic route selected");
            volatileProof.Apply(data,truck,out _,100/3.6);
            assert(ReferenceEquals(item.prefabPath,source),"An initial proof revoked before commit cannot leave entry actions or a replacement route applied");
        }finally{if(File.Exists(file))File.Delete(file);}
    }
}
