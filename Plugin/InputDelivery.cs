using System.Numerics;
using ETS2LA.Game.SDK;
using ETS2LA.Game.Telemetry;
using Semaphore=ETS2LA.Game.SDK.Semaphore;

namespace MautAssistent;

public sealed partial class TollPlugin
{
    // SDK publishers are synchronous. Never make them wait for route/model work:
    // that would also prevent telemetry delivery to the host's other consumers.
    sealed record TelemetryFrame(double Arrived,ulong Time,bool Active,bool Paused,double Speed,Vector3 Position,double Heading);
    sealed record GateFrame(double Arrived,Semaphore[] Gates);
    TelemetryFrame? incomingTelemetry;
    TelemetryFrame? latestDeliveredTelemetry;
    GateFrame? incomingGates;
    double pathReceivedAt=-100;
    double routeWorkMilliseconds,maxRouteWorkMilliseconds;

    void OnTelemetry(GameTelemetryData data)
    {
        var frame=new TelemetryFrame(clock.Elapsed.TotalSeconds,data.time,data.sdkActive,data.paused,
            data.truckFloat.speed,data.truckPlacement.coordinate.ToVector3(),data.truckPlacement.rotation.X);
        Interlocked.Exchange(ref incomingTelemetry,frame);
        Interlocked.Exchange(ref latestDeliveredTelemetry,frame);
        if(!Monitor.TryEnter(sync))return;
        try
        {
            DrainInputFrames();
            RejectUnpreparedStart();
            SafetyWatchdog(clock.Elapsed.TotalSeconds);
        }
        finally {Monitor.Exit(sync);}
    }

    void OnGates(SemaphoreData data)
    {
        var frame=new GateFrame(clock.Elapsed.TotalSeconds,data.semaphores.Select(s=>new Semaphore {
            position=s.position,cx=s.cx,cy=s.cy,rotation=s.rotation,type=s.type,state=s.state,id=s.id
        }).ToArray());
        Interlocked.Exchange(ref incomingGates,frame);
        if(!Monitor.TryEnter(sync))return;
        try {DrainInputFrames();}
        finally {Monitor.Exit(sync);}
    }

    // Call only under sync. Pose, speed and source delivery time move together;
    // a heartbeat alone must never make an old position appear current.
    void DrainInputFrames()
    {
        if(Interlocked.Exchange(ref incomingTelemetry,null) is { } frame)
        {
            telemetryAt=frame.Arrived;
            if(gameTime!=frame.Time) {gameTimeChangedAt=frame.Arrived;gameTime=frame.Time;}
            sdk=frame.Active;paused=frame.Paused;speed=frame.Speed;position=frame.Position;headingTurns=frame.Heading;
            if(previewRouter!=null && (Math.Abs(speed)>.1 || Vector3.DistanceSquared(position,previewPosition)>1))previewRouter=null;
            if(!sdk || paused || (State.EnableAssists && Math.Abs(speed)>.1))connectorHistory.Clear();
            else if(!State.EnableAssists)connectorHistory.Observe(telemetryAt,position,speed);
        }
        if(Interlocked.Exchange(ref incomingGates,null) is { } gateFrame)
        {
            gates=gateFrame.Gates;gatesAt=gateFrame.Arrived;
        }
    }

    void OnPath(PathLib.PlannedPathData data)
    {
        double started=clock.Elapsed.TotalSeconds;
        try {ProcessPath(data);}
        finally
        {
            double elapsed=(clock.Elapsed.TotalSeconds-started)*1000;
            Interlocked.Exchange(ref routeWorkMilliseconds,elapsed);
            if(elapsed>maxRouteWorkMilliseconds)Interlocked.Exchange(ref maxRouteWorkMilliseconds,elapsed);
        }
    }
}
