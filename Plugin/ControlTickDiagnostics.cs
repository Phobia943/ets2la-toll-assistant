using System.Diagnostics;

namespace MautAssistent;

public sealed partial class TollPlugin
{
    internal enum ControlTickPhase
    {
        CapturingEpoch, WaitingForRoute, Preparing, TollDecision,
        Longitudinal, ManualPayment, Diagnostics, Completed
    }

    // Observational only: none of these stamps can create or refresh output
    // authority. One immutable reference makes a watchdog capture coherent
    // without acquiring the route lock it is trying to diagnose.
    internal sealed record ControlTickObservation(ControlTickPhase Phase,long StartedAt,
        long PhaseAt,long LastCompletedAt,long? Epoch,long ClockFrequency);
    ControlTickObservation? controlTickObservation;

    void ObserveControlTick(ControlTickPhase phase,long startedAt,long? epoch=null)
    {
        long observedAt=Stopwatch.GetTimestamp();
        while(true) {
            var previous=Volatile.Read(ref controlTickObservation);
            ControlTickObservation current;
            if(previous?.StartedAt>startedAt) {
                // An older Tick can finish while the newer producer waits.
                // Preserve the newer phase and origin, but record that actual
                // completion instead of implying every producer stayed stuck.
                if(phase!=ControlTickPhase.Completed)return;
                current=previous with {LastCompletedAt=Math.Max(previous.LastCompletedAt,observedAt)};
            } else current=new ControlTickObservation(phase,startedAt,observedAt,
                phase==ControlTickPhase.Completed?observedAt:previous?.LastCompletedAt??0,
                epoch,Stopwatch.Frequency);
            if(ReferenceEquals(Interlocked.CompareExchange(ref controlTickObservation,current,previous),previous))return;
        }
    }
}
