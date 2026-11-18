using ETS2LA.Game.Output;

namespace MautAssistent;

/// <summary>Latched fault braking, independent of ACC and EnableAssists.</summary>
public sealed class FaultBrake(Action<ControlEvent> publish)
{
    volatile bool latched;
    readonly object sync=new();
    readonly AsyncLocal<long?> operation=new();
    long generation;
    public bool Latched => latched;
    internal long ControlEpoch=>Interlocked.Read(ref generation);
    public IDisposable BeginOperation()
    {
        var previous=operation.Value;
        operation.Value=previous??Interlocked.Read(ref generation);
        return new Operation(()=>operation.Value=previous);
    }
    sealed class Operation(Action restore):IDisposable {public void Dispose()=>restore();}
    public bool OperationCurrent=>operation.Value is not long expected||expected==Interlocked.Read(ref generation);
    public bool RunCurrent(Action action)
    {lock(sync){if(!OperationCurrent)return false;action();return true;}}
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
    public void Trip() {lock(sync){if(!OperationCurrent)return;latched=true;Send(-1);}}
    public void Refresh() {lock(sync){if(latched&&OperationCurrent)Send(-1);}}
    public void Release()
    {
        lock(sync) {
            generation++; // Invalidate old route work even when no brake was latched yet.
            if(!latched)return;
            latched=false;
            // Expiry alone may retain a legacy pedal value beside live steering.
            Send(0);
        }
    }
}
