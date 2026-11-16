using System.Numerics;
namespace MautAssistent;

public enum SceneMotionKind { Unknown,ActualPhysics,ObservedPoseEstimate }
public sealed record SceneMotionEstimate(SceneMotionKind Kind,double Reserve,double Speed,double AngularSpeed,
    bool Moving,bool SourceFresh,string Reason)
{
    public string SourceLabel=>Kind switch {SceneMotionKind.ActualPhysics=>"actualPhysics",SceneMotionKind.ObservedPoseEstimate=>"observedPoseEstimate",_=>"unknown"};
}

// A practical, explicitly parked-actor motion model, not an absolute acceleration
// law or an inference that missing physics means stationary. The caller still
// scopes each body's expanded geometry to its actual current driving corridor.
public sealed class SceneMotionPrediction
{
    public const string Assumption="Fresh parked actors: 0.35m/s^2 motion uncertainty, 0.15m pose reserve; " +
        "unknown initial motion additionally assumes 0.3m/s and 0.04rad/s over a maximum two-second reactive horizon. " +
        "Observed motion supersedes stationary assumptions. No unconditional future absence proof.";
    sealed record Track(SceneBody Body,int Samples,long Started,double Linear,double Angular,bool Moving,bool Jump);
    readonly Dictionary<ulong,Track> tracks=[];
    readonly HashSet<(uint Pid,ulong Session,ulong Generation)> retired=[];
    SceneSnapshot? current,last;
    bool currentAdvanced=true;
    public void Reset(){tracks.Clear();retired.Clear();current=null;last=null;currentAdvanced=true;}

    public bool Capture(SceneSnapshot frame,long now)
    {
        bool Fresh()=>frame!=null&&frame.UtcMs>=0&&(decimal)now-frame.UtcMs is >=-25 and <=200;
        if(frame==null||now<0||!Fresh()||frame.ProcessId==0||frame.Session==0||frame.Generation==0||frame.Sequence==0||(frame.Sequence&1)!=0||
            frame.SimulationTime==0||frame.PausedSimulationTime==0||frame.RenderTime==0||frame.Flags is not (58 or 59)||
            frame.Flags==59&&frame.Diagnostic!=0||frame.Flags==58&&frame.Diagnostic is <1 or >7||
            frame.Bodies.Count>512||frame.Bodies.Select(b=>b.Id).Distinct().Count()!=frame.Bodies.Count||frame.Bodies.Any(b=>!BodyValid(b)))return Invalidate();
        frame=frame with{Bodies=Array.AsReadOnly(frame.Bodies.ToArray()),Scopes=Array.AsReadOnly(frame.Scopes.ToArray())};
        var epoch=(frame.ProcessId,frame.Session,frame.Generation);
        if(retired.Contains(epoch))return Invalidate();
        // Sequence is monotone across world generations inside one native
        // process/session, not only while the generation happens to match.
        if(last!=null&&frame.ProcessId==last.ProcessId&&frame.Session==last.Session&&
            (frame.Sequence<last.Sequence||frame.UtcMs<last.UtcMs))return Invalidate();
        bool same=last!=null&&epoch==(last.ProcessId,last.Session,last.Generation);
        if(last!=null&&!same) {
            if(frame.ProcessId==last.ProcessId&&frame.Session==last.Session&&frame.Generation<last.Generation)return Invalidate();
            if(retired.Count>=64)return Invalidate();
            retired.Add((last.ProcessId,last.Session,last.Generation));tracks.Clear();
        }
        if(same) {
            if(frame.Sequence<last!.Sequence||frame.UtcMs<last.UtcMs||frame.SimulationTime<last.SimulationTime||
                frame.PausedSimulationTime<last.PausedSimulationTime||frame.RenderTime<last.RenderTime)return Invalidate();
            if(frame.Sequence==last.Sequence) {
                if(frame.UtcMs!=last.UtcMs||frame.SimulationTime!=last.SimulationTime||frame.PausedSimulationTime!=last.PausedSimulationTime||
                    frame.RenderTime!=last.RenderTime||frame.Flags!=last.Flags||frame.Diagnostic!=last.Diagnostic||frame.OwnPosition!=last.OwnPosition||
                    frame.BodyTotal!=last.BodyTotal||frame.ScopeTotal!=last.ScopeTotal||frame.OmittedMinSurfaceDistance!=last.OmittedMinSurfaceDistance||
                    !frame.Bodies.SequenceEqual(last.Bodies)||!frame.Scopes.SequenceEqual(last.Scopes))return Invalidate();
                current=last;return true; // the same frame never adds history or renews its deadline
            }
        }
        bool advance=!same||frame.SimulationTime>last!.SimulationTime&&frame.PausedSimulationTime>last.PausedSimulationTime&&frame.UtcMs>last.UtcMs;
        double dt=same?(frame.PausedSimulationTime-last!.PausedSimulationTime)/1e6:0;
        if(!advance||dt is <.005 or >.5)tracks.Clear();
        var present=frame.Bodies.Select(b=>b.Id).ToHashSet();
        foreach(var id in tracks.Keys.Where(id=>!present.Contains(id)).ToArray())tracks.Remove(id);
        foreach(var body in frame.Bodies) {
            Track next=new(body,1,frame.UtcMs,0,0,false,false);
            if(tracks.TryGetValue(body.Id,out var prior)&&SameShape(prior.Body,body)&&advance&&dt is >=.005 and <=.5) {
                double speed=Vector3.Distance(prior.Body.Center,body.Center)/dt;
                double angle=2*Math.Acos(Math.Clamp(Math.Abs(Quaternion.Dot(Quaternion.Normalize(prior.Body.Rotation),Quaternion.Normalize(body.Rotation))),0,1));
                double angular=angle/dt;bool jump=speed>50||Vector3.Distance(prior.Body.Center,body.Center)>10||angular>10;
                next=jump?new(body,1,frame.UtcMs,0,0,true,true):new(body,prior.Samples+1,prior.Started,speed,angular,speed>.15||angular>.05,false);
            }
            tracks[body.Id]=next;
        }
        current=frame;last=frame;currentAdvanced=advance;return true;
    }
    public SceneMotionEstimate Predict(SceneBody body,double horizonSeconds,long now)
    {
        SceneMotionEstimate Unknown(string reason,bool fresh=false,bool moving=false,double reserve=double.PositiveInfinity)=>new(SceneMotionKind.Unknown,reserve,0,0,moving,fresh,reason);
        if(current==null||now<0||(decimal)now-current.UtcMs is < -25 or >200)return Unknown("Motion source is stale or absent");
        if(!double.IsFinite(horizonSeconds)||horizonSeconds is <0 or >2)return Unknown("Reactive horizon exceeds the named parked-motion model");
        if(body==null||!tracks.TryGetValue(body.Id,out var track)||track.Body!=body)return Unknown("Actor identity/shape/pose is not bound to the captured source");
        double age=Math.Max(0,now-current.UtcMs)/1000d,t=age+horizonSeconds;
        bool physics=(body.Flags&2)!=0,moving=track.Moving||physics&&body.Speed>.15;
        SceneMotionKind kind=physics&&currentAdvanced?SceneMotionKind.ActualPhysics:
            currentAdvanced&&!track.Jump&&(track.Moving||track.Samples>=3&&current.UtcMs-track.Started>=150)?SceneMotionKind.ObservedPoseEstimate:SceneMotionKind.Unknown;
        // A discontinuous or newly appearing actor is not called stationary.
        // Its uncertainty is local geometry, never a global stop instruction.
        double linear=Math.Max(physics?body.Speed:0,track.Linear),angular=track.Angular;
        if(kind==SceneMotionKind.Unknown){linear=Math.Max(linear,.3);angular=Math.Max(angular,.04);}
        else {linear+=.05;angular+=.02;}
        if(track.Jump){linear=Math.Max(linear,2);angular=Math.Max(angular,.1);}
        double radius=body.HalfExtents.Length();
        double reserve=.15+linear*t+.5*.35*t*t+2*radius*Math.Sin(Math.Min(Math.PI,angular*t)/2);
        return new(kind,reserve,linear,angular,moving,true,kind switch {
            SceneMotionKind.ActualPhysics=>"Actual actor physics reading, with explicit parked-model uncertainty and observed-pose corroboration",
            SceneMotionKind.ObservedPoseEstimate=>"Clock-progressing same-actor pose estimate; not actual physics velocity",
            _=>track.Jump?"Pose discontinuity revoked motion history":!currentAdvanced?"Capture clock did not progress; no stationary history authority":"New or insufficiently observed actor; local parked-model uncertainty only"});
    }
    bool Invalidate(){current=null;tracks.Clear();return false;}
    static bool SameShape(SceneBody a,SceneBody b)=>a.GroupId==b.GroupId&&a.HalfExtents==b.HalfExtents&&(a.Flags&1)==(b.Flags&1);
    static bool BodyValid(SceneBody b)=>b!=null&&b.Id!=0&&b.GroupId!=0&&(b.Flags&~3U)==0&&float.IsFinite(b.Speed)&&b.Speed is >=0 and <=100&&
        ((b.Flags&2)!=0||b.Speed==0)&&GeometryValid(b);
    static bool GeometryValid(SceneBody b)=>SceneSnapshotProtocol.Finite(b.Center)&&SceneSnapshotProtocol.Finite(b.HalfExtents)&&
        b.HalfExtents.X is >0 and <=100&&b.HalfExtents.Y is >0 and <=100&&b.HalfExtents.Z is >0 and <=100&&
        float.IsFinite(b.Rotation.LengthSquared())&&Math.Abs(b.Rotation.LengthSquared()-1)<.001;
}
