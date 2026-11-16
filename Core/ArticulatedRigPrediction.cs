using System.Numerics;
namespace MautAssistent;

// Position is the WORLD hitch position, not the SCS body origin. Along is the
// caller's monotonically increasing route reference. Native model -Z is forward.
public readonly record struct HitchPathPoint(Vector3 Position,double TruckYaw,double Along);
public sealed record RigPredictionInput(VehicleBody Truck,VehicleBody Trailer,
    Vector3 TruckHookLocal,Vector3 TrailerHookLocal,Vector3? TrailerAxleLocal,
    bool SteerableTrailer,bool Reverse,IReadOnlyList<HitchPathPoint> Path,
    double? HitchChordError,double TrackingError=.2,double MaxStep=.25)
{
    // Full ± observed native-to-SCS translation segments, not half vectors and
    // not isotropic radii. Capture times remain the caller's unchanged sources.
    public Vector3 TruckSourceTranslation {get;init;}
    public Vector3 TrailerSourceTranslation {get;init;}
}
public sealed record RigSweptCell(double AlongStart,double AlongEnd,VehicleBody Truck,VehicleBody Trailer,
    double TruckMotionReserve,double TrailerMotionReserve,Vector3 HitchStart,Vector3 HitchEnd,
    double TrailerYawStart,double TrailerYawEnd)
{
    // Translation is the actual directed segment, not an isotropic radius:
    // forward progress must not invent extra width in a straight narrow lane.
    public Vector3 HitchTranslationHalf=>(HitchEnd-HitchStart)/2;
    public Vector3 TruckSourceTranslation {get;init;}
    public Vector3 TrailerSourceTranslation {get;init;}
}
public sealed record RigPredictionResult(bool Supported,string? Issue,IReadOnlyList<RigSweptCell> Cells,
    VehicleBody? EndTruck,VehicleBody? EndTrailer,double StartAlignment,double CouplingGap)
{
    // Geometric result for this explicitly supplied motion model only. Fresh
    // scene coverage, actor motion, tracking validity and driving authority are
    // deliberately the caller's separate responsibilities.
    public bool ClearOf(SceneBody obstacle,double obstacleMotionReserve=0)=>Supported&&Cells.Count>0&&
        Cells.All(c=>ArticulatedRigPrediction.Outside(c,obstacle,obstacleMotionReserve));
}

public static class ArticulatedRigPrediction
{
    public const string Assumptions="Piecewise-linear world hitch path; forward planar no-slip fixed trailer axle; " +
        "constant initial roll/pitch on near-flat ground. Hitch sampling and full-body tracking errors are separate caller bounds. " +
        "This is a motion-model prediction, not unconditional physical collision clearance.";
    const double MaxTilt=.08726646259971647,MaxGrade=.05,MaxArticulation=2.6179938779914944;
    const double MaxCellYaw=.03;
    const int MaxCells=8192;
    static RigPredictionResult Reject(string issue)=>new(false,issue,[],null,null,0,0);

    public static RigPredictionResult Predict(RigPredictionInput input)
    {
        if(input==null||!ValidBody(input.Truck)||!ValidBody(input.Trailer))return Reject("Missing or invalid measured body bounds/pose");
        if(!Finite(input.TruckSourceTranslation)||!Finite(input.TrailerSourceTranslation)||
            input.TruckSourceTranslation.Length()>5||input.TrailerSourceTranslation.Length()>5)
            return Reject("Invalid source translation reserve");
        if(input.SteerableTrailer||input.Reverse)return Reject("Steerable or reversing rig is outside the forward fixed-axle model");
        if(input.TrailerAxleLocal is not { } axle||!Finite(axle)||!Finite(input.TruckHookLocal)||!Finite(input.TrailerHookLocal)||
            MaxAbs(axle)>100||MaxAbs(input.TruckHookLocal)>100||MaxAbs(input.TrailerHookLocal)>100)
            return Reject("Missing or invalid measured hook/axle geometry");
        if(input.HitchChordError is not { } chord||!double.IsFinite(chord)||chord is <0 or >.5||
            !double.IsFinite(input.TrackingError)||input.TrackingError is <0 or >1||!double.IsFinite(input.MaxStep)||input.MaxStep is <.025 or >1)
            return Reject("Missing or unbounded sampling/tracking/cell reserve");
        if(input.Path==null||input.Path.Count is <1 or >4096)return Reject("Missing or unbounded hitch path");
        var path=input.Path.ToArray();
        if(path.Any(p=>!Finite(p.Position)||MaxAbs(p.Position)>1e7||!double.IsFinite(p.TruckYaw)||Math.Abs(p.TruckYaw)>1e6||
            !double.IsFinite(p.Along)||p.Along<0||p.Along>1e7))return Reject("Nonfinite or unbounded hitch path");
        var truckQ=Quaternion.Normalize(input.Truck.Rotation);var trailerQ=Quaternion.Normalize(input.Trailer.Rotation);
        if(!NearFlat(truckQ)||!NearFlat(trailerQ))return Reject("Measured roll/pitch exceeds the near-flat model");
        double truckYaw0=Yaw(truckQ),trailerYaw0=Yaw(trailerQ),yawAlignment=Wrap(truckYaw0-path[0].TruckYaw);
        if(Math.Abs(yawAlignment)>.01||Math.Abs(Wrap(trailerYaw0-truckYaw0))>MaxArticulation)
            return Reject("Initial path yaw or articulation does not match measured poses");
        double localL=axle.Z-input.TrailerHookLocal.Z;
        var hookToAxle=Vector3.Transform(input.TrailerHookLocal-axle,trailerQ);
        double length=DotXZ(hookToAxle,Forward(trailerYaw0));
        if(localL is <=1 or >40||!double.IsFinite(length)||length<=1||length>40||Math.Abs(length-localL)>localL*.1)
            return Reject("Measured fixed axle must lie behind the hitch in native -Z coordinates");
        var actualTruckHitch=input.Truck.Position+Vector3.Transform(input.TruckHookLocal,truckQ);
        var actualTrailerHitch=input.Trailer.Position+Vector3.Transform(input.TrailerHookLocal,trailerQ);
        var alignment=actualTruckHitch-path[0].Position;
        double alignmentSize=alignment.Length(),couplingGap=Vector3.Distance(actualTruckHitch,actualTrailerHitch);
        if(!double.IsFinite(couplingGap)||couplingGap>.25||alignmentSize>input.TrackingError+.0001)
            return Reject("Measured hitch coupling or path start is inconsistent");
        // Preserve the real truck start. A small measured trailer coupling
        // mismatch is NOT discarded: every trailer cell includes that offset
        // as a separate reserve, so the complete original first body is covered.
        for(int i=0;i<path.Length;i++)path[i]=path[i] with{Position=path[i].Position+alignment,TruckYaw=path[i].TruckYaw+yawAlignment};
        double numeric=.005+8*1.1920928955078125e-7*Math.Max(path.Max(p=>MaxAbs(p.Position)),Math.Max(MaxAbs(input.Truck.Position),MaxAbs(input.Trailer.Position)));
        double truckReach=Reach(input.Truck,input.TruckHookLocal),trailerReach=Reach(input.Trailer,input.TrailerHookLocal);
        // TrackingError is relative to the supplied RAW path. Aligning that
        // path to the measured start does not consume or halve the caller's
        // future two-sided tolerance: retain the full error PLUS anchor shift.
        double spatialReserve=chord+input.TrackingError+alignmentSize+numeric;
        double headingAlignmentReserve=2*truckReach*Math.Sin(Math.Abs(yawAlignment)/2);
        var cells=new List<RigSweptCell>();double trailerYaw=trailerYaw0,total=0;
        VehicleBody TruckAt(Vector3 h,double yaw)=>Pose(input.Truck,truckQ,truckYaw0,input.TruckHookLocal,h,yaw);
        VehicleBody TrailerAt(Vector3 h,double yaw)=>Pose(input.Trailer,trailerQ,trailerYaw0,input.TrailerHookLocal,h,yaw);
        VehicleBody endTruck=TruckAt(path[0].Position,path[0].TruckYaw),endTrailer=TrailerAt(path[0].Position,trailerYaw);
        for(int segment=1;segment<path.Length;segment++) {
            var a=path[segment-1];var b=path[segment];var delta=b.Position-a.Position;
            double horizontal=Math.Sqrt((double)delta.X*delta.X+(double)delta.Z*delta.Z),distance=delta.Length();
            double truckDelta=Wrap(b.TruckYaw-a.TruckYaw),alongDelta=b.Along-a.Along;
            if(distance<=1e-6) {
                if(Math.Abs(truckDelta)>1e-6||Math.Abs(alongDelta)>1e-6)return Reject("Stationary hitch cannot hide rotation or route progress");
                continue;
            }
            if(horizontal<=1e-6||Math.Abs(delta.Y)>horizontal*MaxGrade||alongDelta<=0||distance>2000||(total+=distance)>2000)
                return Reject("Hitch path is reversing, steep, non-progressing or unbounded");
            double direction=Math.Atan2(-delta.X,-delta.Z),initialResidual=Wrap(trailerYaw-direction);
            if(Math.Abs(initialResidual)>MaxArticulation||DotXZ(delta,Forward(a.TruckYaw+truckDelta/2))<=0||
                DotXZ(delta,Forward(a.TruckYaw))< -horizontal*1e-6||DotXZ(delta,Forward(a.TruckYaw+truckDelta))< -horizontal*1e-6)
                return Reject("Reverse hitch travel or near-jackknife articulation is unsupported");
            var startRoot=TruckAt(a.Position,a.TruckYaw).Position;var endRoot=TruckAt(b.Position,a.TruckYaw+truckDelta).Position;
            if(DotXZ(endRoot-startRoot,Forward(a.TruckYaw+truckDelta/2))<-.001)
                return Reject("Truck body origin moves backwards along the supplied path");
            double segmentTrailerEnd=IntegrateYaw(trailerYaw,direction,horizontal,length);
            int count=Math.Max(1,(int)Math.Ceiling(Math.Max(distance/input.MaxStep,Math.Max(Math.Abs(truckDelta),Math.Abs(segmentTrailerEnd-trailerYaw))/MaxCellYaw)));
            if(count>MaxCells-cells.Count)return Reject("Swept cell budget exhausted");
            for(int k=0;k<count;k++) {
                double from=k/(double)count,to=(k+1d)/count,mid=(from+to)/2;
                var h0=Vector3.Lerp(a.Position,b.Position,(float)from);var h1=Vector3.Lerp(a.Position,b.Position,(float)to);var hm=Vector3.Lerp(a.Position,b.Position,(float)mid);
                double y0=IntegrateYaw(trailerYaw,direction,horizontal*from,length),ym=IntegrateYaw(trailerYaw,direction,horizontal*mid,length),y1=IntegrateYaw(trailerYaw,direction,horizontal*to,length);
                double ty=a.TruckYaw+truckDelta*mid;
                if(Math.Abs(Wrap(y0-(a.TruckYaw+truckDelta*from)))>MaxArticulation||
                    Math.Abs(Wrap(y1-(a.TruckYaw+truckDelta*to)))>MaxArticulation)return Reject("Predicted articulation exceeds the supported model");
                // The exact trailer yaw is monotone inside a straight hitch
                // segment. Its temporal midpoint is NOT generally the angular
                // midpoint, hence use the larger actual endpoint deviation.
                double trailerAngle=Math.Max(Math.Abs(ym-y0),Math.Abs(y1-ym));
                double truckAngle=Math.Abs(truckDelta)/(2*count);
                if(Math.Abs(Wrap(ym-ty))+trailerAngle+truckAngle>MaxArticulation)return Reject("Within-cell articulation exceeds the supported model");
                double truckReserve=2*truckReach*Math.Sin(truckAngle/2)+spatialReserve+headingAlignmentReserve;
                double trailerReserve=2*trailerReach*Math.Sin(trailerAngle/2)+spatialReserve+couplingGap;
                cells.Add(new(a.Along+alongDelta*from,a.Along+alongDelta*to,TruckAt(hm,ty),TrailerAt(hm,ym),truckReserve,trailerReserve,h0,h1,y0,y1)
                    {TruckSourceTranslation=input.TruckSourceTranslation,TrailerSourceTranslation=input.TrailerSourceTranslation});
            }
            trailerYaw=segmentTrailerEnd;endTruck=TruckAt(b.Position,a.TruckYaw+truckDelta);endTrailer=TrailerAt(b.Position,trailerYaw);
        }
        if(cells.Count==0)cells.Add(new(path[0].Along,path[0].Along,endTruck,endTrailer,spatialReserve+headingAlignmentReserve,
            spatialReserve+couplingGap,path[0].Position,path[0].Position,trailerYaw,trailerYaw)
            {TruckSourceTranslation=input.TruckSourceTranslation,TrailerSourceTranslation=input.TrailerSourceTranslation});
        return new(true,null,cells.AsReadOnly(),endTruck,endTrailer,alignmentSize,couplingGap);
    }

    // Exact planar fixed-axle no-slip solution for a constant hitch direction.
    // The supported residual excludes the tan singularity near a jackknife.
    static double IntegrateYaw(double start,double direction,double horizontalDistance,double length)
    {
        double residual=Wrap(start-direction);
        return start+2*Math.Atan(Math.Tan(residual/2)*Math.Exp(-horizontalDistance/length))-residual;
    }
    static VehicleBody Pose(VehicleBody body,Quaternion initial,double initialYaw,Vector3 localHook,Vector3 hitch,double yaw)
    {
        var q=Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.UnitY,(float)Wrap(yaw-initialYaw))*initial);
        return body with{Position=hitch-Vector3.Transform(localHook,q),Rotation=q};
    }
    public static double Yaw(Quaternion q)
    {
        var f=Vector3.Transform(-Vector3.UnitZ,Quaternion.Normalize(q));return Math.Atan2(-f.X,-f.Z);
    }
    public static Vector3 Forward(double yaw)=>new((float)-Math.Sin(yaw),0,(float)-Math.Cos(yaw));
    static double Wrap(double value)=>Math.Atan2(Math.Sin(value),Math.Cos(value));
    static double DotXZ(Vector3 a,Vector3 b)=>(double)a.X*b.X+(double)a.Z*b.Z;
    static double Reach(VehicleBody body,Vector3 hook)=>Enumerable.Range(0,8).Max(i=>(new Vector3((i&1)==0?body.Minimum.X:body.Maximum.X,
        (i&2)==0?body.Minimum.Y:body.Maximum.Y,(i&4)==0?body.Minimum.Z:body.Maximum.Z)-hook).Length());
    static bool NearFlat(Quaternion q)=>Vector3.Transform(Vector3.UnitY,q).Y>=Math.Cos(MaxTilt);
    static bool Finite(Vector3 p)=>float.IsFinite(p.X)&&float.IsFinite(p.Y)&&float.IsFinite(p.Z);
    static double MaxAbs(Vector3 p)=>Math.Max(Math.Abs((double)p.X),Math.Max(Math.Abs((double)p.Y),Math.Abs((double)p.Z)));
    static bool ValidBody(VehicleBody? b)=>b!=null&&Finite(b.Position)&&MaxAbs(b.Position)<=1e7&&Finite(b.Minimum)&&Finite(b.Maximum)&&
        MaxAbs(b.Minimum)<=100&&MaxAbs(b.Maximum)<=100&&(b.Maximum-b.Minimum) is var size&&size.X is >0 and <=50&&size.Y is >0 and <=50&&size.Z is >0 and <=50&&
        float.IsFinite(b.Rotation.LengthSquared())&&Math.Abs(b.Rotation.LengthSquared()-1)<=.01;

    public static bool Outside(RigSweptCell cell,SceneBody obstacle,double obstacleMotionReserve=0)
    {
        if(cell==null||obstacle==null||!ValidBody(cell.Truck)||!ValidBody(cell.Trailer)||!Finite(obstacle.Center)||!Finite(obstacle.HalfExtents)||
            obstacle.HalfExtents.X is <=0 or >100||obstacle.HalfExtents.Y is <=0 or >100||obstacle.HalfExtents.Z is <=0 or >100||
            !float.IsFinite(obstacle.Rotation.LengthSquared())||Math.Abs(obstacle.Rotation.LengthSquared()-1)>.001||
            !double.IsFinite(obstacleMotionReserve)||obstacleMotionReserve is <0 or >100||
            !double.IsFinite(cell.TruckMotionReserve)||cell.TruckMotionReserve is <0 or >100||
            !double.IsFinite(cell.TrailerMotionReserve)||cell.TrailerMotionReserve is <0 or >100)return false;
        if(!Finite(cell.HitchTranslationHalf)||!Finite(cell.TruckSourceTranslation)||!Finite(cell.TrailerSourceTranslation)||
            cell.TruckSourceTranslation.Length()>5||cell.TrailerSourceTranslation.Length()>5)return false;
        return Separate(cell.Truck,cell.TruckMotionReserve,cell.HitchTranslationHalf,cell.TruckSourceTranslation,obstacle,obstacleMotionReserve)&&
            Separate(cell.Trailer,cell.TrailerMotionReserve,cell.HitchTranslationHalf,cell.TrailerSourceTranslation,obstacle,obstacleMotionReserve);
    }
    static bool Separate(VehicleBody own,double reserve,Vector3 halfTranslation,Vector3 sourceTranslation,SceneBody other,double motion)
    {
        var qa=Quaternion.Normalize(own.Rotation);var qb=Quaternion.Normalize(other.Rotation);
        var center=own.Position+Vector3.Transform((own.Minimum+own.Maximum)/2,qa);
        var half=(own.Maximum-own.Minimum)/2;
        var aa=new[]{Vector3.Transform(Vector3.UnitX,qa),Vector3.Transform(Vector3.UnitY,qa),Vector3.Transform(Vector3.UnitZ,qa)};
        var ba=new[]{Vector3.Transform(Vector3.UnitX,qb),Vector3.Transform(Vector3.UnitY,qb),Vector3.Transform(Vector3.UnitZ,qb)};
        var axes=aa.Concat(ba).Concat(aa.SelectMany(a=>ba.Select(b=>Vector3.Cross(a,b))));
        var displacement=other.Center-center;
        foreach(var axis in axes) {
            double norm=Math.Sqrt(Dot(axis,axis));if(norm<1e-8)continue;
            // Keep the raw axis; a sphere's support is radius*|axis|. This
            // avoids a float-renormalization assumption on near-parallel axes.
            double ownSupport=Math.Abs(Dot(axis,aa[0]))*half.X+Math.Abs(Dot(axis,aa[1]))*half.Y+Math.Abs(Dot(axis,aa[2]))*half.Z+
                reserve*norm+Math.Abs(Dot(axis,halfTranslation))+Math.Abs(Dot(axis,sourceTranslation));
            double otherSupport=Math.Abs(Dot(axis,ba[0]))*other.HalfExtents.X+Math.Abs(Dot(axis,ba[1]))*other.HalfExtents.Y+Math.Abs(Dot(axis,ba[2]))*other.HalfExtents.Z+motion*norm;
            if(Math.Abs(Dot(displacement,axis))>ownSupport+otherSupport+.0001*norm)return true;
        }
        return false;
    }
    static double Dot(Vector3 a,Vector3 b)=>(double)a.X*b.X+(double)a.Y*b.Y+(double)a.Z*b.Z;
}
