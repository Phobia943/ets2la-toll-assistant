using System.Numerics;
using System.Runtime.CompilerServices;
namespace MautAssistent;
internal static class RigRoadSurface
{
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    internal static RigGroundPlane? ForBody(RouteTrace trace,RigSweptCell cell,VehicleBody body)
    {
        double reach=body.Corners().Max(p=>Vector3.Distance(p,body.Position))+2;
        var projection=trace.ProjectNearForTracking(body.Position,(cell.AlongStart+cell.AlongEnd)/2,reach*3+5);
        if(projection.Along<.01||projection.Along>trace.Length-.01||!trace.TryAtAlong(projection.Along,out var at))return null;
        var d=at.Direction;float horizontal=d.X*d.X+d.Z*d.Z;if(horizontal<.9f)return null;
        var normal=Vector3.Normalize(new Vector3(-d.X*d.Y,horizontal,-d.Z*d.Y));
        var point=at.Position;float minimum=0;
        foreach(var corner in body.Corners()) {
            var under=trace.ProjectNearForTracking(corner,projection.Along,reach*2+2);
            if(under.Along<.01||under.Along>trace.Length-.01||!trace.TryAtAlong(under.Along,out var roadPoint))return null;
            minimum=Math.Min(minimum,Vector3.Dot(roadPoint.Position-point,normal));
        }
        return new(point+normal*(minimum-.005f),normal);
    }
}
