using System.Numerics;

namespace MautAssistent;

public sealed record CurveSpeedDecision(double Radius,double CurveDistance,double CurveSpeed,double AllowedSpeed,
    float MaxAcceleration,bool Late);

/// <summary>Additional geometric speed bound; not a vehicle stability or obstacle detector.</summary>
public sealed class CurveSpeedProfile
{
    public const double Horizon=600,Spacing=2,LateralAcceleration=1,ResponseSeconds=1.5;
    const double HoldBehind=8,Margin=8;
    static readonly double[] Spans=[3,6,12];
    readonly RouteTrace route;
    readonly (double Along,double Curvature)[] bends;
    CurveSpeedProfile(RouteTrace route,(double,double)[] bends){this.route=route;this.bends=bends;}
    public int Count=>bends.Length;
    public static CurveSpeedProfile Build(RouteTrace route,Vector3 origin)
    {
        double start=Math.Max(0,route.Project(origin).Along-HoldBehind);
        double end=Math.Min(route.Length,start+Horizon);
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
        return new(route,bends.ToArray());
    }
    public CurveSpeedDecision? Evaluate(Vector3 position,double speed,double deceleration)
    {
        if(!double.IsFinite(speed)||speed<0||!double.IsFinite(deceleration)||deceleration is <.5 or >2)return null;
        var projection=route.ProjectForTracking(position);
        if(projection.Offset>3||Math.Abs(projection.HeightOffset)>2)return null;
        double bestAllowed=double.PositiveInfinity,bestRadius=0,bestDistance=0,bestCurveSpeed=0,bestAvailable=0;
        foreach(var bend in bends) {
            double distance=bend.Along-projection.Along;
            if(distance< -HoldBehind)continue;
            double radius=1/bend.Curvature,curveSpeed=Math.Sqrt(LateralAcceleration*radius);
            double available=Math.Max(0,distance-speed*ResponseSeconds-Margin);
            double allowed=Math.Sqrt(curveSpeed*curveSpeed+2*deceleration*available);
            if(allowed>=bestAllowed)continue;
            bestAllowed=allowed;bestRadius=radius;bestDistance=distance;bestCurveSpeed=curveSpeed;bestAvailable=available;
        }
        if(!double.IsFinite(bestAllowed))return null;
        float acceleration=bestAllowed>speed+1?float.NaN:(float)Math.Clamp((bestAllowed-speed)*.8,-deceleration,.35);
        double needed=(speed*speed-bestCurveSpeed*bestCurveSpeed)/(2*Math.Max(1,bestAvailable));
        return new(bestRadius,bestDistance,bestCurveSpeed,bestAllowed,acceleration,needed>deceleration+.1);
    }
}
