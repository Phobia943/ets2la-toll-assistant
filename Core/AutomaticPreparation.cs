namespace MautAssistent;

// Preparation only: this gate never enables driving or releases a brake.
public sealed class AutomaticPreparation
{
    public bool Requested { get; private set; } = true;
    double? readySince;
    double nextAttempt = double.NegativeInfinity;
    public void Request() { Requested = true; readySince = null; nextAttempt = double.NegativeInfinity; }
    public void Cancel() { Requested = false; readySince = null; }
    public bool ShouldAttempt(double now, bool ready)
    {
        if (!Requested || !ready || !double.IsFinite(now)) { readySince = null; return false; }
        if (readySince is null || now < readySince) readySince = now;
        if (now - readySince.Value < 1 || now < nextAttempt) return false;
        nextAttempt = now + 1;
        return true;
    }
}
