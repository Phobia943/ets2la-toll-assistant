using System.Diagnostics;
using System.Numerics;
using MautAssistent;
internal static class MeasuredTruckCorridorTests
{
    static readonly Vector3 Origin=new(33544,0,12345);
    static VehicleBody Truck()=>new(Origin,Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI),new(-1.54f,.2f,-4),new(1.54f,4,4));
    static RouteTrace Road()=>new([Origin+new Vector3(0,0,-50),Origin,Origin+new Vector3(0,0,300)],[],["before","selected"]);
    static SceneBody Body(float x,float z)=>new(1,2,0,Origin+new Vector3(x,2,z),Quaternion.Identity,new(1,2,2),1);
    internal static void Bounds(Action<bool,string> assert)
    {
        var truck=Truck();var road=Road();var corridor=MeasuredTruckCorridor.Create(truck,road,Origin,100/3.6);
        assert(corridor!=null&&corridor.SampleCount>600&&corridor.HorizonMetres>170,"A selected 100km/h root-route horizon is prepared once at concrete unpadded measured truck poses");
        assert(corridor!.Distance(Body(3,50),0)==null&&corridor.Distance(Body(-3,50),0)==null,
            "A real 46cm lateral gap at x=3m remains clear, with no planning or source-phase AEB inflation");
        var hit=corridor.Distance(Body(0,50),0);
        assert(hit is >=43.7 and <=44.3,"The central blocker is positively contacted by an actual sampled truck box, accounting for the real front reach");
        assert(SceneAcuteHazard.MeasuredDistance(Body(3,50),truck,road,Origin,100/3.6,0)==null&&
            SceneAcuteHazard.MeasuredDistance(Body(0,50),truck,road,Origin,100/3.6,0) is >=43.7 and <=44.3,
            "The legacy measured convenience API delegates to the same prepared raw-box semantics");
        foreach(var malformed in new[]{Body(0,50) with{Flags=0},Body(0,50) with{Flags=6},Body(0,50) with{Speed=float.NaN},
            Body(0,50) with{Speed=.2f},Body(0,50) with{Rotation=new(0,0,0,0)},Body(0,50) with{HalfExtents=new(-1,2,2)},
            Body(0,50) with{Center=new(float.NaN,0,0)}})
            assert(corridor.Distance(malformed,0)==null,"Missing or malformed actual actor physics cannot confer emergency authority");
        assert(corridor.Distance(Body(0,50),double.NaN)==null&&corridor.Distance(Body(0,50),.201)==null&&corridor.Distance(Body(0,50),-.001)==null,
            "Invalid, stale or negative scene age yields no positive acute claim");
        assert(MeasuredTruckCorridor.Create(truck with{Rotation=new(0,0,0,0)},road,Origin,10)==null&&
            MeasuredTruckCorridor.Create(truck,road,Origin,double.NaN)==null,"Invalid measured own pose or speed cannot prepare an acute corridor");
        var unbound=new RouteTrace([Origin,Origin+new Vector3(0,0,100)]);
        assert(MeasuredTruckCorridor.Create(truck,unbound,Origin,10)==null,"An unidentified route does not become measured collision authority");
        var curved=new RouteTrace([Origin+new Vector3(0,0,-50),Origin,Origin+new Vector3(0,0,80),Origin+new Vector3(80,0,80),Origin+new Vector3(80,0,300)],[],["a","b","c","d"]);
        var turn=MeasuredTruckCorridor.Create(truck,curved,Origin,100/3.6);
        assert(turn!=null&&turn.Distance(Body(40,40),0)==null,"Overlap of the whole curved corridor AABB alone is never a physical intrusion");
        assert(turn!.Distance(Body(40,80),0)!=null,"Per-pose AABBs and the raw oriented-box SAT still detect a blocker on the actual selected turn");
        var rotated=Body(3.5f,50) with{Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI/4)};
        assert(corridor.Distance(rotated,0)!=null,"A rotated actor is tested by its actual oriented geometry rather than an axis-aligned centre heuristic");
        var shifted=truck with{Position=truck.Position+new Vector3(.1f,0,0)};
        var anchored=MeasuredTruckCorridor.Create(shifted,road,Origin,10);
        assert(anchored!=null&&anchored.Distance(Body(0,10),0)!=null,"The real measured initial truck origin is retained rather than snapped to the road centre");
    }
    internal static object Benchmark()
    {
        var truck=Truck();var road=Road();var create=Stopwatch.StartNew();
        var corridor=MeasuredTruckCorridor.Create(truck,road,Origin,100/3.6)!;create.Stop();
        var actors=Enumerable.Range(0,512).Select(i=>Body(i%2==0?3:-3,10+(i%160))).ToArray();
        foreach(var actor in actors)corridor.Distance(actor,0); // JIT/first-use outside the warm batch
        var clock=Stopwatch.StartNew();long before=GC.GetAllocatedBytesForCurrentThread();int hits=0;
        for(int repetition=0;repetition<6;repetition++)for(int i=0;i<actors.Length;i++)if(corridor.Distance(actors[i],0)!=null)hits++;
        clock.Stop();long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
        return new{scope="Pure prepared measured raw-truck corridor; synthetic FR-coordinate straight road at100km/h,512actors x6queries; no SDK/control/MMF/source-lease assertion",
            samples=corridor.SampleCount,createMilliseconds=create.Elapsed.TotalMilliseconds,batchMilliseconds=clock.Elapsed.TotalMilliseconds,
            queryCount=3072,hits,allocatedBytes=allocated,below150Milliseconds=clock.Elapsed.TotalMilliseconds<150};
    }
}
