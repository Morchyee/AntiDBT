using System.Threading.Channels;
using System.Diagnostics;
using AntiDbt.Core;
using static Phase555TestSupport;

internal static class TranscriptOrderTests
{
    public static async Task<List<Phase555TestResult>> RunAllAsync(string? suiteFilter, string? caseFilter)
    {
        var list = new List<Phase555TestResult>();

        async Task RunCase(string name, string suite, Func<Task> test)
        {
            if (suiteFilter != null && suiteFilter != suite)
                return;
            if (caseFilter != null && name != caseFilter && !name.StartsWith(caseFilter + "/"))
                return;
            try
            {
                await test();
                list.Add(new Phase555TestResult(name, suite, true, null));
            }
            catch (Exception ex)
            {
                list.Add(new Phase555TestResult(name, suite, false, ex.Message));
            }
        }

        void RunSync(string name, string suite, Action test) => RunCase(name, suite, () => { test(); return Task.CompletedTask; }).GetAwaiter().GetResult();

        // --- AQ-03 Tests ---

        // O01
        await RunCase("SelectsContextInAudioOrder", "AQ03", async () =>
        {
            var store = new SubtitleStore();
            var session = store.BeginSession();
            var a1 = store.ApplySource(session, MakeAsr("A", 1, "alpha.", true, 0, 1000))!;
            var b1 = store.ApplySource(session, MakeAsr("B", 1, "beta.", true, 1000, 2000))!;

            using var fake = new ScriptedTranslator();
            var config = new TranslationConfiguration
            {
                Provider = "DeepL",
                DebounceMilliseconds = 0,
                ContextSentences = 4,
                EnableContextAwareTranslation = true
            };
            await using var scheduler = new TranslationScheduler(store, fake, config, "zh");

            scheduler.Schedule(b1);
            var env = await fake.NextAsync();
            if (env.Request.Context.Count != 1 || env.Request.Context[0] != "alpha.")
                throw new Exception($"Expected context ['alpha.'], but got: [{string.Join(", ", env.Request.Context)}]");
            if (env.Request.Lookahead != null)
                throw new Exception($"Expected lookahead null, but got: '{env.Request.Lookahead}'");

            fake.Complete(env, "B译");
            await scheduler.FlushAsync(CancellationToken.None);
        });

        // O02
        await RunCase("EarlierCorrectionCannotUseLaterFinalAsContext", "AQ03", async () =>
        {
            var store = new SubtitleStore();
            var session = store.BeginSession();
            var a1 = store.ApplySource(session, MakeAsr("A", 1, "alpha.", true, 0, 1000))!;
            var b1 = store.ApplySource(session, MakeAsr("B", 1, "beta.", true, 1000, 2000))!;
            var a2 = store.ApplySource(session, MakeAsr("A", 2, "alpha corrected.", true, 0, 1000))!;

            using var fake = new ScriptedTranslator();
            var config = new TranslationConfiguration
            {
                Provider = "DeepL",
                DebounceMilliseconds = 0,
                ContextSentences = 4,
                EnableContextAwareTranslation = true
            };
            await using var scheduler = new TranslationScheduler(store, fake, config, "zh");

            // Schedule A2 (with max receipt override to simulate late arrival)
            scheduler.Schedule(a2 with { ReceivedTimestamp = long.MaxValue });

            var env = await fake.NextAsync();
            if (env.Request.Context.Count != 0)
                throw new Exception($"Earlier correction A2 should have empty context, but got: [{string.Join(", ", env.Request.Context)}]");
            if (env.Request.Lookahead != "beta.")
                throw new Exception($"Earlier correction A2 should have lookahead 'beta.', but got: '{env.Request.Lookahead}'");

            fake.Complete(env, "A纠正译");
            await scheduler.FlushAsync(CancellationToken.None);
        });

        // O03
        await RunCase("OutOfOrderReceptionStillFindsPredecessorAndLookahead", "AQ03", async () =>
        {
            // B arrives first, A arrives second
            var store = new SubtitleStore();
            var session = store.BeginSession();
            var b1 = store.ApplySource(session, MakeAsr("B", 1, "beta.", true, 1000, 2000))!;
            var a1 = store.ApplySource(session, MakeAsr("A", 1, "alpha.", true, 0, 1000))!;

            using var fake = new ScriptedTranslator();
            var config = new TranslationConfiguration
            {
                Provider = "DeepL",
                DebounceMilliseconds = 0,
                ContextSentences = 4,
                EnableContextAwareTranslation = true
            };
            await using var scheduler = new TranslationScheduler(store, fake, config, "zh");

            // Test scheduling B
            scheduler.Schedule(b1 with { ReceivedTimestamp = 1 });
            var envB = await fake.NextAsync();
            if (envB.Request.Context.Count != 1 || envB.Request.Context[0] != "alpha.")
                throw new Exception($"B1 should find earlier A1 as context despite arriving first. Got: [{string.Join(", ", envB.Request.Context)}]");
            if (envB.Request.Lookahead != null)
                throw new Exception($"B1 lookahead should be null, got: '{envB.Request.Lookahead}'");
            fake.Complete(envB, "B译");
            await scheduler.FlushAsync(CancellationToken.None);

            // Test scheduling A
            scheduler.Schedule(a1 with { ReceivedTimestamp = long.MaxValue });
            var envA = await fake.NextAsync();
            if (envA.Request.Context.Count != 0)
                throw new Exception($"A1 should have empty context. Got: [{string.Join(", ", envA.Request.Context)}]");
            if (envA.Request.Lookahead != "beta.")
                throw new Exception($"A1 should find later B1 as lookahead. Got: '{envA.Request.Lookahead}'");
            fake.Complete(envA, "A译");
            await scheduler.FlushAsync(CancellationToken.None);
        });

        // O04
        await RunCase("SameAudioStartUsesOrdinalIdsAcrossArrivalOrders", "AQ03", async () =>
        {
            var config = new TranslationConfiguration
            {
                Provider = "DeepL",
                DebounceMilliseconds = 0,
                ContextSentences = 4,
                EnableContextAwareTranslation = true
            };

            // Permutation 1: [Z1, A1, M1]
            var store1 = new SubtitleStore();
            var s1 = store1.BeginSession();
            store1.ApplySource(s1, MakeAsr("Z", 1, "zed.", true, 1000, 2000));
            store1.ApplySource(s1, MakeAsr("A", 1, "aye.", true, 1000, 2000));
            var m1_1 = store1.ApplySource(s1, MakeAsr("M", 1, "em.", true, 1000, 2000))!;

            var snap1 = store1.Snapshot();
            var ids1 = snap1.Select(x => x.SegmentId).ToArray();
            if (!ids1.SequenceEqual(new[] { "A", "M", "Z" }))
                throw new Exception($"Permutation 1: Expected order [A, M, Z], but was: [{string.Join(", ", ids1)}]");

            using var fake1 = new ScriptedTranslator();
            await using var scheduler1 = new TranslationScheduler(store1, fake1, config, "zh");

            scheduler1.Schedule(m1_1);
            var env1 = await fake1.NextAsync();
            if (env1.Request.Context.Count != 1 || env1.Request.Context[0] != "aye.")
                throw new Exception($"Permutation 1: M context should be ['aye.'], got: [{string.Join(", ", env1.Request.Context)}]");
            if (env1.Request.Lookahead != "zed.")
                throw new Exception($"Permutation 1: M lookahead should be 'zed.', got: '{env1.Request.Lookahead}'");

            fake1.Complete(env1, "M译");
            await scheduler1.FlushAsync(CancellationToken.None);

            // Permutation 2: [M1, Z1, A1]
            var store2 = new SubtitleStore();
            var s2 = store2.BeginSession();
            var m1_2 = store2.ApplySource(s2, MakeAsr("M", 1, "em.", true, 1000, 2000))!;
            store2.ApplySource(s2, MakeAsr("Z", 1, "zed.", true, 1000, 2000));
            store2.ApplySource(s2, MakeAsr("A", 1, "aye.", true, 1000, 2000));

            var snap2 = store2.Snapshot();
            var ids2 = snap2.Select(x => x.SegmentId).ToArray();
            if (!ids2.SequenceEqual(new[] { "A", "M", "Z" }))
                throw new Exception($"Permutation 2: Expected order [A, M, Z], but was: [{string.Join(", ", ids2)}]");

            using var fake2 = new ScriptedTranslator();
            await using var scheduler2 = new TranslationScheduler(store2, fake2, config, "zh");

            scheduler2.Schedule(m1_2);
            var env2 = await fake2.NextAsync();
            if (env2.Request.Context.Count != 1 || env2.Request.Context[0] != "aye.")
                throw new Exception($"Permutation 2: M context should be ['aye.'], got: [{string.Join(", ", env2.Request.Context)}]");
            if (env2.Request.Lookahead != "zed.")
                throw new Exception($"Permutation 2: M lookahead should be 'zed.', got: '{env2.Request.Lookahead}'");

            fake2.Complete(env2, "M译");
            await scheduler2.FlushAsync(CancellationToken.None);
        });

        // O05
        RunSync("AudioEndRevisionDoesNotReorderEqualStarts", "AQ03", () =>
        {
            var store = new SubtitleStore();
            var session = store.BeginSession();
            store.ApplySource(session, MakeAsr("Z", 1, "zed.", true, 1000, 1500));
            store.ApplySource(session, MakeAsr("A", 1, "aye.", true, 1000, 3000));
            store.ApplySource(session, MakeAsr("A", 2, "aye.", true, 1000, 1200));
            store.ApplySource(session, MakeAsr("Z", 2, "zed.", true, 1000, 5000));

            var snap = store.Snapshot();
            var ids = snap.Select(x => x.SegmentId).ToArray();
            if (!ids.SequenceEqual(new[] { "A", "Z" }))
                throw new Exception($"Expected order [A, Z], but was: [{string.Join(", ", ids)}]");
        });

        // O06
        await RunCase("OverlappingAudioStillHasStableDirectionalContext", "AQ03", async () =>
        {
            var store = new SubtitleStore();
            var session = store.BeginSession();
            store.ApplySource(session, MakeAsr("B", 1, "beta.", true, 1000, 3000));
            var a1 = store.ApplySource(session, MakeAsr("A", 1, "alpha.", true, 0, 2000))!;
            var b2 = store.ApplySource(session, MakeAsr("B", 2, "beta.", true, 1000, 3500))!;

            var snap = store.Snapshot();
            var ids = snap.Select(x => x.SegmentId).ToArray();
            if (!ids.SequenceEqual(new[] { "A", "B" }))
                throw new Exception($"Overlapping audio A[0,2000] and B[1000,3500] should keep A before B. Got: [{string.Join(", ", ids)}]");

            using var fake = new ScriptedTranslator();
            var config = new TranslationConfiguration
            {
                Provider = "DeepL",
                DebounceMilliseconds = 0,
                ContextSentences = 4,
                EnableContextAwareTranslation = true
            };
            await using var scheduler = new TranslationScheduler(store, fake, config, "zh");

            // Schedule B: should find A as context, lookahead null
            scheduler.Schedule(b2);
            var envB = await fake.NextAsync();
            if (envB.Request.Context.Count != 1 || envB.Request.Context[0] != "alpha.")
                throw new Exception($"B context should be ['alpha.'], got: [{string.Join(", ", envB.Request.Context)}]");
            if (envB.Request.Lookahead != null)
                throw new Exception($"B lookahead should be null, got: '{envB.Request.Lookahead}'");
            fake.Complete(envB, "B译");
            await scheduler.FlushAsync(CancellationToken.None);

            // Schedule A: should have empty context, lookahead B
            scheduler.Schedule(a1);
            var envA = await fake.NextAsync();
            if (envA.Request.Context.Count != 0)
                throw new Exception($"A context should be empty, got: [{string.Join(", ", envA.Request.Context)}]");
            if (envA.Request.Lookahead != "beta.")
                throw new Exception($"A lookahead should be 'beta.', got: '{envA.Request.Lookahead}'");
            fake.Complete(envA, "A译");
            await scheduler.FlushAsync(CancellationToken.None);
        });

        // O07
        RunSync("PartialFinalAndCorrectionsRetainOriginalOrderIdentity", "AQ03", () =>
        {
            var store = new SubtitleStore();
            var session = store.BeginSession();
            store.ApplySource(session, MakeAsr("A", 1, "al", false, 0, 500));
            store.ApplySource(session, MakeAsr("B", 1, "beta.", true, 1000, 2000));
            store.ApplySource(session, MakeAsr("A", 2, "alpha", false, 2000, 2500)); // provider shifts start time
            store.ApplySource(session, MakeAsr("A", 3, "alpha.", true, 2000, 3000));

            var snap = store.Snapshot();
            var ids = snap.Select(x => x.SegmentId).ToArray();
            if (ids.Length != 2 || ids[0] != "A" || ids[1] != "B")
                throw new Exception($"A should retain its original ordering before B despite start time revision. Got: [{string.Join(", ", ids)}]");

            if (snap[0].AudioStart != TimeSpan.FromMilliseconds(2000))
                throw new Exception($"A.AudioStart should be updated to 2000ms, was {snap[0].AudioStart}");
            if (snap[0].TranscriptStart != TimeSpan.FromMilliseconds(0))
                throw new Exception($"A.TranscriptStart should remain 0ms, was {snap[0].TranscriptStart}");
        });

        // O08
        RunSync("NewReplacementIdsReceiveNewAudioAnchors", "AQ03", () =>
        {
            var store = new SubtitleStore();
            var session = store.BeginSession();
            store.ApplySource(session, MakeAsr("P", 1, "parent", false, 0, 2000));
            store.ApplySources(session, new[]
            {
                MakeAsr("P", 2, "", true, 0, 2000), // tombstone
                MakeAsr("U", 1, "you", false, 0, 1000),
                MakeAsr("V", 1, "vee.", true, 1000, 2000)
            });
            store.ApplySource(session, MakeAsr("C", 1, "see.", true, 2000, 3000));

            var snap = store.Snapshot();
            var ids = snap.Select(x => x.SegmentId).ToArray();
            if (!ids.SequenceEqual(new[] { "U", "V", "C" }))
                throw new Exception($"Expected active segments [U, V, C], got: [{string.Join(", ", ids)}]");
        });

        // O09
        await RunCase("CorrectedEarlierFinalCannotTriggerBackwardRevision", "AQ03", async () =>
        {
            var store = new SubtitleStore();
            var session = store.BeginSession();
            var a1 = store.ApplySource(session, MakeAsr("A", 1, "alpha.", true, 0, 1000))!;
            var b1 = store.ApplySource(session, MakeAsr("B", 1, "we can", true, 1000, 2000))!; // incomplete clause
            var a2 = store.ApplySource(session, MakeAsr("A", 2, "alpha corrected.", true, 0, 1000))!;

            using var fake = new ScriptedTranslator
            {
                AutoResponder = req => "译:" + req.Text
            };
            var config = new TranslationConfiguration
            {
                Provider = "DeepL",
                DebounceMilliseconds = 0,
                ContextSentences = 4,
                EnableContextAwareTranslation = true
            };
            await using var scheduler = new TranslationScheduler(store, fake, config, "zh");

            scheduler.Schedule(a2 with { ReceivedTimestamp = long.MaxValue });

            await scheduler.FlushAsync(CancellationToken.None);

            // Verify no backwards revision was triggered for B
            var captured = fake.CapturedRequests;
            foreach (var req in captured)
            {
                if (req.Request.SegmentId == "B" && req.Request.Lookahead == a2.SourceText)
                    throw new Exception("Backward contextual revision was triggered for B using earlier corrected segment A2!");
            }
        });

        // O10
        await RunCase("ContextualRevisionUsesStablePreviousHistory", "AQ03", async () =>
        {
            var store = new SubtitleStore();
            var session = store.BeginSession();
            store.ApplySource(session, MakeAsr("H", 1, "history.", true, 0, 500));
            store.ApplySource(session, MakeAsr("P", 1, "we can", true, 500, 1000));
            store.ApplySource(session, MakeAsr("H", 2, "history corrected.", true, 0, 500));

            using var fake = new ScriptedTranslator
            {
                AutoResponder = req => "译:" + req.Text
            };
            var config = new TranslationConfiguration
            {
                Provider = "DeepL",
                DebounceMilliseconds = 0,
                ContextSentences = 4,
                EnableContextAwareTranslation = true
            };
            await using var scheduler = new TranslationScheduler(store, fake, config, "zh");

            var c1 = store.ApplySource(session, MakeAsr("C", 1, "continue.", true, 1000, 2000))!;
            scheduler.Schedule(c1 with { ReceivedTimestamp = long.MaxValue });

            await scheduler.FlushAsync(CancellationToken.None);

            var captured = fake.CapturedRequests;
            var pReq = captured.FirstOrDefault(x => x.Request.SegmentId == "P");
            if (pReq is null)
                throw new Exception("No contextual revision request was dispatched for P!");
            if (pReq.Request.Context.Count == 0 || !pReq.Request.Context.Contains("history corrected."))
                throw new Exception($"P contextual request should contain 'history corrected.', got: [{string.Join(", ", pReq.Request.Context)}]");
            if (pReq.Request.Lookahead != "continue.")
                throw new Exception($"P lookahead should be 'continue.', got: '{pReq.Request.Lookahead}'");
        });

        // O11
        RunSync("ProjectionIgnoresReceiptGapsAndLateCorrections", "AQ03", () =>
        {
            long freq = Stopwatch.Frequency;
            var store = new SubtitleStore();
            var s = store.BeginSession();
            var a = store.ApplySource(s, MakeAsr("A", 1, "alpha", true, 0, 1000))! with { ReceivedTimestamp = 1 };
            var b = store.ApplySource(s, MakeAsr("B", 1, "beta", true, 1000, 2000))! with { ReceivedTimestamp = 1 + 10 * freq };

            var list = new[] { a, b };
            var compact = SubtitleProjection.Compact(list);
            if (compact.Source != "alpha beta")
                throw new Exception($"Compact view truncated text because of receipt gap: was '{compact.Source}', expected 'alpha beta'");

            var expanded = SubtitleProjection.Expanded(list);
            if (expanded.Count != 1)
                throw new Exception($"Expanded view split into {expanded.Count} paragraphs because of receipt gap, expected 1.");
        });

        // O12
        RunSync("ProjectionUsesRealAudioSilenceAndPreservesGroups", "AQ03", () =>
        {
            var store = new SubtitleStore();
            var s = store.BeginSession();
            var a = store.ApplySource(s, MakeAsr("A", 1, "alpha", true, 0, 1000))! with { ReceivedTimestamp = 100 };
            var b = store.ApplySource(s, MakeAsr("B", 1, "beta", true, 7000, 8000))! with { ReceivedTimestamp = 101 };

            // Ungrouped with 6s audio gap -> should split
            var compact = SubtitleProjection.Compact(new[] { a, b });
            if (compact.Source != "beta")
                throw new Exception($"Ungrouped Compact with 6s audio gap should show only recent 'beta', was '{compact.Source}'");

            var expanded = SubtitleProjection.Expanded(new[] { a, b });
            if (expanded.Count != 2)
                throw new Exception($"Ungrouped Expanded with 6s audio gap should produce 2 paragraphs, got {expanded.Count}");

            // Grouped with same group ID -> must NOT split despite audio gap!
            var ag = a with { TranslationDisplayGroup = "group1", LastVisibleTranslation = "GROUP" };
            var bg = b with { TranslationDisplayGroup = "group1", LastVisibleTranslation = "" };

            var compactG = SubtitleProjection.Compact(new[] { ag, bg });
            if (!compactG.Source.Contains("alpha") || !compactG.Source.Contains("beta"))
                throw new Exception($"Grouped Compact should preserve all members, was '{compactG.Source}'");

            var expandedG = SubtitleProjection.Expanded(new[] { ag, bg });
            if (expandedG.Count != 1)
                throw new Exception($"Grouped Expanded must remain in single paragraph, got {expandedG.Count}");
        });

        // O13
        RunSync("CumulativeAudioSilenceAndInvalidTimestampsAreHandledConservatively", "AQ03", () =>
        {
            var store = new SubtitleStore();
            var s = store.BeginSession();

            // 1. Forward Overlapping Audio:
            // A: 0-20s "a"
            // B: 1-2s "b"
            // C: 8-9s "c"
            // A covers [0, 20s], so no true silence exists between 0 and 20s!
            var a = store.ApplySource(s, MakeAsr("A", 1, "a", true, 0, 20000))!;
            var b = store.ApplySource(s, MakeAsr("B", 1, "b", true, 1000, 2000))!;
            var c = store.ApplySource(s, MakeAsr("C", 1, "c", true, 8000, 9000))!;

            var list = new[] { a, b, c };

            // Compact: should NOT truncate to "c" or "b c"
            var compact = SubtitleProjection.Compact(list);
            if (compact.Source != "a b c")
                throw new Exception($"Compact falsely truncated overlapping audio to '{compact.Source}', expected 'a b c'");

            // Expanded: should NOT split into multiple paragraphs
            var expanded = SubtitleProjection.Expanded(list);
            if (expanded.Count != 1 || expanded[0].Source != "a b c")
                throw new Exception($"Expanded falsely split overlapping audio into {expanded.Count} paragraphs, expected 1 paragraph with 'a b c'");

            // 2. Silence detection after cumulative coverage:
            // Add segment D with true silence gap: 26-27s (gap from cumulative 20s to 26s is 6s >= 5s)
            var d = store.ApplySource(s, MakeAsr("D", 1, "d", true, 26000, 27000))!;
            var listWithD = new[] { a, b, c, d };

            var compactWithD = SubtitleProjection.Compact(listWithD);
            if (compactWithD.Source != "d")
                throw new Exception($"Compact after true 6s silence should show only recent 'd', was '{compactWithD.Source}'");

            var expandedWithD = SubtitleProjection.Expanded(listWithD);
            if (expandedWithD.Count != 2 || expandedWithD[0].Source != "a b c" || expandedWithD[1].Source != "d")
                throw new Exception($"Expanded after true 6s silence should split into 2 paragraphs ('a b c' and 'd'), got {expandedWithD.Count}");

            // 3. P2-01: Unknown audio boundary sandwich: A=[0, 1s], B=[0, 0s] (unknown), C=[10, 11s]
            // An unknown segment B must NOT allow silence to be inferred across it!
            var unkA = store.ApplySource(s, MakeAsr("UA", 1, "a", true, 0, 1000))!;
            var unkB = store.ApplySource(s, MakeAsr("UB", 1, "b", true, 0, 0))!; // unknown time
            var unkC = store.ApplySource(s, MakeAsr("UC", 1, "c", true, 10000, 11000))!;

            var unkList = new[] { unkA, unkB, unkC };
            var unkCompact = SubtitleProjection.Compact(unkList);
            if (unkCompact.Source != "a b c")
                throw new Exception($"Compact dropped content across unknown boundary: was '{unkCompact.Source}', expected 'a b c'");

            var unkExpanded = SubtitleProjection.Expanded(unkList);
            if (unkExpanded.Count != 1 || unkExpanded[0].Source != "a b c")
                throw new Exception($"Expanded split across unknown boundary into {unkExpanded.Count} paragraphs, expected 1 paragraph with 'a b c'");

            // 4. Resumption of silence detection after unknown boundary:
            // Now add UD=[20, 21s] after UC=[10, 11s].
            // UC establishes a known audio anchor [10, 11s]. The gap between 11s and 20s is 9s >= 5s.
            // Silence detection MUST resume normally after UC!
            var unkD = store.ApplySource(s, MakeAsr("UD", 1, "d", true, 20000, 21000))!;
            var unkListWithUD = new[] { unkA, unkB, unkC, unkD };

            var unkCompactWithUD = SubtitleProjection.Compact(unkListWithUD);
            if (unkCompactWithUD.Source != "d")
                throw new Exception($"Compact after resumed silence should show only 'd', was '{unkCompactWithUD.Source}'");

            var unkExpandedWithUD = SubtitleProjection.Expanded(unkListWithUD);
            if (unkExpandedWithUD.Count != 2 || unkExpandedWithUD[0].Source != "a b c" || unkExpandedWithUD[1].Source != "d")
                throw new Exception($"Expanded after resumed silence should have 2 paragraphs ('a b c' and 'd'), got {unkExpandedWithUD.Count}");

            // 5. Exact threshold verification:
            // Compact threshold: 5.0s
            var tA = store.ApplySource(s, MakeAsr("TA", 1, "ta", true, 0, 1000))!;
            var tB_below = store.ApplySource(s, MakeAsr("TB1", 1, "tb", true, 5900, 6900))!; // gap = 4.9s < 5.0s
            var compactBelow = SubtitleProjection.Compact(new[] { tA, tB_below });
            if (compactBelow.Source != "ta tb")
                throw new Exception($"Compact at 4.9s gap must not split: was '{compactBelow.Source}', expected 'ta tb'");

            var tB_above = store.ApplySource(s, MakeAsr("TB2", 1, "tb", true, 6100, 7100))!; // gap = 5.1s > 5.0s
            var compactAbove = SubtitleProjection.Compact(new[] { tA, tB_above });
            if (compactAbove.Source != "tb")
                throw new Exception($"Compact at 5.1s gap must split: was '{compactAbove.Source}', expected 'tb'");

            // Expanded threshold: 2.5s
            var eA = store.ApplySource(s, MakeAsr("EA", 1, "ea", true, 0, 1000))!;
            var eB_below = store.ApplySource(s, MakeAsr("EB1", 1, "eb", true, 3400, 4400))!; // gap = 2.4s < 2.5s
            var expBelow = SubtitleProjection.Expanded(new[] { eA, eB_below });
            if (expBelow.Count != 1 || expBelow[0].Source != "ea eb")
                throw new Exception($"Expanded at 2.4s gap must not split: count {expBelow.Count}, source '{expBelow[0].Source}'");

            var eB_above = store.ApplySource(s, MakeAsr("EB2", 1, "eb", true, 3600, 4600))!; // gap = 2.6s > 2.5s
            var expAbove = SubtitleProjection.Expanded(new[] { eA, eB_above });
            if (expAbove.Count != 2 || expAbove[0].Source != "ea" || expAbove[1].Source != "eb")
                throw new Exception($"Expanded at 2.6s gap must split into 2 paragraphs: count {expAbove.Count}");

            // 6. Conservative handling of invalid/zero-length/inverted/extreme timestamps:
            var invalidZero = a with { SegmentId = "Z1", SourceText = "zero", AudioStart = TimeSpan.FromSeconds(5), AudioEnd = TimeSpan.FromSeconds(5) };
            var invalidInverted = a with { SegmentId = "Z2", SourceText = "inv", AudioStart = TimeSpan.FromSeconds(15), AudioEnd = TimeSpan.FromSeconds(5) };
            var invalidNegative = a with { SegmentId = "Z3", SourceText = "neg", AudioStart = TimeSpan.FromSeconds(-5), AudioEnd = TimeSpan.FromSeconds(5) };
            var extremeOverflow = a with { SegmentId = "Z4", SourceText = "ext", AudioStart = TimeSpan.MaxValue, AudioEnd = TimeSpan.MinValue };

            var abnormalList = new[] { a, invalidZero, invalidInverted, invalidNegative, extremeOverflow };

            // Exact assertion: None of these should throw TimeSpan overflow or infer false silence!
            var compactInvalid = SubtitleProjection.Compact(abnormalList);
            if (compactInvalid.Source != "a zero inv neg ext")
                throw new Exception($"Compact on abnormal timestamps must preserve all text: was '{compactInvalid.Source}', expected 'a zero inv neg ext'");

            var expandedInvalid = SubtitleProjection.Expanded(abnormalList);
            if (expandedInvalid.Count != 1 || expandedInvalid[0].Source != "a zero inv neg ext")
                throw new Exception($"Expanded on abnormal timestamps must remain single paragraph: count {expandedInvalid.Count}, source '{expandedInvalid[0].Source}'");
        });

        // --- Regression Tests ---

        // R01
        RunSync("StickySourceCorrectionAndGroupedExactHandoffRemainVisible", "Regression", () =>
        {
            var store = new SubtitleStore();
            var session = store.BeginSession();
            store.ApplySource(session, MakeAsr("A", 1, "Turn left", false, 0, 1000));
            store.ApplyTranslation(session, "A", 1, "向左转");

            // Incompatible correction
            store.ApplySource(session, MakeAsr("A", 2, "Turn right", false, 0, 1000));
            var snap1 = store.Snapshot();
            if (snap1[0].TranslationText != "")
                throw new Exception("Incompatible correction should clear independent translation text.");
            var compact1 = SubtitleProjection.Compact(snap1);
            if (compact1.Translation != "向左转")
                throw new Exception("Sticky translation should remain visible as fallback.");

            store.ApplyTranslation(session, "A", 2, "向右转");
            var snap2 = store.Snapshot();
            if (snap2[0].TranslationText != "向右转")
                throw new Exception("New exact translation should be applied.");
        });

        // R02
        await RunCase("PipelinePublishesSplitBatchBeforeSchedulingMembers", "Regression", async () =>
        {
            // Verify both completion orders (U then V, and V then U) to ensure zero race conditions
            await RunR02PipelineScenarioAsync(completionOrder: 0);
            await RunR02PipelineScenarioAsync(completionOrder: 1);
        });

        // R03
        await RunCase("FinalPriorityAndLatePreviewRemainRevisionSafe", "Regression", async () =>
        {
            var store = new SubtitleStore();
            var session = store.BeginSession();

            using var fake = new ScriptedTranslator();
            var config = new TranslationConfiguration { DebounceMilliseconds = 0 };
            await using var scheduler = new TranslationScheduler(store, fake, config, "zh");

            var a1 = store.ApplySource(session, MakeAsr("A", 1, "part", false, 0, 1000))!;
            scheduler.Schedule(a1);
            var env1 = await fake.NextAsync();

            var a2 = store.ApplySource(session, MakeAsr("A", 2, "complete.", true, 0, 1000))!;
            scheduler.Schedule(a2);
            var env2 = await fake.NextAsync();

            // Deliver Final first, then old Partial
            fake.Complete(env2, "完成");
            fake.Complete(env1, "部分");
            await scheduler.FlushAsync(CancellationToken.None);

            var snap = store.Snapshot();
            if (snap[0].TranslationText != "完成")
                throw new Exception($"Final translation should take precedence over late partial. Was: '{snap[0].TranslationText}'");
        });

        // R04
        RunSync("HistoryAndProjectionContainEveryCurrentSourceExactlyOnce", "Regression", () =>
        {
            var store = new SubtitleStore();
            var frames = new List<IReadOnlyList<CaptionSegment>>();
            store.Changed += () =>
            {
                var snap = store.Snapshot();
                frames.Add(snap);
            };

            // S1:
            // - A1 [0, 1000] "alpha", then corrected to A2 [0, 1000] "alpha corrected" with translation "甲"
            // - B1 [1000, 2000] "beta", with translation "乙" (at revision 1)
            // - E1 [2000, 3000] "error seg", final with TranslationError = "网络超时" (for CanRetry testing)
            var s1 = store.BeginSession();
            store.ApplySource(s1, MakeAsr("A", 1, "alpha", false, 0, 1000));
            store.ApplySource(s1, MakeAsr("B", 1, "beta", true, 1000, 2000));
            store.ApplySource(s1, MakeAsr("E", 1, "error seg", true, 2000, 3000));
            store.ApplySource(s1, MakeAsr("A", 2, "alpha corrected", true, 0, 1000));
            store.ApplyTranslation(s1, "A", 2, "甲");
            store.ApplyTranslation(s1, "B", 1, "乙");
            store.ApplyTranslationError(s1, "E", 1, "网络超时");

            var snapS1 = store.Snapshot();
            var expS1 = SubtitleProjection.Expanded(snapS1, s1);
            if (expS1.Count != 1)
                throw new Exception($"S1 Expanded should be 1 paragraph, got: {expS1.Count}");
            if (expS1[0].Source != "alpha corrected beta error seg")
                throw new Exception($"S1 source mismatch, got: '{expS1[0].Source}'");
            if (expS1[0].TranslationError != "网络超时" || !expS1[0].HasError)
                throw new Exception("S1 paragraph must expose TranslationError and HasError=true");
            if (!expS1[0].CanRetry)
                throw new Exception("S1 paragraph in active session s1 with final error MUST have CanRetry=true");

            // S2: Reconnect session (preserves history, new session id)
            var s2 = store.BeginSession(false);
            if (s2 == s1)
                throw new Exception("New session must have distinct session ID.");

            // S2 parent sentence P1 [10000, 12000] "parent full"
            store.ApplySource(s2, MakeAsr("P", 1, "parent full", false, 10000, 12000));

            // S2 true split handoff: atomic batch replaces P with C1 [10000, 11000] and C2 [11000, 12000]
            store.ApplySources(s2, new[]
            {
                MakeAsr("P", 2, "", false, 10000, 12000, isDeleted: true) with { BatchSize = 3 },
                MakeAsr("C1", 1, "child one", true, 10000, 11000),
                MakeAsr("C2", 1, "child two", true, 11000, 12000)
            });
            store.ApplyTranslation(s2, "C1", 1, "子一");
            store.ApplyTranslation(s2, "C2", 1, "子二");

            // S1 late translation with MATCHING revision (B is revision 1 in S1)
            // If revision check alone were used, revision 1 would match!
            // But because S1 is inactive/old session, it MUST be rejected solely on session isolation!
            bool lateS1Accepted = store.ApplyTranslation(s1, "B", 1, "旧B迟到译");
            if (lateS1Accepted)
                throw new Exception("Late translation for old session must be rejected even when revision matches!");

            // Re-read store snapshot after late translation return
            var snapAfterLate = store.Snapshot();
            var bRow = snapAfterLate.Single(x => x.SessionId == s1 && x.SegmentId == "B");
            if (bRow.TranslationText != "乙")
                throw new Exception($"Old session row B was polluted by late translation! Expected '乙', was '{bRow.TranslationText}'");

            // Frame-by-frame verification on EVERY collected frame
            if (frames.Count == 0)
                throw new Exception("No frames collected via store.Changed!");

            for (int fIdx = 0; fIdx < frames.Count; fIdx++)
            {
                var frame = frames[fIdx];
                if (frame.Count == 0) continue;

                // 1. Snapshot verification: no duplicate IDs within same session
                foreach (var grp in frame.GroupBy(x => (x.SessionId, x.SegmentId)))
                {
                    if (grp.Count() > 1)
                        throw new Exception($"Frame {fIdx} contains duplicate segment ID {grp.Key.SegmentId} in session {grp.Key.SessionId}");
                }

                // 2. Compact projection on this frame:
                var frameCompact = SubtitleProjection.Compact(frame, s2);
                if (string.IsNullOrWhiteSpace(frameCompact.Source))
                    throw new Exception($"Frame {fIdx} produced an unexpected empty Compact projection!");

                // 3. Expanded projection on this frame:
                var frameExpanded = SubtitleProjection.Expanded(frame, s2);
                if (frameExpanded.Count == 0)
                    throw new Exception($"Frame {fIdx} produced zero Expanded paragraphs!");

                foreach (var para in frameExpanded)
                {
                    if (string.IsNullOrWhiteSpace(para.Source))
                        throw new Exception($"Frame {fIdx} produced an Expanded paragraph with empty Source!");
                }
            }

            // Final state assertions under S2:
            var finalSnap = store.Snapshot();
            var finalExp = SubtitleProjection.Expanded(finalSnap, s2);

            // S1 paragraph under S2:
            var pS1UnderS2 = finalExp.FirstOrDefault(p => p.SessionId == s1);
            if (pS1UnderS2 == null)
                throw new Exception("S1 paragraph missing from final projection!");

            // CRITICAL: Prove session isolation protection on CanRetry:
            // Under S1, it was true. Under S2, because it is an old session, CanRetry MUST be false!
            if (pS1UnderS2.CanRetry)
                throw new Exception("S1 paragraph under currentSession=S2 MUST have CanRetry=false even though it has a final error!");

            // Verify S1 paragraph retains its original content and error
            if (!pS1UnderS2.HasError || pS1UnderS2.TranslationError != "网络超时")
                throw new Exception("S1 paragraph must retain its original TranslationError");

            // S2 paragraph under S2:
            var pS2UnderS2 = finalExp.FirstOrDefault(p => p.SessionId == s2);
            if (pS2UnderS2 == null)
                throw new Exception("S2 paragraph missing from final projection!");
            if (pS2UnderS2.Source != "child one child two")
                throw new Exception($"S2 paragraph source mismatch: '{pS2UnderS2.Source}'");
            string expectedS2Trans = SubtitleProjection.JoinTexts(new[] { "子一", "子二" });
            if (pS2UnderS2.Translation != expectedS2Trans)
                throw new Exception($"S2 paragraph translation mismatch: was '{pS2UnderS2.Translation}', expected '{expectedS2Trans}'");
            if (pS2UnderS2.HasError || pS2UnderS2.CanRetry)
                throw new Exception("S2 paragraph should have no errors and CanRetry=false");

            // Compact under S2:
            var finalCompact = SubtitleProjection.Compact(finalSnap, s2);
            // S1 (ended at 3000ms) and S2 (starts at 10000ms) have 7s gap >= 5s silence threshold!
            // Therefore Compact MUST show ONLY S2 content ("child one child two")!
            if (finalCompact.Source != "child one child two")
                throw new Exception($"Final Compact under S2 must contain only current sentence 'child one child two', was: '{finalCompact.Source}'");
            if (finalCompact.Translation != expectedS2Trans)
                throw new Exception($"Final Compact translation mismatch: was '{finalCompact.Translation}', expected '{expectedS2Trans}'");

            // S1 translation cannot contaminate S2
            if (finalCompact.Translation.Contains("甲") || finalCompact.Translation.Contains("乙"))
                throw new Exception("S2 Compact contaminated by S1 translations!");
        });

        return list;
    }

    public static async Task RunR02RepeatsAsync(int count = 30)
    {
        for (int i = 0; i < count; i++)
        {
            int order = i % 2; // Alternate between U-then-V (0) and V-then-U (1)
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await RunR02PipelineScenarioAsync(order);
            sw.Stop();
            Console.WriteLine($"[R02-REPEAT] Iteration {i + 1}/{count} (order: {(order == 0 ? "U-then-V" : "V-then-U")}): PASSED in {sw.ElapsedMilliseconds}ms");
        }
    }

    private static async Task RunR02PipelineScenarioAsync(int completionOrder)
    {
        var audio = new TestAudioSource();
        var asr = new ChannelAsrProvider();
        using var fake = new ScriptedTranslator();
        var store = new SubtitleStore();
        var observedChangedFrames = new List<IReadOnlyList<CaptionSegment>>();
        store.Changed += () =>
        {
            lock (observedChangedFrames)
                observedChangedFrames.Add(store.Snapshot());
        };

        var settings = new AppSettings
        {
            Translation = new() { DebounceMilliseconds = 0 }
        };
        await using var pipeline = new CaptionPipeline(audio, asr, fake, settings, store);
        await pipeline.StartAsync();
        audio.Push();

        // 1. P1 Partial [0, 2000]
        asr.Channel.Writer.TryWrite(MakeAsr("P", 1, "parent prefix", false, 0, 2000));
        var envP1 = await fake.NextAsync(r => r.SegmentId == "P" && r.Revision == 1, TimeSpan.FromSeconds(5));
        fake.Complete(envP1, "原译");

        // Event-driven wait for store to reflect P1 with "原译" (no unbounded polling, no fixed sleep)
        var p1StoreTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void CheckP1()
        {
            if (store.Snapshot().Any(x => x.SegmentId == "P" && x.TranslationText == "原译"))
                p1StoreTcs.TrySetResult(true);
        }
        store.Changed += CheckP1;
        CheckP1();
        using var p1Cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await p1StoreTcs.Task.WaitAsync(p1Cts.Token);
        store.Changed -= CheckP1;

        // 2. P2 Partial [0, 2000] (hold its request in fake translator!)
        asr.Channel.Writer.TryWrite(MakeAsr("P", 2, "parent prefix extended", false, 0, 2000));
        var envP2 = await fake.NextAsync(r => r.SegmentId == "P" && r.Revision == 2, TimeSpan.FromSeconds(5)); // Held!

        // 3. Batch publish: P3 tombstone (BatchSize = 3), U1 Final [0, 1000], V1 Partial [1000, 2000]
        // followed by V2 Final [1000, 2000]
        asr.Channel.Writer.TryWrite(MakeAsr("P", 3, "", false, 0, 2000, isDeleted: true) with { BatchSize = 3 });
        asr.Channel.Writer.TryWrite(MakeAsr("U", 1, "parent", true, 0, 1000));
        asr.Channel.Writer.TryWrite(MakeAsr("V", 1, "prefix", false, 1000, 2000));
        asr.Channel.Writer.TryWrite(MakeAsr("V", 2, "prefix complete", true, 1000, 2000));

        // 4. Dedicated dispatcher with independent completion conditions for U Final, V Final, and V Partial
        var uFinalTcs = new TaskCompletionSource<RequestEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        var vFinalTcs = new TaskCompletionSource<RequestEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        var vPartialTcs = new TaskCompletionSource<RequestEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var dispatchCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var dispatchTask = Task.Run(async () =>
        {
            while (!dispatchCts.IsCancellationRequested)
            {
                RequestEnvelope env;
                try
                {
                    env = await fake.NextAsync(timeout: TimeSpan.FromSeconds(10));
                }
                catch (OperationCanceledException) { break; }
                catch (TimeoutException) { break; }

                var req = env.Request;
                if (req.SegmentId == "U" && req.Revision == 1)
                {
                    uFinalTcs.TrySetResult(env);
                }
                else if (req.SegmentId == "V" && req.Revision == 2)
                {
                    vFinalTcs.TrySetResult(env);
                }
                else if (req.SegmentId == "V" && req.Revision == 1)
                {
                    vPartialTcs.TrySetResult(env);
                    // V Partial is completed independently, never occupying the Final's slot
                    fake.Complete(env, "前缀部分");
                }
                else
                {
                    fake.Complete(env, "");
                }

                if (uFinalTcs.Task.IsCompleted && vFinalTcs.Task.IsCompleted)
                    break;
            }
        }, dispatchCts.Token);

        // Wait for both U Final and V Final requests to be received by the translator
        var envU = await uFinalTcs.Task.WaitAsync(dispatchCts.Token);
        var envV = await vFinalTcs.Task.WaitAsync(dispatchCts.Token);

        // Complete U and V in the specified controlled order
        if (completionOrder == 0)
        {
            // Order 0: U first, then V
            fake.Complete(envU, "父");
            fake.Complete(envV, "前缀完成");
        }
        else
        {
            // Order 1: V first, then U
            fake.Complete(envV, "前缀完成");
            fake.Complete(envU, "父");
        }

        // 5. Event-driven wait for Store to accept both Finals (no unbounded polling, no sleep)
        var storeBothFinalsTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void CheckStoreFinals()
        {
            var s = store.Snapshot();
            var u = s.FirstOrDefault(x => x.SegmentId == "U");
            var v = s.FirstOrDefault(x => x.SegmentId == "V");
            if (u is { IsFinal: true, TranslationText: "父" } &&
                v is { IsFinal: true, TranslationText: "前缀完成" })
            {
                storeBothFinalsTcs.TrySetResult(true);
            }
        }
        store.Changed += CheckStoreFinals;
        CheckStoreFinals();
        await storeBothFinalsTcs.Task.WaitAsync(dispatchCts.Token);
        store.Changed -= CheckStoreFinals;

        // 6. Now release held late P2 translation
        fake.Complete(envP2, "迟到P2译");

        // Drain & finish
        asr.Channel.Writer.TryComplete();
        audio.Finish();
        await pipeline.StopAsync();

        // Verification 1: Frame atomicity: once P has been seen, any frame without P must have both U and V
        lock (observedChangedFrames)
        {
            bool seenP = false;
            foreach (var frame in observedChangedFrames)
            {
                bool hasP = frame.Any(x => x.SegmentId == "P");
                bool hasU = frame.Any(x => x.SegmentId == "U");
                bool hasV = frame.Any(x => x.SegmentId == "V");

                if (hasP)
                    seenP = true;

                if (seenP && !hasP)
                {
                    if (!hasU || !hasV)
                        throw new Exception("Intermediate state observed where P was deleted without both U and V being present!");
                }
            }
        }

        // Verification 2: Final snapshot contains U and V finals, P is absent, late P2 did not overwrite
        var finalSnap = store.Snapshot();
        var finalIds = finalSnap.Select(x => x.SegmentId).ToArray();
        if (!finalIds.SequenceEqual(new[] { "U", "V" }))
            throw new Exception($"Final snapshot should contain [U, V], but got: [{string.Join(", ", finalIds)}]");

        var uRow = finalSnap.Single(x => x.SegmentId == "U");
        var vRow = finalSnap.Single(x => x.SegmentId == "V");
        if (!uRow.IsFinal || uRow.TranslationText != "父")
            throw new Exception($"U row should be final with translation '父', got: {uRow.TranslationText}");
        if (!vRow.IsFinal || vRow.TranslationText != "前缀完成")
            throw new Exception($"V row should be final with translation '前缀完成', got: {vRow.TranslationText}");
    }
}

sealed class ChannelAsrProvider : IAsrProvider
{
    public readonly Channel<AsrResult> Channel = System.Threading.Channels.Channel.CreateUnbounded<AsrResult>();
    public IAsrSession CreateSession(AsrConfiguration config) => new Session(Channel);

    sealed class Session(Channel<AsrResult> channel) : IAsrSession
    {
        public Task ConnectAsync(CancellationToken token) => Task.CompletedTask;
        public Task SendAudioAsync(ReadOnlyMemory<byte> pcm, CancellationToken token) => Task.CompletedTask;
        public IAsyncEnumerable<AsrResult> GetResultsAsync(CancellationToken token) => channel.Reader.ReadAllAsync(token);
        public Task CompleteAsync(CancellationToken token) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
