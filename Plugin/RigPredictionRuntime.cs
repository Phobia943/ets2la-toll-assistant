using System.Numerics;
namespace MautAssistent;

internal enum RigRuntimeIssue { None,MissingEvidence,SpeedOutsideModelScope,UnsupportedRoute,IncoherentPose,UnsupportedModel }
internal sealed record RigRuntimePrediction(RigPredictionResult? Prediction,WheelAxleProof? Axle,
    double StartAlong,double RequiredAhead,string? Issue,RigRuntimeIssue IssueKind)
{
    internal bool Supported=>IssueKind==RigRuntimeIssue.None&&Prediction is{Supported:true};
    internal bool SpeedOutsideModelScope=>IssueKind==RigRuntimeIssue.SpeedOutsideModelScope;
    internal double ModelSpeedMps=>20d/3.6;
    internal RigPredictionInput? Input {get;init;}
    internal VehicleGeometry? Shape {get;init;}
    internal RouteTrace? Trace {get;init;}
    internal string? ContextIdentity {get;init;}
    internal long ContextUtcMs {get;init;}
    internal float PlanningHeight {get;init;}
    internal float PlanningWidth {get;init;}
    internal Vector3 AnchorShift {get;init;}
    internal double AnchorHeadingResidual {get;init;}
    internal string ConfigurationScope=>"SCS primary chain_type=single, one observed attached trailer and matched native presence; unobserved slots are not proven absent";
}
internal static class RigPredictionRuntime
{
    internal static RigRuntimePrediction Build(RouteTrace? finalTrace,double provenCurrentAlong,PredictionEnvelope? envelope,
        EnvelopeContext? context,Vector3 telemetryPosition,double heading,double speed,long now,double requiredAhead,
        double trackingError=.15,bool plazaScope=true)
    {
        RigRuntimePrediction Reject(string why,RigRuntimeIssue kind,double horizon=0,WheelAxleProof? axle=null)=>
            new(null,axle,provenCurrentAlong,horizon,why,kind);
        if(!plazaScope||finalTrace==null||envelope==null||context==null||context.Special||context.Trailers.Length!=1||
            envelope.ContextIdentity!=context.Identity||envelope.ContextUtcMs!=context.UtcMs||!envelope.Shape.TrailerKnown||
            envelope.Shape.Trailer is not{} trailer||context.TruckHook is not{} truckHook||context.Trailers[0].Hook is not{} trailerHook||
            context.Trailers[0].RotationTurns is not{} trailerTurns||!SceneSnapshotProtocol.Finite(trailerTurns)||
            context.Trailers[0].Chain!="single"||!SceneSnapshotProtocol.Finite(telemetryPosition)||
            !double.IsFinite(speed)||speed<0||!double.IsFinite(heading)||!double.IsFinite(trackingError)||trackingError is <.05 or >.5||
            !double.IsFinite(requiredAhead)||requiredAhead<0||requiredAhead>300)
            return Reject("Aktuelle einzelne Gespann-/Ladungs-/Kupplungsmessung oder begrenzter Mautbereich fehlt",RigRuntimeIssue.MissingEvidence);
        var shape=envelope.Shape;
        if(shape.ProcessId==0||shape.Session==0||shape.Sequence==0||now-shape.UtcMs is <0 or >150||now-context.UtcMs is <0 or >150||
            !VehicleEnvelopeEvidence.Coherent(context,shape,telemetryPosition,speed,heading,now))
            return Reject("Native und SCS-Messung sind nicht frisch derselben Fahrzeugbeobachtung zugeordnet",RigRuntimeIssue.IncoherentPose);
        double skew=Math.Abs((double)shape.UtcMs-context.UtcMs)/1000;
        var truckQ=Quaternion.Normalize(shape.Truck.Rotation);
        var truckForward=Vector3.Transform(-Vector3.UnitZ,truckQ);truckForward.Y=0;
        double truckYaw=ArticulatedRigPrediction.Yaw(truckQ);
        double dot=Vector3.Dot(Vector3.Normalize(truckForward),TrackingGuard.TruckForward(heading));
        var trailerForward=Vector3.Transform(-Vector3.UnitZ,Quaternion.Normalize(trailer.Rotation));trailerForward.Y=0;
        double trailerDot=Vector3.Dot(Vector3.Normalize(trailerForward),TrackingGuard.TruckForward(trailerTurns.X));
        double measuredGap=Math.Max(Vector3.Distance(shape.Truck.Position,telemetryPosition),
            Vector3.Distance(trailer.Position,context.Trailers[0].Position));
        if(!double.IsFinite(dot)||!double.IsFinite(trailerDot)||!double.IsFinite(measuredGap)||skew>.03||
            dot<Math.Cos(.01)||trailerDot<Math.Cos(.01)||measuredGap>.15+speed*skew)
            return Reject("Quellpose/-heading oder Messzeitversatz liegt außerhalb der expliziten Vorhersagereserve",RigRuntimeIssue.IncoherentPose);
        var axle=WheelAxle.Trailer(trailerHook,context.Trailers[0].Wheels);
        if(!axle.Supported)return Reject(axle.Issue!,RigRuntimeIssue.UnsupportedModel,axle:axle);
        // Tempo is its own limitation, not a zero-distance missing-data stop.
        if(speed>20d/3.6)return Reject("Vorhersage nur im langsamen Mautbereich bis 20 km/h",RigRuntimeIssue.SpeedOutsideModelScope,axle:axle);
        double rear=trailer.Corners().Concat(shape.Truck.Corners()).Max(p=>Math.Max(0,-Vector3.Dot(p-shape.Truck.Position,truckForward)));
        double minimum=speed+speed*speed/(2*1.2)+3+rear;
        double horizon=Math.Max(requiredAhead,minimum);
        if(horizon>300)return Reject("Vorhersagehorizont überschreitet das begrenzte Mautmodell",RigRuntimeIssue.UnsupportedRoute,horizon,axle);
        if(!finalTrace.TrySlice(provenCurrentAlong,provenCurrentAlong+horizon,.25,out var samples,out var issue))
            return Reject(issue??"Nachgewiesene Route deckt Bremsweg und Gespannheck nicht ab",RigRuntimeIssue.UnsupportedRoute,horizon,axle);
        var first=samples[0];var routeForward=first.Direction;routeForward.Y=0;
        if(Vector3.Distance(shape.Truck.Position,first.Position)>1.5||
            Vector3.Dot(Vector3.Normalize(routeForward),truckForward)<.98)
            return Reject("Aktuelle Wurzelpose ist nicht eng an den bewiesenen gerichteten Trackinganker gebunden",RigRuntimeIssue.IncoherentPose,horizon,axle);
        var rootShift=shape.Truck.Position-first.Position;
        double firstRouteYaw=Math.Atan2(-first.Direction.X,-first.Direction.Z);
        double headingResidual=Math.Atan2(Math.Sin(truckYaw-firstRouteYaw),Math.Cos(truckYaw-firstRouteYaw));
        var path=new List<HitchPathPoint>();double previous=truckYaw,maxDelta=0;
        for(int i=0;i<samples.Count;i++) {
            var point=samples[i];
            // Keep the measured heading at entry. Its small route residual
            // decays continuously over 2m in this explicit monitored model;
            // never rotate the first measured body instantly onto the route.
            double yaw=i==0?truckYaw:Math.Atan2(-point.Direction.X,-point.Direction.Z)+
                headingResidual*Math.Max(0,1-(point.Along-provenCurrentAlong)/2);
            yaw=previous+Math.Atan2(Math.Sin(yaw-previous),Math.Cos(yaw-previous));
            if(i>0)maxDelta=Math.Max(maxDelta,Math.Abs(yaw-previous));
            if(i>0&&Math.Abs(yaw-previous)>.15)return Reject("Richtungswechsel überschreitet die begrenzte Hakenpfadabtastung",RigRuntimeIssue.UnsupportedRoute,horizon,axle);
            var q=Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.UnitY,(float)(yaw-truckYaw))*truckQ);
            path.Add(new(point.Position+rootShift+Vector3.Transform(truckHook,q),yaw,point.Along));previous=yaw;
        }
        // Root motion is exactly piecewise linear on the supplied trace. The
        // rotated local hook follows a circular arc between linear yaw nodes;
        // its chord deviation is bounded, rather than declared zero.
        double chord=truckHook.Length()*(1-Math.Cos(maxDelta/2));
        // Translation between the two observed capture phases is directional:
        // an ordinary forward 16ms phase must not invent lateral truck width.
        // Full ± measured vectors are retained independently for each body.
        var truckSourceTranslation=telemetryPosition-shape.Truck.Position;
        var trailerSourceTranslation=context.Trailers[0].Position-trailer.Position;
        double correlationReserve=2*Math.Max(shape.Truck.Corners().Max(p=>Vector3.Distance(p,shape.Truck.Position)),
                trailer.Corners().Max(p=>Vector3.Distance(p,trailer.Position)))*
                Math.Sin(Math.Max(Math.Acos(Math.Clamp(dot,-1,1)),Math.Acos(Math.Clamp(trailerDot,-1,1)))/2);
        double fullTracking=trackingError+correlationReserve;
        if(fullTracking>1)return Reject("Gemessene Quellabweichung überschreitet die Modellreserve",RigRuntimeIssue.IncoherentPose,horizon,axle);
        var input=new RigPredictionInput(shape.Truck,trailer,truckHook,trailerHook,axle.Axle,false,false,
            path.AsReadOnly(),chord,fullTracking,.25) {
                TruckSourceTranslation=truckSourceTranslation,TrailerSourceTranslation=trailerSourceTranslation};
        var prediction=ArticulatedRigPrediction.Predict(input);
        return new(prediction,axle,provenCurrentAlong,horizon,prediction.Issue,
            prediction.Supported?RigRuntimeIssue.None:RigRuntimeIssue.UnsupportedModel) {
            Input=input,Shape=shape,Trace=finalTrace,ContextIdentity=context.Identity,ContextUtcMs=context.UtcMs,
            PlanningHeight=envelope.PlanningHeight,PlanningWidth=envelope.PlanningWidth,
            AnchorShift=rootShift,AnchorHeadingResidual=headingResidual};
    }
}
