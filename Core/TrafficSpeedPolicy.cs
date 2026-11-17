using System.Numerics;
namespace MautAssistent;

public sealed record TrafficBody(Vector3 Position,Quaternion Rotation,Vector3 Size,double Speed);
public sealed record TrafficSpeedDecision(float Acceleration,bool Clear,string Reason,double? StopDistance=null,string? StopIdentity=null)
{
    public TrafficBody? LimitingBody {get;init;}
    public double? Gap {get;init;}
    public double? ClosingSpeed {get;init;}
}

public static class TrafficSpeedPolicy
{
    public static TrafficSpeedDecision Evaluate(RouteTrace trace,Vector3 truck,double speed,IEnumerable<TrafficBody> bodies)
    {
        var origin=trace.ProjectForTracking(truck);
        double end=Math.Min(trace.Length,origin.Along+Math.Max(200,speed*speed/2.4+Math.Max(0,speed)*2+40));
        if(end-origin.Along<.01)return new(-.35f,false,"Kein Verkehrs-Vorschaubereich");
        float limit=float.NaN;double? nearestStop=null;bool found=false;
        TrafficBody? leading=null;double? leadingGap=null,leadingClosing=null;
        foreach(var body in bodies) {
            if(!Finite(body.Position)||!Finite(body.Size)||body.Size.X<=0||body.Size.Y<=0||body.Size.Z<=0||
                !double.IsFinite(body.Speed)||Math.Abs(body.Speed)>100||!float.IsFinite(body.Rotation.X)||!float.IsFinite(body.Rotation.Y)||
                !float.IsFinite(body.Rotation.Z)||!float.IsFinite(body.Rotation.W)||Math.Abs(body.Rotation.LengthSquared()-1)>.05)
                return new(-.35f,false,"Verkehrsdaten unvollständig");
            var p=trace.ProjectNearForTracking(body.Position,(origin.Along+end)/2,(end-origin.Along)/2);
            var direction=trace.DirectionAt(p.Along);var right=Vector3.Normalize(Vector3.Cross(direction,Vector3.UnitY));
            if(!Finite(direction)||!Finite(right))return new(-.35f,false,"Verkehrsroute nicht eindeutig gerichtet");
            var x=Vector3.Transform(Vector3.UnitX,body.Rotation);var y=Vector3.Transform(Vector3.UnitY,body.Rotation);
            var z=Vector3.Transform(Vector3.UnitZ,body.Rotation);
            double Radius(Vector3 axis)=>Math.Abs(Vector3.Dot(x,axis))*body.Size.X/2+
                Math.Abs(Vector3.Dot(y,axis))*body.Size.Y/2+Math.Abs(Vector3.Dot(z,axis))*body.Size.Z/2;
            double lateral=Radius(right),length=Radius(direction),vertical=Radius(Vector3.UnitY);
            double along=p.Along-origin.Along;
            if(along+length<0||p.Along>=trace.Length-.01||p.HeightOffset-vertical>4.5||p.HeightOffset+vertical<0||p.Offset>lateral+1.65)continue;
            double gap=Math.Max(.05,along-length-4); // truck origin to own front reserve
            // SDK speed has no dependable signed velocity vector. Treat every
            // intersecting body as at most same-direction; uncertain/crossing
            // orientation receives no optimistic forward-speed credit.
            double alignment=Vector3.Dot(-z,direction);
            double other=alignment>.9?Math.Max(0,body.Speed):alignment<-.9?-Math.Abs(body.Speed):0;
            double closing=Math.Max(0,speed-other);
            double desired=6+Math.Max(0,speed)*2+Math.Max(0,speed)*closing/(2*Math.Sqrt(1.2*1.8));
            float bound=(float)Math.Clamp(1.2*(1-Math.Pow(desired/gap,2)),-4.5,1.2);
            if(!float.IsFinite(limit)||bound<limit){leading=body;leadingGap=gap;leadingClosing=closing;}
            limit=float.IsFinite(limit)?Math.Min(limit,bound):bound;found=true;
            if(other<.2)nearestStop=Math.Min(nearestStop??double.PositiveInfinity,Math.Max(0,gap-6));
        }
        return new(limit,!found,found?"Abstand zum Verkehr auf der eigenen Route":"Aktuelle Verkehrsdaten: eigener Korridor frei",nearestStop,
            nearestStop.HasValue?"leading-stopped-vehicle":null){LimitingBody=leading,Gap=leadingGap,ClosingSpeed=leadingClosing};
    }
    static bool Finite(Vector3 point)=>float.IsFinite(point.X)&&float.IsFinite(point.Y)&&float.IsFinite(point.Z);
}
