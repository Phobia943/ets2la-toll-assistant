using System.Numerics;
using System.Runtime.CompilerServices;
using ETS2LA.Game.Data;
using MautAssistent;
using PathLib;
using TruckLib.Models.Ppd;
using TruckLib.ScsMap;

internal static class LaneSearchIntegration
{
    internal static void Run(Action<bool,string> assert)
    {
        var q=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI);
        NavCurve C(Vector3 a,Vector3 b,int gate=-1)=>new(){StartPosition=a,EndPosition=b,StartRotation=q,EndRotation=q,
            Length=Vector3.Distance(a,b),SemaphoreId=gate,AllowedVehicles=AllowedVehicles.AllVehicles};
        var curves=new List<NavCurve>{C(new(0,0,-400),new(0,0,20)),C(new(0,0,20),new(-6,0,60)),
            C(new(-6,0,60),new(-6,0,100),12),C(new(-6,0,100),new(0,0,180)),
            C(new(0,0,20),new(-9,0,40)),C(new(-9,0,40),new(-6,0,60)),
            C(new(0,0,20),new(0,0,60)),C(new(0,0,60),new(0,0,100),4),C(new(0,0,100),new(-6,0,100))};
        foreach(var edge in new[]{(0,1),(1,2),(2,3),(0,4),(4,5),(5,2),(0,6),(6,7),(7,8),(8,3)})curves[edge.Item1].NextLines.Add(edge.Item2);
        var descriptor=new PrefabDescriptor{NavCurves=curves};
        descriptor.Semaphores.Add(new(){SemaphoreId=12,Position=new(-3.5f,0,99.6f)});
        descriptor.Semaphores.Add(new(){SemaphoreId=4,Position=new(2.5f,0,99.6f)});
        var parsed=(ParsedPrefab)RuntimeHelpers.GetUninitializedObject(typeof(ParsedPrefab));
        parsed.Descriptor=descriptor;parsed.Prefab=new(){Uid=3190,Model="fixture",Variant="default"};
        PrefabPath P(params int[] ids)=>new(parsed,new(){Uid=3191},new(){Uid=3192},ids.Select(i=>curves[i]).ToList(),Direction.Forward,Matrix4x4.Identity,Vector3.Zero);
        var source=P(0,6,7,8,3);var shortAuto=P(0,1,2,3);var longAuto=P(0,4,5,2,3);
        var item=new PlannedItem{item=parsed,prefabPath=source,otherPrefabPaths=[shortAuto,longAuto],startNode=source.StartNode,endNode=source.EndNode,direction=Direction.Forward,laneIndex=0};
        var data=new PlannedPathData{HasError=false,PlannedPath=[item]};var own=new Vector3(0,0,-300);
        var blocked=new bool[curves.Count];blocked[1]=true;
        var payments=new Dictionary<int,TollPayment>{{4,TollPayment.Manual},{12,TollPayment.Automatic}};
        bool Clear(PrefabPath p)=>PrefabRefinement.OriginalFor(p).Curves.All(c=>!blocked[curves.IndexOf(c)]);
        var prefs=new PreferenceStore(Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json"));
        var old=new TollRouter(prefs,_=>payments,Clear);old.Discover(data,own,[],10);
        assert(old.SelectedGate==4,"Shortest-then-reject loses the longer clear auto route even with both native alternatives and identical endpoints");
        using var release=new ManualResetEventSlim();
        var evidence=new TollModelEvidence(()=>newFiles,null,_=>{release.Wait(TimeSpan.FromSeconds(3));return new(payments,blocked.ToArray());},()=> (4.4f,3.4f));
        var pending=evidence.FindClearRoute(source,12);
        assert(pending.Path==null&&pending.Reason=="ModelPending","Missing model proof must not authorize a graph search");
        release.Set();
        assert(SpinWait.SpinUntil(()=>evidence.Diagnose(parsed).Completed,TimeSpan.FromSeconds(3)),"Static fixture inspection completed");
        var found=evidence.FindClearRoute(source,12);
        assert(found.Path!=null&&Clear(found.Path)&&PrefabRefinement.OriginalFor(found.Path).Curves.Contains(curves[5]),"The actual evidence search finds the longer proven free branch to the same automatic booth");
        assert(found.Evidence!=null&&found.BlockedCurves.SequenceEqual(new[]{1}),"Decision records the exact completed proof and curve IDs");
        var router=new TollRouter(prefs,evidence.Read,evidence.IsClear,findClearRoute:evidence.FindClearRoute);
        router.Discover(data,own,[],10);
        assert(router.SelectedGate==12&&router.LaneAttempts.Any(a=>a.Gate==12&&a.Reason=="Reachable"),"Actual discovery prefers the reachable automatic route with recorded evidence");
        assert(router.Apply(data,own,out var error,10)&&error==null&&TollSignals.ForPath(item.prefabPath!).Single().Semaphore.SemaphoreId==12,"Actual Apply installs the clear automatic route rather than the manual fallback");
        assert(Clear(item.prefabPath!)&&source.Curves.SequenceEqual(new[]{0,6,7,8,3}.Select(i=>curves[i])),"Blocked branch is never used and original path remains unchanged");
        blocked[5]=true;
        assert(TollRouter.ViaGate(source,12,blocked)==null,"All blocked automatic routes remain rejected");
        blocked[5]=false;curves[5].AllowedVehicles=AllowedVehicles.SmallVehicles;
        assert(TollRouter.ViaGate(source,12,blocked)==null,"Clearance search cannot borrow a car-only alternative");
        var noProof=TollRouter.ViaGate(source,12,null,true);
        assert(noProof==null||!PrefabRefinement.OriginalFor(noProof).Curves.Contains(curves[5]),"An AI-preference flag without a physical mask cannot admit the small-vehicle branch");
        evidence.Prefetch(parsed);
        assert(SpinWait.SpinUntil(()=>evidence.Diagnose(parsed).Completed,TimeSpan.FromSeconds(3)),"Changed AI preference receives a new physical proof");
        var preferred=evidence.FindClearRoute(source,12);
        assert(preferred.Path!=null&&Clear(preferred.Path),"A physically proven automatic lane can use an AI-small-vehicle preference, as documented by SCS");
        var preferredItem=new PlannedItem{item=parsed,prefabPath=source,otherPrefabPaths=[shortAuto,longAuto],startNode=source.StartNode,endNode=source.EndNode,direction=Direction.Forward,laneIndex=0};
        var preferredPlan=new PlannedPathData{HasError=false,PlannedPath=[preferredItem]};
        var preferredRouter=new TollRouter(prefs,evidence.Read,evidence.IsClear,findClearRoute:evidence.FindClearRoute);
        preferredRouter.Discover(preferredPlan,own,[],10);
        assert(preferredRouter.SelectedGate==12&&preferredRouter.Apply(preferredPlan,own,out _,10)&&Clear(preferredItem.prefabPath!),"Production discovery and Apply use the proven automatic AI-preference path");
        var blockedSmall=blocked.ToArray();blockedSmall[5]=true;
        var blockedEvidence=new TollModelEvidence(()=>newFiles,null,_=>new(new Dictionary<int,TollPayment>(payments),blockedSmall),()=> (4.4f,3.4f));
        blockedEvidence.Prefetch(parsed);
        assert(SpinWait.SpinUntil(()=>blockedEvidence.Diagnose(parsed).Completed,TimeSpan.FromSeconds(3)),"Blocked automatic small-vehicle proof completed");
        assert(blockedEvidence.FindClearRoute(source,12).Path==null,"Automatic payment and small-vehicle preference cannot bypass a physical blocker on that same branch");
        var unknownPayments=new Dictionary<int,TollPayment>(payments){{99,TollPayment.Unknown}};unknownPayments[12]=TollPayment.Unknown;
        var unknownEvidence=new TollModelEvidence(()=>newFiles,null,_=>new(unknownPayments,blocked.ToArray()),()=> (4.4f,3.4f));
        unknownEvidence.Prefetch(parsed);
        assert(SpinWait.SpinUntil(()=>unknownEvidence.Diagnose(parsed).Completed,TimeSpan.FromSeconds(3)),"Unknown-payment proof completed");
        assert(unknownEvidence.FindClearRoute(source,12).Path==null,"Unknown payment cannot authorize the small-vehicle preference exception");
        curves[6].AllowedVehicles=AllowedVehicles.SmallVehicles;evidence.Prefetch(parsed);
        assert(SpinWait.SpinUntil(()=>evidence.Diagnose(parsed).Completed,TimeSpan.FromSeconds(3)),"Changed manual lane proof completed");
        assert(evidence.FindClearRoute(source,4).Path==null,"Manual payment does not receive the narrowly scoped automatic AI-preference exception");
        curves[6].AllowedVehicles=AllowedVehicles.AllVehicles;
        // This SDK field is two bits; an arbitrary enum cast is masked back
        // into a valid enum by the model setter, so it is not an unknown-value test.
        curves[5].AllowedVehicles=AllowedVehicles.PlayerOnly;
        assert(TollRouter.ViaGate(source,12,blocked,true)==null,"PlayerOnly remains excluded from this narrowly scoped AI-preference exception");
        curves[5].AllowedVehicles=AllowedVehicles.SmallVehicles;curves[5].TrafficRule="escort_only";
        assert(TollRouter.ViaGate(source,12,blocked,true)==null,"Escort-only restriction survives the AI-preference interpretation");
        curves[5].TrafficRule="";
        curves[5].AllowedVehicles=AllowedVehicles.AllVehicles;curves[5].SemaphoreId=99;
        assert(TollRouter.ViaGate(source,12,blocked)==null,"Clearance search cannot route through a different booth to reach the requested gate");
        assert(TollRouter.ViaGate(source,12,new bool[1])==null,"A mask from a different descriptor cannot be used");
        assert(evidence.FindClearRoute(source,12).Path==null,"Changed source topology invalidates the cached model certificate");
    }
    static readonly ModelClearanceIntegration.StubFiles newFiles=new();
}
