using System.Numerics;
using MautAssistent;
internal static class SceneMotionPredictionTests
{
    static SceneBody Actor(uint flags=0,float speed=0)=>new(1,flags,speed,new(5,2,-10),Quaternion.Identity,new(1,2,2.5f),9);
    static SceneSnapshot Frame(int n,params SceneBody[] bodies)=>new(7,11,(uint)(2+n*2),1000+n*100,1000000+(ulong)n*100000,
        1000000+(ulong)n*100000,1,1000000+(ulong)n*100000,Vector3.Zero,59,(uint)bodies.Length,1,double.PositiveInfinity,bodies,[new(55,3)]);
    internal static void Bounds(Action<bool,string> assert)
    {
        var history=new SceneMotionPrediction();var unknown=Actor();var first=Frame(0,unknown);
        assert(history.Capture(first,1000),"A fresh complete producer snapshot can seed per-actor motion history");
        var initial=history.Predict(unknown,.5,1000);
        assert(initial.Kind==SceneMotionKind.Unknown&&initial.SourceFresh&&double.IsFinite(initial.Reserve),"New parked actor remains explicitly unknown with a named local uncertainty model");
        for(int i=0;i<10;i++)history.Capture(first,1000+i);
        assert(history.Predict(unknown,.5,1010).Kind==SceneMotionKind.Unknown,"Repeated reads of one identical capture never establish stationary observations");
        history.Capture(Frame(1,unknown),1100);history.Capture(Frame(2,unknown),1200);
        assert(history.Predict(unknown,.5,1200).Kind==SceneMotionKind.ObservedPoseEstimate,"Several clock-progressing same-body observations are labelled a pose estimate, never actual physics");
        var physics=Actor(2);history.Reset();history.Capture(Frame(0,physics),1000);
        assert(history.Predict(physics,.5,1000).Kind==SceneMotionKind.ActualPhysics,"Actual physics motion flag is distinguished from a pose-only stationary estimate");
        var moving=physics with{Center=physics.Center+new Vector3(.3f,0,0)};
        history.Capture(Frame(1,moving),1100);var motion=history.Predict(moving,.5,1100);
        assert(motion.Moving&&motion.Speed>2.9&&motion.Reserve>1,"Sudden measured movement overrides a simultaneous zero scalar physics reading");
        history.Reset();history.Capture(Frame(0,unknown),1000);
        var frozen=Frame(1,unknown) with{SimulationTime=first.SimulationTime,PausedSimulationTime=first.PausedSimulationTime,RenderTime=first.RenderTime};
        history.Capture(frozen,1100);
        assert(history.Predict(unknown,.5,1100).Kind==SceneMotionKind.Unknown,"New sequence and UTC without advancing simulation clocks cannot assert stationary motion");
        history.Reset();history.Capture(Frame(0,unknown),1000);history.Capture(Frame(1,unknown),1100);history.Capture(Frame(2,unknown),1200);
        history.Capture(Frame(3),1300);history.Capture(Frame(4,unknown),1400);
        assert(history.Predict(unknown,.5,1400).Kind==SceneMotionKind.Unknown,"Actor disappearance revokes its old stationary history before an ID reappears");
        var changed=unknown with{HalfExtents=new(2,2,2)};history.Capture(Frame(5,changed),1500);
        assert(history.Predict(changed,.5,1500).Kind==SceneMotionKind.Unknown,"Local shape change breaks the same-actor estimate binding");
        var jumped=changed with{Center=changed.Center+new Vector3(20,0,0)};history.Capture(Frame(6,jumped),1600);
        var jump=history.Predict(jumped,.5,1600);
        assert(jump.Kind==SceneMotionKind.Unknown&&jump.Moving,"A pose discontinuity revokes history and cannot become an estimated stationary actor");
        assert(!history.Capture(Frame(7,jumped),1901)&&!history.Predict(jumped,.5,1901).SourceFresh,"Stale captures revoke prediction authority rather than renewing from read time");
        history.Reset();history.Capture(first,1000);var newEpoch=Frame(1,unknown) with{Session=12};history.Capture(newEpoch,1100);
        assert(!history.Capture(first with{UtcMs=1100},1100),"Retired source session cannot return with recycled actor IDs");
        history.Reset();history.Capture(Frame(49,unknown),5900);
        assert(!history.Capture(Frame(50,unknown) with{Generation=2,Sequence=2},6000),
            "A newer world generation cannot roll the producer's process/session sequence backwards");
        foreach(var mutation in new[]{first with{OwnPosition=Vector3.UnitX},first with{Scopes=[]},first with{Flags=58,Diagnostic=1},first with{BodyTotal=2}}) {
            history.Reset();history.Capture(first,1000);assert(!history.Capture(mutation,1000),"Every same-sequence snapshot metadata mutation is rejected, not just changed body positions");
        }
        var mutable=new List<SceneBody>{unknown};var externallyOwned=first with{Bodies=mutable};history.Reset();history.Capture(externallyOwned,1000);
        mutable[0]=unknown with{Center=Vector3.Zero};
        assert(!history.Capture(externallyOwned,1000),"Captured body lists are owned copies and later caller mutation cannot rewrite the old baseline");
        history.Reset();assert(!history.Capture(Frame(0,Actor(2) with{Rotation=new(0,0,0,0)}),1000),"A physics flag cannot bypass geometry validation");
        history.Reset();history.Capture(first,1000);
        var truck=new VehicleBody(Vector3.Zero,Quaternion.Identity,new(-1.3f,0,-3),new(1.3f,4,4));
        var trailer=new VehicleBody(new(0,0,7),Quaternion.Identity,new(-1.3f,0,-5.5f),new(1.3f,4,6.5f));
        var prediction=ArticulatedRigPrediction.Predict(new(truck,trailer,new(0,1,2),new(0,1,-5),new(0,.5f,3),false,false,
            [new(new(0,1,2),0,0),new(new(0,1,-28),0,30)],0,.05));
        var local=history.Predict(unknown,.5,1000);
        assert(local.Kind==SceneMotionKind.Unknown&&prediction.ClearOf(unknown,local.Reserve),"An unknown-motion actor outside the actual expanded driving corridor does not require a global stop");
        var onPath=unknown with{Center=new(0,2,-10)};
        assert(!prediction.ClearOf(onPath,local.Reserve),"The same local uncertainty still occupies a relevant actor on the actual path");
    }
}
