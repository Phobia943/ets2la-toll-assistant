using System.Numerics;
using MautAssistent;

public static class StandardSliceHandoffIntegration
{
    static RouteTrace Trace(Vector3 a,Vector3 b,Vector3 c,Vector3 d)=>new([a,b,c,d],[2],["prefab","seam:prefab-road","road"]);
    static bool Slice(RouteTrace road,out IReadOnlyList<TraceSlicePoint> points)=>road.TryStandardSlice(0,road.Length,.5,out points,out _);
    public static void NumericalItemBoundaries(Action<bool,string> assert)
    {
        // Four actual reconstructed float vertices around the incident's
        // prefab->RoadList seam291, not an extrapolated or invented road.
        Vector3 a=new(-33633.5625f,41.7656288146973f,8136.6484375f),
            b=new(-33634.546875f,41.765625f,8136.5f),c=b,
            d=new(-33635.54296875f,41.7658157348633f,8136.3408203125f);
        var real=Trace(a,b,c,d);
        assert(Slice(real,out var points),"Actual FR exit's coincident item endpoints preserve a continuous standard corridor");
        assert(points.Any(p=>p.Position==b),"Actual handoff vertex remains in sampled geometry");
        assert(real.TryItemRange("prefab",out double start,out double end)&&start==0&&end>0,
            "Duplicate-boundary handling preserves original prefab identity and its range");
        assert(real.TryItemRange("road",out double roadStart,out _)&&roadStart==end,
            "Following road identity starts at the same actual boundary without invented progress");
        assert(points.Any(p=>p.SegmentIdentity=="prefab")&&points.Any(p=>p.SegmentIdentity=="road"),
            "Geometry sampling retains both directed item identities");
        assert(Slice(Trace(a,b,b+new Vector3(.00390625f,0,0),d),out _),
            "One representable backward world-X step does not counterfeit a reversed real exit");
        assert(Slice(Trace(new(-33500,0,8000),new(-33510,0,8000),new(-33509.99609375f,0,8000),new(-33530,0,8000)),out _),
            "Exactly opposite numerical seam cannot cancel the actual straight-road sampling tangent");
        var straight=Trace(new(-33500,0,8000),new(-33510,0,8000),new(-33509.99609375f,0,8000),new(-33530,0,8000));
        assert(straight.TryStandardSlice(10.00390625,20,.5,out _,out _),
            "Window starting exactly after a numerical seam uses only its validated orientation support");
        assert(straight.TryStandardSlice(10.01390625,20,.5,out _,out _),
            "Window starting one centimetre after a numerical seam retains a valid first road tangent");
        assert(straight.TryStandardSlice(0,10,.5,out _,out _),
            "Window ending exactly before a numerical seam retains its incoming road tangent");
        assert(straight.TryStandardSlice(0,9.99,.5,out _,out _),
            "Window ending one centimetre before a numerical seam uses no extra corridor geometry");
        assert(Slice(Trace(a,b,b+new Vector3(0,0,.00390625f),d),out _),
            "Millimetric transverse world rounding stays within the endpoint precision allowance");
        assert(!Slice(Trace(a,b,b+new Vector3(0,0,.017f),d),out _),
            "Transverse displacement just above the actual endpoint ULP allowance is rejected");
        assert(!Slice(Trace(a,b,b+new Vector3(0,0,.125f),d),out _),
            "A real transverse twelve-centimetre handoff cannot borrow numerical tolerance");
        assert(!Slice(Trace(a,b,b,a),out _),"Coincident endpoints with actually opposing neighbours remain rejected");
        var local=Trace(new(0,0,0),new(0,0,-10),new(.02f,0,-10),new(.02f,0,-30));
        assert(!Slice(local,out _),"A two-centimetre lateral offset near the local origin is not world-float rounding");
        assert(!Slice(Trace(new(0,0,0),new(0,0,-10),new(.00390625f,0,-10),new(.00390625f,0,-30)),out _),
            "The same millimetric displacement near origin cannot borrow large-world ULP allowance");
        assert(Slice(Trace(new(0,0,0),new(0,0,-10),new(0,0,-10),new(0,0,-30)),out _),
            "Exact duplicate item endpoints are geometric zero progress at any world origin");
        var unmarked=new RouteTrace([a,b,b,d],null,["prefab","prefab","road"]);
        assert(!Slice(unmarked,out _),"An unmarked duplicate inside item geometry does not gain boundary authority");
        var missingNeighbour=new RouteTrace([b,b+new Vector3(-.00390625f,0,0),d],[1],["seam","road"]);
        assert(!Slice(missingNeighbour,out _),"A boundary without a real incoming directed neighbour remains unsupported");
        bool gapRejected=false;try{Trace(a,b,b+new Vector3(3,0,0),d);}catch(ArgumentException){gapRejected=true;}
        assert(gapRejected,"Actual connector gaps above two metres remain rejected by trace construction");
        assert(!real.TryStandardSlice(0,real.Length+.1,.5,out _,out _),"Handoff correction never extrapolates beyond the real trace");
        assert(!real.TrySlice(0,real.Length,.5,out _,out _),"Articulated model's separate strict seam contract is unchanged");
        assert(Slice(Trace(new(-33737.703f,42.449028f,8142.2866f),new(-33738.67f,42.44922f,8142.5796f),
            new(-33738.67f,42.449223f,8142.576f),new(-33739.63f,42.44922f,8142.867f)),out _),
            "Saved exit seam396's 3.4mm negative roundoff remains continuous within its actual world ULP budget");
        assert(Slice(Trace(new(-33808.496f,42.44924f,8163.9336f),new(-33809.438f,42.44924f,8164.2207f),
            new(-33809.438f,42.44922f,8164.217f),new(-33810.383f,42.44922f,8164.506f)),out _),
            "Saved exit seam473's 3.9mm negative roundoff remains continuous within its actual world ULP budget");
        assert(Slice(Trace(new(-33752.062f,42.44922f,8146.668f),new(-33753.016f,42.44922f,8146.961f),
            new(-33753.016f,42.449257f,8146.9688f),new(-33753.957f,42.449257f,8147.256f)),out _),
            "Previously accepted saved exit seam412 remains accepted");
    }
}
