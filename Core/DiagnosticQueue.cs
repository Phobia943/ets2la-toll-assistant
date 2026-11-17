namespace MautAssistent;

/// <summary>One storage worker, bounded by fixed diagnostic slots. No caller waits for I/O.</summary>
internal sealed class DiagnosticQueue(Action<Exception>? reportError = null)
{
    internal enum Slot { Status, Map, Preview, RouteError, Fault, AccObservation }
    readonly object sync = new();
    readonly Dictionary<Slot, Action> pending = new();
    readonly Queue<Slot> order = new();
    bool running, accepting = true;

    public void Enqueue(Slot slot, Action write)
    {
        bool launch;
        lock (sync)
        {
            if (!accepting) return;
            if (!pending.ContainsKey(slot)) order.Enqueue(slot);
            pending[slot] = write;
            launch = !running;
            running = true;
        }
        if (launch)
            _ = Task.Factory.StartNew(Drain, CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    void Drain()
    {
        while (true)
        {
            Action write;
            lock (sync)
            {
                if (pending.Count == 0) { running = false; return; }
                var slot = order.Dequeue();
                write = pending[slot];
                pending.Remove(slot);
            }
            try { write(); }
            catch (Exception ex)
            {
                // Even a failing error logger must not strand the worker.
                try { reportError?.Invoke(ex); } catch { }
            }
        }
    }

    // An in-flight filesystem call cannot be canceled safely. It may finish,
    // but it cannot access control state. A resumed session uses this same
    // worker, so an old write cannot overtake a newer one.
    public void Pause() { lock (sync) { accepting = false; pending.Clear(); order.Clear(); } }
    public void Resume() { lock (sync) accepting = true; }
    internal bool IsIdle { get { lock (sync) return !running; } }
}
