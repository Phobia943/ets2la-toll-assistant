namespace MautAssistent;
public enum SceneCheckKind { Clear, UnknownEvidence, ConservativeOccupancy }
public static class SceneStopPolicy
{
    // Only the separately measured narrow driving corridor may supply this
    // distance. A large any-articulation enclosure is not acute-hazard proof.
    public static bool NeedsEmergency(double speed,double? acuteDistance)
    {
        if(!double.IsFinite(speed)||speed<=.5||acuteDistance is not double distance||!double.IsFinite(distance))return false;
        double usable=distance-speed*.5;
        return usable<=0||speed*speed/(2*usable)>2.5;
    }
    public static float PlannedLimit(double speed,double distance)
    {
        if(!double.IsFinite(speed)||!double.IsFinite(distance))throw new InvalidDataException("Ungültiger Szenen-Haltepunkt");
        double remaining=distance-Math.Max(0,speed)*1.5;
        double cap=Math.Min(Math.Sqrt(2*Math.Max(0,remaining)),.5*Math.Max(0,remaining));
        if(remaining<=.5&&Math.Abs(speed)<.1)return -.6f;
        return (float)Math.Clamp((cap-Math.Max(0,speed))*.8,-2.5,.35);
    }
}
