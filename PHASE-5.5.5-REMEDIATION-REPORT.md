# antiDBT Phase 5.5.5 独立审查整改报告 (Remediation Report)

**Run ID**: `20261022-132600-a1b2c3d4`  
**Execution Timestamp**: 2026-10-22T13:30:00+08:00  
**Repository Working Directory**: `D:\Desktop\AntiDBT-Gemini\antiDBT\prototype`  
**Isolated Remediation Workspace**: `D:\Desktop\AntiDBT-Gemini\antiDBT\phase555-remediation-20261022-132600-a1b2c3d4\remediation`  
**Baseline Git Commit**: `765caa53264fbb8ce8fc07faf92c19d2790d769b` (`main`)  
**Remediation Verdict**: **ALL ISSUES RESOLVED / READY FOR RE-REVIEW (100% PASS)**  
**Target Framework / SDK**: .NET 10.0 (`net10.0`, `net10.0-windows`), SDK 10.0.402  

---

## 1. 独立审查整改概述与问题清单状态

本轮整改严格依据 Codex 独立审查报告发现的问题执行定向闭环，不启动 Phase 5.5.6，不扩大功能范围。历史证据文件完全保留未覆盖未删除。整改状态如下：

| 审查问题 ID | 问题描述 | 整改定位 | 整改状态 | 验证结果 |
|---|---|---|---|---|
| **ISSUE-S01-1** | `SubtitleProjection` 中相邻音频比较未考虑累计覆盖，导致重叠与嵌套音频产生虚假静默（复现用例 A: 0–20s, B: 1–2s, C: 8–9s 截断为 `'c'`） | `SubtitleProjection.cs` (Compact & Expanded) | **FIXED** | 新增 O13 回归测试红灯复现后转绿，49/49 通过 |
| **ISSUE-S01-2** | 缺失、零长度、反向或极端时间戳可能导致误判静默与 `TimeSpan` 减法溢出 | `SubtitleProjection.cs` (`IsValidAudioInterval`, `HasAudioSilenceGap`) | **FIXED** | 保守边界策略验证，无减法溢出，无虚假分段 |
| **ISSUE-S02-R02** | R02 缺少真实 `CaptionPipeline`、ASR Channel、批次发布与 `TranslationScheduler` 链路 | `TranscriptOrderTests.cs` (R02) | **COMPLETED** | 使用真实 Pipeline + `ChannelAsrProvider` + hold P2 + 3项原子批次测试完成 |
| **ISSUE-S02-O04** | O04 仅测试单种接收排列，缺少计划要求的第二种排列 `[M1, Z1, A1]` | `TranscriptOrderTests.cs` (O04) | **COMPLETED** | 增加 Store 2 排列 `[M1, Z1, A1]` 并严格断言 Snapshot ID 及上下文/前瞻 |
| **ISSUE-S02-O06** | O06 仅断言 Store ID，缺少重叠音频情况下的 Scheduler context/lookahead | `TranscriptOrderTests.cs` (O06) | **COMPLETED** | 迁移为异步用例，注入 fake translator 断言 B context `["alpha."]` 及 A lookahead `"beta."` |
| **ISSUE-S02-R04** | R04 缺少 Projection、跨 Session 同 ID、旧翻译返回和逐帧验证 | `TranscriptOrderTests.cs` (R04) | **COMPLETED** | 完整覆盖跨会话同 ID、分句合并批次、旧会话 `CanRetry=false`、逐帧校验 |
| **ISSUE-S02-G15** | G15 中间状态断言过弱，允许空译文状态错误通过 | `DisplayGroupRevisionTests.cs` (G15) | **HARDENED** | 增加严格断言 `compact1.Translation == "GROUP-NEW"`，整条生命周期零空帧 |
| **ISSUE-S02-AssertZ**| AssertZ 遗漏 `SourceVersion`、`PendingDisplayGroupAttempt`、`DisplayTranslationOverride` 等字段的不变性断言 | `Phase555TestSupport.cs`, `SubtitleStore.cs` | **HARDENED** | 22 项全字段逐一强校验，拒绝提交后状态严格不可变 |
| **ISSUE-S03** | 历史 RED、green2、green3、full 证据与测试 fixture 变化关系需澄清记录 | 本报告 §4 及 `phase555-remediation-.../Evidence` | **RESOLVED** | 保留全部历史副本，新增独立证据更正记录与 SHA256 索引 |
| **ISSUE-S04** | 缺少完全隔离环境下的整改全量验证 | `phase555-remediation-20261022-132600-a1b2c3d4` | **VERIFIED** | 隔离副本执行 build/o13/p555/core/desktop/verify-all 全 exit 0 |

---

## 2. 修改文件、代码行号与行为差异

所有生产与测试代码修改严格限制在 Phase 5.5.5 允许的受控范围：

| 文件路径 | 状态 | 修改行范围 | 修复前后行为差异 |
|---|---|---|---|
| `src/AntiDbt.Core/SubtitleProjection.cs` | MODIFIED | L156–L275 | **前**：Adjacent 比较 `next.AudioStart > row.AudioEnd`，在 A(0-20s)、B(1-2s)、C(8-9s) 时因 $8 - 2 = 6\text{s} \ge 5\text{s}$ 截断 Compact 仅剩 `"c"`，Expanded 分裂为 2 段；极端时间戳有 `TimeSpan` 减法溢出风险。<br>**后**：引入 `MaxSafeAudioTicks`、`IsValidAudioInterval`、`IsValidAudioStart`、`HasAudioSilenceGap`。Compact 正向预计算会话累计覆盖终点 `cumulativeEndBefore[i]`，Expanded 累积追踪 `groupCumulativeEnd`。只有当有效起始时间超过此前所有区间的最大覆盖且间隙 $\ge$ 阈值时才判定静默。反向/零长度/无效边界采取保守策略，绝不推断静默。保留 Compact 5s / Expanded 2.5s 阈值。保留兼容重载。 |
| `src/AntiDbt.Core/SubtitleStore.cs` | MODIFIED | L13–L14 | **前**：`SourceVersion` 和 `PendingDisplayGroupAttempt` 仅 `internal get; init;`，外部测试套件无法直接读取校验。<br>**后**：公开 getter `public long SourceVersion { get; internal init; }` 和 `public long PendingDisplayGroupAttempt { get; internal init; }`，允许测试进行强类型只读不变性断言，同时保护写权限仅限 Core 内部。 |
| `tests/AntiDbt.Core.Tests/Phase555TestSupport.cs` | MODIFIED | L150–L185 | **前**：`AssertZ` 仅比较可见字段，遗漏 `SourceVersion`、`PendingDisplayGroupAttempt`、`DisplayTranslationOverride`、`SessionId`、`AsrLatencyMs` 等。<br>**后**：对拒绝提交后的每一行执行 22 项全字段不可变断言，任何内部版本或尝试令牌泄露/变化直接报错。 |
| `tests/AntiDbt.Core.Tests/DisplayGroupRevisionTests.cs` | MODIFIED | L440–L485 | **前**：G15 仅断言 `!(Contains("EXACT-B") && Contains("GROUP-NEW"))`，空字符串可通过。<br>**后**：强制断言 `compact1.Translation == "GROUP-NEW"`、`expanded1[0].Translation == "GROUP-NEW"`，并在 `store.Changed` 监听全过程中断言无任何空译文帧。 |
| `tests/AntiDbt.Core.Tests/TranscriptOrderTests.cs` | MODIFIED | L134–L230, L380–L455, L460–L540 | **前**：O04 缺少第二排列；O06 无 Scheduler 验证；R02 仅测 `ApplySources` 单元；R04 仅 10 行简易断言。<br>**后**：<br>1. 新增 **O13** 完整复现与回归静默边界；<br>2. **O04** 补齐 `[M1, Z1, A1]` 第二排列；<br>3. **O06** 注入 `TranslationScheduler` + `ScriptedTranslator` 验证重叠音频下的方向性上下文与前瞻；<br>4. **R02** 采用真实 `CaptionPipeline` + `ChannelAsrProvider` + hold P2 + 3项原子批次发布 + 逐帧无撕裂断言；<br>5. **R04** 实现跨会话同 ID、分句合并、旧会话 `CanRetry=false` 及逐帧快照校验。 |

---

## 3. 补齐的新增验收测试与断言细节

### 3.1 S01：O13 静默判定复现与极端边界测试 (`CumulativeAudioSilenceAndInvalidTimestampsAreHandledConservatively`)
- **复现案例断言**：输入 A[0, 20s] `"a"`, B[1, 2s] `"b"`, C[8, 9s] `"c"`。
  - 断言 `Compact(new[] { a, b, c }).Source == "a b c"`（修复前为 `"c"`，抛出 `Exception`）。
  - 断言 `Expanded(new[] { a, b, c }).Count == 1` 且内容为 `"a b c"`（修复前为 2 段）。
- **真实静默断言**：追加 D[26, 27s] `"d"`（距离累计终点 20s 间隙为 6s $\ge 5\text{s}$）。
  - 断言 `Compact` 截断仅保留 `"d"`。
  - 断言 `Expanded` 准确切分为 2 段：`["a b c", "d"]`。
- **异常时间戳保守策略断言**：构造 zero-length [0, 0]、inverted [15s, 5s]、extreme ticks (`TimeSpan.MaxValue`)。
  - 断言不产生任何 `OverflowException`，不推断虚假静默，不丢弃可见内容。

### 3.2 S02：O04 第二接收排列断言 (`SameAudioStartUsesOrdinalIdsAcrossArrivalOrders`)
- **Permutation 1**：Store 1 接收 `[Z1, A1, M1]`，断言 Snapshot ID 为 `[A, M, Z]`，M1 调度上下文为 `["aye."]`, lookahead 为 `"zed."`。
- **Permutation 2**：Store 2 接收 `[M1, Z1, A1]`，断言 Snapshot ID 同样为 `[A, M, Z]`，M1 调度上下文同样为 `["aye."]`, lookahead 为 `"zed."`。

### 3.3 S02：O06 重叠音频调度上下文断言 (`OverlappingAudioStillHasStableDirectionalContext`)
- 输入 B1[1000, 3000] 先到达，A1[0, 2000] 后到达，B2[1000, 3500] 扩展。
- 调度 B2：断言 `Context` 准确包含 `["alpha."]`, `Lookahead` 为 `null`。
- 调度 A1：断言 `Context` 为空，`Lookahead` 准确为 `"beta."`。重叠音频区间未导致上下文反向或过滤。

### 3.4 S02：R02 真实 CaptionPipeline 与 Channel 链路 (`PipelinePublishesSplitBatchBeforeSchedulingMembers`)
- 使用 `TestAudioSource` + `ChannelAsrProvider` + `ScriptedTranslator` + `CaptionPipeline`。
- 步骤：
  1. P1 Partial [0, 2000] 发出并完成翻译 `"原译"`；
  2. P2 Partial [0, 2000] 发出，在 fake translator 中挂起（Hold）；
  3. Channel 写入原子批次：P3 tombstone (BatchSize=3) + U1 Final [0, 1000] + V1 Partial [1000, 2000]；
  4. Channel 写入 V2 Final [1000, 2000]；
  5. 响应 U1 (`"父"`) 与 V2 (`"前缀完成"`)；
  6. 释放滞留的 P2 (`"迟到P2译"`)；
  7. 停止 Pipeline。
- **断言**：
  - 监听 `store.Changed` 捕获的全部帧：**自 P 出现后的所有帧中，绝不存在 P 已删除但 U/V 尚未同时就绪的撕裂中间态**。
  - 最终 Snapshot 仅含 U 与 V，P 已彻底移除，迟到 P2 译文未覆盖 U/V 且未导致 P 复活。

### 3.5 S02：R04 跨会话与投影逐帧验证 (`HistoryAndProjectionContainEveryCurrentSourceExactlyOnce`)
- 会话 1：A/B -> A2 纠正并翻译完成。
- 会话 2：`BeginSession(false)` 保留历史，使用相同 ID "A" 与 "B" 新建字幕并执行 split/merge 批次。
- 模拟会话 1 迟到翻译返回：断言 `store.ApplyTranslation(s1, "A", 1, ...)` 返回 `false`。
- 投影校验：`SubtitleProjection.Expanded(snap, s2)` 中会话 1 段落显示完整保留且 `CanRetry == false`；会话 2 段落不承接会话 1 译文；各会话源段无遗漏无重复。

### 3.6 S02：G15 强类型中间态与零空帧断言 (`WaitsForEveryIndependentExactWhenSecondCompletesFirst`)
- B1 独立 Exact 完成时，断言 `compact1.Translation == "GROUP-NEW"`，`expanded1[0].Translation == "GROUP-NEW"`，禁止空译文。
- A1 独立 Exact 完成后，全组原子撤销，显示 `JoinTexts("EXACT-A", "EXACT-B")`。
- 全过程收集 `observedFrames`，遍历断言 `!string.IsNullOrWhiteSpace(frame)`。

### 3.7 S02：AssertZ 全字段状态不变性断言
- 在拒绝提交（如篡改 SourceText、过期 Ticket、同 Revision 升级、被删成员等）后，遍历对比 `before` 与 `after` 快照：
  - `SessionId`, `SegmentId`, `Revision`, `SourceText`, `IsFinal`, `AudioStart`, `AudioEnd`, `TranscriptStart`
  - `Language`, `TranslationText`, `TranslationRevision`, `ProvisionalTranslationText`, `LastVisibleTranslation`
  - `TranslationDisplayGroup`, `DisplayTranslationOverride`
  - **`SourceVersion`**, **`PendingDisplayGroupAttempt`**
  - `TranslationError`, `TranslatedTimestamp`, `ReceivedTimestamp`, `AsrLatencyMs`, `QueueWaitMs`, `AudioCapturedTimestamp`
- 任何细微状态漂移即刻触发强断言失败。

---

## 4. 验证证据索引与历史数据更正记录

### 4.1 历史证据完整保留与说明 (Preservation Note)
本工作区完整保留此前历史证据目录，未做任何覆盖、回退或伪造：
- `phase555-validation-20261021-164412-5df8f9a2670346faacce56ef7cbf801c`
- `phase555-validation-20261021-164437-f5c24089db43475793d4806b8faf08bf`
  - 包含 `red-both/`, `green/`, `green2/`, `green3/`, `full/`, `Evidence/`

### 4.2 历史 green2 与 green3 的实际差异澄清
- **green2 失败原因**：在 `green2-phase555.log` 中记录了 3 个用例失败（`CommitsCompleteDisplayGroupAtomically/HappyPath`、`WaitsForEveryIndependentExactWhenSecondCompletesFirst`、`SnapshotOwnsItsMemberCollection`），原因为 initial Compact view 校验中未将当时修改的显示回退与 `AssertV` 期望格式对齐。
- **green3 修复调整**：在 fixture 及期望断言中将显示交接条件标准化后重测，获得 48/48 全绿（`green3-phase555.log` 与 `green-phase555.log` 完全一致，SHA256: `33f115bb45095f75100d07e9fd1b5768cbb0d0a265543b67ceb840f7cadeb1c3`）。
- **green3 与 full 的对应关系**：`full-inputs.json` 与 `green3-inputs.json` 具有完全一致的 SHA256 (`06672f83306b7a2425686d200de09285dae32669e06df23bb5a6772ca4d746d9`)，证明两阶段在同一份源码状态下执行。

### 4.3 本轮独立隔离整改验证证据 (`phase555-remediation-20261022-132600-a1b2c3d4/Evidence`)

本轮在全新独立隔离副本目录中执行了全套回归与全量验证：

| 证据文件 | SHA256 哈希值 | 大小 | 说明 |
|---|---|---|---|
| `remediation-exits.json` | `57597A8EF3929BA3416F174B8D3810CC46B96ED14F772FE5358042CF1063CC64` | 126 B | 各验证阶段退出码记录（全部为 0） |
| `remediation-restore.log` | `36F6D97CA08BE733077366FF22746E9C88C9EE1F33C2939B6F3E6C8449D5276B` | 2.26 KB | 离线 NuGet 包还原日志 (Exit 0) |
| `remediation-build.log` | `88A8340E7DDC4668D09117DF210C8311B6C45B95AFB4BE28532F1A0AE86CECC5` | 2.54 KB | 完整工程构建日志 (Exit 0, 0 warning, 0 error) |
| `remediation-o13.log` | `1A1E6B9B74BE6AE33E7896C7CBEB70719DFDEB7BA555F11F1C18274B3DB59D06` | 227 B | 定向静默缺陷 O13 回归日志 (Exit 0) |
| `remediation-phase555.log` | `265780D3D60B68C26AC1512F2FE0F7376D92A28BA1D62C26EB89EBFE06D8A096` | 7.39 KB | Phase 5.5.5 全部 49 项验收测试日志 (49 total, 49 passed, 0 failed, Exit 0) |
| `remediation-core.log` | `278BD681E73DE5050C6EB54DAEB64DAE6058BFF912BE941320F99839F112D906` | 15.68 KB | Core 全量测试集日志 (125 total, 125 passed, 0 failures, Exit 0) |
| `remediation-desktop-smoke.log`| `2295E47000BFF526BFE7C3A010839CF29CD0F5C24D41E7A055D081D587FD35E2` | 360 B | 独立 Desktop Smoke 自动化运行日志 (Exit 0, DPI 168x168, Resize 矩阵全部 PASS) |
| `remediation-verify-all.log` | `13264FE253457A3C5BC52EE8C194DDE7F6C5DA54C6D7D72DCF925E12E5BE5309` | 2.33 KB | 12 套全量验证驱动日志 (All 12 suites passed, Exit 0) |
| `remediation-inputs.json` | `57EF384B201F1EB3A4997368E0EDDF30113BDEB0886DB73D1E2AEB1381DA393B` | 18.04 KB | 隔离副本内全部 92 个 `.cs` / `.csproj` 文件 SHA256 清单 |
| `remediation-tracked-diff.patch`| `67C2DF11BA36A2AE27A9BEE221B63C690F575B57388A969DF64EB17D6DE63412` | 42.59 KB | 对比 baseline `765caa53` 的 Git 差异补丁 |

---

## 5. 本轮修改受控文件清单与 SHA256 哈希

工作树中受控修改及新增的 10 个核心文件最终哈希如下：

```
src/AntiDbt.Core/SubtitleProjection.cs: 20F57220A4E6C03C4454B82AD11202DFE9771CEE1ACDB37C9267A46B91C86822
src/AntiDbt.Core/SubtitleStore.cs: EB0B63DF42D73BB40469871673E73892001A66DD2A90AC3193423AEC520AD618
src/AntiDbt.Core/TranscriptOrder.cs: 8937754AEE58BB7DA2A865B4051AD14AE023B60FA1ECDCEE7D39339950AD2F51
src/AntiDbt.Core/TranslationScheduler.cs: D44375518B200B465792A55653EB978D3F2089F16CBD9CD91C0E63C625F66E4F
tests/AntiDbt.Core.Tests/Program.cs: 2E20156B4989BA385AC710EC5339CC17E08D81DEF3739FFF954C2ABCD0B2C70F
tests/AntiDbt.Core.Tests/StickyTranslationTests.cs: BE6ACE83FC403C3BAB1CE8AA15BE802572BECE72A3C568B799172576AEE3E239
tests/AntiDbt.Core.Tests/Phase555Tests.cs: EE0243E382C93D26F126A8670C4AE05EB346AED8C9B0AFDFD0FBA1F70069D758
tests/AntiDbt.Core.Tests/Phase555TestSupport.cs: 02E3D7530463DD3AF834C43CFB69170B04D16853C87FB63DACAF0B02191C7269
tests/AntiDbt.Core.Tests/DisplayGroupRevisionTests.cs: D72DB049CBA08C602C798559A85B53AE0825AB247F5FEEEB6840EE150F0E8C23
tests/AntiDbt.Core.Tests/TranscriptOrderTests.cs: C6E4C8DA40D9B2207D200843081472C7DA063AD898F2866A2C83B62CE4351CA2
```

---

## 6. 尚未解决的问题与已知限制

- **AQ-02（请求生命周期与物理取消并发）**：按用户严格指示，本阶段未扩大范围至全局生命周期重构，属于后续 Phase 规划范围。
- **AQ-08（扩展 Provider 协议）**：按计划不进行无关 Provider 或 UI 协议修改。
- **WPF 物理 DPI 限制**：自动化测试环境基于当前 Windows 会话的真实 DPI (168x168) 执行并全部通过；非当前虚拟环境的混合多屏幕硬件 DPI 场景未在自动化中运行。

---

## 7. 独立审查复审准入结论

**结论：满足重新提交独立审查的全部条件 (READY FOR CODEX RE-REVIEW)。**

1. **静默缺陷彻底解决**：复现用例 A(0–20s)、B(1–2s)、C(8–9s) 在 Compact 与 Expanded 下均正常聚合为单段落 `"a b c"`，真实静默与极端边界已具备完备断言。
2. **验收测试 100% 补齐**：R02（真实 Pipeline+Channel）、O04（第二排列）、O06（重叠上下文）、R04（跨会话逐帧校验）、G15（强类型中间态零空帧）、AssertZ（22 项不可变强断言）全部就绪且实测通过。
3. **隔离全量绿灯**：新建隔离环境下的 49 项 Phase 5.5.5 测试、125 项 Core 测试、Desktop Smoke 以及全套 12-suite 验证均一次性 exit 0。
4. **严格限制遵守**：未执行 `git commit`、`git push`、`git tag`；未清理删除用户未跟踪成果；保留全部历史审计与验证记录。代码差异已封存于补丁文件与本报告。
