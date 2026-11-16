using System.Numerics;
using MautAssistent;

internal static class ArticulatedRigPredictionTests
{
    static readonly Vector3 TruckHook=new(0,1,2),TrailerHook=new(0,1,-5),Axle=new(0,.5f,3);
    static VehicleBody Truck()=>new(Vector3.Zero,Quaternion.Identity,new(-1.25f,.1f,-3),new(1.25f,4,4));
    static VehicleBody Trailer(double yaw=0)
    {
        var q=Quaternion.CreateFromAxisAngle(Vector3.UnitY,(float)yaw);var h=TruckHook;
        return new(h-Vector3.Transform(TrailerHook,q),q,new(-1.3f,.2f,-5.5f),new(1.3f,4,6.5f));
    }
    static RigPredictionInput Straight(double trailerYaw=0)=>new(Truck(),Trailer(trailerYaw),TruckHook,TrailerHook,Axle,false,false,
        [new(TruckHook,0,0),new(TruckHook+new Vector3(0,0,-30),0,30)],0,.05,.25);
    static SceneBody Obstacle(Vector3 center,Vector3? half=null)=>new(1,2,0,center,Quaternion.Identity,half??new(1,2,3),1);
    static double AngleDifference(double a,double b)=>Math.Atan2(Math.Sin(a-b),Math.Cos(a-b));
    static Vector3 Hook(VehicleBody b,Vector3 local)=>b.Position+Vector3.Transform(local,Quaternion.Normalize(b.Rotation));
    internal static void Bounds(Action<bool,string> assert)
    {
        var straight=Straight();var prediction=ArticulatedRigPrediction.Predict(straight);
        assert(prediction.Supported,"An actual coupled fixed-axle forward rig has a supported explicit hitch-path model");
        assert(prediction.ClearOf(Obstacle(new(5,2,-10))),"A parked car five metres beside a straight rig does not occupy an arbitrary 15m all-articulation disk");
        assert(!prediction.ClearOf(Obstacle(new(0,2,-10))),"A measured car on the actual truck/trailer path is occupied");
        assert(prediction.ClearOf(Obstacle(new(30,2,-10))),"Unrelated distant bodies do not globally invalidate the complete geometric prediction");
        var wide=straight with{Truck=straight.Truck with{Minimum=new(-1.54f,.1f,-3),Maximum=new(1.54f,4,4)},TrackingError=.05,MaxStep=.25};
        var narrowGate=ArticulatedRigPrediction.Predict(wide);
        foreach(float side in new[]{-1f,1f})assert(narrowGate.Supported&&narrowGate.ClearOf(Obstacle(new(side*2.2f,2,-10),new(.5f,3,100))),
            "A 3.08m measured body plus 5cm tracking reserve fits a straight 3.4m gate: forward cell length adds no fictitious lateral width");
        assert(!narrowGate.ClearOf(Obstacle(new(0,2,-10))),"Anisotropic translation still occupies the actual forward vehicle path");
        assert(prediction.EndTruck!.Minimum==straight.Truck.Minimum&&prediction.EndTrailer!.Maximum==straight.Trailer.Maximum,
            "Measured local bounds are retained, rather than reconstructed from a guessed rig length");
        assert(Vector3.Distance(Hook(prediction.EndTruck,TruckHook),Hook(prediction.EndTrailer!,TrailerHook))<.0001,
            "Truck and trailer world hooks remain geometrically coupled at the predicted end");
        assert(Vector3.Distance(prediction.EndTruck.Position,new(0,0,-30))<.0001,
            "The truck body origin advances correctly relative to its actual local hook");
        var stand=ArticulatedRigPrediction.Predict(straight with{Path=[straight.Path[0]]});
        assert(stand.Supported&&stand.Cells.Count==1&&stand.ClearOf(Obstacle(new(5,2,5))),"A stationary rig has actual separate OBBs, not a blanket enclosing disk");
        assert(!stand.ClearOf(Obstacle(new(0,2,12))),"The full current trailer rear remains occupied at standstill");
        var bent=ArticulatedRigPrediction.Predict(Straight(.45));
        double reference=ReferenceYaw(.45,0,30,8);
        assert(bent.Supported&&Math.Abs(AngleDifference(ArticulatedRigPrediction.Yaw(bent.EndTrailer!.Rotation),reference))<.00001,
            "An initially articulated trailer follows an independently integrated no-slip ODE instead of snapping to truck yaw");
        assert(Math.Abs(bent.Cells[0].TrailerYawStart-.45)<.00001,"The first interval begins at the measured articulated orientation");
        var tinyInput=Straight(.45) with{Path=[new(TruckHook,0,0),new(TruckHook+new Vector3(0,0,-.002f),0,.002)]};
        var tiny=ArticulatedRigPrediction.Predict(tinyInput);
        var axleStart=Hook(tinyInput.Trailer,Axle);var axleEnd=Hook(tiny.EndTrailer!,Axle);
        double midYaw=(ArticulatedRigPrediction.Yaw(tinyInput.Trailer.Rotation)+ArticulatedRigPrediction.Yaw(tiny.EndTrailer!.Rotation))/2;
        var transverse=new Vector3((float)-Math.Cos(midYaw),0,(float)Math.Sin(midYaw));
        assert(tiny.Supported&&Math.Abs(Vector3.Dot((axleEnd-axleStart)/.002f,transverse))<.001,
            "The measured axle's independently differenced velocity has no lateral component under native -Z yaw conventions");
        var negative=ArticulatedRigPrediction.Predict(Straight(-.45));
        assert(negative.Supported&&Math.Abs(ArticulatedRigPrediction.Yaw(negative.EndTrailer!.Rotation)+ArticulatedRigPrediction.Yaw(bent.EndTrailer!.Rotation))<.00001,
            "Left and right initial articulation converge symmetrically");
        var fine=ArticulatedRigPrediction.Predict(Straight(.45) with{MaxStep=.05});
        var coarse=ArticulatedRigPrediction.Predict(Straight(.45) with{MaxStep=.8});
        assert(fine.Supported&&coarse.Supported&&fine.Cells.Count>coarse.Cells.Count&&
            Vector3.Distance(fine.EndTrailer!.Position,coarse.EndTrailer!.Position)<.00001&&
            Math.Abs(AngleDifference(ArticulatedRigPrediction.Yaw(fine.EndTrailer.Rotation),ArticulatedRigPrediction.Yaw(coarse.EndTrailer.Rotation)))<.000001,
            "Cell refinement changes enclosure tightness, never the exact segment end pose");
        var divided=straight with{Trailer=Trailer(.45),Path=Enumerable.Range(0,31).Select(i=>new HitchPathPoint(TruckHook+new Vector3(0,0,-i),0,i)).ToArray()};
        var many=ArticulatedRigPrediction.Predict(divided);
        assert(many.Supported&&Vector3.Distance(many.EndTrailer!.Position,bent.EndTrailer!.Position)<.00002,
            "Subdividing an identical straight hitch path preserves the exact no-slip solution");
        Turns(assert);Strict(assert);ReservesAndFrames(assert);Continuous(assert);
    }

    static RigPredictionInput Turn(int side=1)=>Straight() with {Path=[new(TruckHook,0,0),new(TruckHook+new Vector3(0,0,-10),0,10),
        new(TruckHook+new Vector3(-10*side,0,-10),side*Math.PI/2,20),new(TruckHook+new Vector3(-20*side,0,-10),side*Math.PI/2,30)],TrackingError=.01};
    static void Turns(Action<bool,string> assert)
    {
        foreach(int side in new[]{-1,1}) {
            var input=Turn(side);var prediction=ArticulatedRigPrediction.Predict(input);
            double yaw=ReferenceYaw(0,side*Math.PI/2,2,8);var h=TruckHook+new Vector3(-2*side,0,-10);
            var rear=h+Vector3.Transform(new Vector3(1.3f*side,1,11.5f),Quaternion.CreateFromAxisAngle(Vector3.UnitY,(float)yaw));
            var obstacle=Obstacle(rear,new(.12f,.5f,.12f));
            var start=ArticulatedRigPrediction.Predict(input with{Path=[input.Path[0]]});
            assert(prediction.Supported&&start.ClearOf(obstacle)&&!prediction.ClearOf(obstacle),
                "A real turn's trailer rear outswing is occupied even where the same initially straight rig is clear");
            assert(prediction.ClearOf(Obstacle(new(20*side,2,-15))),"Turn prediction does not replace both bodies with a fixed long-radius circle");
        }
        double startYaw=Math.PI-.01,endYaw=-Math.PI+.01;var q=Quaternion.CreateFromAxisAngle(Vector3.UnitY,(float)startYaw);
        var truck=Truck() with{Rotation=q};var h0=Hook(truck,TruckHook);
        var trailer=Trailer() with{Rotation=q,Position=h0-Vector3.Transform(TrailerHook,q)};
        var wrapped=Straight() with{Truck=truck,Trailer=trailer,Path=[new(h0,startYaw,0),new(h0+new Vector3(0,0,10),endYaw,10)]};
        var result=ArticulatedRigPrediction.Predict(wrapped);
        assert(result.Supported&&Math.Abs(AngleDifference(ArticulatedRigPrediction.Yaw(result.EndTruck!.Rotation),endYaw))<.00001,
            "Crossing -pi/pi follows the short yaw turn instead of spinning the rig through a full revolution");
    }

    static void Strict(Action<bool,string> assert)
    {
        var valid=Straight();bool Reject(RigPredictionInput i)=>!ArticulatedRigPrediction.Predict(i).Supported;
        assert(Reject(valid with{TrailerAxleLocal=null}),"An absent measured axle never falls back to bounding-box length");
        assert(Reject(valid with{TrailerAxleLocal=TrailerHook})&&Reject(valid with{TrailerAxleLocal=TrailerHook-new Vector3(0,0,2)}),
            "Zero or forward-pointing hook-to-axle length is unsupported");
        assert(Reject(valid with{TrailerHookLocal=new(float.NaN,0,0)})&&Reject(valid with{TrailerAxleLocal=new(float.PositiveInfinity,0,0)}),
            "Nonfinite measured hook or wheel coordinates fail closed");
        assert(Reject(valid with{HitchChordError=null})&&Reject(valid with{HitchChordError=double.NaN})&&Reject(valid with{TrackingError=-.1}),
            "Unknown or invalid path approximation/tracking reserves cannot imply exact following");
        assert(Reject(valid with{Reverse=true})&&Reject(valid with{SteerableTrailer=true}),"Reverse and steering trailer modes require a different motion model");
        assert(Reject(valid with{Path=[valid.Path[0],new(TruckHook+new Vector3(0,0,10),0,10)]}),"Reversed native -Z travel is rejected even if a caller forgets the reverse flag");
        assert(Reject(valid with{Path=[valid.Path[0],new(TruckHook+new Vector3(0,0,-10),2,10)]}),
            "A segment whose midpoint looks forward cannot conceal reverse hitch travel near one endpoint");
        assert(Reject(valid with{Path=[valid.Path[0],new(TruckHook,.1,1)]}),"A stationary hitch cannot conceal a rotation in place");
        assert(Reject(valid with{Path=[valid.Path[0],new(TruckHook+new Vector3(0,1,-1),0,1)]}),"A steep path does not acquire planar-model permission");
        assert(Reject(valid with{Truck=valid.Truck with{Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitX,.2f)}}),"Large pitch is outside the declared near-flat model");
        assert(Reject(Straight(Math.PI-.01)),"Near-jackknife articulation is explicitly unsupported rather than tan-clamped");
        assert(Reject(valid with{Trailer=valid.Trailer with{Position=valid.Trailer.Position+new Vector3(.3f,0,0)}}),"Inconsistent measured coupling beyond 25cm cannot be silently snapped together");
        assert(Reject(valid with{Path=[new(TruckHook+Vector3.UnitX,0,0),valid.Path[1]]}),"A path start beyond the explicit tracking tolerance is unsupported");
        assert(!ArticulatedRigPrediction.Predict(valid).ClearOf(Obstacle(Vector3.Zero) with{Rotation=new(0,0,0,0)}),"Invalid obstacle orientation never creates separation");
        var duplicates=ArticulatedRigPrediction.Predict(valid with{Path=[valid.Path[0],valid.Path[0],valid.Path[1]]});
        assert(duplicates.Supported&&Vector3.Distance(duplicates.EndTrailer!.Position,new(0,0,-23))<.0001,"A true zero-length repeated sample is harmless and does not change progression");
    }

    static void ReservesAndFrames(Action<bool,string> assert)
    {
        var raw=Straight() with{TrackingError=.5,Truck=Truck() with{Position=new(.5f,0,0)},Trailer=Trailer() with{Position=Trailer().Position+new Vector3(.5f,0,0)}};
        var aligned=ArticulatedRigPrediction.Predict(raw);
        assert(aligned.Supported&&aligned.StartAlignment>.499&&!aligned.ClearOf(Obstacle(new(-1.65f,2,-10),new(.03f,.5f,.03f))),
            "Raw-path two-sided tracking error retains the complete error plus start-alignment offset");
        var mismatch=Straight() with{TrackingError=0,Trailer=Trailer() with{Position=Trailer().Position+new Vector3(.24f,0,0)}};
        var protectedStart=ArticulatedRigPrediction.Predict(mismatch with{Path=[mismatch.Path[0]]});
        assert(protectedStart.Supported&&protectedStart.CouplingGap>.239&&!protectedStart.ClearOf(Obstacle(new(1.53f,2,12),new(.01f,.5f,.1f))),
            "A tolerated coupling measurement offset retains the full original trailer start geometry");
        var noChord=Straight() with{TrackingError=0,MaxStep=.025};var sampled=ArticulatedRigPrediction.Predict(noChord);
        var bounded=ArticulatedRigPrediction.Predict(noChord with{HitchChordError=.3});
        var near=Obstacle(new(1.5f,2,-10),new(.02f,.5f,.02f));
        assert(sampled.Supported&&bounded.Supported&&sampled.ClearOf(near)&&!bounded.ClearOf(near),
            "An explicitly declared curved-hitch spatial approximation reserve widens the chord corridor; it is not a yaw-accuracy proof for an unknown curve");
        var outsideHook=new Vector3(0,1,10);var outside=Straight() with{TruckHookLocal=outsideHook,
            Trailer=Trailer() with{Position=outsideHook-TrailerHook},Path=[new(outsideHook,0,0),new(outsideHook+new Vector3(0,0,-30),0,30)]};
        var outsideResult=ArticulatedRigPrediction.Predict(outside);
        assert(outsideResult.Supported&&Vector3.Distance(outsideResult.EndTruck!.Position,new(0,0,-30))<.0001,
            "A known hook outside body bounds still uses its actual lever arm and body origin, never a guessed rig disk");
        var tilt=Quaternion.Normalize(Quaternion.CreateFromYawPitchRoll(.2f,.02f,-.015f));var truck=Truck() with{Rotation=tilt};
        var trailer=Trailer() with{Rotation=tilt,Position=Hook(truck,TruckHook)-Vector3.Transform(TrailerHook,tilt)};
        var initial=ArticulatedRigPrediction.Predict(Straight() with{Truck=truck,Trailer=trailer,Path=[new(Hook(truck,TruckHook),ArticulatedRigPrediction.Yaw(tilt),0)]});
        assert(initial.Supported&&Quaternion.Dot(initial.EndTruck!.Rotation,tilt)>.999999&&Vector3.Distance(initial.EndTruck.Position,truck.Position)<.0001,
            "Small measured initial roll and pitch remain in the model pose rather than being discarded");
    }

    static void Continuous(Action<bool,string> assert)
    {
        var input=Turn();var result=ArticulatedRigPrediction.Predict(input);assert(result.Supported,"The continuous-turn enclosure fixture is supported");
        foreach(var cell in result.Cells) {
            var delta=cell.HitchEnd-cell.HitchStart;double length=Math.Sqrt((double)delta.X*delta.X+(double)delta.Z*delta.Z);
            double direction=Math.Atan2(-delta.X,-delta.Z);
            for(int k=0;k<=4;k++) {
                double t=k/4d;var h=Vector3.Lerp(cell.HitchStart,cell.HitchEnd,(float)t);
                double yaw=ReferenceYaw(cell.TrailerYawStart,direction,length*t,8);
                var q=Quaternion.CreateFromAxisAngle(Vector3.UnitY,(float)yaw);
                var body=input.Trailer with{Rotation=q,Position=h-Vector3.Transform(TrailerHook,q)};
                var translation=(cell.HitchEnd-cell.HitchStart)*(float)(t-.5);
                foreach(var corner in body.Corners())assert(DistanceToBox(corner-translation,cell.Trailer)<=cell.TrailerMotionReserve+.0001,
                    "Independent intermediate no-slip poses, including every rear corner, lie inside the continuous swept cell");
            }
        }
    }
    static double DistanceToBox(Vector3 point,VehicleBody box)
    {
        var local=Vector3.Transform(point-box.Position,Quaternion.Inverse(Quaternion.Normalize(box.Rotation)));
        return Vector3.Distance(local,Vector3.Clamp(local,box.Minimum,box.Maximum));
    }
    // Independent small-step RK4 integration, rather than the product's exact
    // tan/exponential solution or an Euler construction that shares its errors.
    static double ReferenceYaw(double yaw,double direction,double distance,double length)
    {
        int count=Math.Max(1,(int)Math.Ceiling(distance/.002));double ds=distance/count;
        double F(double angle)=>Math.Sin(direction-angle)/length;
        for(int i=0;i<count;i++) {
            double a=F(yaw),b=F(yaw+ds*a/2),c=F(yaw+ds*b/2),d=F(yaw+ds*c);yaw+=ds*(a+2*b+2*c+d)/6;
        }
        return yaw;
    }
}
