namespace MautAssistent;

public enum BarrierState { Closing, Closed, Opening, Open, Unknown }
public sealed record Gate(string Key, double Distance, BarrierState State);
public sealed record TollInput(double Time, double Speed, bool Enabled, bool Fresh, Gate? Gate);
public sealed record TollDecision(string Phase, double? TargetSpeed, float MaxAcceleration, string? GateKey)
{
    public static TollDecision Idle(string phase) => new(phase, null, float.NaN, null);
}

public sealed record TollOptions
{
    public double ApproachSpeedKph { get; init; } = 20;
    public double PassageSpeedKph { get; init; } = 10;
    // Distance from the telemetry truck origin, not a measured bumper gap.
    public double StopOffsetMetres { get; init; } = 8;
    public double OpenDelaySeconds { get; init; } = 2;
    public double ExitDistanceMetres { get; init; } = 30;
    public double ComfortableDeceleration { get; init; } = 1.2;
    public void Validate()
    {
        var values = new[] { ApproachSpeedKph, PassageSpeedKph, StopOffsetMetres, OpenDelaySeconds, ExitDistanceMetres, ComfortableDeceleration };
        if (values.Any(x => !double.IsFinite(x)) || ApproachSpeedKph is < 5 or > 30 ||
            PassageSpeedKph is < 3 or > 15 || PassageSpeedKph > ApproachSpeedKph ||
            StopOffsetMetres is < 4 or > 15 || OpenDelaySeconds is < 1 or > 10 ||
            ExitDistanceMetres is < 10 or > 80 || ComfortableDeceleration is < .5 or > 2)
            throw new ArgumentException("Ungültige Maut-Einstellungen.");
    }
}

/// <summary>Pure state machine; time is monotonic seconds, speed m/s, distance along route.</summary>
public sealed class TollController(TollOptions options)
{
    string? tracked;
    double? openSince;
    double lastTime = double.NaN;
    bool released;
    bool crossed;
    public bool Tracking => tracked != null;
    public void Reset() { tracked = null; openSince = null; released = false; crossed=false; lastTime = double.NaN; }

    public TollDecision Step(TollInput input)
    {
        if (!input.Enabled) { Reset(); return TollDecision.Idle("Aus"); }
        if (!double.IsFinite(input.Time) || !double.IsFinite(input.Speed) || input.Speed < -.1)
        { Reset(); return TollDecision.Idle("Keine Vorwärtsfahrt"); }
        if (!input.Fresh) { openSince = null; released = false; return tracked == null ? TollDecision.Idle("Warte auf frische Daten") : Lost(); }
        if (double.IsFinite(lastTime) && (input.Time < lastTime || input.Time - lastTime > .5)) { openSince = null; released = false; }
        lastTime = input.Time;
        if (input.Gate is not { } gate || !double.IsFinite(gate.Distance))
            return tracked == null ? TollDecision.Idle("Keine passende Schranke") : Lost();
        if (tracked != null && tracked != gate.Key) return Lost();
        if (gate.Distance < -options.ExitDistanceMetres) { Reset(); return TollDecision.Idle("Mautstelle passiert"); }
        if (tracked == null)
        {
            // Include 1.5 seconds of reaction margin for the host controller's smoothing.
            var horizon = Math.Max(200, input.Speed * input.Speed / (2 * options.ComfortableDeceleration) + input.Speed * 1.5 + 40);
            if (gate.Distance < 0 || gate.Distance > horizon) return TollDecision.Idle("Außerhalb der Mautzone");
            tracked = gate.Key;
        }
        if(released && gate.Distance < -1)crossed=true;
        if(!crossed) {
            if (gate.State == BarrierState.Open) openSince ??= input.Time;
            else { openSince = null; released = false; }
            if (openSince.HasValue && input.Time - openSince.Value >= options.OpenDelaySeconds) released = true;
        }
        else released=true; // A gate closing behind the truck is not a new stop line.

        double speed = Math.Max(0, input.Speed);
        double remaining = Math.Max(0, gate.Distance - options.StopOffsetMetres - speed * 1.5);
        double cap;
        string phase;
        if (!released)
        {
            cap = Math.Min(options.ApproachSpeedKph / 3.6, Math.Sqrt(2 * options.ComfortableDeceleration * remaining));
            phase = gate.State == BarrierState.Open ? "Wartezeit nach Öffnung" : "Anfahrt / Schranke nicht frei";
            if (remaining < .5) { cap = 0; phase = gate.State == BarrierState.Open ? "Warten vor offener Schranke" : "Halten vor Schranke"; }
        }
        else
        {
            cap = options.PassageSpeedKph / 3.6;
            phase = "Langsame Durchfahrt";
        }
        // This is an upper bound consumed by ACC's minimum-of-constraints rule.
        // It can never increase the acceleration chosen by another constraint.
        float accel = (float)Math.Clamp((cap - speed) * .8, -2.5, .35);
        if (cap == 0) accel = speed > .1 ? (float)-Math.Min(2.5, Math.Max(.5, speed)) : -.35f;
        return new(phase, cap, accel, tracked);
    }
    TollDecision Lost() => new("Daten fehlen – manuell übernehmen", 0, -2.5f, tracked);
}
