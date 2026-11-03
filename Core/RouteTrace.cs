using System.Numerics;

namespace MautAssistent;
public sealed record TraceProjection(double Along, double Offset);
public sealed class RouteTrace
{
    readonly Vector3[] points;
    readonly double[] distances;
    public double Length => distances[^1];
    public bool EndsAtGap { get; init; }
    public string? EndReason { get; init; }
    public RouteTrace(IEnumerable<Vector3> source)
    {
        points=source.ToArray(); distances=new double[points.Length];
        if(points.Length<2 || points.Any(p=>!float.IsFinite(p.X)||!float.IsFinite(p.Y)||!float.IsFinite(p.Z)))
            throw new ArgumentException("Ungültige Routenpunkte");
        for(int i=1;i<points.Length;i++) distances[i]=distances[i-1]+Vector3.Distance(points[i-1],points[i]);
    }
    public TraceProjection Project(Vector3 point)
    {
        double best=double.PositiveInfinity,along=0;
        for(int i=1;i<points.Length;i++)
        {
            var delta=points[i]-points[i-1];
            float len=delta.LengthSquared();
            if(len<.000001)continue;
            float t=Math.Clamp(Vector3.Dot(point-points[i-1],delta)/len,0,1);
            double d=Vector3.DistanceSquared(point,points[i-1]+t*delta);
            if(d<best) {best=d;along=distances[i-1]+t*Math.Sqrt(len);}
        }
        return new(along,Math.Sqrt(best));
    }
    public Vector3 DirectionAt(double along)
    {
        for(int i=1;i<points.Length;i++) {
            var delta=points[i]-points[i-1];delta.Y=0;
            if(delta.LengthSquared()<.000001f)continue;
            if(distances[i]>=along || i==points.Length-1)return Vector3.Normalize(delta);
        }
        return Vector3.Zero;
    }
}
