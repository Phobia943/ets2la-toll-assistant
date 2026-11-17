using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using System.Security.Cryptography;
using System.Threading;
using System.Globalization;
using System.Runtime.CompilerServices;
using ETS2LA.Game.Data;
using ETS2LA.Game.Utils;
using TruckLib;
using TruckLib.Models;
using TruckLib.Models.Ppd;
using TruckLib.Sii;

namespace MautAssistent;

public sealed class TollModelEvidence
{
    readonly Func<IFileSystem?> readFiles;
    readonly Func<bool>? readSpecialJob;
    readonly Func<ModelRequest,ModelInspection>? inspect;
    readonly Func<(float Height,float Width)>? readProfile;
    readonly object sync=new();
    IFileSystem? files;
    string[]? archives;
    readonly Dictionary<ModelKey,Entry> cache=[];
    readonly Queue<Job> pending=[];
    bool worker;
    sealed record ModelKey(string Model,string Variant,string Parts,bool SpecialJob,PrefabDescriptor Descriptor,float Height,float Width,string GeometryStamp);
    static long nextRevision;
    sealed class Entry(ModelKey key) {
        public ModelInspection? Result;
        public readonly object Diagnostic=new{revision=Interlocked.Increment(ref nextRevision),key.Model,key.Variant,key.Parts,key.Height,key.Width,key.GeometryStamp};
    }
    sealed record Job(IFileSystem Source,ModelRequest Request,Entry Entry);
    internal sealed record ModelRequest(string Model,string Variant,string[] Parts,bool SpecialJob,PrefabDescriptor Descriptor,string[] Archives,float Height=6,float Width=3);
    public sealed record ModelInspection(IReadOnlyDictionary<int,TollPayment> Payments,bool[] BlockedCurves)
    { public IReadOnlyDictionary<int,ManualStopZone> ManualStops {get;init;}=new Dictionary<int,ManualStopZone>();
      public string? Issue {get;init;}
      public bool HasDynamicParked {get;init;}
      public RigStaticGeometry? StaticGeometry {get;init;}
      public IReadOnlyList<ObstacleSphere> BoundedMovers {get;init;}=Array.Empty<ObstacleSphere>(); }
    public sealed record ModelDiagnostic(bool Completed,string? Issue,int PaymentCount,int BlockedCount)
    { public bool HasDynamicParked {get;init;} }
    static readonly IReadOnlyDictionary<int,TollPayment> Unknown = new Dictionary<int,TollPayment>();
    static readonly ModelInspection Rejected=new(Unknown,[]);
    static ModelInspection Reject(string issue)=>new(Unknown,[]){Issue=issue};
    static readonly Lazy<bool> cubicVerified=new(()=> {
        try {var assembly=typeof(HermiteSpline).Assembly;return assembly.ManifestModule.ModuleVersionId==new Guid("28ccd48e-1f6d-43d0-9e61-acba84ed7403")&&
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location)))=="728BB116B6161498A1C4D1F6095BC5CA3B0CC680091BA83DE06CDB33769E3697";}catch{return false;}
    });

    public TollModelEvidence(Func<IFileSystem?> readFiles,Func<bool>? readSpecialJob=null,Func<(float Height,float Width)>? readProfile=null)
        :this(readFiles,readSpecialJob,null,readProfile) { }
    internal TollModelEvidence(Func<IFileSystem?> readFiles,Func<bool>? readSpecialJob,Func<ModelRequest,ModelInspection>? inspect,Func<(float Height,float Width)>? readProfile=null)
    {this.readFiles=readFiles;this.readSpecialJob=readSpecialJob;this.inspect=inspect;this.readProfile=readProfile;}

    public IReadOnlyDictionary<int,TollPayment> Read(ParsedPrefab prefab)
    {
        var entry=GetEntry(prefab);
        return entry==null?Unknown:Volatile.Read(ref entry.Result)?.Payments ?? Unknown;
    }
    public void Prefetch(ParsedPrefab prefab)
    {
        if(prefab.Descriptor?.NavCurves.Any(c=>c.SemaphoreId>=0)==true)_=GetEntry(prefab);
    }
    internal ModelInspection? ReadInspection(ParsedPrefab prefab)
    {
        var entry=GetEntry(prefab);return entry==null?null:Volatile.Read(ref entry.Result);
    }
    public ModelDiagnostic Diagnose(ParsedPrefab prefab)
    {
        var entry=GetEntry(prefab);var result=entry==null?null:Volatile.Read(ref entry.Result);
        return new(result!=null,result?.Issue,result?.Payments.Count??0,result?.BlockedCurves.Count(b=>b)??0)
            {HasDynamicParked=result?.HasDynamicParked??false};
    }
    internal bool? ParkedRequirement(ParsedPrefab prefab)
    {
        // Attachment kind is structural, independent of the current clearance
        // height/width. A temporarily unavailable rig profile must not hide a
        // previously verified parked-vehicle requirement from the output guard.
        var current=readFiles();if(current==null||prefab.Descriptor==null)return null;
        lock(sync) {
            if(!ReferenceEquals(current,files))return null;
            string model=prefab.Prefab.Model.ToString(),variant=prefab.Prefab.Variant.ToString();
            string parts=string.Join('\u001f',prefab.Prefab.AdditionalParts.Select(p=>p.ToString()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
            string stamp=DescriptorStamp(prefab.Descriptor!);
            var known=cache.Where(p=>p.Key.Model==model&&p.Key.Variant==variant&&p.Key.Parts==parts&&p.Key.GeometryStamp==stamp&&
                ReferenceEquals(p.Key.Descriptor,prefab.Descriptor)&&p.Key.SpecialJob==(readSpecialJob?.Invoke()??false))
                .Select(p=>Volatile.Read(ref p.Value.Result)).Where(r=>r is {Issue:null}&&r.Payments.Count>0).ToArray();
            return known.Length==0?null:known.Any(r=>r!.HasDynamicParked);
        }
    }
    public ManualStopZone? ReadManualStop(ParsedPrefab prefab,int gate)
    {
        var entry=GetEntry(prefab);
        return entry==null?null:Volatile.Read(ref entry.Result)?.ManualStops.GetValueOrDefault(gate);
    }

    // STATIC infrastructure only. This result does not authorize passage past
    // dynamic parked bodies. The live output guard must separately prove the
    // current short scene corridor, including any HasDynamicParked prefab.
    public bool IsClear(PrefabPath input)
    {
        var path=PrefabRefinement.OriginalFor(input);
        var entry=GetEntry(path.Prefab);var result=entry==null?null:Volatile.Read(ref entry.Result);
        var descriptor=path.Prefab.Descriptor;
        if(result==null||descriptor==null||result.BlockedCurves.Length!=descriptor.NavCurves.Count||path.Curves.Count==0)return false;
        return path.Curves.All(curve=>descriptor.NavCurves.IndexOf(curve) is int i && i>=0 && !result.BlockedCurves[i]);
    }

    // Search within the proven free graph, rather than rejecting a gate after
    // testing just its shortest topological path. The final route is still
    // revalidated immediately before application and in the live output guard.
    public TollRouter.RouteSearch FindClearRoute(PrefabPath input,int gate)
    {
        var path=PrefabRefinement.OriginalFor(input);
        var entry=GetEntry(path.Prefab);var result=entry==null?null:Volatile.Read(ref entry.Result);
        if(result==null)return new(null,"ModelPending",[]){Evidence=entry?.Diagnostic};
        var descriptor=path.Prefab.Descriptor;
        if(result.Issue!=null||descriptor==null||result.BlockedCurves.Length!=descriptor.NavCurves.Count)return new(null,"ModelRejected",[]){Evidence=entry?.Diagnostic};
        var blocked=result.BlockedCurves.ToArray();
        // SCS documents Allowed Vehicles as AI traffic suitability, not a
        // player-truck restriction. Permit the small-vehicle preference ONLY
        // for model-proven automatic payment with a completed physical mask.
        // A low beam, narrow lane, unknown type or escort-only route remains
        // rejected. The default/no-proof router is deliberately unchanged.
        bool automatic=result.Payments.GetValueOrDefault(gate)==TollPayment.Automatic;
        var route=TollRouter.ViaGate(path,gate,blocked,automatic);
        return new(route,route!=null?"Available":TollRouter.ViaGate(path,gate,new bool[blocked.Length],automatic)==null?"NoDirectedTruckPath":"NoClearDirectedPath",
            Enumerable.Range(0,blocked.Length).Where(i=>blocked[i]).ToArray()){Evidence=entry?.Diagnostic};
    }

    Entry? GetEntry(ParsedPrefab prefab)
    {
        var current=readFiles();var descriptor=prefab.Descriptor;
        if(current==null||descriptor==null)return null;
        lock(sync) {
            if(!ReferenceEquals(current,files)) {files=current;archives=inspect==null?IndependentAssets.SnapshotPaths(current):[];cache.Clear();}
            if(archives==null)return null;
            var parts=prefab.Prefab.AdditionalParts.Select(p=>p.ToString()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var profile=readProfile?.Invoke()??(6f,3f);
            if(!float.IsFinite(profile.Item1)||!float.IsFinite(profile.Item2)||profile.Item1 is <3 or >8||profile.Item2 is <2 or >5)return null;
            var key=new ModelKey(prefab.Prefab.Model.ToString(),prefab.Prefab.Variant.ToString(),string.Join('\u001f',parts),readSpecialJob?.Invoke()??false,descriptor,profile.Item1,profile.Item2,DescriptorStamp(descriptor));
            cache.TryGetValue(key,out var existing);
            if(existing!=null&&Volatile.Read(ref existing.Result)!=null)return existing;
            var dominating=cache.Where(p=>p.Key.Model==key.Model&&p.Key.Variant==key.Variant&&p.Key.Parts==key.Parts&&
                p.Key.SpecialJob==key.SpecialJob&&ReferenceEquals(p.Key.Descriptor,key.Descriptor)&&p.Key.GeometryStamp==key.GeometryStamp&&
                p.Key.Height>=key.Height&&p.Key.Width>=key.Width&&Volatile.Read(ref p.Value.Result) is {Issue:null})
                .OrderBy(p=>p.Key.Height*p.Key.Width).Select(p=>p.Value).FirstOrDefault();
            if(existing!=null)return dominating??existing;
            // Bound pending heavy work; callers retry from discovery without waiting.
            if(pending.Count>=4)return dominating;
            if(cache.Count>=64) {
                var old=cache.FirstOrDefault(p=>Volatile.Read(ref p.Value.Result)!=null);
                if(old.Key==null)return dominating;
                cache.Remove(old.Key);
            }
            var copy=new PrefabDescriptor {NavCurves=descriptor.NavCurves.Select(CloneCurve).ToList()};
            // Manual trigger matching needs the same gate anchors as the live
            // descriptor. Copy values before dispatch; never read mutable map
            // objects from the model worker.
            foreach(var signal in descriptor.Semaphores)
                copy.Semaphores.Add(new(){SemaphoreId=signal.SemaphoreId,Position=signal.Position});
            var entry=new Entry(key);cache.Add(key,entry);
            pending.Enqueue(new(current,new(key.Model,key.Variant,parts,key.SpecialJob,copy,archives,key.Height,key.Width),entry));
            if(!worker) {worker=true;ThreadPool.QueueUserWorkItem(_=>RunWorker());}
            // A larger completed corridor can safely prove a smaller one.
            // Still schedule the exact smaller profile: a blocked larger box
            // must not permanently hide a lane that the smaller rig can use.
            return dominating??entry;
        }
    }

    static string DescriptorStamp(PrefabDescriptor descriptor)
    {
        using var bytes=new MemoryStream();using var writer=new BinaryWriter(bytes,Encoding.UTF8,true);
        void V(Vector3 v){writer.Write(v.X);writer.Write(v.Y);writer.Write(v.Z);}
        void Q(Quaternion q){writer.Write(q.X);writer.Write(q.Y);writer.Write(q.Z);writer.Write(q.W);}
        writer.Write(descriptor.NavCurves.Count);
        foreach(var c in descriptor.NavCurves) {
            V(c.StartPosition);V(c.EndPosition);Q(c.StartRotation);Q(c.EndRotation);writer.Write(c.Length);writer.Write(c.SemaphoreId);
            writer.Write((uint)c.AllowedVehicles);writer.Write(c.TrafficRule.ToString());
            writer.Write(c.NextLines.Count);foreach(var n in c.NextLines)writer.Write(n);
            writer.Write(c.PreviousLines.Count);foreach(var n in c.PreviousLines)writer.Write(n);
        }
        writer.Write(descriptor.Semaphores.Count);foreach(var s in descriptor.Semaphores){writer.Write(s.SemaphoreId);V(s.Position);}
        writer.Flush();return Convert.ToHexString(SHA256.HashData(bytes.ToArray()));
    }

    static NavCurve CloneCurve(NavCurve c)
    {
        var copy=new NavCurve {StartPosition=c.StartPosition,EndPosition=c.EndPosition,StartRotation=c.StartRotation,
            EndRotation=c.EndRotation,Length=c.Length,SemaphoreId=c.SemaphoreId,AllowedVehicles=c.AllowedVehicles,TrafficRule=c.TrafficRule};
        foreach(var next in c.NextLines)copy.NextLines.Add(next);
        foreach(var previous in c.PreviousLines)copy.PreviousLines.Add(previous);
        return copy;
    }

    void RunWorker()
    {
        IndependentAssets? owned=null;IFileSystem? source=null;Dictionary<string,string>? models=null;HashSet<string>? nonSolid=null;
        Dictionary<string,Unit>? tollUnits=null;
        StaticHookupGeometry? childGeometry=null;
        try {
            while(true) {
                Job job;
                lock(sync) {if(pending.Count==0){worker=false;return;}job=pending.Dequeue();}
                ModelInspection result;
                try {
                    if(inspect!=null)result=inspect(job.Request);
                    else {
                        if(!ReferenceEquals(source,job.Source)) {
                            owned?.Dispose();owned=new(job.Request.Archives);source=job.Source;models=ReadModels(owned.Files);
                            (nonSolid,tollUnits)=ReadHookupDefinitions(owned);
                            childGeometry=new(owned.Files,tollUnits,nonSolid);
                        }
                        var request=job.Request;
                        if(!models!.TryGetValue(request.Model,out var pmd))result=Reject("Mautmodell fehlt im Definitionskatalog: "+request.Model);
                        else {
                            var model=Model.Open(pmd,owned!.Files);
                            childGeometry!.BeginInspection();
                            result=InspectModel(request.Descriptor,model,request.Variant,request.Parts,
                                owned.Files.ReadAllBytes(Path.ChangeExtension(pmd,".pmg")),request.SpecialJob,nonSolid,tollUnits,childGeometry.Read,request.Height,request.Width,()=>childGeometry.Movers)
                                with {HasDynamicParked=childGeometry.HasDynamicParked};
                        }
                    }
                } catch(Exception ex) {
                    result=Reject("Modellprüfung: "+ex.GetType().Name+": "+ex.Message);
                    try {ETS2LA.Logging.Logger.Warn("Maut-Modellprüfung nicht möglich: "+ex.Message);} catch { }
                }
                lock(sync) {
                    Volatile.Write(ref job.Entry.Result,result);
                    while(cache.Values.Select(e=>Volatile.Read(ref e.Result)?.StaticGeometry).Where(g=>g!=null).Distinct().Sum(g=>g!.Count)>1000000) {
                        var old=cache.FirstOrDefault(p=>!ReferenceEquals(p.Value,job.Entry)&&Volatile.Read(ref p.Value.Result)!=null);
                        if(old.Key==null)break;cache.Remove(old.Key);
                    }
                }
            }
        } finally {try {owned?.Dispose();}catch { }}
    }

    public static ModelInspection InspectModel(PrefabDescriptor descriptor,Model model,string variant,string[] extraParts,byte[] pmg,bool specialJob=false,
        IReadOnlySet<string>? nonSolidHookups=null,IReadOnlyDictionary<string,Unit>? tollUnits=null,
        Func<string,Locator,IReadOnlyList<(Part Part,Matrix4x4 Transform)>>? readChildren=null,float height=6,float width=3,
        Func<IReadOnlyList<ObstacleSphere>>? readBoundedMovers=null)
    {
        var variants=model.Variants.Where(v=>v.Name.ToString()==variant).ToArray();
        if(variants.Length!=1||variants[0].Attributes.Count!=model.Parts.Count||
            variants[0].Attributes.Any(a=>a.Tag.ToString()!="visible"||a.Value>1)||
            model.Parts.GroupBy(p=>p.Name).Any(g=>g.Count()>1)||
            extraParts.Any(name=>!model.Parts.Any(p=>p.Name.ToString()==name)))return Reject("Unbekannte Modellvariante, Sichtbarkeit oder Zusatzteile");
        // Additional parts enable named parts in addition to the variant mask.
        // Their names do not establish payment type or obstacle clearance.
        var visible=model.Parts.Where((p,i)=>variants[0].Attributes[i].Value==1||extraParts.Contains(p.Name.ToString(),StringComparer.Ordinal)).ToArray();
        var children=new List<(Part,Matrix4x4)>();
        foreach(var locator in visible.SelectMany(p=>p.Locators)) {
            if(locator.HookupOffset==-1)continue;
            // Unknown child hook-ups can instantiate geometry not in this PMG.
            // Do not silently treat an uninspected child model as empty space.
            var hookup=ReadHookup(pmg,locator.HookupOffset);
            if(tollUnits!=null) {
                if(hookup==""||hookup!=null&&IsNonSolidReference(hookup,nonSolidHookups,tollUnits))continue;
                if(hookup!=null&&tollUnits.TryGetValue(hookup,out var unit)&&TollDefinition.Read(unit,specialJob).Known)continue;
                if(hookup!=null&&readChildren!=null) {children.AddRange(readChildren(hookup,locator));continue;}
                return Reject("Nicht geprüfter Modell-Hookup: "+(hookup??"ungültiger Verweis"));
            }
            if(hookup is not ("" or "tollgate.autopass.custom" or "tollgate.autopass.custom.wide" or
                "tollgate.autopass.oversize" or "tollgate.autopass.ovs_disable" or "tollgate.autopass.ovs_disable.long" or
                "tollgate.manual.ovs_disable" or "tollgate.custom") && (hookup==null||nonSolidHookups?.Contains(hookup)!=true))return Reject("Nicht geprüfter Modell-Hookup: "+(hookup??"ungültiger Verweis"));
        }
        var staticGeometry=ModelClearance.CaptureGeometry(visible,children);
        var blocked=ModelClearance.BlockedCurves(descriptor,visible,height,width,children,staticGeometry);
        var movers=readBoundedMovers?.Invoke()?.ToArray()??[];
        if(movers.Length>0) {
            if(!cubicVerified.Value)return Reject("Ungeprüfte Maut-Splineformel");
            for(int i=0;i<blocked.Length;i++) {
                var curve=descriptor.NavCurves[i];
                Vector3 At(float t)=>PrefabUtils.InterpolateNavCurve(curve,t);
                var hull=CubicSeparation.FromCubicSamples(At(0),At(1f/3),At(2f/3),At(1));
                blocked[i]|=movers.Any(s=>!CubicSeparation.Outside(hull.A,hull.B,hull.C,hull.D,s,width/2));
            }
        }
        var markers=visible.SelectMany(p=>p.Locators).Select(l=> {
            var hook=ReadHookup(pmg,l.HookupOffset);
            var payment=tollUnits==null?DecodeHookup(pmg,l.HookupOffset,specialJob):
                hook!=null&&tollUnits.TryGetValue(hook,out var unit)?TollDefinition.Read(unit,specialJob).Payment:TollPayment.Unknown;
            return (l.Position,Payment:payment);
        })
            .Where(l=>l.Payment!=TollPayment.Unknown);
        return new(Match(descriptor,markers),blocked) {
            StaticGeometry=staticGeometry,BoundedMovers=Array.AsReadOnly(movers),
            ManualStops=tollUnits==null?new Dictionary<int,ManualStopZone>():
                ManualTriggerEvidence.Read(descriptor,visible,pmg,tollUnits,specialJob)
        };
    }
    internal static bool IsNonSolidHookup(Unit unit)
    {
        if(unit.Attributes.Count==0)return false;
        if(unit.Class=="env_area")return unit.Attributes.All(p=>p.Key is "size_x" or "size_y"&&Number((object?)p.Value) is >0 and <100000);
        if(unit.Class=="speed_camera")return unit.Attributes.Count==2&&
            unit.Attributes.TryGetValue("trigger_distance",out var trigger)&&Number((object?)trigger) is >0 and <=10000&&
            unit.Attributes.TryGetValue("measurement_distance",out var measurement)&&Number((object?)measurement) is >=0 and <=10000;
        if(unit.Class=="sound_hookup")return unit.Attributes.Count==2&&
            unit.Attributes.TryGetValue("trigger_distance",out var distance)&&Number((object?)distance) is >0 and <=10000&&
            unit.Attributes.TryGetValue("sound_path",out var path)&&(object?)path is string sound&&sound.Length is >10 and <=256&&
            sound.StartsWith("/sound/",StringComparison.Ordinal)&&sound.EndsWith(".soundref",StringComparison.Ordinal)&&
            !sound.Contains("//")&&!sound.Split('/').Any(p=>p is "." or "..")&&sound.All(c=>char.IsAsciiLetterOrDigit(c)||c is '/' or '_' or '.' or '-');
        if(unit.Class!="light_source")return false;
        foreach(var pair in unit.Attributes) {
            object? value=pair.Value;
            bool valid=pair.Key switch {
                "setup"=>value is LinkPointer p&&p.Value is "lumen_hue_saturation" or "lux_hue_saturation" or "candela_hue_saturation",
                "ambient_color" or "diffuse_color" or "specular_color"=>Color(value),
                "type"=>value is Token t&&t.ToString() is "point" or "spot",
                "cut_direction"=>value is Token d&&d.ToString() is "vertical" or "horizontal",
                "range" or "cut_range" or "fade_distance" or "fade_span"=>Number(value) is >=0 and <=100000,
                "inner_angle" or "outer_angle"=>Number(value) is >=0 and <=360,
                _=>false
            };
            if(!valid)return false;
        }
        return true;
    }
    static double Number(object? value)=>value switch {int n=>n,uint n=>n,long n=>n,ulong n=>n,float n=>n,double n=>n,_=>double.NaN};
    static bool Color(object? value)
    {
        if(value is Vector3 v)return float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z)&&v.X>=0&&v.Y>=0&&v.Z>=0&&v.X<=1000000&&v.Y<=1000000&&v.Z<=1000000;
        return value is ITuple {Length:3} tuple&&Enumerable.Range(0,3).All(i=>Number(tuple[i]) is >=0 and <=1000000);
    }
    internal static bool IsNonSolidReference(string hook,IReadOnlySet<string>? known,IReadOnlyDictionary<string,Unit> units)
    {
        if(known?.Contains(hook)==true)return true;
        int separator=hook.IndexOf('#');
        if(separator<=0||hook.LastIndexOf('#')!=separator||!units.TryGetValue(hook[..separator],out var unit)||unit.Class!="light_source"||!IsNonSolidHookup(unit))return false;
        var seen=new HashSet<string>(StringComparer.Ordinal);var parameters=hook[(separator+1)..].Split(';');
        if(parameters.Length!=2)return false;
        foreach(string parameter in parameters) {
            var pair=parameter.Split(':');
            if(pair.Length!=2||pair[0] is not ("height" or "target")||!seen.Add(pair[0])||
                !double.TryParse(pair[1],NumberStyles.AllowLeadingSign|NumberStyles.AllowDecimalPoint|NumberStyles.AllowExponent,CultureInfo.InvariantCulture,out double number)||
                !double.IsFinite(number)||number is <0 or >1000)return false;
        }
        return true;
    }
    static (HashSet<string>,Dictionary<string,Unit>) ReadHookupDefinitions(IndependentAssets assets)
    {
        var known=new Dictionary<string,bool>(StringComparer.Ordinal);
        var tolls=new Dictionary<string,Unit>(StringComparer.Ordinal);
        foreach(var file in assets.FilesBelow("/unit/hookup").Where(p=>p.EndsWith(".sii",StringComparison.Ordinal))) {
            SiiFile definitions;try {definitions=SiiFile.Open(file,assets.Files);}catch {continue;}
            foreach(var unit in definitions.Units) {
                bool harmless=IsNonSolidHookup(unit);
                known[unit.Name]=harmless && (!known.TryGetValue(unit.Name,out bool previous)||previous);
                if(tolls.TryGetValue(unit.Name,out var prior)&&
                    (prior.Class!=unit.Class||prior.Attributes.Count!=unit.Attributes.Count||unit.Attributes.Any(p=>
                        !prior.Attributes.TryGetValue(p.Key,out var value)||!Equals((object?)value,(object?)p.Value))))
                    tolls[unit.Name]=new Unit("unsupported","ambiguous");
                else tolls.TryAdd(unit.Name,unit);
            }
        }
        return (known.Where(p=>p.Value).Select(p=>p.Key).ToHashSet(StringComparer.Ordinal),
            tolls);
    }
    internal static Dictionary<string,string> ReadModels(IFileSystem files)
    {
        var models = new Dictionary<string,string>();
        foreach (var path in files.GetFiles("/def/world/").Where(f => f.StartsWith("/def/world/prefab.") && f.EndsWith(".sii")))
        {
            var definitions = SiiFile.Open(path,files);
            foreach (var unit in definitions.Units)
            {
                if (!unit.Attributes.TryGetValue("model_desc", out var model)) continue;
                string? pmd = ((object?)model)?.ToString()?.Trim('"');
                // Prefab definitions are the registry, not a directory convention.
                // Older tolls live under /prefab/tollgate and Scandinavian tolls
                // directly under /prefab2. Payment and clearance are still proven
                // from the requested model; accepting a path grants no lane.
                if (pmd==null||!IsModelPath(pmd)) continue;
                models.TryAdd(unit.Name.Split('.').Last(), pmd);
            }
        }
        return models;
    }

    internal static bool IsModelPath(string? path) => path is {Length: > 5} &&
        path.StartsWith('/') && path.EndsWith(".pmd",StringComparison.Ordinal) &&
        !path.Contains('\\') && !path.Split('/').Any(p=>p is ".." or ".");

    // PMG v21: locator offsets point into the null-terminated hookup table.
    public static TollPayment DecodeHookup(ReadOnlySpan<byte> pmg, int offset,bool specialJob=false)
        => ReadHookup(pmg,offset) switch
        {
            "tollgate.autopass.custom" or "tollgate.autopass.custom.wide" => TollPayment.Automatic,
            "tollgate.autopass.oversize" when specialJob => TollPayment.Automatic,
            "tollgate.autopass.ovs_disable" or "tollgate.autopass.ovs_disable.long" when !specialJob => TollPayment.Automatic,
            "tollgate.manual.ovs_disable" when !specialJob => TollPayment.Manual,
            "tollgate.custom" => TollPayment.Manual,
            _ => TollPayment.Unknown
        };

    internal static string? ReadHookup(ReadOnlySpan<byte> pmg,int offset)
    {
        if (pmg.Length < 96 || pmg[0] != 21 || offset < 0) return null;
        int table = BinaryPrimitives.ReadInt32LittleEndian(pmg[88..]);
        int length = BinaryPrimitives.ReadInt32LittleEndian(pmg[92..]);
        if (table < 96 || length <= offset || table > pmg.Length || length > pmg.Length - table)
            return null;
        if (offset != 0 && pmg[table + offset - 1] != 0) return null;
        var tail = pmg.Slice(table + offset, length - offset);
        int end = tail.IndexOf((byte)0);
        if(end is <0 or >256||tail[..end].ContainsAnyExceptInRange((byte)32,(byte)126))return null;
        return Encoding.ASCII.GetString(tail[..end]);
    }

    public static IReadOnlyDictionary<int,TollPayment> Match(PrefabDescriptor descriptor,
        IEnumerable<(Vector3 Position, TollPayment Payment)> markers)
    {
        var curves = descriptor.NavCurves;
        // Some prefabs contain several signal objects for the same physical
        // booth. Payment identity follows the directed lane geometry, not the
        // count of roadside objects sharing a numeric signal ID.
        var booths=new List<List<NavCurve>>();
        var boothForCurve=new Dictionary<NavCurve,int>();
        foreach(var curve in curves.Where(c=>c.SemaphoreId>=0))
        {
            int booth=booths.FindIndex(g=>g[0].SemaphoreId==curve.SemaphoreId && g.All(c=>TollSignals.SameBooth(c,curve)));
            if(booth<0) {booth=booths.Count;booths.Add([]);}
            booths[booth].Add(curve);boothForCurve[curve]=booth;
        }
        int? GateAhead(int index)
        {
            var visited = new HashSet<int>();
            while (index >= 0 && index < curves.Count && visited.Add(index))
            {
                var curve = curves[index];
                if (curve.SemaphoreId >= 0) return boothForCurve.GetValueOrDefault(curve);
                if (curve.NextLines.Count != 1) return null;
                index = curve.NextLines[0];
            }
            return null;
        }
        var paths = curves.Select((curve, i) => (curve, Gate: GateAhead(i))).ToArray();
        var evidence = new Dictionary<int, HashSet<TollPayment>>();
        foreach (var marker in markers)
        {
            if (marker.Payment == TollPayment.Unknown) continue;
            var near = paths.Select(path =>
            {
                int count = Math.Clamp((int)Math.Ceiling(path.curve.Length * 2), 4, 600);
                float distance = Enumerable.Range(0, count + 1).Min(i => Vector3.Distance(
                    PrefabUtils.InterpolateNavCurveOriented(path.curve, (float)i/count).Position, marker.Position));
                return (path.Gate, distance);
            }).Where(x => x.distance < 2.4f).OrderBy(x => x.distance).ToArray();
            if (near.Length == 0 || near[0].distance > 1.2f || near[0].Gate is not int gate ||
                near.Any(x => x.Gate != gate && x.distance - near[0].distance < .85f)) continue;
            if (!evidence.TryGetValue(gate, out var kinds)) evidence[gate] = kinds = new();
            kinds.Add(marker.Payment);
        }
        var result=new Dictionary<int,TollPayment>();
        foreach(var id in booths.Select((curves,index)=>(Gate:curves[0].SemaphoreId,Index:index)).GroupBy(b=>b.Gate))
        {
            var kinds=id.Select(b=>evidence.TryGetValue(b.Index,out var values)&&values.Count==1?values.Single():TollPayment.Unknown).ToArray();
            if(kinds.All(k=>k!=TollPayment.Unknown)&&kinds.Distinct().Count()==1)result[id.Key]=kinds[0];
        }
        return result;
    }
}
