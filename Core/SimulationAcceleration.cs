namespace MautAssistent;
public static class SimulationAcceleration
{
    // SCS scs_timestamp_t is microseconds. Arrival clocks are for freshness
    // only; queueing a callback cannot change the vehicle's measured dynamics.
    public static bool TryMeasure(ulong before,ulong after,double previousSpeed,double speed,out double acceleration)
    {
        acceleration=0;
        if(before==0||after<=before||after-before>500_000||!double.IsFinite(previousSpeed)||!double.IsFinite(speed))return false;
        double value=(speed-previousSpeed)/((after-before)/1_000_000d);
        if(!double.IsFinite(value)||Math.Abs(value)>20)return false;
        acceleration=value;return true;
    }
}
