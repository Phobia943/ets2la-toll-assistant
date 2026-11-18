using System.Diagnostics;
using System.Text.Json;
using ETS2LA.Game.Output;
using MautAssistent;

public static class ControlTickDiagnosticReviewTests
{
    public static void OverlappingTicks(Action<bool,string> check)
    {
        using var firstInside=new ManualResetEventSlim();using var releaseFirst=new ManualResetEventSlim();
        using var secondInside=new ManualResetEventSlim();using var releaseSecond=new ManualResetEventSlim();
        var f=new RollingStartIntegration.Fixture(withoutAcc:true,writeDiagnostic:(_,_)=>{});
        f.Set("options",new TollOptions {CurvePreviewEnabled=false,BriskOpenRoads=false});
        f.F8();f.Probe();f.Fresh();f.Plugin.Tick();
        var initial=f.Get<TollPlugin.ControlTickObservation>("controlTickObservation");
        int calls=0;Task? first=null,second=null;long firstReleasedAt=0;
        f.DuringReadTraffic=()=> {
            if(Interlocked.Increment(ref calls)==1) {
                firstInside.Set();if(!releaseFirst.Wait(TimeSpan.FromSeconds(5)))throw new TimeoutException();
                firstReleasedAt=Stopwatch.GetTimestamp();
            } else {secondInside.Set();if(!releaseSecond.Wait(TimeSpan.FromSeconds(5)))throw new TimeoutException();}
        };
        try {
            f.Fresh();first=Task.Run(f.Plugin.Tick);
            check(firstInside.Wait(TimeSpan.FromSeconds(2)),"The first actual Tick is held inside decision work");
            second=Task.Run(f.Plugin.Tick);
            check(SpinWait.SpinUntil(()=>f.Get<TollPlugin.ControlTickObservation>("controlTickObservation").Phase==
                TollPlugin.ControlTickPhase.WaitingForRoute,TimeSpan.FromSeconds(2)),"A newer actual Tick is waiting for the same route lock");
            var waiting=f.Get<TollPlugin.ControlTickObservation>("controlTickObservation");
            releaseFirst.Set();
            check(first.Wait(TimeSpan.FromSeconds(2))&&secondInside.Wait(TimeSpan.FromSeconds(2)),
                "The older Tick finishes while the newer Tick remains in progress");
            var observed=f.Get<TollPlugin.ControlTickObservation>("controlTickObservation");
            check(observed.StartedAt==waiting.StartedAt&&observed.Epoch==waiting.Epoch&&
                observed.Phase==TollPlugin.ControlTickPhase.Longitudinal,
                "Completion of the older producer cannot overwrite the newer phase, epoch or original start");
            check(observed.LastCompletedAt>=firstReleasedAt&&observed.LastCompletedAt>initial.LastCompletedAt,
                "The older Tick's actual completion remains visible while the newer Tick is unfinished");
        } finally {
            releaseFirst.Set();releaseSecond.Set();
            first?.Wait(TimeSpan.FromSeconds(2));second?.Wait(TimeSpan.FromSeconds(2));f.Call("StopDiagnostics");
        }
    }

    public static void BlockedProducer(Action<bool,string> check)
    {
        string? report=null;
        using var written=new ManualResetEventSlim();
        var f=new RollingStartIntegration.Fixture(true,withoutAcc:true,writeDiagnostic:(path,text)=> {
            if(Path.GetFileName(path)=="last-fault.json"){report=text;written.Set();}
        });
        f.Set("steeringMonitoringLive",true);
        f.Set("options",new TollOptions {CurvePreviewEnabled=false,BriskOpenRoads=false});
        f.F8();f.Probe();
        void Lane()=>f.Call("OnControlOutput",new ControlEvent {
            ChannelDefinition=new(){Id="LaneAssist.SteeringCorrection"},Variables=new(){steering=0}
        });
        Lane();f.Call("CheckSteeringOutput");f.Fresh();f.Plugin.Tick();
        var first=f.Get<TollPlugin.LongitudinalSnapshot>("longitudinalSnapshot");
        var completed=f.Get<TollPlugin.ControlTickObservation>("controlTickObservation");
        // The DI event sink has no patched host fields. Preserve the exact
        // actual Tick origin in the accepted command for the native-clock test.
        var command=f.Get<object>("lastAccCommand");
        command.GetType().GetProperty("IssuedTicks")!.SetValue(command,first.IssuedAt);
        command.GetType().GetProperty("Epoch")!.SetValue(command,first.Epoch);
        f.Call("CheckLongitudinalOutput");
        check(first.Published&&completed.Phase==TollPlugin.ControlTickPhase.Completed&&
            completed.StartedAt==first.IssuedAt&&completed.LastCompletedAt>=completed.StartedAt,
            "The real completed producer retains its original output stamp and actual end time");

        var gate=f.Get<object>("sync");Task? blocked=null;
        TollPlugin.PedalFailure? failure=null;
        Monitor.Enter(gate);
        try {
            blocked=Task.Run(f.Plugin.Tick);
            check(SpinWait.SpinUntil(()=>f.Get<TollPlugin.ControlTickObservation>("controlTickObservation").Phase==
                TollPlugin.ControlTickPhase.WaitingForRoute,TimeSpan.FromSeconds(2)),
                "The real next Tick exposes its route-lock wait before it can enter the control body");
            for(int i=0;i<6;i++){Thread.Sleep(32);Lane();f.Call("CheckSteeringOutput");}
            f.Call("CheckLongitudinalOutput");
            failure=f.Get<TollPlugin.PedalFailure>("pendingPedalFailure");
            check(f.Plugin.FaultBraking&&!f.State.EnableAssists&&failure.CommandAge>.15&&
                failure.IssuedAt==first.IssuedAt&&failure.Epoch==first.Epoch,
                "A stalled producer trips on the original accepted pedal age without a start-grace extension");
            check(failure.Producer is {Phase:TollPlugin.ControlTickPhase.WaitingForRoute} producer&&
                producer.StartedAt>first.IssuedAt&&producer.PhaseAt>=producer.StartedAt&&
                producer.LastCompletedAt==completed.LastCompletedAt&&producer.ClockFrequency==Stopwatch.Frequency,
                "The independent watchdog freezes the blocked Tick phase and prior completion without taking the route lock");
        } finally {Monitor.Exit(gate);}
        try {
            check(blocked!=null&&blocked.Wait(TimeSpan.FromSeconds(2)),"The blocked producer settles after the fault");
            check(written.Wait(TimeSpan.FromSeconds(2)),"The permanent fault report contains the pedal watchdog evidence");
            using var document=JsonDocument.Parse(report!);
            var recorded=document.RootElement.GetProperty("pedalFailure");
            check(recorded.GetProperty("IssuedAt").GetInt64()==first.IssuedAt&&
                recorded.GetProperty("Producer").GetProperty("Phase").GetInt32()==(int)TollPlugin.ControlTickPhase.WaitingForRoute&&
                recorded.GetProperty("Producer").GetProperty("LastCompletedAt").GetInt64()==completed.LastCompletedAt,
                "Serialization retains the pre-trip phase even after a later Tick completes");
            var frozen=failure!.Producer;
            f.F9();f.Fresh();f.Plugin.Tick();
            check(!f.Plugin.FaultBraking&&failure.Producer==frozen&&failure.IssuedAt==first.IssuedAt,
                "Manual cancellation and subsequent Tick observation cannot rewrite the captured incident origin");
        } finally {f.Call("StopDiagnostics");}
    }

    public static void LateDecision(Action<bool,string> check)
    {
        var f=new RollingStartIntegration.Fixture(withoutAcc:true,writeDiagnostic:(_,_)=>{});
        f.Set("options",new TollOptions {CurvePreviewEnabled=false,BriskOpenRoads=false});
        f.F8();f.Probe();f.Fresh();
        TollPlugin.ControlTickObservation? inDecision=null;
        f.DuringReadTraffic=()=> {
            inDecision=f.Get<TollPlugin.ControlTickObservation>("controlTickObservation");
            Thread.Sleep(220);
        };
        try {
            f.Plugin.Tick();
            var output=f.Get<TollPlugin.LongitudinalSnapshot>("longitudinalSnapshot");
            var complete=f.Get<TollPlugin.ControlTickObservation>("controlTickObservation");
            check(inDecision?.Phase==TollPlugin.ControlTickPhase.Longitudinal&&
                inDecision.StartedAt==output.IssuedAt&&complete.StartedAt==output.IssuedAt,
                "A slow actual decision retains one producer origin through every diagnostic phase");
            check(!output.Published&&complete.Phase==TollPlugin.ControlTickPhase.Completed&&
                complete.LastCompletedAt-output.IssuedAt>Stopwatch.Frequency*.2&&
                f.Outputs.All(x=>x.ChannelDefinition.Id!=TollPlugin.LongitudinalChannelId),
                "Diagnostic completion never rejuvenates an expired pedal decision or publishes old output");
        } finally {f.Call("StopDiagnostics");}
    }
}
