using System.Reflection;

public static class PaymentReviewRegressionTests
{
    public static void OlderArrivingPathCannotBorrowNewerAppliedGeneration(Action<bool,string> check)
    {
        var f=new RollingStartIntegration.Fixture(withoutAcc:true);
        f.Call("StopDiagnostics");
        var sync=typeof(MautAssistent.TollPlugin).GetField("sync",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(f.Plugin)!;
        var older=f.Plan()!;
        var newer=RollingStartIntegration.Fixture.Disabled();
        Task? oldWork=null;
        bool newerApplied=false;
        Monitor.Enter(sync);
        try {
            // A arrives first and waits at ProcessPath's lock. This thread owns
            // that lock and can deterministically process B reentrantly first.
            oldWork=Task.Run(()=>f.Call("OnPath",older));
            if(!SpinWait.SpinUntil(()=>f.Get<long>("paymentPathGeneration")==1,TimeSpan.FromSeconds(3)))
                throw new TimeoutException("Older route did not arrive at its held processing lock");
            f.Call("OnPath",newer);
            newerApplied=f.Get<long>("paymentAppliedPathGeneration")==2;
        } finally {Monitor.Exit(sync);}
        if(oldWork==null||!oldWork.Wait(TimeSpan.FromSeconds(3)))throw new TimeoutException("Older route did not finish");
        check(newerApplied,"The latest arriving route can finish processing before an earlier waiting callback");
        check(ReferenceEquals(f.Get<PathLib.PlannedPathData>("path"),older)&&
            f.Get<long>("paymentAppliedPathGeneration")==0&&f.Get<long>("paymentPathGeneration")==2,
            "An older callback applied after the newer route invalidates payment instead of borrowing its applied generation");
        f.Call("OnPath",f.Plan()!);
        check(f.Get<long>("paymentPathGeneration")==3&&f.Get<long>("paymentAppliedPathGeneration")==3,
            "A subsequent latest route can restore payment's applied generation after the stale callback is finished");
    }

    public static void RouteProbeKeepsDrivingLockAvailable(Action<bool,string> check)
    {
        var f=new RollingStartIntegration.Fixture(withoutAcc:true);
        f.Call("StopDiagnostics");
        using var entered=new ManualResetEventSlim();
        using var release=new ManualResetEventSlim();
        var sync=typeof(MautAssistent.TollPlugin).GetField("sync",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(f.Plugin)!;
        f.DuringProbe=()=> {
            entered.Set();
            if(!release.Wait(TimeSpan.FromSeconds(5)))throw new TimeoutException("Isolated route-probe release");
        };
        f.F8();
        var work=Task.Run(f.Probe);
        bool available=false,pending=false;
        try {
            if(!entered.Wait(TimeSpan.FromSeconds(3)))throw new TimeoutException("Isolated route probe did not start");
            pending=f.Get<long>("paymentPathGeneration")!=f.Get<long>("paymentAppliedPathGeneration");
            available=Monitor.TryEnter(sync);
            if(available)Monitor.Exit(sync);
        } finally {
            release.Set();
            if(!work.Wait(TimeSpan.FromSeconds(3)))throw new TimeoutException("Isolated route probe did not finish");
        }
        check(pending,"A native route probe keeps the arriving payment generation unapplied until its checked commit");
        check(available,"A blocked native route probe must leave the driving lock available for tick and lifecycle bookkeeping");
    }
}
