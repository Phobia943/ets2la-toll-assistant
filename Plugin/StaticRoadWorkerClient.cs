using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using TruckLib;
using W=MautAssistent.StaticRoadWorkerProtocol;

namespace MautAssistent;

// Read/IsCurrent never start a process, read an archive, serialize a route or
// traverse collision meshes/BVHs. Rig results add only the bounded original-
// route movement check. A missing helper stays unknown; no mesh fallback.
internal sealed class StaticRoadWorkerClient:IStaticRoadChecks,IDisposable
{
    internal sealed record Result(long Generation,long RouteId,long Epoch,long OriginalTimestamp,
        RouteTrace Route,string MapName,IReadOnlyList<W.Hit> Hits,bool Incomplete,string? Issue)
    {internal IReadOnlyList<MapGateCandidate> Gates {get;init;}=Array.Empty<MapGateCandidate>();
     internal double QueryHorizon {get;init;} internal long RouteEpoch {get;init;}
     internal long RigEpoch {get;init;} internal RigRoadObservation? OriginalRig {get;init;}}
    sealed record Job(long Generation,long RouteId,long RouteEpoch,long Epoch,long Timestamp,RouteTrace Route,
        Vector3 Position,double Horizon,StaticRoadSceneSource.MapSelection Selection,string[] Archives)
    {internal RigRoadObservation? Rig {get;init;} internal long RigEpoch {get;init;}}
    readonly Func<IFileSystem?> files;
    readonly Func<StaticRoadSceneSource.MapSelection?> map;
    readonly string directory;
    readonly Func<IFileSystem,string[]?> paths;
    readonly object sync=new();
    readonly AutoResetEvent wake=new(false);
    readonly Action<Action> schedule;
    IFileSystem? source;StaticRoadSceneSource.MapSelection? selection;
    RouteTrace? route;long generation,routeId,routeEpoch,manualEpoch=-1;Vector3 latestPosition;double activeHorizon;string[]? archives;
    Job? pending;Result? completed;bool resetProcess,workerRunning,disposed;long pathRetryAt,startRetryAt;
    RigRoadObservation? rig;long rigEpoch;
    CancellationTokenSource? inFlight;
    string status="Statische Straßenszene noch nicht geprüft.";
    Process? process;string session="";long nextId;double retryAt;int failures;
    // Worker-owned immutable tail only. Current forward authority still uses
    // job.Route, while rear ground may be retained across exact route pruning.
    RouteTrace? rigGroundRoute;long groundGeneration=-1,groundRouteEpoch=-1,groundManualEpoch=-1;
    public string Status {get{lock(sync)return status;}}
    internal StaticRoadWorkerClient(Func<IFileSystem?> files,Func<StaticRoadSceneSource.MapSelection?> map,
        string? directory=null,Func<IFileSystem,string[]?>? paths=null,Action<Action>? schedule=null)
    {
        this.files=files;this.map=map;this.paths=paths??IndependentAssets.SnapshotPaths;
        // The host shadow-copies only our DLL to Temp/PluginShadow. The
        // installer-owned helper lives in the application layout, not beside
        // that temporary assembly. Never depend on the launch working folder.
        this.directory=directory??Path.Combine(AppContext.BaseDirectory,"Plugins","StaticRoadWorker");
        this.schedule=schedule??(action=>new Thread(()=>action()){IsBackground=true,Name="ETS2LA static scene IPC",Priority=ThreadPriority.BelowNormal}.Start());
    }
    public Result? Read(RouteTrace trace,Vector3 position,double horizon,long manualEpoch)
        =>Read(trace,position,horizon,manualEpoch,null);
    public Result? Read(RouteTrace trace,Vector3 position,double horizon,long manualEpoch,RigRoadObservation? observedRig)
    {
        var now=Stopwatch.GetTimestamp();bool start=false;Result? result;
        lock(sync) {
            if(disposed||!Bind()||manualEpoch<0||!SceneSnapshotProtocol.Finite(position)||!double.IsFinite(horizon)||horizon is <1 or >300) {
                completed=null;pending=null;return null;
            }
            latestPosition=position;
            BindRig(observedRig);
            if(completed is { } oldRig&&oldRig.Hits.Any(h=>h.Rig)&&!RigCurrent(oldRig,rig)){completed=null;rigEpoch++;}
            if(this.manualEpoch!=manualEpoch){this.manualEpoch=manualEpoch;completed=null;}
            if(!ReferenceEquals(route,trace)) {
                // A lower new speed must not shrink the identity check below
                // an outstanding older query's possible positive hits.
                double identityHorizon=Math.Max(horizon,Math.Max(activeHorizon,completed?.QueryHorizon??0));
                if(route!=null&&!Compatible(route,trace,position,identityHorizon)){routeEpoch++;completed=null;}
                route=trace;routeId++;
            }
            if(archives==null&&now>=pathRetryAt) {
                try{archives=paths(source!);}catch{archives=null;}
                if(archives==null||archives.Length is <1 or >256) {
                    archives=null;pathRetryAt=now+5*Stopwatch.Frequency;status="Statische Quelle nicht unabhängig lesbar.";return null;
                }
                archives=archives.ToArray();
            }
            if(archives==null)return null;
            if(now<startRetryAt)return null;
            pending=new(generation,routeId,routeEpoch,manualEpoch,now,trace,position,horizon,selection!,archives){Rig=rig,RigEpoch=rigEpoch};
            wake.Set();
            if(!workerRunning){workerRunning=true;start=true;}
            result=completed is { } r&&r.Generation==generation&&r.RouteEpoch==routeEpoch&&r.Epoch==manualEpoch&&
                Compatible(r.Route,trace,position,r.QueryHorizon)&&RigCurrent(r,rig)&&W.Fresh(r.OriginalTimestamp,now)?r:null;
        }
        if(start)try{schedule(Run);}catch(Exception e) {
            lock(sync){workerRunning=false;pending=null;completed=null;startRetryAt=Stopwatch.GetTimestamp()+5*Stopwatch.Frequency;
                status="Statischer Szenenworker konnte nicht starten: "+e.GetType().Name;}
            return null;
        }
        return result;
    }
    public bool IsCurrent(Result result,RouteTrace trace,long manualEpoch)
    {
        lock(sync) {
            bool valid=!disposed&&Bind()&&ReferenceEquals(result,completed)&&result.Generation==generation&&result.RouteEpoch==routeEpoch&&
                result.Epoch==manualEpoch&&this.manualEpoch==manualEpoch&&Compatible(result.Route,trace,latestPosition,result.QueryHorizon)&&
                RigCurrent(result,rig)&&W.Fresh(result.OriginalTimestamp,Stopwatch.GetTimestamp());
            if(!valid&&ReferenceEquals(result,completed)&&result.Hits.Any(h=>h.Rig)){completed=null;rigEpoch++;}
            return valid;
        }
    }
    // Final local veto after the last provider/focus check. No callbacks,
    // locks or geometry work: a replacement/reset cannot revive this result.
    public bool IsLocallyCurrent(Result result)=>!Volatile.Read(ref disposed)&&
        ReferenceEquals(Volatile.Read(ref completed),result)&&result.Generation==Volatile.Read(ref generation)&&
        result.RouteEpoch==Volatile.Read(ref routeEpoch)&&result.Epoch==Volatile.Read(ref manualEpoch)&&
        W.Fresh(result.OriginalTimestamp,Stopwatch.GetTimestamp());
    public bool IsRigCurrent(Result result,RigRoadObservation? current)
    {
        lock(sync) {
            BindRig(current);
            if(disposed||!ReferenceEquals(result,completed))return false;
            if(RigCurrent(result,rig))return true;
            completed=null;rigEpoch++;return false;
        }
    }
    void BindRig(RigRoadObservation? current)
    {
        if(current?.Valid(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())!=true)current=null;
        if(rig==null?current!=null:!rig.SameConfiguration(current)||current!.Shape.Sequence<rig.Shape.Sequence||current.Shape.UtcMs<rig.Shape.UtcMs||
            current.Shape.Sequence==rig.Shape.Sequence&&current.Shape!=rig.Shape||current.Shape.Sequence>rig.Shape.Sequence&&current.Shape.UtcMs<=rig.Shape.UtcMs) {
            rigEpoch++;
            if(completed?.Hits.Any(h=>h.Rig)==true)completed=null;
        }
        rig=current;
    }
    bool RigCurrent(Result result,RigRoadObservation? current)
    {
        if(!result.Hits.Any(h=>h.Rig))return true;
        long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return result.RigEpoch==rigEpoch&&result.OriginalRig is { } original&&current!=null&&
            original.Valid(now)&&current.Valid(now)&&original.SameConfiguration(current)&&
            current.Shape.Sequence>=original.Shape.Sequence&&current.Shape.UtcMs>=original.Shape.UtcMs&&
            RigRoadPrediction.MatchesObserved(original,current,result.Route,now)&&
            original.Valid(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())&&current.Valid(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())&&
            W.Fresh(result.OriginalTimestamp,Stopwatch.GetTimestamp());
    }
    static bool Compatible(RouteTrace original,RouteTrace current,Vector3 position,double horizon)=>
        ReferenceEquals(original,current)||original.IdentityKnown&&current.IdentityKnown&&double.IsFinite(horizon)&&horizon is >=1 and <=300&&
        original.MatchesAhead(current,position,horizon,out _,out _);
    // This is called on the host thread only. Getters return identity and
    // immutable selection; no filesystem enumeration is allowed in them.
    bool Bind()
    {
        IFileSystem? current;StaticRoadSceneSource.MapSelection? chosen;
        try{current=files();chosen=map();}catch{current=null;chosen=null;}
        if(!ReferenceEquals(current,source)||chosen!=selection) {
            generation++;source=current;selection=chosen;archives=null;pathRetryAt=0;
            completed=null;pending=null;route=null;routeEpoch++;manualEpoch=-1;activeHorizon=0;resetProcess=true;wake.Set();
            rig=null;rigEpoch++;
        }
        return current!=null&&chosen!=null&&W.MapName(chosen.BaseMapName)&&
            (chosen.ExplicitMapName==null||W.MapName(chosen.ExplicitMapName));
    }
    public void Reset()
    {
        CancellationTokenSource? cancel;
        lock(sync){generation++;source=null;selection=null;archives=null;route=null;routeEpoch++;manualEpoch=-1;activeHorizon=0;completed=null;pending=null;
            rig=null;rigEpoch++;
            resetProcess=true;status="Statische Straßenszene zurückgesetzt.";cancel=inFlight;wake.Set();}
        // Never wait for pipe callbacks or process termination in the host tick.
        if(cancel!=null)try{_=cancel.CancelAsync();}catch(ObjectDisposedException){}
    }
    public void Dispose(){lock(sync)disposed=true;Reset();}
    void Run()
    {
        for(;;) {
            bool signalled=wake.WaitOne(60000);
            Job? job;bool reset;
            lock(sync){reset=resetProcess;resetProcess=false;job=pending;pending=null;activeHorizon=job?.Horizon??0;}
            if(reset){StopOwnedProcess();failures=0;retryAt=0;}
            if(job==null) {
                if(reset||!signalled) {
                    StopOwnedProcess();
                    lock(sync){if(pending==null){workerRunning=false;return;}}
                }
                continue;
            }
            if(Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency<retryAt)continue;
            try {
                // A cold start/identity check can take time. Never issue that
                // old job afterwards; the next fresh tick uses the ready child.
                EnsureProcess();
                if(!W.Fresh(job.Timestamp,Stopwatch.GetTimestamp()))continue;
                var q=MakeQuery(job);
                if(q==null){SetStatus(job,"Keine auswertbare Standardroute für statische Szene.");continue;}
                var bytes=W.Encode(q,W.MaxRequestBytes);
                if(!W.Fresh(job.Timestamp,Stopwatch.GetTimestamp()))continue;
                using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(10));
                lock(sync){if(job.Generation!=generation||disposed)continue;inFlight=deadline;}
                byte[]? line;
                try {
                    W.WriteFrame(process!.StandardInput.BaseStream,bytes,deadline.Token).GetAwaiter().GetResult();
                    line=W.ReadFrame(process.StandardOutput.BaseStream,W.MaxResponseBytes,deadline.Token).GetAwaiter().GetResult();
                }finally{lock(sync)if(ReferenceEquals(inFlight,deadline))inFlight=null;}
                if(line==null)throw new EndOfStreamException("Static worker exited");
                var reply=W.Decode<W.Reply>(line,W.MaxResponseBytes);
                if(!W.ValidReply(reply,q,Stopwatch.GetTimestamp())) {
                    SetStatus(job,"Veraltete oder ungültige statische Antwort verworfen.");continue;
                }
                var result=new Result(job.Generation,job.RouteId,job.Epoch,job.Timestamp,job.Route,
                    reply.MapName,Array.AsReadOnly(reply.Hits),reply.Incomplete,reply.Issue){Gates=Array.AsReadOnly(reply.Gates),QueryHorizon=job.Horizon,RouteEpoch=job.RouteEpoch,RigEpoch=job.RigEpoch,OriginalRig=job.Rig};
                lock(sync)if(job.Generation==generation&&job.RouteEpoch==routeEpoch&&job.Epoch==manualEpoch&&
                    (!reply.Hits.Any(h=>h.Rig)||job.RigEpoch==rigEpoch)&&!resetProcess&&!disposed) {
                    completed=result;status=$"{reply.Hits.Length} positive statische Hindernisse; keine Freiraumfreigabe.";
                }
                failures=0;
            }catch(Exception e) {
                StopOwnedProcess();retryAt=Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency+Math.Min(30,1<<Math.Min(++failures,4));
                lock(sync){if(job.Generation==generation)completed=null;status="Statischer Szenenprozess nicht verfügbar: "+e.GetType().Name;}
            }
        }
    }
    W.Query? MakeQuery(Job job)
    {
        var projection=job.Route.ProjectForTracking(job.Position);
        if(!double.IsFinite(projection.Along)||projection.Offset>3||Math.Abs(projection.HeightOffset)>2||
            !job.Route.TryStandardSlice(projection.Along,Math.Min(job.Route.Length,projection.Along+job.Horizon),.5,out var slice,out _)||slice.Count is <2 or >1000)return null;
        IReadOnlyList<TraceSlicePoint> tail=[];
        if(job.Rig!=null) {
            double rear=job.Rig.Shape.Trailer!.Corners().Concat(job.Rig.Shape.Truck.Corners()).Max(p=>Vector3.Distance(p,job.Rig.Shape.Truck.Position))+5;
            if(groundGeneration!=job.Generation||groundRouteEpoch!=job.RouteEpoch||groundManualEpoch!=job.Epoch)rigGroundRoute=null;
            var ground=rigGroundRoute==null?job.Route:job.Route.WithVerifiedTail(rigGroundRoute,job.Position,55)??job.Route;
            rigGroundRoute=ground;groundGeneration=job.Generation;groundRouteEpoch=job.RouteEpoch;groundManualEpoch=job.Epoch;
            double at=ground.ProjectForTracking(job.Position).Along;
            double from=Math.Max(0,at-Math.Min(55,rear));
            if(from<at&&ground.TryStandardSlice(from,at,.5,out var capturedTail,out _)) {
                double shift=projection.Along-at;
                tail=capturedTail.Select(p=>p with{Along=p.Along+shift}).ToArray();
            }
        }
        var segments=slice.Concat(tail).Select(p=>p.SegmentIdentity).Distinct(StringComparer.Ordinal).ToArray();
        if(segments.Length>64||segments.Any(s=>s==null||s.Length>512))return null;
        var points=slice.Select(p=>new W.Point(p.Position.X,p.Position.Y,p.Position.Z,p.Direction.X,p.Direction.Y,p.Direction.Z,
            p.Along,Array.IndexOf(segments,p.SegmentIdentity),p.Connector)).ToArray();
        var q=new W.Query(W.Version,session,++nextId,job.Generation,job.RouteId,job.Epoch,job.Timestamp,job.Selection,
            job.Archives,job.Position.X,job.Position.Y,job.Position.Z,projection.Along,segments!,points);
        if(job.Rig!=null&&tail.Count is >=2 and <=160)
            q=q with{Rig=new(job.Rig,tail.Select(p=>new W.Point(p.Position.X,p.Position.Y,p.Position.Z,p.Direction.X,p.Direction.Y,p.Direction.Z,
                p.Along,Array.IndexOf(segments,p.SegmentIdentity),p.Connector)).ToArray())};
        return q;
    }
    void SetStatus(Job job,string value){lock(sync)if(job.Generation==generation)status=value;}
    void EnsureProcess()
    {
        if(process is {HasExited:false})return;
        StopOwnedProcess();VerifyBundle(directory);
        session=Guid.NewGuid().ToString("N");nextId=0;
        var start=new ProcessStartInfo(Path.Combine(directory,"MautStaticRoadWorker.exe")) {
            UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,
            RedirectStandardError=true,WorkingDirectory=directory
        };
        start.ArgumentList.Add(session);process=Process.Start(start)??throw new IOException("Worker start failed");
        // Keep draining even after the diagnostic budget; otherwise a library
        // flood can block its stderr pipe. No ReadToEnd/unbounded memory log.
        var owned=process;
        _=Task.Run(async()=>{try{char[] buffer=new char[1024];int retained=0;
            while(await owned.StandardError.ReadAsync(buffer)>0)retained=Math.Min(8192,retained+buffer.Length);
        }catch{}});
    }
    void StopOwnedProcess()
    {
        var owned=process;process=null;session="";
        if(owned==null)return;
        try{if(!owned.HasExited)owned.Kill();}catch{}finally{owned.Dispose();}
    }
    internal static void VerifyBundle(string directory)
    {
        var assembly=typeof(StaticRoadWorkerClient).Assembly;
        string name=assembly.GetManifestResourceNames().Single(n=>n.EndsWith(".StaticRoadWorkerManifest.json",StringComparison.Ordinal));
        using var resource=assembly.GetManifestResourceStream(name)??throw new InvalidDataException("No pinned worker manifest");
        if(resource.Length>16384)throw new InvalidDataException("Oversized worker manifest");
        var manifest=JsonSerializer.Deserialize<Dictionary<string,string>>(resource)??throw new InvalidDataException("Invalid worker manifest");
        if(manifest.Count is <4 or >32||new[]{"MautStaticRoadWorker.exe","MautStaticRoadWorker.dll","MautStaticRoadWorker.deps.json","MautStaticRoadWorker.runtimeconfig.json"}.Any(n=>!manifest.ContainsKey(n)))
            throw new InvalidDataException("Incomplete pinned worker manifest");
        if(Directory.EnumerateFiles(directory).Any(p=>new[]{".dll",".exe",".json"}.Contains(Path.GetExtension(p),StringComparer.OrdinalIgnoreCase)&&
            !manifest.ContainsKey(Path.GetFileName(p))))throw new InvalidDataException("Unpinned worker runtime file");
        foreach(var entry in manifest) {
            if(Path.GetFileName(entry.Key)!=entry.Key||entry.Value.Length!=64||!entry.Value.All(char.IsAsciiHexDigit))throw new InvalidDataException("Invalid worker manifest entry");
            using var file=File.OpenRead(Path.Combine(directory,entry.Key));
            if(!Convert.ToHexString(SHA256.HashData(file)).Equals(entry.Value,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Worker identity mismatch");
        }
    }
}
