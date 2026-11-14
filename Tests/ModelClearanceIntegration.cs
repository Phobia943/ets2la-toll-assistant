using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using ETS2LA.Game.Data;
using MautAssistent;
using TruckLib;
using TruckLib.Models;
using TruckLib.Models.Ppd;
using TruckLib.ScsMap;

public static class ModelClearanceIntegration
{
    static NavCurve Lane(float x,int gate)=>new() {StartPosition=new(x,0,0),EndPosition=new(x,0,20),Length=20,
        StartRotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI),EndRotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI),
        SemaphoreId=gate,AllowedVehicles=AllowedVehicles.AllVehicles};
    static Part Beam(string name,float x,float height,float z=10)=>new(name) {Pieces=[new() {
        Vertices=[new(new Vector3(x-2,height,z-.1f)),new(new Vector3(x+2,height,z-.1f)),
            new(new Vector3(x+2,height,z+.1f)),new(new Vector3(x-2,height,z+.1f))],
        Triangles=[new(0,1,2),new(0,2,3)]}]};
    public static void Geometry(Action<bool,string> assert)
    {
        var descriptor=new PrefabDescriptor {NavCurves=[Lane(0,7),Lane(6,8)]};
        var low=Beam("unknownbeam",0,2.5f);
        var blocked=ModelClearance.BlockedCurves(descriptor,[low]);
        assert(blocked.SequenceEqual(new[]{true,false}),"Low mesh blocks only the intersecting lane, independent of its name");
        assert(ModelClearance.BlockedCurves(descriptor,[Beam("roof",0,7.5f)]).All(x=>!x),"A high roof must leave a regular truck corridor free");
        assert(ModelClearance.BlockedCurves(descriptor,[Beam("marking",0,.05f)]).All(x=>!x),"Paint below the tested body corridor must not masquerade as a height restriction");
        assert(!ModelClearance.TriangleBox(new(2,0,.5f),new(.5f,0,2),new(2,0,2),Vector3.One),
            "An overlapping triangle bounding box alone cannot reject a lane");
        var q=Quaternion.CreateFromAxisAngle(Vector3.UnitY,.71f);var shift=new Vector3(12,0,-8);
        foreach(var c in descriptor.NavCurves) {c.StartPosition=Vector3.Transform(c.StartPosition,q)+shift;c.EndPosition=Vector3.Transform(c.EndPosition,q)+shift;c.StartRotation=q*c.StartRotation;c.EndRotation=q*c.EndRotation;}
        foreach(var vertex in low.Pieces.SelectMany(p=>p.Vertices))vertex.Position=Vector3.Transform(vertex.Position,q)+shift;
        assert(ModelClearance.BlockedCurves(descriptor,[low]).SequenceEqual(blocked),"Rotating and translating both the road and beam must preserve clearance classification");
        var bad=Beam("bad",0,2);bad.Pieces[0].Triangles[0]=new(0,1,99);
        bool rejected=false;try{ModelClearance.BlockedCurves(descriptor,[bad]);}catch(InvalidDataException){rejected=true;}
        assert(rejected,"Bad mesh indices cannot yield a free lane");
    }

    public static void VisibleParts(Action<bool,string> assert)
    {
        var descriptor=new PrefabDescriptor {NavCurves=[Lane(0,7),Lane(6,8)]};
        var floor=Beam("base",0,-.1f);floor.Locators.Add(new(){Position=new(0,0,5),HookupOffset=0});
        floor.Locators.Add(new(){Position=new(6,0,5),HookupOffset=0});
        var low=Beam("optional",0,2.5f);
        var model=new TruckLib.Models.Model {Parts=[floor,low],Variants=[new("default") {Attributes=[new(){Tag="visible",Value=1},new(){Tag="visible",Value=0}]}]};
        var text=Encoding.ASCII.GetBytes("tollgate.autopass.custom\0");var pmg=new byte[96+text.Length];pmg[0]=21;
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(pmg.AsSpan(88),96);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(pmg.AsSpan(92),text.Length);text.CopyTo(pmg,96);
        var inactive=TollModelEvidence.InspectModel(descriptor,model,"default",[],pmg);
        assert(inactive.BlockedCurves.All(x=>!x)&&inactive.Payments.Count==2,"Inactive geometry must not deny valid automatic payment lanes");
        var active=TollModelEvidence.InspectModel(descriptor,model,"default",["optional"],pmg);
        assert(active.BlockedCurves.SequenceEqual(new[]{true,false})&&active.Payments.Count==2,
            "Payment and active structural clearance must be distinct evidence");
        assert(TollModelEvidence.InspectModel(descriptor,model,"default",["missing"],pmg).BlockedCurves.Length==0,
            "An unrecognized additional part must fail closed");
        pmg[96]=(byte)'x';
        assert(TollModelEvidence.InspectModel(descriptor,model,"default",[],pmg).BlockedCurves.Length==0,
            "An unknown external child hook-up cannot masquerade as geometry-free space");
        pmg[96]=(byte)'t';
        var light=new TruckLib.Sii.Unit("light_source","unfamiliar.name");light.Attributes["range"]=10;
        assert(TollModelEvidence.IsNonSolidHookup(light),"Light classification must use its unit type, not a Greek model or light name");
        light.Attributes["model"]="/hidden/obstacle.pmd";
        assert(!TollModelEvidence.IsNonSolidHookup(light),"A child model attribute cannot be smuggled into a non-solid primitive");
        model.Variants[0].Attributes[0].Value=2;
        assert(TollModelEvidence.InspectModel(descriptor,model,"default",[],pmg).Payments.Count==0,"Unknown visibility rules cannot grant payment clearance");
    }

    public static void NonBlocking(Action<bool,string> assert)
    {
        var files=new StubFiles();var descriptor=new PrefabDescriptor {NavCurves=[Lane(0,7)]};
        var prefab=(ParsedPrefab)RuntimeHelpers.GetUninitializedObject(typeof(ParsedPrefab));
        prefab.Prefab=new Prefab{Model="fixture",Variant="default"};prefab.Descriptor=descriptor;
        using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();using var finished=new ManualResetEventSlim();int calls=0;bool detached=false;
        var evidence=new TollModelEvidence(()=>files,null,request=>{
            Interlocked.Increment(ref calls);entered.Set();release.Wait(TimeSpan.FromSeconds(5));
            detached=!ReferenceEquals(request.Descriptor,descriptor)&&!ReferenceEquals(request.Descriptor.NavCurves[0],descriptor.NavCurves[0]);
            var result=new TollModelEvidence.ModelInspection(request.Parts.Contains("newpart")?new Dictionary<int,TollPayment>():new Dictionary<int,TollPayment>{{7,TollPayment.Automatic}},[false]);
            finished.Set();return result;
        });
        assert(evidence.Read(prefab).Count==0,"The initial request must stay unknown while heavy inspection starts");
        assert(entered.Wait(TimeSpan.FromSeconds(2)),"The worker must reach its held inspection");
        try {
            var read=Task.Run(()=>evidence.Read(prefab));
            assert(read.Wait(TimeSpan.FromMilliseconds(300))&&read.Result.Count==0,"A blocked model loader must not hold a route caller");
            for(int i=0;i<100;i++){prefab.Prefab.Model="fixture"+i;evidence.Read(prefab);}
        } finally {release.Set();}
        assert(SpinWait.SpinUntil(()=>Volatile.Read(ref calls)>=5,TimeSpan.FromSeconds(2)),"Bounded queued requests must eventually finish");
        Thread.Sleep(20);
        assert(calls==5&&detached,"At most one active plus four pending jobs, with detached descriptor geometry");
        prefab.Prefab.Model="fixture";
        assert(evidence.Read(prefab).GetValueOrDefault(7)==TollPayment.Automatic,"The completed original model must remain cached");
        finished.Reset();
        prefab.Prefab.AdditionalParts.Add("newpart");
        assert(evidence.Read(prefab).Count==0,"A changed additional-part mask cannot reuse old clearance");
        assert(finished.Wait(TimeSpan.FromSeconds(2)),"The changed mask must get its own inspection");
    }
    sealed class StubFiles : IFileSystem
    {
        public char DirectorySeparator=>'/';public bool FileExists(string p)=>false;public bool DirectoryExists(string p)=>false;
        public IList<string> GetFiles(string p)=>[];public byte[] ReadAllBytes(string p)=>throw new NotSupportedException();
        public string ReadAllText(string p)=>throw new NotSupportedException();public string ReadAllText(string p,Encoding e)=>throw new NotSupportedException();
        public Stream Open(string p)=>throw new NotSupportedException();public string GetParent(string p)=>"/";
    }
}
