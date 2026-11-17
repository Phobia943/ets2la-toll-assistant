using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using ETS2LA.Game.Data;
using MautAssistent;
using PathLib;
using TruckLib;
using TruckLib.Models;
using TruckLib.Models.Ppd;

// Synthetic, independently typed model/worker fixtures only; no SDK or outputs.
public static class StandardModelEvidenceIntegration
{
    sealed class Files:IFileSystem
    {
        public char DirectorySeparator=>'/';public bool FileExists(string p)=>false;public bool DirectoryExists(string p)=>false;
        public IList<string> GetFiles(string p)=>[];public byte[] ReadAllBytes(string p)=>throw new NotSupportedException();
        public string ReadAllText(string p)=>throw new NotSupportedException();public string ReadAllText(string p,Encoding e)=>throw new NotSupportedException();
        public Stream Open(string p)=>throw new NotSupportedException();public string GetParent(string p)=>"/";
    }
    static (ParsedPrefab Prefab,PrefabPath Path,Model Model,byte[] Pmg) Fixture()
    {
        var q=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI);
        var c=new NavCurve {StartPosition=Vector3.Zero,EndPosition=new(0,0,20),StartRotation=q,EndRotation=q,
            Length=20,AllowedVehicles=AllowedVehicles.AllVehicles,SemaphoreId=7};
        var prefab=(ParsedPrefab)RuntimeHelpers.GetUninitializedObject(typeof(ParsedPrefab));
        prefab.Prefab=new(){Uid=3200,Model="fixture",Variant="default"};prefab.Descriptor=new(){NavCurves=[c]};
        var path=new PrefabPath(prefab,new(){Uid=3201},new(){Uid=3202},[c],Direction.Forward,Matrix4x4.Identity,Vector3.Zero);
        var p=new Part("base") {Locators=[new(){Position=new(0,0,5),HookupOffset=0}],Pieces=[new(){
            Vertices=[new(new Vector3(-2,-1,10)),new(new Vector3(2,-1,10)),new(new Vector3(0,-1,11))],Triangles=[new(0,1,2)]}]};
        var model=new Model {Parts=[p],Variants=[new("default"){Attributes=[new(){Tag="visible",Value=1}]}]};
        var text=Encoding.ASCII.GetBytes("tollgate.autopass.custom\0");var pmg=new byte[96+text.Length];pmg[0]=21;
        BinaryPrimitives.WriteInt32LittleEndian(pmg.AsSpan(88),96);BinaryPrimitives.WriteInt32LittleEndian(pmg.AsSpan(92),text.Length);text.CopyTo(pmg,96);
        return(prefab,path,model,pmg);
    }
    public static void PaymentWithoutClearance(Action<bool,string> assert)
    {
        var f=Fixture();var descriptor=f.Prefab.Descriptor!;
        var valid=TollModelEvidence.InspectModel(descriptor,f.Model,"default",[],f.Pmg,height:4.5f,width:3.4f);
        var invalid=TollModelEvidence.InspectModel(descriptor,f.Model,"default",[],f.Pmg,height:float.NaN,width:3.4f);
        assert(valid.Issue==null&&valid.Payments.GetValueOrDefault(7)==TollPayment.Automatic&&valid.BlockedCurves.SequenceEqual(new[]{false}),
            "A valid nominal corridor still proves the unchanged automatic lane");
        assert(invalid.Issue!=null&&invalid.Payments.GetValueOrDefault(7)==TollPayment.Automatic&&invalid.BlockedCurves.Length==0&&invalid.StaticGeometry==null,
            "NaN clearance preserves independent payment markers but cannot fabricate a free mask or geometry certificate");
        assert(invalid.AttachmentsVerified,"A profile error does not invalidate an already completed attachment-kind scan");
        f.Model.Parts[0].Pieces[0].Triangles[0]=new(0,1,99);
        var broken=TollModelEvidence.InspectModel(descriptor,f.Model,"default",[],f.Pmg,height:4.5f,width:3.4f);
        assert(broken.Issue!=null&&broken.Payments.GetValueOrDefault(7)==TollPayment.Automatic&&broken.BlockedCurves.Length==0&&broken.StaticGeometry==null,
            "Invalid mesh indices retain a separately matched payment type while geometry stays rejected");
        f.Pmg[96]=(byte)'x';
        assert(TollModelEvidence.InspectModel(descriptor,f.Model,"default",[],f.Pmg).Payments.Count==0,
            "Unknown hook-up structure cannot invent a payment-only success");
        f.Pmg[96]=(byte)'t';f.Model.Variants[0].Attributes[0].Value=2;
        assert(TollModelEvidence.InspectModel(descriptor,f.Model,"default",[],f.Pmg).Payments.Count==0,
            "Unknown visibility remains structurally unknown rather than taking the nominal fallback");
    }
    public static void ProfileIndependentCache(Action<bool,string> assert)
    {
        var f=Fixture();IFileSystem files=new Files();(float,float) profile=(float.NaN,3.4f);int calls=0,hold=0;
        (float,float) requested=default;using var release=new ManualResetEventSlim();using var entered=new ManualResetEventSlim();
        var evidence=new TollModelEvidence(()=>files,null,r=>{
            Interlocked.Increment(ref calls);
            requested=(r.Height,r.Width);
            if(Volatile.Read(ref hold)!=0) {entered.Set();if(!release.Wait(TimeSpan.FromSeconds(5)))throw new TimeoutException("Metadata fixture not released");}
            return TollModelEvidence.InspectModel(r.Descriptor,f.Model,r.Variant,r.Parts,f.Pmg,height:r.Height,width:r.Width)
                with {HasDynamicParked=true,ManualStops=new Dictionary<int,ManualStopZone>{{7,new(11,19)}}};
        },()=>profile);
        assert(SpinWait.SpinUntil(()=>evidence.Read(f.Prefab).Count==1,TimeSpan.FromSeconds(3)),
            "Cold metadata discovery works even when the vehicle profile is NaN");
        assert(evidence.Read(f.Prefab).GetValueOrDefault(7)==TollPayment.Automatic&&evidence.ParkedRequirement(f.Prefab)==true,
            "Typed payment and physical parked requirement are independent of the unavailable cargo envelope");
        assert(requested==(4.5f,3.4f)&&evidence.ReadManualStop(f.Prefab,7)==new ManualStopZone(11,19),
            "The fallback uses the named nominal 4.5x3.4 profile and retains independently verified manual-stop metadata");
        assert(!evidence.IsClear(f.Path)&&evidence.FindClearRoute(f.Path,7).Path==null&&evidence.ReadInspection(f.Prefab)==null,
            "Metadata's nominal inspection never authorizes strict clearance for an invalid current profile");
        profile=(4.5f,3.4f);
        assert(evidence.IsClear(f.Path)&&evidence.FindClearRoute(f.Path,7).Path!=null,
            "Selecting the exact nominal profile can use its completed certificate");
        profile=(4.5f,3.6f);
        assert(SpinWait.SpinUntil(()=>evidence.Diagnose(f.Prefab).Completed,TimeSpan.FromSeconds(3)),"The larger explicit profile gets its own certificate");
        assert(Volatile.Read(ref calls)>=2,"A smaller nominal metadata proof cannot substitute for larger clearance inspection");
        profile=(float.NaN,float.NaN);Volatile.Write(ref hold,1);files=new Files();
        try {
            evidence.Prefetch(f.Prefab);
            assert(entered.Wait(TimeSpan.FromSeconds(2)),"Replacement asset metadata is genuinely pending");
            assert(evidence.ParkedRequirement(f.Prefab)==null&&evidence.Read(f.Prefab).Count==0,
                "A changed asset source cannot inherit the old-source payment or parked certificate");
            assert(!evidence.IsClear(f.Path),"Asset replacement cannot turn unavailable current dimensions into clearance");
        } finally {release.Set();}
    }
    public static void FailedGeometryStaysClosed(Action<bool,string> assert)
    {
        var f=Fixture();var files=new Files();
        var result=new TollModelEvidence.ModelInspection(new Dictionary<int,TollPayment>{{7,TollPayment.Automatic}},[false])
            {Issue="Synthetic geometry failure",AttachmentsVerified=true,HasDynamicParked=true};
        var evidence=new TollModelEvidence(()=>files,null,_=>result,()=> (4.5f,3.4f));
        assert(SpinWait.SpinUntil(()=>evidence.Read(f.Prefab).Count==1,TimeSpan.FromSeconds(3)),"Independent metadata remains readable on a completed geometry error");
        assert(!evidence.IsClear(f.Path)&&evidence.FindClearRoute(f.Path,7).Path==null,
            "Issue rejects both clearance APIs even when a malformed inspection supplies a full all-false mask");
        assert(evidence.ParkedRequirement(f.Prefab)==true,"A fully discovered parked attachment remains required despite a geometry error");
        var unknown=new TollModelEvidence(()=>files,null,_=>result with {AttachmentsVerified=false,HasDynamicParked=false},()=> (4.5f,3.4f));
        assert(SpinWait.SpinUntil(()=>unknown.Read(f.Prefab).Count==1,TimeSpan.FromSeconds(3)),"The test actually completed its unknown attachment inspection");
        assert(unknown.ParkedRequirement(f.Prefab)==null,"Incomplete attachment discovery plus an Issue cannot become a false/no-scene-requirement claim");
    }
    public static void MetadataIdentity(Action<bool,string> assert)
    {
        var f=Fixture();var files=new Files();bool special=false;int hold=0;
        using var release=new ManualResetEventSlim();
        var evidence=new TollModelEvidence(()=>files,()=>special,r=>{
            if(Volatile.Read(ref hold)!=0&&!release.Wait(TimeSpan.FromSeconds(5)))throw new TimeoutException("Identity fixture not released");
            return new(new Dictionary<int,TollPayment>{{7,TollPayment.Automatic}},[false]){HasDynamicParked=true};
        },()=> (float.NaN,float.NaN));
        assert(SpinWait.SpinUntil(()=>evidence.Read(f.Prefab).Count==1,TimeSpan.FromSeconds(3)),"Base structural metadata completed before identity mutations");
        Volatile.Write(ref hold,1);
        void Unknown(string reason)=>assert(evidence.Read(f.Prefab).Count==0&&evidence.ParkedRequirement(f.Prefab)==null,reason);
        try {
            f.Prefab.Prefab.Variant="other";Unknown("Changed variant cannot borrow nominal payment or parked metadata");f.Prefab.Prefab.Variant="default";
            f.Prefab.Prefab.AdditionalParts.Add("optional");Unknown("Changed enabled parts cannot borrow nominal metadata");f.Prefab.Prefab.AdditionalParts.Clear();
            special=true;Unknown("Changed special-job mode cannot borrow ordinary automatic-payment metadata");special=false;
            var curve=f.Prefab.Descriptor!.NavCurves[0];curve.EndPosition+=Vector3.UnitX;
            Unknown("Mutation of the same descriptor object invalidates its structural metadata stamp");curve.EndPosition-=Vector3.UnitX;
            assert(evidence.Read(f.Prefab).Count==1&&evidence.ParkedRequirement(f.Prefab)==true,
                "Exactly restored complete structural identity can still reuse its original metadata");
        } finally {release.Set();}
    }
}
