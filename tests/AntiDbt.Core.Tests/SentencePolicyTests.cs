using AntiDbt.Core;

internal static class SentencePolicyTests
{
    public static void Run()
    {
        static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        foreach (int seconds in new[] { 30, 120 })
        foreach (bool resetSupported in new[] { true, false })
        {
            var manager = new StreamingSentenceManager(new(), "en", resetSupported);
            var finals = new List<AsrResult>();
            var seen = new Dictionary<string, int>();
            var words = new List<string>();
            string native = "";
            for (int i = 1; i <= seconds * 10; i++)
            {
                string word = $"word{i}";
                words.Add(word);
                native += " " + word;
                var result = manager.Observe(native, TimeSpan.FromMilliseconds(i * 100), TimeSpan.Zero);
                foreach (var row in result.Results)
                {
                    Check(!row.IsDeleted && row.Text.Length > 0, "manager emitted a deletion/empty placeholder");
                    Check(!seen.TryGetValue(row.SegmentId, out int revision) || row.Revision > revision, "revision regressed");
                    seen[row.SegmentId] = row.Revision;
                    if (row.IsFinal) finals.Add(row);
                }
                if (result.ResetRecognizer) native = "";
            }
            finals.AddRange(manager.Observe(native, TimeSpan.FromSeconds(seconds), TimeSpan.Zero, inputFinished: true).Results);
            Check(finals.Count >= seconds / 9, "continuous speech did not keep generating segments");
            Check(finals.All(x => x.IsFinal && (x.AudioEnd - x.AudioStart).TotalSeconds <= 9.001), "unbounded segment duration");
            string actual = string.Join(" ", finals.Select(x => x.Text)).ToLowerInvariant();
            Check(actual == string.Join(" ", words), "a rapid continuous word was dropped or repeated across a reset");
            var store = new SubtitleStore();
            long session = store.BeginSession();
            foreach (var final in finals) store.ApplySource(session, final);
            foreach (var final in finals.AsEnumerable().Reverse()) store.ApplyTranslation(session, final.SegmentId, final.Revision, "translated " + final.SegmentId);
            Check(store.Snapshot().Select(x => x.SourceText).SequenceEqual(finals.Select(x => x.Text)), "out-of-order MT changed confirmed source history");
        }
        var paused = new StreamingSentenceManager(new(), "en");
        var splitStore = new SubtitleStore();
        long splitSession = splitStore.BeginSession();
        splitStore.ApplySource(splitSession, new("a", 1, "alpha be", false, TimeSpan.Zero, TimeSpan.FromSeconds(5), "en"));
        splitStore.ApplyTranslation(splitSession, "a", 1, "ALPHA-BE");
        splitStore.ApplySources(splitSession, new[] {
            new AsrResult("a", 2, "alpha", true, TimeSpan.Zero, TimeSpan.FromSeconds(4.5), "en"),
            new AsrResult("b", 1, "be", false, TimeSpan.FromSeconds(4.5), TimeSpan.FromSeconds(5.1), "en") });
        splitStore.ApplyTranslation(splitSession, "b", 1, "BE");
        Check(SubtitleProjection.Compact(splitStore.Snapshot()).Translation == "ALPHA-BE", "new tail MT duplicated an older full-sentence fallback");
        splitStore.ApplyTranslation(splitSession, "a", 2, "ALPHA");
        Check(SubtitleProjection.Compact(splitStore.Snapshot()).Translation == "ALPHA BE", "sentence split did not atomically hand off both exact translations");
        var partial = paused.Observe("speech without punctuation", TimeSpan.FromSeconds(1), TimeSpan.Zero).Results.Single();
        var pauseFinal = paused.Observe("speech without punctuation", TimeSpan.FromMilliseconds(1500), TimeSpan.FromMilliseconds(500));
        Check(pauseFinal.Boundary == "silence" && pauseFinal.Results.Single().IsFinal, "natural pause not segmented");
        var next = paused.Observe("next sentence", TimeSpan.FromSeconds(2), TimeSpan.Zero).Results.Single();
        Check(next.SegmentId != partial.SegmentId, "next sentence reused the confirmed ID");
        var emptyFlush = paused.Observe("", TimeSpan.FromSeconds(2.2), TimeSpan.Zero, inputFinished: true);
        Check(emptyFlush.Results.Single().Text == "next sentence", "empty EOF swallowed an existing Partial");

        var stable = new StreamingSentenceManager(new(), "en");
        stable.Observe("a stable semantic boundary", TimeSpan.FromSeconds(4.7), TimeSpan.Zero);
        Check(stable.Observe("a stable semantic boundary", TimeSpan.FromSeconds(5.1), TimeSpan.Zero).Boundary == "stable-boundary", "preferred five second stable cut missing");
        var punctuation = new StreamingSentenceManager(new(), "en");
        punctuation.Observe("This is complete.", TimeSpan.FromSeconds(1), TimeSpan.Zero);
        Check(punctuation.Observe("This is complete.", TimeSpan.FromSeconds(1.4), TimeSpan.Zero).Boundary == "punctuation", "stable punctuation ignored");
        var settings = SentenceConfiguration.From(new() { Options = new() { ["sentenceMaxSeconds"] = "3", ["sentenceMaxChars"] = "60" } });
        Check(settings.MaximumSeconds == 3 && settings.PreferredSeconds == 3 && settings.MaximumCharacters == 60, "configurable limits ignored");
        try { SentenceConfiguration.From(new() { Options = new() { ["sentenceMaxSeconds"] = "NaN" } }); throw new Exception("invalid config accepted"); }
        catch (ProviderException ex) { Check(ex.Kind == ProviderErrorKind.Configuration, "invalid config misclassified"); }
    }
}
