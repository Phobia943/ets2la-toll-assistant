using System.Diagnostics;
using MautAssistent;

public static class GameInputContextIntegration
{
    public static void Run(Action<bool,string> assert)
    {
        const long frequency=1_000_000;long now=1_000_000;
        string path=Path.Combine(Path.GetTempPath(),"maut-input-context-test","bin","win_x64","eurotrucks2.exe");
        bool alive=true;int foreground=42,reads=0,releases=0,nativeReads=0;
        GameProcessIdentity? Read(int pid){reads++;return new(pid,"eurotrucks2",path,()=>alive,()=>releases++);}
        var context=new GameInputContext([path],Read,()=>{nativeReads++;return 5;},_=>foreground,()=>now);
        assert(reads==0&&context.Capture(42).GameForeground==null&&reads==0,"Constructing/capturing unverified input context performs no process discovery");
        assert(context.RefreshProcessIdentity(42),"Exact allowlisted game image and live original process are verified outside Capture");
        var first=context.Capture(42);
        assert(first.GameForeground==true&&first.GamePid==42&&first.ForegroundPid==42&&first.IsFreshGameForeground(now,frequency),"Current verified game PID in foreground produces a fresh immutable witness");
        for(int i=0;i<30;i++)context.Capture(42);
        assert(reads==1&&nativeReads==31,"Repeated captures use cached identity and only cheap native foreground/alive observations");
        foreground=99;var other=context.Capture(42);
        assert(other.GameForeground==false&&!other.IsFreshGameForeground(now,frequency)&&first.GameForeground==true,"Window switch is observed independently; earlier snapshot is not mutated or refreshed");
        now+=100001;
        assert(!first.IsFreshGameForeground(now,frequency)&&!first.IsFreshGameForeground(first.CapturedAt-1,frequency)&&!first.IsFreshGameForeground(now,0),"Old/future-origin/invalid-frequency snapshots cannot authorize input");
        foreground=42;alive=false;
        assert(context.Capture(42).Status==GameInputContextStatus.ProcessExited,"Original process exit revokes a cached identity even if a reused PID owns the foreground");
        context.Dispose();context.Dispose();
        assert(releases==1&&context.Capture(42).Status==GameInputContextStatus.Disposed,"Dispose releases original process handle once and makes later captures unknown");
        using(var wrong=new GameInputContext([path],pid=>new(pid,"eurotrucks2",path+".other",()=>true),()=>5,_=>42,()=>now))
            assert(!wrong.RefreshProcessIdentity(42)&&wrong.Capture(42).GameForeground==null,"Matching process name at a different executable path is not a game identity");
        using(var wrongName=new GameInputContext([path],pid=>new(pid,"other",path,()=>true),()=>5,_=>42,()=>now))
            assert(!wrongName.RefreshProcessIdentity(42),"An allowlisted pathname does not override a contradictory process name");
        using(var wrongPid=new GameInputContext([path],pid=>new(pid+1,"eurotrucks2",path,()=>true),()=>5,_=>42,()=>now))
            assert(!wrongPid.RefreshProcessIdentity(42),"A query result for another PID never authorizes the requested game");
        using(var failed=new GameInputContext([path],pid=>throw new IOException(),()=>5,_=>42,()=>now))
            assert(!failed.RefreshProcessIdentity(42)&&failed.Capture(42).GameForeground==null,"Unavailable process identity remains unknown instead of name-based fallback");
        using(var none=new GameInputContext([path],Read,()=>0,_=>42,()=>now)) {
            alive=true;none.RefreshProcessIdentity(42);assert(none.Capture(42).Status==GameInputContextStatus.NoForegroundWindow,"Missing foreground window is unknown, not confirmed game focus");
        }
        using(var noPid=new GameInputContext([path],Read,()=>5,_=>0,()=>now)) {
            noPid.RefreshProcessIdentity(42);assert(noPid.Capture(42).Status==GameInputContextStatus.NativeFailure,"Unreadable window PID is unknown");
        }
        using(var delayed=new GameInputContext([path],Read,()=>{now+=150000;return 5;},_=>42,()=>now)) {
            delayed.RefreshProcessIdentity(42);var snapshot=delayed.Capture(42);
            assert(!snapshot.IsFreshGameForeground(now,frequency),"Slow native observation retains capture-start timestamp instead of freshly stamping an old focus read");
        }
        int reusedReads=0;bool originalAlive=true;
        using(var reused=new GameInputContext([path],pid=>{reusedReads++;return reusedReads==1?
            new(pid,"eurotrucks2",path,()=>originalAlive):new(pid,"eurotrucks2",path+".other",()=>true);},()=>5,_=>42,()=>now)) {
            reused.RefreshProcessIdentity(42);originalAlive=false;
            assert(!reused.RefreshProcessIdentity(42)&&reused.Capture(42).GameForeground==null,"PID reuse after original exit requires a new exact-path verification and rejects a different image");
        }
        bool aliveDuringRead=true;
        using(var exitsDuringRead=new GameInputContext([path],pid=>new(pid,"eurotrucks2",path,()=>aliveDuringRead),()=>5,
            _=>{aliveDuringRead=false;return 42;},()=>now)) {
            exitsDuringRead.RefreshProcessIdentity(42);
            assert(exitsDuringRead.Capture(42).Status==GameInputContextStatus.ProcessExited,"Original process ending during the window PID read cannot yield positive foreground authority");
        }
        GameInputContext? disposedDuringRead=null;
        using(disposedDuringRead=new([path],pid=>new(pid,"eurotrucks2",path,()=>true),()=>5,
            _=>{disposedDuringRead!.Dispose();return 42;},()=>now)) {
            disposedDuringRead.RefreshProcessIdentity(42);
            assert(disposedDuringRead.Capture(42).Status==GameInputContextStatus.Disposed,"Dispose during the native read is rechecked before a foreground result is accepted");
        }
        GameInputContext? changedDuringRead=null;
        using(changedDuringRead=new([path],pid=>new(pid,"eurotrucks2",path,()=>true),()=>5,
            _=>{changedDuringRead!.RefreshProcessIdentity(43);return 42;},()=>now)) {
            changedDuringRead.RefreshProcessIdentity(42);
            assert(changedDuringRead.Capture(42).Status==GameInputContextStatus.UnverifiedGameProcess,"A concurrently replaced verified identity cannot authorize the preceding PID");
        }
        Diagnosis(assert,now,frequency);
        Lifecycle(assert,path);
    }
    static void Diagnosis(Action<bool,string> assert,long now,long frequency)
    {
        var focused=new GameInputContextSnapshot(now,42,42,GameInputContextStatus.GameForeground,"test");
        var unfocused=new GameInputContextSnapshot(now,99,42,GameInputContextStatus.OtherForeground,"test");
        var sample=new PedalDeliverySample(now,now,.6,0,.6,0,0,0,.9,12,11,1400);
        GameInputDiagnosisResult Check(PedalDeliverySample s,GameInputContextSnapshot? f=null)=>GameInputDiagnosis.Evaluate(s,f??focused,now,frequency);
        assert(Check(sample).Code=="SimulationLimited"&&!Check(sample).ReceiptMissing,"Received throttle with zero effective throttle is simulation-limited, not a delivery failure or proven gearshift");
        assert(Check(sample,unfocused).Code=="SimulationLimited","Background window alone does not convert received input into delivery loss");
        var missing=sample with{ReceivedThrottle=0};
        assert(Check(missing).Code=="ReceiptMissing"&&Check(missing).ReceiptMissing,"Absent logical input is distinguished from ineffective but received input");
        assert(Check(missing,unfocused).Code=="UnfocusedReceiptMissing","Unfocused missing input is recorded as a correlation without claiming a proven cause");
        assert(Check(sample with{DeviceActive=false}).Code=="InputDeviceInactive","An explicit fresh SDK inactive-device observation is distinct from a window-focus observation");
        assert(Check(sample with{LastInputFrameAt=now-frequency}).Code=="InputCallbackStalled","A stale input callback witness is not a current transport acknowledgement");
        assert(Check(sample with{ObservedAt=now-frequency}).Code=="Unknown"&&Check(sample with{CommandIssuedAt=now+1}).Code=="Unknown","Stale telemetry or future commands cannot be rejuvenated by a new focus snapshot");
        assert(Check(sample with{ObservedAt=now-1000,ReceivedThrottle=0}).Code=="AwaitingObservation","Telemetry predating the command is not evidence that its delivery failed");
        assert(Check(sample with{ReceivedThrottle=.2}).Code=="ReceiptDifferent","Partial or competing logical pedal values are distinguished from both absence and matching receipt");
        assert(Check(sample with{ReceivedThrottle=null}).Code=="ReceiptUnknown"&&Check(sample with{ReceivedBrake=double.NaN}).Code=="ReceiptUnknown","Missing/nonfinite logical inputs remain unknown");
        assert(Check(sample with{EffectiveThrottle=double.PositiveInfinity}).Code=="EffectUnknown","Invalid effective values do not become a normal transmission state");
        assert(Check(sample with{CommandBrake=.4}).Code=="Unknown"&&Check(sample with{CommandThrottle=-.1}).Code=="Unknown","Conflicting or out-of-range pedal commands cannot produce a delivery diagnosis");
        var stale=focused with{CapturedAt=now-frequency};
        assert(Check(sample,stale).GameForeground==null,"An old foreground witness is not renewed by fresh pedal telemetry");
        var brake=new PedalDeliverySample(now,now,0,.6,0,.6,0,.6);
        assert(Check(brake).Code=="Observed"&&!Check(brake).ReceiptMissing&&!Check(brake).SimulationLimited,"Normal received/effective braking remains separately observed");
    }
    static void Lifecycle(Action<bool,string> assert,string path)
    {
        var f=new RollingStartIntegration.Fixture(withoutAcc:true);f.Call("StopDiagnostics");
        using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
        int created=0,released=0;
        f.Set("inputContextReadInstallations",(Func<string[]>)(()=>[path]));
        f.Set("inputContextFindProcesses",(Func<string[],int[]>)(_=>[42]));
        f.Set("inputContextFactory",(Func<string[],GameInputContext>)(paths=> {
            int generation=Interlocked.Increment(ref created);
            return new(paths,pid=>{if(generation==1){entered.Set();if(!release.Wait(5000))throw new TimeoutException();}
                return new(pid,"eurotrucks2",path,()=>true,()=>Interlocked.Increment(ref released));},()=>5,_=>42);
        }));
        try {
            f.Call("StartInputContext");var old=f.Get<Task>("inputContextWorker");
            assert(entered.Wait(3000),"Isolated lifecycle worker reaches injected read-only process lookup without SDK/output activity");
            f.Call("StopInputContext");
            assert(((GameInputContextSnapshot)f.Call("ReadInputContext")!).GameForeground==null,"Stopping publishes unknown immediately without waiting for blocked discovery");
            f.Call("StartInputContext");var current=f.Get<Task>("inputContextWorker");
            bool ready=SpinWait.SpinUntil(()=>((GameInputContextSnapshot)f.Call("ReadInputContext")!).GameForeground==true,3000);
            assert(ready,"A new lifecycle generation can verify focus while the old discovery remains blocked");
            release.Set();assert(old.Wait(3000),"Cancelled old discovery releases its process identity and completes");
            assert(((GameInputContextSnapshot)f.Call("ReadInputContext")!).GameForeground==true,"Late old-worker cleanup cannot revoke or overwrite the new generation");
            f.Call("StopInputContext");assert(current.Wait(3000),"Current worker stops without app or input operations");
            assert(released==2&&((GameInputContextSnapshot)f.Call("ReadInputContext")!).GameForeground==null,"Both owned handles are released exactly once and stopped lifecycle cannot authorize focus");
        } finally {release.Set();f.Call("StopInputContext");}
    }
}
