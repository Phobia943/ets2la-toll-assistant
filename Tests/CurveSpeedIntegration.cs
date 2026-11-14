using MautAssistent;

public static class CurveSpeedIntegration
{
    public static void ActiveBound(Action<bool,string> assert)
    {
        var f=new RollingStartIntegration.Fixture();f.F8();f.Probe();f.Fresh(25);
        var trace=CurveSpeedTests.Approach(f.Point,approach:150);
        f.Set("safetyTrace",trace);f.Call("RequestCurvePreview",trace);
        var preview=f.Get<CurvePreview>("curvePreview");
        assert(SpinWait.SpinUntil(()=>preview.Read(trace)!=null,TimeSpan.FromSeconds(2)),"The actual accepted route must receive its own preview");
        f.Fresh(25);f.Plugin.Tick();f.Fresh(25);
        float limit=(float)f.Call("Evaluate")!;
        assert(limit<0&&limit>=-1.20001&&f.State.EnableAssists&&!f.Plugin.FaultBraking&&f.Plugin.CurveStatus.Contains("Kurvenvorschau"),
            "The real ACC callback must apply the comfort curve bound without fault braking");
        f.Set("options",new TollOptions{CurvePreviewEnabled=false});f.Fresh(25);
        assert(float.IsNaN((float)f.Call("Evaluate")!)&&f.Plugin.CurveStatus==""&&f.State.EnableAssists,
            "Disabling only this preview restores the native bound without dropping the assist session");
        f.Set("options",new TollOptions());
        f.Set("decision",new TollDecision("Existing stronger stop",0,-2.5f,null));f.Fresh(25);
        assert((float)f.Call("Evaluate")! ==-2.5f,"A stronger toll stop cannot be weakened by the curve limiter");
        f.State.DrivingMode=ETS2LA.State.DrivingMode.LaneAssistOnly;
        assert(float.IsNaN((float)f.Call("Evaluate")!),"Lane-only mode must not acquire automatic pedal control");
        f.F9();assert(preview.Read(trace)==null&&f.Plugin.CurveStatus==""&&!f.State.EnableAssists&&!f.Plugin.FaultBraking,
            "F9 clears the profile, speed display and assist state");
    }
    public static void BlockedWorker(Action<bool,string> assert)
    {
        using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
        var f=new RollingStartIntegration.Fixture(buildCurveProfile:(route,origin)=>{
            entered.Set();if(!release.Wait(TimeSpan.FromSeconds(5)))throw new TimeoutException();
            return CurveSpeedProfile.Build(route,origin);
        });
        try {
            f.F8();f.Probe();assert(entered.Wait(TimeSpan.FromSeconds(2)),"The actual preview builder must be held");
            f.Fresh();f.Plugin.Tick();f.Fresh();
            var evaluate=Task.Run(()=>f.Call("Evaluate"));
            assert(evaluate.Wait(TimeSpan.FromMilliseconds(300))&&f.State.EnableAssists&&!f.Plugin.FaultBraking,
                "A held preview builder cannot block ACC or invent a tracking fault");
            var route=Task.Run(()=>f.Call("OnPath",f.Plan()));
            assert(route.Wait(TimeSpan.FromMilliseconds(300)),"Route delivery cannot wait for optional curve work");
            var manual=Task.Run(f.F9);
            assert(manual.Wait(TimeSpan.FromMilliseconds(300))&&!f.State.EnableAssists&&!f.Plugin.FaultBraking,
                "Manual takeover cannot wait for optional curve work");
        } finally {release.Set();}
        var cache=f.Get<CurvePreview>("curvePreview");
        assert(SpinWait.SpinUntil(()=>cache.IsIdle,TimeSpan.FromSeconds(2))&&f.Plugin.CurveStatus=="",
            "A late preview completion cannot rearm the canceled session");
    }
}
