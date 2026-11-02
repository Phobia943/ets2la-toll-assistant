namespace MautAssistent;

public enum ControlPermission { Neutral, Limit, Disarm }
public static class ControlGuard
{
    public static ControlPermission Check(bool active, bool pluginRunning, bool assistsEnabled,
        bool laneOnly, bool fresh, double decisionAge, bool missingTarget)
    {
        // Manual cancellation always wins, even when every other input is invalid.
        if (!active || !pluginRunning || !assistsEnabled || laneOnly) return ControlPermission.Neutral;
        if (!fresh || !double.IsFinite(decisionAge) || decisionAge is < 0 or > .3 || missingTarget) return ControlPermission.Disarm;
        return ControlPermission.Limit;
    }
}
