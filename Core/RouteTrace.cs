using System.Numerics;

namespace MautAssistent;
public sealed record TraceProjection(double Along, double Offset, int SegmentEndIndex=0,double HeightOffset=0);
public sealed class RouteTrace
{
    readonly Vector3[] points;
    readonly double[] distances;
    readonly HashSet<int> connectorEnds;
    public double Length => distances[^1];
    public bool EndsAtGap { get; init; }
    public string? EndReason { get; init; }
    public RouteTrace(IEnumerable<Vector3> source,IEnumerable<int>? connectorEndIndexes=null)
    {
        points=source.ToArray(); distances=new double[points.Length];
        connectorEnds=connectorEndIndexes?.ToHashSet()??[];
        if(points.Length<2 || points.Any(p=>!float.IsFinite(p.X)||!float.IsFinite(p.Y)||!float.IsFinite(p.Z)))
            throw new ArgumentException("Ungültige Routenpunkte");
        if(connectorEnds.Any(i=>i<1||i>=points.Length||Vector3.Distance(points[i-1],points[i])>2))
            throw new ArgumentException("Ungültige Abschnittsverbindung");
        for(int i=1;i<points.Length;i++) distances[i]=distances[i-1]+Vector3.Distance(points[i-1],points[i]);
    }
    public TraceProjection Project(Vector3 point)
    {
        double best=double.PositiveInfinity,along=0;int segment=0;
        for(int i=1;i<points.Length;i++)
        {
            var delta=points[i]-points[i-1];
            float len=delta.LengthSquared();
            if(len<.000001)continue;
            float t=Math.Clamp(Vector3.Dot(point-points[i-1],delta)/len,0,1);
            double d=Vector3.DistanceSquared(point,points[i-1]+t*delta);
            if(d<best) {best=d;along=distances[i-1]+t*Math.Sqrt(len);segment=i;}
        }
        return new(along,Math.Sqrt(best),segment);
    }
    public TraceProjection ProjectForTracking(Vector3 point)
    {
        // Keep the 3D segment choice, including stacked road levels, and measure
        // lane departure horizontally like the native pathfinder.
        var projection=Project(point);int i=projection.SegmentEndIndex;
        if(i<1)return projection;
        double length=distances[i]-distances[i-1];
        float factor=length>0?(float)((projection.Along-distances[i-1])/length):0;
        var nearest=Vector3.Lerp(points[i-1],points[i],factor);
        double horizontal=Vector2.Distance(new(point.X,point.Z),new(nearest.X,nearest.Z));
        return projection with {Offset=horizontal,HeightOffset=point.Y-nearest.Y};
    }
    public Vector3 DirectionAt(double along)
    {
        Vector3 previous=Vector3.Zero;
        for(int i=1;i<points.Length;i++) {
            // The <=2 m seam between separately sampled planned items is only
            // geometric continuity. It is not a directed driving segment.
            if(connectorEnds.Contains(i))continue;
            var delta=points[i]-points[i-1];delta.Y=0;
            if(delta.LengthSquared()<.000001f)continue;
            previous=Vector3.Normalize(delta);
            if(distances[i]>=along)return previous;
        }
        return previous;
    }
    public object Diagnose(Vector3 position)
    {
        var p=Project(position);var direction=DirectionAt(p.Along);
        int first=Math.Max(0,p.SegmentEndIndex-12),last=Math.Min(points.Length-1,p.SegmentEndIndex+12);
        return new {Length,EndsAtGap,EndReason,projection=p,trackingProjection=ProjectForTracking(position),
            direction=new{x=direction.X,y=direction.Y,z=direction.Z},
            samples=Enumerable.Range(first,last-first+1).Select(i=>new{index=i,along=distances[i],
                x=points[i].X,y=points[i].Y,z=points[i].Z,connectorFromPrevious=connectorEnds.Contains(i)}).ToArray()};
    }
}
