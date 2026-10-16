namespace AntiDbt.Core;

public sealed record AudioQueueStats(
    int Count,
    TimeSpan TotalDuration,
    TimeSpan OldestChunkAge,
    long DroppedChunks,
    long Discontinuities);

public sealed class BoundedAudioQueue
{
    private readonly object _sync = new();
    private readonly Queue<AudioChunk> _queue = new();
    private readonly int _capacity;
    private readonly TimeSpan _maxAge;
    private long _dropped;
    private long _discontinuities;
    private bool _lastWasDrop;
    private TimeSpan _totalDuration;

    public BoundedAudioQueue(int capacity = 50, TimeSpan? maxAge = null)
    {
        _capacity = Math.Max(1, capacity);
        _maxAge = maxAge ?? TimeSpan.FromSeconds(2.0);
    }

    public int Capacity => _capacity;
    public int Count { get { lock (_sync) return _queue.Count; } }
    public long DroppedChunks { get { lock (_sync) return _dropped; } }
    public long Discontinuities { get { lock (_sync) return _discontinuities; } }
    public TimeSpan TotalDuration { get { lock (_sync) return _totalDuration; } }

    public TimeSpan OldestChunkAge
    {
        get
        {
            lock (_sync)
            {
                if (_queue.Count == 0) return TimeSpan.Zero;
                var head = _queue.Peek();
                if (head.CapturedTimestamp <= 0) return TimeSpan.Zero;
                return System.Diagnostics.Stopwatch.GetElapsedTime(head.CapturedTimestamp);
            }
        }
    }

    public AudioQueueStats GetStats()
    {
        lock (_sync)
        {
            TimeSpan oldestAge = TimeSpan.Zero;
            if (_queue.Count > 0 && _queue.Peek().CapturedTimestamp > 0)
            {
                oldestAge = System.Diagnostics.Stopwatch.GetElapsedTime(_queue.Peek().CapturedTimestamp);
            }
            return new AudioQueueStats(_queue.Count, _totalDuration, oldestAge, _dropped, _discontinuities);
        }
    }

    public void Enqueue(AudioChunk chunk)
    {
        lock (_sync)
        {
            if (_queue.Count >= _capacity)
            {
                var removed = _queue.Dequeue();
                _totalDuration -= removed.AudioDuration;
                _dropped++;
                if (!_lastWasDrop)
                {
                    _discontinuities++;
                    _lastWasDrop = true;
                }
            }
            else
            {
                _lastWasDrop = false;
            }
            _queue.Enqueue(chunk);
            _totalDuration += chunk.AudioDuration;
            if (_totalDuration < TimeSpan.Zero) _totalDuration = TimeSpan.Zero;
        }
    }

    public int TrimStale(TimeSpan? maxAge = null)
    {
        var limit = maxAge ?? _maxAge;
        int trimmed = 0;
        lock (_sync)
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            while (_queue.Count > 0)
            {
                var head = _queue.Peek();
                if (head.CapturedTimestamp > 0)
                {
                    var age = System.Diagnostics.Stopwatch.GetElapsedTime(head.CapturedTimestamp, now);
                    if (age > limit)
                    {
                        var removed = _queue.Dequeue();
                        _totalDuration -= removed.AudioDuration;
                        _dropped++;
                        trimmed++;
                        if (!_lastWasDrop)
                        {
                            _discontinuities++;
                            _lastWasDrop = true;
                        }
                        continue;
                    }
                }
                break;
            }
            if (_totalDuration < TimeSpan.Zero) _totalDuration = TimeSpan.Zero;
        }
        return trimmed;
    }

    public bool TryDequeue(out AudioChunk? chunk)
    {
        lock (_sync)
        {
            if (_queue.TryDequeue(out chunk))
            {
                _totalDuration -= chunk.AudioDuration;
                if (_totalDuration < TimeSpan.Zero) _totalDuration = TimeSpan.Zero;
                _lastWasDrop = false;
                return true;
            }
            return false;
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _queue.Clear();
            _totalDuration = TimeSpan.Zero;
            _lastWasDrop = false;
        }
    }
}
