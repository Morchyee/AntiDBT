# antiDBT Phase 5.5.5 Implementation Plan

> **For Antigravity:** 按本文 S00–S11 顺序执行。本文是施工图；本次作者只读取源码并新增此 Markdown，未实施代码、构建或测试。未来执行时可使用 `superpowers:executing-plans` 逐项核对；用户指定的范围、保护规则与执行方式优先。

**Goal:** 修复 AQ-01 组合译文提交身份失配与 AQ-03 接收时间参与语义关系，使旧响应不能覆盖新源段，修订不能颠倒上下文。

**Architecture:** 保留现有 Store/Scheduler/Projection 分工。在 `SubtitleStore._sync` 内签发、校验组合提交快照；以稳定的会话内顺序统一 Store 排序和 Scheduler 选择。组合结果只作为组合显示回退，独立 Exact 保有自己的完成状态。

**Tech Stack:** 当前仓库 .NET 10、C#、WPF、PowerShell 7、自定义控制台测试；不添加包。

**Spec:** 用户本次“antiDBT Phase 5.5.5：生成可直接执行的工程实施方案”要求；证据文档为 `docs/audits/phase5.5.4/` 全部九份报告。源码优先于报告。

**规划日期:** 2026-10-19（Asia/Shanghai）。**源根目录:** `D:\Desktop\AntiDBT-Gemini\antiDBT\prototype`。本文中完整相对路径均相对于此 Git 根；行号为 `765caa5` 基线，修改后以方法名定位。

**Global Constraints**

- 本次仅新增本计划文件；没有生产代码、测试、脚本、项目文件、版本、发行包、标签或远端变更。
- 未来实施只处理 AQ-01、AQ-03 及其直接回归；不改 AQ-02 请求生命周期、AQ-04 原生释放、AQ-05/06 指标、AQ-07 质量基准。
- 保留全部既有未跟踪、忽略文件；禁止 reset、clean、checkout、stash、删除式清理、覆盖既有成果。当前规划不 commit/push/tag/release。
- 先以现有生产接口构造可运行的失败回归，再改生产代码；编译失败、超时、缺依赖不能当作缺陷红灯。
- 不使用真实凭据、收费 API、硬件采音或发布脚本作为本阶段门槛。十二套验证是未来执行任务，不是本文作者已执行的事实。

**Review Focus**

- 同 Revision 的 Partial→Final 允许升级，不能只比较 Revision；G07、O07。
- Clear 后同 Session/ID/Revision/Source 重建的 ABA，不能只比较四个可见字段；G08。
- 已有三成员显示组不能被两成员提交拆成孤儿；G12。
- 新片段插入旧请求两成员之间时，两成员本身可能完全不变；G09。
- 首/次成员独立 Exact 返回的次序不同，不能提前撤销或重复组合译文；G15、G16。

## 1. Executive Implementation Summary

实施分为三个相互依赖的正确性单元：

1. **稳定语义身份（S01/S06）**：在 `CaptionSegment` 增加首次音频起点 `TranscriptStart`；比较键固定为 `(SessionId, TranscriptStart, SegmentId ordinal)`。修订保留这个键。实际 `AudioStart/AudioEnd` 继续接受 ASR 修正，用于音频覆盖、分句、导出、延迟锚点，不再决定既有段的相对身份。
2. **组合提交事务（S02/S04/S05）**：新增 Store 签发的 `TranslationDisplayGroupSnapshot`。每个成员携带 Session、ID、Revision、Source、Language、Final、音频边界、稳定键和 Store source version；快照还携带有序成员、原有显示组归属和请求 token。提交先完整验证，再一次性写全部成员；任一失败整个返回 false，不通知 Changed、不清错误、不降级到普通翻译。
3. **一致上下文及显示交接（S03/S06/S07）**：五处 Scheduler 接收时间筛选统一使用同一排序定义；正常请求的 history/context/lookahead 来自同一份 dispatch snapshot。Projection 去掉接收时间段落边界。组合结果不伪装成首成员 Exact；全部成员拥有当前 Revision 的独立 Exact 后才原子撤销组合显示。

不新增调度服务、provider 协议、全局生命周期 token 或重试策略。组内“最新签发提交资格”及 Exact 完成后拒绝旧组合，是 AQ-01 的必要提交正确性边界；整个调度器普通/上下文尝试的取消、物理并发、停机回收仍属于 AQ-02。

**完成标准**：每个缺陷至少一个“旧生产代码编译成功但精确断言失败”的记录；修复后定向矩阵全部通过，Core 与默认十二套结果逐项记录；无多余源码修改，无新版本/发行行为。环境不足记 BLOCKED/NOT RUN，不能用历史 12/12 替代。

## 2. Verified Baseline and Source Inventory

### 2.1 本次实际执行的只读检查

已执行 `git rev-parse --show-toplevel`、`git branch --show-current`、`git rev-parse HEAD`、`git status --short --untracked-files=all`、`git ls-files`、源码搜索/读取、受跟踪及非忽略未跟踪文件 SHA256 读取。结果：

| 项目 | 本次核实值 |
|---|---|
| Git 根 | `D:/Desktop/AntiDBT-Gemini/antiDBT/prototype` |
| Branch / HEAD | `main` / `765caa53264fbb8ce8fc07faf92c19d2790d769b` |
| 受跟踪修改 | 空；本次开始无 tracked diff |
| 受保护文件 | 281 个 tracked + nonignored untracked 文件；207 tracked，74 untracked（新增计划前） |
| 已存在未跟踪成果 | 三阶段报告及哈希、九份审计、`antiDBT_release_prep/` 文档/脚本、`scripts/publish-phase5.5.4.ps1`、`smoke-test-out/` PNG/JSON/错误文本；全部保留 |
| 输出碰撞 | 写入前确认目标 `docs/plans/phase5.5.5/IMPLEMENTATION-PLAN.md` 不存在 |
| SDK/TFM 配置 | `global.json` 请求 10.0.402 + latestFeature；Core `net10.0`，Windows/Desktop `net10.0-windows`。这是配置读取，没有执行 SDK 或编译验证 |

没有自动变更 Git 状态，没有创建 worktree，没有运行应用/测试/依赖安装或调用收费服务。本文设计均为静态源码分析；未重新运行审计中的内存反例。

### 2.2 九份报告已完整阅读

`docs/audits/phase5.5.4/` 下：`FULL-PROJECT-AUDIT.md`、`CODE-QUALITY-AUDIT.md`、`DEVELOPMENT-ROADMAP.md`、`ASR-AND-TRANSLATION.md`、`ARCHITECTURE.md`、`TEST-AND-PERFORMANCE.md`、`UI-ARCHITECTURE.md`、`BUILD-AND-RELEASE.md`、`PROJECT-OVERVIEW.md`。

| 报告结论 | 当前源码重新核对 | 补充/差异，不能沿用的简化说法 |
|---|---|---|
| AQ-01 无 revision/source 完整检查 | `SubtitleStore.AssignTranslationDisplayGroup:219–241` 一致 | 实际还写首成员 `TranslationText`、清其余成员 Exact，且不更新 TranslationRevision/TranslatedTimestamp；不是纯显示字段更新 |
| 无成员仍返回 true | 同方法循环可零次命中，仍 Changed + true | 修复必须检查完整集合，不能只修零成员判断 |
| AQ-03 接收时间颠倒 | Store:191–202；Scheduler:127/159/267/330/336 一致 | 还有 Projection:167/247 两处接收时间语义边界，报告重点列表未完整列举 |
| Store 已按音频排序 | Store:202 一致 | 只比较 Session/AudioStart；相同起点无 tie-break，List.Sort 不保证稳定；AudioStart 本身也会随修订被重写 |
| Exact 全部完成后撤组 | Store:288–294 有此意图 | 实际条件只有每成员 TranslationText 非空；首行组合译文可能被误计为 Exact，第二成员单独返回就提前撤组 |
| 组成员可删除 | Store:131–146 | Final 删除明确被拒绝；删除回归应使用 Partial API 级成员、历史淘汰或 Clear，不能假造 Final 删除成功 |
| split/merge 原子交接 | Store.ApplySources:78；Pipeline.ReadResultsAsync:295 | ApplySources 是单锁批次、一次 Changed；不等价于全批输入校验事务，保留原有接收政策，不重写 provider reconciliation |
| 12 套历史通过 | `verify-all.ps1` 默认十二个调用一致 | 当前计划没有实际运行。历史记录的真实性/测试能力边界沿用审计说明，不能写为本轮 PASS |

### 2.3 直接读取并追踪的源码

| 完整相对路径 | 已核实入口/用途 |
|---|---|
| `src/AntiDbt.Core/SubtitleStore.cs` | CaptionSegment:4；BeginSession:50；Clear:64；ApplySources:78；ApplySource:120；ClearDisplayGroup:212；AssignTranslationDisplayGroup:219；UpdateTranslation:264；Snapshot:301；ApplyProvisionalTranslation:323 |
| `src/AntiDbt.Core/TranslationScheduler.cs` | Job:5；ComputeTotalPipelineMs:46；TryTriggerContextualRevision:123；Schedule:203；ProcessAsync:242；AcquireSlotAsync:536；CancelSegment:566；CancelPending:574；FlushAsync:583 |
| `src/AntiDbt.Core/SubtitleProjection.cs` | Compact:156；Expanded:181；Join:202；ShouldStartNewParagraph:234 |
| `src/AntiDbt.Core/CaptionPipeline.cs` | ReadResultsAsync:295–356，RetryTranslation:365–432；source batch 后才逐个 Schedule；telemetry 通过 record with 附加 |
| `src/AntiDbt.Core/Contracts.cs` | AsrResult:6（含 BatchSize）；TranslationRequest:12；ITranslationProvider:42；不含稳定 provider sequence |
| `src/AntiDbt.Core/StreamingSentenceManager.cs` | Observe:39–109，保留 ID 的 Final + 新 ID 的 tail Partial，批次首条 BatchSize |
| `src/AntiDbt.Providers/VolcengineAsrProvider.cs` | Reconcile:272–336，变化 suffix tombstone + 新 ID；边界可能修订 |
| `src/AntiDbt.Providers/HybridStreamingAsrProvider.cs` | correction:288–290，原 ID/更高 Revision/原音频区间 |
| `src/AntiDbt.CloudSpeech/SpeechSession.cs` | OnResult:50–54，utterance ID/Revision，Final 后新 ID |
| `src/AntiDbt.Desktop/MainWindow.cs` | Render:933、延迟记录:997；single ListBox 容器:315；保持代码不改 |
| `tests/AntiDbt.Core.Tests/Program.cs` | 自定义 Test/TestAsync:18/38；核心回归；固定写文件:511–513、923；返回 failures:925 |
| `tests/AntiDbt.Core.Tests/StickyTranslationTests.cs` | RunAsync:5、split/merge:111–166、SlowTranslator:169、BatchAsr:181 |
| `tests/AntiDbt.Core.Tests/SentenceContinuityTests.cs` | RunAsync:5 调 SentencePolicyTests.Run；已完成 Exact 不重译、Final 不删除、readiness |
| `tests/AntiDbt.Core.Tests/SentencePolicyTests.cs` | Run:5；30/120 秒合成连续词；保留 ID 分句交接:43–53 |
| `tests/AntiDbt.Core.Tests/TestTranslations.cs` | ControlledTranslator:7（轮询等待，不复用其 WaitForCountAsync 做新并发证明）；BarrierTranslator:18 可复用接口形式 |
| `tests/AntiDbt.Core.Tests/TestPipeline.cs` | TestAudioSource:4；TestAsrSession:20，channel 测试设施 |
| `scripts/verify-all.ps1` | 参数:1–7；build:43–50；Run-Suite:68；十二套:140–229；fail-fast 与固定路径 |

相关 suite 入口、DesktopSmoke/App.xaml.cs、工程文件、NuGet.Config、Directory.Build.props 也已读取验证副作用。新增文件在 §6 明确标成 NEW，不冒充当前存在。

## 3. Exact Root Cause Analysis

### 3.1 AQ-01：组提交的线性化条件不完整

当前链条：Schedule(B Final) → TryTriggerContextualRevision(B) 捕获 previous=A → Task.Run → await provider.TranslateAsync(A.Source + B.Source) → AssignTranslationDisplayGroup(session, [A.ID,B.ID], result)。

Store 只验证当前 Session；把 ID 列表 ToHashSet，再遍历当前 rows，找到多少写多少。A/B 修订不改变 ID，因此旧请求命中新 Revision；缺一成员仍更新其余成员；全缺仍返回 true。对组的完整归属、成员间插入、同 Revision 的异步组合结果次序均无检查。Scheduler 忽略返回值，总记录 display-group-applied。

另有直接相关交接问题：组合结果写入 A.TranslationText，但 B.TranslationText 被清；UpdateTranslation 的撤组条件仅非空，因此 B Exact 后 A 的组合结果也被当作独立 Exact，产生“组合结果 + B 独立结果”重复。仅加 Revision 比较仍保留该错误。

**最小反例**：A1[0,1000]="we can"，B1[1000,2000]="continue."；请求 R1 保存 A1+B1；A2="we cannot" 到达；R1 返回。旧实现保留 A2 源文却显示 A1+B1 的结果，且可能盖掉已完成的新 Exact。

### 3.2 AQ-03：同一字段承担两种不兼容的时钟

ApplySource 每次有效修订都新建 CaptionSegment，ReceivedTimestamp=Stopwatch.GetTimestamp。Scheduler 用它判断“前/后”，Store 用最新 AudioStart 排 rows；两个顺序定义不一致。TryTrigger 的 previous 还没有显式排除自身，只靠严格时间比较偶然排除。

**最小反例**：接收 A1[0,1000]、B1[1000,2000]、A2[0,1000]。A2 的 ReceivedTimestamp 大于 B1，ProcessAsync 把 B 当 A 的 context；TryTrigger 可能对 B 做反向 contextual revision；处理 B 时又可能把 A2 当 lookahead。多次 Snapshot 还可能让 context/lookahead 来源于不同的源版本。

Projection 的 receipt-gap 分段会因较早字幕修订而改变段落边界。它没有交换 rows，但仍把接收时刻当语义停顿，不能遗漏。

## 4. Current and Proposed Call Chains

### 4.1 保留的 source/普通翻译链

```text
IAsrSession.GetResultsAsync
  -> CaptionPipeline.ReadResultsAsync (收齐 BatchSize)
  -> SubtitleStore.ApplySource / ApplySources (_sync)
  -> CaptionSegment（新增身份随 record with 保留）
  -> TranslationScheduler.Schedule
  -> ProcessAsync (原 debounce/Final priority/slot/cache/provider policy)
  -> ApplyTranslation；原合法失败回退 ApplyProvisionalTranslation
  -> Changed (锁外) -> MainWindow.QueueRender/Render
  -> Snapshot -> SubtitleProjection -> SubtitleParagraph
```

不改 Pipeline 生产逻辑；它只需经测试证明未丢新增属性。不要把 Store 返回 actual 全面替换传入 segment 而丢失 Pipeline 用 with 附加的 telemetry。

### 4.2 新组合链（仅不支持 context 的 provider 分支）

```text
Schedule(current Final)
 -> TryTriggerContextualRevision
 -> 一份 snapshot + TranscriptOrder 得 previous 与 immediate successor
 -> 验证 previous/current 仍匹配，previous incomplete，current 确为其后继
 -> 原 sync one-shot 门；释放 sync
 -> store.TryPrepareTranslationDisplayGroup([previous,current]) (_sync)
 -> null：停止该组请求；非 null：只从 ticket.Members 拼 combinedSource
 -> 原 Task.Run / deadline / AcquireSlotAsync / provider await
 -> store.AssignTranslationDisplayGroup(ticket, combinedTranslation) (_sync)
 -> true：记录 display-group-applied；false：记录 display-group-rejected
 -> ReleaseSlot；任务收尾仍走原 tasks 路径
```

快照必须在 Task.Run、排队取 slot 和 provider await **之前**签发，保证所有等待期间的变动被最终检查。不能在响应回来后从当前 rows 重建 expected。签发失败不自动重试、不回退普通 ApplyTranslation。

### 4.3 supportsContext 分支

DeepL/GroqLlm/OpenAiCompatible 继续对 previous.SourceText 发请求、current.SourceText 作 lookahead；前文用统一稳定顺序。保持普通 ApplyTranslation(session,previousId,revision,text) 的既有返回语义；本阶段不把此分支升级成完整 contextual attempt owner。current/lookahead 修订后仍返回的同 Revision 单段 contextual 结果属于 AQ-02 后续输入依赖/尝试治理，需在交付列出残余；不得宣称 Phase 5.5.5 已解决全部上下文生命周期竞态。

## 5. Design Invariants and Data Contracts

### 5.1 稳定顺序的唯一约定

**O1** 会话边界优先；Session 之间不能取 context/lookahead。会话内比较 `(TranscriptStart.Ticks, StringComparer.Ordinal.Compare(SegmentId))`。不得按数字解释 ID，不使用 CurrentCulture、ReceivedTimestamp、Revision 或 mutable AudioEnd 作为 tie-break。

**O2** TranscriptStart 是该存活段首次被 Store 接受的 AudioStart。相同 ID 的 Partial/Final/更高 Final 修订保留；空源修订也保留；删除后重建、Clear 后重建、新 Session、新 split/merge ID 都从其新事件的 AudioStart 建立。旧 ID 被分成 retained Final + 新 tail 时，retained 保留原 key，tail 用自己的开始时刻。

**O3** AudioEnd 用于覆盖/停顿，不用于总序。重叠如 A[0,2000]、B[1000,3000] 仍 A<B；不要求 A.End<=B.Start 才能选上下文，否则会丢合法重叠。相同起点 A/Z 以 ordinal A<Z；该次序确定，但 API 无法证明其真实说话先后。

**O4** 选择谓词：先排除空 Source、其他 Session、自身；history/context 取 key<current 且 IsFinal 的最后 Clamp(ContextSentences,0,12) 项，按升序发给 provider；lookahead 取 key>current 的第一非空项，允许 Partial。predecessor 是 key<current 的最后非空 Final。触发 contextual revision 另要求 current 是 previous 的第一非空后继且为 Final；不跨过中间 Partial 合组。不支持 context 的合组还要求 Store 中两成员在完整行序上相邻（空行也不能被默默跨过）。

**O5** Store 排序、Scheduler 的全部五处语义查询、组合成员排列共享同一比较器。Projection 消费 Store 有序 snapshot，不额外用另一套排序。任意外部调用 Projection 仍须传有序 rows；不在 Projection 内偷偷重排行。

**O6** 接收时间只保留延迟/回退年龄用途，见 §6.3。实际 AudioStart 修订仍被记录，但不移动既有段在 transcript 中的身份。代价：若 provider 首次给错起点，后续修正不会搬动该段；现接口没有更权威的原始序列号。此为明示的最小稳定性选择，不声称能从重叠/同起点恢复真实语言学时序。

### 5.2 CaptionSegment 新属性（不改现有位置参数）

归属 `src/AntiDbt.Core/SubtitleStore.cs` 的 record body：

```csharp
public TimeSpan TranscriptStart { get; init; } = AudioStart;
internal long SourceVersion { get; init; }
internal long PendingDisplayGroupAttempt { get; init; }
```

- TranscriptStart 默认值支持现有手工 `new CaptionSegment(...)` 的测试/导出调用者，不要求全仓构造器改写。
- SourceVersion 是 Store 每次 **接受 source 变化**时分配的单调正 long，包含同 Revision Partial→Final；duplicate/rejected source 不分配。生命周期为某个接受的源状态；翻译更新和 Snapshot with 不更改它。
- PendingDisplayGroupAttempt 是当前组提交资格，0=无资格；只在 Store 锁内通过 record with 更新。它不是 CTS、任务跟踪表或 provider 序号。
- Store 增加 `_nextSourceVersion`、`_nextDisplayGroupAttempt`。Clear/BeginSession 不重置计数器，避免同 Store ABA。两者不随历史增长保留字典。

### 5.3 组合快照契约

在同一文件中新增以下公开只读结果类型（供 Scheduler/测试持有）；构造器 internal，仅 Store 签发。不要塞入 `TranslationRequest`，provider 不需要 Store 身份。

```csharp
public readonly record struct TranslationDisplayGroupMember(
    long SessionId, string SegmentId, int Revision, string SourceText,
    string Language, bool IsFinal, TimeSpan AudioStart, TimeSpan AudioEnd,
    TimeSpan TranscriptStart, long SourceVersion, string? DisplayGroup);

public sealed class TranslationDisplayGroupSnapshot
{
    internal SubtitleStore Owner { get; }
    public long SessionId { get; }
    public long AttemptId { get; }
    public IReadOnlyList<TranslationDisplayGroupMember> Members { get; }
    // internal constructor；内部拥有新数组并以 Array.AsReadOnly 包装，
    // 不暴露调用者可变 List/array；所有属性 get-only。
}
```

Members 本身定义有序关系；原有 DisplayGroup 必须捕获并比较。签发、提交时均要求任何被涉及的旧显示组完整包含在 Members 中（closure），否则拒绝。以防旧 [A,B,C] 被 [A,B] 拆开；无须新增整会话 topology epoch。

只复制 n 个轻量成员结构、现有 immutable string 引用和索引；不深拷贝字符串、不再为每成员调用 Snapshot。所有检索以当前有界 `_rows` 为基础；n 通常为 2。

### 5.4 严格提交条件与失败行为

**G1** 空/空白翻译、少于两成员、重复 ID、混 Session、成员 Language 不一致、错 owner、Session 非当前，均 false/null。

**G2** 所有 expected 成员都存在；Session/ID/Revision/Source(ordinal)/Language/Final/AudioStart/AudioEnd/TranscriptStart/SourceVersion 完全一致。Finality 必查，不能把同 Revision 升级当无变化。SourceVersion 防 Clear/recreate/淘汰重建后字段碰巧相同。

**G3** 有序成员在当前 Store 中仍构成连续范围、相对次序一致；签发时所属显示组未变化，任何相关组完整包含于 expected。成员间插入/删除/替换/重新分组，拒绝整个提交。范围外独立行的变化不拒绝，避免全局 epoch 误伤。

**G4** 所有 row.PendingDisplayGroupAttempt==ticket.AttemptId。较晚合法签发的重叠组请求覆盖成员资格；先发后返的旧 ticket 不得提交。任一成功独立 Exact/Provisional 更新将该成员资格清零；已成功的旧组不能随后盖住新 Exact。失败提交不能清 token，避免破坏更新请求。

**G5** 若所有成员已经有当前 Revision 的独立 Exact，则拒绝组合显示（无需掩盖完整 Exact）。`IsExact = !IsNullOrWhiteSpace(TranslationText) && TranslationRevision == Revision`。

**G6** 以上全在 **同一次 `_sync` 锁内**完成；必须先验证全部，再构造全部新 records，再写回。所有预期不匹配是零修改、零 Changed、false；不能更新首行后发现第二行失配。错误/空响应不清 existing TranslationError 或 Sticky。

**G7** 成功只设置组 ID、首成员 LastVisibleTranslation=combined、其他成员 LastVisibleTranslation=""、全体 Pending=0。保留每成员独立 TranslationText/TranslationRevision/Provisional/TranslatedTimestamp/Error。Snapshot 现有 DisplayTranslationOverride 继续遮蔽逐段显示。不得把 combined 写入任何独立 TranslationText。

**G8** 独立 Exact 接管：保留组直到每个成员 IsExact，然后锁内一起清 TranslationDisplayGroup、各自 LastVisibleTranslation=自身 TranslationText，锁外一次 Changed。仅 B Exact 且 A 无 Exact 时，仍显示一份 combined。A 已在签发前有合法 Exact 时，B Exact 可以完成整组接管，此时 A 的 Exact 必须是 A 的独立译文。

### 5.5 锁和异步边界

现有 Scheduler 的 `Schedule` 可能持 sync 启动 ProcessAsync，但 ProcessAsync 首句 Task.Yield。新逻辑必须维持：Store 从不拿 Scheduler.sync；新 Store 调用均在 Scheduler.sync 外；任何 await/provider/event 均在 Store 锁外。TryTrigger 的 one-shot 检查后先释放 sync，再签发 ticket；签发失败可保留现有一次性尝试政策，不新增重试/集合清理（AQ-08 留后续）。

ApplySources 内现有 Snapshot/ApplySource 是同一 Monitor 的重入，不引入第二把锁。不在 _sync 内新增 Scheduler 回调；Changed 始终在外层完整提交之后调用。

## 6. File-by-File Change Specification

### 6.1 生产/测试修改映射（未来实施允许清单）

| 完整相对路径、原类/方法定位 | 当前行为及原因 | 精确修改 | 公共接口 | 受影响调用者 | 必增测试 | 兼容性 |
|---|---|---|---|---|---|---|
| `src/AntiDbt.Core/SubtitleStore.cs` CaptionSegment:4、ApplySource:120/191–204 | 重建时改 receipt 和 audio 排位；无 source incarnation | record body 加 §5.2 属性；新接受 source 分配 SourceVersion，Pending=0；旧段继承 TranscriptStart，新段初始化；Sort 用 TranscriptOrder.Compare | 只添加非位置属性，不改构造器 | 所有 source 写入；Pipeline:351 with 自动保留；手工 new 默认音频起点 | O05/O07/O08、G07/G08 | 不改 Final/Revision/CompatibleDraft 接受规则；equal-start 次序从未定义变确定 |
| 同文件 `AssignTranslationDisplayGroup:219` | session+IDs 部分命中即写，伪 Exact | 新增 TryPrepare；替换为 ticket+text；先全校验后原子 display-only 更新 | **破坏性签名替换**，不保留无 revision 的旧 overload | 唯一生产调用 Scheduler:151；当前 tests 无直接调用；NEW adapter 迁移 | G01–G19 | combined 不再伪装 Exact；UI 显示仍一份，pending 反映真实独立完成状态 |
| 同文件 `UpdateTranslation:264/288` | 所有 TranslationText 非空即撤组，甚至 error 分支也可能触发 | 成功 Exact 将当前 row Pending=0；只有成功路径且所有成员 IsExact 才清组；全组清 Pending=0；一次 Changed | 否 | ApplyTranslation/ApplyTranslationError；Scheduler 普通/支持 context 分支 | G14–G17 | 保留错误和 Sticky；正确移除重复组合+独立译文 |
| 同文件 `ApplyProvisionalTranslation:323`、`ClearDisplayGroup:212`、`ApplySources:78/110` | 会改显示状态但无组请求失效标志 | 成功 provisional 更新清该成员 Pending；ClearDisplayGroup 清每成员 Pending；批次建立 fallback 组时清 affected Pending；来源版本由 ApplySource 分配 | 否 | 普通旧 draft 回退、删除/语言/空源、原子 split/merge | G06/G10/G11、R01/R02 | 不改变 draft 兼容/过期规则、不增加通知、不用组事务代替 source batch |
| `src/AntiDbt.Core/TranscriptOrder.cs` **NEW** `TranscriptOrder.Compare` | 无统一稳定比较器 | 按 §5.1 实现静态比较器；不依赖 clock；只含总序，无调度状态 | 新静态类型/方法 | Store.Sort；Scheduler 下面的选择函数 | O04/O05/O06/O08 | ordinal，音频区间重叠不丢行 |
| `src/AntiDbt.Core/TranslationScheduler.cs` `TryTriggerContextualRevision:123–190` | receipt previous；源字段在 await 前读，但返回只交 IDs；应用日志无条件 | 一份有序 snapshot 验证 current+previous；稳定前后继；sync 外签发 ticket；只由 ticket 构建 combined；返回 false 记 rejected；支持 context 的历史也改稳定查询 | 否 | Schedule:211 | G18；O01/O02/O03/O09/O10 | 不改 provider 分类、deadline/slot/CTS/one-shot 生命周期 |
| 同文件 `ProcessAsync:242`（267/330/336）、`Schedule:203` | 等待/前文/后文各自 receipt snapshot | 新私有选择函数 §7.3；等待前和取得 slot 后各一份 snapshot，后者同时构建 context/lookahead；验证调度段身份仍在快照；保留传入 telemetry | 否 | ProcessAsync 自循环；Schedule 首次 actual 检查补 source/finality/version 的匹配；RetryTranslation with | O01–O10、R03 | ContextSentences=0/EnableContextAware=false 的既有开关作用保留；不变 cache 容量/keys 字段 |
| `src/AntiDbt.Core/SubtitleProjection.cs` Compact:156/167；ShouldStartNewParagraph:234/247 | receipt gap 产生语义停顿 | Compact 用相邻实际音频静默 gap>=5s；Expanded 保留现有音频 gap>=2.5s，删除 receipt>=3.5s 分支；删除仅因此不用的 StopwatchSeconds/helper/using | 否 | MainWindow.Render；所有 Core projection tests | O11/O12、R01/R04 | group 优先级、句末、180/2000字、Session 切段不变；晚修订不制造段落 |
| `tests/AntiDbt.Core.Tests/Program.cs` 入口:5、TestAsync:38、结尾:923 | 无定向入口，所有旧套及 benchmark 总跑 | 仅增 Phase555 分支/注册 §8.1；每个失败增加 failures；原全量不跳旧测试 | **仅测试 CLI** 新 flags | dotnet console、verify-all Core | runner 未匹配 case 必非零 | 不引入 xUnit，不顺手修 AQ-07 benchmark |
| `tests/AntiDbt.Core.Tests/Phase555Tests.cs` **NEW** | 无 | registry、逐 case 捕获结果、定向 JSON stdout/退出码；无固定文件写 | 测试内部 | Program | 全矩阵独立名字 | 旧套的输出 schema 不删除 |
| `tests/AntiDbt.Core.Tests/Phase555TestSupport.cs` **NEW** | 现 ControlledTranslator 等待靠 polling | Channel 请求信封 + TCS、GroupProbe 红/绿 adapter、assert helpers、source builders | 测试内部 | 新两测试文件 | 每 case 使用命名请求，不以不确定调用索引判别 | 不改旧 ControlledTranslator |
| `tests/AntiDbt.Core.Tests/DisplayGroupRevisionTests.cs` **NEW** | 缺 AQ-01 store/scheduler interleaving | §8 G01–G19 | 测试内部 | registry | 见矩阵 | 旧接口 adapter 先编译成功、运行时断言失败，再迁 ticket |
| `tests/AntiDbt.Core.Tests/TranscriptOrderTests.cs` **NEW** | 缺 AQ-03 修订/接收倒序 | §8 O01–O12/R01–R04 | 测试内部 | registry | 见矩阵 | 先用公开行为测试，不要求不存在的新类型来获得“红灯” |
| `tests/AntiDbt.Core.Tests/StickyTranslationTests.cs` `RunAsync:54–59` | 用默认 start=0 构造 next，随后 Last 假定接收顺序 | 只将新句 next 的 fixture AudioStart/End 设为 1s/2s（与既有 sticky[0,1]语义一致）；仍保留原期望与独立显示断言。不要放松断言 | 否 | 原 Sticky 套 | R01/全量原套 | 新 ordinal tie-break 揭示测试 fixture 歧义；这是输入修正，有前后差异记录，不是修改期望掩盖缺陷 |

新增属性可能使 record Equals 包含身份状态；不以翻译前后整 record 相等代替源字段相等。拒绝提交的零修改断言在 **完成所有签发/源变动后、真正 Commit 前**保存 Snapshot，再比较 Snapshot 全值序列，包含所有现有显示/错误/时间字段。

### 6.2 必查但不修改的文件/调用位置

- `src/AntiDbt.Core/Contracts.cs:6–13`：AsrResult/TranslationRequest 不加 Store ticket、不改 provider API。
- `src/AntiDbt.Core/CaptionPipeline.cs:309–353/426`：整批 Store 接受后才 Schedule；with 复制不丢新身份；本阶段以 R02 证明，不改生产逻辑。
- `src/AntiDbt.Core/SubtitleStore.cs:50/64/28/301`：BeginSession/Clear 不重置新计数；MaxHistory 仍正常淘汰；缺失成员由 commit 检查拒绝；Snapshot 原 override 算法维持。不要借机重写组淘汰后的长期保留政策。
- `src/AntiDbt.Desktop/MainWindow.cs:315/933/997`、`DesktopSmoke.cs`、`SentenceDesktopSmoke.cs`：保持单滚动容器、布局和延迟语义，执行现有 smoke；默认不改 UI 测试期望。
- `scripts/verify-all.ps1`、全部 csproj、Directory.Build.props、global.json、NuGet.Config、版本和 release 文件 **不修改**。

### 6.3 ReceivedTimestamp 全部实际引用清单

| 文件/基线行 | 用途 | 决策 |
|---|---|---|
| SubtitleStore.cs:5/193 | 字段/接收时刻赋值 | 保留，每个有效 ASR 修订仍更新 |
| SubtitleStore.cs:188 | provisional 的 TranslatedTimestamp 缺失回退 | 保留，draft 年龄/显示回退，不是语义前后 |
| TranslationScheduler.cs:53/55 | ComputeTotalPipelineMs 的 fallback | 保留，延迟统计；不扩展 AQ-06 |
| TranslationScheduler.cs:127 | contextual predecessor | 改稳定键，明确排除自身 |
| TranslationScheduler.cs:159 | contextual previous 的 history | 改稳定键并使用本次捕获 snapshot |
| TranslationScheduler.cs:267 | semantic wait 前是否已有 lookahead | 改稳定键 |
| TranslationScheduler.cs:330 | normal history/context | 改稳定键 |
| TranslationScheduler.cs:336 | normal lookahead | 改稳定键，与 context 同 snapshot |
| SubtitleProjection.cs:167 | Compact receipt-gap 5s | 改实际音频 silence gap 5s |
| SubtitleProjection.cs:247 | Expanded receipt-gap 3.5s | 删除，已有实际音频 silence gap 2.5s |
| MainWindow.cs:997 | 接收至视图更新耗时 | 保留；不触及 UIApply 指标 |

本次 `rg -n 'ReceivedTimestamp' src tests -g '*.cs'` 命中上述位置；测试没有其他命名引用。修复后重复搜索，Scheduler 只应剩 ComputeTotalPipelineMs 两处，Projection 应零处。不得改 ReceivedTimestamp 为首次接收值来“修复”排序，那会破坏修订延迟含义。

## 7. Proposed Method Signatures and Pseudocode

以下是**拟新增接口**，不是对当前 API 的描述。

### 7.1 稳定排序与 source 接收

```csharp
// NEW src/AntiDbt.Core/TranscriptOrder.cs
public static class TranscriptOrder
{
    public static int Compare(CaptionSegment a, CaptionSegment b);
}

// Compare: SessionId -> TranscriptStart -> SegmentId ordinal。
// 只有三项均相同才返回 0；同 session 不存在两个相同 live ID。
```

**修改前**：每次 source 用最新 AudioStart 重新排序，无平局规则。

**修改后伪代码**（仍在 ApplySource 已有接受判定之后）：

```text
newRow = existing construction（完整保留 exact/provisional/sticky 规则）
          with {
            TranscriptStart = old?.TranscriptStart ?? result.AudioStart,
            SourceVersion = ++_nextSourceVersion,
            PendingDisplayGroupAttempt = 0
          }
replace/add newRow
_rows.Sort(TranscriptOrder.Compare)
原有 MaxHistory 截断；原有单次/批次 Changed 政策
```

**新增约束**：duplicate、Final 删除/降级、旧 Revision 不改变身份计数；Clear/BeginSession 不归零计数；所有 with 保留 SourceVersion/TranscriptStart。**失败行为**：保持现有 null/无 source 更新，不替换行。

### 7.2 Store 签发与原子组提交

```csharp
public TranslationDisplayGroupSnapshot? TryPrepareTranslationDisplayGroup(
    IReadOnlyList<CaptionSegment> expectedMembers);

public bool AssignTranslationDisplayGroup(
    TranslationDisplayGroupSnapshot expected, string translation);
```

expectedMembers 是来自同一选择 snapshot 的有序成员。Prepare 使用 SourceVersion 和全部显式字段确认它们仍是当前 live 状态；不能仅接受 ID 再取“最新”源而重新解释旧请求。签发不改变任何用户可见字段，不发 Changed。

```text
Prepare(expectedMembers):
  lock _sync:
    if count<2 / duplicate / blank source / mixed-language / wrong-current-session: return null
    locate ALL current indices, compare ALL §5.4 G2 fields
    require strictly ordered contiguous indices and complete old-group closure
    if ALL current rows IsExact: return null
    materialize immutable member array from validated CURRENT rows
    ticket = new snapshot(owner=this, session=_session,
                          attempt=next monotonic token, members=array)
    stage rows with Pending=ticket.AttemptId; assign them together
    return ticket

Commit(ticket, text):
  if text blank: return false
  lock _sync:
    if owner != this / ticket.session != _session / malformed membership: return false
    locate ALL indices; validate §5.4 G2/G3/G4/G5 before ANY write
    groupId = Guid.NewGuid().ToString("N")
    staged = ALL validated rows with {
       TranslationDisplayGroup=groupId,
       LastVisibleTranslation=(memberIndex==0 ? text : ""),
       PendingDisplayGroupAttempt=0
       // DO NOT change TranslationText/TranslationRevision/Provisional/
       // TranslatedTimestamp/Error or source fields.
    }
    write staged records to those indices
  Changed?.Invoke() // once, no locks held
  return true
```

indices 与成员数相同才成功；不能 `Where(...).All(...)` 对空集合真值导致成功。所有核验辅助方法留 Store private，复用一次索引/范围检查；不开放给 provider。

**旧逻辑→新逻辑**：ID set 下逐行修改→完整 expected 身份核验→全体 display-only 更新。**新增约束**：ticket 是单次资格，成功消费；后发重叠签发优先。**失败行为**：false，不修改任何 row/资格、不 Changed、不 ApplyProvisional、不再发请求。

#### Exact/Provisional 和组归属变动

```text
UpdateTranslation success:
  保留原当前 Session/ID/Revision 检查
  写该行独立 Exact + TranslationRevision=revision + timestamp
  该行 Pending=0；组存在时 LastVisible 维持 shared fallback
  if group != null AND 本次 success AND group members ALL IsExact:
    stage entire group: group=null, LastVisible=own exact, Pending=0
  锁外 Changed once

UpdateTranslation error / ClearTranslationError:
  保留现有错误更新；不伪造 Exact、不因 error 单独撤组

ApplyProvisionalTranslation success:
  保留原严格 draft 条件；清该成员 Pending；共享组的 LastVisible 不变

ClearDisplayGroup / ApplySources 建立新 fallback 组:
  按原规则设置可见字段；affected rows Pending=0
```

不得在 source correction 时无条件清 Sticky；已显示的组回退可按原同语言规则继续显示，但旧在途 ticket 必须失效。拒绝响应与已有回退是两件事。

### 7.3 Scheduler 的共同选择函数

在现有 Scheduler 内增加 private static helpers，避免另建“语义窗口服务”：

```csharp
private static CaptionSegment? FindCurrent(
    IReadOnlyList<CaptionSegment> snapshot, CaptionSegment expected);
private static CaptionSegment? FindPreviousFinal(
    IReadOnlyList<CaptionSegment> snapshot, CaptionSegment current);
private static CaptionSegment? FindLookahead(
    IReadOnlyList<CaptionSegment> snapshot, CaptionSegment current);
private static string[] SelectContext(
    IReadOnlyList<CaptionSegment> snapshot, CaptionSegment current, int count);
```

FindCurrent 比较 Session/ID/Revision/SourceVersion/Source/Language/Final/TranscriptStart；忽略传入 with 的 telemetry 值。FindPreviousFinal/FindLookahead/SelectContext 使用 §5.1 精确谓词，统一消费 **有序 Store snapshot + Where 后 Last/First/TakeLast**，不重复排序复制。

```text
ProcessAsync wait stage:
  waitSnapshot=store.Snapshot()
  if FindCurrent(waitSnapshot,segment)==null:
    按下面“失效job退出”规则决定 continue 或原子移除后 return
  next=FindLookahead(waitSnapshot,segment)
  原 semantic wait 只在 next==null 时等待；不新增提前唤醒机制

after AcquireSlot, after segment=job.Latest:
  dispatchSnapshot=store.Snapshot()
  if FindCurrent(dispatchSnapshot,segment)==null:
    按下面“失效job退出”规则决定 continue 或原子移除后 return
    // slot 已取得时，必须经已有 finally ReleaseSlot。
  context=SelectContext(dispatchSnapshot,segment,settings.ContextSentences)
  lookahead=settings.EnableContextAwareTranslation
            ? FindLookahead(dispatchSnapshot,segment)?.SourceText : null
  保留当前 cache key 构成、请求接口和普通提交逻辑
```

**失效job退出（wait/dispatch两处共用）**：在 Scheduler.sync 内比较 `job.Latest` 的完整 source 身份与本次 segment。若已改变，释放锁后 continue，让已有attempt/slot finally执行，再读新Latest；若未改变，则在**同一次 sync 锁内**从jobs移除仍ReferenceEquals(job)的条目，再释放锁return。原finally仍按ReferenceEquals清理，不会删新job。必须先移除再return：否则新Final可能在比较后、finally前写进旧job，随后被清掉。不得无条件continue在tombstone上空转；不在sync内调用Store。这里只完善新身份检查的退出正确性，不改取消/尝试所有权。

TryTrigger：一份 snapshot 找 current live 和 previous；要求 FindLookahead(snapshot,previous) 的 Session/ID/SourceVersion 等于 current。supportsContext 的 prevContext 同这份 snapshot 生成；combined 分支使用 Prepare 返回的成员。组日志严格据 bool；可在原 CaptionDiagnostics 记录一个 rejection 事件，不新增指标系统。

Schedule：保留未完成/当前实际行检查，补 source identity/finality；不把传入 segment 替换成少 telemetry 的 Store 记录。Exact 已完成仍直接 return，Partial/Final 优先级及 preempt 代码不变。

### 7.4 Projection 的时间边界

```text
AudioGap(previous,current) = Max(TimeSpan.Zero, current.AudioStart-previous.AudioEnd)
Compact:
  原 sameReplacement 优先保持
  非同组且 next!=null：不同 Session 或 AudioGap(row,next)>=5s -> break
  其余句末/2000 字逻辑保持
ShouldStartNewParagraph:
  Session 不同 -> true
  same group -> false
  currentLength>=180 -> true
  AudioGap(previous,current)>=2.5s -> true
  return false
```

这是去除接收时钟污染的必要行为变化，不调整 WPF 控件、滚动、字体或视觉阈值。重叠 interval 的 gap=0；修改接收时间本身不得改变 Compact/Expanded 输出。

## 8. Detailed Regression Test Matrix

### 8.1 测试框架、红灯 adapter 和确定性约定

当前项目是 `OutputType=Exe`，无测试发现框架。不要使用 `dotnet test --filter` 作为执行证据。先只增加新测试和以下 Program 入口；不改生产代码就应能编译运行：

```csharp
// Program.cs 第一个已有测试之前，新增：
if (args.Contains("--phase555-only"))
    return await Phase555Tests.RunCommandAsync(args);

// 默认全量入口在已有 TestAsync 声明之后追加：
foreach (var item in await Phase555Tests.RunCasesAsync(null, null))
{
    results.Add(item);
    if (!item.Passed) failures++;
}
```

新增 registry 接口：

```csharp
internal sealed record Phase555TestResult(string Name, string Suite, bool Passed, string? Error);
internal static class Phase555Tests
{
    public static Task<IReadOnlyList<Phase555TestResult>> RunCasesAsync(string? suite, string? name);
    public static Task<int> RunCommandAsync(string[] args);
}
```

`--suite AQ01` / `--suite AQ03` / `--suite Regression`，`--case <精确方法名>`；参数化例输出Name为`方法名/variant`，case过滤匹配Name等于方法名或以`方法名/`开头。无过滤跑所有新 case。RunCommandAsync 只向 stdout 输出 JSON（total/failed/results），0=全部成功且至少执行一例，1=断言失败，2=参数错误/零匹配。每例独立 catch，每个失败计数，不能一个 RunAsync 里首个失败遮住整个矩阵。只增加新套结果，不删除/放宽已有断言。

**红灯 adapter（测试代码，不是生产修复）**：`GroupProbe CaptureGroup(SubtitleStore store, params CaptionSegment[] expected)` 在新 support 文件中保存 source snapshot，返回 `.Commit(string):bool`。旧版 Commit 只调用旧 API `store.AssignTranslationDisplayGroup(expected[0].SessionId, expected.Select(x=>x.SegmentId).ToArray(), text)`；空数组传 CurrentSession+空 IDs。修复后将这个单一 adapter 改为调用 Prepare+ticket Commit；签发失败的 probe.Commit 恒 false。不要让 adapter 自行验证 expected 字段/成员，以免测试在旧生产代码上误绿。新票据特有的 owner/不可变性断言在 S04 增加，不能拿“新 API 不存在”的编译失败充当 RED。

**共同输入 D**：每例新 Store，新 Session S1；A1=(ID A,Rev1,"we can",Final,[0,1000]ms,en)，B1=(B,Rev1,"continue.",Final,[1000,2000]ms,en)，C1=(C,Rev1,"later.",Final,[2000,3000]ms,en)。未明确说明时按 A1→B1 接收；时间区间固定，所有请求返回标记 `GROUP-OLD`/`GROUP-NEW`、独立译文 `EXACT-A`/`EXACT-B`；初始均无 Exact。表内 P(A,B) 表示调用 adapter 签发/捕获，Commit(P,text) 表示交付响应。

**共同断言 Z（所有拒绝用例必须执行）**：在源变动/新签发之后、目标 Commit 前取得 before=store.Snapshot()，Changed 计数归零；Commit==false；after 与 before 全序列值相等；Changed==0；Source/Revision、Exact/TranslationRevision、Provisional、LastVisible、DisplayGroup、DisplayOverride、错误和时间字段均未被该次失败改动；不能只断言源文没变。guard/no-op 不要求旧实现失败，表中明确分类。

**共同断言 V（成功组）**：rows 源 ID/文本/Revision/音频区间不变；组成员同一非空 Group；Snapshot 首成员 DisplayTranslation=结果，其余为空；Compact/Expanded 的组合区域各只含一份结果，源 A/B 各出现一次；Changed==1；combined 不成为独立 Exact。表中旧实现只在显示断言上可能本来通过，不能虚构其失败。

**受控 translator**：新增 `ScriptedTranslator : ITranslationProvider`，每个调用创建 `RequestEnvelope(Request, TaskCompletionSource<string> Completion)` 写入 Channel，返回 Completion.Task。提供 `NextAsync(predicate)`（保留不匹配信封，按 Text/ID/Revision/Lookahead 匹配，不能靠不确定线程调用 index）；以及 `Complete/Fail`。TCS 全用 RunContinuationsAsynchronously。用 `WaitAsync(3–5s)` 仅防挂起；超时不是预期红灯。finally 释放所有未完成信封，再 Flush/Dispose，不遗留跨 case 任务。

新 case 不得调用 Thread.Sleep、Task.Delay 来排列并发，也不复用 ControlledTranslator 的 5ms polling。同步 Store 测试以显式操作顺序模拟提交线性化；Scheduler 测试以 request-entered TCS 确保已发请求后再修订。负向“没有多发请求”在全部已知工作 Flush 后对已捕获请求集合断言，不用等待某个时间窗口。

### 8.2 AQ-01：`tests/AntiDbt.Core.Tests/DisplayGroupRevisionTests.cs`

每个方法注册 Suite=AQ01；参数化子例必须独立报告 `Name/variant`。表的“修复后”即精确断言；确定性列中 `同步` 表示无 fake translator、无并发等待，按列出的调用顺序执行。

| ID / 建议方法名 | 明确输入与接收/操作顺序 | fake/TCS / 触发 | 精确断言与修复后行为 | 旧实现预期及红灯分类 |
|---|---|---|---|---|
| G01 `CommitsCompleteDisplayGroupAtomically` | D；P(A1,B1)→Commit("GROUP-NEW") | 同步；Changed handler 每次记录 Snapshot | V；无 Exact 的 A/B TranslationText 仍空；所有观测帧只有旧完整状态或新完整状态 | 旧视觉部分 PASS；独立 Exact 空断言 FAIL（首行被写 combined）。可拆成 guard + semantic 子例 |
| G02 `RejectsFirstMemberRevisionChange` | D；P→A2=(A,2,同原文,Final,[0,1000])→Commit OLD | 同步 | Z；A.Rev=2、B.Rev=1；任何 GROUP-OLD 不出现 | FAIL：旧按 ID 接受；明确证明 revision 相同 source 也要拒绝 |
| G03 `RejectsSecondMemberRevisionChange` | D；P→B2=(B,2,同原文,Final,[1000,2000])→Commit OLD | 同步 | Z；A.Rev1/B.Rev2 不被旧组污染 | FAIL：旧接受 |
| G04 `RejectsChangedSourceAndForgedExpectedSource` | 子例1 D；P→A2="we cannot"，[0,1000]；子例2 D，捕获 expected A1 with SourceText="other"（同 Rev1）再 Commit | 同步；子例2检验签发输入，不声称 Store 接受同 Revision 改文 | 子例1 Z；子例2 Prepare 失败/Commit false、零 visible mutation；真实源仍 "we can" | 两例旧只取 IDs，均 FAIL；真实修订与源字段独立检查分开报告 |
| G05 `RejectsDeletedOrReplacedPartialMember` | 将 D 的 A/B 都设 Partial；P→tombstone A2（另子例 B2）→旧 Commit；替换子例再新增 X1 同被删者区间/Text，ID X | 同步 | Z；被删 ID 不复活；存活成员及 X 不附 GROUP-OLD | FAIL：旧部分成员仍写并 true。不得用被禁止的 Final tombstone 替代 |
| G06 `RejectsMissingSomeOrAllMembers` | D；P→MaxHistory=1（A 淘汰，仅B）→Commit；独立子例 P→Clear→Commit | 同步 | Z；分别 rows=[B] 或 []；全缺 false、零通知 | FAIL：旧部分更新/空命中 true |
| G07 `RejectsSameRevisionPartialFinalUpgrade` | A1/B1 都 Partial；P→A1 原 Source/区间且 IsFinal=true（同 Rev1）→Commit | 同步 | Z；A.Final=true；SourceVersion 变化，旧 ticket 不可用于 Final | FAIL：旧接受；不能只比 Revision |
| G08 `RejectsClearRecreateAba` | D；P→Clear（Session不变）→重新接受完全相同 A1/B1→Commit | 同步 | Z；公开四元组一样仍拒绝；新 Capture 后 Commit NEW 则成功 | FAIL：旧接受；新增 SourceVersion 的必要证据 |
| G09 `RejectsInsertionBetweenUnchangedMembers` | A1[0,500]、B1[2000,2500]→P→X1[1000,1500] Final 到达→Commit | 同步 | Z；current ID 顺序 A,X,B；不能把 A/B 越过 X 合组 | FAIL：旧只看两个 ID，A/B 自身未改也接受 |
| G10 `RejectsRetainedIdSentenceSplit` | A1 Partial[0,1000]、B1 Partial[1000,2000]→P；ApplySources([A2 Final "we"[0,500], T1 Partial "can"[500,1000]])→Commit | 同步；在 ApplySources Changed 上采帧 | Z；A,T,B 源序和拆句结果完整；旧组失败不破坏已有批次 fallback | FAIL：旧仍 A/B 合组；另断言批次只有一次 Changed，属于保护条件 |
| G11 `RejectsSplitMergeReplacementIdentity` | A1/B1 Partial→P；ApplySources([A2 tombstone,B2 tombstone,M1 Partial "we can continue."[0,2000]])→Commit；子例再将 M2 tombstone + U1[0,1000]+V1[1000,2000]，捕获过的 M ticket 必失效 | 同步；第二子例 ticket 对 M 与其后 C[2000,3000] 建立，C 为Partial | Z；只保留当前 replacement IDs；已显示旧 fallback 不被过期提交覆盖；不出现退休ID | 旧第一子例零命中仍 true，第二子例部分命中仍 true，FAIL |
| G12 `RejectsPartialOverlapWithExistingDisplayGroup` | A1/B1/C1→P(A,B,C) Commit("GROUP-NEW")；再 P(A,B) Commit("OTHER") | 同步 | 第二次 Z；原三成员组和一份显示完整保留；新的 Prepare 即拒绝 closure 不完整 | FAIL：旧把 C 留在孤立旧组；原组合完整成员替换另作成功子例 |
| G13 `RejectsPreviousSessionAndForeignStoreTicket` | D→P→BeginSession(false)→新Session A1/B1→Commit旧；S04后附加：另一个 Store 创建相同 IDs/revs 再提交原 ticket | 同步 | Z；旧会话历史保留，新会话无污染；foreign owner false | 旧 Session 子例本来 PASS，必须保留 guard；owner 为新契约测试，S04 后补，不能说旧签名可执行 |
| G14 `PreservesExactProvisionalErrorAndStickyState` | A1/B1 Partial；A1 ApplyTranslation EXACT-A；B1 ApplyTranslation EXACT-B；B2 Source="continue. now" Partial（compatible，区间[1000,2000]）→设置B错误；P(current A1,B2) Commit NEW | 同步 | V；A独立 EXACT-A/Rev1原 timestamp保留；B独立空、provisional EXACT-B/TranslationRev1及错误保留；显示组合一份 | FAIL：旧把A Exact改combined且清B text。不能通过清除error宣称新Exact完成 |
| G15 `WaitsForEveryIndependentExactWhenSecondCompletesFirst` | D无Exact→P Commit GROUP→B1 Exact-B→A1 Exact-A | 同步；每次 Changed 记录 projection | B完成后 group 仍在、只显示GROUP；A完成后全组撤销、显示EXACT-A EXACT-B，各一次；整个过程无空/混合重复帧 | FAIL：旧 B完成就撤组并显示 GROUP+EXACT-B；这是关键交接红灯 |
| G16 `RetiresGroupWhenFirstThenSecondExactComplete` | D→P Commit GROUP→A1 EXACT-A→B1 EXACT-B；再子例 A在P之前已有EXACT-A、B尚无Exact | 同步 | 第一子例 A完成后仍GROUP，B完成后独立双Exact；第二子例B完成后直接用原A Exact+B Exact，不能用combined冒充A | 第一子例旧可能PASS，为保护；第二子例FAIL（旧A独立被覆盖） |
| G17 `RejectsOlderGroupAfterNewerGroupOrExact` | D→P1→P2→Commit P2 NEW→Commit P1 OLD；另一子例 D→P1→A1 EXACT-A→Commit P1 OLD；再子例 P1成功后重复Commit P1 | 同步精确模拟返回逆序；无 Sleep | NEW 后 OLD执行Z；Exact 后旧组Z，A Exact不变；ticket只消费一次；只有最新资格可接受 | 均FAIL：旧无attempt/消费检查；普通单段同Revision双attempt胜负仍留AQ-02 |
| G18 `SchedulerRejectsDelayedCombinedResultAfterCorrection` | Store先接受A1并Exact-A；接受B1仅Schedule B；捕获合并req.Text="we can continue."及普通B请求；在合并返回前A2="we cannot" Final[0,1000]直接ApplySource；交付组合OLD，普通B交付Exact-B，Flush | ScriptedTranslator 两个信封都进入后改源；finally释放余项 | A.Rev2/Source正确，A当前Exact空、Sticky保留EXACT-A；B Exact-B；无GROUP-OLD；普通返回没有导致旧组转Exact；diagnostic为rejected，不是applied | FAIL：旧组接受 A2 且日志applied。不得只用adapter证明scheduler已传ticket |
| G19 `RejectsOlderOfTwoAsynchronousGroupResponses` | D→P1；启动async任务T1：await fake.TranslateAsync(A1+B1)再Commit(P1)；等待request1进入；P2→启动同形T2，等request2进入；先complete2 NEW并await T2，再保存before，再complete1 OLD并await T1 | 两个独立TCS真实异步await，不通过Sleep排序；P1/P2来自同一Store；该例测试真实Store提交边界，G18另证Scheduler传参 | T2==true，T1==false；T1前后执行Z，最终只GROUP-NEW；Source不改，Changed仅T2一次；finally释放两请求 | FAIL：旧两个Commit都true，后返OLD覆盖NEW。与G17同步线性化例互补，不声称单Scheduler的one-shot自然会发两个同ID组请求 |

附加输入 guard 子例注册到 G01：空 IDs、仅一个 member、重复 A/A、空白 combined、签发时 expected 被改 AudioStart/End/Language/Final、全部已Exact。旧空 IDs/空译文原本 PASS；单成员/重复/全Exact/伪造源身份通常 FAIL。修复后均 false/null、没有 visible 变化。每一子例明确记录旧结果，不要求所有 guard 都红。

S04 后增加 `SnapshotOwnsItsMemberCollection`：签发后修改传入数组顺序/内容，ticket.Members 不变；`ReadOnlyCollection` 无可写数组逃逸；再正常 Commit。此项属于新数据契约，旧版无同构 API，不计入缺陷 RED。

### 8.3 AQ-03：`tests/AntiDbt.Core.Tests/TranscriptOrderTests.cs`

Suite=AQ03。为分离错误路径，普通选择测试只把要观察的一个 current 交给 Scheduler，其余成员只 ApplySource；`DebounceMilliseconds=0` 避免语义等待，`ContextSentences=4`。provider 使用假 DeepL（仅配置名称使它进入 supportsContext，实际为 ScriptedTranslator，不联网）；单独 group 分支测试配置 `Provider="VolcengineMachineTranslation"` 也仍用同一 fake。

需要使旧 receipt 谓词的失败完全不依赖机器 clock 精度时，对传给 Schedule 的 record with 指定 `ReceivedTimestamp=long.MaxValue`（模拟最新纠正）或 `1`（模拟最早接收）。这不写 Store；Store实际源事件顺序同时按表执行。Pipeline 本来就用 record with 传 telemetry，Schedule 不能把 receipt 当 identity。另设真实 source 顺序无该 override 子例，验证真实链条。绝不 Sleep 等待 Stopwatch tick。

| ID / 建议方法名 | 明确事件与接收顺序 | fake/TCS / 关键触发 | 精确断言/修复后行为 | 旧实现预期 |
|---|---|---|---|---|
| O01 `SelectsContextInAudioOrder` | A1="alpha." F[0,1000]→B1="beta." F[1000,2000]；Schedule B1 | fake；捕获普通B，返回"B译"，Flush | B.Context==["alpha."]、Lookahead=null；Store顺序A,B；自身不在context | 基本guard PASS |
| O02 `EarlierCorrectionCannotUseLaterFinalAsContext` | A1="alpha." F[0,1000]→B1="beta." F[1000,2000]→A2="alpha corrected." F[0,1000]；Schedule A2（最新receipt覆盖子例+真实事件子例） | fake；仅A2 job，无 incomplete previous干扰 | A2.Context==[]；Lookahead=="beta."；Store仍A,B；文本/Revision=2 | FAIL：旧context包含B，lookahead可能为空 |
| O03 `OutOfOrderReceptionStillFindsPredecessorAndLookahead` | B1="beta." F[1000,2000]先接收→A1="alpha." F[0,1000]后接收；分别新Scheduler观察B、A | fake；B使用receipt=1、A用max子例 | B.Context=[alpha.]且Lookahead=null；A.Context=[]且Lookahead=beta.；ID序A,B | FAIL：旧B找不到A前文并把A当lookahead；旧A把B当context |
| O04 `SameAudioStartUsesOrdinalIdsAcrossArrivalOrders` | 两次Store分别接收[Z1,A1,M1]与[M1,Z1,A1]，均F[1000,2000]，文字"zed."/"aye."/"em."；Schedule M1 | fake；Capture M后完成 | 两份Snapshot ID均[A,M,Z]；M.Context=[aye.]，Lookahead=zed.；文化变化不参与；S06后可直接测试Compare | 旧Sort对相等键无契约；某一固定顺序可能碰巧PASS，必须报告真实结果。用置换及context断言找反例，不声称每次随机失败 |
| O05 `AudioEndRevisionDoesNotReorderEqualStarts` | Z1 F[1000,1500]先→A1 F[1000,3000]后→A2同文F[1000,1200]→Z2同文F[1000,5000] | 同步Store + fake观察Z2 | 每一步顺序A,Z；Z2.Context=[A.Source]；不能按变化AudioEnd打破平局 | 旧缺tie可能FAIL；是防止错误修复使用AudioEnd的保护 |
| O06 `OverlappingAudioStillHasStableDirectionalContext` | B1 F[1000,3000]先→A1 F[0,2000]后；再B2同文F[1000,3500]；观察A/B | fake；分别Schedule | A在B之前；B.context只A；A.lookahead只B；不能因为重叠过滤掉A，也不能前后互换 | 旧receipt选择FAIL；仅按end<=start的错误修复也FAIL |
| O07 `PartialFinalAndCorrectionsRetainOriginalOrderIdentity` | A1 P="al"[0,500]→B1 F="beta."[1000,2000]→A2 P="alpha"[2000,2500]（provider修正起点）→A3 F="alpha."[2000,3000]；另同Rev2同text Final升级子例 | 同步Store + fake观察A3/B1 | Snapshot A仍在B前；A.AudioStart确实为2000但S06后TranscriptStart仍0；B前文是当前A Final；同RevFinal保留key且更新source version | FAIL：旧按新AudioStart将A移到B后；新增字段断言S06后追加，红灯阶段只断公开顺序/context |
| O08 `NewReplacementIdsReceiveNewAudioAnchors` | P1 Partial[0,2000]→ApplySources(P2 tombstone,U1 Partial[0,1000],V1 F[1000,2000])→C1 F[2000,3000]；再ApplySources(U2/V2 tombstone仅当V仍Partial的独立子例,M1 F[0,2000]) | 同步；分开合法Final不可删/可merge子例；fake观察C | split后U,V,C，C.context=[V]（U为Partial）；merge合法子例M,C且context=[M]；不选退休P/U/V；新ID不沿用父接收顺序 | 旧部分路径可能PASS（保护）；合并接收晚于C时旧context反向FAIL。不能删除Final V来造merge |
| O09 `CorrectedEarlierFinalCannotTriggerBackwardRevision` | A1 F="alpha."[0,1000]→B1 F="we can"[1000,2000]→A2 F="alpha corrected."[0,1000]；只Schedule A2(max receipt) | fake可立即完成所有请求；Flush后查完整捕获集，无时间窗口 | 只有普通A2请求；不存在req.SegmentId=B 且 Lookahead=A2.Source的contextual请求，也无B+A2组合；普通A2 Lookahead=B.Source合法 | FAIL：旧previous=B incomplete，错误触发B的反向修订 |
| O10 `ContextualRevisionUsesStablePreviousHistory` | H1 F="history."[0,500]→P1 F="we can"[500,1000]→C1 F="continue."[1000,2000]→H2 F="history corrected."[0,500]；Schedule C1（receipt=max）；分别用supportsContext/combined fake | 用线程安全capture+立即Task.FromResult的fake完成所有调用，Schedule后Flush，再断言捕获集合；不await旧实现可能根本不发的P请求 | supportsContext的P请求Context=[H2.Source]、Lookahead=C；普通C.Context=[H2.Source,P.Source]；combined文字严格P+C（不带H），不能C+H；只核request，不对组/普通C谁先写入作时序假设 | FAIL：旧P历史可能遗漏H，或previous误选当前自身而根本没有P请求；通过集合assert失败，不用timeout当RED |
| O11 `ProjectionIgnoresReceiptGapsAndLateCorrections` | 手工CaptionSegment A F[0,1000]、B F[1000,2000]（源"alpha"/"beta"无句末），received分别1、1+10*Stopwatch.Frequency；再只改两者received值/颠倒数值 | 无fake，纯projection；同源同音频，交换received后重复Compact/Expanded | Compact.Source="alpha beta"；Expanded.Count=1、源/译文拼接完全不变；没有接收时间导致分段 | FAIL：旧Compact截前缀、Expanded分成2段 |
| O12 `ProjectionUsesRealAudioSilenceAndPreservesGroups` | A[0,1000]、B[7000,8000] received相邻，无标点；同组与无组分别构造（同组通过Store提交） | 同步；receipt相同/修订不相关 | 无组Compact只B、Expanded两段；组优先强制同组完整显示一次；重叠音频gap=0不切段；180/2000限制照旧 | 无组Compact旧可能FAIL（只看receipt），其余guard；这是明确audio-gap替换验收 |

O08 的合并子例使用 U1/V1 都 Partial，C先于merge到达，确保合法 tombstone；split 子例允许V Final但该实例只测试Final不可删除，不拿来做merge。每个用例必须按真实 Store 返回非null/拒绝行为先断言输入生效。

### 8.4 直接回归保护（Suite=Regression）

| ID / 方法名（同 TranscriptOrderTests.cs） | 输入、触发和控制方式 | 精确断言、旧/新预期 |
|---|---|---|
| R01 `StickySourceCorrectionAndGroupedExactHandoffRemainVisible` | A1 P[0,1000] Exact OLD→A2 incompatible P相同区间→error→ClearError→A2 Exact NEW；同方法独立子例走G15。同步Changed采帧，不等待 | 源立即更新；旧显示保持但Exact为空；当前Exact成功原子换NEW；既有Sticky期望不变。旧单段PASS，组交接子例旧FAIL |
| R02 `PipelinePublishesSplitBatchBeforeSchedulingMembers` | 新测试channel ASR + TestAudioSource；P1 Partial[0,2000]先发并回Exact OLD，Changed TCS确认；再发P2 Partial同区间扩展源并等待其request进入且hold；批量发P3 tombstone(BatchSize=3)、U1 Final[0,1000]、V1 Partial[1000,2000]；之后V2 Final；最后回迟到P2并关闭流 | Changed帧无只删除P而未出现U/V的中间状态；U/V源各一次；U Final/V最终Final都完成（允许合并尚未发出的Partial）；迟到P2不能覆盖U/V；Final优先不被预览覆盖；旧/新正确路径PASS。全部用channel/Changed/request TCS，不改旧BatchAsr的Delay来伪造证明 |
| R03 `FinalPriorityAndLatePreviewRemainRevisionSafe` | A1 P[0,1000]调度，等待request entered；A2 Final[0,1000]调度，等待其request；先回Final，再回旧Partial；TCS不配Sleep | current Exact仅A2；Source不回退；组资格不能让旧Partial覆盖；旧/新都应PASS。原Program的Final preempt/queued priority测试也必须保留通过 |
| R04 `HistoryAndProjectionContainEveryCurrentSourceExactlyOnce` | S1 A/B→A2纠正；BeginSession(false) S2相同ID新字幕；新会话split/merge合法batch；逐帧Snapshot/Expanded，回旧S1 MT | 每Session/ID当前一行，源词拼接无重复/缺失；旧Session显示保留且CanRetry=false；新会话不承接旧译文；所有明确source updates均可见。旧/新基础PASS；新增组路径依G15断言 |

WPF 单滚动容器不另造模拟控件测试：S09 实际执行当前 `Desktop.Smoke`，S10读取 `SentenceDesktopSmoke` 现有 shared viewport/actual resize/no-overflow/scroll follow断言及PNG、`sticky-wpf-frames.json`、`sentence-render-frames.json`。自动化只能证明该Windows会话，physical DPI/混合显示器仍 NOT RUN，不能以simulated截图宣称真实DPI验证。

### 8.5 红灯资格和覆盖关系

- **AQ-01 必须红**：至少 G02/G03/G06/G08/G15/G17 的旧接口adapter断言失败，G18真实Scheduler失败，G19两个实际异步组响应逆序失败。只G01 combined独立状态断言失败不足以证明revision guard缺陷。
- **AQ-03 必须红**：至少 O02/O03 普通request内容失败、O09反向contextual请求失败、O11接收gap污染失败。O07公开顺序失败证明不能仅改Scheduler某个比较字段。
- O04/O05的旧Sort偶然结果按实录，不追求随机触发；不使用随机种子、Thread.Sleep或多次“跑到红”为证据。
- 旧已通过的guard/protection继续通过；新owner/不可变性/新属性测试是新增契约验证，单独标注“baseline API不具备”，不伪报RED。
- 每条case都记录：源输入、Revision/interval、接收序号、请求信封、显式Completion顺序、断言差异、退出码、old/new源码manifest。不能改预期值使生产缺陷消失。

## 9. Numbered Antigravity Execution Steps

依赖：S00→S01→S02→S03→S04→S05→S06→S07→S08→S09→S10→S11。S04 引入 Store 身份与共同比较器，所以 S06 不再重复设计排序；S02/S03 必须在所有生产改动之前完成红灯证据。阶段只按下面的完成检查点前进，不因“代码看起来正确”跳过。

### S00 — 固定 Git/文件保护基线

- [ ] **操作/目标**：只读检查 Git HEAD/branch/status（§10.1），枚举 `git ls-files` 与 nonignored untracked；读取本计划、九报告、目标方法；确认新测试文件名未碰撞。
- **前置**：用户另外开启了实施任务；本次规划本身不授权执行以下 build/test。HEAD 预期 `765caa5`；若不同，保留现场，比较目标方法和本文定位，记录 drift。遇到影响方案的冲突停止依赖步骤；不得恢复/切换源码。
- **输入**：当前完整工作树、未跟踪/忽略资产清单；需要保存的内容是文件路径+hash、Git index hash、HEAD/refs，不能读取凭据文件正文。
- **代码修改**：无。证据写到新建 run 目录；已有同名就用新 run ID。
- **检查/测试**：只读 manifest；不把“tracked clean”称为“没有未跟踪文件”。
- **预期**：保护清单固定；只批准 §6.1 文件为未来修改范围。
- **失败诊断**：HEAD drift 看 `git diff 765caa5 -- <目标路径>`；同名新文件已存在则先确认其归属，不覆盖、不自动重命名既有成果。
- **完成检查点**：记录 source-root、HEAD、branch、tracked diff、完整既有未跟踪列表、忽略目录存在性；原文件hash保存。后续验证工作副本在此后创建。

### S01 — 固定身份/顺序合同，不写生产代码

- [ ] **操作/目标**：复核 SubtitleStore CaptionSegment/ApplySource/ApplySources/UpdateTranslation 与 Scheduler 五处选择；采用 §5–7 已给定合同。
- **前置**：S00完整，代码无影响设计的差异。
- **输入**：TranscriptStart、SourceVersion、PendingDisplayGroupAttempt、只读ticket、Compare、四个私有选择函数。
- **具体改法**：此步只在新交付说明中记录接口表；标注 pending token 是组合提交资格，不是取消机制。确认相同起点 ordinal、interval overlap允许、Final不可删及source修订保留key。
- **检查/测试**：静态走 G02/G08/G09/G15/O02/O07 输入，逐条列 expected；此步不更改生产类型以提前让测试变绿。
- **预期**：没有“以后再决定用哪个字段排序”的待设计项。
- **失败诊断**：若 provider 新增明确权威sequence或当前源码已有相同功能，先记录冲突与证据再调整计划；不能静默采用另一个顺序。
- **完成检查点**：合同与测试名字一致，明确哪些是当前事实、哪些是拟新增API。

### S02 — 建 AQ-01 可编译失败回归

- [ ] **操作/目标**：新增 `Phase555Tests.cs`、`Phase555TestSupport.cs`、`DisplayGroupRevisionTests.cs`；修改 Program入口/默认注册；实现 G01–G19 的旧接口adapter版本。
- **前置**：S01；生产src保持基线。测试涉及新ticket属性的附加断言暂不注册（标“等S04”）。
- **输入**：D fixtures、GroupProbe、ScriptedTranslator/Channel/TCS、Z/V断言。
- **具体改法**：按 §8.1 写registry、异常计数、flags；每个case独立执行；G18走真实Scheduler；不得在adapter里补身份检查。
- **需执行**：只在新 red 副本离线restore/build Core，再运行 §10.3 的 AQ01 定向及关键case；保存build exit=0与case断言FAIL。
- **预期**：G02/G03/G06/G08/G15/G17 和 G18 产生目标断言红灯，原有guard可绿；没有测试挂起。
- **失败诊断**：编译错误先修测试，不算RED；G18未捕获combined时核查Provider配置、A文字incomplete、A只ApplySource而未排普通lookahead、ContextSentences非0、B未已有Exact。
- **完成检查点**：red生产hash与baseline一致，旧src diff为空；保存每例old结果与真实请求；程序确实因failures返回1。

### S03 — 建 AQ-03 可编译失败回归

- [ ] **操作/目标**：新增 `TranscriptOrderTests.cs`，注册O01–O12；可同时补R01–R04（仍不改生产）。
- **前置**：S02保存红灯，source仍基线；新类型属性断言留后续步骤加入。
- **输入**：明确音频区间、接收顺序、synthetic receipt clone子例；fake provider信封。
- **具体改法**：普通选择只Schedule待测current；O09/O10专测contextual；O11/O12纯projection，构造TimeSpan/Stopwatch tick不Sleep。
- **需执行**：重新创建完整red副本或在S02尚未运行AQ03的red中复制本步新测试（新文件只创建、已复制文件仅限本次测试修改；推荐新red副本避免覆盖证据）；用§10.3运行AQ03，保存独立日志。
- **预期**：O02/O03/O09/O11稳定断言失败；O07公开row顺序反例失败；O04偶然旧通过如实记。
- **失败诊断**：上下文请求与普通请求混淆时按Text/ID/Revision/Lookahead区别；收到自己作为previous也是旧缺陷，不以“修fixture”掩盖；先确认ApplySource非null。
- **完成检查点**：两缺陷都有编译成功后的RED，src hash没有变化；有完整case manifest，未用新API不存在制造失败。

### S04 — 实现 Store 身份和组原子一致性

- [ ] **操作/目标**：修改 SubtitleStore.cs 的record、ApplySource、ClearDisplayGroup、ApplySources、组方法、UpdateTranslation/Provisional；新增TranscriptOrder.cs。
- **前置**：S02/S03 RED已归档；签名和fields按§5–7固定。
- **输入**：两个单调计数、readonly成员快照、owner+attempt ticket、IsExact谓词。
- **具体改法**：先加metadata并切Store.Sort；再实现Prepare所有校验后才签发；替换旧group签名、全量校验后display-only commit；修Exact退休条件和资格失效。不得保留legacy裸IDs入口让调用者绕过。
- **调用依赖**：替换公开签名会导致Scheduler旧调用暂时编译失败，所以S04与S05是一个可构建修改单元；不能把这个编译错误当新测试失败。adapter同步迁ticket；测试正文/预期不变。
- **需执行**：S05完成后按§10.4跑G01–G17及owner/collection附加契约，再跑原Sticky/SentencePolicy所在Core全量。
- **预期**：Z零修改、一次Changed，source身份升级严格，新签发覆盖旧资格；独立Exact字段不丢。
- **失败诊断**：B先Exact重复显示查group是否只写LastVisible及IsExact是否检查revision；ABA漏拒绝查计数是否被Clear重置；失败改变资格查验证前是否写row。
- **完成检查点**：组内无await/外部callback、没有半提交、无空集合All误通过；source/Draft原接受规则未改。

### S05 — 迁移唯一生产组合调用与日志

- [ ] **操作/目标**：`TranslationScheduler.TryTriggerContextualRevision:123` 中非context分支；`rg AssignTranslationDisplayGroup` 核对调用完整性。
- **前置**：S04ticket可用，生产尚未宣称可构建；snapshot选择改稳定规则可与S06共享同一批修改。
- **输入**：此前snapshot的previous/current；Prepare返回ticket/null；readonly Members。
- **具体改法**：本步先定义§7.3四个private helpers供TryTrigger使用（S06再迁移普通路径，不能调用未定义helper）；sync外、Task.Run前Prepare；combined只读ticket成员；await后传ticket；bool false仅记rejected、不fallback、不重试；true才applied。不动deadline/AcquireSlot/CancelPending。
- **需执行**：G18 + G17；完整AQ01；新增静态搜索确认旧signature零处。
- **预期**：实际Scheduler延迟组返回不会污染纠正行，日志与Store返回一致。
- **失败诊断**：测试adapter绿而G18红，查Scheduler是否仍传ID或响应后重建expected；任务未被Flush收集查是否改变了原tasks注册边界。
- **完成检查点**：S04/S05合并状态编译通过，所有生产调用已迁移；不存在未记录的legacy调用者。

### S06 — 统一语义选择和 Projection 边界

- [ ] **操作/目标**：Scheduler五处查询/四个private helpers/Schedule actual校验；Projection两receipt边界；Sticky fixture:54。
- **前置**：S04稳定key与比较器已就绪；S03红灯已保存。
- **输入**：有序Store snapshot、当前source身份；context count开关；Gap公式。
- **具体改法**：按§7.3在wait和dispatch分别读取一份snapshot；dispatch context+lookahead同源；所有前后谓词用Compare且排自身；contextual只向真正后继。Projection按§7.4；仅修next fixture音频区间，不改已有期待。
- **需执行**：O01–O12；补public TranscriptStart断言；SourceVersion通过公开ticket.Members观察（CaptionSegment.SourceVersion为internal，不增加InternalsVisibleTo）；Core原测试（S08）；静态ReceivedTimestamp搜索必须符合§6.3。
- **预期**：A迟到修订仍位于B之前；重叠/相同起点确定；新provider request文本和context顺序准确。
- **失败诊断**：O07失败查ApplySource是否继承TranscriptStart而不是重新赋AudioStart；旧Sticky失败先查同起点fixture，不可放宽Exact/显示断言；request过期路径查continue是否保留new Final或形成空转。
- **完成检查点**：不存在receipt语义查询，保留延迟三处用途；Store/组/Scheduler顺序同定义；没有UI布局diff。

### S07 — 关闭修订、批次、逆序返回边界

- [ ] **操作/目标**：复核Store Prepare/Commit、ApplySources与Scheduler stale-job分支；完成G07–G17/O07–O10/R02/R03。
- **前置**：S06核心路径通过；新测试有界TCS释放机制就绪。
- **输入**：同RevisionFinal、Clear/recreate、同起点插入、已有组closure、保留ID+新tail、合法Partial tombstone merge、P2后返。
- **具体改法**：只能补上述合同内遗漏（如遗漏清Pending、漏self排除、旧group closure）；不移动取消机制、不清one-shot集合、不改native/provider生命周期。
- **需执行**：定向AQ01/AQ03/Regression，拒绝用例逐行Z；Compare置换与receipt clone/真实链条双证据。
- **预期**：每条提交的资格、源身份、成员结构均闭合；Exact先到的组响应拒绝；Final原不可删除。
- **失败诊断**：若为了过测试需要AQ-02结构重写，停止该依赖并报告：具体失败case、当前attempt先后、必要提交边界与可延期生命周期边界；不扩大修复。
- **完成检查点**：测试没有Sleep/随机；后发ticket与独立Exact受保护；单段同Revisionattempt总序仍明确列为后续风险。

### S08 — 新回归与完整 Core 验证

- [ ] **操作/目标**：在**新 green 副本**中从当前修改后的source复制完整构建输入，运行全部新case与现有Core Program。
- **前置**：S07完成；red副本/日志封存；green不复用red的bin/obj。
- **输入**：当前tracked source字节 + 明确新文件allowlist；离线SDK/cache。
- **具体修改**：原则上无；若失败仅在§6.1范围修根因，生成新green副本和新证据，不覆盖旧绿/红日志。
- **需执行**：§10.4命令，退出0且新结果全部存在；读取`artifacts/core-tests.json`的failures及每个passed，不只看退出码（历史benchmark有已知计数局限）。
- **预期**：新matrix、Sticky、SentenceContinuity/Policy、Partial/Final/preempt、旧普通late/cancel、history/projection/export测试不回归。
- **失败诊断**：hang查未释放fake请求；不匹配case退出2；OOM/SDK/restore错误记BLOCKED，不修改测试阈值求绿。
- **完成检查点**：source manifest、build exit、case结果、Core JSON/log完整，source diff仍在允许范围。

### S09 — 默认完整十二套验证

- [ ] **操作/目标**：创建独立 **full 副本**（保留本轮所有source/test变更），离线restore整个solution，然后执行当前verify-all.ps1。
- **前置**：S08绿且未再修改；Windows交互桌面/WPF/native依赖可用；新full目录、全新ArtifactsDir。
- **输入**：同S08最终source manifest；不复制bin/obj/data/models；可复制已有package缓存到新副本，缺模型明确NOT RUN。
- **具体修改**：无；不改verify-all.ps1、suite时限和UI阈值。
- **需执行**：§10.5；不用-Soak/-HardwareTone，准确十二项；核对summary名字、唯一性、各ExitCode，不只TotalSuites。
- **预期**：可用环境下12项全部exit0；缺fixture的可选真实模型路径即便suite绿仍NOT RUN。
- **失败诊断**：脚本fail-fast后余套标NOT RUN；Native/WPF/loopback权限失败保留stderr/PNG/JSON，BLOCKED或FAIL区别记录；不得安装新依赖或调用云来绕过。
- **完成检查点**：十二行状态表和实测输入hash；有阻塞就不写“完整验证通过”，不拿历史证据补空项。

### S10 — 用户可见行为与修改范围检查

- [ ] **操作/目标**：读取full副本WPF smoke的结果/图片/frames，审阅最终diff和原工作区保护manifest。
- **前置**：S09至少产生可分析结果；失败也保留材料，不清理。
- **输入**：Desktop.Smoke目录；Z/V帧；source manifest。
- **具体修改**：无；只在确认AQ-01/03引入回归时回S07小范围修根因再建新验证副本。
- **需执行检查**：单ListBox scroller、source先显示、未完成Sticky、组显示一份/Exact完整接管、history手动滚动/follow、实际resize增长、无horizontal overflow；diff允许清单；原既有untracked/ignored路径没有被测试写入。
- **预期**：无字幕重复/丢失/错误覆盖，无WPF布局源码改动，无版本/发行/远端差异。
- **失败诊断**：仅synthetic DPI图不能说明physical DPI；投影帧错误先核对group字段，不靠更改UI缓解Store错误。
- **完成检查点**：UI可见行为逐项证据、残余限制、原文件保护比对。

### S11 — 封存证据与交付

- [ ] **操作/目标**：在新run的Evidence目录新增交付报告/manifest/变更摘要；链接red/green/full日志；不改审计报告或既有phase报告。
- **前置**：S10已审阅；所有NOT RUN/BLOCKED明确。
- **输入**：最终diff、源hash、每case结果、每suite退出码、屏幕/帧记录。
- **具体修改**：只新增本阶段交付文档；不覆盖本文/已有报告；报告参考§11模板。
- **需执行检查**：§11验收逐条；全量未运行时状态写“实现完成/全量验证受阻”，不得写Phase完成全部门槛。
- **预期**：另一个reviewer可只看证据重建输入与结果；附建议提交摘要和精确待提交文件列表，**本授权不自动commit/push/tag/release**。
- **失败诊断**：缺RED/漏case/缺原始log则补对应可重复验证；不能补写想象结果或修改历史JSON。
- **完成检查点**：交付可审查，最终source输入可唯一识别，既有成果完整保留。

## 10. Commands and Isolated Verification Procedure

**本节是未来实施命令，不是本轮执行记录。** 在新的PowerShell 7进程中按顺序运行；路径有空格均使用变量/数组参数，不拼接shell命令。不依赖终端默认cwd。使用现有SDK与已缓存包；缺少资源就记录BLOCKED，不自动安装或联网补包。

### 10.1 S00 只读检查与新证据根

```powershell
$ErrorActionPreference = 'Stop'
$phase555Source = 'D:\Desktop\AntiDBT-Gemini\antiDBT\prototype'
Set-Location -LiteralPath $phase555Source
git rev-parse --show-toplevel
git branch --show-current
git rev-parse HEAD
git -c core.quotepath=false status --short --untracked-files=all
git diff --name-only HEAD
git ls-files --others --ignored --exclude-standard --directory

$phase555Run = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N')
$phase555Root = Join-Path (Split-Path $phase555Source -Parent) ('phase555-validation-' + $phase555Run)
if (Test-Path -LiteralPath $phase555Root) { throw 'Run directory collision' }
[IO.Directory]::CreateDirectory($phase555Root) | Out-Null
$phase555Evidence = Join-Path $phase555Root 'Evidence'
[IO.Directory]::CreateDirectory($phase555Evidence) | Out-Null

$phase555Protected = @(& git -c core.quotepath=false -C $phase555Source ls-files --cached --others --exclude-standard)
if ($LASTEXITCODE -ne 0) { throw 'Cannot inventory source' }
$phase555Before = @($phase555Protected | Sort-Object -Unique | ForEach-Object {
    $path = Join-Path $phase555Source $_
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing input: $_" }
    [pscustomobject]@{ Path=$_; SHA256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
})
$phase555Before | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $phase555Evidence 'source-before.json') -Encoding utf8
git -C $phase555Source rev-parse HEAD > (Join-Path $phase555Evidence 'head-before.txt')
git -C $phase555Source show-ref > (Join-Path $phase555Evidence 'refs-before.txt')
git -c core.quotepath=false -C $phase555Source status --short --untracked-files=all > (Join-Path $phase555Evidence 'status-before.txt')
Get-FileHash -LiteralPath (Join-Path $phase555Source '.git/index') -Algorithm SHA256 |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $phase555Evidence 'index-before.json') -Encoding utf8
```

原工作区ignored目录可能含模型、release包、个人配置，不复制/枚举正文；记录其目录存在与本轮不写入政策。可按用户已有资产清单额外hash指定大文件，不必为证明不写而递归读取所有个人data。

### 10.2 创建包含当前修改的独立副本

不用普通“干净HEAD checkout”测试旧代码：它会遗漏本次修改和新增测试。下面只在新目录创建本地clone的Git元数据（no-checkout、no-hardlinks），然后复制**当前工作树**tracked文件以及明确新增的文件；不修改原仓库/index/branch。clone的origin仅指原本地路径，禁止对其push。

```powershell
$phase555NewFiles = @(
  'src/AntiDbt.Core/TranscriptOrder.cs',
  'tests/AntiDbt.Core.Tests/Phase555Tests.cs',
  'tests/AntiDbt.Core.Tests/Phase555TestSupport.cs',
  'tests/AntiDbt.Core.Tests/DisplayGroupRevisionTests.cs',
  'tests/AntiDbt.Core.Tests/TranscriptOrderTests.cs'
)

function New-Phase555Copy([string]$Name) {
    if ($Name -notmatch '^[a-z0-9-]+$') { throw 'Invalid copy name' }
    $copy = Join-Path $phase555Root $Name
    if (Test-Path -LiteralPath $copy) { throw "Copy already exists: $copy" }
    & git clone --no-checkout --no-hardlinks -- $phase555Source $copy | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Local clone failed; preserve partial copy and use a fresh name' }
    $tracked = @(& git -c core.quotepath=false -C $phase555Source ls-files)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot list tracked inputs' }
    $inputs = @($tracked) + @($phase555NewFiles | Where-Object { Test-Path -LiteralPath (Join-Path $phase555Source $_) })
    $copyFull = [IO.Path]::GetFullPath($copy).TrimEnd('\') + '\'
    $sourceFull = [IO.Path]::GetFullPath($phase555Source).TrimEnd('\') + '\'
    foreach ($relative in @($inputs | Sort-Object -Unique)) {
        if ($relative -match '(^|[\\/])\.(git|agents|codex|aws)([\\/]|$)') { throw "Excluded input: $relative" }
        $from = [IO.Path]::GetFullPath((Join-Path $phase555Source $relative))
        $to = [IO.Path]::GetFullPath((Join-Path $copy $relative))
        if (-not $from.StartsWith($sourceFull, [StringComparison]::OrdinalIgnoreCase) -or
            -not $to.StartsWith($copyFull, [StringComparison]::OrdinalIgnoreCase)) { throw 'Path escaped copy roots' }
        if (-not (Test-Path -LiteralPath $from -PathType Leaf)) { throw "Missing tracked input: $from" }
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($to)) | Out-Null
        [IO.File]::Copy($from, $to, $false) # Never overwrite any pre-existing destination file.
    }
    # Optional offline cache, only into a new destination. Never reuse source obj/bin/data.
    $packages = Join-Path $phase555Source '.packages'
    if (Test-Path -LiteralPath $packages -PathType Container) {
        $copyPackages = Join-Path $copy '.packages'
        if (Test-Path -LiteralPath $copyPackages) { throw 'Package destination collision' }
        Copy-Item -LiteralPath $packages -Destination $copyPackages -Recurse -ErrorAction Stop
    }
    $manifest = @($inputs | Sort-Object -Unique | ForEach-Object {
        [pscustomobject]@{Path=$_;SHA256=(Get-FileHash -LiteralPath (Join-Path $copy $_) -Algorithm SHA256).Hash}
    })
    $manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $phase555Evidence ($Name+'-inputs.json')) -Encoding utf8
    & git -C $copy diff --binary HEAD > (Join-Path $phase555Evidence ($Name+'-tracked-diff.patch'))
    return $copy
}

$phase555Dotnet = if (Test-Path -LiteralPath (Join-Path $phase555Source '.dotnet/dotnet.exe')) {
    Join-Path $phase555Source '.dotnet/dotnet.exe'
} else { (Get-Command dotnet -ErrorAction Stop).Source }
$env:PATH = [IO.Path]::GetDirectoryName($phase555Dotnet) + ';' + $env:PATH
$env:MSBuildEnableWorkloadResolver = 'false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
```

运行helper前，在原工作树确认新增文件属于本次、当前tracked diff只在允许清单；如果有其他人修改的生产代码，禁止自动复制后宣称本阶段独立验证，应先记录影响并采用只包含本次修改的显式输入清单。不要复制已有untracked audits/release/smoke成果。文档不会参与构建，无须为此复制全部untracked目录。

helper已将clone stdout送Out-Host，函数返回值只含副本路径。只有新副本中的受跟踪路径会显示diff；新source/test的身份由inputs.json补足，git diff本身不包含untracked新文件。

### 10.3 S02/S03：旧生产代码上的 RED

先保存测试代码，然后创建副本。建议S02用`red-aq01`、S03用`red-both`，每个输入版本只创建一次。以下以S03完成、src仍基线的`red-both`为例：

```powershell
$phase555Red = New-Phase555Copy 'red-both'
$env:DOTNET_CLI_HOME = Join-Path $phase555Red '.dotnet-home'
$redFeed = Join-Path $phase555Red '.offline-feed'
[IO.Directory]::CreateDirectory($redFeed) | Out-Null
Push-Location $phase555Red
try {
    & $phase555Dotnet --version > (Join-Path $phase555Evidence 'red-sdk.txt')
    if ($LASTEXITCODE -ne 0) { throw 'BLOCKED: SDK unavailable' }
    & $phase555Dotnet restore tests/AntiDbt.Core.Tests/AntiDbt.Core.Tests.csproj --disable-parallel --ignore-failed-sources --source $redFeed --packages (Join-Path $phase555Red '.packages') *> (Join-Path $phase555Evidence 'red-restore.log')
    if ($LASTEXITCODE -ne 0) { throw 'BLOCKED: offline restore; do not install/download' }
    & $phase555Dotnet build tests/AntiDbt.Core.Tests/AntiDbt.Core.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false *> (Join-Path $phase555Evidence 'red-build.log')
    if ($LASTEXITCODE -ne 0) { throw 'Test compile failure is not a defect RED' }
    $dll = Join-Path $phase555Red 'tests/AntiDbt.Core.Tests/bin/Debug/net10.0/AntiDbt.Core.Tests.dll'
    & $phase555Dotnet $dll --phase555-only --suite AQ01 *> (Join-Path $phase555Evidence 'red-aq01.log')
    $aq01Exit = $LASTEXITCODE
    & $phase555Dotnet $dll --phase555-only --suite AQ03 *> (Join-Path $phase555Evidence 'red-aq03.log')
    $aq03Exit = $LASTEXITCODE
    [pscustomobject]@{AQ01=$aq01Exit;AQ03=$aq03Exit;ExpectedDefectExit=1} |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $phase555Evidence 'red-exits.json') -Encoding utf8
    if ($aq01Exit -ne 1 -or $aq03Exit -ne 1) { throw 'RED not established: inspect case results, zero matches or infrastructure' }
    & git diff --name-only HEAD -- src > (Join-Path $phase555Evidence 'red-production-diff.txt')
    if ((Get-Item -LiteralPath (Join-Path $phase555Evidence 'red-production-diff.txt')).Length -gt 0) {
        throw 'RED production differs from baseline'
    }
} finally { Pop-Location }
```

还须核对red-inputs.json中每个生产文件与S00相同，且red/src没有新增TranscriptOrder.cs；`git diff -- src`只看tracked，不足以排除untracked生产修复。逐例读取red JSON：目标失败必须是§8中预测的断言差异，而非timeout/未匹配/编译异常。若某要求旧API本来已有guard，记录old PASS而不是改输入强求红。

需要单例诊断时使用**新日志文件名**：

```powershell
# 在副本内执行；必须已成功构建。
& $phase555Dotnet $dll --phase555-only --case RejectsFirstMemberRevisionChange
& $phase555Dotnet $dll --phase555-only --case EarlierCorrectionCannotUseLaterFinalAsContext
```

离线源是一个新空目录；所有包必须已在副本 `.packages` 中。Core无外包，但仍需已安装net10 SDK/targeting packs。不要把用户全局NuGet配置/凭据复制到副本。缺失包/SDK不自动连接nuget.org。

### 10.4 S08：修复后 GREEN 与 Core

在S04–S07完成后创建green副本，重新offline restore/build，不能复用旧dll。以下命令在本节S00/S02定义的同一PowerShell进程运行：

```powershell
$phase555Green = New-Phase555Copy 'green'
$env:DOTNET_CLI_HOME = Join-Path $phase555Green '.dotnet-home'
$greenFeed = Join-Path $phase555Green '.offline-feed'
[IO.Directory]::CreateDirectory($greenFeed) | Out-Null
Push-Location $phase555Green
try {
    & $phase555Dotnet restore tests/AntiDbt.Core.Tests/AntiDbt.Core.Tests.csproj --disable-parallel --ignore-failed-sources --source $greenFeed --packages (Join-Path $phase555Green '.packages') *> (Join-Path $phase555Evidence 'green-restore.log')
    if ($LASTEXITCODE -ne 0) { throw 'BLOCKED: offline restore' }
    & $phase555Dotnet build tests/AntiDbt.Core.Tests/AntiDbt.Core.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false *> (Join-Path $phase555Evidence 'green-build.log')
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    $greenDll = Join-Path $phase555Green 'tests/AntiDbt.Core.Tests/bin/Debug/net10.0/AntiDbt.Core.Tests.dll'
    & $phase555Dotnet $greenDll --phase555-only *> (Join-Path $phase555Evidence 'green-phase555.log')
    $newExit = $LASTEXITCODE
    if ($newExit -ne 0) { throw 'New regression matrix failed; inspect per-case results' }
    & $phase555Dotnet $greenDll *> (Join-Path $phase555Evidence 'green-core.log')
    $coreExit = $LASTEXITCODE
    [pscustomobject]@{NewCases=$newExit;Core=$coreExit} | ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $phase555Evidence 'green-exits.json') -Encoding utf8
    if ($coreExit -ne 0) { throw 'Core failed' }
    $core = Get-Content -LiteralPath 'artifacts/core-tests.json' -Raw | ConvertFrom-Json
    if ($core.failures -ne 0 -or @($core.results | Where-Object { -not $_.passed }).Count -gt 0) {
        throw 'Core exit/result mismatch or failed result'
    }
} finally { Pop-Location }
```

NewCases结果必须包含所有registry预期case/subcase数量；不能只检查0。对红→绿case比较**测试正文与期待**，唯一允许adapter从旧API改ticket。新契约追加断言单独列出。green Core会写green内固定输出；之后full使用新副本避免覆盖green原始证据。

### 10.5 S09：完整十二套

```powershell
$phase555Full = New-Phase555Copy 'full'
$env:DOTNET_CLI_HOME = Join-Path $phase555Full '.dotnet-home'
$fullFeed = Join-Path $phase555Full '.offline-feed'
[IO.Directory]::CreateDirectory($fullFeed) | Out-Null
$phase555Artifacts = 'artifacts/phase5.5.5/full-' + $phase555Run
if (Test-Path -LiteralPath (Join-Path $phase555Full $phase555Artifacts)) { throw 'Artifacts collision' }
Push-Location $phase555Full
try {
    & $phase555Dotnet restore AntiDbt.sln --disable-parallel --ignore-failed-sources --source $fullFeed --packages (Join-Path $phase555Full '.packages') *> (Join-Path $phase555Evidence 'full-restore.log')
    if ($LASTEXITCODE -ne 0) { throw 'BLOCKED: complete offline solution dependencies unavailable' }
    & pwsh -NoProfile -File ./scripts/verify-all.ps1 -SkipRestore -ArtifactsDir $phase555Artifacts *> (Join-Path $phase555Evidence 'full-driver.log')
    $driverExit = $LASTEXITCODE
    [pscustomobject]@{ExitCode=$driverExit} | ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $phase555Evidence 'full-exit.json') -Encoding utf8
    if ($driverExit -ne 0) { throw 'Full verification stopped; remaining suites NOT RUN' }
    $summary = Get-Content -LiteralPath (Join-Path $phase555Artifacts 'verification-summary.json') -Raw | ConvertFrom-Json
    $expected = @('Core','Windows','CloudSpeech','CloudSpeech.NativeSmoke','Providers',
      'Providers.Multi','Providers.Multi.Localhost','Language','Domestic','Domestic.Offline',
      'Desktop.Smoke','Diagnostics.Performance')
    $actual = @($summary.Suites | ForEach-Object { $_.Name })
    $nameDifference = @(Compare-Object ($expected | Sort-Object) ($actual | Sort-Object))
    $duplicates = @($actual | Group-Object | Where-Object { $_.Count -ne 1 })
    $bad = @($summary.Suites | Where-Object { -not $_.Passed -or $_.ExitCode -ne 0 })
    if ($summary.TotalSuites -ne 12 -or $summary.PassedSuites -ne 12 -or
        $summary.FailedSuites -ne 0 -or $nameDifference.Count -gt 0 -or
        $duplicates.Count -gt 0 -or $bad.Count -gt 0) { throw 'Not an exact twelve-suite pass' }
} finally { Pop-Location }
```

**输入一致性**：green/full的tracked及NEW source/test哈希必须一致（验证时没有再改源码）；副本metadata HEAD仍可能为`765caa5`，因为未commit当前修复。必须把inputs.json+tracked diff+新增文件列表一起附报告，不能把summary.Commit当作“该commit已包含修复”。若之后另获commit授权，最终测试证据与commit对应关系另行明确，不可改历史summary。

### 10.6 verify-all.ps1 的实际行为与写路径

| 位置/参数 | 核实行为与执行要求 |
|---|---|
| `-TargetFramework` | 读取Core csproj TFM（无值fallback net10.0），Windows拼`-windows`；该override只影响驱动的执行路径，build未传TargetFramework。保持默认，不试图用它跨TFM迁移 |
| `-SkipRestore` | build加`--no-restore`，仍build整个solution Debug；不等于跳build。先offline restore whole solution再用 |
| build:43–50 | `dotnet build AntiDbt.sln -m:1 -p:UseSharedCompilation=false`；日志写ArtifactsDir/build.log；失败throw，没有十二套summary也可能正常发生 |
| Run-Suite:68–137 | 默认单套60s，重定向stdout/stderr；超时杀该进程树（taskkill/fallback Kill），记录exit=-1/false后throw；非零exit同样fail-fast；每完成一套写summary |
| Desktop:164–226 | EXE `--smoke-test <newdir>`，60s，真实WPF会话；写PNG/JSON、console/error；无交互桌面不能宣称UI通过 |
| optional flags | `-Soak`额外synthetic soak；`-HardwareTone`额外真实设备tone；都不在十二套内，本阶段不启用 |
| env/path | driver Push-Location脚本根；设置repo `.dotnet-home`；优先副本`.dotnet/dotnet.exe`，否则PATH dotnet。本方案不复制SDK，只将已存在SDK目录放入当前进程PATH |

| 默认十二套 / Log | 副作用与证据边界 |
|---|---|
| Core / core.log | cwd `artifacts/core-tests.json`、`artifacts/phase5.5.4-benchmark.json`；另bin/Debug/net10.0/artifacts/phase5.3/diagnostic-telemetry-sample.json（driver复制到本run）。benchmark仍synthetic，不宣称质量提升 |
| Windows / windows.log | bin下testtemp/GUID合成settings/WAV并删其**自建**temp；固定root artifacts/windows-tests.json；DPAPI使用合成key；无`--capture-smoke` |
| CloudSpeech / cloudspeech.log | injected SDK contracts，console结果；未发现本入口固定报告写入 |
| CloudSpeech.NativeSmoke / cloudspeech-native-smoke.log | 同DLL加`--native-smoke`，本机SDK/native装载，不是云识别 |
| Providers / providers.log | loopback HTTP/WebSocket；系统temp/test_tokens_GUID/test_weights_GUID自建fixture与删除；固定root artifacts/provider-tests.json；模型目录若存在会执行真实本地WAV，缺WAV/tokens则早退却被旧runner标pass |
| Providers.Multi / providers-multi.log | fake handlers，固定artifacts/phase3/http-tests.json |
| Providers.Multi.Localhost / providers-multi-localhost.log | localhost 15组合，固定artifacts/phase4/combinations.json；不是15真实云组合 |
| Language / language.log | fake language/provider contracts，固定artifacts/phase4/language-tests.json |
| Domestic / domestic.log | localhost owned transport，固定artifacts/phase4/domestic-localhost-green.json（无--red） |
| Domestic.Offline / domestic-offline.log | 无listener分支，固定artifacts/phase4/domestic-offline-green.json |
| Desktop.Smoke / desktop-smoke/desktop-smoke.json | testMode禁个人settings读写；本run下PNG/JSON/caption-trace/frames；只synthetic provider |
| Diagnostics.Performance / diagnostics-performance.log | 40000 synthetic Core操作，固定artifacts/performance.json；不测真实ASR/MT/WPF或process全内存 |

所以仅换 `-ArtifactsDir` 不保护原工作区子输出；必须使用新副本。新full副本默认没有ignored models，Providers可选真实模型路径明确 **NOT RUN（fixture absent）**，即使suite exit0。若用户事先授权使用已有模型，可单独将确切model和test_wavs复制到新的模型验证副本并记录hash；本阶段不下载，不自动启用live。禁止复制原 `data/`、运行 `AntiDbt.Sticky.Live`、`--local-asr-smoke` 或任何publish/create-release脚本。

### 10.7 S10/S11 保护检查

在原工作区运行只读diff/status。将S00 manifest中的每个**不在§6.1实施允许清单内**的文件重新hash比较；所有原有untracked路径hash必须相同，不因其在允许目录内就豁免。原允许清单中的既有tracked变更须全部属于本任务；`.git/index`、HEAD、refs仍与before一致。

```powershell
git -c core.quotepath=false -C $phase555Source status --short --untracked-files=all
git -C $phase555Source diff --check
git -C $phase555Source diff --stat
git -C $phase555Source diff --name-only HEAD
rg -n 'ReceivedTimestamp|AssignTranslationDisplayGroup|TryPrepareTranslationDisplayGroup' (Join-Path $phase555Source 'src') (Join-Path $phase555Source 'tests') -g '*.cs'
```

不使用 `git clean`、`Remove-Item -Recurse`、`robocopy /MIR`、全目录覆盖、checkout/reset、自动stash。验证副本与失败产物保留原地供review，不在收尾删除。原ignored资产不被任何build/test选作cwd或输出目标；副本inputs manifest没有引用原obj/bin/data路径。

CLI语法依据本仓库脚本及Context7读取的官方.NET文档：[执行已编译DLL](https://github.com/dotnet/docs/blob/main/docs/core/tools/dotnet.md)、[dotnet build 的 --no-restore](https://github.com/dotnet/docs/blob/main/docs/core/tools/dotnet-build.md)。没有将xUnit/vstest能力臆加给本仓库的console runner。

## 11. Acceptance Criteria and Evidence Requirements

### 11.1 硬性验收

- [ ] AQ-01所有§5.4原子条件实现并经对应G用例验证；空/重复/缺失/删改/Session/ABA/关系/逆序/Exact优先全部有证据。
- [ ] 失败提交Z：没有部分数据修改、清错误、Pending破坏、Changed通知或普通翻译回退。
- [ ] 组合只在显示层存在；Exact/Provisional/Sticky不混用；首/次成员两种Exact返回次序及签发前已有Exact均正确。
- [ ] AQ-03全部五处Scheduler与两处Projection接收时钟语义依赖清除；延迟用途保留；key及tie-break唯一。
- [ ] 音频起点修订保留key、重叠可比较、同起点与接收置换确定；split/merge基于新ID区间关系，旧ID/旧ticket不能复活。
- [ ] 实际Scheduler失败→成功，不只是Store helper测试；新增runner零匹配非零，所有case有独立结果。
- [ ] 旧完整Core通过；WPF已有source-first/sticky/atomic handoff/history/scroll/resize断言无放宽。
- [ ] 十二套逐项exit0且无遗漏，或逐项明确FAIL/BLOCKED/NOT RUN并不宣称完整验收。
- [ ] 只改允许文件；没有版本/项目/发布/UI布局差异；原既有untracked/ignored成果未删未覆盖；Git/index/refs不变。

### 11.2 交付报告模板

在新run的Evidence目录新增 `PHASE-5.5.5-IMPLEMENTATION-REPORT.md`（不存在才创建），内容至少：

1. source-root、原HEAD/branch、实际SDK/TFM、最终tracked diff与新增文件manifest/hash；状态“未commit工作树快照”。
2. AQ-01/AQ-03根因、最终接口/不变量、对应S04–S07差异；不附无法证明的性能提升数字。
3. RED表：case、旧build exit、旧assertion差异、源manifest；GREEN表：同case/同预期、修复后exit和断言；契约新增测试单列。
4. 原Core和十二套状态表，分别记录PASS/FAIL/BLOCKED/NOT RUN、退出码、日志路径、运行时间/输入；未知用unknown，禁止用历史补值。
5. WPF smoke JSON/PNG/frames路径及观察：源/译文各一次、组一份、Exact接管无空帧、单容器/scroll/resize；synthetic与physical边界明确。
6. 原工作区保护比较结果：existing untracked hashes全部相等，非允许tracked相等；ignored原路径未作为输出；HEAD/index/refs不变。
7. 残余AQ-02、同起点真实语义无法恢复、模型fixture未跑、物理DPI未跑；没有release。
8. S00–S11完成/阻塞表；需要用户后续决策的具体事项，建议提交信息和待提交文件列表（不执行git提交）。

**状态语义**：PASS=本轮命令/断言真实运行成功；FAIL=目标逻辑断言或suite非零；BLOCKED=缺SDK/权限/native/桌面/依赖等无法完成；NOT RUN=后续suite因fail-fast或可选fixture不具备而未执行。timeout根据日志归类，不自动当成期望RED。历史CI/报告只写HISTORICAL，不进入新run的通过计数。

## 12. Risk Analysis and Rollback Strategy

| 风险/取舍 | 处理与证据 |
|---|---|
| first AudioStart不准确，后修正不移动语义key | 明示稳定性优先；保留实际音频字段；O07检查。若provider未来有权威sequence，另立接口变更，不在本阶段改provider |
| 相同起点ordinal与人理解的说话先后不同 | deterministic tie无法推断不可见信息；O04/O05固定。禁止宣称精确恢复多说话人语序 |
| 严格组检查拒绝更多旧响应 | 这是正确性代价；原独立翻译/Sticky继续显示；不加自动重试或付费调用。错误不清state |
| 组不再写TranslationText，pending/retry比旧伪Exact更真实 | 不压掉pending来迎合旧错误；保留error字段；G14/G15和现有WPF smoke验证显示不空。未做UI新设计 |
| ticket签发在async取slot前，长等待可能更常失效 | 保守拒绝，避免等待期间源变化被掩盖；可观察rejected事件；不在返回时重签发 |
| latest group ticket优先可能让重叠旧请求失效 | G17固定；失败不撤销更新资格。仅组合提交资格，不能声称限制了真实网络请求 |
| 同Revision普通与supportsContext单段请求仍可能后写覆盖 | 明确AQ-02残余；本阶段只保证旧组合不覆盖新独立Exact，不建立全请求attempt总序 |
| ApplySources已有非事务输入接受策略、历史组被淘汰后的旧策略 | 只在组提交处拒绝缺失/重排，不重写source batch或长期组淘汰政策；G10/G11/R02合法输入保护 |
| 新metadata/成员snapshot分配 | 每row三个scalar、每inflight group一个有界成员数组；不加无限字典、不深拷贝文本；不能据此宣称性能提升 |
| 验证输出覆盖/本机凭据混入 | 新red/green/full副本，CreateNew式文件复制；不复制data、不启live；副本日志不可回写原artifacts |

**回退策略**：不自动运行任何还原命令。保留S00原始hash、差异、各隔离副本；若实施失败，停止新增修改并报告，保留现场。另获用户明确还原授权时，只逐个反向撤销本任务允许文件中的本任务hunk，对照S00和他人修改；本任务新文件也须先确认没有后续用户内容，再讨论是否保留/删除。禁止reset/clean/stash/checkout整树。现有发行包/版本从未改动，不需要所谓“回滚release”。

## 13. Scope Boundaries and Deferred Findings

**本阶段必要边界**：完整成员source/version/关系检查、组合资格的后发优先、Exact完成后使旧组合资格失效，都是“这个组合结果是否仍可显示”的原子正确性。不能把这部分全部推给AQ-02而只加两个revision比较。

**AQ-02明确延期**：所有普通/contextual attempts统一所有权、CancelSegment/CancelPending覆盖contextual任务、stop/dispose drain、physical outstanding accounting、统一429/terminal guard、相同Revision单段普通/contextual last-writer治理、lookahead依赖版本的完整单段contextual提交、reconfigure后新网络请求控制。不改Job结构、取消token拓扑、AcquireSlot/ReleaseSlot、请求上限、fallback费用同意。

**其他延期**：AQ-04/11 native/SDK资源释放；AQ-05/06掉帧与UI端到端遥测；AQ-07 benchmark质量与计数；AQ-08 one-shot集合的长会话清理；AQ-09/10隐私/配置；AQ-12/15/17发布和skip-aware runner重构；AQ-13跨Session导出；UI美化、新provider、自动设备切换。现有测试局限只在报告标明，不顺手修。

**需调查但不假装已验证的事项**：

| 事项 | 当前证据/假设 | 调查方法与决策条件 |
|---|---|---|
| 首次AudioStart能否代表正确相对位置 | 已核实local/hybrid/Volc/SpeechSession提供音频区间；没有全provider权威sequence证明 | O03/O07固定现有API合同；若发现provider首报起点系统性错误，只记录后续provider议题，不能用ReceivedTimestamp退回旧方案 |
| 新设计所有原测试是否通过 | 本轮未运行 | S08真实Core；失败必须定位fixture歧义或生产回归，保留原预期；只有§6已指定next时间fixture可直接校正 |
| 独立副本full环境是否有所有依赖/WPF权限 | 配置已读，实际SDK/native/权限未验证 | S00/S09探测；缺失BLOCKED/NOT RUN，不安装/提升权限绕过 |
| GUI的pending视觉在display-only组上是否符合既有行为 | 源码Join按独立TranslationText判断pending；本方案保留真实pending | S10检查现有smoke及G14/G15帧；若仅需要美化pending则延期，若组消失/重复则回Store修正确性 |
| 某些旧Sort等起点用例是否恰好通过 | 未执行，不能预判每个置换结果 | S03如实记old结果；核心AQ03用O02/O03/O09/O11红灯，不靠随机排序 |

## 14. Final Antigravity Handoff Instructions

以下可直接复制到Antigravity作为下一次**实施任务**指令：

> 请依据 `D:\Desktop\AntiDBT-Gemini\antiDBT\prototype\docs\plans\phase5.5.5\IMPLEMENTATION-PLAN.md` 实施antiDBT Phase 5.5.5。只解决AQ-01、AQ-03，严格按S00→S11执行，不重新设计另一套架构。
>
> 先执行S00只读保护与源码drift检查，保存原HEAD/branch/index/refs、tracked/untracked文件hash；保留全部既有未跟踪与忽略成果。S01固定§5合同：TranscriptStart首次音频起点、Session/key/ordinal ID总序、SourceVersion防ABA、Store签发只读ticket、PendingDisplayGroupAttempt后发资格。
>
> S02/S03先只改测试：依§8增加可编译旧接口adapter、Channel/TCS fake translator、console定向入口，按§10在新red副本证明G02/G03/G06/G08/G15/G17/G18和O02/O03/O09/O11等目标断言失败；编译失败/timeout不能当红灯。保存旧生产hash和每case结果后才能改src。
>
> S04/S05作为一个可构建单元实施Store全成员原子校验与唯一Scheduler组合调用迁移：先全校验后写、失败零修改零Changed；combined只作为组显示，保留独立Exact/Provisional/Sticky/Error；所有独立Exact完成才原子撤组。不得保留裸IDs绕过接口。S06统一五处Scheduler选择与两处Projection receipt边界，保留ReceivedTimestamp延迟用途，正确处理current stale job而不丢新Final/空转。S07完成Final同revision、删除/淘汰/Clear ABA、插入、split/merge、已有组closure及逆序返回边界；不改AQ-02生命周期。
>
> S08用新green副本跑全部新case和原完整Core；S09用另一个新full副本执行当前verify-all.ps1默认十二套，离线缺依赖记BLOCKED，不安装、不真实采音、不使用凭据/收费API。S10核对单WPF滚动容器、源先显示、Sticky、组显示/Exact接管、history/scroll/resize及严格diff范围；不得放宽已有测试预期值。S11新增完整证据报告，列每步/每case/每suite状态、源输入manifest、red→green与残余风险；实际未跑的一律NOT RUN/HISTORICAL。
>
> 只修改§6.1允许文件；新增文件若已存在先保留并核实，禁止覆盖。不要修改版本、项目文件、发布资料、现有审计/报告/发行包、UI布局；不要reset/clean/checkout/stash/commit/push/tag/release，不删除失败副本。遇到方案前提被真实源码否定，记录具体位置、调查结果与受影响Step，再请求决策；不偷偷扩大范围。最终交付实现差异及可审查证据，不以一句“12/12通过”替代原始日志。

**规划交付自检（本轮）**：九报告完整读取；所有既有目标/方法均经源码确认；唯一生产组调用已追踪；全部ReceivedTimestamp引用分流；新类型明确为拟新增；原子条件/稳定key/tie-break/失败路径具体；RED与新契约测试分开；默认十二套和副作用/隔离程序明确；没有运行build/test或修改生产文件。本文件是施工图，实施及运行结果仍待Antigravity在后续授权任务中产生。

本轮文档静态检查：14节齐全，S00–S11齐全；七个PowerShell命令块仅用内置Parser解析，零语法错误（未执行这些命令）；既有文件引用不存在缺失目标，五个拟新增源码/测试文件已单列。保护比对：281个既有tracked/nonignored untracked文件hash全部相同，HEAD、branch、Git index hash相同；唯一新增项目文件为本计划。原ignored目录没有被任何构建或测试写入。
