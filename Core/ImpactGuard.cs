namespace MautAssistent;

/// <summary>Post-impact cutout. This detects a new collision signature, not obstacles ahead.</summary>
public readonly record struct DamageState(float Cabin,float Chassis)
{
    public bool Valid => float.IsFinite(Cabin)&&float.IsFinite(Chassis)&&Cabin is >=0 and <=1&&Chassis is >=0 and <=1;
    public bool IncreasedFrom(DamageState other) => Cabin-other.Cabin>=.0001f||Chassis-other.Chassis>=.0001f;
    public bool RepairedFrom(DamageState other) => Cabin<other.Cabin-.0001f||Chassis<other.Chassis-.0001f;
}
public readonly record struct ImpactFrame(double Arrived,ulong GameTime,ulong SimulatedTime,bool Enabled,bool Paused,
    bool Sdk,double Speed,double X,double Y,double Z,DamageState Damage);
public sealed record ImpactEvidence(ImpactFrame Before,ImpactFrame After,double SpeedLoss,double Elapsed);

public sealed class ImpactGuard
{
    ImpactFrame? previous;
    public void Reset()=>previous=null;
    public ImpactEvidence? Observe(ImpactFrame current)
    {
        var prior=previous;
        if(!Valid(current)||!current.Enabled||current.Paused||!current.Sdk) {Reset();return null;}
        // Duplicate SDK frames neither create evidence nor erase the last sample.
        if(prior.HasValue&&current.GameTime==prior.Value.GameTime)return null;
        previous=current;
        if(prior is not { } before||!before.Enabled||!before.Damage.Valid||!current.Damage.Valid)return null;
        double dt=current.Arrived-before.Arrived;
        if(dt is <=0 or >.25||current.GameTime<=before.GameTime||current.SimulatedTime<=before.SimulatedTime||
            current.Damage.RepairedFrom(before.Damage))return null;
        double sourceDt=(current.GameTime-before.GameTime)/1_000_000d;
        double simulationDt=(current.SimulatedTime-before.SimulatedTime)/1_000_000d;
        if(sourceDt>.25||simulationDt>.25||Math.Abs(sourceDt-dt)>.15||Math.Abs(simulationDt-dt)>.15)return null;
        // Loads/teleports are not a local physical impact. Damage must increase
        // contemporaneously: old damage or ordinary braking cannot satisfy this.
        double distance=Math.Sqrt(Math.Pow(current.X-before.X,2)+Math.Pow(current.Y-before.Y,2)+Math.Pow(current.Z-before.Z,2));
        if(distance>Math.Abs(before.Speed)*dt+5||Math.Abs(current.Y-before.Y)>2)return null;
        double loss=Math.Abs(before.Speed)-Math.Abs(current.Speed);
        if(Math.Abs(before.Speed)<4||loss<3||loss/Math.Max(dt,Math.Max(sourceDt,simulationDt))<25||!current.Damage.IncreasedFrom(before.Damage))return null;
        Reset();
        return new(before,current,loss,dt);
    }
    static bool Valid(ImpactFrame f)=>double.IsFinite(f.Arrived)&&double.IsFinite(f.Speed)&&
        double.IsFinite(f.X)&&double.IsFinite(f.Y)&&double.IsFinite(f.Z);
}
