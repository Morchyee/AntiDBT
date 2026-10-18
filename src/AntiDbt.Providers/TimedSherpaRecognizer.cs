using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using SherpaOnnx;

namespace AntiDbt.Providers;

// The 1.13.8 C# result omits the C API's JSON start_time. Read that documented
// metadata with owned native handles so word-boundary PCM ownership is exact.
internal sealed class TimedSherpaRecognizer : IDisposable
{
    private const string Library = "sherpa-onnx-c-api";
    private readonly RecognizerHandle handle;
    public TimedSherpaRecognizer(OnlineRecognizerConfig config)
    {
        handle = Create(ref config);
        if (handle.IsInvalid) throw new InvalidOperationException("sherpa recognizer initialization failed");
    }
    public OnlineStream CreateStream() => new(CreateStreamNative(handle));
    public bool IsReady(OnlineStream stream) => Ready(handle, stream.Handle) != 0;
    public bool IsEndpoint(OnlineStream stream) => Endpoint(handle, stream.Handle) != 0;
    public void Decode(OnlineStream stream) => DecodeNative(handle, stream.Handle);
    public void Reset(OnlineStream stream) => ResetNative(handle, stream.Handle);
    public (string Text, IReadOnlyDictionary<int, TimeSpan> WordStarts) GetResult(OnlineStream stream)
    {
        var pointer = Result(handle, stream.Handle);
        if (pointer == IntPtr.Zero) return ("", new Dictionary<int, TimeSpan>());
        try
        {
            var managed = new OnlineRecognizerResult(pointer);
            var native = Marshal.PtrToStructure<NativeResult>(pointer);
            var boundaries = new Dictionary<int, TimeSpan>();
            if (native.Json != IntPtr.Zero)
            {
                using var json = JsonDocument.Parse(Marshal.PtrToStringUTF8(native.Json)!);
                double start = json.RootElement.TryGetProperty("start_time", out var value) ? value.GetDouble() : 0;
                string joined = string.Concat(managed.Tokens).Replace('▁', ' ');
                if (joined.TrimStart() == managed.Text.TrimStart() && managed.Timestamps.Length == managed.Tokens.Length)
                {
                    int position = 0;
                    int delta = managed.Text.Length - managed.Text.TrimStart().Length - (joined.Length - joined.TrimStart().Length);
                    for (int i = 0; i < managed.Tokens.Length; i++)
                    {
                        string token = managed.Tokens[i].Replace('▁', ' ');
                        for (int j = 0; j < token.Length; j++)
                            if (char.IsWhiteSpace(token[j]) && position + j + delta >= 0)
                                boundaries[position + j + delta] = TimeSpan.FromSeconds(start + managed.Timestamps[i]);
                        position += token.Length;
                    }
                }
            }
            return (managed.Text, boundaries);
        }
        finally { DestroyResult(pointer); }
    }
    public void Dispose() => handle.Dispose();
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeResult
    {
        public IntPtr Text, Tokens, TokensArray, Timestamps;
        public int Count;
        public IntPtr Json;
    }
    private sealed class RecognizerHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public RecognizerHandle() : base(true) { }
        protected override bool ReleaseHandle() { Destroy(handle); return true; }
    }
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SherpaOnnxCreateOnlineRecognizer")]
    private static extern RecognizerHandle Create(ref OnlineRecognizerConfig config);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SherpaOnnxDestroyOnlineRecognizer")]
    private static extern void Destroy(IntPtr recognizer);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SherpaOnnxCreateOnlineStream")]
    private static extern IntPtr CreateStreamNative(RecognizerHandle recognizer);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SherpaOnnxIsOnlineStreamReady")]
    private static extern int Ready(RecognizerHandle recognizer, IntPtr stream);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SherpaOnnxOnlineStreamIsEndpoint")]
    private static extern int Endpoint(RecognizerHandle recognizer, IntPtr stream);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SherpaOnnxDecodeOnlineStream")]
    private static extern void DecodeNative(RecognizerHandle recognizer, IntPtr stream);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SherpaOnnxOnlineStreamReset")]
    private static extern void ResetNative(RecognizerHandle recognizer, IntPtr stream);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SherpaOnnxGetOnlineStreamResult")]
    private static extern IntPtr Result(RecognizerHandle recognizer, IntPtr stream);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SherpaOnnxDestroyOnlineRecognizerResult")]
    private static extern void DestroyResult(IntPtr result);
}
