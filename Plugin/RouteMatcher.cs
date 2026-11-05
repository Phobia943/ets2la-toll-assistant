using System.Numerics;
using ETS2LA.Game.SDK;
using PathLib;

namespace MautAssistent;

public static class RouteMatcher
{
    public static object Diagnose(PlannedPathData? path, Vector3 truck, ETS2LA.Game.SDK.Semaphore[] semaphores)
    {
        if (path != null) path = RouteSnapshot.Capture(path);
        static double? Finite(double value) => double.IsFinite(value) ? value : null;
        bool ready = path != null && !path.HasError && path.TotalLength > 0;
        float tf = ready ? path!.GetFactorForPoint(truck) : float.NaN;
        var tp = ready ? path!.Interpolate(tf, 0, false) : null;
        return new {
            truck = new { x=truck.X, y=truck.Y, z=truck.Z },
            pathLength = ready ? Finite(path!.TotalLength) : null,
            truckFactor = Finite(tf), truckPathOffset = tp.HasValue ? Finite(Vector3.Distance(truck,tp.Value.Position)) : null,
            mappedSemaphores = ready ? path!.GetNextSemaphores(truck).Select(s => {
                var p=s.GetWorldOrientedPoint().Position;
                var start=s.GetCurveStartWorldCoordinates().Position;
                var end=s.GetCurveEndWorldCoordinates().Position;
                return new { id=s.Semaphore.SemaphoreId, x=p.X,y=p.Y,z=p.Z,
                    startX=start.X,startZ=start.Z,endX=end.X,endZ=end.Z };
            }).ToArray() : null,
            gates = semaphores.Where(g=>g.type==SemaphoreType.GATE).Select(g => {
                var p=g.GetWorldCoordinates();
                float gf=ready ? path!.GetFactorForPoint(p) : float.NaN;
                var gp=ready ? path!.Interpolate(gf,0,false) : null;
                return new { key=Key(g), state=g.state, x=p.X,y=p.Y,z=p.Z,
                    straightDistance=Finite(Vector3.Distance(p,truck)), factor=Finite(gf),
                    routeDistance=ready ? Finite((gf-tf)*path!.TotalLength) : null,
                    pathOffset=gp.HasValue ? Finite(Vector3.Distance(p,gp.Value.Position)) : null,
                    horizontalOffset=gp.HasValue ? Finite(Vector2.Distance(new(p.X,p.Z),new(gp.Value.Position.X,gp.Value.Position.Z))) : null
                };
            }).ToArray()
        };
    }
    public static string Key(ETS2LA.Game.SDK.Semaphore gate)
    {
        var p = gate.GetWorldCoordinates();
        return FormattableString.Invariant($"{gate.id}:{p.X:F1}:{p.Z:F1}");
    }

    public static Gate? Select(PlannedPathData path, Vector3 truck, ETS2LA.Game.SDK.Semaphore[] semaphores, string? tracked)
        =>Select(path,truck,semaphores,tracked,out _);

    public static Gate? Select(PlannedPathData path, Vector3 truck, ETS2LA.Game.SDK.Semaphore[] semaphores, string? tracked,out RouteTrace? sampled,double speed=0)
    {
        sampled=null;
        if (path.HasError || !float.IsFinite(path.TotalLength) || path.TotalLength < 1) return null;
        var trace=SampleForTracking(path,truck,speed);
        sampled=trace;
        var truckProjection=trace.ProjectForTracking(truck);
        if(truckProjection.Offset>3) return null;
        var candidates = new List<GateProjection>();
        var mapped = path.PlannedPath.Where(i=>i.prefabPath!=null).SelectMany(i=>TollSignals.ForPath(i.prefabPath!)).ToArray();
        var canonical=mapped.Select(s=>TollSignals.ReadLive(semaphores,(int)s.Semaphore.SemaphoreId,s.GetWorldOrientedPoint().Position))
            .Where(s=>s!=null).Cast<ETS2LA.Game.SDK.Semaphore>().DistinctBy(Key).ToArray();
        foreach (var gate in canonical.Concat(semaphores.Where(g=>!mapped.Any(s=>s.Semaphore.SemaphoreId==(uint)g.id &&
            Vector3.Distance(s.GetWorldOrientedPoint().Position,g.GetWorldCoordinates())<=1))))
        {
            if (gate.type != SemaphoreType.GATE) continue;
            var key = Key(gate);
            if (tracked != null && key != tracked) continue;
            var position = gate.GetWorldCoordinates();
            var projected = trace.Project(position);
            // A distant unresolved connection is not bridged. Do not assign a
            // gate beyond it to the last point of the valid prefix.
            if(trace.EndsAtGap && projected.Along>=trace.Length-.01)continue;
            float offset = (float)projected.Offset;
            // Barrier SDK coordinates locate the roadside post, not the lane centre.
            // Permit the wider offset only with a route semaphore ID + world-position match,
            // or for the same previously selected gate while it passes behind the truck.
            bool routeMatched = tracked == key || mapped.Any(s => s.Semaphore.SemaphoreId == (uint)gate.id &&
                Vector3.Distance(s.GetWorldOrientedPoint().Position,position) < 1);
            if (!float.IsFinite(offset) || offset > (routeMatched ? 4 : 2)) continue;
            double distance = projected.Along-truckProjection.Along;
            if (tracked == null && distance < 0) continue;
            var state = gate.state is >= 0 and <= 3 ? (BarrierState)gate.state : BarrierState.Unknown;
            candidates.Add(new(new(key, distance, state), offset, routeMatched));
        }
        return GateSelection.Select(candidates, tracked);
    }

    public static ETS2LA.Game.SDK.Semaphore? FindLiveGate(PlannedPathData? path,ETS2LA.Game.SDK.Semaphore[] gates,string? key,Vector3? remembered=null)
    {
        if(key==null)return null;
        if(path is {HasError:false})
            foreach(var signal in path.PlannedPath.Where(i=>i.prefabPath!=null).SelectMany(i=>TollSignals.ForPath(i.prefabPath!)))
            {
                var anchor=signal.GetWorldOrientedPoint().Position;
                var live=TollSignals.ReadLive(gates,(int)signal.Semaphore.SemaphoreId,anchor);
                if(live!=null && Key(live)==key)return live;
            }
        if(remembered.HasValue && int.TryParse(key.Split(':')[0],out var id))return TollSignals.ReadLive(gates,id,remembered.Value);
        return gates.FirstOrDefault(g=>g.type==SemaphoreType.GATE && Key(g)==key);
    }

    public static RouteTrace Sample(PlannedPathData path) => Sample(path,null,0);
    public static RouteTrace SampleForTracking(PlannedPathData path,Vector3 truck,double speed)
        => Sample(path,truck,TrackingGuard.RequiredLookahead(speed));

    static RouteTrace Sample(PlannedPathData path,Vector3? truck,double requiredAhead)
    {
        path = RouteSnapshot.Capture(path);
        var points=new List<Vector3>();
        var connectorEnds=new List<int>();
        RouteTrace Gap(string reason)
        {
            if(truck.HasValue&&points.Count>=2) {
                var prefix=new RouteTrace(points,connectorEnds) {EndsAtGap=true,EndReason=reason};
                var projected=prefix.ProjectForTracking(truck.Value);
                double ahead=prefix.Length-projected.Along;
                if(projected.Offset<=3 && ahead>requiredAhead)return prefix;
                reason+=FormattableString.Invariant($"; noch {ahead:F1} m, Anhaltereserve {requiredAhead:F1} m");
            }
            throw new InvalidOperationException(reason);
        }
        // Global PathLib.Interpolate excludes endpoints and returns null within a
        // one-metre boundary tolerance. Sample each item directly instead.
        for(int itemIndex=0;itemIndex<path.PlannedPath.Count;itemIndex++) {
            var item=path.PlannedPath[itemIndex];
            int count=Math.Clamp((int)Math.Ceiling(item.GetLength()),2,6000);
            for(int i=0;i<=count;i++) {
                float t=Math.Clamp((float)i/count,.00001f,.99999f);
                if(item.item is ETS2LA.Game.Data.ParsedRoadList or ETS2LA.Game.Data.ParsedRoad && item.direction==ETS2LA.Game.Data.Direction.Backward)t=1-t;
                var point=item.Interpolate(t,0,false);
                if(!point.HasValue)return Gap("Routenpunkt fehlt");
                if(i==0 && points.Count>0 && Vector3.Distance(points[^1],point.Value.Position)>2) {
                    var previous=path.PlannedPath[itemIndex-1];
                    var gap=Vector3.Distance(points[^1],point.Value.Position);
                    return Gap(FormattableString.Invariant(
                        $"Nicht verbundene Route: Übergang {itemIndex-1}->{itemIndex}, {gap:F2} m; {previous.item.GetType().Name}/{previous.direction}/Spur {previous.laneIndex} -> {item.item.GetType().Name}/{item.direction}/Spur {item.laneIndex}"));
                }
                if(i==0&&points.Count>0)connectorEnds.Add(points.Count);
                points.Add(point.Value.Position);
            }
        }
        return new(points,connectorEnds);
    }
}
