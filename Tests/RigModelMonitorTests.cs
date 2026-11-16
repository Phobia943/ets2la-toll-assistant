using System.Numerics;
using MautAssistent;
internal static class RigModelMonitorTests
{
    static readonly Vector3 TruckHook=new(0,1,2),TrailerHook=new(0,1,-5),Axle=new(0,.5f,3);
    static RigPredictionInput Input()=>new(new(Vector3.Zero,Quaternion.Identity,new(-1.3f,0,-3),new(1.3f,4,4)),
        new(new(0,0,7),Quaternion.Identity,new(-1.3f,0,-5.5f),new(1.3f,4,6.5f)),TruckHook,TrailerHook,Axle,false,false,
        [new(TruckHook,0,100),new(TruckHook+new Vector3(0,0,-30),0,130)],0,.05);
    static WheelAxleProof Wheels(bool stable=true)=>new(Axle,8,3,null){ContactStable=stable};
    static VehicleGeometry Shape(RigPredictionInput input,ulong seq=2,long at=1000,float forward=0)=>new(7,9,seq,at,
        input.Truck with{Position=input.Truck.Position+new Vector3(0,0,-forward)},true,input.Trailer with{Position=input.Trailer.Position+new Vector3(0,0,-forward)});
    static RigPredictionInput CurrentInput(RigPredictionInput input,VehicleGeometry shape,double along)=>input with{
        Truck=shape.Truck,Trailer=shape.Trailer!,Path=[new(shape.Truck.Position+Vector3.Transform(TruckHook,shape.Truck.Rotation),0,along),input.Path[^1]]};
    internal static void Bounds(Action<bool,string> assert)
    {
        var input=Input();var prediction=ArticulatedRigPrediction.Predict(input);var initial=Shape(input);var monitor=new RigModelMonitor();
        assert(monitor.Capture(input,prediction,initial,"directed-path","fixed-wheels",Wheels(),1000),"A fresh supported real model with stable wheel contact can establish a monitoring baseline");
        var progressed=Shape(input,4,1100,10);var nextInput=CurrentInput(input,progressed,110);var next=ArticulatedRigPrediction.Predict(nextInput);
        assert(!monitor.Capture(nextInput,next,progressed,"directed-path","fixed-wheels",Wheels(),1100),"Capture cannot replace the old model before checking the new real frame");
        var checkedProgress=monitor.Check(progressed,"directed-path","fixed-wheels",Wheels(),110,1100);
        assert(checkedProgress.WithinModel&&checkedProgress.TruckError<.001&&checkedProgress.TrailerError<.001,
            "Actual progress is compared at old route Along110, not against the old model's start pose");
        assert(monitor.Capture(nextInput,next,progressed,"new-future-path","fixed-wheels",Wheels(),1100),"After a successful old-model check, a renewed future prediction can replace it");
        monitor.Reset();monitor.Capture(input,prediction,initial,"directed-path","fixed-wheels",Wheels(),1000);
        var drift=progressed with{Truck=progressed.Truck with{Position=progressed.Truck.Position+Vector3.UnitX},Trailer=progressed.Trailer! with{Position=progressed.Trailer.Position+Vector3.UnitX}};
        assert(monitor.Check(drift,"directed-path","fixed-wheels",Wheels(),110,1100).Kind==RigModelCheckKind.Deviation,"A significant corresponding-corner departure requests local caution");
        var driftInput=CurrentInput(input,drift,110);
        assert(!monitor.Capture(driftInput,ArticulatedRigPrediction.Predict(driftInput),drift,"directed-path","fixed-wheels",Wheels(),1100),"A fresh model fitted to the departed pose cannot blindly erase a detected failure");
        assert(!monitor.Check(progressed with{Sequence=6,UtcMs=1200},"directed-path","fixed-wheels",Wheels(),110,1200).WithinModel,
            "Returning inside the tolerance after an observed excursion does not erase its latched failure");
        assert(!monitor.RebaseAtStandstill(input,prediction,initial,"directed-path","fixed-wheels",Wheels(),.2,1000)&&monitor.Faulted,"A rolling recovery is not a stopped rebase");
        assert(!monitor.RebaseAtStandstill(input,prediction with{Supported=false},initial,"directed-path","fixed-wheels",Wheels(),0,1000)&&monitor.Faulted,
            "An unsuccessful explicit stopped recovery leaves the previous failure latch intact");
        assert(monitor.RebaseAtStandstill(input,prediction,initial,"directed-path","fixed-wheels",Wheels(),0,1000)&&!monitor.Faulted,"Explicit supported stopped recovery may establish a new measured model");
        assert(!monitor.Check(progressed,"directed-path","fixed-wheels",Wheels(),0,1100).WithinModel,"A new pruned route's zero Along cannot be confused with the captured path's absolute Along");
        monitor.Reset();monitor.Capture(input,prediction,initial,"directed-path","fixed-wheels",Wheels(),1000);
        assert(monitor.Check(progressed,"directed-path","fixed-wheels",Wheels(),110,1100).WithinModel,"Retaining the old trace coordinate makes identical physical progress valid despite external route pruning");
        monitor.Reset();assert(!monitor.Capture(input,prediction,initial,"directed-path","fixed-wheels",Wheels(false),1000),"Initial missing wheel-contact confidence has no elapsed-time grace authority");
        monitor.Capture(input,prediction,initial,"directed-path","fixed-wheels",Wheels(),1000);
        var bounced=monitor.Check(progressed,"directed-path","fixed-wheels",Wheels(false),110,1100);
        assert(bounced.Kind==RigModelCheckKind.ContactGrace&&bounced.WithinModel,"A brief contact bounce retains unchanged validated axle geometry under reduced confidence");
        monitor.Capture(nextInput,next,progressed,"directed-path","fixed-wheels",Wheels(false),1100);
        assert(!monitor.Check(progressed with{Sequence=6,UtcMs=1701},"directed-path","fixed-wheels",Wheels(false),110,1701).WithinModel,
            "Capturing a bounced frame cannot refresh the six-hundred-millisecond stable-contact lease");
        monitor.Reset();monitor.Capture(input,prediction,initial,"directed-path","fixed-wheels",Wheels(),1000);
        assert(!monitor.Check(progressed,"directed-path","fixed-wheels",Wheels() with{LiftTransition=true},110,1100).WithinModel,"An axle lift transition is distinct from a contact-confidence bounce");
        monitor.Reset();monitor.Capture(input,prediction,initial,"directed-path","fixed-wheels",Wheels(),1000);
        assert(monitor.Check(progressed with{Session=10},"directed-path","fixed-wheels",Wheels(),110,1100).Kind==RigModelCheckKind.SourceChanged,"A vehicle source epoch change revokes the old prediction");
        monitor.Reset();monitor.Capture(input,prediction,initial,"directed-path","fixed-wheels",Wheels(),1000);
        assert(!monitor.Check(initial with{Truck=initial.Truck with{Position=new(.01f,0,0)}},"directed-path","fixed-wheels",Wheels(),100,1000).WithinModel,
            "A pose mutation at an identical source capture identity is rejected even when it would fit the tracking tolerance");
        monitor.Reset();monitor.Capture(input,prediction,initial,"directed-path","fixed-wheels",Wheels(),1000);
        assert(!monitor.Check(initial,"directed-path","fixed-wheels",Wheels(),100,1251).WithinModel,"Paused or stale source data cannot renew monitor confidence");
        monitor.Reset();assert(!monitor.Faulted&&!monitor.Check(initial,"directed-path","fixed-wheels",Wheels(),100,1000).WithinModel,
            "F9 reset revokes prior history; no old prediction or contact lease silently survives it");
        SourcePhase(assert);
    }
    static void SourcePhase(Action<bool,string> assert)
    {
        var input=Input() with{TrackingError=.08,TruckSourceTranslation=new(0,0,-.089f),TrailerSourceTranslation=new(0,0,-.089f)};
        const double progressAt20Kph=20/3.6*.1,along=100+progressAt20Kph;
        var prediction=ArticulatedRigPrediction.Predict(input);var initial=Shape(input);var actual=Shape(input,4,1100,(float)progressAt20Kph);
        var monitor=new RigModelMonitor();monitor.Capture(input,prediction,initial,"phase-path","fixed-wheels",Wheels(),1000);
        var phased=monitor.Check(actual,"phase-path","fixed-wheels",Wheels(),along+.089,1100);
        assert(phased.WithinModel&&phased.TruckError<.001&&phased.TrailerError<.001,
            "At 20km/h over 100ms, an extra 8.9cm native/SCS capture phase is inside its directed source segment");
        monitor.Reset();monitor.Capture(input,prediction,initial,"phase-path","fixed-wheels",Wheels(),1000);
        var lateral=actual with{Truck=actual.Truck with{Position=actual.Truck.Position+new Vector3(.089f,0,0)},
            Trailer=actual.Trailer! with{Position=actual.Trailer.Position+new Vector3(.089f,0,0)}};
        assert(monitor.Check(lateral,"phase-path","fixed-wheels",Wheels(),along,1100).Kind==RigModelCheckKind.Deviation,
            "The same 8.9cm displacement sideways is not excused by a longitudinal source-phase segment");
        monitor.Reset();monitor.Capture(input,prediction,initial,"phase-path","fixed-wheels",Wheels(),1000);
        assert(monitor.Check(actual,"phase-path","fixed-wheels",Wheels(),along+.18,1100).Kind==RigModelCheckKind.Deviation,
            "Projection clamps to both finite segment endpoints instead of allowing unbounded longitudinal drift");
        var truckOnly=input with{TrailerSourceTranslation=Vector3.Zero};
        monitor.Reset();monitor.Capture(truckOnly,ArticulatedRigPrediction.Predict(truckOnly),Shape(truckOnly),"phase-path","fixed-wheels",Wheels(),1000);
        var trailerPhase=actual with{Trailer=actual.Trailer! with{Position=actual.Trailer.Position+new Vector3(0,0,.089f)}};
        assert(monitor.Check(trailerPhase,"phase-path","fixed-wheels",Wheels(),along,1100).Kind==RigModelCheckKind.Deviation,
            "A truck's source segment cannot widen the independently measured trailer's model tolerance");
    }
}
