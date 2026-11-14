using System.Numerics;
using System.Runtime.CompilerServices;
using ETS2LA.Game.Data;
using MautAssistent;
using PathLib;
using TruckLib.Models.Ppd;
using TruckLib.ScsMap;

public static class SignalIdentityIntegration
{
    public static void WidePassage(Action<bool,string> assert)
    {
        NavCurve Line(float x,int gate)=>new() {StartPosition=new(x,0,0),EndPosition=new(x,0,30),Length=30,SemaphoreId=gate,
            StartRotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI),EndRotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI),AllowedVehicles=AllowedVehicles.AllVehicles};
        var lane=Line(0,91);var tail=Line(0,-1);tail.StartPosition=new(0,0,30);tail.EndPosition=new(0,0,100);tail.Length=70;lane.NextLines.Add(1);
        var descriptor=new PrefabDescriptor{NavCurves=[lane,tail]};
        descriptor.Semaphores.Add(new(){SemaphoreId=91,Position=new(-3.7f,0,29)});
        descriptor.Semaphores.Add(new(){SemaphoreId=91,Position=new(3.7f,0,29)});
        var prefab=(ParsedPrefab)RuntimeHelpers.GetUninitializedObject(typeof(ParsedPrefab));prefab.Prefab=new(){Uid=910};prefab.Descriptor=descriptor;
        var path=new PrefabPath(prefab,new(){Uid=911},new(){Uid=912},[lane,tail],Direction.Forward,Matrix4x4.Identity,Vector3.Zero);
        var frame=new PlannedPathData{HasError=false,PlannedPath=[new(){item=prefab,prefabPath=path,startNode=path.StartNode,endNode=path.EndNode,laneIndex=0,direction=Direction.Forward}]};
        ETS2LA.Game.SDK.Semaphore S(float x,int state)=>new(){id=91,position=new(x,0,29),state=state,type=ETS2LA.Game.SDK.SemaphoreType.GATE};
        var left=S(-3.7f,1);var right=S(3.7f,3);var truck=new Vector3(0,0,10);
        var gate=RouteMatcher.Select(frame,truck,[left,right],null);
        assert(gate?.State==BarrierState.Closed,"One open arm cannot clear a wide passage whose other arm is closed");
        var key=gate!.Key;left.state=3;
        assert(RouteMatcher.Select(frame,truck,[left,right],key)?.State==BarrierState.Open,"Both separately observed arms must open before clearance");
        assert(RouteMatcher.Select(frame,truck,[left],key)?.State==BarrierState.Unknown,"A missing wide-passage arm must never look open");
        var live=RouteMatcher.FindLiveGate(frame,[left,right],key)!;right.state=1;
        assert(RouteMatcher.FindLiveGate(null,[left,right],key,live.GetWorldCoordinates())?.state==1,
            "The paired-arm requirement must survive route pruning and exit handoff");
        var unknown=new ETS2LA.Game.SDK.Semaphore{id=929,position=new(111,0,29),state=3,type=ETS2LA.Game.SDK.SemaphoreType.GATE};
        assert(RouteMatcher.FindLiveGate(null,[unknown],RouteMatcher.Key(unknown),unknown.GetWorldCoordinates())?.state==-1,
            "Absent map metadata must not invent a single-arm opening proof");
        var neighbor=Line(6,91);descriptor.NavCurves.Add(neighbor);
        var separate=RouteMatcher.Select(frame,truck,[left,right],null);
        assert(separate?.State==BarrierState.Open,"A reused number on a distinctly mapped neighboring booth must not merge its state");
    }

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
