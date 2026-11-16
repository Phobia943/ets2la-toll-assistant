using System.Numerics;
using MautAssistent;

// Independent runtime model fixtures. No SDK, mapped memory, game output,
// singletons or product plugin constructors are used.
public static class RuntimeMotionAdversarialTests
{
    static SceneBody Body(uint flags=0,float speed=0)=>new(10,flags,speed,new(5,1,-20),Quaternion.Identity,new(1,1,2),10);
    static SceneSnapshot Frame(uint seq=2,long now=10000,SceneBody? body=null)=>new(1,2,seq,now,
        (ulong)(now*1000),(ulong)(now*1000),1,(ulong)(now*1000),Vector3.Zero,59,1,1,double.PositiveInfinity,
        new[]{body??Body()},new[]{new SceneScope(7,3)});

    public static void MotionSources(Action<bool,string> check)
    {
        var motion=new SceneMotionPrediction();var first=Frame();
        check(motion.Capture(first,10000),"A fresh source is admitted without claiming known motion");
        var unknown=motion.Predict(first.Bodies[0],.5,10000);
        check(unknown.Kind==SceneMotionKind.Unknown&&unknown.Reserve>.15,"Missing actor physics is explicit uncertain geometry, never measured standstill");
        for(int i=0;i<4;i++)check(motion.Capture(first,10000+i*40),"Repeated reads preserve the same source deadline");
        check(motion.Predict(first.Bodies[0],.5,10160).Kind==SceneMotionKind.Unknown,"Repeated copies cannot manufacture three-frame stationary history");
        check(!motion.Predict(first.Bodies[0],.5,10201).SourceFresh,"Repeated copies cannot renew producer freshness");
        motion.Reset();
        for(int i=0;i<3;i++)check(motion.Capture(Frame((uint)(2+2*i),10000+100*i),10000+100*i),"Distinct advancing source frames establish history");
        var stable=Frame(6,10200);
        check(motion.Predict(stable.Bodies[0],.5,10200).Kind==SceneMotionKind.ObservedPoseEstimate,"Advancing same-actor stand history is labelled an estimate rather than physics");
        var gone=Frame(8,10300) with{BodyTotal=0,Bodies=Array.Empty<SceneBody>()};motion.Capture(gone,10300);
        var back=Frame(10,10400);motion.Capture(back,10400);
        check(motion.Predict(back.Bodies[0],.5,10400).Kind==SceneMotionKind.Unknown,"A body which disappeared cannot borrow its old stationary history");
        var jumped=Frame(12,10500,Body() with{Center=new(100,1,-20)});motion.Capture(jumped,10500);
        var jump=motion.Predict(jumped.Bodies[0],.5,10500);
        check(jump.Kind==SceneMotionKind.Unknown&&jump.Moving,"A pose jump revokes stationary history and is marked moving");
        motion.Reset();var physics=Frame(body:Body(2,3));motion.Capture(physics,10000);
        var actual=motion.Predict(physics.Bodies[0],.5,10000);
        check(actual.Kind==SceneMotionKind.ActualPhysics&&actual.Speed>=3&&actual.Moving,"Actual actor speed retains its measured source label and nonzero movement");
        var other=Frame(4,10100) with{Session=3};motion.Capture(other,10100);
        check(!motion.Capture(Frame(6,10150),10150),"A retired session cannot resurrect when actor IDs happen to match");
        motion.Reset();motion.Capture(Frame(100),10000);
        check(!motion.Capture(Frame(2,10100) with{Generation=2},10100),"A generation increment cannot launder sequence rollback in the same producer session");
    }

    // Capture must reject the same source identity with altered completeness,
    // scopes or own pose even if its body list and clocks remained unchanged.
    public static void MotionIdentityMutation(Action<bool,string> check)
    {
        foreach(var mutated in new Func<SceneSnapshot,SceneSnapshot>[] {
            f=>f with{Flags=58,Diagnostic=1},f=>f with{OwnPosition=Vector3.UnitX},
            f=>f with{Scopes=new[]{new SceneScope(8,3)}},f=>f with{BodyTotal=2,OmittedMinSurfaceDistance=1}}) {
            var motion=new SceneMotionPrediction();var first=Frame(body:Body(2));motion.Capture(first,10000);
            check(!motion.Capture(mutated(first),10000),"A mutated same-sequence scene cannot preserve motion authority");
        }
    }

    static VehicleGeometry Shape(uint seq=2,long now=10000,float z=0)=>new(1,2,seq,now,
        new(new(0,0,z),Quaternion.Identity,new(-1.2f,0,-4),new(1.2f,3,3)),true,
        new(new(0,0,7+z),Quaternion.Identity,new(-1.2f,0,-5),new(1.2f,4,8)));
    static WheelAxleProof Axle(bool contact=true)=>new(new(0,.5f,3),8,2,null){ContactStable=contact};
    static (RigPredictionInput Input,RigPredictionResult Prediction,VehicleGeometry Shape) Model()
    {
        var shape=Shape();var input=new RigPredictionInput(shape.Truck,shape.Trailer!,new(0,0,2),new(0,0,-5),new(0,.5f,3),false,false,
            new[]{new HitchPathPoint(new(0,0,2),0,30),new HitchPathPoint(new(0,0,-28),0,60)},0,.15,.25);
        return(input,ArticulatedRigPrediction.Predict(input),shape);
    }
    static RigModelMonitor Monitor()
    {
        var f=Model();var monitor=new RigModelMonitor();
        if(!monitor.Capture(f.Input,f.Prediction,f.Shape,"directed road 9","fixed wheel config",Axle(),10000))throw new Exception("Invalid monitor fixture");
        return monitor;
    }
    public static void ModelLifecycle(Action<bool,string> check)
    {
        var fixture=Model();check(fixture.Prediction.Supported,"The independent straight no-slip fixture is supported");
        var actual=Shape(4,10100,-1);var monitor=Monitor();
        check(monitor.Check(actual,"directed road 9","fixed wheel config",Axle(),31,10100).WithinModel,"Real poses are compared at progress in the retained old route coordinates");
        monitor=Monitor();
        check(!monitor.Capture(fixture.Input,fixture.Prediction,actual,"directed road 9","fixed wheel config",Axle(),10100),"Capture cannot silently overwrite an unchecked prior prediction");
        check(monitor.Check(actual,"directed road 9","fixed wheel config",Axle(),1,10100).RequiresCaution,"A newly pruned route coordinate cannot masquerade as old-route progress");
        monitor=Monitor();var escaped=actual with{Trailer=actual.Trailer! with{Position=actual.Trailer.Position+new Vector3(.6f,0,0)}};
        check(monitor.Check(escaped,"directed road 9","fixed wheel config",Axle(),31,10100).Kind==RigModelCheckKind.Deviation,"Trailer departure from the old model is caught by full corresponding body corners");
        check(monitor.Check(actual,"directed road 9","fixed wheel config",Axle(),31,10100).RequiresCaution,"A later return to the route cannot erase an unacknowledged departure");
        check(!monitor.RebaseAtStandstill(fixture.Input,fixture.Prediction,fixture.Shape,"directed road 9","fixed wheel config",Axle(),2,10000),"Moving recovery cannot erase a failed prediction");
        check(!monitor.RebaseAtStandstill(fixture.Input,fixture.Prediction,fixture.Shape,"directed road 9","",Axle(),0,10000)&&monitor.Faulted,
            "A malformed stopped rebase is transactional and leaves the departure latch intact");
        check(monitor.RebaseAtStandstill(fixture.Input,fixture.Prediction,fixture.Shape,"directed road 9","fixed wheel config",Axle(),0,10000),"Explicit stable standstill permits a fresh model lifecycle");
        monitor=Monitor();
        check(monitor.Check(Shape(4,10500,-1),"directed road 9","fixed wheel config",Axle(false),31,10500).Kind==RigModelCheckKind.ContactGrace,"Brief contact loss retains the same configured axle under an explicit grace");
        monitor=Monitor();
        check(monitor.Check(Shape(4,10601,-1),"directed road 9","fixed wheel config",Axle(false),31,10601).RequiresCaution,"Contact grace cannot renew from repeated source reads");
        monitor=Monitor();
        check(monitor.Check(actual with{Session=3},"directed road 9","fixed wheel config",Axle(),31,10100).Kind==RigModelCheckKind.SourceChanged,"A source session change revokes a model despite identical body IDs/configuration");
    }

    public static void StaticSweepBoundary(Action<bool,string> check)
    {
        var origin=new Vector3(-33543.87f,23.19f,15893.19f);
        var q=Quaternion.CreateFromAxisAngle(Vector3.UnitY,.31f);
        var world=Matrix4x4.CreateFromQuaternion(q)*Matrix4x4.CreateTranslation(origin);
        Vector3 Point(Vector3 p)=>Vector3.Transform(p,world);
        VehicleBody Transform(VehicleBody b)=>b with{Position=Point(b.Position),Rotation=q*b.Rotation};
        var shape=Shape();var truck=Transform(shape.Truck with{Minimum=new(-1.54f,0,-4),Maximum=new(1.54f,3.85f,3)});
        var trailer=Transform(shape.Trailer! with{Minimum=new(-1.23f,0,-5),Maximum=new(1.23f,4,8)});
        var input=new RigPredictionInput(truck,trailer,new(0,0,2),new(0,0,-5),new(0,.5f,3),false,false,
            new[]{new HitchPathPoint(Point(new(0,0,2)),.31,30),new HitchPathPoint(Point(new(0,0,-28)),.31,60)},0,.08,.25);
        var prediction=ArticulatedRigPrediction.Predict(input);
        check(prediction.Supported,"Rotated large-world narrow-gate fixture uses the real continuous rig model");
        StaticTriangle[] Panel(float x)=>new[]{
            new StaticTriangle(new(x,.31f,-45),new(x,7,-45),new(x,7,12)),
            new StaticTriangle(new(x,.31f,-45),new(x,7,12),new(x,.31f,12))};
        var walls=new RigStaticGeometry(Panel(-1.7f).Concat(Panel(1.7f)));
        var clear=walls.Check(prediction,world,_=>origin.Y);
        check(clear.Clear,"A straight 3.08m truck with the productive .08m model budget fits a 3.4m static gate at large FR coordinates");
        var bound=walls.Check(prediction,world,_=>null);
        check(!bound.Clear&&bound.Issue!=null,"Absent local ground evidence does not authorize a static sweep");
        var lowRoof=new RigStaticGeometry(new[]{new StaticTriangle(new(-2,3.95f,-12),new(2,3.95f,-12),new(0,3.95f,-9))});
        check(!lowRoof.Check(prediction,world,_=>origin.Y).Clear,"A low beam crossing the actual trailer sweep remains blocked");
        var neighbour=new SceneBody(10,2,0,Point(new(5,1.5f,-10)),q,new(1,1.5f,2),10);
        check(prediction.ClearOf(neighbour,.3),"A measured neighbour-lane car does not occupy the modelled articulated sweep");
        var central=neighbour with{Center=Point(new(0,1.5f,-10))};
        check(!prediction.ClearOf(central,.3),"The identical body on the actual path is a genuine obstruction");
        check(RigStaticGeometry.CheckMovers(prediction,new[]{new ObstacleSphere(new(4,1.5f,-10),1)},world).Clear,
            "A bounded roadside mover outside the actual rig sweep does not block the whole plaza");
        check(!RigStaticGeometry.CheckMovers(prediction,new[]{new ObstacleSphere(new(0,1.5f,-10),1)},world).Clear,
            "A bounded mover on the actual path is detected by the static endpoint");
        // The high roof can be discarded by BVH before any face work. Make a
        // deliberately broad coplanar panel whose AABB intersects the own box,
        // but whose triangular half does not, to force the bounded leaf path.
        var budgetMesh=new RigStaticGeometry(Enumerable.Repeat(new StaticTriangle(new(1.6f,6,-20),new(100,6,-20),new(100,.4f,-20)),20));
        var budget=budgetMesh.Check(prediction,world,_=>origin.Y,1);
        check(!budget.Clear&&budget.Issue!=null,"Exhausted static face budget is explicit unknown rather than clearance");
    }

    public static void StackClipMasks(Action<bool,string> check)
    {
        foreach(bool large in new[]{false,true}) {
            var origin=large?new Vector3(33544,23.19f,-57174):Vector3.Zero;
            var rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,large?.37f:0);
            var transform=Matrix4x4.CreateFromQuaternion(rotation)*Matrix4x4.CreateTranslation(origin);
            var body=new VehicleBody(origin,rotation,new(-1,0,-1),new(1,2,1));
            var cell=new RigSweptCell(0,0,body,body,0,0,origin,origin,0,0);
            var prediction=new RigPredictionResult(true,null,new[]{cell},body,body,0,0);
            for(int mask=0;mask<8;mask++) {
                float Height(int bit)=>(mask&(1<<bit))==0?-.4f:1;
                var face=new StaticTriangle(new(0,Height(0),-.5f),new(.5f,Height(1),.5f),new(-.5f,Height(2),.5f));
                var result=new RigStaticGeometry(new[]{face}).Check(prediction,transform,_=>origin.Y);
                check(result.Issue==null&&result.Clear==(mask==0),
                    $"Bounded stack clipping preserves all triangle/ground masks {mask}, large-world={large}, without an undersized polygon buffer or lost clipped intersection");
            }
        }
    }
}
