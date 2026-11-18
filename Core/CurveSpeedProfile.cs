using System.Numerics;

namespace MautAssistent;

public sealed record CurveSpeedDecision(double Radius,double CurveDistance,double CurveSpeed,double AllowedSpeed,
    float MaxAcceleration,bool Late)
{ public double LateralAcceleration {get;init;}=1; }

/// <summary>Additional geometric speed bound; not a vehicle stability or obstacle detector.</summary>
public sealed class CurveSpeedProfile
{
    public const double Horizon=600,Spacing=2,LateralAcceleration=1,ResponseSeconds=1.5;
    public const double MaximumHorizon=2000;
    const double HoldBehind=8,Margin=8;
    static readonly double[] Spans=[3,6,12];
    readonly RouteTrace route;
    readonly (double Along,double Curvature)[] bends;
    readonly double coveredFrom,coveredTo;
    CurveSpeedProfile(RouteTrace route,(double,double)[] bends,double from,double to){this.route=route;this.bends=bends;coveredFrom=from;coveredTo=to;}
    public int Count=>bends.Length;
    public bool Covers(RouteTrace current,Vector3 position,double requiredAhead)
    {
        // Curvature windows extend up to 18 m beyond their centre.
        if(!route.MatchesAhead(current,position,requiredAhead+24,out var along,out var currentAlong))return false;
        return along>=coveredFrom&&Math.Min(route.Length,along+Math.Min(requiredAhead+24,current.Length-currentAlong))<=coveredTo+.001;
    }
    public bool HasFullCoverage(RouteTrace current,Vector3 position,double requiredAhead)
        =>current.IdentityKnown&&!current.EndsAtGap&&double.IsFinite(requiredAhead)&&requiredAhead>=0&&
            current.Length-current.ProjectForTracking(position).Along>=requiredAhead+24&&Covers(current,position,requiredAhead);
    public static double RequiredCoverage(double speed,double deceleration)
        =>speed*speed/(2*deceleration)+speed*ResponseSeconds+Margin+24;
    public bool HasSlowerCurve(Vector3 position,double speed,double ahead)
    {
        double along=route.ProjectForTracking(position).Along;
        return bends.Any(b=>b.Along>=along-HoldBehind&&b.Along<=along+ahead&&
            Math.Sqrt(LateralAcceleration/b.Curvature)<speed);
    }
    public static CurveSpeedProfile Build(RouteTrace route,Vector3 origin)=>Build(route,origin,Horizon);
    public static CurveSpeedProfile Build(RouteTrace route,Vector3 origin,double horizon)
    {
        if(!double.IsFinite(horizon)||horizon<1||horizon>MaximumHorizon)throw new ArgumentOutOfRangeException(nameof(horizon));
        double start=Math.Max(0,route.Project(origin).Along-HoldBehind);
        double end=Math.Min(route.Length,start+horizon);
        var bends=new List<(double,double)>();
        for(double center=start;center<=end;center+=Spacing) {
            double confirmed=0;
            foreach(double span in Spans) {
                if(!route.TryCurvature(center-span/2,span,out var left)||!route.TryCurvature(center,span,out var middle)||
                    !route.TryCurvature(center+span/2,span,out var right)||left*middle<=0||right*middle<=0)continue;
                double low=Math.Min(Math.Min(Math.Abs(left),Math.Abs(middle)),Math.Abs(right));
                double high=Math.Max(Math.Max(Math.Abs(left),Math.Abs(middle)),Math.Abs(right));
                // Require a persistent bend; isolated interpolation spikes and
                // opposing S-bend lobes cannot masquerade as one tight circle.
                if(low>=high*.25)confirmed=Math.Max(confirmed,low);
            }
            if(confirmed>.0001)bends.Add((center,confirmed));
        }
        return new(route,bends.ToArray(),start,end);
    }
    public static double LateralFor(double radius,bool brisk)
    {
        if(!brisk||!double.IsFinite(radius)||radius<=150)return LateralAcceleration;
        double blend=Math.Clamp((radius-150)/550,0,1);
        blend=blend*blend*(3-2*blend);
        return LateralAcceleration+.35*blend;
    }
    public CurveSpeedDecision? Evaluate(Vector3 position,double speed,double deceleration)
        =>Evaluate(position,speed,deceleration,false);
    public CurveSpeedDecision? Evaluate(Vector3 position,double speed,double deceleration,bool brisk)
        =>TryEvaluate(position,speed,deceleration,brisk,out var decision)?decision:null;
    // A valid straight profile has no limiting curve. That is different from
    // being unable to evaluate the current vehicle against the profile.
    public bool TryEvaluate(Vector3 position,double speed,double deceleration,bool brisk,out CurveSpeedDecision? decision)
    {
        decision=null;
        if(!float.IsFinite(position.X)||!float.IsFinite(position.Y)||!float.IsFinite(position.Z)||
            !ForwardMotion.TryNormalizeForwardSpeed(speed,out speed)||!double.IsFinite(deceleration)||deceleration is <.5 or >2)return false;
        var projection=route.ProjectForTracking(position);
        if(projection.Offset>3||Math.Abs(projection.HeightOffset)>2)return false;
        double bestAllowed=double.PositiveInfinity,bestRadius=0,bestDistance=0,bestCurveSpeed=0,bestAvailable=0,bestLateral=1;
        foreach(var bend in bends) {
            double distance=bend.Along-projection.Along;
            if(distance< -HoldBehind)continue;
            double radius=1/bend.Curvature,lateral=LateralFor(radius,brisk),curveSpeed=Math.Sqrt(lateral*radius);
            double available=Math.Max(0,distance-speed*ResponseSeconds-Margin);
            double allowed=Math.Sqrt(curveSpeed*curveSpeed+2*deceleration*available);
            if(allowed>=bestAllowed)continue;
            bestAllowed=allowed;bestRadius=radius;bestDistance=distance;bestCurveSpeed=curveSpeed;bestAvailable=available;
            bestLateral=lateral;
        }
        if(!double.IsFinite(bestAllowed))return true;
        float acceleration=bestAllowed>speed+1?float.NaN:(float)Math.Clamp((bestAllowed-speed)*.8,-deceleration,.35);
        double needed=(speed*speed-bestCurveSpeed*bestCurveSpeed)/(2*Math.Max(1,bestAvailable));
        decision=new(bestRadius,bestDistance,bestCurveSpeed,bestAllowed,acceleration,needed>deceleration+.1){LateralAcceleration=bestLateral};
        return true;
    }
}
