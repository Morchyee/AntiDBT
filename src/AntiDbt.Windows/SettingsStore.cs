using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using AntiDbt.Core;

namespace AntiDbt.Windows;

public sealed class SettingsStore
{
    readonly string directory;
    readonly Action<string> deleteTemporary;
    public SettingsStore(string directory) : this(directory, File.Delete) { }
    internal SettingsStore(string directory, Action<string> deleteTemporary)
    {
        this.directory = directory;
        this.deleteTemporary = deleteTemporary;
    }
    readonly SemaphoreSlim gate = new(1, 1);
    public string? Warning
    {
        get; private set;
    }
    sealed record Envelope(int Version, AppSettings Settings, string ProtectedSecrets);
    sealed record Secrets(string AsrApiKey, Dictionary<string, string> Asr,
        string TranslationApiKey, Dictionary<string, string> Translation);

    public async Task<AppSettings> LoadAsync()
    {
        await gate.WaitAsync();
        try
        {
            Warning = null;
            var path = Path.Combine(directory, "settings.json");
            if (!File.Exists(path))
                return new();
            try
            {
                var envelope = JsonSerializer.Deserialize<Envelope>(await File.ReadAllBytesAsync(path))
                    ?? throw new InvalidDataException();
                if (envelope.Version != 1 || envelope.Settings is null)
                    throw new InvalidDataException("Unsupported settings schema");
                var plain = Dpapi.Transform(Convert.FromBase64String(envelope.ProtectedSecrets), false);
                Secrets secrets;
                try
                {
                    secrets = JsonSerializer.Deserialize<Secrets>(plain) ?? throw new InvalidDataException();
                }
                finally { CryptographicOperations.ZeroMemory(plain); }
                if (secrets.Asr is null || secrets.Translation is null)
                    throw new InvalidDataException();
                Validate(envelope.Settings);
                return envelope.Settings with
                {
                    Asr = envelope.Settings.Asr with
                    {
                        ApiKey = secrets.AsrApiKey,
                        Secrets = secrets.Asr
                    },
                    Translation = envelope.Settings.Translation with
                    {
                        ApiKey = secrets.TranslationApiKey,
                        Secrets = secrets.Translation
                    }
                };
            }
            catch (Exception e) when (e is JsonException or InvalidDataException or
                CryptographicException or FormatException or ArgumentException)
            {
                Warning = "Settings were invalid or could not be decrypted; defaults loaded.";
                return new();
            }
        }
        finally { gate.Release(); }
    }

    public async Task SaveAsync(AppSettings settings)
    {
        Validate(settings);
        await gate.WaitAsync();
        string? temporary = null;
        try
        {
            Directory.CreateDirectory(directory);
            var secret = new Secrets(settings.Asr.ApiKey, settings.Asr.Secrets,
                settings.Translation.ApiKey, settings.Translation.Secrets);
            var plain = JsonSerializer.SerializeToUtf8Bytes(secret);
            byte[] encrypted;
            try
            {
                encrypted = Dpapi.Transform(plain, true);
            }
            finally { CryptographicOperations.ZeroMemory(plain); }
            // One atomic envelope avoids mismatching public settings and encrypted credentials.
            // JsonIgnore on the Core contracts keeps every secret out of Settings.
            var envelope = new Envelope(1, settings, Convert.ToBase64String(encrypted));
            var target = Path.Combine(directory, "settings.json");
            temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await using (var stream = new FileStream(temporary, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, envelope);
                await stream.FlushAsync();
                stream.Flush(true);
            }
            File.Move(temporary, target, true);
            temporary = null;
            Warning = null;
        }
        finally
        {
            try
            {
                if (temporary != null)
                    deleteTemporary(temporary);
            }
            finally { gate.Release(); }
        }
    }

    static void Validate(AppSettings s)
    {
        if (s.Asr is null || s.Translation is null || s.Asr.Options is null ||
            s.Translation.Options is null || s.Asr.Secrets is null || s.Translation.Secrets is null ||
            s.MaxHistory <= 0 || s.SilenceSeconds <= 0 || s.Translation.TimeoutSeconds <= 0 ||
            s.Translation.DebounceMilliseconds < 0 || s.Translation.ContextSentences < 0)
            throw new InvalidDataException("Invalid settings schema");
    }
}

internal static class Dpapi
{
    [StructLayout(LayoutKind.Sequential)]
    struct Blob
    {
        public int Length; public IntPtr Data;
    }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy,
        IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy,
        IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")]
    static extern IntPtr LocalFree(IntPtr memory);

    public static byte[] Transform(byte[] data, bool encrypt)
    {
        var input = new Blob { Length = data.Length, Data = Marshal.AllocHGlobal(data.Length) };
        Blob output = default;
        try
        {
            Marshal.Copy(data, 0, input.Data, data.Length);
            // CRYPTPROTECT_UI_FORBIDDEN, with no LOCAL_MACHINE flag: CurrentUser scope.
            bool success = encrypt
                ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!success)
                throw new CryptographicException(Marshal.GetLastWin32Error());
            var result = new byte[output.Length];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            for (int i = 0; i < input.Length; i++)
                Marshal.WriteByte(input.Data, i, 0);
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero)
            {
                for (int i = 0; i < output.Length; i++)
                    Marshal.WriteByte(output.Data, i, 0);
                LocalFree(output.Data);
            }
        }
    }
}



