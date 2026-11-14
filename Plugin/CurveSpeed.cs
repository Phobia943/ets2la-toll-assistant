namespace MautAssistent;

public sealed partial class TollPlugin
{
    readonly CurvePreview curvePreview;
    CurveSpeedDecision? curveDecision;
    public string CurveStatus {get {lock(sync) {
        if(curveDecision is not { } curve||!float.IsFinite(curve.MaxAcceleration))return "";
        return $"Kurvenvorschau: jetzt {curve.AllowedSpeed*3.6:F0} km/h · Kurve in {Math.Max(0,curve.CurveDistance):F0} m mit "+
            $"{curve.CurveSpeed*3.6:F0} km/h"+(curve.Late?" · Bremsreserve knapp; bitte manuell bremsbereit bleiben.":"");
    }}}
    void RequestCurvePreview(RouteTrace trace) {if(options.CurvePreviewEnabled)curvePreview.RequestProfile(trace,position);}
    float LimitForCurve(float existing)
    {
        curveDecision=null;
        if(!options.CurvePreviewEnabled||safetyTrace is not { } trace)return existing;
        RequestCurvePreview(trace);
        curveDecision=curvePreview.Read(trace)?.Evaluate(position,speed,options.ComfortableDeceleration);
        if(curveDecision is not {MaxAcceleration:var limit}||!float.IsFinite(limit))return existing;
        return float.IsFinite(existing)?Math.Min(existing,limit):limit;
    }
}
