using System.Numerics;
using System.Runtime.CompilerServices;
using ETS2LA.Game.Data;
using TruckLib.Models.Ppd;
using TruckLib.ScsMap;
using MautAssistent;
using PathLib;

public static class ParkedSceneAdmissionIntegration
{
    static PrefabPath Path(float from=50,float to=250)
    {
        var curve=new NavCurve {StartPosition=new(0,0,from),EndPosition=new(0,0,to),Length=to-from,
            StartRotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI),
            EndRotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI),AllowedVehicles=AllowedVehicles.AllVehicles,SemaphoreId=1};
        var prefab=(ParsedPrefab)RuntimeHelpers.GetUninitializedObject(typeof(ParsedPrefab));
        prefab.Descriptor=new(){NavCurves=[curve]};prefab.Prefab=new(){Uid=55};
        return new(prefab,new Node{Uid=551},new Node{Uid=552},[curve],Direction.Forward,Matrix4x4.Identity,Vector3.Zero);
    }
    static SceneSnapshot Scene(params SceneBody[] bodies)=>new(321,44,2,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),1000000,900000,1,1000000,
        Vector3.Zero,59,(uint)bodies.Length,1,double.PositiveInfinity,bodies,[new(55,3)]);
    public static void CurrentEvidence(Action<bool,string> assert)
    {
        var path=Path();var road=new RouteTrace([new Vector3(0,0,-100),new Vector3(0,0,1000)]);
        ParkedSceneAdmission.Result Check(SceneSnapshot? scene,PrefabPath? route=null,float radius=5)=>
            ParkedSceneAdmission.Check(route??path,scene,Vector3.Zero,road,radius,10,1000000,1000000,4.4f,3.4f,3);
        // Warm native interpolation/JIT before testing the short source lease.
        Check(null);
        assert(Check(Scene()).Clear,"Fresh complete parked-only evidence may admit its actually scoped route");
        var blocking=new SceneBody(1,2,0,new(0,2,150),Quaternion.Identity,new(1,2,4),1);
        var occupied=Check(Scene(blocking));
        assert(!occupied.Clear&&occupied.StopDistance>=0,"A newly materialized real parked body supersedes an earlier empty-scene result");
        assert(Check(Scene()).Clear,"Later actually renewed empty evidence may recover without a permanent fault latch");
        var distant=blocking with{Center=new(40,2,150)};
        assert(Check(Scene(distant)).Clear,"A measured distant parked body does not globally reject an entire toll model");
        assert(!Check(Scene() with{UtcMs=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()-201}).Clear,"Expired producer evidence is not renewed by entering the admission function");
        assert(!Check(Scene() with{Scopes=new[]{new SceneScope(56,3)}}).Clear,"Another valid prefab UID does not authorize this actual lane");
        assert(!Check(Scene() with{Scopes=new[]{new SceneScope(55,1)}}).Clear,"A loaded but unenumerated relevant segment cannot be treated as empty");
        assert(!Check(null).Clear,"A missing current source means planned scene reservation, not clear space");
        assert(!Check(Scene(),radius:float.NaN).Clear,"Missing actual coupling/cargo enclosure cannot fall back to a narrower profile");
        assert(!Check(Scene(blocking with{Flags=0})).Clear,"Unmeasured parked motion is not silently assumed stationary");
        assert(Check(null,Path(800,950)).Clear,"A far planned prefab may be statically selected before current dynamic coverage is required");
        assert(!Check(null,path).Clear,"That far-planning exception cannot persist when the actual entry is inside the stopping horizon");
        var cornerIntrusion=blocking with{Center=new(9,2,150),HalfExtents=new(1,2,15),Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI/2)};
        assert(!Check(Scene(cornerIntrusion)).Clear,"A trailer whose centre misses the route still blocks when its rotated body intrudes");
    }
    public static void PositiveHazardsSurviveUnknown(Action<bool,string> assert)
    {
        var path=Path(0,250);var road=new RouteTrace([new Vector3(0,0,-100),new Vector3(0,0,1000)]);
        var hazard=new SceneBody(1,2,0,new(0,1,10),Quaternion.Identity,new(1,1,2),1);
        var unknown=hazard with{Id=2,GroupId=2,Flags=0,Center=new(200,1,200)};
        ParkedSceneAdmission.Result Check(SceneSnapshot scene)=>ParkedSceneAdmission.Check(path,scene,Vector3.Zero,road,5,50/3.6,1000000,1000000,4.4f,3.4f,3);
        Check(Scene(hazard)); // Warm geometry/hazard JIT before the deliberately short source lease.
        foreach(var frame in new Func<SceneSnapshot>[]{()=>Scene(hazard),()=>Scene(unknown,hazard),()=>Scene(hazard,unknown)}) {
            var check=Check(frame());
            assert(!check.Clear&&SceneStopPolicy.NeedsEmergency(50/3.6,check.AcuteDistance),"Unknown unrelated motion cannot hide a distinct current measured imminent collision; body order is irrelevant: "+check.Reason+" / acute="+check.AcuteDistance);
        }
        var missingScope=Scene(hazard) with{Scopes=new[]{new SceneScope(99,3)}};
        assert(SceneStopPolicy.NeedsEmergency(50/3.6,Check(missingScope).AcuteDistance),"Missing absence coverage for a prefab cannot erase positive near-body evidence from the otherwise fresh source");
        var partial=Scene(hazard) with{Flags=58,Diagnostic=3};var observed=Check(partial);
        assert(!observed.Clear&&SceneStopPolicy.NeedsEmergency(50/3.6,observed.AcuteDistance),"An incomplete valid current source may protect against its known present body while denying clear passage");
        assert(!Check(Scene() with{Flags=58,Diagnostic=3}).Clear,"An incomplete empty scene can never become free passage");
        assert(!SceneStopPolicy.NeedsEmergency(50/3.6,Check(partial with{UtcMs=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()-201}).AcuteDistance),"Expired partial source cannot assert a present acute collision");
        assert(!SceneStopPolicy.NeedsEmergency(50/3.6,Check(partial with{OwnPosition=new(10,0,0)}).AcuteDistance),"A partial snapshot from an unmatched own pose cannot assert a local acute collision");
        assert(!SceneStopPolicy.NeedsEmergency(50/3.6,Check(Scene(unknown)).AcuteDistance),"Unknown evidence alone still cannot become an acute-object emergency");
    }
    public static void CanonicalRouteIdentity(Action<bool,string> assert)
    {
        var first=Path(0,20);var a=first.Curves[0];
        var b=new NavCurve{StartPosition=a.StartPosition,EndPosition=a.EndPosition,StartRotation=a.StartRotation,EndRotation=a.EndRotation,
            Length=a.Length,AllowedVehicles=a.AllowedVehicles,SemaphoreId=a.SemaphoreId};
        first.Prefab.Descriptor!.NavCurves.Add(b);
        var second=new PrefabPath(first.Prefab,first.StartNode,first.EndNode,[b],Direction.Forward,Matrix4x4.Identity,Vector3.Zero);
        PlannedItem Item(PrefabPath p)=>new(){item=p.Prefab,startNode=p.StartNode,endNode=p.EndNode,prefabPath=p,direction=Direction.Forward,laneIndex=0,targetLaneIndex=0,allowedToLaneChange=false};
        string original=RouteMatcher.ItemIdentity(Item(first));
        string refined=RouteMatcher.ItemIdentity(Item(PrefabRefinement.Refine(first)));
        string other=RouteMatcher.ItemIdentity(Item(PrefabRefinement.Refine(second)));
        assert(original==refined,"Refinement retains the exact original directed prefab identity rather than replacing IDs with -1 pieces");
        assert(refined!=other,"Distinct real descriptor curves retain distinct identities even with identical geometry and refinement piece counts");
        var direction=new PrefabPath(first.Prefab,first.StartNode,first.EndNode,[a],Direction.Backward,Matrix4x4.Identity,Vector3.Zero);
        assert(original!=RouteMatcher.ItemIdentity(Item(direction)),"Native curve-direction metadata belongs to the exact route identity");

        var loop=new RouteTrace([new(-.1f,0,-100),new(-.1f,0,50),new(-.1f,0,250),new(100,0,250),
            new(100,0,0),Vector3.Zero,new(0,0,20)],null,["approach","wanted-plaza","later","later","later","later"]);
        var ambiguous=ParkedSceneAdmission.Check(Path(),null,Vector3.Zero,loop,5,10,1000000,1000000,
            4.4f,3.4f,3,"wanted-plaza");
        assert(!ambiguous.Clear,
            "A later globally closer loop occurrence cannot erase the upcoming prefab's required scene proof as already passed");
        var prefix=new RouteTrace([new(0,0,-100),new(0,0,300)],null,["accepted-prefix"]){EndsAtGap=true};
        var remote=ParkedSceneAdmission.Check(Path(800,1000),null,Vector3.Zero,prefix,5,10,1000000,1000000,
            4.4f,3.4f,3,"future-plaza-after-gap");
        assert(remote.Clear||remote.StopDistance>200,
            "A physically remote future prefab outside the accepted prefix cannot become an immediate zero-distance scene stop");
    }
}
