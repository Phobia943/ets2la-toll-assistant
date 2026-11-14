using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using System.Threading;
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
    readonly object sync=new();
    IFileSystem? files;
    string[]? archives;
    readonly Dictionary<ModelKey,Entry> cache=[];
    readonly Queue<Job> pending=[];
    bool worker;
    sealed record ModelKey(string Model,string Variant,string Parts,bool SpecialJob,PrefabDescriptor Descriptor);
    sealed class Entry {public ModelInspection? Result;}
    sealed record Job(IFileSystem Source,ModelRequest Request,Entry Entry);
    internal sealed record ModelRequest(string Model,string Variant,string[] Parts,bool SpecialJob,PrefabDescriptor Descriptor,string[] Archives);
    public sealed record ModelInspection(IReadOnlyDictionary<int,TollPayment> Payments,bool[] BlockedCurves)
    { public IReadOnlyDictionary<int,ManualStopZone> ManualStops {get;init;}=new Dictionary<int,ManualStopZone>(); }
    static readonly IReadOnlyDictionary<int,TollPayment> Unknown = new Dictionary<int,TollPayment>();
    static readonly ModelInspection Rejected=new(Unknown,[]);

    public TollModelEvidence(Func<IFileSystem?> readFiles,Func<bool>? readSpecialJob=null)
        :this(readFiles,readSpecialJob,null) { }
    internal TollModelEvidence(Func<IFileSystem?> readFiles,Func<bool>? readSpecialJob,Func<ModelRequest,ModelInspection>? inspect)
    {this.readFiles=readFiles;this.readSpecialJob=readSpecialJob;this.inspect=inspect;}

    public IReadOnlyDictionary<int,TollPayment> Read(ParsedPrefab prefab)
    {
        var entry=GetEntry(prefab);
        return entry==null?Unknown:Volatile.Read(ref entry.Result)?.Payments ?? Unknown;
    }
    public void Prefetch(ParsedPrefab prefab)
    {
        if(prefab.Descriptor?.NavCurves.Any(c=>c.SemaphoreId>=0)==true)_=GetEntry(prefab);
    }
    public ManualStopZone? ReadManualStop(ParsedPrefab prefab,int gate)
    {
        var entry=GetEntry(prefab);
        return entry==null?null:Volatile.Read(ref entry.Result)?.ManualStops.GetValueOrDefault(gate);
    }

    public bool IsClear(PrefabPath input)
    {
        var path=PrefabRefinement.OriginalFor(input);
        var entry=GetEntry(path.Prefab);var result=entry==null?null:Volatile.Read(ref entry.Result);
        var descriptor=path.Prefab.Descriptor;
        if(result==null||descriptor==null||result.BlockedCurves.Length!=descriptor.NavCurves.Count||path.Curves.Count==0)return false;
        return path.Curves.All(curve=>descriptor.NavCurves.IndexOf(curve) is int i && i>=0 && !result.BlockedCurves[i]);
    }

    Entry? GetEntry(ParsedPrefab prefab)
    {
        var current=readFiles();var descriptor=prefab.Descriptor;
        if(current==null||descriptor==null)return null;
        lock(sync) {
            if(!ReferenceEquals(current,files)) {files=current;archives=inspect==null?IndependentAssets.SnapshotPaths(current):[];cache.Clear();}
            if(archives==null)return null;
            var parts=prefab.Prefab.AdditionalParts.Select(p=>p.ToString()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var key=new ModelKey(prefab.Prefab.Model.ToString(),prefab.Prefab.Variant.ToString(),string.Join('\u001f',parts),readSpecialJob?.Invoke()??false,descriptor);
            if(cache.TryGetValue(key,out var existing))return existing;
            // Bound pending heavy work; callers retry from discovery without waiting.
            if(pending.Count>=4)return null;
            if(cache.Count>=64) {
                var old=cache.FirstOrDefault(p=>Volatile.Read(ref p.Value.Result)!=null);
                if(old.Key==null)return null;
                cache.Remove(old.Key);
            }
            var copy=new PrefabDescriptor {NavCurves=descriptor.NavCurves.Select(CloneCurve).ToList()};
            var entry=new Entry();cache.Add(key,entry);
            pending.Enqueue(new(current,new(key.Model,key.Variant,parts,key.SpecialJob,copy,archives),entry));
            if(!worker) {worker=true;ThreadPool.QueueUserWorkItem(_=>RunWorker());}
            return entry;
        }
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
                        }
                        var request=job.Request;
                        if(!models!.TryGetValue(request.Model,out var pmd))result=Rejected;
                        else {
                            var model=Model.Open(pmd,owned!.Files);
                            result=InspectModel(request.Descriptor,model,request.Variant,request.Parts,
                                owned.Files.ReadAllBytes(Path.ChangeExtension(pmd,".pmg")),request.SpecialJob,nonSolid,tollUnits);
                        }
                    }
                } catch(Exception ex) {
                    result=Rejected;
                    try {ETS2LA.Logging.Logger.Warn("Maut-Modellprüfung nicht möglich: "+ex.Message);} catch { }
                }
                Volatile.Write(ref job.Entry.Result,result);
            }
        } finally {try {owned?.Dispose();}catch { }}
    }

    public static ModelInspection InspectModel(PrefabDescriptor descriptor,Model model,string variant,string[] extraParts,byte[] pmg,bool specialJob=false,
        IReadOnlySet<string>? nonSolidHookups=null,IReadOnlyDictionary<string,Unit>? tollUnits=null)
    {
        var variants=model.Variants.Where(v=>v.Name.ToString()==variant).ToArray();
        if(variants.Length!=1||variants[0].Attributes.Count!=model.Parts.Count||
            variants[0].Attributes.Any(a=>a.Tag.ToString()!="visible"||a.Value>1)||
            model.Parts.GroupBy(p=>p.Name).Any(g=>g.Count()>1)||
            extraParts.Any(name=>!model.Parts.Any(p=>p.Name.ToString()==name)))return Rejected;
        // Additional parts enable named parts in addition to the variant mask.
        // Their names do not establish payment type or obstacle clearance.
        var visible=model.Parts.Where((p,i)=>variants[0].Attributes[i].Value==1||extraParts.Contains(p.Name.ToString(),StringComparer.Ordinal)).ToArray();
        var blocked=ModelClearance.BlockedCurves(descriptor,visible);
        foreach(var locator in visible.SelectMany(p=>p.Locators)) {
            if(locator.HookupOffset==-1)continue;
            // Unknown child hook-ups can instantiate geometry not in this PMG.
            // Do not silently treat an uninspected child model as empty space.
            var hookup=ReadHookup(pmg,locator.HookupOffset);
            if(tollUnits!=null) {
                if(hookup==""||hookup!=null&&nonSolidHookups?.Contains(hookup)==true)continue;
                if(hookup!=null&&tollUnits.TryGetValue(hookup,out var unit)&&TollDefinition.Read(unit,specialJob).Known)continue;
                return Rejected;
            }
            if(hookup is not ("" or "tollgate.autopass.custom" or "tollgate.autopass.custom.wide" or
                "tollgate.autopass.oversize" or "tollgate.autopass.ovs_disable" or "tollgate.autopass.ovs_disable.long" or
                "tollgate.manual.ovs_disable" or "tollgate.custom") && (hookup==null||nonSolidHookups?.Contains(hookup)!=true))return Rejected;
        }
        var markers=visible.SelectMany(p=>p.Locators).Select(l=> {
            var hook=ReadHookup(pmg,l.HookupOffset);
            var payment=tollUnits==null?DecodeHookup(pmg,l.HookupOffset,specialJob):
                hook!=null&&tollUnits.TryGetValue(hook,out var unit)?TollDefinition.Read(unit,specialJob).Payment:TollPayment.Unknown;
            return (l.Position,Payment:payment);
        })
            .Where(l=>l.Payment!=TollPayment.Unknown);
        return new(Match(descriptor,markers),blocked) {
            ManualStops=tollUnits==null?new Dictionary<int,ManualStopZone>():
                ManualTriggerEvidence.Read(descriptor,visible,pmg,tollUnits,specialJob)
        };
    }
    internal static bool IsNonSolidHookup(Unit unit)
    {
        string[] keys=unit.Class switch {
            "light_source"=>["setup","ambient_color","diffuse_color","type","range"],
            "env_area"=>["size_x","size_y"],
            _=>[]
        };
        return keys.Length>0 && unit.Attributes.Count>0 && unit.Attributes.Keys.All(k=>keys.Contains(k,StringComparer.Ordinal));
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
            tolls.Where(p=>p.Value.Class=="tollgate").ToDictionary(p=>p.Key,p=>p.Value,StringComparer.Ordinal));
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
        return end is <0 or >64?null:Encoding.ASCII.GetString(tail[..end]);
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
