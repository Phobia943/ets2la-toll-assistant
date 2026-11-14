using System.Numerics;
using System.Text;
using MautAssistent;
using TruckLib.Models;
using TruckLib.Models.Ppd;
using TruckLib.Sii;

public static class ManualTriggerIntegration
{
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
        assert(zones.TryGetValue(1,out var zone)&&zone==new ManualStopZone(11,19)&&zone.Choose(8)==15,
            "Straight contained model trigger yields an upstream stop without reducing the safe gap");
        part.Pieces.Add(new() {Vertices=[new(new Vector3(-1,-.1f,0)),new(new Vector3(1,-.1f,0)),new(new Vector3(0,-.1f,30))],Triangles=[new(0,1,2)]});
        var model=new Model {Parts=[part],Variants=[new("default"){Attributes=[new(){Tag="visible",Value=1}]}]};
        var inspected=TollModelEvidence.InspectModel(descriptor,model,"default",[],bytes,tollUnits:units);
        assert(inspected.Payments.GetValueOrDefault(1)==TollPayment.Manual&&inspected.ManualStops.GetValueOrDefault(1)==zone,
            "Full model inspection must retain payment identity and the matching stop interval together");
        unit.Attributes["trigger_offset"]=-15;
        assert(ManualTriggerEvidence.Read(descriptor,[part],bytes,units,false).Count==0,"Unverified offset semantics cannot supply a target");
        unit.Attributes.Remove("trigger_offset");marker.Scale=2;
        assert(ManualTriggerEvidence.Read(descriptor,[part],bytes,units,false).Count==0,"Scaled trigger placement is not guessed");
        marker.Scale=1;marker.Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI/2);
        assert(ManualTriggerEvidence.Read(descriptor,[part],bytes,units,false).Count==0,"Crosswise trigger cannot supply a lane target");
        marker.Rotation=Quaternion.Identity;unit.Attributes["max_activation_speed"]=200;
        assert(ManualTriggerEvidence.Read(descriptor,[part],bytes,units,false).Count==0,"Automatic trigger never supplies a manual stop");
        var geometry=StatusGeometry.Build(new RouteTrace([Vector3.Zero,new(0,0,40)]),new(0,0,10),[]);
        var json=System.Text.Json.JsonSerializer.SerializeToElement(geometry);
        assert(!json.GetProperty("nativeProjectionMeasured").GetBoolean()&&json.GetProperty("truckFactor").ValueKind==System.Text.Json.JsonValueKind.Null,
            "Sampled diagnostic geometry must not masquerade as a native PathLib projection");
    }
}
