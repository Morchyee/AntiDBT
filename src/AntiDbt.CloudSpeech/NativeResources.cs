namespace AntiDbt.CloudSpeech;
// Tracks native acquisitions until the Azure constructor transfers ownership.
internal sealed class NativeResources : IDisposable {
 readonly List<IDisposable> resources = new();
 public T Acquire<T>(Func<T> create) where T : IDisposable { var value = create(); resources.Add(value); return value; }
 public void Dispose() { foreach (var resource in resources.AsEnumerable().Reverse()) { try { resource.Dispose(); } catch { } } resources.Clear(); }
}
