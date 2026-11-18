using System.Numerics;

namespace MautAssistent;

// Nominal selected-road corridor for positive static-obstacle detection.
// It is not a swept trailer/cargo model and cannot certify collision freedom.
public sealed class StaticRoadCorridor
{
    internal sealed record Segment(VehicleBody Box, Vector3 Minimum, Vector3 Maximum,
        RigGroundPlane Ground, double Along);
    internal IReadOnlyList<Segment> Segments { get; }
    public double CurrentAlong { get; }
    public Vector3 Minimum { get; }
    public Vector3 Maximum { get; }
    StaticRoadCorridor(Segment[] segments,double along) {
        Segments=segments;CurrentAlong=along;
        Minimum=segments.Select(s=>s.Minimum).Aggregate(Vector3.Min);
        Maximum=segments.Select(s=>s.Maximum).Aggregate(Vector3.Max);
    }

    public static StaticRoadCorridor? Create(RouteTrace route,Vector3 own,double horizon,
        float width=TollOptions.StandardLaneWidth,float height=TollOptions.StandardLaneHeight)
    {
        if(route==null||!SceneSnapshotProtocol.Finite(own)||!double.IsFinite(horizon)||horizon is <1 or >300||
            !float.IsFinite(width)||!float.IsFinite(height)||width is <=0 or >10||height is <=.3f or >10)return null;
        var current=route.ProjectForTracking(own);
        if(!double.IsFinite(current.Along)||current.Offset>3||Math.Abs(current.HeightOffset)>2)return null;
        if(!route.TryStandardSlice(current.Along,Math.Min(route.Length,current.Along+horizon),.5,out var points,out _))return null;
        return CreateFromSlice(points,current.Along,width,height);
    }

    // The worker receives an already selected/sampled route, not permission to
    // interpolate an alternative lane. Both in-process fixtures and the worker
    // use this identical geometry; source identity/age remain the caller's job.
    public static StaticRoadCorridor? CreateFromSlice(IReadOnlyList<TraceSlicePoint> points,double currentAlong,
        float width=TollOptions.StandardLaneWidth,float height=TollOptions.StandardLaneHeight)
    {
        if(points==null||points.Count is <2 or >1000||!double.IsFinite(currentAlong)||currentAlong<0||
            !float.IsFinite(width)||!float.IsFinite(height)||width is <=0 or >10||height is <=.3f or >10||
            Math.Abs(points[0].Along-currentAlong)>.00001||points[^1].Along-currentAlong is <=0 or >300)return null;
        foreach(var p in points)
            if(!SceneSnapshotProtocol.Finite(p.Position)||!SceneSnapshotProtocol.Finite(p.Direction)||
                Math.Max(Math.Abs(p.Position.X),Math.Abs(p.Position.Z))>262144||Math.Abs(p.Position.Y)>10000||
                !double.IsFinite(p.Along)||p.Along<0||p.Direction.LengthSquared() is <.5f or >1.5f||
                string.IsNullOrEmpty(p.SegmentIdentity)||p.SegmentIdentity.Length>512)return null;
        var segments=new List<Segment>();
        for(int i=1;i<points.Count;i++) {
            var a=points[i-1];var b=points[i];var delta=b.Position-a.Position;
            double length=Math.Sqrt((double)delta.X*delta.X+(double)delta.Z*delta.Z);
            double step=b.Along-a.Along;
            // Float positions can lose a few millimetres at the map's edge.
            // Reject non-monotonic or fabricated progress rather than converting
            // it into an apparently closer obstacle.
            if(step<=0||step>1||Math.Abs(step-delta.Length())>.05||
                length<.00001||length>1||Math.Abs(delta.Y)>length*.15)return null;
            var center=(a.Position+b.Position)/2;
            var rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,(float)Math.Atan2(-delta.X,-delta.Z));
            // The box encloses this linear half-metre road slice, including its
            // height change. Ground clipping uses that same sloped road plane.
            var box=new VehicleBody(center,rotation,
                new(-width/2,-Math.Abs(delta.Y)/2,(float)-length/2),
                new(width/2,height+Math.Abs(delta.Y)/2,(float)length/2));
            var corners=box.Corners().ToArray();
            var forward=Vector3.Normalize(delta);var across=Vector3.Normalize(new Vector3(-delta.Z,0,delta.X));
            var normal=Vector3.Normalize(Vector3.Cross(across,forward));
            if(normal.Y<0)normal=-normal;
            segments.Add(new(box,corners.Aggregate(Vector3.Min),corners.Aggregate(Vector3.Max),
                new(center,normal),a.Along));
        }
        return new(segments.ToArray(),currentAlong);
    }
}
