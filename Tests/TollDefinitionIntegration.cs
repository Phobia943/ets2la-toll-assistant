using System.Numerics;
using System.Text;
using ETS2LA.Game.Data;
using MautAssistent;
using TruckLib.Models;
using TruckLib.Models.Ppd;
using TruckLib.Sii;

public static class TollDefinitionIntegration
{
    public static void Run(Action<bool,string> assert)
    {
        var manual=new Unit("tollgate","tollgate.end.pl.a2_1");
        manual.Attributes["model"]="/model/activation/tollgate.pmd";
        manual.Attributes["animation"]="/model/activation/tollgate.pma";
        manual.Attributes["type"]=(TruckLib.Token)"end";manual.Attributes["trigger_length"]=10;
        assert(TollDefinition.Read(manual,false)==new TollDefinition(true,TollPayment.Manual),
            "Legacy ticket/payment gates must be known manual gates rather than unrecognized geometry or invented autopass");
        var automatic=new Unit("tollgate","unfamiliar.region.tag");automatic.Attributes["max_activation_speed"]=200;
        automatic.Attributes["trigger_length"]=20;automatic.Attributes["type"]=new LinkPointer("oversize_disabled");
        assert(TollDefinition.Read(automatic,false).Payment==TollPayment.Automatic,
            "Explicit automatic trigger semantics work without a country-specific name");
        assert(TollDefinition.Read(automatic,true)==new TollDefinition(true,TollPayment.Unknown),"Disabled special-job lanes cannot grant passage");
        automatic.Attributes["model"]="/uninspected/low-bar.pmd";
        assert(!TollDefinition.Read(automatic,false).Known,"Arbitrary child geometry cannot hide behind a toll unit class");
        automatic.Attributes.Remove("model");automatic.Attributes["type"]="unrecognized";
        assert(!TollDefinition.Read(automatic,false).Known,"Unknown toll semantics remain unsupported");

        var descriptor=new PrefabDescriptor {NavCurves=[new() {StartPosition=Vector3.Zero,EndPosition=new(0,0,20),Length=20,
            StartRotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI),EndRotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI),SemaphoreId=1}]};
        var part=new Part("base") {Pieces=[new() {Vertices=[new(new Vector3(-1,-.1f,0)),new(new Vector3(1,-.1f,0)),new(new Vector3(0,-.1f,20))],Triangles=[new(0,1,2)]}],
            Locators=[new(){Position=new(0,0,5),HookupOffset=0}]};
        var model=new Model {Parts=[part],Variants=[new("default"){Attributes=[new(){Tag="visible",Value=1}]}]};
        var hook=Encoding.ASCII.GetBytes(manual.Name+"\0");var bytes=new byte[96+hook.Length];bytes[0]=21;
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(88),96);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(92),hook.Length);hook.CopyTo(bytes,96);
        var evidence=TollModelEvidence.InspectModel(descriptor,model,"default",[],bytes,tollUnits:new Dictionary<string,Unit>{{manual.Name,manual}});
        assert(evidence.Payments.GetValueOrDefault(1)==TollPayment.Manual&&evidence.BlockedCurves is [false],
            "An actual legacy locator can supply manual payment evidence while geometry is evaluated separately");
        hook=Encoding.ASCII.GetBytes("tollgate.autopass.custom\0");bytes=new byte[96+hook.Length];bytes[0]=21;
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(88),96);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(92),hook.Length);hook.CopyTo(bytes,96);
        assert(TollModelEvidence.InspectModel(descriptor,model,"default",[],bytes,tollUnits:new Dictionary<string,Unit>()).Payments.Count==0,
            "An automatic-sounding name without its definition cannot grant payment evidence in production");
    }
}
