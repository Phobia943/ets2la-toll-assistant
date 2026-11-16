using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using ETS2LA.Game.Data;
using MautAssistent;
using PathLib;
using TruckLib;
using TruckLib.Models.Ppd;

// Real cache/worker code with an injected bounded inspector; no asset engine,
// SDK, native output owner or memory-mapped channel is constructed.
public static class ModelCertificateAdversarialIntegration
{
    sealed class Files:IFileSystem
    {
        public char DirectorySeparator=>'/';public bool FileExists(string p)=>false;public bool DirectoryExists(string p)=>false;
        public IList<string> GetFiles(string p)=>[];public byte[] ReadAllBytes(string p)=>throw new NotSupportedException();
        public string ReadAllText(string p)=>throw new NotSupportedException();public string ReadAllText(string p,Encoding e)=>throw new NotSupportedException();
        public Stream Open(string p)=>throw new NotSupportedException();public string GetParent(string p)=>"/";
    }
    public static void DominanceAndRevocation(Action<bool,string> assert)
    {
        IFileSystem source=new Files();(float Height,float Width) profile=(4.5f,3.5f);
        var rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI);
        var curve=new NavCurve{StartPosition=Vector3.Zero,EndPosition=new(0,0,100),Length=100,
            StartRotation=rotation,EndRotation=rotation,AllowedVehicles=AllowedVehicles.AllVehicles,SemaphoreId=7};
        var descriptor=new PrefabDescriptor{NavCurves=[curve]};
        var prefab=(ParsedPrefab)RuntimeHelpers.GetUninitializedObject(typeof(ParsedPrefab));
        prefab.Prefab=new(){Uid=77,Model="fixture",Variant="default"};prefab.Descriptor=descriptor;
        var path=new PrefabPath(prefab,new(){Uid=1},new(){Uid=2},[curve],Direction.Forward,Matrix4x4.Identity,Vector3.Zero);
        int holdWorkers=0,entered=0;var release=new ManualResetEventSlim();
        var evidence=new TollModelEvidence(()=>source,null,request=>{
            if(Volatile.Read(ref holdWorkers)!=0) {
                Interlocked.Increment(ref entered);
                if(!release.Wait(TimeSpan.FromSeconds(10)))throw new TimeoutException("Independent cache fixture gate not released");
            }
            return new TollModelEvidence.ModelInspection(new Dictionary<int,TollPayment>{{7,TollPayment.Automatic}},[false]){HasDynamicParked=true};
        },()=>profile);
        assert(SpinWait.SpinUntil(()=>evidence.Read(prefab).Count==1,TimeSpan.FromSeconds(2)),"The actual worker must finish the initial 4.5x3.5 certificate");
        assert(evidence.IsClear(path),"A completed certificate authorizes its exact fixture corridor");
        Volatile.Write(ref holdWorkers,1);
        try {
            profile=(4.4f,3.4f);
            assert(evidence.IsClear(path),"A completed certificate dominates a corridor smaller in BOTH dimensions while its exact worker is pending");
            assert(SpinWait.SpinUntil(()=>Volatile.Read(ref entered)>0,TimeSpan.FromSeconds(2)),"The smaller exact-profile worker is genuinely pending rather than instantly completing the test");
            profile=(4.6f,3.4f);
            assert(!evidence.IsClear(path),"A narrower but taller corridor cannot borrow the old certificate");
            profile=(4.4f,3.6f);
            assert(!evidence.IsClear(path),"A shorter but wider corridor cannot borrow the old certificate");
            profile=(float.NaN,3.4f);
            assert(!evidence.IsClear(path)&&evidence.ParkedRequirement(prefab)==true,
                "Unknown current size revokes movement proof while retaining the independently known physical parked requirement");
            profile=(4.4f,3.4f);
            curve.EndPosition=new(5,0,100);
            assert(!evidence.IsClear(path)&&evidence.ParkedRequirement(prefab)==null,
                "Mutation of the same descriptor object cannot reuse its earlier geometry or parked-attachment certificate");
            curve.EndPosition=new(0,0,100);
            prefab.Prefab.AdditionalParts.Add("newpart");
            assert(!evidence.IsClear(path),"A changed enabled-part mask cannot borrow geometrically larger proof for the previous model state");
            prefab.Prefab.AdditionalParts.Clear();
            source=new Files();
            assert(!evidence.IsClear(path)&&evidence.ParkedRequirement(prefab)==null,
                "A new asset source cannot inherit old-source clear or structural parked proof, even while old workers remain queued");
        } finally {Volatile.Write(ref holdWorkers,0);release.Set();}
    }
}
