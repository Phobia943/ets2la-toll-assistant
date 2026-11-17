namespace MautAssistent;
public static class NativeCommandTime
{
    public static double? AgeSeconds(long issued,long now,long frequency)
        =>UnboundedAgeSeconds(issued,now,frequency) is double age&&age<=.2?age:null;

    // Diagnostic/watchdog age in the producer's original Stopwatch domain.
    // This does not admit an output: AgeSeconds retains its separate 200ms cap.
    public static double? UnboundedAgeSeconds(long issued,long now,long frequency)
    {
        if(issued<=0||now<issued||frequency<=0)return null;
        // Both stamps are positive and ordered, so this difference cannot
        // overflow. Subtract first to preserve small deltas near long.MaxValue.
        return (double)(now-issued)/frequency;
    }
}
