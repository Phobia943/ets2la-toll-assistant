using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace MautAssistent;

internal enum GameInputContextStatus { Unknown, GameForeground, OtherForeground, NoForegroundWindow, UnverifiedGameProcess, ProcessExited, NativeFailure, Disposed }

internal sealed record GameInputContextSnapshot(long CapturedAt,int ForegroundPid,int GamePid,
    GameInputContextStatus Status,string Reason)
{
    // Opaque in-process identity only; never serialize the live handle owner
    // or treat a reused Windows PID as the same verified game instance.
    [System.Text.Json.Serialization.JsonIgnore]
    internal object? ProcessIdentity {get;init;}
    internal bool? GameForeground=>Status==GameInputContextStatus.GameForeground?true:
        Status==GameInputContextStatus.OtherForeground?false:null;
    internal bool IsFresh(long now,long frequency)=>frequency>0&&CapturedAt>0&&now>=CapturedAt&&now-CapturedAt<=frequency*.1;
    internal bool IsFreshGameForeground(long now,long frequency)=>IsFresh(now,frequency)&&
        GamePid>0&&ForegroundPid==GamePid&&Status==GameInputContextStatus.GameForeground;
}

// IsAlive must be nonblocking. Production keeps a read-only process handle:
// a dead process cannot gain authority when Windows subsequently reuses its PID.
internal sealed record GameProcessIdentity(int ProcessId,string ProcessName,string ExecutablePath,Func<bool> IsAlive,Action? Release=null);

internal sealed class GameInputContext:IDisposable
{
    readonly HashSet<string> allowedPaths;
    readonly Func<int,GameProcessIdentity?> readProcess;
    readonly Func<nint> foregroundWindow;
    readonly Func<nint,int> windowProcess;
    readonly Func<long> now;
    readonly object refreshSync=new();
    readonly List<GameProcessIdentity> owned=[];
    GameProcessIdentity? identity;
    string identityReason="Spielprozess wurde noch nicht außerhalb des Ausgabepfads geprüft.";
    bool disposed;

    internal GameInputContext(IEnumerable<string> allowedGameExecutablePaths,
        Func<int,GameProcessIdentity?>? readProcess=null,Func<nint>? foregroundWindow=null,
        Func<nint,int>? windowProcess=null,Func<long>? now=null)
    {
        allowedPaths=allowedGameExecutablePaths.Select(NormalizePath).Where(p=>p!=null&&SupportedName(Path.GetFileNameWithoutExtension(p)))
            .Cast<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        this.readProcess=readProcess??ReadProcess;
        this.foregroundWindow=foregroundWindow??GetForegroundWindow;
        this.windowProcess=windowProcess??WindowProcess;
        this.now=now??Stopwatch.GetTimestamp;
    }
    static bool SupportedName(string? name)=>name is not null&&
        (name.Equals("eurotrucks2",StringComparison.OrdinalIgnoreCase)||name.Equals("amtrucks",StringComparison.OrdinalIgnoreCase));
    static string? NormalizePath(string? path)
    {
        try {
            if(string.IsNullOrWhiteSpace(path))return null;
            if(path.StartsWith(@"\\?\",StringComparison.Ordinal))path=path[4..];
            return Path.IsPathFullyQualified(path)?Path.GetFullPath(path):null;
        } catch(Exception e) when(e is ArgumentException or NotSupportedException or PathTooLongException){return null;}
    }

    // Call from initialization/a worker, never while holding a control-output
    // lock. Capture below does no process enumeration, path lookup or file I/O.
    internal bool RefreshProcessIdentity(int gamePid)
    {
        lock(refreshSync) {
            if(disposed)return false;
            GameProcessIdentity? candidate=null;bool retained=false;
            try {
                var previous=Volatile.Read(ref identity);
                if(gamePid>0&&previous?.ProcessId==gamePid&&previous.IsAlive())return true;
                Volatile.Write(ref identity,null);
                identityReason="Spielprozess oder erlaubter Installationspfad ist nicht bestätigt.";
                if(gamePid<=0||allowedPaths.Count==0)return false;
                candidate=readProcess(gamePid);
                if(candidate==null)return false;
                string? path=NormalizePath(candidate.ExecutablePath);
                if(candidate.ProcessId!=gamePid||!SupportedName(candidate.ProcessName)||path==null||
                    !Path.GetFileNameWithoutExtension(path).Equals(candidate.ProcessName,StringComparison.OrdinalIgnoreCase)||
                    !allowedPaths.Contains(path)||!candidate.IsAlive()) {
                    return false;
                }
                // Keep published handles until Dispose so a concurrent Capture
                // cannot race replacement with disposal of its original handle.
                owned.Add(candidate);retained=true;Volatile.Write(ref identity,candidate);
                identityReason="Spielprozess und exakter Installationspfad bestätigt.";
                return true;
            } catch(Exception e) when(e is not OutOfMemoryException) {
                Volatile.Write(ref identity,null);identityReason="Spielprozessprüfung nicht verfügbar: "+e.GetType().Name;return false;
            } finally {
                if(candidate!=null&&!retained)try{candidate.Release?.Invoke();}catch{ }
            }
        }
    }
    internal GameInputContextSnapshot Capture(int gamePid)
    {
        long captured=now();
        GameInputContextSnapshot Result(GameInputContextStatus status,string reason,int foreground=0)=>new(captured,foreground,gamePid,status,reason);
        if(Volatile.Read(ref disposed))return Result(GameInputContextStatus.Disposed,"Eingabekontext beendet.");
        var verified=Volatile.Read(ref identity);
        if(gamePid<=0||verified?.ProcessId!=gamePid)return Result(GameInputContextStatus.UnverifiedGameProcess,identityReason);
        try {
            if(!verified.IsAlive())return Result(GameInputContextStatus.ProcessExited,"Der ursprünglich geprüfte Spielprozess ist beendet.");
            nint window=foregroundWindow();
            if(window==0)return Result(GameInputContextStatus.NoForegroundWindow,"Kein Vordergrundfenster bekannt.");
            int pid=windowProcess(window);
            if(pid<=0)return Result(GameInputContextStatus.NativeFailure,"Vordergrundprozess konnte nicht gelesen werden.");
            if(Volatile.Read(ref disposed))return Result(GameInputContextStatus.Disposed,"Eingabekontext während der Fensterprobe beendet.",pid);
            if(!ReferenceEquals(Volatile.Read(ref identity),verified))
                return Result(GameInputContextStatus.UnverifiedGameProcess,"Spielprozess während der Fensterprobe gewechselt.",pid);
            if(!verified.IsAlive())return Result(GameInputContextStatus.ProcessExited,"Der ursprünglich geprüfte Spielprozess ist während der Fensterprobe beendet.",pid);
            return (pid==gamePid?Result(GameInputContextStatus.GameForeground,"Bestätigtes Spielfenster im Vordergrund.",pid):
                Result(GameInputContextStatus.OtherForeground,"Ein anderes Fenster ist im Vordergrund.",pid)) with {ProcessIdentity=verified};
        } catch(Exception e) when(e is not OutOfMemoryException) {
            return Result(GameInputContextStatus.NativeFailure,"Fensterkontext nicht verfügbar: "+e.GetType().Name);
        }
    }
    public void Dispose()
    {
        lock(refreshSync) {
            Volatile.Write(ref disposed,true);Volatile.Write(ref identity,null);
            foreach(var item in owned)try{item.Release?.Invoke();}catch{ }
            owned.Clear();
        }
    }

    static GameProcessIdentity? ReadProcess(int pid)
    {
        if(!OperatingSystem.IsWindows())return null;
        var handle=OpenProcess(0x00100000|0x00001000,false,checked((uint)pid)); // SYNCHRONIZE | QUERY_LIMITED_INFORMATION
        if(handle.IsInvalid){handle.Dispose();return null;}
        try {
            var path=new StringBuilder(32768);uint size=(uint)path.Capacity;
            if(!QueryFullProcessImageName(handle,0,path,ref size)){handle.Dispose();return null;}
            string image=path.ToString();
            return new(pid,Path.GetFileNameWithoutExtension(image),image,
                ()=>!handle.IsClosed&&!handle.IsInvalid&&WaitForSingleObject(handle,0)==0x00000102,handle.Dispose);
        } catch {handle.Dispose();throw;}
    }
    static int WindowProcess(nint window)=>GetWindowThreadProcessId(window,out uint pid)!=0&&pid<=int.MaxValue?(int)pid:0;
    [DllImport("user32.dll")] static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(nint window,out uint pid);
    [DllImport("kernel32.dll",SetLastError=true)] static extern SafeProcessHandle OpenProcess(uint access,[MarshalAs(UnmanagedType.Bool)] bool inherit,uint pid);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]
    static extern bool QueryFullProcessImageName(SafeProcessHandle process,uint flags,StringBuilder path,ref uint length);
    [DllImport("kernel32.dll",SetLastError=true)] static extern uint WaitForSingleObject(SafeProcessHandle handle,uint milliseconds);
}
