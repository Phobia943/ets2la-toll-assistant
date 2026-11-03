using ETS2LA.Game.Output;

namespace MautAssistent;

/// <summary>Latched fault braking, independent of ACC and EnableAssists.</summary>
public sealed class FaultBrake(Action<ControlEvent> publish)
{
    public bool Latched { get; private set; }
    public const string ChannelId="local.mautassistent.fault-brake";
    void Send(float pedal) => publish(new ControlEvent {
        ChannelDefinition=new() {Id=ChannelId,Timeout=.2f},
        Properties=new() {Weight=100},
        // The host combines aforward/abackward into one signed pedal axis.
        // A negative value brakes and clears throttle in either output mode.
        // Explicit neutral steering releases the modern steering override once
        // the disabled lane-assist channel expires. Otherwise its last value
        // can remain in output memory while our brake channel stays alive.
        Variables=new() {abackward=pedal,steering=0}
    });
    public void Trip() {Latched=true;Send(-1);}
    public void Refresh() {if(Latched)Send(-1);}
    public void Release()
    {
        if(!Latched)return;
        Latched=false;
        // Explicit zero is required: expiry alone may leave legacy pedal memory
        // unchanged while another channel (e.g. steering) is still present.
        Send(0);
    }
}
