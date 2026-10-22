using System.Diagnostics;
using AntiDbt.Core;
using static Phase555TestSupport;

internal static class DisplayGroupRevisionTests
{
    private static void SetupFixtures(SubtitleStore store, out long session, out CaptionSegment a1, out CaptionSegment b1)
    {
        session = store.BeginSession();
        a1 = store.ApplySource(session, MakeAsr("A", 1, "we can", true, 0, 1000))!;
        b1 = store.ApplySource(session, MakeAsr("B", 1, "continue.", true, 1000, 2000))!;
    }

    public static async Task<List<Phase555TestResult>> RunAllAsync(string? caseFilter)
    {
        var list = new List<Phase555TestResult>();

        async Task RunCase(string name, Func<Task> test)
        {
            if (caseFilter != null && name != caseFilter && !name.StartsWith(caseFilter + "/"))
                return;
            try
            {
                await test();
                list.Add(new Phase555TestResult(name, "AQ01", true, null));
            }
            catch (Exception ex)
            {
                list.Add(new Phase555TestResult(name, "AQ01", false, ex.Message));
            }
        }

        void RunSync(string name, Action test) => RunCase(name, () => { test(); return Task.CompletedTask; }).GetAwaiter().GetResult();

        // G01
        RunSync("CommitsCompleteDisplayGroupAtomically/HappyPath", () =>
        {
            var store = new SubtitleStore();
            SetupFixtures(store, out var session, out var a1, out var b1);
            var probe = CaptureGroup(store, a1, b1);
            int changed = 0;
            store.Changed += () => changed++;
            var before = store.Snapshot();
            bool ok = probe.Commit("GROUP-NEW");
            AssertV(store, before, ok, changed, "GROUP-NEW", "A", "B");
            var snap = store.Snapshot();
            if (snap[0].TranslationText != "" || snap[1].TranslationText != "")
                throw new Exception("Members without independent exact must retain empty TranslationText.");
        });

        RunSync("CommitsCompleteDisplayGroupAtomically/EmptyIds", () =>
        {
            var store = new SubtitleStore();
            SetupFixtures(store, out _, out _, out _);
            var probe = CaptureGroup(store, Array.Empty<CaptionSegment>());
            int changed = 0;
            store.Changed += () => changed++;
            var before = store.Snapshot();
            bool ok = probe.Commit("GROUP-NEW");
            AssertZ(store, before, ok, changed);
        });

        RunSync("CommitsCompleteDisplayGroupAtomically/SingleMember", () =>
        {
            var store = new SubtitleStore();
            SetupFixtures(store, out _, out var a1, out _);
            var probe = CaptureGroup(store, a1);
            int changed = 0;
            store.Changed += () => changed++;
            var before = store.Snapshot();
            bool ok = probe.Commit("GROUP-NEW");
            AssertZ(store, before, ok, changed);
        });

        RunSync("CommitsCompleteDisplayGroupAtomically/DuplicateMember", () =>
        {
            var store = new SubtitleStore();
            SetupFixtures(store, out _, out var a1, out _);
            var probe = CaptureGroup(store, a1, a1);
            int changed = 0;
            store.Changed += () => changed++;
            var before = store.Snapshot();
            bool ok = probe.Commit("GROUP-NEW");
            AssertZ(store, before, ok, changed);
        });

        RunSync("CommitsCompleteDisplayGroupAtomically/BlankText", () =>
        {
            var store = new SubtitleStore();
            SetupFixtures(store, out _, out var a1, out var b1);
            var probe = CaptureGroup(store, a1, b1);
            int changed = 0;
            store.Changed += () => changed++;
            var before = store.Snapshot();
            bool ok = probe.Commit("   ");
            AssertZ(store, before, ok, changed);
        });

        RunSync("CommitsCompleteDisplayGroupAtomically/AllAlreadyExact", () =>
        {
            var store = new SubtitleStore();
            SetupFixtures(store, out var session, out var a1, out var b1);
            store.ApplyTranslation(session, "A", 1, "EXACT-A");
            store.ApplyTranslation(session, "B", 1, "EXACT-B");
            var probe = CaptureGroup(store, a1, b1);
            int changed = 0;
            store.Changed += () => changed++;
            var before = store.Snapshot();
            bool ok = probe.Commit("GROUP-NEW");
            AssertZ(store, before, ok, changed);
        });

        // G02
        RunSync("RejectsFirstMemberRevisionChange", () =>
        {
            var store = new SubtitleStore();
            SetupFixtures(store, out var session, out var a1, out var b1);
            var probe = CaptureGroup(store, a1, b1);
            store.ApplySource(session, MakeAsr("A", 2, "we can", true, 0, 1000));
            int changed = 0;
            store.Changed += () => changed++;
            var before = store.Snapshot();
            bool ok = probe.Commit("GROUP-OLD");
            AssertZ(store, before, ok, changed);
            var snap = store.Snapshot();
            if (snap[0].Revision != 2 || snap[1].Revision != 1)
                throw new Exception($"Expected A.Rev=2 and B.Rev=1, but was A.Rev={snap[0].Revision}, B.Rev={snap[1].Revision}");
        });

        // G03
        RunSync("RejectsSecondMemberRevisionChange", () =>
        {
            var store = new SubtitleStore();
            SetupFixtures(store, out var session, out var a1, out var b1);
            var probe = CaptureGroup(store, a1, b1);
            store.ApplySource(session, MakeAsr("B", 2, "continue.", true, 1000, 2000));
            int changed = 0;
            store.Changed += () => changed++;
            var before = store.Snapshot();
            bool ok = probe.Commit("GROUP-OLD");
            AssertZ(store, before, ok, changed);
            var snap = store.Snapshot();
            if (snap[0].Revision != 1 || snap[1].Revision != 2)
                throw new Exception($"Expected A.Rev=1 and B.Rev=2, but was A.Rev={snap[0].Revision}, B.Rev={snap[1].Revision}");
        });

        // G04
        RunSync("RejectsChangedSourceAndForgedExpectedSource/ChangedSource", () =>
        {
            var store = new SubtitleStore();
            SetupFixtures(store, out var session, out var a1, out var b1);
            var probe = CaptureGroup(store, a1, b1);
            store.ApplySource(session, MakeAsr("A", 2, "we cannot", true, 0, 1000));
            int changed = 0;
            store.Changed += () => changed++;
            var before = store.Snapshot();
            bool ok = probe.Commit("GROUP-OLD");
            AssertZ(store, before, ok, changed);

            // Foreign store check
            var foreignStore = new SubtitleStore();
            var foreignSession = foreignStore.BeginSession();
            foreignStore.ApplySource(foreignSession, MakeAsr("A", 1, "we can", true, 0, 1000));
            foreignStore.ApplySource(foreignSession, MakeAsr("B", 1, "continue.", true, 1000, 2000));
            if (probe.Ticket != null)
            {
                var foreignBefore = foreignStore.Snapshot();
                int foreignChanged = 0;
                foreignStore.Changed += () => foreignChanged++;
                bool foreignOk = foreignStore.AssignTranslationDisplayGroup(probe.Ticket, "GROUP-FOREIGN");
                AssertZ(foreignStore, foreignBefore, foreignOk, foreignChanged);
            }
        });

        RunSync("RejectsChangedSourceAndForgedExpectedSource/ForgedExpectedSource", () =>
        {
            var store = new SubtitleStore();
            SetupFixtures(store, out _, out var a1, out var b1);
            var forgedA = a1 with { SourceText = "other" };
            var probe = CaptureGroup(store, forgedA, b1);
            int changed = 0;
            store.Changed += () => changed++;
            var before = store.Snapshot();
            bool ok = probe.Commit("GROUP-OLD");
            AssertZ(store, before, ok, changed);
            if (store.Snapshot()[0].SourceText != "we can")
                throw new Exception("Real source text mutated unexpectedly.");
        });

        // G05
        RunSync("RejectsDeletedOrReplacedPartialMember/Deleted", () =>
        {
            var store = new SubtitleStore();
            var session = store.BeginSession();
            var a1 = store.ApplySource(session, MakeAsr("A", 1, "we can", false, 0, 1000))!;
            var b1 = store.ApplySource(session, MakeAsr("B", 1, "continue.", false, 1000, 2000))!;
            var probe = CaptureGroup(store, a1, b1);
            // Delete A by sending empty correction / tombstone
            store.ApplySource(session, MakeAsr("A", 2, "", true, 0, 1000));
            int changed = 0;
            store.Changed += () => changed++;
            var before = store.Snapshot();
            bool ok = probe.Commit("GROUP-OLD");
            AssertZ(store, before, ok, changed);
        });

        RunSync("RejectsDeletedOrReplacedPartialMember/Replaced", () =>
        {
            var store = new SubtitleStore();
            var session = store.BeginSession();
            var a1 = store.ApplySource(session, MakeAsr("A", 1, "we can", false, 0, 1000))!;
            var b1 = store.ApplySource(session, MakeAsr("B", 1, "continue.", false, 1000, 2000))!;
            var probe = CaptureGroup(store, a1, b1);
            store.ApplySource(session, MakeAsr("A", 2, "", true, 0, 1000));
            store.ApplySource(session, MakeAsr("X", 1, "we can", false, 0, 1000));
            int changed = 0;
            store.Changed += () => changed++;
            var before = store.Snapshot();
            bool ok = probe.Commit("GROUP-OLD");
            AssertZ(store, before, ok, changed);
        });

        // G06
        RunSync("RejectsMissingSomeOrAllMembers/Evicted", () =>
        {
            var store = new SubtitleStore(2); // capacity 2
            var session = store.BeginSession();
            var a1 = store.ApplySource(session, MakeAsr("A", 1, "we can", true, 0, 1000))!;
            var b1 = store.ApplySource(session, MakeAsr("B", 1, "continue.", true, 1000, 2000))!;
            var probe = CaptureGroup(store, a1, b1);
            store.ApplySource(session, MakeAsr("C", 1, "later.", true, 2000, 3000)); // evicts A
            int changed = 0;
            store.Changed += () => changed++;
            var before = store.Snapshot();
            bool ok = probe.Commit("GROUP-OLD");
            AssertZ(store, before, ok, changed);
        });

        RunSync("RejectsMissingSomeOrAllMembers/Clear", () =>
        {
            var store = new SubtitleStore();
            SetupFixtures(store, out _, out var a1, out var b1);
            var probe = CaptureGroup(store, a1, b1);
            store.Clear();
            int changed = 0;
            store.Changed += () => changed++;
            var before = store.Snapshot();
            bool ok = probe.Commit("GROUP-OLD");
            AssertZ(store, before, ok, changed);
        });

        // G07
        RunSync("RejectsSameRevisionPartialFinalUpgrade", () =>
        {
            var store = new SubtitleStore();
            var session = store.BeginSession();
            var a1 = store.ApplySource(session, MakeAsr("A", 1, "we can", false, 0, 1000))!;
            var b1 = store.ApplySource(session, MakeAsr("B", 1, "continue.", false, 1000, 2000))!;
            var probe = CaptureGroup(store, a1, b1);
            store.ApplySource(session, MakeAsr("A", 1, "we can", true, 0, 1000)); // Partial -> Final upgrade at same revision!
            int changed = 0;
            store.Changed += () => changed++;
            var before = store.Snapshot();
            bool ok = probe.Commit("GROUP-OLD");
            AssertZ(store, before, ok, changed);
        });

        // G08
        RunSync("RejectsClearRecreateAba", () =>
        {
            var store = new SubtitleStore();
            SetupFixtures(store, out var session, out var a1, out var b1);
            var probeOld = CaptureGroup(store, a1, b1);
            store.Clear();
            var a2 = store.ApplySource(session, MakeAsr("A", 1, "we can", true, 0, 1000))!;
            var b2 = store.ApplySource(session, MakeAsr("B", 1, "continue.", true, 1000, 2000))!;
            int changed = 0;
            store.Changed += () => changed++;
            var before = store.Snapshot();
            bool okOld = probeOld.Commit("GROUP-OLD");
            AssertZ(store, before, okOld, changed);

            changed = 0;
            var probeNew = CaptureGroup(store, a2, b2);
            var beforeNew = store.Snapshot();
            bool okNew = probeNew.Commit("GROUP-NEW");
            AssertV(store, beforeNew, okNew, changed, "GROUP-NEW", "A", "B");
        });

        // G09
        RunSync("RejectsInsertionBetweenUnchangedMembers", () =>
        {
            var store = new SubtitleStore();
            var session = store.BeginSession();
            var a1 = store.ApplySource(session, MakeAsr("A", 1, "we can", true, 0, 500))!;
            var b1 = store.ApplySource(session, MakeAsr("B", 1, "continue.", true, 2000, 2500))!;
            var probe = CaptureGroup(store, a1, b1);
            store.ApplySource(session, MakeAsr("X", 1, "middle", true, 1000, 1500));
            int changed = 0;
            store.Changed += () => changed++;
            var before = store.Snapshot();
            bool ok = probe.Commit("GROUP-OLD");
            AssertZ(store, before, ok, changed);
        });

        // G10
        RunSync("RejectsRetainedIdSentenceSplit", () =>
        {
            var store = new SubtitleStore();
            var session = store.BeginSession();
            var a1 = store.ApplySource(session, MakeAsr("A", 1, "we can", false, 0, 1000))!;
            var b1 = store.ApplySource(session, MakeAsr("B", 1, "continue.", false, 1000, 2000))!;
            var probe = CaptureGroup(store, a1, b1);
            store.ApplySources(session, new[]
            {
                MakeAsr("A", 2, "we", true, 0, 500),
                MakeAsr("T", 1, "can", false, 500, 1000)
            });
            int changed = 0;
            store.Changed += () => changed++;
            var before = store.Snapshot();
            bool ok = probe.Commit("GROUP-OLD");
            AssertZ(store, before, ok, changed);
        });

        // G11
        RunSync("RejectsSplitMergeReplacementIdentity/Merge", () =>
        {
            var store = new SubtitleStore();
            var session = store.BeginSession();
            var a1 = store.ApplySource(session, MakeAsr("A", 1, "we can", false, 0, 1000))!;
            var b1 = store.ApplySource(session, MakeAsr("B", 1, "continue.", false, 1000, 2000))!;
            var probe = CaptureGroup(store, a1, b1);
            store.ApplySources(session, new[]
            {
                MakeAsr("A", 2, "", true, 0, 1000),
                MakeAsr("B", 2, "", true, 1000, 2000),
                MakeAsr("M", 1, "we can continue.", false, 0, 2000)
            });
            int changed = 0;
            store.Changed += () => changed++;
            var before = store.Snapshot();
            bool ok = probe.Commit("GROUP-OLD");
            AssertZ(store, before, ok, changed);
        });

        RunSync("RejectsSplitMergeReplacementIdentity/SplitAfterMerge", () =>
        {
            var store = new SubtitleStore();
            var session = store.BeginSession();
            var m1 = store.ApplySource(session, MakeAsr("M", 1, "we can continue.", false, 0, 2000))!;
            var c1 = store.ApplySource(session, MakeAsr("C", 1, "later.", false, 2000, 3000))!;
            var probe = CaptureGroup(store, m1, c1);
            store.ApplySources(session, new[]
            {
                MakeAsr("M", 2, "", true, 0, 2000),
                MakeAsr("U", 1, "we can", false, 0, 1000),
                MakeAsr("V", 1, "continue.", false, 1000, 2000)
            });
            int changed = 0;
            store.Changed += () => changed++;
            var before = store.Snapshot();
            bool ok = probe.Commit("GROUP-OLD");
            AssertZ(store, before, ok, changed);
        });

        // G12
        RunSync("RejectsPartialOverlapWithExistingDisplayGroup", () =>
        {
            var store = new SubtitleStore();
            SetupFixtures(store, out var session, out var a1, out var b1);
            var c1 = store.ApplySource(session, MakeAsr("C", 1, "later.", true, 2000, 3000))!;
            var probeAll = CaptureGroup(store, a1, b1, c1);
            int changed = 0;
            store.Changed += () => changed++;
            bool ok1 = probeAll.Commit("GROUP-NEW");
            if (!ok1) throw new Exception("Setup commit for 3 members failed.");

            changed = 0;
            var probeSub = CaptureGroup(store, a1, b1);
            var before = store.Snapshot();
            bool ok2 = probeSub.Commit("OTHER");
            AssertZ(store, before, ok2, changed);
        });

        // G13
        RunSync("RejectsPreviousSessionAndForeignStoreTicket/PreviousSession", () =>
        {
            var store = new SubtitleStore();
            SetupFixtures(store, out _, out var a1, out var b1);
            var probe = CaptureGroup(store, a1, b1);
            store.BeginSession(false);
            var newSession = store.CurrentSession;
            store.ApplySource(newSession, MakeAsr("A", 1, "we can", true, 0, 1000));
            store.ApplySource(newSession, MakeAsr("B", 1, "continue.", true, 1000, 2000));
            int changed = 0;
            store.Changed += () => changed++;
            var before = store.Snapshot();
            bool ok = probe.Commit("GROUP-OLD");
            AssertZ(store, before, ok, changed);
        });

        // G14
        RunSync("PreservesExactProvisionalErrorAndStickyState", () =>
        {
            var store = new SubtitleStore();
            var session = store.BeginSession();
            var a1 = store.ApplySource(session, MakeAsr("A", 1, "we can", false, 0, 1000))!;
            var b1 = store.ApplySource(session, MakeAsr("B", 1, "continue.", false, 1000, 2000))!;
            store.ApplyTranslation(session, "A", 1, "EXACT-A");
            store.ApplyTranslation(session, "B", 1, "EXACT-B");
            var b2 = store.ApplySource(session, MakeAsr("B", 2, "continue. now", false, 1000, 2000))!;
            store.ApplyTranslationError(session, "B", 2, "network error");

            var probe = CaptureGroup(store, a1, b2);
            int changed = 0;
            store.Changed += () => changed++;
            var before = store.Snapshot();
            bool ok = probe.Commit("GROUP-NEW");
            AssertV(store, before, ok, changed, "GROUP-NEW", "A", "B");

            var snap = store.Snapshot();
            if (snap[0].TranslationText != "EXACT-A")
                throw new Exception($"A independent translation overwritten: was '{snap[0].TranslationText}'");
            if (snap[1].TranslationText != "")
                throw new Exception($"B independent translation should be empty, was '{snap[1].TranslationText}'");
            if (snap[1].ProvisionalTranslationText != "EXACT-B")
                throw new Exception($"B provisional translation lost: was '{snap[1].ProvisionalTranslationText}'");
            if (snap[1].TranslationError != "network error")
                throw new Exception($"B translation error lost: was '{snap[1].TranslationError}'");
        });

        // G15
        RunSync("WaitsForEveryIndependentExactWhenSecondCompletesFirst", () =>
        {
            var store = new SubtitleStore();
            SetupFixtures(store, out var session, out var a1, out var b1);
            var observedFrames = new List<string>();
            store.Changed += () =>
            {
                var snap = store.Snapshot();
                var c = SubtitleProjection.Compact(snap);
                observedFrames.Add(c.Translation);
            };

            var probe = CaptureGroup(store, a1, b1);
            bool ok = probe.Commit("GROUP-NEW");
            if (!ok) throw new Exception("Initial group commit failed.");

            // B completes exact first
            store.ApplyTranslation(session, "B", 1, "EXACT-B");
            var snap1 = store.Snapshot();
            if (string.IsNullOrEmpty(snap1[0].TranslationDisplayGroup) || string.IsNullOrEmpty(snap1[1].TranslationDisplayGroup))
                throw new Exception("Group prematurely disbanded when only B completed exact!");

            var compact1 = SubtitleProjection.Compact(snap1);
            // CRITICAL: Intermediate state must strictly retain group translation "GROUP-NEW". Cannot be empty!
            if (string.IsNullOrWhiteSpace(compact1.Translation))
                throw new Exception("Intermediate state translation became empty when only B completed exact!");
            if (compact1.Translation != "GROUP-NEW")
                throw new Exception($"Intermediate state must strictly retain 'GROUP-NEW', but was: '{compact1.Translation}'");

            var expanded1 = SubtitleProjection.Expanded(snap1);
            if (expanded1.Count != 1 || expanded1[0].Translation != "GROUP-NEW")
                throw new Exception($"Intermediate expanded state must retain 'GROUP-NEW', got: [{string.Join(", ", expanded1.Select(x => x.Translation))}]");

            // A completes exact second
            store.ApplyTranslation(session, "A", 1, "EXACT-A");
            var snap2 = store.Snapshot();
            if (!string.IsNullOrEmpty(snap2[0].TranslationDisplayGroup) || !string.IsNullOrEmpty(snap2[1].TranslationDisplayGroup))
                throw new Exception("Group not retired after both A and B completed exact!");

            var compact2 = SubtitleProjection.Compact(snap2);
            string expectedBoth = SubtitleProjection.JoinTexts(new[] { "EXACT-A", "EXACT-B" });
            if (compact2.Translation != expectedBoth)
                throw new Exception($"Both exact translations should be visible as '{expectedBoth}', but was: '{compact2.Translation}'");
            if (compact2.Translation.Contains("GROUP-NEW"))
                throw new Exception("Stale group translation still visible after both exact completed!");

            var expanded2 = SubtitleProjection.Expanded(snap2);
            if (expanded2.Count != 1 || expanded2[0].Translation != expectedBoth)
                throw new Exception($"Both exact translations in expanded should be '{expectedBoth}', got: [{string.Join(", ", expanded2.Select(x => x.Translation))}]");

            // Verify entire lifecycle had no empty translation frames
            foreach (var frame in observedFrames)
            {
                if (string.IsNullOrWhiteSpace(frame))
                    throw new Exception("Observed an empty translation frame during group handoff lifecycle!");
            }
        });

        // G16
        RunSync("RetiresGroupWhenFirstThenSecondExactComplete/FirstThenSecond", () =>
        {
            var store = new SubtitleStore();
            SetupFixtures(store, out var session, out var a1, out var b1);
            var probe = CaptureGroup(store, a1, b1);
            probe.Commit("GROUP-NEW");

            store.ApplyTranslation(session, "A", 1, "EXACT-A");
            var snap1 = store.Snapshot();
            if (string.IsNullOrEmpty(snap1[0].TranslationDisplayGroup))
                throw new Exception("Group retired prematurely after only A completed.");

            store.ApplyTranslation(session, "B", 1, "EXACT-B");
            var snap2 = store.Snapshot();
            if (!string.IsNullOrEmpty(snap2[0].TranslationDisplayGroup))
                throw new Exception("Group not retired after both A and B completed.");
            if (snap2[0].TranslationText != "EXACT-A" || snap2[1].TranslationText != "EXACT-B")
                throw new Exception("Exact translations corrupted after retirement.");
        });

        RunSync("RetiresGroupWhenFirstThenSecondExactComplete/AAlreadyExactBeforeGroup", () =>
        {
            var store = new SubtitleStore();
            SetupFixtures(store, out var session, out var a1, out var b1);
            store.ApplyTranslation(session, "A", 1, "EXACT-A");
            var probe = CaptureGroup(store, a1, b1);
            probe.Commit("GROUP-NEW");

            // When B completes Exact, group retires; A must keep its own EXACT-A, not GROUP-NEW!
            store.ApplyTranslation(session, "B", 1, "EXACT-B");
            var snap = store.Snapshot();
            if (snap[0].TranslationText != "EXACT-A")
                throw new Exception($"A's exact translation was corrupted: was '{snap[0].TranslationText}', expected 'EXACT-A'");
            if (snap[1].TranslationText != "EXACT-B")
                throw new Exception($"B's exact translation was corrupted: was '{snap[1].TranslationText}', expected 'EXACT-B'");
        });

        // G17
        RunSync("RejectsOlderGroupAfterNewerGroupOrExact/NewerGroupPreempts", () =>
        {
            var store = new SubtitleStore();
            SetupFixtures(store, out _, out var a1, out var b1);
            var p1 = CaptureGroup(store, a1, b1);
            var p2 = CaptureGroup(store, a1, b1);
            bool ok2 = p2.Commit("GROUP-NEW");
            if (!ok2) throw new Exception("P2 commit failed.");

            int changed = 0;
            store.Changed += () => changed++;
            var before = store.Snapshot();
            bool ok1 = p1.Commit("GROUP-OLD");
            AssertZ(store, before, ok1, changed);
        });

        RunSync("RejectsOlderGroupAfterNewerGroupOrExact/ExactPreemptsGroup", () =>
        {
            var store = new SubtitleStore();
            SetupFixtures(store, out var session, out var a1, out var b1);
            var p1 = CaptureGroup(store, a1, b1);
            store.ApplyTranslation(session, "A", 1, "EXACT-A");

            int changed = 0;
            store.Changed += () => changed++;
            var before = store.Snapshot();
            bool ok1 = p1.Commit("GROUP-OLD");
            AssertZ(store, before, ok1, changed);
        });

        RunSync("RejectsOlderGroupAfterNewerGroupOrExact/SingleUseTicket", () =>
        {
            var store = new SubtitleStore();
            SetupFixtures(store, out _, out var a1, out var b1);
            var p1 = CaptureGroup(store, a1, b1);
            bool okFirst = p1.Commit("GROUP-NEW");
            if (!okFirst) throw new Exception("First commit failed.");

            int changed = 0;
            store.Changed += () => changed++;
            var before = store.Snapshot();
            bool okSecond = p1.Commit("GROUP-NEW-AGAIN");
            AssertZ(store, before, okSecond, changed);
        });

        // G18
        await RunCase("SchedulerRejectsDelayedCombinedResultAfterCorrection", async () =>
        {
            var store = new SubtitleStore();
            var session = store.BeginSession();
            var a1 = store.ApplySource(session, MakeAsr("A", 1, "we can", true, 0, 1000))!;
            store.ApplyTranslation(session, "A", 1, "EXACT-A");

            using var fake = new ScriptedTranslator();
            var config = new TranslationConfiguration
            {
                Provider = "VolcengineMachineTranslation", // supportsContext = false, triggers display group branch!
                DebounceMilliseconds = 0,
                ContextSentences = 1
            };
            await using var scheduler = new TranslationScheduler(store, fake, config, "zh");

            var b1 = store.ApplySource(session, MakeAsr("B", 1, "continue.", true, 1000, 2000))!;
            scheduler.Schedule(b1);

            // Expect two requests: normal B and combined (A+B)
            var req1 = await fake.NextAsync();
            var req2 = await fake.NextAsync();

            var combinedReq = req1.Request.Text.Contains("continue.") && req1.Request.Text.Contains("we can") ? req1 : req2;
            var normalBReq = combinedReq == req1 ? req2 : req1;

            if (!combinedReq.Request.Text.Contains("we can continue."))
                throw new Exception($"Expected combined request for 'we can continue.', but got: '{combinedReq.Request.Text}'");

            // Apply A2 correction to store BEFORE completing combined translation!
            var a2 = store.ApplySource(session, MakeAsr("A", 2, "we cannot", true, 0, 1000))!;

            // Now complete combined request with GROUP-OLD and normal B with EXACT-B
            fake.Complete(combinedReq, "GROUP-OLD");
            fake.Complete(normalBReq, "EXACT-B");

            await scheduler.FlushAsync(CancellationToken.None);

            var snap = store.Snapshot();
            if (snap[0].SourceText != "we cannot" || snap[0].Revision != 2)
                throw new Exception($"A should have latest revision 2 and text 'we cannot', was: {snap[0]}");

            if (snap[0].TranslationText == "GROUP-OLD" || snap[1].TranslationText == "GROUP-OLD" ||
                snap[0].LastVisibleTranslation == "GROUP-OLD" || snap[1].LastVisibleTranslation == "GROUP-OLD")
            {
                throw new Exception("Stale combined result GROUP-OLD was applied after A had been corrected!");
            }
        });

        // G19
        await RunCase("RejectsOlderOfTwoAsynchronousGroupResponses", async () =>
        {
            var store = new SubtitleStore();
            SetupFixtures(store, out _, out var a1, out var b1);

            using var fake = new ScriptedTranslator();

            var p1 = CaptureGroup(store, a1, b1);
            var t1Task = Task.Run(async () =>
            {
                var text = await fake.TranslateAsync(new TranslationRequest(store.CurrentSession, "A", 1, "req1", "en", "zh", Array.Empty<string>()), CancellationToken.None);
                return p1.Commit(text);
            });

            var env1 = await fake.NextAsync(r => r.Text == "req1");

            var p2 = CaptureGroup(store, a1, b1);
            var t2Task = Task.Run(async () =>
            {
                var text = await fake.TranslateAsync(new TranslationRequest(store.CurrentSession, "A", 1, "req2", "en", "zh", Array.Empty<string>()), CancellationToken.None);
                return p2.Commit(text);
            });

            var env2 = await fake.NextAsync(r => r.Text == "req2");

            // Complete T2 first (newer)
            fake.Complete(env2, "GROUP-NEW");
            bool ok2 = await t2Task;
            if (!ok2) throw new Exception("T2 (GROUP-NEW) commit failed.");

            // Now complete T1 (older)
            int changed = 0;
            store.Changed += () => changed++;
            var before = store.Snapshot();
            fake.Complete(env1, "GROUP-OLD");
            bool ok1 = await t1Task;

            AssertZ(store, before, ok1, changed);
        });

        // Contract test for snapshot collection immutability
        RunSync("SnapshotOwnsItsMemberCollection", () =>
        {
            var store = new SubtitleStore();
            SetupFixtures(store, out var session, out var a1, out var b1);
            var arr = new[] { a1, b1 };
            var ticket = store.TryPrepareTranslationDisplayGroup(arr);
            if (ticket is null) throw new Exception("Prepare failed");

            // Mutate input array
            arr[0] = b1;
            arr[1] = a1;

            if (ticket.Members[0].SegmentId != "A" || ticket.Members[1].SegmentId != "B")
                throw new Exception("Snapshot.Members was mutated by external array change.");

            int changed = 0;
            store.Changed += () => changed++;
            var before = store.Snapshot();
            bool ok = store.AssignTranslationDisplayGroup(ticket, "GROUP-OWNED");
            AssertV(store, before, ok, changed, "GROUP-OWNED", "A", "B");
        });

        return list;
    }
}
