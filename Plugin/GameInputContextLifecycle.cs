using System.Diagnostics;

namespace MautAssistent;

public sealed partial class TollPlugin
{
    readonly object inputContextLifecycleSync=new();
    GameInputContext? inputContext;
    int inputGamePid;
    long inputContextGeneration;
    CancellationTokenSource? inputContextCancellation;
    Task? inputContextWorker;
    string inputContextWorkerReason="Fensterkontext wurde noch nicht gestartet.";
    // Optional pure sources for isolated lifecycle regression tests. Production
    // leaves these null; no callback or discovery is invoked by construction.
    Func<string[]>? inputContextReadInstallations=null;
    Func<string[],int[]>? inputContextFindProcesses=null;
    Func<string[],GameInputContext>? inputContextFactory=null;

    // The only potentially blocking discovery is on this dedicated read-only
    // worker. No game handlers, process enumeration or module lookup in Capture.
    void StartInputContext()
    {
        lock(inputContextLifecycleSync) {
            if(inputContextCancellation!=null)return;
            var cancellation=new CancellationTokenSource();
            inputContextCancellation=cancellation;
            long generation=++inputContextGeneration;
            inputContextWorker=Task.Run(()=>InputContextLoop(generation,cancellation));
        }
    }
    void StopInputContext()
    {
        lock(inputContextLifecycleSync) {
            ++inputContextGeneration;
            inputContextCancellation?.Cancel();inputContextCancellation=null;
            Volatile.Write(ref inputGamePid,0);Volatile.Write(ref inputContext,null);
            inputContextWorkerReason="Fensterkontext beendet.";
            // Do not wait for process I/O or a delayed worker while stopping.
            // The worker owns and releases its handles in finally.
            inputContextWorker=null;
        }
    }
    GameInputContextSnapshot ReadInputContext()
    {
        var context=Volatile.Read(ref inputContext);
        int pid=Volatile.Read(ref inputGamePid);
        return context?.Capture(pid)??new(Stopwatch.GetTimestamp(),0,pid,GameInputContextStatus.Unknown,
            Volatile.Read(ref inputContextWorkerReason));
    }
    async Task InputContextLoop(long generation,CancellationTokenSource cancellation)
    {
        GameInputContext? current=null;string[] previousPaths=[];
        var token=cancellation.Token;
        try {
            while(!token.IsCancellationRequested) {
                try {
                    // Installation.ExecutablePath is the host's detected full
                    // EXE path. Never synthesize an installation from a process
                    // name, and do not call Parse/GetFileSystem/telemetry here.
                    var paths=inputContextReadInstallations?.Invoke()??ETS2LA.Game.GameHandler.Current.Installations.ToArray()
                        .Select(i=>i.ExecutablePath).Where(p=>!string.IsNullOrWhiteSpace(p)&&Path.IsPathFullyQualified(p))
                        .Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
                    token.ThrowIfCancellationRequested();
                    if(current==null||!paths.SequenceEqual(previousPaths,StringComparer.OrdinalIgnoreCase)) {
                        var replacement=inputContextFactory?.Invoke(paths)??new GameInputContext(paths);var old=current;
                        current=replacement;previousPaths=paths;
                        PublishInputContext(generation,token,current,0,"Installationspfade aktualisiert; Prozessprüfung läuft.");
                        old?.Dispose();
                    }
                    token.ThrowIfCancellationRequested();
                    var pids=inputContextFindProcesses?.Invoke(paths)??FindKnownGameProcesses(paths);
                    int pid=pids.Length==1&&current.RefreshProcessIdentity(pids[0])?pids[0]:0;
                    string reason=pid>0?"Genau ein Spielprozess mit erlaubtem Installationspfad bestätigt.":
                        pids.Length>1?"Mehrere passende Spielprozesse; keine eindeutige Fensterzuordnung.":
                        "Kein laufender Spielprozess mit bestätigtem Installationspfad.";
                    PublishInputContext(generation,token,current,pid,reason);
                } catch(Exception e) when(e is not OutOfMemoryException) {
                    PublishInputContext(generation,token,current,0,"Fensterkontextprüfung nicht verfügbar: "+e.GetType().Name);
                }
                await Task.Delay(1000,token).ConfigureAwait(false);
            }
        } catch(OperationCanceledException) when(token.IsCancellationRequested) { }
        finally {
            lock(inputContextLifecycleSync) {
                if(inputContextGeneration==generation) {
                    Volatile.Write(ref inputGamePid,0);Volatile.Write(ref inputContext,null);
                    inputContextCancellation=null;inputContextWorker=null;
                }
            }
            current?.Dispose();cancellation.Dispose();
        }
    }
    void PublishInputContext(long generation,CancellationToken token,GameInputContext? context,int pid,string reason)
    {
        lock(inputContextLifecycleSync) {
            if(token.IsCancellationRequested||inputContextGeneration!=generation)return;
            Volatile.Write(ref inputContext,context);Volatile.Write(ref inputGamePid,pid);
            Volatile.Write(ref inputContextWorkerReason,reason);
        }
    }
    static int[] FindKnownGameProcesses(IReadOnlyCollection<string> allowedPaths)
    {
        if(!OperatingSystem.IsWindows()||allowedPaths.Count==0)return [];
        var allowed=allowedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);var result=new HashSet<int>();
        foreach(string name in new[]{"eurotrucks2","amtrucks"})
        foreach(var process in Process.GetProcessesByName(name))using(process) {
            try {
                string? image=process.MainModule?.FileName;
                if(!process.HasExited&&process.ProcessName.Equals(name,StringComparison.OrdinalIgnoreCase)&&
                    image!=null&&allowed.Contains(Path.GetFullPath(image)))result.Add(process.Id);
            } catch(Exception e) when(e is not OutOfMemoryException) { }
        }
        return result.Order().ToArray();
    }
}
