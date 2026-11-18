using System.Numerics;
using MautAssistent;

// Independent review regression: a completed straight profile and a missing
// profile used to collapse to the same null curve decision in the real Tick.
public static class CurveReadinessReviewTests
{
    public static void StraightRoad(Action<bool,string> check)
    {
        var f=new RollingStartIntegration.Fixture(withoutAcc:true,writeDiagnostic:(_,_)=>{});
        f.Set("options",new TollOptions{CurvePreviewEnabled=true,BriskOpenRoads=false});
        f.F8();f.Probe();f.State.DesiredSpeed=25;f.Fresh(20);
        var trace=f.Get<RouteTrace>("safetyTrace");
        var preview=f.Get<CurvePreview>("curvePreview");
        f.Call("RequestCurvePreview",trace);
        check(SpinWait.SpinUntil(()=>preview.Read(trace,f.Point,200)!=null,TimeSpan.FromSeconds(2)),
            "The real straight route must have a completed current geometric profile");
        check(preview.Read(trace,f.Point,200)!.Count==0,
            "This regression must exercise an actually straight profile without future bends");
        f.Fresh(20);f.Plugin.Tick();
        var result=f.Get<TollPlugin.LongitudinalSnapshot>("longitudinalSnapshot");
        check(!result.Bounds.Any(b=>b.Source=="CurvePending")&&result.Output.Brake==0&&result.Output.Throttle>0,
            "A fully checked straight road must accelerate normally instead of permanent CurvePending braking");

        // A pending rebuild is genuinely unknown: it cannot borrow the prior
        // straight result after the route cache was revoked.
        preview.Clear();
        f.Set("safetyTrace",new RouteTrace([f.Point,f.Point+new Vector3(0,0,20)]));
        int before=f.Outputs.Count;
        f.Fresh(20);f.Plugin.Tick();
        check(f.Outputs.Skip(before).All(c=>c.Variables.aforward is null or <=0),
            "Insufficient or revoked forward geometry cannot inherit the straight-road throttle permission");
        f.F9();f.Call("StopDiagnostics");
    }

    public static void DistinctOutcomes(Action<bool,string> check)
    {
        var origin=Vector3.Zero;
        var straight=new RouteTrace(Enumerable.Range(0,901).Select(i=>new Vector3(0,0,i)));
        var profile=CurveSpeedProfile.Build(straight,origin);
        check(profile.TryEvaluate(origin,20,1.2,false,out var clear)&&clear==null,
            "A valid straight profile explicitly distinguishes no bound from no evaluation");
        foreach(var point in new[]{new Vector3(4,0,0),new Vector3(0,3,0),new Vector3(float.NaN,0,0)})
            check(!profile.TryEvaluate(point,20,1.2,false,out _),
                "Out-of-corridor or nonfinite position cannot become a clear-road result");
        foreach(double speed in new[]{-.2,double.NaN,double.PositiveInfinity})
            check(!profile.TryEvaluate(origin,speed,1.2,false,out _),
                "Invalid forward speed cannot borrow the no-curve state");
        var curve=CurveSpeedProfile.Build(CurveSpeedTests.Approach(),origin);
        check(curve.TryEvaluate(CurveSpeedTests.Point(90),25,1.2,false,out var bounded)&&bounded?.MaxAcceleration<0,
            "A genuine upcoming bend retains the existing deceleration bound");
    }
}
