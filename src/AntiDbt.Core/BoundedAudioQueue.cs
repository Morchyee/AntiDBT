namespace AntiDbt.Core;
public sealed class BoundedAudioQueue
{
    private readonly object _sync = new(); private readonly Queue<AudioChunk> _queue = new();
    private readonly int _capacity; private long _dropped;
    public BoundedAudioQueue(int capacity = 50) => _capacity = Math.Max(1, capacity);
    public int Count
    {
        get
        {
            lock (_sync)
                return _queue.Count;
        }
    }
    public long DroppedChunks
    {
        get
        {
            lock (_sync)
                return _dropped;
        }
    }
    public void Enqueue(AudioChunk chunk)
    {
        lock (_sync)
        {
            if (_queue.Count == _capacity)
            {
                _queue.Dequeue();
                _dropped++;
            }
            _queue.Enqueue(chunk);
        }
    }
    public bool TryDequeue(out AudioChunk? chunk)
    {
        lock (_sync)
            return _queue.TryDequeue(out chunk);
    }
    public void Clear()
    {
        lock (_sync)
            _queue.Clear();
    }
}
