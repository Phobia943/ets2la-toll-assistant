using System.Numerics;

namespace MautAssistent;

// Measured body-only evidence. This intentionally does not assert that cargo,
// unobserved trailer slots or all roadside objects have been captured.
public sealed record RigRoadObservation(VehicleGeometry Shape,string Configuration,long TelemetryUtcMs,
    Vector3 TruckHook,Vector3 TrailerHook,WheelAxleProof Axle,Vector3 TelemetryTruck,
    Vector3 TelemetryTrailer,double Heading,double TrailerHeading,double Speed)
{
    public bool Valid(long now)
    {
        if(Shape is not {Truck:not null,TrailerKnown:true,Trailer:not null} s||s.ProcessId==0||s.Session==0||s.Sequence==0||(s.Sequence&1)!=0||
            string.IsNullOrEmpty(Configuration)||Configuration.Length>256||
            (decimal)now-s.UtcMs is <0 or >150||(decimal)now-TelemetryUtcMs is <0 or >150||
            !double.IsFinite(Speed)||Speed is <0 or >20d/3.6||!double.IsFinite(Heading)||!double.IsFinite(TrailerHeading)||
            !SceneSnapshotProtocol.Finite(TruckHook)||!SceneSnapshotProtocol.Finite(TrailerHook)||
            TruckHook.Length()>50||TrailerHook.Length()>50||!SceneSnapshotProtocol.Finite(TelemetryTruck)||!SceneSnapshotProtocol.Finite(TelemetryTrailer)||
            Axle is not {Supported:true,ContactStable:true,LiftTransition:false}||!SceneSnapshotProtocol.Finite(Axle.Axle)||
            !float.IsFinite(Axle.HookDistance)||Axle.HookDistance is <1 or >20||Axle.ActiveAxles is <1 or >4)return false;
        double skew=Math.Abs((double)s.UtcMs-TelemetryUtcMs)/1000;
        if(skew>.03||Math.Abs(Axle.HookDistance-(Axle.Axle.Z-TrailerHook.Z))>.02)return false;
        static bool Body(VehicleBody b)=>SceneSnapshotProtocol.Finite(b.Position)&&b.Position.Length()<1e7&&
            SceneSnapshotProtocol.Finite(b.Minimum)&&SceneSnapshotProtocol.Finite(b.Maximum)&&
            b.Maximum.X>b.Minimum.X&&b.Maximum.Y>b.Minimum.Y&&b.Maximum.Z>b.Minimum.Z&&
            (b.Maximum-b.Minimum).Length()<=70&&float.IsFinite(b.Rotation.LengthSquared())&&Math.Abs(b.Rotation.LengthSquared()-1)<.01;
        if(!Body(s.Truck)||!Body(s.Trailer))return false;
        if(Vector3.Transform(Vector3.UnitY,Quaternion.Normalize(s.Truck.Rotation)).Y<Math.Cos(Math.PI/36)||
            Vector3.Transform(Vector3.UnitY,Quaternion.Normalize(s.Trailer.Rotation)).Y<Math.Cos(Math.PI/36)||
            Vector3.Distance(s.Truck.Position+Vector3.Transform(TruckHook,s.Truck.Rotation),
                s.Trailer.Position+Vector3.Transform(TrailerHook,s.Trailer.Rotation))>.25)return false;
        double Match(VehicleBody body,Vector3 at,double heading) {
            var f=Vector3.Transform(-Vector3.UnitZ,Quaternion.Normalize(body.Rotation));f.Y=0;
            return f.LengthSquared()>.9&&Vector3.Distance(body.Position,at)<=.15+Speed*skew?
                Vector3.Dot(Vector3.Normalize(f),TrackingGuard.TruckForward(heading)):-1;
        }
        return Match(s.Truck,TelemetryTruck,Heading)>=Math.Cos(.01)&&Match(s.Trailer,TelemetryTrailer,TrailerHeading)>=Math.Cos(.01);
    }
    public bool SameConfiguration(RigRoadObservation? other)=>other!=null&&Configuration==other.Configuration&&
        Shape.ProcessId==other.Shape.ProcessId&&Shape.Session==other.Shape.Session&&
        Shape.Truck.Minimum==other.Shape.Truck.Minimum&&Shape.Truck.Maximum==other.Shape.Truck.Maximum&&
        Shape.Trailer?.Minimum==other.Shape.Trailer?.Minimum&&Shape.Trailer?.Maximum==other.Shape.Trailer?.Maximum&&
        TruckHook==other.TruckHook&&TrailerHook==other.TrailerHook&&Axle.Axle==other.Axle.Axle&&Axle.ActiveAxles==other.Axle.ActiveAxles;
}

public sealed class RigRoadPrediction
{
    public RigPredictionResult Prediction {get;}
    public Vector3 Minimum {get;}
    public Vector3 Maximum {get;}
    readonly RouteTrace ground;readonly double origin;
    RigRoadPrediction(RigPredictionResult prediction,RouteTrace ground,double origin)
    {
        Prediction=prediction;this.ground=ground;this.origin=origin;
        var low=new Vector3(float.PositiveInfinity);var high=new Vector3(float.NegativeInfinity);
        foreach(var cell in prediction.Cells)foreach(var pair in new[]{(cell.Truck,cell.TruckMotionReserve,cell.TruckSourceTranslation),(cell.Trailer,cell.TrailerMotionReserve,cell.TrailerSourceTranslation)}) {
            var reserve=Vector3.Abs(cell.HitchTranslationHalf)+Vector3.Abs(pair.Item3)+new Vector3((float)pair.Item2+.01f);
            foreach(var corner in pair.Item1.Corners()){low=Vector3.Min(low,corner-reserve);high=Vector3.Max(high,corner+reserve);}
        }
        Minimum=low;Maximum=high;
    }
    public static RigRoadPrediction? Build(RigRoadObservation observation,IReadOnlyList<TraceSlicePoint> ahead,
        IReadOnlyList<TraceSlicePoint> tail,long now,out string? issue)
    {
        issue="Gespannmessung oder rückwärtiger Straßenbezug fehlt";
        if(observation==null||!observation.Valid(now)||ahead==null||tail==null||ahead.Count is <2 or >1000||tail.Count is <2 or >160)return null;
        var join=tail[^1];var first=ahead[0];
        if(join.Position!=first.Position||Math.Abs(join.Along-first.Along)>.00001||join.SegmentIdentity!=first.SegmentIdentity||
            first.Along-tail[0].Along is <1 or >60)return null;
        var points=tail.Concat(ahead.Skip(1)).ToArray();
        for(int i=0;i<points.Length;i++) {
            var p=points[i];
            if(!SceneSnapshotProtocol.Finite(p.Position)||!SceneSnapshotProtocol.Finite(p.Direction)||
                p.Direction.LengthSquared() is <.5f or >1.5f||!double.IsFinite(p.Along)||string.IsNullOrEmpty(p.SegmentIdentity)||
                p.SegmentIdentity.Length>512)return null;
            if(i==0)continue;
            var d=p.Position-points[i-1].Position;double step=p.Along-points[i-1].Along;
            if(step is <=0 or >.501||Math.Abs(d.Length()-step)>.05||Math.Abs(d.Y)>Math.Sqrt((double)d.X*d.X+(double)d.Z*d.Z)*.03)return null;
        }
        var s=observation.Shape;var trailer=s.Trailer!;var truckQ=Quaternion.Normalize(s.Truck.Rotation);
        double yaw0=ArticulatedRigPrediction.Yaw(truckQ),firstYaw=Math.Atan2(-first.Direction.X,-first.Direction.Z);
        double residual=Wrap(yaw0-firstYaw);var shift=s.Truck.Position-first.Position;
        if(shift.Length()>.15||Math.Abs(residual)>.01){issue="Gespannpose ist nicht eng an die vorgegebene Route gebunden";return null;}
        double rear=trailer.Corners().Concat(s.Truck.Corners()).Max(p=>Math.Max(0,-Vector3.Dot(p-s.Truck.Position,ArticulatedRigPrediction.Forward(yaw0))));
        double minimum=observation.Speed+observation.Speed*observation.Speed/(2*1.2)+3+rear;
        // Ground evidence must extend past the final predicted tractor front;
        // otherwise a clear query becomes unknown only at its last cell.
        double groundMargin=s.Truck.Corners().Max(p=>Vector3.Distance(p,s.Truck.Position))+2;
        // This is a low-speed local conflict query, not a full-route planner.
        // Keep work bounded even when the nominal query covers 300 metres.
        double motionEnd=Math.Min(first.Along+60,ahead[^1].Along-groundMargin);
        var motion=ahead.Where(p=>p.Along<=motionEnd).ToArray();
        if(motion.Length<2||motion[^1].Along-first.Along<minimum){issue="Route deckt Bremsweg und gemessenes Gespannheck nicht ab";return null;}
        var predicted=PredictMotion(observation,motion,out issue);
        if(predicted==null)return null;
        var road=new RouteTrace(points.Select(p=>p.Position),points.Select((p,i)=>(p,i)).Where(p=>p.i>0&&p.p.Connector).Select(p=>p.i),points.Skip(1).Select(p=>p.SegmentIdentity!));
        issue=null;return new(predicted,road,points[0].Along);
    }
    static RigPredictionResult? PredictMotion(RigRoadObservation observation,IReadOnlyList<TraceSlicePoint> motion,out string? issue)
    {
        issue=null;var first=motion[0];var s=observation.Shape;var trailer=s.Trailer!;
        var truckQ=Quaternion.Normalize(s.Truck.Rotation);double yaw0=ArticulatedRigPrediction.Yaw(truckQ);
        double residual=Wrap(yaw0-Math.Atan2(-first.Direction.X,-first.Direction.Z));var shift=s.Truck.Position-first.Position;
        if(shift.Length()>.15||Math.Abs(residual)>.01){issue="Gespannpose ist nicht eng an die ursprüngliche Route gebunden";return null;}
        var hitch=new List<HitchPathPoint>();double prior=yaw0,maxDelta=0;
        foreach(var p in motion) {
            double yaw=Math.Atan2(-p.Direction.X,-p.Direction.Z)+residual*Math.Max(0,1-(p.Along-first.Along)/2);
            yaw=prior+Wrap(yaw-prior);double delta=Math.Abs(yaw-prior);
            if(delta>.15){issue="Kurvenabtastung liegt außerhalb der begrenzten Gespannvorhersage";return null;}
            maxDelta=Math.Max(maxDelta,delta);
            var q=Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.UnitY,(float)(yaw-yaw0))*truckQ);
            hitch.Add(new(p.Position+shift+Vector3.Transform(observation.TruckHook,q),yaw,p.Along));prior=yaw;
        }
        double HeadingError(VehicleBody body,double heading) {
            var forward=Vector3.Transform(-Vector3.UnitZ,Quaternion.Normalize(body.Rotation));forward.Y=0;
            return Math.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(forward),TrackingGuard.TruckForward(heading)),-1,1));
        }
        double reach=Math.Max(s.Truck.Corners().Max(p=>Vector3.Distance(p,s.Truck.Position)),trailer.Corners().Max(p=>Vector3.Distance(p,trailer.Position)));
        double tracking=.08+shift.Length()+2*reach*Math.Sin(Math.Max(HeadingError(s.Truck,observation.Heading),HeadingError(trailer,observation.TrailerHeading))/2);
        if(tracking>1){issue="Quellabweichung überschreitet die begrenzte Gespannreserve";return null;}
        var input=new RigPredictionInput(s.Truck,trailer,observation.TruckHook,observation.TrailerHook,observation.Axle.Axle,false,false,
            hitch,observation.TruckHook.Length()*(1-Math.Cos(maxDelta/2)),tracking,.25) {
            TruckSourceTranslation=observation.TelemetryTruck-s.Truck.Position,
            TrailerSourceTranslation=observation.TelemetryTrailer-trailer.Position};
        var predicted=ArticulatedRigPrediction.Predict(input);
        if(!predicted.Supported){issue=predicted.Issue;return null;}
        return predicted;
    }
    // Final authority check only: at most 2m / 16 accepted cells. There is no
    // mesh, archive or BVH work here, and no new source timestamp is generated.
    public static bool MatchesObserved(RigRoadObservation original,RigRoadObservation current,RouteTrace route,long now)
    {
        if(!original.Valid(now)||!current.Valid(now)||!original.SameConfiguration(current)||
            current.Shape.Sequence<original.Shape.Sequence||current.Shape.UtcMs<original.Shape.UtcMs)return false;
        var before=route.ProjectForTracking(original.Shape.Truck.Position);
        var after=route.ProjectNearForTracking(current.Shape.Truck.Position,before.Along,3);
        double delta=after.Along-before.Along;
        if(before.Offset>.15||after.Offset>.15||Math.Abs(before.HeightOffset)>.15||Math.Abs(after.HeightOffset)>.15||delta is <0 or >2)return false;
        IReadOnlyList<TraceSlicePoint> points;
        if(delta<.0001) {
            if(!route.TryAtAlong(before.Along,out var point))return false;
            points=[point];
        }else if(!route.TryStandardSlice(before.Along,after.Along,.25,out points,out _)||points.Count>16)return false;
        var predicted=PredictMotion(original,points,out _);
        if(predicted is not {Supported:true,EndTruck:not null,EndTrailer:not null}||predicted.Cells.Count>16)return false;
        var last=predicted.Cells[^1];
        static bool Within(VehicleBody actual,VehicleBody expected,double reserve,Vector3 source) {
            double squared=source.LengthSquared();
            foreach(var pair in actual.Corners().Zip(expected.Corners())) {
                var diff=pair.First-pair.Second;
                double t=squared>0?Math.Clamp(Vector3.Dot(diff,source)/squared,-1,1):0;
                if((diff-source*(float)t).Length()>reserve)return false;
            }
            return true;
        }
        return Within(current.Shape.Truck,predicted.EndTruck,last.TruckMotionReserve,last.TruckSourceTranslation)&&
            Within(current.Shape.Trailer!,predicted.EndTrailer,last.TrailerMotionReserve,last.TrailerSourceTranslation);
    }
    public RigStaticGeometry.CheckResult Check(RigStaticGeometry geometry,int budget)
    {
        double? Height(double along)=>ground.TryAtAlong(along-origin,out var at)?at.Position.Y:null;
        return geometry.Check(Prediction,Matrix4x4.Identity,Height,budget,
            bodyRoadPlane:(cell,body)=>GroundForBody(ground,cell with{AlongStart=cell.AlongStart-origin,AlongEnd=cell.AlongEnd-origin},body),countNodeWork:true);
    }
    public static RigGroundPlane? GroundForBody(RouteTrace trace,RigSweptCell cell,VehicleBody body)
    {
        double reach=body.Corners().Max(p=>Vector3.Distance(p,body.Position))+2;
        var projection=trace.ProjectNearForTracking(body.Position,(cell.AlongStart+cell.AlongEnd)/2,reach*3+5);
        if(projection.Along<.01||projection.Along>trace.Length-.01||!trace.TryAtAlong(projection.Along,out var at))return null;
        var d=at.Direction;float horizontal=d.X*d.X+d.Z*d.Z;if(horizontal<.9f)return null;
        var normal=Vector3.Normalize(new Vector3(-d.X*d.Y,horizontal,-d.Z*d.Y));var point=at.Position;float minimum=0;
        foreach(var corner in body.Corners()) {
            var under=trace.ProjectNearForTracking(corner,projection.Along,reach*2+2);
            if(under.Along<.01||under.Along>trace.Length-.01||!trace.TryAtAlong(under.Along,out var roadPoint))return null;
            minimum=Math.Min(minimum,Vector3.Dot(roadPoint.Position-point,normal));
        }
        return new(point+normal*(minimum-.005f),normal);
    }
    static double Wrap(double value)=>Math.Atan2(Math.Sin(value),Math.Cos(value));
}
