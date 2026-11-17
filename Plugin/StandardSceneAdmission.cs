using System.Numerics;
using ETS2LA.Game.Data;
namespace MautAssistent;

// Nominal lane guidance only. This is deliberately NOT an articulated trailer,
// cargo-envelope or complete future swept-path certificate.
internal static class StandardSceneAdmission
{
    const string Scope="Standardspurführung; kein Gesamtgespann-/Ladungs-Freiraumnachweis";
    // Alias retained for existing callers; interpretation is shared with
    // forward curve planning, supervision and longitudinal control.
    internal const double StationarySignedSpeedTolerance=ForwardMotion.StationarySpeed;
    readonly record struct Segment(Vector3 A,Vector3 B,double From,double To);
    internal static ParkedSceneAdmission.Result Check(PrefabPath input,SceneSnapshot? scene,Vector3 own,RouteTrace road,
        double speed,ulong simulationTime,ulong renderTime,string itemIdentity,bool dynamicRequired,VehicleGeometry? rawShape,
        SceneMotionPrediction? motions=null)
    {
        var path=PrefabRefinement.OriginalFor(input);
        string owner=$"standard-scene:{path.Prefab.Prefab.Uid}:{path.StartNode.Uid}:{path.EndNode.Uid}";
        ParkedSceneAdmission.Result Clear(string why)=>new(true,why+" · "+Scope,0){HoldKey=owner};
        ParkedSceneAdmission.Result Wait(string why,double distance=0,bool used=false,double? acute=null)=>
            new(false,why+" · "+Scope,Math.Max(0,distance)){HoldKey=owner,UsesLiveSource=used,AcuteDistance=acute};
        if(!double.IsFinite(speed))return Wait("Geschwindigkeitsmessung ist nicht endlich");
        if(!ForwardMotion.TryNormalizeForwardSpeed(speed,out speed))return Wait("Rückwärtsfahrt außerhalb der vorwärts gerichteten Standardspurprüfung");
        if(!SceneSnapshotProtocol.Finite(own)||road==null||
            !road.TryItemRange(itemIdentity,out double from,out double to))
            return Wait("Bestätigter gerichteter Mautabschnitt fehlt");
        var current=BarrierBinding.UniqueCurrent(road,own,(from+to)/2);
        if(current==null)return Wait("Aktuelle Standardspur nicht eindeutig zugeordnet");
        double first=from-current.Along;
        double horizon=Math.Clamp(TrackingGuard.RequiredLookahead(speed)+20,35,300);
        if(to<current.Along-10)return Clear("Standard-Mautabschnitt hinter dem Lkw; keine Anhänger-Passageaussage");
        if(first>horizon)return Clear("Entfernte Maut außerhalb des aktuellen Standardkorridors");
        double waitDistance=Math.Max(0,first-ApproachStop.BufferMetres);
        double end=Math.Min(road.Length,current.Along+horizon);
        if(!road.TryStandardSlice(current.Along,end,.5,out var points,out var sliceIssue))
            return Wait("Begrenzter Standard-Routenverlauf fehlt: "+sliceIssue,waitDistance);
        var segments=new Segment[points.Count-1];
        var low=new Vector3(float.PositiveInfinity);var high=new Vector3(float.NegativeInfinity);
        for(int i=1;i<points.Count;i++) {
            segments[i-1]=new(points[i-1].Position,points[i].Position,points[i-1].Along,points[i].Along);
            low=Vector3.Min(low,Vector3.Min(points[i-1].Position,points[i].Position));
            high=Vector3.Max(high,Vector3.Max(points[i-1].Position,points[i].Position));
        }
        bool Trusted(long now) {
            if(scene==null||rawShape==null||scene.ProcessId==0||scene.ProcessId!=rawShape.ProcessId||scene.Session==0||
                scene.Generation==0||scene.Sequence==0||(scene.Sequence&1)!=0||rawShape.Session==0||rawShape.Sequence==0||
                (rawShape.Sequence&1)!=0||(decimal)now-scene.UtcMs is <-25 or >200||
                (decimal)now-rawShape.UtcMs is <-25 or >200||!ValidTruck(rawShape.Truck))return false;
            double rawAge=Math.Max(0,now-rawShape.UtcMs)/1000d;
            double skew=Math.Abs((double)scene.SimulationTime-simulationTime)/1e6;
            return Vector3.Distance(own,rawShape.Truck.Position)<=.5+speed*rawAge&&
                scene.Synchronized(own,simulationTime,renderTime,.5+speed*skew);
        }
        long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if(!Trusted(now))return dynamicRequired?Wait("Aktuelle rohe Truck-/Parkszenenquelle fehlt oder ist nicht zugeordnet",waitDistance):
            Clear("Nur statische Standardprüfung benötigt; keine Parkquellenfreigabe");
        // The sessions are independent producer epochs. Equality across the two
        // protocols is not required. Parent readers recheck both original epochs.
        motions??=new SceneMotionPrediction();
        if(!motions.Capture(scene!,now))return dynamicRequired?Wait("Parkszenenquelle ist nicht verwertbar",waitDistance):
            Clear("Optionale Parkquelle unverwertbar; keine Freigabe daraus");
        double age=Math.Max(0,now-scene!.UtcMs)/1000d;
        MeasuredTruckCorridor? acuteCorridor=null;
        if(Vector3.Distance(own,rawShape!.Truck.Position)<=.5&&
            Vector3.Dot(Vector3.Transform(-Vector3.UnitZ,Quaternion.Normalize(rawShape.Truck.Rotation)),road.DirectionAt(current.Along))>=.8)
            acuteCorridor=MeasuredTruckCorridor.Create(rawShape.Truck,road,own,speed);
        double? acute=null;
        foreach(var body in scene.Bodies) {
            var d=acuteCorridor?.Distance(body,age);
            if(d is double value)acute=acute is double old?Math.Min(old,value):value;
        }
        if(!Trusted(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()))return dynamicRequired?
            Wait("Ursprüngliche Quelle während der Prüfung veraltet",waitDistance,true):Clear("Optionale Quelle veraltet; kein akuter Nachweis");
        if(!dynamicRequired)return Clear("Nur statische Standardprüfung benötigt") with{UsesLiveSource=true,AcuteDistance=acute};
        // Coverage radius bounds only omitted-source relevance, never the
        // collision corridor. Include the bounded source-motion model (<=2s is
        // available; this Check uses only a .25s reactive horizon plus source age).
        double radius=points.Max(p=>Vector3.Distance(p.Position,own))+60;
        double poseSkew=Math.Abs((double)scene.SimulationTime-simulationTime)/1e6;
        if(!scene.Covers(path.Prefab.Prefab.Uid,own,radius,simulationTime,renderTime,.5+speed*poseSkew))
            return Wait("Geladener Standard-Mautbereich nicht vollständig erfasst",waitDistance,true,acute);
        double nearest=double.PositiveInfinity;ulong nearestId=0;
        foreach(var body in scene.Bodies) {
            var motion=motions.Predict(body,.25,now);
            if(!motion.SourceFresh||!double.IsFinite(motion.Reserve)||motion.Reserve>100)
                return Wait("Lokale Körperbewegung nicht begrenzt verfügbar",waitDistance,true,acute);
            var q=Quaternion.Normalize(body.Rotation);
            var extent=Vector3.Abs(Vector3.Transform(Vector3.UnitX,q))*body.HalfExtents.X+
                Vector3.Abs(Vector3.Transform(Vector3.UnitY,q))*body.HalfExtents.Y+
                Vector3.Abs(Vector3.Transform(Vector3.UnitZ,q))*body.HalfExtents.Z;
            float lateral=TollOptions.StandardLaneWidth/2+.2f+(float)motion.Reserve;
            if(body.Center.X+extent.X<low.X-lateral||body.Center.X-extent.X>high.X+lateral||
                body.Center.Z+extent.Z<low.Z-lateral||body.Center.Z-extent.Z>high.Z+lateral||
                body.Center.Y+extent.Y<low.Y-.2f-motion.Reserve||
                body.Center.Y-extent.Y>high.Y+TollOptions.StandardLaneHeight+.2f+motion.Reserve)continue;
            foreach(var segment in segments) {
                if(!Entry(segment,body,motion.Reserve,out double fraction))continue;
                double stop=segment.From+(segment.To-segment.From)*fraction-current.Along-3;
                if(stop<nearest){nearest=stop;nearestId=body.Id;}break;
            }
        }
        if(!Trusted(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()))return Wait("Ursprüngliche Quelle während der Prüfung veraltet",waitDistance,true);
        return double.IsFinite(nearest)?new(false,"Körper im nominalen Standardspur-Korridor · "+Scope,Math.Max(0,nearest)) {
            Kind=SceneCheckKind.ConservativeOccupancy,HoldKey=$"standard-body:{scene.ProcessId}:{scene.Session}:{nearestId}",
            UsesLiveSource=true,AcuteDistance=acute}:
            Clear("Örtliche Parkkörper außerhalb des nominalen Standardspur-Korridors") with{UsesLiveSource=true,AcuteDistance=acute};
    }
    static bool ValidTruck(VehicleBody body)=>body!=null&&SceneSnapshotProtocol.Finite(body.Position)&&
        SceneSnapshotProtocol.Finite(body.Minimum)&&SceneSnapshotProtocol.Finite(body.Maximum)&&
        body.Maximum.X>body.Minimum.X&&body.Maximum.Y>body.Minimum.Y&&body.Maximum.Z>body.Minimum.Z&&
        float.IsFinite(body.Rotation.LengthSquared())&&Math.Abs(body.Rotation.LengthSquared()-1)<.01;
    // Conservative slab intersection of a linear road segment against an OBB
    // expanded by the nominal vertical lane cylinder and LOCAL actor reserve.
    static bool Entry(Segment segment,SceneBody body,double motion,out double fraction)
    {
        fraction=0;var inverse=Quaternion.Inverse(body.Rotation);
        float height=TollOptions.StandardLaneHeight,width=TollOptions.StandardLaneWidth;
        var shift=new Vector3(0,height/2,0);
        var a=Vector3.Transform(segment.A+shift-body.Center,inverse);
        var b=Vector3.Transform(segment.B+shift-body.Center,inverse);
        var up=Vector3.Transform(Vector3.UnitY,inverse);
        float Support(float axis)=>width/2*MathF.Sqrt(MathF.Max(0,1-axis*axis))+height/2*MathF.Abs(axis)+.2f+(float)motion;
        var expanded=body.HalfExtents+new Vector3(Support(up.X),Support(up.Y),Support(up.Z));
        double enter=0,exit=1;
        bool Slab(double start,double finish,double half) {
            double delta=finish-start;
            if(Math.Abs(delta)<1e-10)return Math.Abs(start)<=half;
            double lo=(-half-start)/delta,hi=(half-start)/delta;if(lo>hi)(lo,hi)=(hi,lo);
            enter=Math.Max(enter,lo);exit=Math.Min(exit,hi);return enter<=exit;
        }
        if(!Slab(a.X,b.X,expanded.X)||!Slab(a.Y,b.Y,expanded.Y)||!Slab(a.Z,b.Z,expanded.Z))return false;
        fraction=Math.Clamp(enter,0,1);return true;
    }
}
