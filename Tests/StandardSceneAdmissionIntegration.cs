using System.Numerics;
using System.Runtime.CompilerServices;
using ETS2LA.Game.Data;
using TruckLib.Models.Ppd;
using TruckLib.ScsMap;
using MautAssistent;
using PathLib;

public static class StandardSceneAdmissionIntegration
{
    static PrefabPath Path()
    {
        var curve=new NavCurve{StartPosition=Vector3.Zero,EndPosition=new(0,0,-200),Length=200,
            StartRotation=Quaternion.Identity,EndRotation=Quaternion.Identity,AllowedVehicles=AllowedVehicles.AllVehicles};
        var prefab=(ParsedPrefab)RuntimeHelpers.GetUninitializedObject(typeof(ParsedPrefab));
        prefab.Descriptor=new(){NavCurves=[curve]};prefab.Prefab=new(){Uid=55};
        return new(prefab,new Node{Uid=551},new Node{Uid=552},[curve],Direction.Forward,Matrix4x4.Identity,Vector3.Zero);
    }
    static RouteTrace Road(float grade=0)=>new([new(0,-10*grade,10),Vector3.Zero,new(0,200*grade,-200)],null,["approach","plaza"]);
    static SceneSnapshot Scene(params SceneBody[] bodies)=>new(321,44,2,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),1000000,900000,1,1000000,
        Vector3.Zero,59,(uint)bodies.Length,1,double.PositiveInfinity,bodies,[new(55,3)]);
    static VehicleGeometry Raw()=>new(321,99,2,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        new(Vector3.Zero,Quaternion.Identity,new(-1.5f,0,-3),new(1.5f,4,3)),false,null);
    static SceneBody Body(float x=0,float z=-15,uint flags=2)=>new(1,flags,0,new(x,1,z),Quaternion.Identity,new(1,1,2),1);

    public static void CurrentEvidence(Action<bool,string> assert)
    {
        var path=Path();var road=Road();
        ParkedSceneAdmission.Result Check(SceneSnapshot? scene,VehicleGeometry? raw,bool required=true,RouteTrace? trace=null)=>
            StandardSceneAdmission.Check(path,scene,Vector3.Zero,trace??road,5,1000000,1000000,"plaza",required,raw);
        Check(Scene(Body()),Raw()); // JIT is not producer time.
        var empty=Check(Scene(),Raw());
        assert(empty.Clear&&empty.UsesLiveSource,"Fresh complete nominal scope admits empty loaded actors without cargo/axle proof");
        assert(empty.Reason.Contains("kein Gesamtgespann"),"Nominal guidance explicitly excludes whole-rig/cargo clearance");
        assert(Check(Scene(Body(4)),Raw()).Clear,"Neighbour four metres away does not inherit a whole-rig collision sphere");
        assert(Check(Scene(Body(40,flags:0)),Raw()).Clear,"Unknown motion on a distant body is local, not global stop");
        var blocker=Check(Scene(Body()),Raw());
        assert(!blocker.Clear&&double.IsFinite(blocker.StopDistance)&&blocker.UsesLiveSource,"Present nominal corridor blocker yields finite normal reservation");
        assert(blocker.AcuteDistance is double,"Fresh measured truck corridor can supply independent positive acute evidence");
        assert(!Check(Scene(),null).Clear,"Required scene cannot clear without independent raw measured truck source");
        assert(!Check(Scene() with{ProcessId=654},Raw()).Clear,"Different producer PID never binds its scene to this truck");
        assert(Check(Scene(),Raw()).Clear,"Independent native producer session IDs need not be equal");
        assert(!Check(Scene() with{Scopes=[new(56,3)]},Raw()).Clear,"Another prefab scope cannot authorize absence here");
        assert(!Check(Scene() with{UtcMs=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()-201},Raw()).Clear,"Original stale scene cannot be refreshed by helper entry");
        assert(!Check(Scene(),Raw() with{UtcMs=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()-201}).Clear,"Original stale raw pose cannot be refreshed by helper entry");
        assert(!Check(Scene(),Raw() with{Truck=Raw().Truck with{Position=new(8,0,0)}}).Clear,"Unmatched current raw pose denies nominal dynamic authorization");
        var optional=Check(null,null,false);
        assert(optional.Clear&&!optional.UsesLiveSource,"Explicit static-only check uses no live source and promises no scene absence");
        var missing=Check(null,Raw());
        assert(!missing.Clear&&!missing.UsesLiveSource&&missing.AcuteDistance==null,"Missing required source is normal caution, never invented acute object");
        var incomplete=Check(Scene(Body()) with{Flags=58,Diagnostic=3},Raw());
        assert(!incomplete.Clear&&incomplete.AcuteDistance is double,"Missing absence coverage preserves valid present measured hazard");
        assert(Check(Scene(),Raw(),trace:Road(.04f)).Clear,"A normal four-percent road grade is not a no-slip trailer-model failure");
        assert(!Check(Scene(Body() with{Center=new(0,1.6f,-15)}),Raw(),trace:Road(.04f)).Clear,"The same graded road still detects an actual nominal body obstruction");
        var far=new RouteTrace([new Vector3(0,0,10),Vector3.Zero,new(0,0,-200),new(0,0,-300)],null,["a","approach","plaza"]);
        var farResult=Check(null,null,trace:far);
        assert(farResult.Clear&&!farResult.UsesLiveSource,"A far confirmed item does not create an immediate scene-source stop");
        var reversed=Scene() with{OwnPosition=new(8,0,0)};
        assert(!Check(reversed,Raw()).Clear,"Fresh scene from unrelated own position is not usable absence evidence");
        var motion=new SceneMotionPrediction();long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var before=Scene(Body(4,flags:0)) with{UtcMs=now-50,SimulationTime=950000,PausedSimulationTime=850000,RenderTime=950000};
        assert(StandardSceneAdmission.Check(path,before,Vector3.Zero,road,5,1000000,1000000,"plaza",true,Raw(),motion).Clear,
            "First distant unknown actor receives only local initial uncertainty");
        var after=Scene(Body(3.5f,flags:0)) with{UtcMs=now,Sequence=4};
        var moved=StandardSceneAdmission.Check(path,after,Vector3.Zero,road,5,1000000,1000000,"plaza",true,Raw(),motion);
        assert(!moved.Clear&&motion.Predict(after.Bodies[0],.25,now).Kind==SceneMotionKind.ObservedPoseEstimate,
            "Persistent standard tracker preserves observed motion between source frames instead of resetting to initial parked assumption");
    }
    public static void StandardTraceContract(Action<bool,string> assert)
    {
        var road=Road(.04f);
        assert(road.TryStandardSlice(10,60,.5,out var points,out _)&&points.Count>2,"Bounded standard sampling accepts actual sloped geometry");
        assert(!road.TrySlice(10,60,.5,out _,out _),"Articulated near-flat model keeps its original stricter contract");
        assert(!road.TryStandardSlice(10,road.Length+1,.5,out _,out _),"Standard sampling never extrapolates beyond recorded route");
        assert(!road.TryStandardSlice(0,301,.5,out _,out _),"Standard sampling retains finite computational horizon");
        var corner=new RouteTrace([Vector3.Zero,new(0,0,-5),new(5,0,-5)],null,["a","a"]);
        assert(corner.TryStandardSlice(0,10,.5,out var bend,out _)&&bend.Any(p=>p.Position==new Vector3(0,0,-5)),"Every actual corner vertex remains in nominal standard corridor");
    }
    public static void StationarySignedSpeed(Action<bool,string> assert)
    {
        var path=Path();var road=Road();
        ParkedSceneAdmission.Result Check(double speed,SceneSnapshot? scene,VehicleGeometry? raw,string identity="plaza")=>
            StandardSceneAdmission.Check(path,scene,Vector3.Zero,road,speed,1000000,1000000,identity,true,raw);
        Check(0,Scene(Body()),Raw());
        foreach(double speed in new[]{-6.240893344511278E-05/3.6,-0.0009240548475645483/3.6,
            -.011614151299, .011614151299,-.03,.03,-.099,.099,
            -StandardSceneAdmission.StationarySignedSpeedTolerance,0d,.000001}) {
            assert(Check(speed,Scene(),Raw()).Clear,"Signed standstill telemetry retains valid empty scoped corridor at "+speed+"m/s");
            assert(!Check(speed,Scene(Body()),Raw()).Clear,"Standstill normalization cannot erase a present corridor blocker at "+speed+"m/s");
            assert(!Check(speed,null,Raw()).Clear,"Standstill normalization cannot invent required missing scene evidence at "+speed+"m/s");
        }
        foreach(double speed in new[]{-.101,-StandardSceneAdmission.StationarySignedSpeedTolerance-.000001,-1d}) {
            var reverse=Check(speed,Scene(),Raw());
            assert(!reverse.Clear&&reverse.Reason.Contains("Rückwärtsfahrt")&&!reverse.UsesLiveSource,
                "Meaningful reverse travel is explicitly unsupported, never made into forward clearance by Abs");
        }
        foreach(double speed in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity}) {
            var invalid=Check(speed,Scene(),Raw());
            assert(!invalid.Clear&&invalid.Reason.Contains("Geschwindigkeitsmessung"),"Nonfinite speed has an independent measurement diagnostic");
        }
        var missing=Check(-.0001,Scene(),Raw(),"unrelated-route");
        assert(!missing.Clear&&missing.Reason.Contains("Mautabschnitt fehlt"),"Actual missing directed identity remains a separate blocked condition");
        assert(!Check(-.0001,Scene() with{UtcMs=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()-201},Raw()).Clear,
            "Standstill normalization does not renew original stale producer evidence");
        assert(!Check(-.0001,Scene(),null).Clear,"Standstill normalization never substitutes missing raw truck identity");
    }
}
