using System.Numerics;
using ETS2LA.Game.SDK;
using PathLib;

namespace MautAssistent;

public static class RouteMatcher
{
    public static object Diagnose(PlannedPathData? path, Vector3 truck, ETS2LA.Game.SDK.Semaphore[] semaphores)
    {
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

    public static Gate? Select(PlannedPathData path, Vector3 truck, ETS2LA.Game.SDK.Semaphore[] semaphores, string? tracked,out RouteTrace? sampled)
    {
        sampled=null;
        if (path.HasError || !float.IsFinite(path.TotalLength) || path.TotalLength < 1) return null;
        var trace=Sample(path);
        sampled=trace;
        var truckProjection=trace.Project(truck);
        if(truckProjection.Offset>3) return null;
        var candidates = new List<GateProjection>();
        var mapped = path.PlannedPath.Where(i=>i.prefabPath!=null).SelectMany(i=>i.prefabPath!.GetSemaphores()).ToArray();
        foreach (var gate in semaphores)
        {
            if (gate.type != SemaphoreType.GATE) continue;
            var key = Key(gate);
            if (tracked != null && key != tracked) continue;
            var position = gate.GetWorldCoordinates();
            var projected = trace.Project(position);
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

    public static RouteTrace Sample(PlannedPathData path)
    {
        var points=new List<Vector3>();
        // Global PathLib.Interpolate excludes endpoints and returns null within a
        // one-metre boundary tolerance. Sample each item directly instead.
        foreach(var item in path.PlannedPath) {
            int count=Math.Clamp((int)Math.Ceiling(item.GetLength()),2,6000);
            for(int i=0;i<=count;i++) {
                float t=Math.Clamp((float)i/count,.00001f,.99999f);
                if(item.item is ETS2LA.Game.Data.ParsedRoadList && item.direction==ETS2LA.Game.Data.Direction.Backward)t=1-t;
                var point=item.Interpolate(t,0,false);
                if(!point.HasValue)throw new InvalidOperationException("Routenlücke bei der Maut-Anfahrt");
                if(i==0 && points.Count>0 && Vector3.Distance(points[^1],point.Value.Position)>2)
                    throw new InvalidOperationException("Nicht verbundene Maut-Anfahrt");
                points.Add(point.Value.Position);
            }
        }
        return new(points);
    }
}
