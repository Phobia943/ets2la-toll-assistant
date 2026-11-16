using System.Buffers.Binary;
using System.Numerics;
namespace MautAssistent;

public sealed record SceneBody(ulong Id,uint Flags,float Speed,Vector3 Center,Quaternion Rotation,Vector3 HalfExtents,ulong GroupId);
public sealed record SceneScope(ulong Uid,uint Flags);
// This source covers enumerated, currently loaded parked actors only. Active
// road users still belong to the host's independently refreshed ACC feed.
public sealed record SceneSnapshot(uint ProcessId,ulong Session,uint Sequence,long UtcMs,ulong SimulationTime,
    ulong PausedSimulationTime,ulong Generation,ulong RenderTime,Vector3 OwnPosition,uint Flags,
    uint BodyTotal,uint ScopeTotal,double OmittedMinSurfaceDistance,IReadOnlyList<SceneBody> Bodies,IReadOnlyList<SceneScope> Scopes)
{
    public uint Diagnostic {get;init;}
    public bool Synchronized(Vector3 ownPosition,ulong simulationTime,ulong renderTime,double poseTolerance=.75)=>
        (Flags&58)==58&&(Flags&~59U)==0&&double.IsFinite(poseTolerance)&&poseTolerance is >=0 and <=10&&
        SceneSnapshotProtocol.Finite(ownPosition)&&Vector3.Distance(ownPosition,OwnPosition)<=poseTolerance&&
        Difference(simulationTime,SimulationTime)<=250000&&Difference(renderTime,RenderTime)<=250000;
    public bool Covers(ulong wantedUid,Vector3 ownPosition,double radius,ulong simulationTime,ulong renderTime,double poseTolerance=.75)
    {
        if(Flags!=59||Diagnostic!=0||wantedUid==0||!double.IsFinite(radius)||radius<0||radius>1000||
            !Synchronized(ownPosition,simulationTime,renderTime,poseTolerance)||
            !Scopes.Any(s=>s.Uid==wantedUid&&s.Flags==3))return false;
        return BodyTotal==Bodies.Count||OmittedMinSurfaceDistance>radius+Vector3.Distance(ownPosition,OwnPosition);
    }
    static ulong Difference(ulong a,ulong b)=>a>b?a-b:b-a;
}
// A newly read copy does not renew producer freshness or simulation progress.
public sealed class SceneTimeline
{
    SceneSnapshot? prior;long progressedAt;
    readonly HashSet<(uint Pid,ulong Session)> retired=[];
    public void Reset(){prior=null;progressedAt=0;retired.Clear();}
    public bool Accept(SceneSnapshot frame,long now)
    {
        if(now<0||(decimal)now-frame.UtcMs is < -25 or >200)return false;
        if(retired.Contains((frame.ProcessId,frame.Session)))return false;
        if(prior!=null&&frame.ProcessId==prior.ProcessId&&frame.Session==prior.Session) {
            if(frame.Sequence<prior.Sequence)return false;
            if(frame.Sequence==prior.Sequence&&
                (frame.UtcMs!=prior.UtcMs||frame.Generation!=prior.Generation||frame.SimulationTime!=prior.SimulationTime||
                frame.PausedSimulationTime!=prior.PausedSimulationTime||frame.RenderTime!=prior.RenderTime||frame.OwnPosition!=prior.OwnPosition||
                frame.Flags!=prior.Flags||frame.Diagnostic!=prior.Diagnostic||frame.BodyTotal!=prior.BodyTotal||frame.ScopeTotal!=prior.ScopeTotal||
                frame.OmittedMinSurfaceDistance!=prior.OmittedMinSurfaceDistance||!frame.Bodies.SequenceEqual(prior.Bodies)||!frame.Scopes.SequenceEqual(prior.Scopes)))return false;
            if(frame.Generation<prior.Generation)return false;
            if(frame.Generation==prior.Generation) {
                if(frame.Sequence<prior.Sequence||frame.UtcMs<prior.UtcMs||frame.PausedSimulationTime<prior.PausedSimulationTime||
                    frame.SimulationTime<prior.SimulationTime||frame.RenderTime<prior.RenderTime)return false;
                if(frame.PausedSimulationTime!=prior.PausedSimulationTime)progressedAt=now;
                if((decimal)now-progressedAt>200)return false;
            } else progressedAt=now;
        } else {
            if(prior!=null){if(retired.Count>=64)return false;retired.Add((prior.ProcessId,prior.Session));}
            progressedAt=now;
        }
        prior=frame;return true;
    }
}
public static class SceneSnapshotProtocol
{
    public const int Size=41120,HeaderSize=160,BodySize=64,ScopeSize=16,Capacity=512;
    public const string BuildId="C65EFA730837A8C05FE382C330C2F4DFFEEEC69FBDA81BB0F89072C2C4ABA6D5";
    public static SceneSnapshot? Decode(ReadOnlySpan<byte> d,long nowMs,uint expectedPid,bool allowIncompleteForSafety=false)
    {
        if(d.Length!=Size||U32(d,0)!=0x3143534d||U32(d,4)!=1||U32(d,8)!=Size)return null;
        uint seq=U32(d,12),pid=U32(d,24),flags=U32(d,28),bc=U32(d,80),bt=U32(d,84),sc=U32(d,88),st=U32(d,92),diagnostic=U32(d,104);
        long utc=BinaryPrimitives.ReadInt64LittleEndian(d[16..]);decimal age=(decimal)nowMs-utc;
        ulong session=U64(d,32),generation=U64(d,144),sim=U64(d,40),pausedSim=U64(d,48),render=U64(d,152);
        // Complete + started + pose + clocks + explicitly parked-only. Paused,
        // unknown flags, diagnostics and incomplete snapshots never mean empty.
        bool validAuthority=flags==59&&diagnostic==0;
        bool partialSafety=allowIncompleteForSafety&&flags==58&&diagnostic is >=1 and <=7;
        if(seq==0||(seq&1)!=0||pid==0||pid!=expectedPid||session==0||generation==0||!validAuthority&&!partialSafety||utc<0||nowMs<0||age is < -25 or >200||
            bc>Capacity||sc>Capacity||bc>bt||sc>st||st>100000||bt>100000||U32(d,108)!=0||
            Convert.ToHexString(d.Slice(112,32))!=BuildId)return null;
        var own=new Vector3((float)D64(d,56),(float)D64(d,64),(float)D64(d,72));
        double omitted=D64(d,96);
        if(!Finite(own)||own.Length()>1e7||double.IsNaN(omitted)||omitted<0||
            (bt==bc?!double.IsPositiveInfinity(omitted):!double.IsFinite(omitted)))return null;
        var bodies=new SceneBody[bc];var ids=new HashSet<ulong>();
        for(int i=0;i<bc;i++) {
            var b=d.Slice(HeaderSize+i*BodySize,BodySize);ulong id=U64(b,0),group=U64(b,56);uint bf=U32(b,8);float speed=F32(b,12);
            var center=V3(b,16);var q=new Quaternion(F32(b,28),F32(b,32),F32(b,36),F32(b,40));var half=V3(b,44);
            if(id==0||group==0||!ids.Add(id)||(bf&~3U)!=0||!float.IsFinite(speed)||speed is <0 or >100||
                (bf&2)==0&&speed!=0||!Finite(center)||center.Length()>1e7||!Finite(half)||
                half.X is <=0 or >100||half.Y is <=0 or >100||half.Z is <=0 or >100||
                !float.IsFinite(q.LengthSquared())||Math.Abs(q.LengthSquared()-1)>.001)return null;
            bodies[i]=new(id,bf,speed,center,Quaternion.Normalize(q),half,group);
        }
        var scopes=new SceneScope[sc];ids.Clear();
        for(int i=0;i<sc;i++) {
            var s=d.Slice(HeaderSize+Capacity*BodySize+i*ScopeSize,ScopeSize);ulong uid=U64(s,0);uint sf=U32(s,8);
            if(uid==0||!ids.Add(uid)||(sf&~3U)!=0||U32(s,12)!=0)return null;
            scopes[i]=new(uid,sf);
        }
        return new(pid,session,seq,utc,sim,pausedSim,generation,render,own,flags,bt,st,omitted,Array.AsReadOnly(bodies),Array.AsReadOnly(scopes)){Diagnostic=diagnostic};
    }
    internal static bool Finite(Vector3 p)=>float.IsFinite(p.X)&&float.IsFinite(p.Y)&&float.IsFinite(p.Z);
    static uint U32(ReadOnlySpan<byte>d,int p)=>BinaryPrimitives.ReadUInt32LittleEndian(d[p..]);
    static ulong U64(ReadOnlySpan<byte>d,int p)=>BinaryPrimitives.ReadUInt64LittleEndian(d[p..]);
    static float F32(ReadOnlySpan<byte>d,int p)=>BinaryPrimitives.ReadSingleLittleEndian(d[p..]);
    static double D64(ReadOnlySpan<byte>d,int p)=>BinaryPrimitives.ReadDoubleLittleEndian(d[p..]);
    static Vector3 V3(ReadOnlySpan<byte>d,int p)=>new(F32(d,p),F32(d,p+4),F32(d,p+8));
}
