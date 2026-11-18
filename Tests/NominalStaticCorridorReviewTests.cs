using System.Numerics;
using MautAssistent;

internal static class NominalStaticCorridorReviewTests
{
    static RouteTrace Route(params Vector3[] points)=>new(points,null,Enumerable.Range(1,points.Length-1).Select(i=>"road:"+i));
    static StaticTriangle Wall(float z,float x=0,float y=0,float half=.5f)=>new(new(x-half,y,z),new(x+half,y,z),new(x,y+4,z));
    static RigStaticGeometry Mesh(params StaticTriangle[] faces)=>new(faces);
    static RigStaticGeometry.CheckResult Check(RigStaticGeometry mesh,RouteTrace route,Vector3 own,double horizon=80)=>
        mesh.CheckNominal(StaticRoadCorridor.Create(route,own,horizon)??throw new Exception("Expected valid nominal fixture"));
    static bool Hit(RigStaticGeometry.CheckResult result)=>!result.Clear&&result.Issue==null&&result.ConflictAlong.HasValue;
    static bool NoHit(RigStaticGeometry.CheckResult result)=>result.Clear&&result.Issue==null&&!result.ConflictAlong.HasValue;

    internal static void Geometry(Action<bool,string> check)
    {
        var road=Route(Vector3.Zero,new(0,0,-100));
        var hit=Check(Mesh(Wall(-30)),road,Vector3.Zero);
        check(Hit(hit)&&hit.ConflictAlong is >=29 and <=30,"A concrete coll face on the selected road has bounded first-hit progress");
        check(NoHit(Check(Mesh(Wall(-30,4)),road,Vector3.Zero)),"A neighbouring lane wall four metres aside does not become our obstacle");
        check(NoHit(Check(Mesh(Wall(5)),road,Vector3.Zero)),"An already passed static wall cannot create a forward hold");
        check(NoHit(Check(Mesh(Wall(-90)),road,Vector3.Zero)),"The static horizon is not extended to distant geometry");
        check(NoHit(Check(Mesh(new StaticTriangle(new(-10,5,-25),new(10,5,-25),new(0,5,-35))),road,Vector3.Zero)),
            "A high overpass remains outside the nominal vertical corridor");
        check(Hit(Check(Mesh(new StaticTriangle(new(-10,4,-25),new(10,4,-25),new(0,4,-35))),road,Vector3.Zero)),
            "A concrete low overhead beam is a positive ordinary obstacle");
        check(NoHit(Check(Mesh(new StaticTriangle(new(-10,.29f,-10),new(10,.29f,-10),new(0,.29f,-90))),road,Vector3.Zero)),
            "Support surfaces below the explicit 0.3m exclusion cannot become a phantom wall");
        check(Hit(Check(Mesh(new StaticTriangle(new(-1,.35f,-20),new(1,.35f,-20),new(0,.35f,-21))),road,Vector3.Zero)),
            "The support-surface exclusion does not erase real geometry above its threshold");
        check(Hit(Check(Mesh(new StaticTriangle(new(-.5f,-10,-30),new(.5f,-10,-30),new(0,2,-30))),road,Vector3.Zero)),
            "Ground clipping preserves the blocking above-road portion of a straddling triangle");
        check(!Hit(Check(Mesh(new StaticTriangle(new(0,2,-30),new(0,2,-30),new(0,2,-30))),road,Vector3.Zero)),
            "A zero-area point face cannot establish a positive physical obstacle");
        check(!Hit(Check(Mesh(new StaticTriangle(new(0,1,-30),new(0,2,-30),new(0,3,-30))),road,Vector3.Zero)),
            "A collinear zero-area face cannot establish a positive physical obstacle");
        var moved=Check(Mesh(Wall(-30)),road,new(0,0,-10));
        var corridor=StaticRoadCorridor.Create(road,new(0,0,-10),80)!;
        check(Hit(moved)&&Math.Abs(corridor.CurrentAlong-10)<.001&&moved.ConflictAlong-corridor.CurrentAlong is >=19 and <=20,
            "Obstacle distance subtracts current route progress exactly once after the truck advances");
    }

    internal static void GradesAndWorldPose(Action<bool,string> check)
    {
        foreach(float grade in new[]{-.12f,.04f,.12f}) {
            var road=Route(Vector3.Zero,new(0,100*grade,-100));
            Vector3 At(float x,float z,float above=0)=>new(x,-z*grade+above,z);
            var floor=Mesh(new StaticTriangle(At(-10,0),At(10,0),At(-10,-100)),new(At(10,0),At(10,-100),At(-10,-100)));
            check(NoHit(Check(floor,road,Vector3.Zero)),"Sloped coll road support is clipped on its own plane, grade="+grade);
            var wall=Mesh(new StaticTriangle(At(-1,-30),At(1,-30),At(0,-30,3)));
            check(Hit(Check(wall,road,Vector3.Zero)),"The same sloped road still detects a positive wall, grade="+grade);
        }
        var origin=new Vector3(60000,120,-50000);
        var transform=Matrix4x4.CreateRotationY(.8f)*Matrix4x4.CreateTranslation(origin);
        var worldRoad=Route(Vector3.Transform(Vector3.Zero,transform),Vector3.Transform(new(0,0,-100),transform));
        var local=Wall(-30);var world=new StaticTriangle(Vector3.Transform(local.A,transform),Vector3.Transform(local.B,transform),Vector3.Transform(local.C,transform));
        var result=Check(Mesh(world),worldRoad,origin);
        check(Hit(result)&&result.ConflictAlong is >=29 and <=30.1,"Large translated and rotated world coordinates preserve the actual selected-road hit");
        check(StaticRoadCorridor.Create(Route(Vector3.Zero,new(0,16,-100)),Vector3.Zero,80)==null,
            "A grade outside the supported model is unknown, not a synthetic blocker");
    }

    internal static void SelectedRoute(Action<bool,string> check)
    {
        var turn=Route(Vector3.Zero,new(0,0,-20),new(20,0,-20));
        var diagonal=Mesh(new StaticTriangle(new(9,0,-9),new(11,0,-9),new(10,4,-11)));
        check(NoHit(Check(diagonal,turn,Vector3.Zero,40)),"A bend does not invent a diagonal driving shortcut through its centre");
        check(Hit(Check(Mesh(new StaticTriangle(new(10,0,-21),new(10,0,-19),new(10,4,-20))),turn,Vector3.Zero,40)),
            "The selected segment after a sharp turn remains in the bounded corridor");
        var laneChange=Route(Vector3.Zero,new(0,0,-10),new(4,0,-30),new(4,0,-100));
        check(Hit(Check(Mesh(Wall(-50,4)),laneChange,Vector3.Zero)),"A chosen lane change checks its destination lane");
        check(NoHit(Check(Mesh(Wall(-50,0)),laneChange,Vector3.Zero)),"After a selected lane change the abandoned parallel line is not treated as our lane");
        var reverseSeam=new RouteTrace([Vector3.Zero,new(0,0,-10),new(0,0,-9),new(0,0,-30)],[2],["a","seam","b"]);
        check(StaticRoadCorridor.Create(reverseSeam,Vector3.Zero,30)==null,"A reversing seam cannot manufacture an authorised static corridor");
        var gap=Route(Vector3.Zero,new(0,0,-20));
        check(NoHit(Check(Mesh(Wall(-25)),gap,Vector3.Zero,80)),"A finite route ending before a wall is never extrapolated past its endpoint");
        check(StaticRoadCorridor.Create(new RouteTrace([Vector3.Zero,new(0,0,-100)]),Vector3.Zero,80)==null,
            "Unidentified geometry cannot authorise the static route test");
    }

    internal static void WorkAndUnknown(Action<bool,string> check)
    {
        var road=Route(Vector3.Zero,new(0,0,-100));var corridor=StaticRoadCorridor.Create(road,Vector3.Zero,80)!;
        var noOverlap=Mesh(Wall(-30,20));var bounded=noOverlap.CheckNominal(corridor,1);
        check(!bounded.Clear&&bounded.ConflictAlong==null&&bounded.Issue!=null&&bounded.FaceChecks<=2,
            "Budget exhaustion is unknown with no fabricated obstacle progress or unbounded traversal");
        foreach(int budget in new[]{0,-1,30001}) {
            var invalid=noOverlap.CheckNominal(corridor,budget);
            check(!invalid.Clear&&invalid.ConflictAlong==null&&invalid.Issue!=null&&invalid.FaceChecks==0,
                "Invalid work limit cannot authorise clearance or generate a positive blocker: "+budget);
        }
        foreach(double horizon in new[]{0d,301,double.NaN,double.PositiveInfinity})
            check(StaticRoadCorridor.Create(road,Vector3.Zero,horizon)==null,"Unsupported horizon is unknown: "+horizon);
        check(StaticRoadCorridor.Create(road,new(4,0,0),80)==null&&StaticRoadCorridor.Create(road,new(0,3,0),80)==null,
            "Unmatched lateral or vertical truck pose does not seed a nominal corridor");
        var behindEnd=StaticRoadCorridor.Create(road,new(0,0,-101),80);
        check(behindEnd==null,"No nominal corridor can be built past the final recorded route point");
        double start=road.ProjectForTracking(new(0,0,-10)).Along;
        road.TryStandardSlice(start,start+80,.5,out var slice,out _);
        var transferred=StaticRoadCorridor.CreateFromSlice(slice,start);
        var original=StaticRoadCorridor.Create(road,new(0,0,-10),80)!;
        check(transferred!=null&&transferred.Minimum==original.Minimum&&transferred.Maximum==original.Maximum&&
            Mesh(Wall(-30)).CheckNominal(transferred)==Mesh(Wall(-30)).CheckNominal(original),
            "Worker slice reconstruction preserves the same nominal corridor and hit progress");
        check(StaticRoadCorridor.CreateFromSlice(slice,0)==null,
            "A transferred slice cannot substitute different current progress");
        foreach(var broken in new[]{
            slice.Select((p,i)=>i==2?p with {Along=p.Along-1}:p).ToArray(),
            slice.Select((p,i)=>i==2?p with {Position=new(float.NaN,0,0)}:p).ToArray(),
            slice.Select((p,i)=>i==2?p with {SegmentIdentity=null}:p).ToArray(),
            slice.Select((p,i)=>i==2?p with {Direction=Vector3.Zero}:p).ToArray(),
            slice.Select((p,i)=>i==2?p with {Along=p.Along+.4}:p).ToArray()})
            check(StaticRoadCorridor.CreateFromSlice(broken,start)==null,
                "Malformed transferred progress, pose, identity or tangent remains unknown");
    }
}
