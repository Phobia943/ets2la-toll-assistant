using System.Numerics;
using ETS2LA.Game.SDK;
using Semaphore=ETS2LA.Game.SDK.Semaphore;

namespace MautAssistent;

/// <summary>Immutable sampled corridor and copied SDK data only; no native path queries.</summary>
internal static class StatusGeometry
{
    public static object Build(RouteTrace? trace,Vector3 truck,Semaphore[] gates)
    {
        var projection=trace?.ProjectForTracking(truck);
        return new {
            source=trace==null?"SdkOnly":"VerifiedSampledTrace",nativeProjectionMeasured=false,
            truck=new {x=truck.X,y=truck.Y,z=truck.Z},pathLength=trace?.Length,
            truckFactor=(double?)null,truckPathOffset=projection?.Offset,traceProjection=projection,
            mappedSemaphores=(object?)null,
            gates=gates.Where(g=>g.type==SemaphoreType.GATE).Select(g=> {
                var p=g.GetWorldCoordinates();var target=trace?.Project(p);
                return new {key=RouteMatcher.Key(g),state=g.state,x=p.X,y=p.Y,z=p.Z,
                    straightDistance=Vector3.Distance(truck,p),factor=(double?)null,
                    routeDistance=target!=null&&projection!=null&&target.Offset<=4&&
                        !(trace!.EndsAtGap&&target.Along>=trace.Length-.01)?target.Along-projection.Along:(double?)null,
                    pathOffset=target?.Offset};
            }).ToArray()
        };
    }
}
