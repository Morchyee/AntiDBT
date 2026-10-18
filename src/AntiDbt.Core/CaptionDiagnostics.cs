using System.Text.Json;
using System.Text.RegularExpressions;

namespace AntiDbt.Core;

public static class CaptionErrorCategory
{
    public const string AsrTranscriptionError = "AsrTranscriptionError";
    public const string SegmentationCutError = "SegmentationCutError";
    public const string MissingContext = "MissingContext";
    public const string ModelTranslationError = "ModelTranslationError";
    public const string ProjectionMappingError = "ProjectionMappingError";
}

public sealed record CaptionDiagnostic(
    DateTimeOffset At, string Stage, string Event, long Session, string Segment,
    int Revision, int SourceLength, int TranslationLength, int Rows, string? Detail, double? AudioEnd,
    string? SourceText = null, string? TranslationText = null, string? Context = null, string? Lookahead = null, string? ErrorCategory = null);

public static class CaptionDiagnostics
{
    private static readonly object Sync = new();
    private static readonly Queue<CaptionDiagnostic> Entries = new();
    private static string? _file;
    private static readonly Regex SecretRegex = new(
        @"(?:api[_-]?key|bearer|token|secret|password)[\s=:""']+([A-Za-z0-9_\-\.]{8,})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string? Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return SecretRegex.Replace(text, "***REDACTED***");
    }

    public static void EnableFile(string path)
    {
        lock (Sync)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            _file = path;
        }
    }
    public static void Record(
        string stage, string action, long session = 0, string segment = "", int revision = 0,
        int sourceLength = 0, int translationLength = 0, int rows = 0, string? detail = null, double? audioEnd = null,
        string? sourceText = null, string? translationText = null, string? context = null, string? lookahead = null, string? errorCategory = null)
    {
        var entry = new CaptionDiagnostic(
            DateTimeOffset.UtcNow, stage, action, session, segment, revision,
            sourceLength, translationLength, rows,
            Sanitize(detail), audioEnd,
            Sanitize(sourceText), Sanitize(translationText), Sanitize(context), Sanitize(lookahead), errorCategory);

        lock (Sync)
        {
            if (Entries.Count >= 2048) Entries.Dequeue();
            Entries.Enqueue(entry);
            if (_file is null) return;
            try
            {
                if (File.Exists(_file) && new FileInfo(_file).Length >= 2 * 1024 * 1024)
                    File.Move(_file, _file + ".previous", true);
                File.AppendAllText(_file, JsonSerializer.Serialize(entry) + Environment.NewLine);
            }
            catch (IOException) { } // Diagnostics must never interrupt captions.
            catch (UnauthorizedAccessException) { }
        }
    }
    public static IReadOnlyList<CaptionDiagnostic> Snapshot() { lock (Sync) return Entries.ToArray(); }
}
