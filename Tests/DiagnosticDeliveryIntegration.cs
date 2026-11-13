using System.Reflection;
using MautAssistent;

public static class DiagnosticDeliveryIntegration
{
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
