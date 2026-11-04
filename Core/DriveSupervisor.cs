namespace MautAssistent;

public enum RouteFeed { Waiting, Loading, Valid, Rejected }
public enum DriveAction { Idle, Paused, AwaitRoute, Drive, Recover, RejectStart, EmergencyStop }

public readonly record struct DriveFrame(
    double Time, double Speed, bool Enabled, bool Paused, bool TelemetryFresh,
    RouteFeed Route, double RouteAge, bool DecisionReady, bool CanRecover);

public readonly record struct DriveCheckpoint(double? Started, double? Interrupted, double? StableSince, bool Drove);
public readonly record struct DriveSample(DriveFrame Frame, DriveAction Action);
public sealed record DriveRecording(DriveCheckpoint InitialState, DriveSample[] Samples);

/// <summary>Decides whether the host can continue driving. Toll behaviour is a separate speed constraint.</summary>
public sealed class DriveSupervisor(DriveCheckpoint initialState = default)
{
    double? started = initialState.Started;
    double? interrupted = initialState.Interrupted;
    double? stableSince = initialState.StableSince;
    bool drove = initialState.Drove;
    public DriveCheckpoint Checkpoint => new(started, interrupted, stableSince, drove);

    public void Reset()
    {
        started = interrupted = stableSince = null;
        drove = false;
    }

    public DriveAction Step(DriveFrame frame)
    {
        if (!frame.Enabled)
        {
            Reset();
            return DriveAction.Idle;
        }
        if (!frame.TelemetryFresh || !double.IsFinite(frame.Speed) || !double.IsFinite(frame.Time))
            return DriveAction.EmergencyStop;
        if (frame.Paused)
        {
            if (!drove) started = null;
            return DriveAction.Paused;
        }

        started ??= frame.Time;
        bool stationary = Math.Abs(frame.Speed) <= .1;
        if (frame.Route == RouteFeed.Rejected || (drove && frame.Route != RouteFeed.Valid))
            return !drove && stationary ? DriveAction.RejectStart : DriveAction.EmergencyStop;

        // Only begin the early slowdown when the current corridor supports it.
        // Keep the existing one-second route limit in a critical approach.
        double freshUntil = frame.CanRecover ? .5 : 1;
        bool ready = frame.Route == RouteFeed.Valid && frame.DecisionReady && frame.RouteAge < freshUntil;
        if (!drove)
        {
            if (ready)
            {
                drove = true;
                return DriveAction.Drive;
            }
            if (!stationary && frame.Route != RouteFeed.Valid) return DriveAction.EmergencyStop;
            double timeout = frame.Route == RouteFeed.Loading ? 8 : 2;
            if (frame.Time - started.Value < timeout) return DriveAction.AwaitRoute;
            return stationary ? DriveAction.RejectStart : DriveAction.EmergencyStop;
        }

        if (ready && interrupted == null) return DriveAction.Drive;
        if (!frame.CanRecover || frame.RouteAge >= 1) return DriveAction.EmergencyStop;
        interrupted ??= frame.Time;
        if (frame.Time - interrupted.Value >= .5) return DriveAction.EmergencyStop;
        if (!ready) stableSince = null;
        else if (frame.Time - (stableSince ??= frame.Time) >= .15)
        {
            interrupted = stableSince = null;
            return DriveAction.Drive;
        }
        return DriveAction.Recover;
    }
}
