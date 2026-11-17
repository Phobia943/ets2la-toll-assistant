using System.Diagnostics;
using System.Numerics;

namespace MautAssistent;

// One caller serializes Observe/Read/Reset and binds Reset to its own session,
// takeover and phase lifecycle. Numeric SDK clocks alone are not process IDs.
public sealed class SimulationDynamics
{
    public const double MaximumAgeSeconds=.2;
    public readonly record struct Evidence(double Acceleration,long Origin,double ArrivedAt,ulong SdkStamp,ulong SimStamp);
    readonly record struct Sample(double Arrival,long Origin,ulong Sdk,ulong Sim,double Speed,Vector3 Position,double Heading);
    readonly long maximumAgeTicks;
    Sample? receipt,physical;
    Evidence? evidence;
    ulong? quarantinedSim;
    double? lastReadAt;
    long lastReadTicks;

    public SimulationDynamics():this(Stopwatch.Frequency) { }
    public SimulationDynamics(long frequency)
    {
        if(frequency<=0)throw new ArgumentOutOfRangeException(nameof(frequency));
        maximumAgeTicks=frequency/5; // Exactly floor(200 ms), no large-clock subtraction through doubles.
    }
    public void Reset()
    {
        receipt=physical=null;evidence=null;quarantinedSim=null;lastReadAt=null;lastReadTicks=0;
    }
    void Reject(ulong sim)
    {
        Reset();if(sim!=0)quarantinedSim=sim;
    }
    static bool SamePhysics(Sample a,Sample b)=>a.Speed==b.Speed&&a.Position==b.Position&&a.Heading==b.Heading;
    public void Observe(double arrivalSeconds,long originalQpc,ulong sdkTime,ulong simTime,double speed,Vector3 position,double heading,bool active,bool paused)
    {
        if(!active||paused) {Reset();return;}
        if(!double.IsFinite(arrivalSeconds)||arrivalSeconds<0||originalQpc<=0||sdkTime==0||simTime==0||
            !double.IsFinite(speed)||Math.Abs(speed)>100||!double.IsFinite(heading)||!float.IsFinite(position.LengthSquared())) {
            Reject(simTime);return;
        }
        var current=new Sample(arrivalSeconds,originalQpc,sdkTime,simTime,speed,position,heading);
        if(quarantinedSim is ulong blocked) {
            if(simTime<=blocked)return;
            quarantinedSim=null;receipt=physical=current;return;
        }
        if(receipt is not { } last) {receipt=physical=current;return;}
        if(arrivalSeconds<last.Arrival||originalQpc<last.Origin||sdkTime<last.Sdk||simTime<last.Sim) {
            Reject(simTime);return;
        }
        if(simTime==last.Sim) {
            if(!SamePhysics(current,last)) {Reject(simTime);return;}
            // A benign delivery/SDK heartbeat may advance, but the certificate
            // and its original physical sample retain their OLD origins.
            receipt=current;return;
        }
        if(arrivalSeconds<=last.Arrival||originalQpc<=last.Origin||physical is not { } prior) {
            Reject(simTime);return;
        }
        double elapsed=(simTime-prior.Sim)/1_000_000d;
        if(elapsed>.5) {
            // A gap cannot create acceleration; start a new two-frame baseline.
            evidence=null;receipt=physical=current;return;
        }
        if(!SimulationAcceleration.TryMeasure(prior.Sim,simTime,prior.Speed,speed,out double acceleration)) {
            Reject(simTime);return;
        }
        // Coarse pose consistency only, never route/collision permission. The
        // 20 m/s² bound matches TryMeasure; 25 cm is a quantization/sanity slack.
        double maximumChord=.25+Math.Max(Math.Abs(prior.Speed),Math.Abs(speed))*elapsed+10*elapsed*elapsed;
        double chord=Vector3.Distance(prior.Position,position);
        if(!double.IsFinite(chord)||chord>maximumChord) {Reject(simTime);return;}
        receipt=physical=current;
        evidence=new(acceleration,originalQpc,arrivalSeconds,sdkTime,simTime);
    }
    public Evidence? Read(double nowSeconds,long qpcNow)
    {
        if(!double.IsFinite(nowSeconds)||nowSeconds<0||qpcNow<=0||
            lastReadAt is double previous&&(nowSeconds<previous||qpcNow<lastReadTicks)) {
            Reset();return null;
        }
        lastReadAt=nowSeconds;lastReadTicks=qpcNow;
        if(evidence is not { } value)return null;
        double elapsed=nowSeconds-value.ArrivedAt;
        if(elapsed<0||qpcNow<value.Origin) {Reject(value.SimStamp);return null;}
        if(elapsed>MaximumAgeSeconds||qpcNow-value.Origin>maximumAgeTicks)return null;
        return value;
    }
}
