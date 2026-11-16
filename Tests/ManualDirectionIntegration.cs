using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using ETS2LA.Game.Data;
using MautAssistent;
using PathLib;
using TruckLib.Models.Ppd;

public static class ManualDirectionIntegration
{
    public static void Run(Action<bool,string> assert)
    {
        var rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI);
        NavCurve Curve(float a,float b,int id=-1)=>new(){StartPosition=new(0,0,a),EndPosition=new(0,0,b),Length=b-a,
            StartRotation=rotation,EndRotation=rotation,SemaphoreId=id,AllowedVehicles=AllowedVehicles.AllVehicles};
        var curves=new List<NavCurve>{Curve(0,20),Curve(20,40,1),Curve(40,80)};
        curves[0].NextLines.Add(1);curves[1].NextLines.Add(2);
        var descriptor=new PrefabDescriptor{NavCurves=curves};descriptor.Semaphores.Add(new(){SemaphoreId=1,Position=new(2,0,39.6f)});
        var parsed=(ParsedPrefab)RuntimeHelpers.GetUninitializedObject(typeof(ParsedPrefab));parsed.Prefab=new(){Uid=810};parsed.Descriptor=descriptor;
        var path=new PrefabPath(parsed,new(){Uid=811},new(){Uid=812},curves.ToList(),Direction.Forward,Matrix4x4.Identity,Vector3.Zero);
        path.CurveDirection=Direction.Backward;
        var item=new PlannedItem{item=parsed,prefabPath=path,startNode=path.StartNode,endNode=path.EndNode,direction=Direction.Forward,laneIndex=0};
        var frame=new PlannedPathData{HasError=false,PlannedPath=[item]};
        var router=new TollRouter(new PreferenceStore(Path.Combine(Path.GetTempPath(),"unused-manual-direction-"+Guid.NewGuid()+".json")),
            _=>new Dictionary<int,TollPayment>{{1,TollPayment.Manual}},_=>true,readManualStop:(_,_)=>new ManualStopZone(11,19));
        router.Discover(frame,new(0,0,5),[]);
        assert(router.SelectedGate==1&&router.ManualStopOffset(8)==15,
            "A Next-conform world-forward manual lane keeps its proven trigger despite unrelated Backward host metadata");
        // Mutate the selected original rather than asking discovery to select a
        // different plan: the optional trigger must revalidate current geometry.
        curves[0].NextLines.Clear();
        assert(router.ManualStopOffset(8)==null,"A broken directed native chain cannot lend a manual payment interval");
        curves[0].NextLines.Add(1);curves[1].StartPosition=new(0,0,50);
        assert(router.ManualStopOffset(8)==null,"A discontinuity between directed curve endpoints cannot lend a payment interval");
        curves[1].StartPosition=new(0,0,20);
        var plans=(Dictionary<int,PrefabPath>)typeof(TollRouter).GetField("plans",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(router)!;
        var selected=PrefabRefinement.OriginalFor(plans[1]);selected.Curves.Reverse();
        assert(router.ManualStopOffset(8)==null,"A physically reversed chain is rejected even if its metadata flag alone looks acceptable");
    }
}
