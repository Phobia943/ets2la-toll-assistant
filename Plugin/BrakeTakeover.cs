using ETS2LA.Controls;
using ETS2LA.Game.Output;

namespace MautAssistent;

public sealed partial class TollPlugin
{
    internal static readonly ControlDefinition BrakeControl=new() {
        Id="local.mautassistent.brake-takeover",Name="Bremse – manuelle Übernahme",
        Description="An dieselbe physische Achse wie die Spielbremse binden. Ab 12 % schalten Lenkung und Tempomat ab; F8 startet erst nach dem Loslassen erneut.",
        Type=ControlType.Float,DefaultKeybind=""
    };
    bool brakeHeld;
    float brakeInputValue;
    double takeoverUntil=-100;
    readonly Action<ControlEvent> publishOutput;

    void OnBrakeInput(object? sender,ControlChangeEventArgs e)
    {
        float value=e.NewValue switch {float f=>f,bool b=>b?1:0,_=>float.NaN};
        if(!float.IsFinite(value)||value<0||value>1)return;
        bool immediateTakeover=value>=.12f && (State.EnableAssists || rollingStartPending || faultBrake.Latched);
        if(immediateTakeover)
        {
            // Releasing the modern override must not wait for model/route work.
            // State is disabled first; bookkeeping follows under the plugin lock.
            CancelSteeringSession();
            ReleaseAssistOutputs();
        }
        lock(sync)
        {
            brakeInputValue=value;
            if(value<=.05f)brakeHeld=false;
            if(value<.12f || brakeHeld)return;
            brakeHeld=true;
            bool driving=immediateTakeover || State.EnableAssists || rollingStartPending || faultBrake.Latched;
            CancelRollingStart();
            preview=previewCalculating=false;previewGeneration++;
            State.EnableAssists=false;
            active=false;
            faultBrake.Release();
            ResetDriving();startCheck=null;
            controller.Reset();passage.Reset();tracked=null;candidate=null;
            router.CancelPlannedStop();warnedStopKey=null;
            automaticPreparation.Request();
            preparationReason=rollingStartStatus="Manuelle Übernahme durch Bremse. Erst Bremse loslassen; danach kann F8 erneut starten.";
            if(driving)
            {
                takeoverUntil=clock.Elapsed.TotalSeconds+.3;
                if(!immediateTakeover)ReleaseAssistOutputs();
            }
        }
    }

    void ReleaseAssistOutputs()
    {
        // Use the public output contract to overwrite residual/in-flight native
        // commands. Merely expiring a channel can leave the last value in memory.
        // Refresh briefly after disable; zero releases the modern override.
        foreach(var id in new[]{"AdaptiveCruiseControl.Acceleration","LaneAssist.SteeringCorrection",FaultBrake.ChannelId})
            publishOutput(new() {ChannelDefinition=new() {Id=id,Timeout=.1f},
                Variables=new() {aforward=0,abackward=0,steering=0}});
    }
}
