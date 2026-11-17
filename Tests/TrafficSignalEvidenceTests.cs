using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using ETS2LA.Game.Data;
using MautAssistent;
using PathLib;
using TruckLib.Models.Ppd;
using TruckLib.ScsMap;
using LiveSignal=ETS2LA.Game.SDK.Semaphore;
using LiveType=ETS2LA.Game.SDK.SemaphoreType;
using MapType=TruckLib.Models.Ppd.SemaphoreType;

// Independent counterexamples for the actual signal-policy entry point.
// These construct only map/SDK DTOs and an explicitly injected Tick fixture.
public static class TrafficSignalEvidenceTests
{
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
    sealed record Fixture(Prefab Raw,PlannedPathData Plan,RouteTrace Trace,Vector3 Truck,Vector3 Anchor);
    static void SetSpawn(Prefab raw,bool value)
    {
        var bounds=typeof(MapItem).GetProperty("Kdop",All)!.GetValue(raw)!;
        var field=bounds.GetType().GetField("Flags",All)!;
        var flags=field.GetValue(bounds)!;
        flags.GetType().GetProperty("Item",[typeof(int)])!.SetValue(flags,value,[19]);
        field.SetValue(bounds,flags); // FlagField is a value type; persist the changed boxed copy.
    }
    static Fixture Build(bool enabled,MapType type=MapType.UseProfile,Vector3? shift=null)
    {
        var offset=shift??Vector3.Zero;var rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI);
        NavCurve Line(float a,float b,int signal)=>new(){StartPosition=offset+new Vector3(0,0,a),EndPosition=offset+new Vector3(0,0,b),
            StartRotation=rotation,EndRotation=rotation,Length=b-a,SemaphoreId=signal,AllowedVehicles=AllowedVehicles.AllVehicles};
        var lead=Line(0,20,-1);var approach=Line(20,60,3);var tail=Line(60,200,-1);
        lead.NextLines.Add(1);approach.PreviousLines.Add(0);approach.NextLines.Add(2);tail.PreviousLines.Add(1);
        var anchor=offset+new Vector3(3,0,20);
        var descriptor=new PrefabDescriptor {NavCurves=[lead,approach,tail]};
        descriptor.Semaphores.Add(new(){SemaphoreId=3,Position=anchor,Type=type,Profile="cr_1x1"});
        var raw=new Prefab {Uid=991001,Model="fixture",Variant="default"};SetSpawn(raw,enabled);
        var parsed=(ParsedPrefab)RuntimeHelpers.GetUninitializedObject(typeof(ParsedPrefab));parsed.Prefab=raw;parsed.Descriptor=descriptor;
        var start=new Node {Uid=991002,Position=offset,Rotation=rotation};
        var end=new Node {Uid=991003,Position=offset+new Vector3(0,0,200),Rotation=rotation};
        var path=new PrefabPath(parsed,start,end,[lead,approach,tail],Direction.Forward,Matrix4x4.Identity,Vector3.Zero);
        var plan=new PlannedPathData {HasError=false,PlannedPath=[new(){item=parsed,prefabPath=path,startNode=start,endNode=end,direction=Direction.Forward,laneIndex=0}]};
        return new(raw,plan,RouteMatcher.Sample(plan),offset,anchor);
    }
    static LiveSignal Live(Fixture f,int state,LiveType type=LiveType.TRAFFICLIGHT,Vector3? at=null,int id=3)
        =>new(){id=id,type=type,state=state,position=at??f.Anchor};
    static TrafficSpeedDecision Evaluate(Fixture f,LiveSignal[] signals,TollBarrierPlan? barrier=null,List<object>? diagnostics=null)
        =>TollPlugin.EvaluateTrafficLights(f.Plan,f.Trace,f.Truck,signals,barrier,diagnostics);

    public static void InstanceFlags(Action<bool,string> check)
    {
        check(TrafficSignalEvidence.Prepare().GetAwaiter().GetResult(),"Signal flag tests use the exact pinned TruckLib reader, prepared outside any driving Tick");
        var f=Build(false);
        check(TrafficSignalEvidence.SpawnEnabled(f.Raw)==false,"The actual prefab kDOP bit19 reads explicitly disabled");
        SetSpawn(f.Raw,true);
        check(TrafficSignalEvidence.SpawnEnabled(f.Raw)==true,"Changing only actual kDOP bit19 changes the instance signal evidence");
        check(TrafficSignalEvidence.SpawnEnabled(null)==null&&
            TrafficSignalEvidence.SpawnEnabled((Prefab)RuntimeHelpers.GetUninitializedObject(typeof(Prefab)))==null,
            "Missing prefab or unreadable kDOP stays unknown rather than becoming disabled");
        check(!TrafficSignalEvidence.OmitUnspawned(null,false)&&!TrafficSignalEvidence.OmitUnspawned(true,false)&&
            !TrafficSignalEvidence.OmitUnspawned(false,true),"Only explicit disabled instance evidence with no observed matching actor permits omission");
    }
    public static void Policy(Action<bool,string> check)
    {
        TrafficSignalEvidence.Prepare().GetAwaiter().GetResult();
        var disabled=Build(false);var observations=new List<object>();
        var absent=Evaluate(disabled,[],diagnostics:observations);
        check(absent.Clear&&absent.StopDistance==null&&observations.Count==1,"Disabled instance without a runtime actor cannot invent an upstream traffic-light stop");
        var enabled=Build(true);
        var missing=Evaluate(enabled,[]);
        check(!missing.Clear&&missing.StopDistance>0,"Enabled instance with a missing actual signal remains unknown and stopping");
        check(!Evaluate(disabled,[Live(disabled,2)]).Clear,"An actual own red traffic light overrides the disabled model-spawn flag");
        check(Evaluate(disabled,[Live(disabled,8)]).Clear,"An observed own green traffic light remains usable despite disabled model-spawn metadata");
        check(!Evaluate(disabled,[Live(disabled,3,LiveType.GATE)]).Clear,
            "An observed conflicting GATE actor cannot be laundered into absence or green by disabled metadata");
        check(!Evaluate(enabled,[Live(enabled,2),Live(enabled,8,at:enabled.Anchor+new Vector3(8,0,0))]).Clear,
            "A green actor on a neighbouring same-ID approach cannot release the own red signal");
        check(Evaluate(enabled,[Live(enabled,8),Live(enabled,2,at:enabled.Anchor+new Vector3(8,0,0))]).Clear,
            "A red actor on a neighbouring same-ID approach cannot create an own-lane stop when own signal is green");
        check(!Evaluate(enabled,[Live(enabled,2),Live(enabled,8)]).Clear,"Conflicting replicas at one exact anchor cannot yield green clearance");
        check(!Evaluate(enabled,[Live(enabled,8,id:4)]).Clear,"A different signal ID at the same position cannot supply a missing own state");
        check(!Evaluate(enabled,[Live(enabled,99)]).Clear,"An unknown actual signal state cannot be interpreted as green");
        check(Evaluate(enabled,[Live(enabled,0)]).Clear,"The established explicit off-state policy is preserved by the instance fix");
        var ownItem=enabled.Plan.PlannedPath.Single();
        var ownCanonical=TollSignals.ForPath(ownItem.prefabPath!).Single();
        var barrier=new TollBarrierPlan("synthetic-own-toll",991001,3,ownCanonical.GetWorldOrientedPoint().Position,Vector3.UnitZ,
            RouteMatcher.ItemIdentity(ownItem));
        check(Evaluate(enabled,[Live(enabled,1,LiveType.GATE)],barrier).Clear,
            "The exactly selected canonical toll barrier remains owned by the toll controller instead of a duplicate traffic-light hold");
        check(!Evaluate(enabled,[Live(enabled,1,LiveType.GATE)],barrier with{Anchor=barrier.Anchor+new Vector3(8,0,0)}).Clear,
            "An unrelated selected barrier cannot suppress a contradictory actor at the own signal");
        check(!Evaluate(enabled,[Live(enabled,2)],barrier with{PrefabUid=991099}).Clear,
            "A selected barrier from another prefab cannot suppress an actual red traffic light at reused coordinates and ID");
        check(!Evaluate(enabled,[Live(enabled,1,LiveType.GATE)],barrier with{RouteIdentity=null}).Clear,
            "Absent directed route identity cannot suppress an unclassified runtime actor as a selected toll barrier");
        var twin=Build(true);var twinPath=twin.Plan.PlannedPath.Single().prefabPath!;
        var secondAnchor=twin.Anchor+new Vector3(-6,0,0);
        twinPath.Prefab.Descriptor.Semaphores.Add(new(){SemaphoreId=3,Position=secondAnchor,Type=MapType.UseProfile,Profile="cr_1x1"});
        var canonical=TollSignals.ForPath(twinPath).Single();
        var twinBarrier=new TollBarrierPlan("two-arm-toll",twin.Raw.Uid,3,canonical.GetWorldOrientedPoint().Position,Vector3.UnitZ,
            RouteMatcher.ItemIdentity(twin.Plan.PlannedPath.Single()));
        var arms=new[]{Live(twin,3,LiveType.GATE),Live(twin,3,LiveType.GATE,secondAnchor)};
        check(Vector3.Distance(twinBarrier.Anchor,twin.Anchor)>1&&TollSignals.ReadLive(arms,3,twinBarrier.Anchor)?.state==3,
            "The real canonical booth grouping confirms both open arms although legacy GetSemaphores chooses its other descriptor-first post");
        check(Evaluate(twin,arms,twinBarrier).Clear,
            "The actual selected two-arm toll passage cannot acquire a duplicate traffic-light stop at its noncanonical post");
        var explicitGate=Build(true,MapType.BarrierManualTimed);
        check(Evaluate(explicitGate,[Live(explicitGate,1,LiveType.GATE)]).Clear,
            "An explicitly typed PPD barrier retains its separate toll authority");
    }
    public static void TickPolicy(Action<bool,string> check)
    {
        TrafficSignalEvidence.Prepare().GetAwaiter().GetResult();
        foreach(var state in new[]{"unspawned","red","missing"}) {
            var driver=new RollingStartIntegration.Fixture(withoutAcc:true,writeDiagnostic:(_,_)=>{});
            driver.Set("options",new TollOptions{CurvePreviewEnabled=false,BriskOpenRoads=false});
            driver.F8();driver.Probe();driver.State.DesiredSpeed=25;
            var mapped=Build(state!="unspawned",shift:driver.Point);
            // Explicitly test actual Tick consumption of the extracted policy;
            // this is not an SDK-source or complete historical map replay.
            driver.Traffic=()=>Evaluate(mapped,state=="red"?[Live(mapped,2)]:[]);
            driver.Fresh();driver.Plugin.Tick();
            var command=driver.Outputs.LastOrDefault(e=>e.ChannelDefinition.Id==TollPlugin.LongitudinalChannelId);
            check(command!=null&&!driver.Plugin.FaultBraking&&driver.State.EnableAssists&&
                (state=="unspawned"?command.Variables.aforward>0&&command.Variables.abackward==0:
                    command.Variables.aforward==0&&command.Variables.abackward<0),
                "Actual Tick consumes signal policy without a native ACC or invented TollDecision: "+state);
            driver.Call("StopDiagnostics");
        }
    }
}
