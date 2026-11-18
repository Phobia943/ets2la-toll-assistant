using System.Numerics;
using System.Reflection;
using TruckLib;
using TruckLib.ScsMap;
using Model=TruckLib.Models.Model;
using MapModel=TruckLib.ScsMap.Model;

namespace MautAssistent;

// Optional positive map-object evidence. Missing/partial results are not a
// clearance certificate and do not by themselves justify a vehicle stop.
internal sealed class StaticRoadSceneSource
{
    internal const float RegionRadius=300,ReuseDistance=150,RefreshDistance=100;
    internal sealed record MapSelection(string BaseMapName,bool ForceBaseMapName,string? ExplicitMapName=null);
    internal sealed record Body(ulong Uid,RigStaticGeometry Geometry,Vector3 Minimum,Vector3 Maximum);
    internal sealed record Snapshot(long Generation,string MapName,Vector3 RegionCenter,float Radius,
        IReadOnlyList<Body> Bodies,int UnknownCount,string? Issue)
    { internal IReadOnlyList<MapGateArea> Gates {get;init;}=Array.Empty<MapGateArea>();
      internal MapSelection? Selection {get;init;} internal double BuildMilliseconds {get;init;} internal long AllocatedBytes {get;init;}
      internal IReadOnlyDictionary<string,long>? PhaseAllocations {get;init;} }
    internal sealed record Request(long Generation,IFileSystem Source,MapSelection Selection,Vector3 Center,string[] Archives);
    readonly Func<IFileSystem?> readFiles;
    readonly Func<MapSelection?> readMap;
    readonly Func<Request,Snapshot> load;
    readonly Func<IFileSystem,string[]?> snapshotPaths;
    readonly Action<Action> schedule;
    readonly Action? cleanup;
    readonly Func<double> seconds;
    readonly object sync=new();
    IFileSystem? source;
    MapSelection? selection;
    long generation;
    Vector3 latestPosition;
    Vector3? requestedCenter;
    Request? pending;
    Snapshot? completed;
    bool worker;
    double retryAt=double.PositiveInfinity;
    int failures;
    string status="Statische Straßenobjekte noch nicht geladen.";
    internal string Status {get {lock(sync)return status;}}
    internal StaticRoadSceneSource(Func<IFileSystem?> files,Func<MapSelection?> map)
        :this(files,map,IndependentAssets.SnapshotPaths) {}
    internal StaticRoadSceneSource(Func<IFileSystem?> files,Func<MapSelection?> map,Func<IFileSystem,string[]?> paths)
    {
        readFiles=files;readMap=map;var session=new Loader();load=session.Load;cleanup=session.Dispose;
        schedule=action=>new Thread(()=>action()){IsBackground=true,Name="ETS2LA static road models",Priority=ThreadPriority.BelowNormal}.Start();
        snapshotPaths=paths;seconds=()=>System.Diagnostics.Stopwatch.GetTimestamp()/(double)System.Diagnostics.Stopwatch.Frequency;
    }
    internal StaticRoadSceneSource(Func<IFileSystem?> files,Func<MapSelection?> map,Func<Request,Snapshot> loader,
        Action<Action> scheduler,Func<IFileSystem,string[]?> paths,Func<double>? time=null)
    {readFiles=files;readMap=map;load=loader;schedule=scheduler;snapshotPaths=paths;seconds=time??(()=>System.Diagnostics.Stopwatch.GetTimestamp()/(double)System.Diagnostics.Stopwatch.Frequency);}

    // Called on the plugin thread. No archive reads, model work or mesh walks.
    internal Snapshot? Read(Vector3 position)
    {
        IFileSystem? current;MapSelection? chosen;
        try{current=readFiles();chosen=NormalizeSelection(readMap());}catch{Reset();return null;}
        bool start=false;Snapshot? result;
        lock(sync) {
            if(!ReferenceEquals(current,source)||chosen!=selection) {
                generation++;source=current;selection=chosen;completed=null;pending=null;requestedCenter=null;failures=0;retryAt=double.PositiveInfinity;
            }
            latestPosition=position;
            if(current==null||chosen==null||!Finite(position)) {
                completed=null;pending=null;requestedCenter=null;generation++;
                status="Statische Straßenobjekte ohne aktuelle Kartenquelle.";return null;
            }
            result=completed is { } ready&&ready.Generation==generation&&ready.Selection==chosen&&
                Vector3.Distance(position,ready.RegionCenter)<=ReuseDistance?ready:null;
            if(requestedCenter is not Vector3 anchor||Vector3.Distance(position,anchor)>RefreshDistance||seconds()>=retryAt) {
                // SnapshotPaths only copies immutable archive names; a worker
                // must never seek, extract or enumerate a host stream.
                string[]? paths;try{paths=snapshotPaths(current);}catch{paths=null;}requestedCenter=position;
                if(paths==null||paths.Length is <1 or >256) {
                    pending=null;completed=null;retryAt=seconds()+Math.Min(30,5*++failures);status="Statische Straßenquelle nicht unabhängig lesbar; erneute Prüfung folgt.";return null;
                }
                pending=new(generation,current,chosen,position,paths.ToArray());retryAt=double.PositiveInfinity;
                status="Statische Straßenobjekte werden im Hintergrund gelesen.";
                if(!worker){worker=true;start=true;}
            }
        }
        if(start)schedule(Run);
        return result;
    }
    internal void Reset()
    {
        bool start=false;
        lock(sync){generation++;source=null;selection=null;pending=null;completed=null;requestedCenter=null;failures=0;retryAt=double.PositiveInfinity;status="Statische Straßenquelle zurückgesetzt.";
            if(cleanup!=null&&!worker){worker=true;start=true;}}
        if(start)schedule(Run);
    }
    internal bool IsCurrent(Snapshot snapshot,Vector3 position)
    {
        IFileSystem? current;MapSelection? chosen;
        try{current=readFiles();chosen=NormalizeSelection(readMap());}catch{current=null;chosen=null;}
        lock(sync) {
            if(!ReferenceEquals(current,source)||chosen!=selection||current==null||chosen==null) {
                generation++;source=current;selection=chosen;completed=null;pending=null;requestedCenter=null;return false;
            }
            return ReferenceEquals(snapshot,completed)&&snapshot.Generation==generation&&snapshot.Selection==chosen&&Finite(position)&&
                Vector3.Distance(position,snapshot.RegionCenter)<=ReuseDistance;
        }
    }
    void Run()
    {
        for(;;) {
            Request? request;bool clear;
            lock(sync){request=pending;pending=null;clear=source==null;if(request==null&&!clear){worker=false;return;}}
            if(request==null){cleanup?.Invoke();lock(sync){if(pending==null){worker=false;return;}}continue;}
            Snapshot result;bool failed=false;
            try {result=load(request);}
            catch(Exception e){cleanup?.Invoke();failed=true;result=new(request.Generation,"unknown",request.Center,RegionRadius,Array.Empty<Body>(),1,"Statische Straßenquelle: "+e.GetType().Name);}
            lock(sync) {
                if(request.Generation!=generation||!ReferenceEquals(request.Source,source)||request.Selection!=selection||
                    Vector3.Distance(request.Center,latestPosition)>ReuseDistance)continue;
                // The immutable geometry container is built before publication.
                // Do not let a custom loader change the request's source stamp.
                completed=result with {Generation=request.Generation,Selection=request.Selection,RegionCenter=request.Center,Radius=RegionRadius,
                    Bodies=Array.AsReadOnly(result.Bodies.Take(257).ToArray()),Gates=Array.AsReadOnly(result.Gates.Take(65).ToArray())};
                if(completed.Bodies.Count>256)completed=completed with {Bodies=Array.Empty<Body>(),Issue="Zu viele statische Straßenobjekte",UnknownCount=completed.UnknownCount+1};
                if(completed.Gates.Count>64)completed=completed with {Gates=Array.Empty<MapGateArea>(),Issue="Zu viele eigenständige Schranken",UnknownCount=completed.UnknownCount+1};
                if(failed)retryAt=seconds()+Math.Min(30,5*++failures);else {failures=0;retryAt=double.PositiveInfinity;}
                status=completed.Issue??$"{completed.Bodies.Count} statische Kollisionsmodelle; keine vollständige Szenenfreigabe.";
            }
        }
    }
    static MapSelection? NormalizeSelection(MapSelection? value)=>value==null?null:
        NormalizeMap(value.BaseMapName) is { } basis&&(value.ExplicitMapName==null||NormalizeMap(value.ExplicitMapName)!=null)?
            new(basis,value.ForceBaseMapName,value.ExplicitMapName==null?null:NormalizeMap(value.ExplicitMapName)):null;
    static string? NormalizeMap(string? value)
    {
        if(string.IsNullOrWhiteSpace(value))return null;
        if(value.StartsWith("/map/",StringComparison.Ordinal)&&value.EndsWith(".mbd",StringComparison.Ordinal))value=value[5..^4];
        return value.Length is >0 and <=64&&value.All(c=>char.IsAsciiLetterOrDigit(c)||c is '_' or '-')?value:null;
    }
    static bool Finite(Vector3 p)=>float.IsFinite(p.X)&&float.IsFinite(p.Y)&&float.IsFinite(p.Z)&&
        Math.Abs(p.X)<=262144&&Math.Abs(p.Y)<=10000&&Math.Abs(p.Z)<=262144;

    // Only the unconditional main-layer, non-LOD static map instance is admitted.
    // Unknown KDOP flags (including conditional/event bits) do not become solids.
    static readonly PropertyInfo? kdop=typeof(MapItem).GetProperty("Kdop",BindingFlags.NonPublic|BindingFlags.Instance);
    internal static bool EligibleInstance(MapModel item)
    {
        // Only shadow/mirror/terrain rendering toggles may differ. Do not treat
        // known-but-conditional traffic handedness, vegetation, LOD, color
        // variant, cut-plane or hookup modes as unconditional physical proof.
        const uint known=0x0002C000; // rendering-only bits 14,15,17 in pinned Model
        if(item.Layer!=0||item.Lod!=0||!item.Collision)return false;
        try {
            var metadata=kdop?.GetValue(item);
            var flags=metadata?.GetType().GetField("Flags",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(metadata);
            return flags is FlagField f&&(f.Bits&~known)==0;
        }catch{return false;}
    }
    internal static bool EligibleSign(TruckLib.ScsMap.Sign item)
    {
        if(item.Layer!=0||!item.Collision||item.FollowRoadDir||!string.IsNullOrEmpty(item.SignTemplate)||
            item.SignOverrides is {Count:>0}||item.BoardOverrides is {Count:>0}||item.SignBoards is {Length:>0})return false;
        try {
            var metadata=kdop?.GetValue(item);
            var flags=metadata?.GetType().GetField("Flags",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(metadata);
            // Pinned Sign: only bit26 is a render-only water-reflection toggle.
            // No broad reuse of Model's unrelated flag layout.
            return flags is FlagField f&&(f.Bits&~(1u<<26))==0;
        }catch{return false;}
    }
    internal static StaticRoadSignGeometry.Result CaptureSign(TruckLib.ScsMap.Sign item,StaticRoadSignRegistry.Definition definition,Model model,byte[] collision)
    {
        if(!EligibleSign(item))return new(null,"Unsupported sign instance activity or composition");
        string variant=item.Variant.ToString();if(variant=="")variant=definition.Variant;
        return StaticRoadSignGeometry.Capture(model,collision,variant,item.Node.Position,item.Node.Rotation);
    }
    sealed class ModelsOnlyMap:Map
    {
        internal bool Truncated;
        int count;
        protected override bool PostProcessItem(MapItem item)
        {
            if(item is not (MapModel or TruckLib.ScsMap.Sign or TruckLib.ScsMap.Gate))return false;
            if(++count>12000){Truncated=true;return false;}
            return true;
        }
    }
    internal static bool TryMapBounds(MapItem item,out Vector3 minimum,out Vector3 maximum)
    {
        minimum=maximum=default;
        try {
            if(kdop?.GetValue(item) is not KdopBounds {Minimums.Length:5,Maximums.Length:5} b)return false;
            minimum=new(b.Minimums[0],b.Minimums[1],b.Minimums[2]);maximum=new(b.Maximums[0],b.Maximums[1],b.Maximums[2]);
            return Finite(minimum)&&Finite(maximum)&&minimum.X<=maximum.X&&minimum.Y<=maximum.Y&&minimum.Z<=maximum.Z;
        }catch{return false;}
    }
    // Confined to the single background worker; all caches are revoked together
    // when source generation or map selection changes. No host streams used.
    sealed class Loader:IDisposable
    {
        IndependentAssets? owned;
        long generation=-1;
        string selected="unknown";
        readonly Dictionary<string,string> registry=new(StringComparer.Ordinal);
        readonly HashSet<string> ambiguous=new(StringComparer.Ordinal);
        readonly Dictionary<string,StaticRoadSignRegistry.Definition?> signRegistry=new(StringComparer.Ordinal);
        IReadOnlyDictionary<string,MapGateSource.Trigger?> gateProfiles=new Dictionary<string,MapGateSource.Trigger?>();
        readonly Dictionary<SectorCoordinate,MapItem[]> sectors=[];
        readonly Dictionary<string,Model?> models=new(StringComparer.Ordinal);
        readonly Dictionary<string,byte[]?> collisions=new(StringComparer.Ordinal);
        int collisionBytes;
        int cachedVertices;
        bool truncated;
        public void Dispose(){owned?.Dispose();owned=null;generation=-1;registry.Clear();ambiguous.Clear();signRegistry.Clear();gateProfiles=new Dictionary<string,MapGateSource.Trigger?>();sectors.Clear();models.Clear();collisions.Clear();collisionBytes=0;cachedVertices=0;truncated=false;}
        internal Snapshot Load(Request request)
        {
            var watch=System.Diagnostics.Stopwatch.StartNew();long allocation=GC.GetAllocatedBytesForCurrentThread();
            var phases=new Dictionary<string,long>();long measured=allocation;
            void Mark(string name){long next=GC.GetAllocatedBytesForCurrentThread();phases[name]=next-measured;measured=next;}
            if(owned==null||generation!=request.Generation) {
                Dispose();owned=new IndependentAssets(request.Archives);generation=request.Generation;
                Mark("independentArchives");
                var choice=request.Selection;selected=choice.ExplicitMapName??choice.BaseMapName;
                if(choice.ExplicitMapName==null&&!choice.ForceBaseMapName) {
                    var alternative=owned.Files.GetFiles("/map/").FirstOrDefault(p=>p.EndsWith(".mbd",StringComparison.Ordinal)&&p!="/map/"+choice.BaseMapName+".mbd");
                    if(alternative!=null)selected=NormalizeMap(alternative)??throw new InvalidDataException("Unsupported selected map path");
                }
                if(!owned.Files.FileExists("/map/"+selected+".mbd"))throw new InvalidDataException("Selected map missing");
                ReadRegistry(owned.Files);
                gateProfiles=MapGateSource.ReadProfiles(owned);
                Mark("registryAndMapSelection");
            }
            var files=owned.Files;
            var wanted=new[]{-1200f,0f,1200f}.SelectMany(x=>new[]{-1200f,0f,1200f}.Select(z=>
                Map.GetSectorOfCoordinate(request.Center+new Vector3(x,0,z)))).Distinct().ToArray();
            foreach(var sector in wanted)if(!sectors.ContainsKey(sector)) {
                var map=new ModelsOnlyMap();StaticRoadAuxMapReader.Read(map,files,selected,sector);truncated|=map.Truncated;
                if(sectors.Count>=9)sectors.Remove(sectors.Keys.First(k=>!wanted.Contains(k)));
                sectors.Add(sector,map.MapItems.Values.ToArray());
            }
            Mark("auxiliarySectors");
            int unknown=0;bool budget=truncated;var bodies=new List<Body>();int triangles=0,instances=0;
            var gateAreas=new List<MapGateArea>();
            var candidates=new List<(MapItem Item,float Distance)>();
            foreach(var item in wanted.SelectMany(s=>sectors[s]).DistinctBy(m=>m.Uid)) {
                if(item is TruckLib.ScsMap.Gate gate) {
                    if(Vector3.Distance(gate.Node.Position,request.Center)>RegionRadius+20)continue;
                    var area=MapGateSource.Capture(gate,gateProfiles);
                    if(area==null){unknown++;continue;}
                    if(gateAreas.Count>=64){unknown++;budget=true;continue;}
                    gateAreas.Add(area);continue;
                }
                if(!TryMapBounds(item,out var min,out var max)){unknown++;continue;}
                float distance=Vector3.DistanceSquared(Vector3.Clamp(request.Center,min,max),request.Center);
                if(distance<=(RegionRadius+1)*(RegionRadius+1))candidates.Add((item,distance));
            }
            foreach(var candidate in candidates.OrderBy(c=>c.Distance)) {
                var item=candidate.Item;string path;StaticRoadSignRegistry.Definition? signDefinition=null;
                if(item is MapModel ordinary) {
                    if(!EligibleInstance(ordinary)||ambiguous.Contains(ordinary.Name.ToString())||!registry.TryGetValue(ordinary.Name.ToString(),out path!)){unknown++;continue;}
                }else if(item is TruckLib.ScsMap.Sign sign) {
                    if(!EligibleSign(sign)||!signRegistry.TryGetValue(sign.Model.ToString(),out signDefinition)||signDefinition==null){unknown++;continue;}
                    path=signDefinition.Model;
                }else {unknown++;continue;}
                if(++instances>256){budget=true;break;}
                if(!models.TryGetValue(path,out var model)) {
                    model=ReadModelAsset(files,path);
                    int vertices=model?.Parts.Sum(p=>p.Pieces.Sum(q=>q.Vertices.Count))??0;
                    if(vertices>100000){model=null;vertices=0;}
                    while(models.Count>=128||cachedVertices+vertices>200000) {
                        var old=models.First();cachedVertices-=old.Value?.Parts.Sum(p=>p.Pieces.Sum(q=>q.Vertices.Count))??0;models.Remove(old.Key);
                    }
                    models.Add(path,model);cachedVertices+=vertices;
                }
                if(model==null){unknown++;continue;}
                // Preflight BEFORE constructing transformed triangles or a BVH.
                int faces=signDefinition==null?model.Parts.Where(p=>p.Name.ToString()=="coll").Sum(p=>p.Pieces.Sum(q=>q.Triangles.Count)):20000;
                if(faces>20000||triangles+faces>100000){unknown++;budget=true;continue;}
                RigStaticGeometry? geometry;
                if(item is MapModel mapModel)geometry=StaticRoadModelGeometry.Capture(model,mapModel.Variant.ToString(),mapModel.AdditionalParts.Select(p=>p.ToString()).ToArray(),
                    mapModel.Collision,mapModel.Node.Position,mapModel.Node.Rotation,mapModel.Scale).Geometry;
                else {
                    string collisionPath=signDefinition!.Collision;
                    if(!collisions.TryGetValue(collisionPath,out var bytes)) {
                        bytes=ReadCollisionAsset(files,collisionPath);
                        while(collisions.Count>=128||collisionBytes+(bytes?.Length??0)>32*1024*1024) {
                            var old=collisions.First();collisionBytes-=old.Value?.Length??0;collisions.Remove(old.Key);
                        }
                        collisions.Add(collisionPath,bytes);collisionBytes+=bytes?.Length??0;
                    }
                    geometry=bytes==null?null:CaptureSign((TruckLib.ScsMap.Sign)item,signDefinition,model,bytes).Geometry;
                }
                if(geometry==null){unknown++;continue;}triangles+=geometry.Count;
                var min=new Vector3(float.PositiveInfinity);var max=new Vector3(float.NegativeInfinity);
                foreach(var t in geometry.Triangles){min=Vector3.Min(min,Vector3.Min(t.A,Vector3.Min(t.B,t.C)));max=Vector3.Max(max,Vector3.Max(t.A,Vector3.Max(t.B,t.C)));}
                if(Vector3.DistanceSquared(Vector3.Clamp(request.Center,min,max),request.Center)>RegionRadius*RegionRadius)continue;
                if(bodies.Count>=64){budget=true;break;}
                bodies.Add(new(item.Uid,geometry,min,max));
            }
            return new(request.Generation,selected,request.Center,RegionRadius,Array.AsReadOnly(bodies.OrderBy(b=>
                Vector3.DistanceSquared(Vector3.Clamp(request.Center,b.Minimum,b.Maximum),request.Center)).ToArray()),unknown,
                budget?"Statische Straßenquelle nur teilweise gelesen (Arbeitsgrenze); keine Freiraumfreigabe.":null)
                {Gates=Array.AsReadOnly(gateAreas.ToArray()),BuildMilliseconds=watch.Elapsed.TotalMilliseconds,AllocatedBytes=GC.GetAllocatedBytesForCurrentThread()-allocation,
                    PhaseAllocations=new System.Collections.ObjectModel.ReadOnlyDictionary<string,long>(phases.Append(
                        new KeyValuePair<string,long>("modelsAndGeometry",GC.GetAllocatedBytesForCurrentThread()-measured)).ToDictionary(p=>p.Key,p=>p.Value))};
        }
        void ReadRegistry(IFileSystem files)
        {
            var definitions=files.GetFiles("/def/world/").Where(p=>p.StartsWith("/def/world/model.",StringComparison.Ordinal)&&p.EndsWith(".sii",StringComparison.Ordinal)).Take(1025).ToArray();
            if(definitions.Length>1024)throw new InvalidDataException("Model registry budget exceeded");
            int units=0;
            foreach(var path in definitions)foreach(var unit in StaticRoadModelRegistry.Parse(files.ReadAllText(path))) {
                if(++units>100000)throw new InvalidDataException("Model registry unit budget exceeded");
                string key=unit.Key;string? pmd=unit.Value;
                if(pmd==null){ambiguous.Add(key);continue;}
                if(registry.TryGetValue(key,out var existing)&&existing!=pmd)ambiguous.Add(key);else registry[key]=pmd;
            }
            foreach(var unit in StaticRoadSignRegistry.ReadCatalog(files)) {
                if(++units>100000)throw new InvalidDataException("Static registry unit budget exceeded");
                signRegistry.Add(unit.Key,unit.Value);
            }
        }
    }
    internal static byte[]? ReadCollisionAsset(IFileSystem files,string path)
    {
        if(!files.FileExists(path))return null;
        using var stream=files.Open(path);
        if(stream.Length is <40 or >4*1024*1024)return null;
        var bytes=new byte[checked((int)stream.Length)];stream.ReadExactly(bytes);return bytes;
    }
    internal static Model? ReadModelAsset(IFileSystem files,string path)
    {
        string geometry=Path.ChangeExtension(path,".pmg");
        if(!files.FileExists(path)||!files.FileExists(geometry))return null;
        try {using var descriptor=files.Open(path);using var mesh=files.Open(geometry);
            if(descriptor.Length>4*1024*1024||mesh.Length>32*1024*1024)return null;
            return Model.Load(descriptor,mesh);
        }
        // A temporarily inaccessible archive must reach the source backoff and
        // be retried, rather than becoming a permanent cached null model.
        catch(IOException){throw;}
        catch{return null;}
    }
}
