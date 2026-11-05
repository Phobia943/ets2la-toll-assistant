using System.Numerics;
using System.Runtime.CompilerServices;
using ETS2LA.Game.Data;
using MautAssistent;
using PathLib;
using TruckLib.Models.Ppd;
using TruckLib.ScsMap;

public static class SignalIdentityIntegration
{
    public static void Run(Action<bool,string> assert)
    {
        TollPayment Hookup(string name,bool special=false) {
            var text=System.Text.Encoding.ASCII.GetBytes(name+'\0');var pmg=new byte[97+text.Length];pmg[0]=21;
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(pmg.AsSpan(88),96);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(pmg.AsSpan(92),text.Length);
            text.CopyTo(pmg,96);return TollModelEvidence.DecodeHookup(pmg,0,special);
        }
        assert(Hookup("tollgate.autopass.ovs_disable")==TollPayment.Automatic&&Hookup("tollgate.autopass.ovs_disable.long")==TollPayment.Automatic,
            "Normal-job AutoPASS markers must be recognized");
        assert(Hookup("tollgate.manual.ovs_disable")==TollPayment.Manual&&Hookup("tollgate.autopass.oversize")==TollPayment.Unknown,
            "A normal job cannot use an oversize-only payment marker");
        assert(Hookup("tollgate.autopass.oversize",true)==TollPayment.Automatic&&Hookup("tollgate.autopass.ovs_disable",true)==TollPayment.Unknown,
            "Special transport observes its own enabled payment lanes");
        NavCurve Line(float x,float from,float to,int id)=>new() {StartPosition=new(x,0,from),EndPosition=new(x,0,to),
            StartRotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI),EndRotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI),
            Length=to-from,SemaphoreId=id,AllowedVehicles=AllowedVehicles.AllVehicles};
        var lane=Line(0,0,30,7);lane.NextLines.Add(1);
        var tail=Line(0,30,100,-1);tail.PreviousLines.Add(0);
        var alternate=Line(.35f,0,30,7);alternate.NextLines.Add(1);
        var descriptor=new PrefabDescriptor {NavCurves=[lane,tail,alternate]};
        descriptor.Semaphores.Add(new() {SemaphoreId=7,Position=new(10,0,29)}); // native first-by-ID points at another object
        descriptor.Semaphores.Add(new() {SemaphoreId=7,Position=new(2.5f,0,29)});
        descriptor.Semaphores.Add(new() {SemaphoreId=7,Position=new(2.5f,0,29.35f)});
        var prefab=(ParsedPrefab)RuntimeHelpers.GetUninitializedObject(typeof(ParsedPrefab));prefab.Descriptor=descriptor;prefab.Prefab=new() {Uid=750};
        var path=new PrefabPath(prefab,new() {Uid=751},new() {Uid=752},[lane,tail],Direction.Forward,Matrix4x4.Identity,Vector3.Zero);
        var frame=new PlannedPathData {HasError=false,PlannedPath=[new() {item=prefab,prefabPath=path,startNode=path.StartNode,endNode=path.EndNode,direction=Direction.Forward,laneIndex=0}]};
        ETS2LA.Game.SDK.Semaphore S(float x,float z,int state)=>new() {id=7,position=new(x,0,z),type=ETS2LA.Game.SDK.SemaphoreType.GATE,state=state};
        var a=S(2.5f,29,1);var b=S(2.5f,29.35f,1);var neighbor=S(7.5f,29,3);
        var truck=new Vector3(0,0,5);
        var gate=RouteMatcher.Select(frame,truck,[a,b,neighbor],null);
        assert(gate!=null&&gate.State==BarrierState.Closed,"Duplicate SDK objects at the mapped booth must form one closed target");
        var same=RouteMatcher.Select(frame,truck,[b,neighbor],gate!.Key);
        assert(same?.Key==gate.Key,"One replica disappearing must not switch the physical booth identity");
        b.state=3;
        var conflict=RouteMatcher.Select(frame,truck,[a,b,neighbor],gate.Key);
        assert(conflict?.State==BarrierState.Unknown,"Contradictory states cannot grant clearance");
        assert(path.GetSemaphores().Single().Semaphore.Position.X==10&&descriptor.Semaphores.Count==3,
            "Canonical measurement must not modify the native map or steering route");
        var payment=TollModelEvidence.Match(descriptor,[(new(0,0,5),TollPayment.Automatic)]);
        assert(payment.GetValueOrDefault(7)==TollPayment.Automatic,"Same-booth curve variants may share automatic payment evidence");
        var distant=Line(5,0,30,7);descriptor.NavCurves.Add(distant);
        assert(!TollModelEvidence.Match(descriptor,[(new(0,0,5),TollPayment.Automatic)]).ContainsKey(7),
            "A reused ID on a different physical lane remains ambiguous");
        assert(TollModelEvidence.Match(descriptor,[(new(0,0,5),TollPayment.Automatic),(new(5,0,5),TollPayment.Automatic)]).GetValueOrDefault(7)==TollPayment.Automatic,
            "A shared number may be classified only when every physical booth has the same explicit payment evidence");
        assert(!TollModelEvidence.Match(descriptor,[(new(0,0,5),TollPayment.Automatic),(new(5,0,5),TollPayment.Manual)]).ContainsKey(7),
            "Conflicting payment types on a reused number remain unknown");
        descriptor.NavCurves.Remove(distant);
        var settings=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"unused-signal-identity-"+Guid.NewGuid()+".json");
        var router=new TollRouter(new PreferenceStore(settings),_=>payment);
        router.Discover(frame,truck,[a,b,neighbor]);
        assert(router.SelectedGate==7&&router.Choices.Count(c=>c.GateId==7)==1,
            "Known automatic payment selects one physical lane despite duplicate curve/signal objects");
        var live=RouteMatcher.FindLiveGate(frame,[a],gate.Key)!;
        var memory=new PassageMemory();memory.Capture(gate.Key,live.GetWorldCoordinates(),RouteMatcher.Sample(frame));
        var pruned=RouteMatcher.FindLiveGate(null,[b],gate.Key,memory.PositionFor(gate.Key));
        assert(pruned!=null&&RouteMatcher.Key(pruned)==gate.Key,"Gate identity must survive route pruning and a missing replica");
        assert(memory.AfterCrossing(gate.Key,new(0,0,35),pruned!.GetWorldCoordinates(),BarrierState.Closed,true,true)!=null,
            "A closing passed booth must still allow the recorded exit handoff");
    }
}
