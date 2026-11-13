using System.Reflection;
using System.Text.Json;
using ETS2LA.Game.Output;
using MautAssistent;

public static class DiagnosticDeliveryIntegration
{
    public static void ConsistentAges(Action<bool,string> assert)
    {
        using var written=new ManualResetEventSlim();string? json=null;int delivered=0;
        var f=new RollingStartIntegration.Fixture(writeDiagnostic:(name,text)=>{
            if(Path.GetFileName(name)=="status.json"){json=text;written.Set();}
        });
        f.F8();f.Probe();f.Fresh();f.Set("lastWrite",-100d);f.Set("lastMapWrite",0d);
        f.DuringReadAcc=()=>{
            delivered++;
            f.Call("OnControlOutput",new ControlEvent {
                ChannelDefinition=new(){Id="LaneAssist.SteeringCorrection"},Variables=new(){steering=.1f}
            });
        };
        try {
            f.Plugin.Tick();
            assert(written.Wait(TimeSpan.FromSeconds(2)),"The test must receive the actual serialized status");
            using var document=JsonDocument.Parse(json!);
            var age=document.RootElement.GetProperty("steeringAge");
            assert(delivered>0&&age.ValueKind==JsonValueKind.Number,"A real steering event must arrive during the driving tick");
            assert(age.GetDouble()>=0&&age.GetDouble()<.15,
                "A command delivered after tick start must have a nonnegative, fresh age in the diagnostic snapshot");
            double previousTick=f.Now;
            f.Call("OnControlOutput",new ControlEvent {
                ChannelDefinition=new(){Id="AdaptiveCruiseControl.Acceleration"},Variables=new(){aforward=.2f}
            });
            f.Call("RecordMotion",previousTick,true);
            var history=(System.Collections.IEnumerable)f.Get<object>("motionHistory");
            var motion=history.Cast<object>().Last();
            double steeringAge=(double)motion.GetType().GetProperty("SteeringAge")!.GetValue(motion)!;
            double accAge=(double)motion.GetType().GetProperty("AccAge")!.GetValue(motion)!;
            assert(steeringAge>=0&&accAge>=0,"Motion diagnostics must use a clock after both captured command references, too");
        } finally {
            f.Call("StopDiagnostics");
            assert(SpinWait.SpinUntil(()=>f.Get<DiagnosticQueue>("diagnostics").IsIdle,TimeSpan.FromSeconds(2)),"Diagnostic worker must settle");
        }
    }
    public static void BlockedStorage(Action<bool,string> assert)
    {
        using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
        var f=new RollingStartIntegration.Fixture(writeDiagnostic:(_,_)=> {
            entered.Set();
            if(!release.Wait(TimeSpan.FromSeconds(5)))throw new TimeoutException("Diagnostic test store was not released");
        });
        f.F8();f.Probe();f.Fresh();f.Set("lastWrite",-100d);f.Set("lastMapWrite",0d);
        var tick=Task.Run(f.Plugin.Tick);Task? acc=null,path=null,takeover=null;
        bool storeEntered=false,tickReturned=false,accReturned=false,pathReturned=false,takeoverReturned=false,healthy=false;
        try {
            storeEntered=entered.Wait(TimeSpan.FromSeconds(1));
            tickReturned=tick.Wait(TimeSpan.FromMilliseconds(300));
            acc=Task.Run(()=>f.Call("Evaluate"));accReturned=acc.Wait(TimeSpan.FromMilliseconds(300));
            path=Task.Run(()=>f.Call("OnPath",f.Plan()));pathReturned=path.Wait(TimeSpan.FromMilliseconds(300));
            healthy=f.State.EnableAssists&&!f.Plugin.FaultBraking;
            takeover=Task.Run(f.F9);takeoverReturned=takeover.Wait(TimeSpan.FromMilliseconds(300));
        } finally {
            release.Set();
            if(!Task.WaitAll(new[]{tick,acc??Task.CompletedTask,path??Task.CompletedTask,takeover??Task.CompletedTask},TimeSpan.FromSeconds(3)))
                throw new Exception("Blocked-storage test tasks did not settle");
            f.Call("StopDiagnostics");
            var queue=f.Get<DiagnosticQueue>("diagnostics");
            if(!SpinWait.SpinUntil(()=>queue.IsIdle,TimeSpan.FromSeconds(2)))
                throw new Exception("Diagnostic worker did not settle after releasing the store");
        }
        assert(storeEntered,"Regression must actually hold a diagnostic write in progress");
        assert(tickReturned,"The driving tick cannot wait for diagnostic storage");
        assert(accReturned,"An occupied diagnostic store cannot delay the ACC control callback");
        assert(pathReturned,"An occupied diagnostic store cannot delay route delivery");
        assert(healthy,"Diagnostic I/O alone cannot manufacture a driving fault");
        assert(takeoverReturned&&!f.State.EnableAssists&&!f.Plugin.FaultBraking,
            "F9 must release controls before the blocked diagnostic store recovers");
    }
}
