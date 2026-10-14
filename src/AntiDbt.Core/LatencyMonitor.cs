namespace AntiDbt.Core;
public sealed record LatencySnapshot(int Count, double MedianMilliseconds, double P95Milliseconds);
public sealed class LatencyMonitor
{
    private readonly object _sync = new();
    private readonly Queue<double> _samples = new();
    public void Record(TimeSpan latency)
    {
        if (latency < TimeSpan.Zero || latency > TimeSpan.FromMinutes(5))
            return;
        lock (_sync)
        {
            _samples.Enqueue(latency.TotalMilliseconds);
            while (_samples.Count > 1000)
                _samples.Dequeue();
        }
    }
    public LatencySnapshot Snapshot()
    {
        lock (_sync)
        {
            var a = _samples.Order().ToArray();
            if (a.Length == 0)
                return new(0, 0, 0);
            var mid = a.Length / 2;
            return new(a.Length, a.Length % 2 == 0 ? (a[mid - 1] + a[mid]) / 2 : a[mid], a[(int)Math.Ceiling(a.Length * .95) - 1]);
        }
    }
}
