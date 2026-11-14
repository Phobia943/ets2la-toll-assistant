using ETS2LA.State;

namespace MautAssistent;

public sealed partial class TollPlugin
{
    readonly BrakeResponseGuard brakeResponse=new();
    sealed record BrakeWarning(DateTimeOffset Utc,BrakeResponseFailure Evidence);
    BrakeWarning? brakeWarning;
    public string BrakeResponseStatus=>Volatile.Read(ref brakeWarning)!=null?
        "Warnung: Bremsanforderung ohne Spielreaktion aufgezeichnet. Ins Spiel wechseln und manuell übernehmen.":"";

    void ObserveBrakeResponse(TelemetryFrame frame)
    {
        BrakeWarning? issued=null;
        lock(steeringSync) {
            var command=Volatile.Read(ref lastAccCommand);
            double now=clock.Elapsed.TotalSeconds;
            bool required=_IsRunning&&Volatile.Read(ref active)&&State.EnableAssists&&!Volatile.Read(ref preview)&&
                State.DrivingMode!=DrivingMode.LaneAssistOnly;
            var failure=brakeResponse.Observe(new(frame.Arrived,frame.Time,frame.Physics.SimulatedTime,required,
                frame.Active,frame.Paused,frame.Speed,command!=null?now-command.At:double.PositiveInfinity,
                command?.Brake is float brake?Math.Clamp(-brake,0,1):0,frame.Physics.GameBrake));
            if(failure!=null)brakeWarning=issued=new(DateTimeOffset.UtcNow,failure);
            if(!required)brakeWarning=null;
        }
        if(issued==null)return;
        var report=issued;
        _=Task.Run(()=> {
            // Diagnostic only. Do not drop working steering or promise that a
            // second brake request through an unresponsive output can stop the truck.
            if(!ReferenceEquals(Volatile.Read(ref brakeWarning),report)||!State.EnableAssists)return;
            try {notify("Bremsanforderung ohne Spielreaktion",
                "Die Assistenz fordert Bremsung an, das Spiel meldet seit mindestens einer Sekunde keinen Bremseingriff. Bitte ins Spiel wechseln und manuell übernehmen.");}
            catch { /* Notification failures never change the control state. */ }
        });
    }
    void ClearBrakeResponse() {brakeResponse.Reset();brakeWarning=null;}
}
