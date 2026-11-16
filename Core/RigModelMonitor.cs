using System.Numerics;
namespace MautAssistent;

public enum RigModelCheckKind { Unavailable,WithinModel,ContactGrace,Deviation,SourceChanged }
public sealed record RigModelCheck(RigModelCheckKind Kind,string Reason,double TruckError,double TrailerError,double TruckBudget,double TrailerBudget)
{
    public bool WithinModel=>Kind is RigModelCheckKind.WithinModel or RigModelCheckKind.ContactGrace;
    // This monitor requests ordinary local caution. Imminent actual collision
    // evidence belongs to the independent acute-hazard/braking guard.
    public bool RequiresCaution=>!WithinModel;
}

public sealed class RigModelMonitor
{
    sealed record Stored(RigPredictionInput Input,RigPredictionResult Prediction,VehicleGeometry Shape,string Path,string Wheels,WheelAxleProof Axle);
    Stored? stored;
    VehicleGeometry? checkedShape;
    string? failure;
    string? stableWheels;
    Vector3 stableAxle;
    int stableAxles;
    long stableAt=-1;
    public bool Faulted=>failure!=null;
    public string? Failure=>failure;
    public string? StoredPathIdentity=>stored?.Path;
    public void Reset(){stored=null;checkedShape=null;failure=null;stableWheels=null;stableAt=-1;}

    // A subsequent model may replace the previous one ONLY after Check has
    // compared this exact new source frame to that previous model. Calling
    // Capture first cannot erase a measured departure from the old prediction.
    public bool Capture(RigPredictionInput input,RigPredictionResult prediction,VehicleGeometry shape,string pathIdentity,
        string wheelConfigKey,WheelAxleProof axle,long now)
    {
        if(failure!=null||!Fresh(shape,now)||input==null||prediction==null||!prediction.Supported||prediction.Cells.Count==0||
            !Key(pathIdentity)||!Key(wheelConfigKey)||shape.Trailer==null||input.Truck!=shape.Truck||input.Trailer!=shape.Trailer||
            input.Path==null||input.Path.Count==0||input.HitchChordError is not { } chord||!double.IsFinite(chord)||chord<0||
            !double.IsFinite(input.TrackingError)||input.TrackingError<0||
            !SceneSnapshotProtocol.Finite(input.TruckSourceTranslation)||!SceneSnapshotProtocol.Finite(input.TrailerSourceTranslation)||
            input.TruckSourceTranslation.Length()>5||input.TrailerSourceTranslation.Length()>5||
            input.TrailerAxleLocal is not { } a||axle==null||!axle.Supported||Vector3.Distance(a,axle.Axle)>.001||
            Math.Abs(axle.HookDistance-(a.Z-input.TrailerHookLocal.Z))>.02)return false;
        if(stored!=null&&checkedShape!=shape)return false;
        if(!Contact(axle,wheelConfigKey,shape.UtcMs,out _))return false;
        var copy=input with{Path=Array.AsReadOnly(input.Path.ToArray())};
        stored=new(copy,prediction with{Cells=Array.AsReadOnly(prediction.Cells.ToArray())},shape,pathIdentity,wheelConfigKey,axle);
        checkedShape=null;return true;
    }

    // actualAlong MUST use the coordinate system of Stored Input.Path. A
    // newly pruned route commonly starts again at zero; the caller must project
    // onto its retained old trace instead of passing the new route's offset.
    public RigModelCheck Check(VehicleGeometry actual,string storedPathIdentity,string wheelConfigKey,WheelAxleProof axle,double actualAlong,long now)
    {
        if(failure!=null)return Result(RigModelCheckKind.Unavailable,failure);
        if(stored==null)return Result(RigModelCheckKind.Unavailable,"No previous supported model has been captured");
        checkedShape=null;
        if(!Fresh(actual,now))return Fail(RigModelCheckKind.Unavailable,"Measured model source is stale or invalid");
        var old=stored.Shape;
        if(actual.ProcessId!=old.ProcessId||actual.Session!=old.Session)return Fail(RigModelCheckKind.SourceChanged,"Vehicle source epoch changed");
        if(actual.Sequence<old.Sequence||actual.UtcMs<old.UtcMs||actual.Sequence==old.Sequence&&actual!=old||
            actual.Sequence>old.Sequence&&actual.UtcMs<=old.UtcMs)return Fail(RigModelCheckKind.SourceChanged,"Vehicle source regressed, froze or mutated at the same capture identity");
        if(storedPathIdentity!=stored.Path||wheelConfigKey!=stored.Wheels||!SameBounds(actual.Truck,old.Truck)||actual.Trailer==null||
            old.Trailer==null||!SameBounds(actual.Trailer,old.Trailer))return Fail(RigModelCheckKind.SourceChanged,"Path, rig configuration or local body bounds changed");
        if(axle==null||!axle.Supported||Vector3.Distance(axle.Axle,stored.Axle.Axle)>.02||axle.ActiveAxles!=stored.Axle.ActiveAxles||
            Math.Abs(axle.HookDistance-stored.Axle.HookDistance)>.02)return Fail(RigModelCheckKind.Unavailable,"Fixed axle geometry changed or is unsupported");
        if(!Contact(axle,wheelConfigKey,actual.UtcMs,out bool grace))return Fail(RigModelCheckKind.Unavailable,"Wheel contact confidence expired or lift configuration is changing");
        var path=stored.Input.Path;
        if(!double.IsFinite(actualAlong)||actualAlong<path[0].Along-.001||actualAlong>path[^1].Along+.001)
            return Fail(RigModelCheckKind.Unavailable,"Current position is outside the captured prediction's route coordinates");
        actualAlong=Math.Clamp(actualAlong,path[0].Along,path[^1].Along);
        var prefix=new List<HitchPathPoint>{path[0]};
        for(int i=1;i<path.Count;i++) {
            var p=path[i];var previous=path[i-1];
            if(p.Along<=actualAlong){prefix.Add(p);continue;}
            if(actualAlong>previous.Along) {
                double t=(actualAlong-previous.Along)/(p.Along-previous.Along);
                prefix.Add(new(Vector3.Lerp(previous.Position,p.Position,(float)t),previous.TruckYaw+Wrap(p.TruckYaw-previous.TruckYaw)*t,actualAlong));
            }
            break;
        }
        var nominal=ArticulatedRigPrediction.Predict(stored.Input with{Path=prefix});
        if(!nominal.Supported||nominal.EndTruck==null||nominal.EndTrailer==null)return Fail(RigModelCheckKind.Unavailable,"Captured model cannot be evaluated at current old-route progress");
        double numeric=.005+8*1.1920928955078125e-7*Math.Max(path.Max(p=>MaxAbs(p.Position)),Math.Max(MaxAbs(actual.Truck.Position),MaxAbs(actual.Trailer.Position)));
        double initialAlignment=Vector3.Distance(Hook(stored.Input.Truck,stored.Input.TruckHookLocal),path[0].Position);
        double initialCoupling=Vector3.Distance(Hook(stored.Input.Truck,stored.Input.TruckHookLocal),Hook(stored.Input.Trailer,stored.Input.TrailerHookLocal));
        double baseBudget=stored.Input.TrackingError+stored.Input.HitchChordError!.Value+initialAlignment+numeric;
        double heading=Wrap(ArticulatedRigPrediction.Yaw(stored.Input.Truck.Rotation)-path[0].TruckYaw);
        double reach=stored.Input.Truck.Corners().Max(c=>Vector3.Distance(c,Hook(stored.Input.Truck,stored.Input.TruckHookLocal)));
        double truckBudget=baseBudget+2*reach*Math.Sin(Math.Abs(heading)/2),trailerBudget=baseBudget+initialCoupling;
        double truckError=Deviation(actual.Truck,nominal.EndTruck,stored.Input.TruckSourceTranslation),
            trailerError=Deviation(actual.Trailer,nominal.EndTrailer,stored.Input.TrailerSourceTranslation);
        double coupling=Vector3.Distance(Hook(actual.Truck,stored.Input.TruckHookLocal),Hook(actual.Trailer,stored.Input.TrailerHookLocal));
        if(coupling>.25||truckError>truckBudget||trailerError>trailerBudget) {
            failure="Measured rig departed from its stored no-slip/tracking model";
            return new(RigModelCheckKind.Deviation,failure,truckError,trailerError,truckBudget,trailerBudget);
        }
        checkedShape=actual;
        return new(grace?RigModelCheckKind.ContactGrace:RigModelCheckKind.WithinModel,
            grace?"Within model; brief contact-confidence bounce retained under the unchanged fixed axle layout":"Measured full-body poses agree with the old model at current old-route progress",
            truckError,trailerError,truckBudget,trailerBudget);
    }

    // Explicit stopped recovery is distinct from blind per-frame Capture. A
    // caller must provide a real current speed and renewed stable source data.
    public bool RebaseAtStandstill(RigPredictionInput input,RigPredictionResult prediction,VehicleGeometry shape,string pathIdentity,
        string wheelConfigKey,WheelAxleProof axle,double measuredSpeed,long now)
    {
        if(!double.IsFinite(measuredSpeed)||Math.Abs(measuredSpeed)>.1||axle==null||!axle.Supported||!axle.ContactStable||axle.LiftTransition||!Fresh(shape,now))return false;
        var candidate=new RigModelMonitor();
        if(!candidate.Capture(input,prediction,shape,pathIdentity,wheelConfigKey,axle,now))return false;
        stored=candidate.stored;checkedShape=null;failure=null;stableWheels=candidate.stableWheels;stableAxle=candidate.stableAxle;
        stableAxles=candidate.stableAxles;stableAt=candidate.stableAt;return true;
    }
    bool Contact(WheelAxleProof proof,string key,long capture)
        =>Contact(proof,key,capture,out _);
    bool Contact(WheelAxleProof proof,string key,long capture,out bool grace)
    {
        grace=false;
        if(proof==null||!proof.Supported||proof.LiftTransition||!SceneSnapshotProtocol.Finite(proof.Axle)||!float.IsFinite(proof.HookDistance)||
            proof.HookDistance<1||proof.ActiveAxles is <1 or >4)return false;
        if(proof.ContactStable) {stableWheels=key;stableAxle=proof.Axle;stableAxles=proof.ActiveAxles;stableAt=capture;return true;}
        grace=stableAt>=0&&key==stableWheels&&Vector3.Distance(stableAxle,proof.Axle)<=.02&&stableAxles==proof.ActiveAxles&&capture>=stableAt&&capture-stableAt<=600;
        return grace;
    }
    RigModelCheck Fail(RigModelCheckKind kind,string reason){failure=reason;checkedShape=null;return Result(kind,reason);}
    static RigModelCheck Result(RigModelCheckKind kind,string reason)=>new(kind,reason,double.NaN,double.NaN,double.NaN,double.NaN);
    static bool Key(string? s)=>!string.IsNullOrWhiteSpace(s)&&s.Length<=8192;
    static bool SameBounds(VehicleBody a,VehicleBody b)=>a.Minimum==b.Minimum&&a.Maximum==b.Maximum;
    static bool Fresh(VehicleGeometry? shape,long now)=>shape!=null&&now>=0&&shape.ProcessId!=0&&shape.Session!=0&&shape.Sequence!=0&&(shape.Sequence&1)==0&&
        shape.UtcMs>=0&&(decimal)now-shape.UtcMs is >=-25 and <=250&&shape.TrailerKnown&&shape.Trailer!=null&&Body(shape.Truck)&&Body(shape.Trailer);
    static bool Body(VehicleBody b)=>b!=null&&SceneSnapshotProtocol.Finite(b.Position)&&MaxAbs(b.Position)<=1e7&&SceneSnapshotProtocol.Finite(b.Minimum)&&SceneSnapshotProtocol.Finite(b.Maximum)&&
        b.Maximum.X>b.Minimum.X&&b.Maximum.Y>b.Minimum.Y&&b.Maximum.Z>b.Minimum.Z&&float.IsFinite(b.Rotation.LengthSquared())&&Math.Abs(b.Rotation.LengthSquared()-1)<=.01;
    static Vector3 Hook(VehicleBody b,Vector3 local)=>b.Position+Vector3.Transform(local,Quaternion.Normalize(b.Rotation));
    static double Deviation(VehicleBody a,VehicleBody b,Vector3 sourceTranslation)
    {
        // Geometry allows a directed ±source segment, not an isotropic ball.
        // Compare each corner to precisely that same convex segment before
        // applying the ordinary tracking/numerical ball budget. In particular,
        // a longitudinal capture phase cannot excuse a lateral departure.
        double sx=sourceTranslation.X,sy=sourceTranslation.Y,sz=sourceTranslation.Z,lengthSquared=sx*sx+sy*sy+sz*sz;
        return a.Corners().Zip(b.Corners(),(actual,nominal)=>{
            double x=(double)actual.X-nominal.X,y=(double)actual.Y-nominal.Y,z=(double)actual.Z-nominal.Z;
            double t=lengthSquared>0?Math.Clamp((x*sx+y*sy+z*sz)/lengthSquared,-1,1):0;
            x-=t*sx;y-=t*sy;z-=t*sz;return Math.Sqrt(x*x+y*y+z*z);
        }).Max();
    }
    static double Wrap(double a)=>Math.Atan2(Math.Sin(a),Math.Cos(a));
    static double MaxAbs(Vector3 p)=>Math.Max(Math.Abs((double)p.X),Math.Max(Math.Abs((double)p.Y),Math.Abs((double)p.Z)));
}
