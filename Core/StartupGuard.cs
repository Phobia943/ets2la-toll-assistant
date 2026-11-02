namespace MautAssistent;

public enum StartupPermission { Neutral, Hold, Ready, Disarm }
public sealed class StartupGuard
{
    double? started;
    bool hadRoute;
    public void Reset() { started=null; hadRoute=false; }
    public StartupPermission Check(bool enabled, bool ready, double now)
    {
        if (!enabled) { Reset(); return StartupPermission.Neutral; }
        if (!double.IsFinite(now)) return StartupPermission.Disarm;
        started ??= now;
        if (ready) { hadRoute=true; return StartupPermission.Ready; }
        if (hadRoute || now-started.Value >= 2 || now<started.Value) return StartupPermission.Disarm;
        return StartupPermission.Hold;
    }
}
