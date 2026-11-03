using System.Numerics;

namespace MautAssistent;

public sealed class TrackingGuard
{
    RouteTrace? accepted;
    public bool HasRoute => accepted!=null;
    public void Reset() => accepted=null;
    // Planning reserve: one second response, 2 m/s² deceleration, 15 m margin.
    // It is a geometry horizon, not a promise about the vehicle's braking distance.
    public static double RequiredLookahead(double speed) => double.IsFinite(speed)
        ? Math.Max(25,15+Math.Abs(speed)+speed*speed/4) : double.PositiveInfinity;
    public static Vector3 TruckForward(double headingTurns) =>
        new((float)-Math.Sin(headingTurns*2*Math.PI),0,(float)-Math.Cos(headingTurns*2*Math.PI));

    public string? Check(RouteTrace route,Vector3 position,double headingTurns,double speed,bool checkDirection=true)
    {
        if(!double.IsFinite(headingTurns)||!double.IsFinite(speed))return "Ungültige Fahrtrichtungsdaten.";
        var projection=route.Project(position);
        if(!double.IsFinite(projection.Offset)||projection.Offset>3)return "Routenverfolgung verloren: Lkw außerhalb der geplanten Spur.";
        if(route.EndsAtGap && route.Length-projection.Along<=RequiredLookahead(speed))
            return "Planungslücke innerhalb der Anhaltereserve: "+route.EndReason;
        if(checkDirection) {
            var direction=route.DirectionAt(projection.Along);
            if(direction==Vector3.Zero)return "Fahrtrichtung der Route fehlt.";
            if(speed<-.5)return "Rückwärtsfahrt bei eingeschaltetem Autopiloten.";
            // Do not fold opposite headings onto the same road axis.
            if(Vector3.Dot(TruckForward(headingTurns),direction)<0)
                return "Lkw zeigt entgegen der geplanten Fahrtrichtung.";
            if(accepted!=null&&!ReferenceEquals(accepted,route)) {
                var old=accepted.Project(position);
                if(old.Offset<5&&Vector3.Dot(accepted.DirectionAt(old.Along),direction)<0)
                    return "Neu berechnete Route kehrt die bisherige Fahrtrichtung um.";
            }
        }
        accepted=route;
        return null;
    }
}
