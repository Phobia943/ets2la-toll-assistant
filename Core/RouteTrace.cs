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
    // Interpolation stays inside the verified trace. Item seams are geometric
    // continuity, not evidence of a bend, so curvature windows exclude them.
    public bool TryCurvature(double center,double span,out double curvature)
    {
        curvature=0;
        if(!double.IsFinite(center)||!double.IsFinite(span)||span<=0||center-span<0||center+span>Length)return false;
        foreach(int i in connectorEnds)if(distances[i]>=center-span&&distances[i-1]<=center+span)return false;
        Vector3 At(double along) {
            int end=Array.BinarySearch(distances,along);
            if(end<0)end=~end;
            end=Math.Clamp(end,1,points.Length-1);
            while(end<points.Length-1&&distances[end]-distances[end-1]<.000001)end++;
            double length=distances[end]-distances[end-1];
            return Vector3.Lerp(points[end-1],points[end],length>0?(float)((along-distances[end-1])/length):0);
        }
        var a=At(center-span);var b=At(center);var c=At(center+span);
        double abX=(double)b.X-a.X,abZ=(double)b.Z-a.Z,bcX=(double)c.X-b.X,bcZ=(double)c.Z-b.Z;
        double ab=Math.Sqrt(abX*abX+abZ*abZ),bc=Math.Sqrt(bcX*bcX+bcZ*bcZ);
        double ac=Math.Sqrt((abX+bcX)*(abX+bcX)+(abZ+bcZ)*(abZ+bcZ));
        if(Math.Min(Math.Min(ab,bc),ac)<.25)return false;
        curvature=2*(abX*bcZ-abZ*bcX)/(ab*bc*ac);
        return double.IsFinite(curvature);
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
