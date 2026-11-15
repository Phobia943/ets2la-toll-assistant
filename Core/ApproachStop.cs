namespace MautAssistent;

/// <summary>A planned stop on a still valid approach, not a tracking fault.</summary>
public static class ApproachStop
{
    public const double BufferMetres = 15;
    // The soft stopping target is 15 m before the uncertain branch. Keep a
    // separate protected origin clearance rather than treating every response
    // margin at the soft target as an already unavoidable collision.
    public const double ProtectedClearanceMetres=8;
    public static float BeforeRouteGap(double speed, double connectedAhead)
    {
        if (!double.IsFinite(speed) || !double.IsFinite(connectedAhead))
            throw new InvalidOperationException("Ungültige Daten zur Planungslücke.");
        // Leave space between the planned stop and the emergency threshold.
        double remaining = Math.Max(0, connectedAhead - TrackingGuard.RequiredLookahead(0) - 10 - Math.Max(0, speed) * 1.5);
        double cap = Math.Min(Math.Sqrt(1.8 * remaining), .45 * remaining);
        if (cap < .1 && speed < .1) return -.35f;
        return (float)Math.Clamp((cap - speed) * .8, -2, .35);
    }

    public static TollDecision Evaluate(double speed, double distance)
    {
        if (!double.IsFinite(speed) || !double.IsFinite(distance) || speed < -.1)
            throw new InvalidOperationException("Ungültige Daten für den geplanten Mauthalt.");
        speed = Math.Max(0, speed);
        // Reserve for host smoothing and vehicle response. Distance already ends
        // before the first unconfirmed branch, not at the payment booth.
        double usable = distance - speed * 1.5 - 3;
        double protectedRemaining=distance+BufferMetres-ProtectedClearanceMetres;
        double emergencyUsable=protectedRemaining-speed*1.5;
        if (speed > .1 && (emergencyUsable <= 0 || speed * speed / (2 * emergencyUsable) > 2.5))
            throw new InvalidOperationException("Unbestätigte Mautspur zu spät erkannt: Anhalteweg vor der Aufteilung reicht nicht.");
        if (protectedRemaining < 0)
            throw new InvalidOperationException("Haltegrenze vor unbestätigter Mautspur überschritten.");
        if (usable <= .5 && speed < .1)
            return new("Warten auf Mautspur-Freigabe – F9 zur Übernahme", 0, -.35f, null);
        double needed = speed * speed / (2 * Math.Max(.1, usable));
        double deceleration = Math.Clamp(needed, .4, 2.5);
        double limit = usable <= .5 ? 0 : Math.Min(Math.Sqrt(1.8 * usable), .6 * usable);
        float acceleration = (float)Math.Clamp((limit-speed)*.8, -deceleration, .35);
        return new("Geplanter Halt vor unbestätigter Mautspur", limit, acceleration, null);
    }
}

/// <summary>Once a planned uncertain-lane stop is reached, hold until routing resolves it or the driver cancels.</summary>
public sealed class ApproachStopController
{
    public bool Holding {get;private set;}
    public string? StopKey {get;private set;}
    public void Reset(){Holding=false;StopKey=null;}
    public TollDecision Step(string stopKey,double speed,double distance)
    {
        if(string.IsNullOrWhiteSpace(stopKey))throw new InvalidOperationException("Haltepunkt nicht eindeutig zugeordnet.");
        if(StopKey!=stopKey){Holding=false;StopKey=stopKey;}
        var decision=ApproachStop.Evaluate(speed,distance); // Always retain the physical-boundary emergency check.
        if(decision.TargetSpeed==0)Holding=true;
        if(!Holding)return decision;
        // ACC 2.1.1 returns before publishing when v<0.5 and the target is
        // [-0.5,+0.1]. Stay strictly below that dead band for an actual hold.
        return new("Warten auf Mautspur-Freigabe – F9 zur Übernahme",0,
            Math.Min(decision.MaxAcceleration,-.6f),null);
    }
}
