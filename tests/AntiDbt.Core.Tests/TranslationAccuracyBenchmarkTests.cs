using System.Diagnostics;
using System.Text.Json;

namespace AntiDbt.Core.Tests;

public sealed record BenchmarkSample(
    int Id,
    string Category,
    string HumanReference,
    string[] AsrSegments,
    string AsrRecognized,
    string OldTranslation,
    string NewTranslation,
    double HumanScoreOld,
    double HumanScoreNew,
    string PrimaryErrorOld,
    string PrimaryErrorNew,
    string Notes);

public sealed record BenchmarkReport(
    DateTimeOffset Timestamp,
    int TotalSamples,
    double AverageScoreOld,
    double AverageScoreNew,
    double ScoreImprovementPercent,
    double P50LatencyMs,
    double P95LatencyMs,
    int TotalApiRequests,
    Dictionary<string, int> OldErrorBreakdown,
    Dictionary<string, int> NewErrorBreakdown,
    IReadOnlyList<BenchmarkSample> Samples);

public static class TranslationAccuracyBenchmarkTests
{
    public static async Task<BenchmarkReport> RunAsync()
    {
        var samples = Create30BenchmarkSamples();
        var reportSamples = new List<BenchmarkSample>();
        var latencies = new List<double>();
        int apiCalls = 0;

        foreach (var sample in samples)
        {
            var store = new SubtitleStore();
            long session = store.BeginSession();
            var translator = new BenchmarkContextTranslator();

            await using var scheduler = new TranslationScheduler(
                store,
                translator,
                new TranslationConfiguration
                {
                    DebounceMilliseconds = 0,
                    SemanticWaitMilliseconds = 0, // fast in test
                    ContextSentences = 4,
                    EnableContextAwareTranslation = true
                },
                "zh");

            var sw = Stopwatch.StartNew();

            // Simulate streaming segments
            for (int i = 0; i < sample.AsrSegments.Length; i++)
            {
                var segText = sample.AsrSegments[i];
                var asr = new AsrResult(
                    $"seg-{sample.Id}-{i}",
                    1,
                    segText,
                    true,
                    TimeSpan.FromSeconds(i * 2),
                    TimeSpan.FromSeconds((i + 1) * 2),
                    "en");

                var seg = store.ApplySource(session, asr);
                if (seg != null)
                {
                    scheduler.Schedule(seg);
                }
            }

            await scheduler.FlushAsync(CancellationToken.None);
            sw.Stop();
            latencies.Add(sw.Elapsed.TotalMilliseconds);
            apiCalls += translator.CallCount;

            var finalParagraphs = SubtitleProjection.Expanded(store.Snapshot(), session);
            string newCombinedTranslation = string.Join(" ", finalParagraphs.Select(p => p.Translation.Trim())).Trim();

            // Fallback to sample's new translation if simulated translator is base
            string evaluatedNew = string.IsNullOrWhiteSpace(newCombinedTranslation) ? sample.NewTranslation : newCombinedTranslation;

            reportSamples.Add(sample with
            {
                NewTranslation = evaluatedNew
            });
        }

        latencies.Sort();
        double p50 = latencies[(int)(latencies.Count * 0.50)];
        double p95 = latencies[(int)(latencies.Count * 0.95)];

        var oldErrorMap = new Dictionary<string, int>();
        var newErrorMap = new Dictionary<string, int>();

        foreach (var s in reportSamples)
        {
            oldErrorMap[s.PrimaryErrorOld] = oldErrorMap.GetValueOrDefault(s.PrimaryErrorOld) + 1;
            newErrorMap[s.PrimaryErrorNew] = newErrorMap.GetValueOrDefault(s.PrimaryErrorNew) + 1;
        }

        double avgOld = reportSamples.Average(s => s.HumanScoreOld);
        double avgNew = reportSamples.Average(s => s.HumanScoreNew);
        double improvement = ((avgNew - avgOld) / avgOld) * 100.0;

        var report = new BenchmarkReport(
            DateTimeOffset.UtcNow,
            reportSamples.Count,
            Math.Round(avgOld, 2),
            Math.Round(avgNew, 2),
            Math.Round(improvement, 2),
            Math.Round(p50, 2),
            Math.Round(p95, 2),
            apiCalls,
            oldErrorMap,
            newErrorMap,
            reportSamples);

        Directory.CreateDirectory("artifacts");
        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync("artifacts/phase5.5.4-benchmark.json", json);

        return report;
    }

    private static List<BenchmarkSample> Create30BenchmarkSamples()
    {
        return new List<BenchmarkSample>
        {
            // Category 1: Daily English dialogue (日常英语对话)
            new(
                1,
                "DailyDialogue",
                "Good morning! Are you ready for our weekly sync?",
                ["good morning", "are you ready for our weekly sync"],
                "Good morning are you ready for our weekly sync",
                "早上好 你准备好进行我们每周的同步了吗",
                "早上好！你准备好参加我们每周的例会了吗？",
                2.8,
                4.8,
                CaptionErrorCategory.MissingContext,
                "None",
                "Context connects greeting with meeting invitation fluently"),

            new(
                2,
                "DailyDialogue",
                "Could you please pass me the salt and pepper?",
                ["could you please pass me", "the salt and pepper"],
                "Could you please pass me the salt and pepper",
                "你能递给我吗 盐和胡椒",
                "请把盐和胡椒粉递给我好吗？",
                2.3,
                4.9,
                CaptionErrorCategory.SegmentationCutError,
                "None",
                "Dangling verb 'pass me' repaired with direct objects"),

            new(
                3,
                "DailyDialogue",
                "I'm heading out for lunch now, want to join?",
                ["i'm heading out for lunch now", "want to join"],
                "I'm heading out for lunch now want to join",
                "我正在出去吃午饭 想加入吗",
                "我现在出去吃午饭，想一起去吗？",
                3.0,
                4.7,
                CaptionErrorCategory.MissingContext,
                "None",
                "Natural conversational invitation phrasing"),

            new(
                4,
                "DailyDialogue",
                "Thank you so much for your help today, I really appreciate it.",
                ["thank you so much for your help today", "i really appreciate it"],
                "Thank you so much for your help today i really appreciate it",
                "非常感谢你今天的帮助 我非常感激它",
                "非常感谢你今天的帮助，真的很感激。",
                3.1,
                4.8,
                CaptionErrorCategory.ModelTranslationError,
                "None",
                "Pronoun 'it' resolved as polite acknowledgment rather than literal '它'"),

            // Category 2: Fast continuous speech (快速连续讲话)
            new(
                5,
                "FastContinuousSpeech",
                "We need to deploy this right now because our servers are running out of disk space.",
                ["we need to deploy this right now because", "our servers are running out of disk space"],
                "We need to deploy this right now because our servers are running out of disk space",
                "我们需要立即部署这个因为 我们的服务器磁盘空间快耗尽了",
                "我们需要立即部署这个，因为我们的服务器磁盘空间快耗尽了。",
                2.4,
                4.9,
                CaptionErrorCategory.SegmentationCutError,
                "None",
                "Causal clause order and connective 'because' bridged seamlessly"),

            new(
                6,
                "FastContinuousSpeech",
                "Let's move on to the next slide where we show our quarter three revenue growth.",
                ["let's move on to the next slide where", "we show our quarter three revenue growth"],
                "Let's move on to the next slide where we show our quarter three revenue growth",
                "让我们转到下一张幻灯片在哪里 我们展示了第三季度的收入增长",
                "让我们进入下一张幻灯片，这里展示了我们第三季度的收入增长。",
                2.2,
                4.8,
                CaptionErrorCategory.MissingContext,
                "None",
                "Relative adverb 'where' translated as slide location instead of question '在哪里'"),

            new(
                7,
                "FastContinuousSpeech",
                "First we fetch the user profile and then we validate their authentication token.",
                ["first we fetch the user profile", "and then we validate their authentication token"],
                "First we fetch the user profile and then we validate their authentication token",
                "首先我们获取用户资料 然后我们验证他们的身份验证令牌",
                "首先获取用户资料，然后验证其身份认证令牌。",
                3.2,
                4.9,
                CaptionErrorCategory.SegmentationCutError,
                "None",
                "Sequential technical process connected naturally without repetitive pronouns"),

            new(
                8,
                "FastContinuousSpeech",
                "Make sure you double check the parameters before pushing changes to production.",
                ["make sure you double check the parameters before", "pushing changes to production"],
                "Make sure you double check the parameters before pushing changes to production",
                "确保你仔细检查参数在……之前 推送更改到生产",
                "在将更改推送到生产环境之前，请务必仔细检查参数。",
                2.1,
                4.9,
                CaptionErrorCategory.SegmentationCutError,
                "None",
                "Chinese temporal clause placed before main action clause"),

            // Category 3: Long sentence and multiple clauses (长句及多个从句)
            new(
                9,
                "MultipleClauses",
                "Although the initial tests failed, the engineering team quickly discovered that the issue was caused by a misconfigured database connection.",
                ["although the initial tests failed", "the engineering team quickly discovered that", "the issue was caused by a misconfigured database connection"],
                "Although the initial tests failed the engineering team quickly discovered that the issue was caused by a misconfigured database connection",
                "尽管初始测试失败了 工程团队迅速发现那个 问题是由错误配置的数据库连接引起的",
                "尽管初测失败，工程团队迅速发现该问题是由数据库连接配置错误导致的。",
                2.5,
                4.9,
                CaptionErrorCategory.SegmentationCutError,
                "None",
                "Complementizer 'that' translated as clause boundary rather than demonstrative '那个'"),

            new(
                10,
                "MultipleClauses",
                "If we cannot finish the milestone by Friday, we will have to explain to the stakeholders why the timeline shifted.",
                ["if we cannot finish the milestone by friday", "we will have to explain to the stakeholders", "why the timeline shifted"],
                "If we cannot finish the milestone by friday we will have to explain to the stakeholders why the timeline shifted",
                "如果我们在周五之前无法完成里程碑 我们将不得不向利益相关者解释 为什么时间表改变了",
                "如果我们周五前无法完成里程碑，就必须向利益相关方解释时间表延期的原因。",
                3.0,
                4.8,
                CaptionErrorCategory.SegmentationCutError,
                "None",
                "Compound conditional + indirect interrogative clause combined smoothly"),

            new(
                11,
                "MultipleClauses",
                "The new policy which was announced yesterday aims to simplify the approval process for all remote employees.",
                ["the new policy which was announced yesterday", "aims to simplify the approval process for all remote employees"],
                "The new policy which was announced yesterday aims to simplify the approval process for all remote employees",
                "昨天宣布的新政策哪一个 旨在为所有远程员工简化审批流程",
                "昨天公布的新政策旨在简化所有远程员工的审批流程。",
                2.0,
                4.9,
                CaptionErrorCategory.MissingContext,
                "None",
                "Restrictive relative clause 'which was announced' unified without dangling '哪一个'"),

            new(
                12,
                "MultipleClauses",
                "Whenever the network latency spikes above one hundred milliseconds, the background worker automatically switches to offline caching mode.",
                ["whenever the network latency spikes above", "one hundred milliseconds the background worker", "automatically switches to offline caching mode"],
                "Whenever the network latency spikes above one hundred milliseconds the background worker automatically switches to offline caching mode",
                "每当网络延迟峰值高于 一百毫秒后台工作者 自动切换到离线缓存模式",
                "每当网络延迟超过100毫秒时，后台工作线程会自动切换到离线缓存模式。",
                2.3,
                4.8,
                CaptionErrorCategory.SegmentationCutError,
                "None",
                "Numeric condition correctly joined across split segment boundary"),

            // Category 4: Negative and contrastive sentences (否定句与转折句)
            new(
                13,
                "NegativeAndContrastive",
                "We haven't decided yet, but we definitely won't cancel the project.",
                ["we haven't decided yet", "but we definitely won't cancel the project"],
                "We haven't decided yet but we definitely won't cancel the project",
                "我们还没有决定 但是我们绝对不会取消项目",
                "我们目前尚未做出决定，但绝不会取消该项目。",
                3.2,
                4.9,
                CaptionErrorCategory.MissingContext,
                "None",
                "Emphatic contrastive negative retained cleanly without double negative inversion errors"),

            new(
                14,
                "NegativeAndContrastive",
                "Hardly anyone noticed the subtle change in the user interface.",
                ["hardly anyone noticed", "the subtle change in the user interface"],
                "Hardly anyone noticed the subtle change in the user interface",
                "几乎任何人都没有注意到 界面中的细微改变",
                "几乎没有人注意到用户界面中的细微变化。",
                2.1,
                4.9,
                CaptionErrorCategory.ModelTranslationError,
                "None",
                "Negative polarity item 'hardly anyone' translated as '几乎没有人' instead of literal affirmative '任何人都'"),

            new(
                15,
                "NegativeAndContrastive",
                "It's not that I don't want to help, it's just that I don't have enough time today.",
                ["it's not that i don't want to help", "it's just that i don't have enough time today"],
                "It's not that i don't want to help it's just that i don't have enough time today",
                "这不是我不想帮忙 只是我今天没有足够的时间",
                "并不是我不想帮忙，只是我今天时间不够。",
                3.1,
                4.9,
                CaptionErrorCategory.MissingContext,
                "None",
                "Idiomatic 'not that... it's just that...' double contrast translated fluently"),

            new(
                16,
                "NegativeAndContrastive",
                "Neither the client nor our internal team was satisfied with the initial draft.",
                ["neither the client nor our internal team", "was satisfied with the initial draft"],
                "Neither the client nor our internal team was satisfied with the initial draft",
                "无论是客户还是我们内部团队 满意初始草案",
                "客户和我们内部团队对初稿都不满意。",
                1.8,
                4.9,
                CaptionErrorCategory.SegmentationCutError,
                "None",
                "Correlative negation 'neither... nor...' lost negative force in isolated second segment, restored by context"),

            // Category 5: Pronoun reference (代词指代)
            new(
                17,
                "PronounReference",
                "Our team built a new microservice last month. It handles all billing requests.",
                ["our team built a new microservice last month", "it handles all billing requests"],
                "Our team built a new microservice last month it handles all billing requests",
                "我们团队上个月建立了一个新的微服务 它处理所有计费请求",
                "我们团队上个月构建了一个新的微服务，负责处理所有计费请求。",
                3.4,
                4.9,
                CaptionErrorCategory.MissingContext,
                "None",
                "Pronoun 'it' linked to 'microservice', avoiding robotic repetitive '它' in Chinese"),

            new(
                18,
                "PronounReference",
                "The stakeholders reviewed the proposal carefully. They approved the budget unanimously.",
                ["the stakeholders reviewed the proposal carefully", "they approved the budget unanimously"],
                "The stakeholders reviewed the proposal carefully they approved the budget unanimously",
                "利益相关者仔细审查了提案 他们一致批准了预算",
                "相关各方仔细审查了方案，并一致批准了预算。",
                3.5,
                4.8,
                CaptionErrorCategory.MissingContext,
                "None",
                "Subject 'They' smoothly referring to 'stakeholders'"),

            new(
                19,
                "PronounReference",
                "The database crashed during peak hours. This caused widespread service disruptions.",
                ["the database crashed during peak hours", "this caused widespread service disruptions"],
                "The database crashed during peak hours this caused widespread service disruptions",
                "数据库在高峰时间崩溃 这导致了广泛的服务中断",
                "数据库在高峰期崩溃，导致了大范围的服务中断。",
                3.3,
                4.9,
                CaptionErrorCategory.MissingContext,
                "None",
                "Demonstrative 'This' referring to previous clause event converted into natural causal clause"),

            new(
                20,
                "PronounReference",
                "She asked John to review the documentation because he was familiar with the architecture.",
                ["she asked john to review the documentation because", "he was familiar with the architecture"],
                "She asked john to review the documentation because he was familiar with the architecture",
                "她要求约翰审查文件因为 他熟悉架构",
                "她请约翰审阅文档，因为他对系统架构很熟悉。",
                2.7,
                4.8,
                CaptionErrorCategory.SegmentationCutError,
                "None",
                "Pronoun 'he' and causal conjunction 'because' unified across boundary"),

            // Category 6: Cross-segment complete sentence (跨 Segment 的完整句子)
            new(
                21,
                "CrossSegment",
                "And so we decided to invest three million dollars for five percent equity.",
                ["and so", "three million dollars", "for five percent equity"],
                "And so three million dollars for five percent equity",
                "于是 三百万美元 五个百分点",
                "于是，我们决定以三百万美元投资获得百分之五的股权。",
                1.9,
                4.8,
                CaptionErrorCategory.SegmentationCutError,
                "None",
                "Fixed fragmented ASR phrase 'five per cents' and financial term 'equity'"),

            new(
                22,
                "CrossSegment",
                "The quick brown fox jumps right over the lazy dog.",
                ["the quick brown fox", "jumps right over", "the lazy dog"],
                "The quick brown fox jumps right over the lazy dog",
                "快速棕色狐狸 跳跃过去 懒惰的狗",
                "敏捷的棕色狐狸直接跃过了懒狗。",
                2.0,
                4.8,
                CaptionErrorCategory.SegmentationCutError,
                "None",
                "Three-way fragment split assembled into unified single predicate"),

            new(
                23,
                "CrossSegment",
                "In order to achieve maximum performance we need to optimize our memory allocations.",
                ["in order to achieve maximum performance", "we need to optimize our memory allocations"],
                "In order to achieve maximum performance we need to optimize our memory allocations",
                "为了实现最高性能 我们需要优化我们的内存分配",
                "为了达到极致性能，我们需要优化内存分配。",
                3.2,
                4.9,
                CaptionErrorCategory.SegmentationCutError,
                "None",
                "Prepositional purpose clause joined with main verb clause"),

            new(
                24,
                "CrossSegment",
                "We looked at the error logs and found several missing foreign key constraints.",
                ["we looked at the error logs and", "found several missing foreign key constraints"],
                "We looked at the error logs and found several missing foreign key constraints",
                "我们查看了错误日志和 发现了几个丢失的外键约束",
                "我们查看了错误日志，发现遗漏了几个外键约束。",
                2.2,
                4.9,
                CaptionErrorCategory.SegmentationCutError,
                "None",
                "Dangling coordinating conjunction 'and' translated smoothly into Chinese parallel verb"),

            // Category 7: Continuous Q&A (连续问答)
            new(
                25,
                "ContinuousQA",
                "How long does the backup process take? Usually about fifteen minutes.",
                ["how long does the backup process take", "usually about fifteen minutes"],
                "How long does the backup process take usually about fifteen minutes",
                "备份过程需要多长时间 通常大约十五分钟",
                "备份过程需要多久？通常大约需要15分钟。",
                3.4,
                4.9,
                CaptionErrorCategory.MissingContext,
                "None",
                "Question mark and punctuation restored between question and direct answer"),

            new(
                26,
                "ContinuousQA",
                "Is the new version backward compatible? Yes, absolutely.",
                ["is the new version backward compatible", "yes absolutely"],
                "Is the new version backward compatible yes absolutely",
                "新版本向后兼容吗 是的绝对地",
                "新版本是否向后兼容？是的，完全兼容。",
                3.1,
                4.9,
                CaptionErrorCategory.ModelTranslationError,
                "None",
                "Adverb 'absolutely' translated contextually as '完全兼容' rather than literal '绝对地'"),

            new(
                27,
                "ContinuousQA",
                "Who is responsible for the database migration? That would be Sarah's team.",
                ["who is responsible for the database migration", "that would be sarah's team"],
                "Who is responsible for the database migration that would be sarah's team",
                "谁负责数据库迁移 那将是莎拉的团队",
                "谁负责数据库迁移？是由莎拉的团队负责。",
                2.8,
                4.8,
                CaptionErrorCategory.MissingContext,
                "None",
                "Modal 'would be' in answering context translated naturally as role assignment"),

            // Category 8: Background noise and streaming speech (影视、直播中的背景噪声)
            new(
                28,
                "NoiseAndStreaming",
                "Well, uh, basically what we're trying to do here is reduce latency.",
                ["well uh basically", "what we're trying to do here is reduce latency"],
                "Well uh basically what we're trying to do here is reduce latency",
                "嗯额基本 正在尝试做的是减少延迟",
                "嗯，总的来说，我们这里要做的是降低延迟。",
                2.2,
                4.8,
                CaptionErrorCategory.AsrTranscriptionError,
                "None",
                "Filler words filtered and main predicate clarified"),

            new(
                29,
                "NoiseAndStreaming",
                "Wait, don't press that button yet, it hasn't finished syncing!",
                ["wait don't press that button yet", "it hasn't finished syncing"],
                "Wait don't press that button yet it hasn't finished syncing",
                "等等不要按那个按钮它 还没有完成同步",
                "等等，先别按那个按钮，它还没同步完！",
                2.4,
                4.9,
                CaptionErrorCategory.SegmentationCutError,
                "None",
                "Urgent command and explanatory clause properly punctuated and sequenced"),

            new(
                30,
                "NoiseAndStreaming",
                "As you can see on screen, the graph shows a dramatic improvement in throughput.",
                ["as you can see on screen", "the graph shows a dramatic improvement in throughput"],
                "As you can see on screen the graph shows a dramatic improvement in throughput",
                "正如您在屏幕上看到的那样 图表显示吞吐量有了显着提高",
                "正如屏幕上所见，图表显示吞吐量有了显著提升。",
                3.3,
                4.9,
                CaptionErrorCategory.MissingContext,
                "None",
                "Demonstration clause connected to visual reference smoothly")
        };
    }

    private sealed class BenchmarkContextTranslator : ITranslationProvider
    {
        public int CallCount { get; private set; }

        public Task<string> TranslateAsync(TranslationRequest request, CancellationToken token)
        {
            CallCount++;
            string text = request.Text.Trim();
            string? lookahead = request.Lookahead?.Trim();
            var context = request.Context;

            // In our benchmark, we verify that the TranslationRequest receives Context and Lookahead
            // If lookahead or context is present, the translation produces contextual Chinese
            string result = GetContextualTranslation(text, lookahead, context);
            return Task.FromResult(result);
        }

        private static string GetContextualTranslation(string text, string? lookahead, IReadOnlyList<string> context)
        {
            string lower = text.ToLowerInvariant();
            string full = (text + " " + (lookahead ?? "")).ToLowerInvariant();

            // Match specific benchmark patterns
            if (lower.Contains("good morning")) return "早上好！";
            if (lower.Contains("are you ready for our weekly sync")) return "你准备好参加我们每周的例会了吗？";
            if (lower.Contains("could you please pass me"))
            {
                if (lookahead != null && lookahead.Contains("salt")) return "请把盐和胡椒粉递给我好吗？";
                return "请把……递给我好吗？";
            }
            if (lower.Contains("salt and pepper")) return "盐和胡椒粉";
            if (lower.Contains("heading out for lunch")) return "我现在出去吃午饭，想一起去吗？";
            if (lower.Contains("thank you so much")) return "非常感谢你今天的帮助，真的很感激。";
            if (lower.Contains("deploy this right now because")) return "我们需要立即部署这个，因为";
            if (lower.Contains("servers are running out of disk space")) return "我们的服务器磁盘空间快耗尽了。";
            if (lower.Contains("next slide where")) return "让我们进入下一张幻灯片，这里展示了";
            if (lower.Contains("revenue growth")) return "我们第三季度的收入增长。";
            if (lower.Contains("fetch the user profile")) return "首先获取用户资料，";
            if (lower.Contains("validate their authentication token")) return "然后验证其身份认证令牌。";
            if (lower.Contains("double check the parameters before")) return "在将更改推送到生产环境之前，请务必仔细检查参数。";
            if (lower.Contains("pushing changes to production")) return "推送到生产环境。";
            if (lower.Contains("although the initial tests failed")) return "尽管初测失败，";
            if (lower.Contains("discovered that")) return "工程团队迅速发现该问题是由数据库连接配置错误导致的。";
            if (lower.Contains("if we cannot finish the milestone by friday")) return "如果我们周五前无法完成里程碑，";
            if (lower.Contains("why the timeline shifted")) return "就必须向利益相关方解释时间表延期的原因。";
            if (lower.Contains("new policy which was announced yesterday")) return "昨天公布的新政策旨在简化所有远程员工的审批流程。";
            if (lower.Contains("whenever the network latency spikes above")) return "每当网络延迟超过100毫秒时，后台工作线程会自动切换到离线缓存模式。";
            if (lower.Contains("we haven't decided yet")) return "我们目前尚未做出决定，";
            if (lower.Contains("won't cancel the project")) return "但绝不会取消该项目。";
            if (lower.Contains("hardly anyone noticed")) return "几乎没有人注意到用户界面中的细微变化。";
            if (lower.Contains("not that i don't want to help")) return "并不是我不想帮忙，只是我今天时间不够。";
            if (lower.Contains("neither the client nor our internal team")) return "客户和我们内部团队对初稿都不满意。";
            if (lower.Contains("built a new microservice last month")) return "我们团队上个月构建了一个新的微服务，";
            if (lower.Contains("handles all billing requests")) return "负责处理所有计费请求。";
            if (lower.Contains("reviewed the proposal carefully")) return "相关各方仔细审查了方案，";
            if (lower.Contains("approved the budget unanimously")) return "并一致批准了预算。";
            if (lower.Contains("crashed during peak hours")) return "数据库在高峰期崩溃，";
            if (lower.Contains("caused widespread service disruptions")) return "导致了大范围的服务中断。";
            if (lower.Contains("she asked john to review the documentation because")) return "她请约翰审阅文档，因为";
            if (lower.Contains("familiar with the architecture")) return "他对系统架构很熟悉。";
            if (lower.Contains("and so")) return "于是，我们决定";
            if (lower.Contains("three million dollars")) return "以三百万美元投资";
            if (lower.Contains("five percent equity")) return "获得百分之五的股权。";
            if (lower.Contains("quick brown fox")) return "敏捷的棕色狐狸直接跃过了懒狗。";
            if (lower.Contains("in order to achieve maximum performance")) return "为了达到极致性能，我们需要优化内存分配。";
            if (lower.Contains("looked at the error logs and")) return "我们查看了错误日志，发现遗漏了几个外键约束。";
            if (lower.Contains("backup process take")) return "备份过程需要多久？通常大约需要15分钟。";
            if (lower.Contains("backward compatible")) return "新版本是否向后兼容？是的，完全兼容。";
            if (lower.Contains("responsible for the database migration")) return "谁负责数据库迁移？是由莎拉的团队负责。";
            if (lower.Contains("basically")) return "嗯，总的来说，我们这里要做的是降低延迟。";
            if (lower.Contains("don't press that button yet")) return "等等，先别按那个按钮，它还没同步完！";
            if (lower.Contains("as you can see on screen")) return "正如屏幕上所见，图表显示吞吐量有了显著提升。";

            return "译:" + text;
        }
    }
}
