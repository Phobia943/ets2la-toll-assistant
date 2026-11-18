using System.Numerics;
using MautAssistent;

internal static class RigRoadPredictionReviewTests
{
    internal sealed record Fixture(RouteTrace Route,RigRoadObservation Observation,IReadOnlyList<TraceSlicePoint> Ahead,IReadOnlyList<TraceSlicePoint> Tail);
    internal static Fixture Create(int side=1,bool straight=false)
    {
        var points=new List<Vector3>();
        for(int i=0;i<=220;i++)points.Add(new(0,0,35-i*.25f));
        if(!straight) {
            const double radius=12;int n=(int)Math.Ceiling(radius*Math.PI/2/.25);
            for(int i=1;i<=n;i++){double yaw=Math.PI/2*i/n;points.Add(new((float)(side*radius*(Math.Cos(yaw)-1)),0,(float)(-20-radius*Math.Sin(yaw))));}
            var end=points[^1];for(int i=1;i<=200;i++)points.Add(end+new Vector3(-side*i*.25f,0,0));
        } else for(int i=1;i<=280;i++)points.Add(new(0,0,-20-i*.25f));
        var route=new RouteTrace(points,[],Enumerable.Repeat("same-road",points.Count-1));
        var truck=new VehicleBody(Vector3.Zero,Quaternion.Identity,new(-1.25f,.2f,-3),new(1.25f,4,4));
        var trailer=new VehicleBody(new(0,0,7),Quaternion.Identity,new(-1.3f,.2f,-5.5f),new(1.3f,4,6.5f));
        const long now=10000;
        var observation=new RigRoadObservation(new(1,7,2,now,truck,true,trailer),"truck-trailer-config",now,
            new(0,1,2),new(0,1,-5),new(new(0,.5f,3),8,3,null){ContactStable=true},truck.Position,trailer.Position,0,0,3);
        double current=route.ProjectForTracking(Vector3.Zero).Along;
        if(!route.TryStandardSlice(current,route.Length,.5,out var ahead,out _)||!route.TryStandardSlice(current-25,current,.5,out var tail,out _))
            throw new Exception("Fixture route cannot be sampled");
        return new(route,observation,ahead,tail);
    }
    internal static RigStaticGeometry Pole(Vector3 position)=>new(Box(position,new(.08f,1.6f,.08f)));
    static IEnumerable<StaticTriangle> Box(Vector3 center,Vector3 half)
    {
        var p=Enumerable.Range(0,8).Select(i=>center+new Vector3((i&1)==0?-half.X:half.X,(i&2)==0?-half.Y:half.Y,(i&4)==0?-half.Z:half.Z)).ToArray();
        int[][] faces=[[0,1,3,2],[4,6,7,5],[0,4,5,1],[2,3,7,6],[0,2,6,4],[1,5,7,3]];
        foreach(var f in faces){yield return new(p[f[0]],p[f[1]],p[f[2]]);yield return new(p[f[0]],p[f[2]],p[f[3]]);}
    }
    internal static void PositiveSweep(Action<bool,string> check)
    {
        foreach(int side in new[]{-1,1}) {
            var f=Create(side);var prediction=RigRoadPrediction.Build(f.Observation,f.Ahead,f.Tail,10000,out var issue);
            check(prediction!=null&&issue==null,"Coherent low-speed body observations admit a supported smooth road preview");
            var polePosition=new Vector3(-side*1.78746f,2,-20.106462f);var geometry=Pole(polePosition);
            var nominal=geometry.CheckNominal(StaticRoadCorridor.Create(f.Route,Vector3.Zero,80)!);
            check(nominal.Clear&&nominal.Issue==null,"The demonstrated inside pole is outside the old nominal tractor corridor");
            var truckOnly=prediction!.Prediction with{Cells=prediction.Prediction.Cells.Select(c=>c with{Trailer=c.Truck,TrailerMotionReserve=c.TruckMotionReserve,TrailerSourceTranslation=c.TruckSourceTranslation}).ToArray()};
            var truckResult=geometry.Check(truckOnly,Matrix4x4.Identity,_=>0);
            check(truckResult.Clear&&truckResult.Issue==null,"The pole also clears the measured tractor sweep, not just its route centreline");
            var hit=prediction.Check(geometry,30000);
            check(!hit.Clear&&hit.Issue==null&&hit.ConflictAlong is >60 and <63,"A real trailer-only overlap is reported as its earliest truck progress");
            check(hit.ConflictAlong>f.Route.ProjectForTracking(polePosition).Along+5,
                "The hit cannot be discarded merely because the pole projection has already passed behind the tractor");
            check(hit.ConflictAlong-f.Ahead[0].Along>20,"The synthetic initial approach leaves ordinary braking room instead of first discovering contact at the pole");
        }
        var straight=Create(straight:true);var road=RigRoadPrediction.Build(straight.Observation,straight.Ahead,straight.Tail,10000,out _)!;
        foreach(float side in new[]{-1f,1f}) {
            var wall=new RigStaticGeometry(Box(new(side*2.2f,2,-10),new(.5f,2,40)));
            var result=road.Check(wall,30000);
            check(result.Clear&&result.Issue==null,"A straight narrow 3.4m lane remains clear without an all-articulation circle or global widening: "+result.Issue);
        }
    }
    internal static void StrictInputsAndGround(Action<bool,string> check)
    {
        var f=Create();
        RigRoadPrediction? Build(RigRoadObservation? o=null,IReadOnlyList<TraceSlicePoint>? ahead=null,IReadOnlyList<TraceSlicePoint>? tail=null,long now=10000)
            =>RigRoadPrediction.Build(o??f.Observation,ahead??f.Ahead,tail??f.Tail,now,out _);
        check(Build(now:10151)==null,"Expired measured bodies cannot acquire a new result lease");
        check(Build(o:f.Observation with{Speed=20d/3.6+.001})==null,"Actual speed is not silently clamped into the supported low-speed model");
        check(Build(o:f.Observation with{Speed=-.01})==null,"Reverse is outside the model");
        check(Build(o:f.Observation with{TelemetryUtcMs=9969})==null,"The native/SCS capture skew remains bounded to 30ms");
        check(Build(o:f.Observation with{Axle=f.Observation.Axle with{ContactStable=false}})==null,"Missing wheel contact supplies no model permission");
        check(Build(o:f.Observation with{Axle=f.Observation.Axle with{Issue="steerable"}})==null,"Unsupported axle layouts remain unknown");
        check(Build(o:f.Observation with{TelemetryTrailer=new(5,0,7)})==null,"A mismatched trailer origin is rejected independently of the truck");
        check(Build(tail:[])==null,"Missing road behind the truck cannot be replaced with a guessed flat plane");
        var mismatched=f.Tail.ToArray();mismatched[^1]=mismatched[^1] with{Position=new(1,0,0)};
        check(Build(tail:mismatched)==null,"Tail must join the original route at the exact current sample");
        var steep=f.Ahead.ToArray();steep[5]=steep[5] with{Position=steep[5].Position+Vector3.UnitY};
        check(Build(ahead:steep)==null,"Unsupported grade never acquires a planar prediction");
        var shortAhead=f.Ahead.Take(10).ToArray();
        check(Build(ahead:shortAhead)==null,"Insufficient brake-and-rear route coverage is unknown");
        var valid=Build()!;
        var zeroFace=new RigStaticGeometry([new(new(0,1,-5),new(0,1,-5),new(0,1,-5))]);
        var degenerate=valid.Check(zeroFace,30000);
        check(degenerate.Clear&&degenerate.Issue==null,"Degenerate faces cannot fabricate a body obstacle");
        var limited=valid.Check(Pole(new(0,2,-10)),1);
        check(limited.Issue!=null,"Exhausted geometry budget is unknown, even if a ConflictAlong diagnostic is present");
    }
}
