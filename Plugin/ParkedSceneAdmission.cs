using System.Numerics;
using System.Reflection;
using ETS2LA.Game.Data;
namespace MautAssistent;

// Recomputed synchronously from the latest producer frame at every output
// evaluation. Only immutable geometry may be reused; no clear-scene lease is
// cached over a source epoch, a newly appearing body or a driver cancellation.
internal static class ParkedSceneAdmission
{
    internal sealed record Result(bool Clear,string Reason,double StopDistance)
    {
        internal SceneCheckKind Kind {get;init;}=Clear?SceneCheckKind.Clear:SceneCheckKind.UnknownEvidence;
        internal double? AcuteDistance {get;init;}
        internal string HoldKey {get;init;}="scene-unresolved";
        internal float? AccelerationLimit {get;init;}
        internal bool RequiresPhysicalHold {get;init;}=true;
    }
    internal static Result CheckRig(PrefabPath input,SceneSnapshot? scene,Vector3 own,RouteTrace road,double speed,
        ulong simulationTime,ulong renderTime,RigRuntimePrediction runtime,SceneMotionPrediction motions,string itemIdentity,
        bool dynamicRequired,float height,float width,float frontReach,VehicleGeometry? measuredRig=null,MeasuredTruckCorridor? acuteCorridor=null)
    {
        var path=PrefabRefinement.OriginalFor(input);
        string owner=$"scene-evidence:{path.Prefab.Prefab.Uid}:{path.StartNode.Uid}:{path.EndNode.Uid}";
        if(!road.TryItemRange(itemIdentity,out double start,out double end))return OutsidePlanningHorizon(path,own,speed)?
            new(true,"Entfernte Maut außerhalb des aktuellen Bremskorridors",0):new(false,"Mautabschnitt nicht im bestätigten Verlauf",0){HoldKey=owner};
        var current=BarrierBinding.UniqueCurrent(road,own,(start+end)/2);
        if(current==null)return new(false,"Mautposition nicht eindeutig",0){HoldKey=owner};
        double first=start-current.Along;
        double rear=runtime.Shape?.Trailer?.Corners().Max(p=>Vector3.Distance(p,runtime.Shape.Truck.Position))??30;
        if(end<current.Along-rear-3)return new(true,"Maut einschließlich Auflieger passiert",0);
        if(first>Math.Max(100,speed*speed/2+Math.Max(0,speed)*1.5+90))return new(true,"Statische Vorausplanung außerhalb der Nahprüfung",0);
        Result Wait(string reason)=>new(false,reason,Math.Max(0,first-ApproachStop.BufferMetres)){HoldKey=owner};
        long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        bool Fresh()=>scene!=null&&(decimal)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()-scene.UtcMs is >=-25 and <=200;
        double skew=scene==null?0:Math.Abs((double)scene.SimulationTime-simulationTime)/1e6;
        bool synchronized=Fresh()&&scene!.Synchronized(own,simulationTime,renderTime,.5+Math.Abs(speed)*skew);
        double? acute=null;
        if(synchronized) {
            double age=Math.Max(0,now-scene!.UtcMs)/1000d;
            var corridor=acuteCorridor??(measuredRig==null?null:MeasuredTruckCorridor.Create(measuredRig.Truck,road,own,speed));
            foreach(var body in scene.Bodies) {
                var distance=corridor?.Distance(body,age);
                if(distance is double d)acute=acute is double previous?Math.Min(previous,d):d;
            }
        }
        if(runtime.SpeedOutsideModelScope) {
            double usable=Math.Max(0,first-35-speed*.5);
            double cap=Math.Sqrt(Math.Pow(runtime.ModelSpeedMps,2)+2*1.2*usable);
            return new(false,"Normale Anfahrt auf den 20-km/h-Vorhersagebereich",Math.Max(0,first)) {
                HoldKey="model-speed:"+owner,AccelerationLimit=(float)Math.Clamp((cap-speed)*.8,-2,.35),
                RequiresPhysicalHold=false,AcuteDistance=acute};
        }
        if(!runtime.Supported||runtime.Prediction==null)return Wait(runtime.Issue??"Gespannvorhersage noch nicht verfügbar") with{AcuteDistance=acute};
        if(!dynamicRequired)return new(true,"Statische Gespannprüfung erforderlich; keine Parkquelle für diese Anlage",0){AcuteDistance=acute};
        if(!synchronized)return Wait("Aktuelle örtliche Parkszene fehlt oder passt nicht zur Spielpose");
        var prediction=runtime.Prediction;
        var bounds=Bounds(prediction);
        double radius=Vector3.Max(Vector3.Abs(own-bounds.Min),Vector3.Abs(own-bounds.Max)).Length()+5;
        if(!scene!.Covers(path.Prefab.Prefab.Uid,own,radius,simulationTime,renderTime,.5+Math.Abs(speed)*skew))
            return Wait("Örtlicher geladener Mautbereich nicht vollständig erfasst") with{AcuteDistance=acute};
        double nearest=double.PositiveInfinity;ulong nearestId=0;
        foreach(var body in scene.Bodies) {
            var motion=motions.Predict(body,.25,now);
            if(!motion.SourceFresh||!double.IsFinite(motion.Reserve))return Wait("Bewegungsquelle der örtlichen Parkszene nicht frisch") with{AcuteDistance=acute};
            var q=Quaternion.Normalize(body.Rotation);
            var extent=Vector3.Abs(Vector3.Transform(Vector3.UnitX,q))*body.HalfExtents.X+
                Vector3.Abs(Vector3.Transform(Vector3.UnitY,q))*body.HalfExtents.Y+
                Vector3.Abs(Vector3.Transform(Vector3.UnitZ,q))*body.HalfExtents.Z+new Vector3((float)motion.Reserve);
            if(body.Center.X+extent.X<bounds.Min.X||body.Center.X-extent.X>bounds.Max.X||
                body.Center.Y+extent.Y<bounds.Min.Y||body.Center.Y-extent.Y>bounds.Max.Y||
                body.Center.Z+extent.Z<bounds.Min.Z||body.Center.Z-extent.Z>bounds.Max.Z)continue;
            foreach(var cell in prediction.Cells) {
                if(ArticulatedRigPrediction.Outside(cell,body,motion.Reserve))continue;
                double stop=cell.AlongStart-current.Along-3;
                if(stop<nearest){nearest=stop;nearestId=body.Id;}break;
            }
        }
        if(!Fresh())return Wait("Parkszene während der Prüfung veraltet");
        return double.IsFinite(nearest)?new(false,"Parkkörper im modellierten Gespannweg",Math.Max(0,nearest)) {
            Kind=SceneCheckKind.ConservativeOccupancy,HoldKey=$"scene-body:{scene.ProcessId}:{scene.Session}:{nearestId}",AcuteDistance=acute}:
            new(true,"Örtliche Parkkörper außerhalb des modellierten Gespannwegs",0){AcuteDistance=acute};
    }
    static (Vector3 Min,Vector3 Max) Bounds(RigPredictionResult prediction)
    {
        var min=new Vector3(float.PositiveInfinity);var max=new Vector3(float.NegativeInfinity);
        foreach(var cell in prediction.Cells)foreach(var pair in new[]{(cell.Truck,cell.TruckMotionReserve,cell.TruckSourceTranslation),(cell.Trailer,cell.TrailerMotionReserve,cell.TrailerSourceTranslation)}) {
            var (body,reserve,source)=pair;var span=Vector3.Abs(cell.HitchTranslationHalf)+Vector3.Abs(source)+new Vector3((float)reserve+.01f);
            foreach(var point in body.Corners()){min=Vector3.Min(min,point-span);max=Vector3.Max(max,point+span);}
        }
        return(min,max);
    }
    // Historical offline source-contract probe. Production output uses CheckRig
    // above; this compatibility entry is never a runtime clearance authority.
    static readonly MethodInfo clone=typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic)!;
    internal static Result Check(PrefabPath path,SceneSnapshot? scene,Vector3 own,RouteTrace road,float rigRadius,
        double speed,ulong simulationTime,ulong renderTime,float height=float.NaN,float width=float.NaN,float frontReach=float.NaN,string? itemIdentity=null)
    {
        TraceProjection current;
        var original=PrefabRefinement.OriginalFor(path);
        var curves=new List<SceneCurve>();
        double first=double.PositiveInfinity;
        double rangeStart=0,rangeEnd=road.Length;
        if(itemIdentity!=null&&!road.TryItemRange(itemIdentity,out rangeStart,out rangeEnd))
            return OutsidePlanningHorizon(original,own,speed)?
                new(true,"Ferne Mautgeometrie außerhalb des aktuellen Bremskorridors; keine Szenenfreigabe",0):
                new(false,"Mautabschnitt nicht eindeutig im bestätigten Straßenverlauf zugeordnet",0);
        var unique=BarrierBinding.UniqueCurrent(road,own,(rangeStart+rangeEnd)/2);
        if(unique==null)return new(false,"Aktuelle Straßenposition vor der Mautstelle nicht eindeutig zugeordnet",0)
            {HoldKey=$"scene-evidence:{path.Prefab.Prefab.Uid}:{path.StartNode.Uid}:{path.EndNode.Uid}"};
        current=unique;
        if(itemIdentity!=null) {
            if(rangeEnd<current.Along-60)return new(true,"Mautabschnitt einschließlich Gespann hinter dem Lkw",0);
            first=rangeStart-current.Along;
        }
        TraceProjection Local(Vector3 point)=>road.ProjectNearForTracking(point,(rangeStart+rangeEnd)/2,Math.Max(.001,(rangeEnd-rangeStart)/2));
        foreach(var curve in original.Curves) {
            var single=(PrefabPath)clone.Invoke(original,null)!;single.Curves=[curve];single.Length=curve.Length;
            Vector3 At(float f)=>single.Interpolate(f,0)?.Position??throw new InvalidDataException("Mautkurve ohne Weltkoordinaten.");
            var hull=CubicSeparation.FromCubicSamples(At(0),At(1f/3),At(2f/3),At(1));
            var controls=new[]{hull.A,hull.B,hull.C,hull.D};
            if(controls.Any(p=>!SceneSnapshotProtocol.Finite(p)))throw new InvalidDataException("Ungültige Mautkurve.");
            // A complete curve behind the entire rig cannot authorize or block
            // its future passage. Keep close rear geometry for the trailer.
            if(itemIdentity==null&&controls.All(p=>road.Project(p).Along<current.Along-60))continue; // Offline legacy fixture only.
            if(itemIdentity==null)first=Math.Min(first,road.Project(hull.A).Along-current.Along);
            curves.Add(new(hull.A,hull.B,hull.C,hull.D));
        }
        if(curves.Count==0)return new(true,"Anlage vollständig hinter dem Gespann",0);
        double horizon=Math.Max(100,speed*speed/2+Math.Max(0,speed)*1.5+90);
        if(first>horizon)return new(true,"Nur statische Vorausplanung; Szene wird bei Annäherung erneut geprüft",0);
        double unknownStop=Math.Max(0,first-ApproachStop.BufferMetres);
        Result Unknown(string reason)=>new(false,reason,unknownStop){HoldKey=$"scene-evidence:{path.Prefab.Prefab.Uid}:{path.StartNode.Uid}:{path.EndNode.Uid}"};
        if(scene==null)return Unknown("Aktuelle Parkfahrzeug-Erfassung fehlt");
        bool Fresh()=>DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()-scene.UtcMs is >= -25 and <=200;
        if(!Fresh())return Unknown("Parkfahrzeug-Erfassung während der Prüfung veraltet");
        double skew=Math.Abs((double)scene.SimulationTime-simulationTime)/1e6;
        double tolerance=.5+Math.Abs(speed)*skew;
        if(!scene.Synchronized(own,simulationTime,renderTime,tolerance))return Unknown("Mautszene nicht mit der Spielpose synchron");
        double radius=curves.SelectMany(c=>new[]{c.A,c.B,c.C,c.D}).Max(p=>Vector3.Distance(p,own))+rigRadius+25;
        string? unknown=!float.IsFinite(rigRadius)?"Gespann-Kupplungs-/Ladungshülle noch nicht nachgewiesen":
            !scene.Covers(path.Prefab.Prefab.Uid,own,radius,simulationTime,renderTime,tolerance)?"Lokale Mautszene nicht vollständig erfasst (Diagnose "+scene.Diagnostic+")":null;
        double age=Math.Max(0,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()-scene.UtcMs)/1000d;
        double nearest=double.PositiveInfinity;
        ulong nearestBody=0;
        double? acute=null;
        foreach(var body in scene.Bodies) {
            // No motion claim is inferred from the parked actor class itself.
            var immediate=SceneAcuteHazard.Distance(body,curves,height,width,frontReach,road,own,age,
                itemIdentity!=null?(rangeStart,rangeEnd):null,current.Along);
            if(immediate is double candidate)acute=acute is double previous?Math.Min(previous,candidate):candidate;
            if((body.Flags&2)==0){unknown??="Bewegung eines erfassten Parkfahrzeugs unbekannt";continue;}
            if(!float.IsFinite(rigRadius))continue;
            float motion=(float)(body.Speed*(age+.25)+.5*50*Math.Pow(age+.25,2)+.2);
            if(curves.All(c=>SceneClearance.OutsideRig(c,body,rigRadius,motion)))continue;
            double bodyStop=Local(body.Center).Along-current.Along-body.HalfExtents.Length()-rigRadius-motion-3;
            if(bodyStop<nearest){nearest=bodyStop;nearestBody=body.Id;}
        }
        if(!Fresh())return Unknown("Parkfahrzeug-Erfassung während der Prüfung veraltet");
        if(unknown!=null)return Unknown(unknown) with{AcuteDistance=acute};
        return double.IsFinite(nearest)?new(false,"Parkfahrzeug im konservativen Gespann-Bewegungsbereich",Math.Max(0,nearest))
            {Kind=SceneCheckKind.ConservativeOccupancy,AcuteDistance=acute,HoldKey=$"scene-body:{scene.ProcessId}:{scene.Session}:{nearestBody}"}:
            new(true,"Aktuell erfasste Parkfahrzeuge außerhalb des Gespann-Bewegungsbereichs",0);
    }
    static bool OutsidePlanningHorizon(PrefabPath original,Vector3 own,double speed)
    {
        if(!SceneSnapshotProtocol.Finite(own)||!double.IsFinite(speed)||original.Curves.Count is <1 or >256)return false;
        // Every cubic is contained in its control hull/AABB. Distance to that
        // box is a LOWER bound, unlike minimum distance to sampled points.
        // Include the maximum supported rig reach while its proof is unknown.
        double horizon=Math.Max(100,speed*speed/2+Math.Max(0,speed)*1.5+90)+60;
        foreach(var curve in original.Curves) {
            var single=(PrefabPath)clone.Invoke(original,null)!;single.Curves=[curve];single.Length=curve.Length;
            Vector3? At(float f)=>single.Interpolate(f,0)?.Position;
            var a=At(0);var b=At(1f/3);var c=At(2f/3);var d=At(1);
            if(a==null||b==null||c==null||d==null)return false;
            var hull=CubicSeparation.FromCubicSamples(a.Value,b.Value,c.Value,d.Value);
            var values=new[]{hull.A,hull.B,hull.C,hull.D};if(values.Any(p=>!SceneSnapshotProtocol.Finite(p)))return false;
            var low=Vector3.Min(Vector3.Min(hull.A,hull.B),Vector3.Min(hull.C,hull.D));
            var high=Vector3.Max(Vector3.Max(hull.A,hull.B),Vector3.Max(hull.C,hull.D));
            var delta=Vector3.Max(low-own,Vector3.Max(Vector3.Zero,own-high));
            if(delta.Length()<=horizon)return false;
        }
        return true;
    }
}
