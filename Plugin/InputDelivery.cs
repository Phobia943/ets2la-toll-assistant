using System.Numerics;
using ETS2LA.Game.SDK;
using ETS2LA.Game.Telemetry;
using Semaphore=ETS2LA.Game.SDK.Semaphore;

namespace MautAssistent;

public sealed partial class TollPlugin
{
    // SDK publishers are synchronous. Never make them wait for route/model work:
    // that would also prevent telemetry delivery to the host's other consumers.
    sealed record PhysicsObservation(ulong SimulatedTime,ulong RenderTime,float EngineDamage,float TransmissionDamage,
        float CabinDamage,float ChassisDamage,float WheelDamage,float GameThrottle,float GameBrake)
    { public float SpeedLimitMps {get;init;} public bool GameCruise {get;init;} }
    sealed record TelemetryFrame(double Arrived,ulong Time,bool Active,bool Paused,double Speed,Vector3 Position,double Heading,
        PhysicsObservation Physics) {public EnvelopeContext? Envelope {get;init;}}
    sealed record GateFrame(double Arrived,Semaphore[] Gates) {public long Origin {get;init;} public long Epoch {get;init;}}
    long gatesOrigin,gatesSourceEpoch;
    TelemetryFrame? incomingTelemetry;
    TelemetryFrame? latestDeliveredTelemetry;
    TelemetryFrame? appliedTelemetry;
    GateFrame? incomingGates;
    double pathReceivedAt=-100;
    double routeWorkMilliseconds,maxRouteWorkMilliseconds;
    PhysicsObservation? physicsObservation;

    void OnTelemetry(GameTelemetryData data)
    {
        using var operation=faultBrake.BeginOperation();
        var frame=new TelemetryFrame(clock.Elapsed.TotalSeconds,data.time,data.sdkActive,data.paused,
            data.truckFloat.speed,data.truckPlacement.coordinate.ToVector3(),data.truckPlacement.rotation.X,
            new(data.simulatedTime,data.renderTime,data.truckFloat.wearEngine,data.truckFloat.wearTransmission,
                data.truckFloat.wearCabin,data.truckFloat.wearChassis,data.truckFloat.wearWheels,
                data.truckFloat.gameThrottle,data.truckFloat.gameBrake){SpeedLimitMps=data.truckFloat.speedLimit,GameCruise=data.truckBool.cruiseControl}) {Envelope=EnvelopeContext.Capture(data)};
        Interlocked.Exchange(ref incomingTelemetry,frame);
        Interlocked.Exchange(ref latestDeliveredTelemetry,frame);
        var ground=Volatile.Read(ref stationaryGround);
        if(ground?.InvalidatedBy(frame.Position,frame.Heading,frame.Speed,frame.Active)==true)
            Interlocked.CompareExchange(ref stationaryGround,null,ground);
        ObserveImpact(frame);
        ObserveBrakeResponse(frame);
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
        long sourceEpoch=CaptureSourceEpoch();
        var source=requireHostCompatibility?ownSemaphoreFrame?.GetValue(null) as Tuple<long,SemaphoreData>:null;
        var frame=new GateFrame(clock.Elapsed.TotalSeconds,(source?.Item2??data).semaphores.Select(s=>new Semaphore {
            position=s.position,cx=s.cx,cy=s.cy,rotation=s.rotation,type=s.type,state=s.state,id=s.id
        }).ToArray()){Origin=source?.Item1??0,Epoch=sourceEpoch};
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
            observedAcceleration=0;
            accelerationKnown=frame.Active&&!frame.Paused&&SimulationAcceleration.TryMeasure(
                physicsObservation?.SimulatedTime??0,frame.Physics.SimulatedTime,speed,frame.Speed,out observedAcceleration);
            appliedTelemetry=frame;
            telemetryAt=frame.Arrived;
            if(gameTime!=frame.Time) {gameTimeChangedAt=frame.Arrived;gameTime=frame.Time;}
            sdk=frame.Active;paused=frame.Paused;speed=frame.Speed;position=frame.Position;headingTurns=frame.Heading;
            physicsObservation=frame.Physics;
            envelopeContext=frame.Envelope;
            if(stationaryGround?.InvalidatedBy(position,headingTurns,speed,sdk)==true)
                stationaryGround=null;
            if(previewRouter!=null && (Math.Abs(speed)>.1 || Vector3.DistanceSquared(position,previewPosition)>1))previewRouter=null;
            if(!sdk || paused || (State.EnableAssists && Math.Abs(speed)>.1))connectorHistory.Clear();
            else if(!State.EnableAssists)connectorHistory.Observe(telemetryAt,position,speed);
        }
        if(Interlocked.Exchange(ref incomingGates,null) is { } gateFrame)
        {
            gates=gateFrame.Gates;gatesAt=gateFrame.Arrived;gatesOrigin=gateFrame.Origin;gatesSourceEpoch=gateFrame.Epoch;
        }
    }

    void OnPath(PathLib.PlannedPathData data)
    {
        using var operation=faultBrake.BeginOperation();
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
