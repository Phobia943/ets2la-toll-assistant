namespace MautAssistent;

/// <summary>A planned stop on a still valid approach, not a tracking fault.</summary>
public static class ApproachStop
{
    public const double BufferMetres = 15;
    public static TollDecision Evaluate(double speed, double distance)
    {
        if (!double.IsFinite(speed) || !double.IsFinite(distance) || speed < -.1)
            throw new InvalidOperationException("Ungültige Daten für den geplanten Mauthalt.");
        speed = Math.Max(0, speed);
        // Reserve for host smoothing and vehicle response. Distance already ends
        // before the first unconfirmed branch, not at the payment booth.
        double usable = distance - speed * 1.5 - 3;
        if (speed > .5 && (usable <= 0 || speed * speed / (2 * usable) > 2.5))
            throw new InvalidOperationException("Unbestätigte Mautspur zu spät erkannt: Anhalteweg vor der Aufteilung reicht nicht.");
        if (distance < -1)
            throw new InvalidOperationException("Haltegrenze vor unbestätigter Mautspur überschritten.");
        if (speed < .1)
            return new("Warten auf Mautspur-Freigabe – F9 zur Übernahme", 0, -.35f, null);
        double needed = speed * speed / (2 * Math.Max(.1, usable));
        double deceleration = Math.Clamp(needed, .4, 2.5);
        return new("Geplanter Halt vor unbestätigter Mautspur", 0, (float)-deceleration, null);
    }
}
