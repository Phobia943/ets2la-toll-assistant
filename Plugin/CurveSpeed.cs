namespace MautAssistent;

public sealed partial class TollPlugin
{
    readonly CurvePreview curvePreview;
    CurveSpeedDecision? curveDecision;
    bool curvePreviewReady;
    public string CurveStatus {get {lock(sync) {
        if(curveDecision is not { } curve||!float.IsFinite(curve.MaxAcceleration))return "";
        return $"Kurvenvorschau: jetzt {curve.AllowedSpeed*3.6:F0} km/h · Kurve in {Math.Max(0,curve.CurveDistance):F0} m mit "+
            $"{curve.CurveSpeed*3.6:F0} km/h"+(curve.Late?" · Bremsreserve knapp; bitte manuell bremsbereit bleiben.":"");
    }}}
    double RequiredCurveCoverage=>Math.Min(CurveSpeedProfile.MaximumHorizon-74,
        CurveSpeedProfile.RequiredCoverage(Math.Max(0,Math.Max(speed,State.DesiredSpeed)),options.ComfortableDeceleration));
    double PreferredCruiseCoverage=>CurveSpeedProfile.RequiredCoverage(options.RoadCruiseSpeedKph/3.6,options.ComfortableDeceleration);
    void RequestCurvePreview(RouteTrace trace) {
        if(options.CurvePreviewEnabled)curvePreview.RequestProfile(trace,position,
            options.BriskOpenRoads?Math.Max(RequiredCurveCoverage,PreferredCruiseCoverage):RequiredCurveCoverage);
    }
    float LimitForCurve(float existing)
    {
        curveDecision=null;curvePreviewReady=false;
        if(!options.CurvePreviewEnabled||safetyTrace is not { } trace)return existing;
        RequestCurvePreview(trace);
        bool brisk=options.BriskOpenRoads&&!controller.Tracking&&!router.StopBoundary.HasValue&&!router.HasShortChange&&
            !trace.EndsAtGap&&!(candidate is { } gate&&gate.Distance<=controller.ApproachHorizon(speed));
        var profile=curvePreview.Read(trace,position,RequiredCurveCoverage);
        curvePreviewReady=profile!=null&&profile.TryEvaluate(position,speed,options.ComfortableDeceleration,brisk,out curveDecision);
        if(curveDecision is not {MaxAcceleration:var limit}||!float.IsFinite(limit))return existing;
        return float.IsFinite(existing)?Math.Min(existing,limit):limit;
    }
}
