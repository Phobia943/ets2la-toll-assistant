using System.Numerics;

namespace MautAssistent;

/// <summary>Short manual trajectory used to distinguish overlapping connector paths at a stop.</summary>
public sealed class ConnectorHistory
{
    readonly Queue<Vector3> points = new();
    double lastAt;

    public void Clear() => points.Clear();

    public void Observe(double now, Vector3 position, double speed)
    {
        if (!double.IsFinite(now) || !float.IsFinite(position.LengthSquared()) || !double.IsFinite(speed) || speed < -.1)
        {
            Clear();
            return;
        }
        if (points.Count > 0)
        {
            float step = Vector3.Distance(points.Last(), position);
            if (step < 1) return;
            if (step > 4 || now - lastAt > 30) Clear();
        }
        if (speed <= .1) return;
        if (points.Count == 16) points.Dequeue();
        points.Enqueue(position);
        lastAt = now;
    }

    public Vector3[] Snapshot(double now, Vector3 position) =>
        points.Count >= 7 && now - lastAt <= 90 && Vector3.Distance(points.Last(), position) <= 2
            ? points.ToArray() : [];

    public static double Fit(RouteTrace route, IReadOnlyList<Vector3> history)
    {
        if (history.Count < 7) return double.PositiveInfinity;
        var samples = history.Select(route.Project).ToArray();
        if (samples[0].Along < .25 || samples[^1].Along >= route.Length - .25 ||
            samples[^1].Along - samples[0].Along < 6 ||
            samples.Zip(samples.Skip(1)).Any(pair => pair.Second.Along <= pair.First.Along))
            return double.PositiveInfinity;
        return Math.Sqrt(samples.Average(s => s.Offset * s.Offset));
    }
}
