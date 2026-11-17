using System.Numerics;
using System.Text;
using MautAssistent;
using TruckLib.Models;
using TruckLib.Models.Ppd;
using TruckLib.Sii;
using ETS2LA.Game.Data;
using System.Runtime.CompilerServices;

public static class ManualTriggerIntegration
{
    public static void WorkerSnapshot(Action<bool,string> assert)
    {
        var rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI);
        var descriptor=new PrefabDescriptor {NavCurves=[new NavCurve {StartPosition=Vector3.Zero,EndPosition=new(0,0,30),Length=30,
            StartRotation=rotation,EndRotation=rotation,SemaphoreId=1}]};
        descriptor.Semaphores.Add(new(){SemaphoreId=1,Position=new(2,0,30)});
        var part=new Part("base"){Locators=[new Locator {Position=new(0,0,15),Rotation=Quaternion.Identity,Scale=1,HookupOffset=0}]};
        part.Pieces.Add(new(){Vertices=[new(new Vector3(-1,-.1f,0)),new(new Vector3(1,-.1f,0)),new(new Vector3(0,-.1f,30))],Triangles=[new(0,1,2)]});
        var unit=new Unit("tollgate","tollgate.fixture");unit.Attributes["trigger_length"]=10;unit.Attributes["trigger_width"]=4;
        var units=new Dictionary<string,Unit>{{unit.Name,unit}};
        var model=new Model {Parts=[part],Variants=[new("default"){Attributes=[new(){Tag="visible",Value=1}]}]};
        var hook=Encoding.ASCII.GetBytes(unit.Name+"\0");var bytes=new byte[96+hook.Length];bytes[0]=21;
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(88),96);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(92),hook.Length);hook.CopyTo(bytes,96);
        var incomplete=new PrefabDescriptor {NavCurves=descriptor.NavCurves};
        assert(TollModelEvidence.InspectModel(incomplete,model,"default",[],bytes,tollUnits:units).ManualStops.Count==0&&
            TollModelEvidence.InspectModel(descriptor,model,"default",[],bytes,tollUnits:units).ManualStops.Count==1,
            "The former signal-free snapshot loses a valid payment interval even with valid model geometry");
        var prefab=(ParsedPrefab)RuntimeHelpers.GetUninitializedObject(typeof(ParsedPrefab));
        prefab.Prefab=new TruckLib.ScsMap.Prefab {Model="fixture",Variant="default"};prefab.Descriptor=descriptor;
        using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
        // A stable source identity is needed by the real cache, as in the host.
        var files=new ModelClearanceIntegration.StubFiles();
        TollModelEvidence.ModelInspection? detachedResult=null;
        var evidence=new TollModelEvidence(()=>files,null,request=>{
            entered.Set();if(!release.Wait(TimeSpan.FromSeconds(4)))throw new TimeoutException();
            var inspected=TollModelEvidence.InspectModel(request.Descriptor,model,"default",[],bytes,tollUnits:units);
            Interlocked.CompareExchange(ref detachedResult,inspected,null);return inspected;
        });
        try {
            assert(evidence.ReadManualStop(prefab,1)==null&&entered.Wait(TimeSpan.FromSeconds(2)),"The actual model worker must be held before inspection");
            descriptor.Semaphores[0].Position=new(2,0,300);
            release.Set();
            assert(SpinWait.SpinUntil(()=>Volatile.Read(ref detachedResult)!=null,TimeSpan.FromSeconds(2))&&
                detachedResult!.ManualStops.GetValueOrDefault(1) is {MinimumOffset:11,MaximumOffset:19,Trigger:not null},
                "The detached worker retains the original signal geometry even after the live descriptor changes");
            assert(evidence.ReadManualStop(prefab,1)==null,"A mutated live descriptor cannot reuse the old payment-zone certificate");
            descriptor.Semaphores[0].Position=new(2,0,30);
            assert(SpinWait.SpinUntil(()=>evidence.ReadManualStop(prefab,1) is {MinimumOffset:11,MaximumOffset:19,Trigger:not null},TimeSpan.FromSeconds(2)),
                "Exactly restored geometry can reuse its original immutable snapshot certificate");
        } finally {release.Set();}
    }
    public static void Run(Action<bool,string> assert)
    {
        var rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI);
        var curve=new NavCurve {StartPosition=Vector3.Zero,EndPosition=new(0,0,30),Length=30,
            StartRotation=rotation,EndRotation=rotation,SemaphoreId=1};
        var descriptor=new PrefabDescriptor {NavCurves=[curve]};
        descriptor.Semaphores.Add(new(){SemaphoreId=1,Position=new(2,0,30)});
        var marker=new Locator {Position=new(0,0,15),Rotation=Quaternion.Identity,Scale=1,HookupOffset=0};
        var part=new Part("base"){Locators=[marker]};
        var unit=new Unit("tollgate","tollgate.fixture");
        unit.Attributes["trigger_length"]=10;unit.Attributes["trigger_width"]=4;
        var units=new Dictionary<string,Unit>{{unit.Name,unit}};
        var hook=Encoding.ASCII.GetBytes(unit.Name+"\0");var bytes=new byte[96+hook.Length];bytes[0]=21;
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(88),96);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(92),hook.Length);hook.CopyTo(bytes,96);
        var zones=ManualTriggerEvidence.Read(descriptor,[part],bytes,units,false);
        assert(zones.TryGetValue(1,out var zone)&&zone is {MinimumOffset:11,MaximumOffset:19,Trigger:not null}&&zone.Choose(8)==15,
            "Straight contained model trigger yields an upstream stop without reducing the safe gap");
        assert(zone!.Trigger==new ManualTriggerZone(new(0,0,15),Vector3.UnitZ,4,1.75),
            "Interaction proof retains the actual directed trigger center and conservative longitudinal/lateral interior");
        var duplicate=new Locator {Position=marker.Position,Rotation=marker.Rotation,Scale=1,HookupOffset=0};
        part.Locators.Add(duplicate);
        assert(ManualTriggerEvidence.Read(descriptor,[part],bytes,units,false).GetValueOrDefault(1)?.Trigger==zone.Trigger,
            "Identical independently verified activation rectangles retain their common proof");
        duplicate.Position+=new Vector3(0,0,.1f);
        assert(ManualTriggerEvidence.Read(descriptor,[part],bytes,units,false).GetValueOrDefault(1) is {Trigger:null},
            "A longitudinal overlap never fabricates a common lateral rectangle from distinct markers");
        part.Locators.Remove(duplicate);
        part.Pieces.Add(new() {Vertices=[new(new Vector3(-1,-.1f,0)),new(new Vector3(1,-.1f,0)),new(new Vector3(0,-.1f,30))],Triangles=[new(0,1,2)]});
        var model=new Model {Parts=[part],Variants=[new("default"){Attributes=[new(){Tag="visible",Value=1}]}]};
        var inspected=TollModelEvidence.InspectModel(descriptor,model,"default",[],bytes,tollUnits:units);
        assert(inspected.Payments.GetValueOrDefault(1)==TollPayment.Manual&&inspected.ManualStops.GetValueOrDefault(1)==zone,
            "Full model inspection must retain payment identity and the matching stop interval together");
        unit.Attributes["trigger_offset"]=-15;
        assert(ManualTriggerEvidence.Read(descriptor,[part],bytes,units,false).Count==0,"Unverified offset semantics cannot supply a target");
        unit.Attributes.Remove("trigger_offset");marker.Scale=2;
        assert(ManualTriggerEvidence.Read(descriptor,[part],bytes,units,false).Count==0,"Scaled trigger placement is not guessed");
        marker.Scale=float.NaN;
        assert(ManualTriggerEvidence.Read(descriptor,[part],bytes,units,false).Count==0,"A NaN trigger scale cannot bypass the unscaled-placement contract");
        marker.Scale=1;marker.Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI/2);
        assert(ManualTriggerEvidence.Read(descriptor,[part],bytes,units,false).Count==0,"Crosswise trigger cannot supply a lane target");
        marker.Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitZ,MathF.PI/2);
        assert(ManualTriggerEvidence.Read(descriptor,[part],bytes,units,false).Count==0,"Roll about an aligned forward axis cannot fabricate a horizontal trigger");
        marker.Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,5*MathF.PI/180);
        curve.EndPosition=new(0,0,100);curve.Length=100;marker.Position=new(0,0,50);
        descriptor.Semaphores[0].Position=new(2,0,100);unit.Attributes["trigger_length"]=50;unit.Attributes["trigger_width"]=3;
        assert(ManualTriggerEvidence.Read(descriptor,[part],bytes,units,false).Count==0,
            "A5-degree yaw on a50m narrow trigger is rejected: lane-aligned edges would extend outside the real rectangle");
        marker.Rotation=Quaternion.Identity;
        assert(ManualTriggerEvidence.Read(descriptor,[part],bytes,units,false).Count==1,"The same long trigger is usable with actually aligned marker geometry");
        curve.EndPosition=new(0,0,30);curve.Length=30;marker.Position=new(0,0,15);
        descriptor.Semaphores[0].Position=new(2,0,30);unit.Attributes["trigger_length"]=10;unit.Attributes["trigger_width"]=4;
        marker.Rotation=Quaternion.Identity;unit.Attributes["max_activation_speed"]=200;
        assert(ManualTriggerEvidence.Read(descriptor,[part],bytes,units,false).Count==0,"Automatic trigger never supplies a manual stop");
        var geometry=StatusGeometry.Build(new RouteTrace([Vector3.Zero,new(0,0,40)]),new(0,0,10),[]);
        var json=System.Text.Json.JsonSerializer.SerializeToElement(geometry);
        assert(!json.GetProperty("nativeProjectionMeasured").GetBoolean()&&json.GetProperty("truckFactor").ValueKind==System.Text.Json.JsonValueKind.Null,
            "Sampled diagnostic geometry must not masquerade as a native PathLib projection");
    }
}
