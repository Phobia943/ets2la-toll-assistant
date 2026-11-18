using System.Numerics;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using ETS2LA.Game.Data;
using MautAssistent;
using TruckLib;
using TruckLib.Models;
using TruckLib.Models.Ppd;

// Exercise the actual queue, idle teardown, catalog parsers and source changes.
// Only archive I/O is replaced; the model inspector is NOT bypassed.
public static class ModelCatalogReuseIntegration
{
    sealed class Source(string name) : IFileSystem
    {
        public readonly string Name=name;
        public int Registries,HookupScans,Opened,Disposed,ModelReads;
        public volatile bool BadRegistry,BadHookup;
        public bool Known,Manual,Blocked,FailPaths,Duplicate,BadDuplicate;
        public ManualResetEventSlim? Entered,Continue;
        public char DirectorySeparator=>'/';
        public bool FileExists(string p)=>true;
        public bool DirectoryExists(string p)=>true;
        public IList<string> GetFiles(string p)
        {
            Interlocked.Increment(ref Registries);Entered?.Set();
            if(Continue!=null&&!Continue.Wait(TimeSpan.FromSeconds(5)))throw new TimeoutException();
            if(BadRegistry)throw new IOException("Catalog source unavailable");
            return ["/def/world/prefab.fixture.sii"];
        }
        string Text(string path)
        {
            if(path.StartsWith("/unit/",StringComparison.Ordinal)) {
                if(BadHookup)throw new IOException("Hookup source unavailable");
                bool conflict=path.EndsWith("conflict.sii",StringComparison.Ordinal);
                if(conflict&&BadDuplicate)throw new IOException("Conflicting definition unavailable");
                return "SiiNunit\n{\ntollgate : tollgate.fixture\n{\ntrigger_length: 20\nmax_activation_speed: "+(Manual||conflict?"0":"200")+"\n}\n}";
            }
            if(Known)return "SiiNunit { prefab_model : prefab.known1 { model_desc: \"/prefab/fixture.pmd\" } prefab_model : prefab.known2 { model_desc: \"/prefab/fixture.pmd\" } }";
            return "SiiNunit { prefab_model : prefab."+Name+" { model_desc: \"/prefab/fixture.pmd\" } }";
        }
        public byte[] ReadAllBytes(string p)
        {
            if(!p.EndsWith(".pmg",StringComparison.Ordinal))return Encoding.UTF8.GetBytes(Text(p));
            var hook=Encoding.ASCII.GetBytes("tollgate.fixture\0");var pmg=new byte[96+hook.Length];pmg[0]=21;
            BinaryPrimitives.WriteInt32LittleEndian(pmg.AsSpan(88),96);
            BinaryPrimitives.WriteInt32LittleEndian(pmg.AsSpan(92),hook.Length);hook.CopyTo(pmg,96);return pmg;
        }
        public Model ReadModel()
        {
            Interlocked.Increment(ref ModelReads);
            float y=Blocked?2.5f:-1;
            var part=new Part("base") {Locators=[new(){Position=new(0,0,5),Rotation=Quaternion.Identity,Scale=1,HookupOffset=0}],Pieces=[new(){
                Vertices=[new(new Vector3(-2,y,9)),new(new Vector3(2,y,9)),new(new Vector3(0,y,11))],Triangles=[new(0,1,2)]}]};
            return new Model {Parts=[part],Variants=[new("default"){Attributes=[new(){Tag="visible",Value=1}]}]};
        }
        public string ReadAllText(string p)=>Text(p);
        public string ReadAllText(string p,Encoding e)=>Text(p);
        public Stream Open(string p)=>new MemoryStream(ReadAllBytes(p));
        public string GetParent(string p)=>p[..p.LastIndexOf('/')];
    }
    sealed class Assets(Source source) : IModelAssets
    {
        bool disposed;
        public IFileSystem Files=>source;
        public Model ReadModel(string path)=>source.ReadModel();
        public IEnumerable<string> FilesBelow(string root)
        {Interlocked.Increment(ref source.HookupScans);return source.Duplicate?
            ["/unit/hookup/fixture.sii","/unit/hookup/conflict.sii"]:["/unit/hookup/fixture.sii"];}
        public void Dispose(){if(!disposed){disposed=true;Interlocked.Increment(ref source.Disposed);}}
    }
    sealed class Fixture
    {
        public Source Current=new("a");
        readonly ConcurrentDictionary<string,Source> sources=new();
        public readonly TollModelEvidence Evidence;
        public Fixture()
        {
            Evidence=new(()=>Current,null,null,archivePaths:files=> {
                var source=(Source)files;
                if(source.FailPaths){source.FailPaths=false;throw new IOException("Archive snapshot unavailable");}
                sources[source.Name]=source;return [source.Name];
            },openAssets:paths=> {
                var source=sources[paths[0]];Interlocked.Increment(ref source.Opened);return new Assets(source);
            });
        }
        public static ParsedPrefab Prefab(string model)
        {
            var p=(ParsedPrefab)RuntimeHelpers.GetUninitializedObject(typeof(ParsedPrefab));
            p.Prefab=new(){Uid=7,Model=model,Variant="default"};
            p.Descriptor=new PrefabDescriptor {NavCurves=[new NavCurve {
                StartPosition=Vector3.Zero,EndPosition=new(0,0,20),Length=20,SemaphoreId=7,
                StartRotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI),
                EndRotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI),AllowedVehicles=AllowedVehicles.AllVehicles}]};
            return p;
        }
        public TollModelEvidence.ModelDiagnostic Finish(ParsedPrefab p)
        {
            if(!SpinWait.SpinUntil(()=>Evidence.Diagnose(p).Completed,TimeSpan.FromSeconds(4)))throw new TimeoutException("Model job did not finish");
            return Evidence.Diagnose(p);
        }
        public static void Idle(Source source)
        {
            if(!SpinWait.SpinUntil(()=>Volatile.Read(ref source.Opened)>0&&Volatile.Read(ref source.Opened)==Volatile.Read(ref source.Disposed),TimeSpan.FromSeconds(3)))
                throw new TimeoutException("Archive batch did not close");
        }
    }
    public static void IdleReuse(Action<bool,string> check)
    {
        var f=new Fixture();var source=f.Current;
        var first=Fixture.Prefab("missing1");var one=f.Finish(first);Fixture.Idle(source);
        var second=Fixture.Prefab("missing2");var two=f.Finish(second);Fixture.Idle(source);
        check(source.Opened==2&&source.Disposed==2,"Both actual worker batches close their own archive lifetime");
        check(source.Registries==1&&source.HookupScans==1,"A later worker reuses both parsed catalogs after the queue became empty");
        check(one.Issue?.Contains("Mautmodell fehlt")==true&&two.Issue?.Contains("Mautmodell fehlt")==true&&
            f.Evidence.Read(first).Count==0&&f.Evidence.Read(second).Count==0,
            "Catalog reuse grants neither payment nor clearance for an unknown model");
    }
    public static void SourceChange(Action<bool,string> check)
    {
        var f=new Fixture();var old=f.Current;
        using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
        old.Entered=entered;old.Continue=release;
        var first=Fixture.Prefab("missing1");f.Evidence.Read(first);
        try {
            check(entered.Wait(TimeSpan.FromSeconds(2)),"An old-source catalog load is genuinely in progress");
            f.Current=new("b");var next=Fixture.Prefab("missing2");f.Evidence.Read(next);
            release.Set();f.Finish(next);Fixture.Idle(old);Fixture.Idle(f.Current);
            check(old.Registries==1&&f.Current.Registries==1&&old.HookupScans==1&&f.Current.HookupScans==1,
                "Late old-source completion cannot populate the new source generation");
            f.Finish(Fixture.Prefab("missing3"));Fixture.Idle(f.Current);
            check(f.Current.Registries==1,"The new generation also survives its own idle restart");
            f.Current=old;old.Entered=null;old.Continue=null;
            f.Finish(Fixture.Prefab("missing4"));Fixture.Idle(old);
            check(old.Registries==2&&old.HookupScans==2,"Switching back to an earlier filesystem starts a new generation rather than reviving its old cache");
        } finally {release.Set();}
    }
    public static void IncompleteAndFailedLoads(Action<bool,string> check)
    {
        var f=new Fixture();var source=f.Current;source.BadHookup=true;
        var partial=f.Finish(Fixture.Prefab("missing1"));Fixture.Idle(source);
        check(partial.Issue?.Contains("Hookup-Katalog unvollständig")==true,"A failed hookup read rejects the whole catalog rather than returning partial definitions");
        source.BadHookup=false;
        f.Finish(Fixture.Prefab("missing2"));Fixture.Idle(source);
        f.Finish(Fixture.Prefab("missing3"));Fixture.Idle(source);
        check(source.Registries==2&&source.HookupScans==2,
            "An incomplete hookup catalog is retried by the next new job, and only a complete result is reused");
        var failed=new Fixture();var bad=failed.Current;bad.BadRegistry=true;
        var first=Fixture.Prefab("missing1");var result=failed.Finish(first);Fixture.Idle(bad);
        check(result.Issue?.Contains("Catalog source unavailable")==true,"Registry read failure remains a rejected model result");
        bad.BadRegistry=false;
        failed.Finish(Fixture.Prefab("missing2"));Fixture.Idle(bad);
        failed.Finish(Fixture.Prefab("missing3"));Fixture.Idle(bad);
        check(bad.Registries==2&&bad.HookupScans==1,"A failed catalog load never publishes a partial cached value");
        check(failed.Evidence.Diagnose(first).Issue==result.Issue,
            "Catalog reuse does not silently retry or authorize an already rejected model certificate");
    }
    public static void KnownResultsAndFailedSourceChange(Action<bool,string> check)
    {
        var f=new Fixture();var source=f.Current;source.Known=true;
        var unit=TruckLib.Sii.SiiFile.Open("/unit/hookup/fixture.sii",source).Units.Single();
        check(TollDefinition.Read(unit,false).Payment==TollPayment.Automatic,
            "The actual SII fixture must encode automatic payment: "+string.Join(", ",unit.Attributes.Select(p=>p.Key+"="+p.Value+" ("+((object?)p.Value)?.GetType().Name+")")));
        var first=Fixture.Prefab("known1");var second=Fixture.Prefab("known2");
        f.Finish(first);Fixture.Idle(source);
        f.Finish(second);Fixture.Idle(source);
        var a=f.Evidence.ReadInspection(first)!;var b=f.Evidence.ReadInspection(second)!;
        check(a.Issue==null&&b.Issue==null&&a.Payments.GetValueOrDefault(7)==TollPayment.Automatic&&
            b.Payments.GetValueOrDefault(7)==TollPayment.Automatic&&a.BlockedCurves is [false]&&b.BlockedCurves is [false],
            "A known model keeps actual automatic-payment and free-corridor results across idle batches: "+a.Issue+" / "+b.Issue);
        check(source.Registries==1&&source.HookupScans==1&&source.ModelReads==2,
            "Only immutable catalogs are shared; each requested model still gets its own actual inspection");
        f.Current=new("b"){Known=true,Manual=true,Blocked=true,FailPaths=true};
        bool threw=false;try{f.Evidence.Read(first);}catch(IOException){threw=true;}
        check(threw,"The source snapshot really fails once during the A to B transition");
        f.Finish(first);Fixture.Idle(f.Current);
        var changed=f.Evidence.ReadInspection(first)!;
        check(changed.Issue==null&&changed.Payments.GetValueOrDefault(7)==TollPayment.Manual&&changed.BlockedCurves is [true],
            "A failed then retried source change cannot borrow A's automatic payment or free geometry for B");
        check(f.Current.Registries==1&&f.Current.HookupScans==1&&f.Current.ModelReads==1,
            "The new source loads its own catalogs and model after a failed archive snapshot");
    }
    public static void MissingConflictingDefinition(Action<bool,string> check)
    {
        var f=new Fixture();var source=f.Current;source.Known=true;source.Duplicate=true;source.BadDuplicate=true;
        var first=Fixture.Prefab("known1");var result=f.Finish(first);Fixture.Idle(source);
        check(result.Issue?.Contains("Hookup-Katalog unvollständig")==true&&f.Evidence.Read(first).Count==0,
            "An unreadable contradictory definition cannot leave the first automatic-payment unit authoritative");
        source.BadDuplicate=false;
        var second=Fixture.Prefab("known2");f.Finish(second);Fixture.Idle(source);
        check(f.Evidence.Read(second).Count==0&&f.Evidence.ReadInspection(second)?.Issue!=null&&source.HookupScans==2,
            "A subsequent new job rereads the complete catalog and keeps its conflicting units ambiguous");
    }
}
