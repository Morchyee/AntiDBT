# antiDBT 第二轮独立审计专项修复交付成果报告 (Round 2 Audit Fix Delivery)

- **基准分支**：`fix/phase5.3-independent-audit`
- **审计基线 Commit**：`7cd43e272d28d5b0d2433f392cae3f965fb79a7a` (.NET 10 LTS Baseline)
- **修复完成 Commit**：`40bd49219324dc05865d44921ee4cfda600e49a6` (Clean working tree)
- **审查对接对象**：GPT-6 Astra（Codex 独立复审团队）
- **报告时间**：2026-10-16 14:30 (UTC+8)

---

## 一、执行概览与工作树状态

依据 GPT-6 Astra（Codex）对 `7cd43e2` 的第二轮独立源码审计报告（`ROUND2-INDEPENDENT-AUDIT.md`）与任务书（`ANTIGRAVITY-ROUND2-FIX-TASKBOOK.md`），我们在本地独立修复分支 `fix/phase5.3-independent-audit` 上彻底完成了全部 **R0～R6 工作包** 的实施与 **F01～F12 缺陷** 的逐项闭环修复。

所有提交均按工作包原子划分，严守工程安全规范：
- 严格在 `fix/phase5.3-independent-audit` 分支实施，**未执行 `git push`**、**未合并 `main` 分支**、**未修改或覆盖既有 GitHub Tag 与 Release 附件**。
- 保留全部既有多语言 UI（中/英/日）、4 ASR / 8 MT Provider、同语言直通与 .NET 10 LTS 架构。
- 绝不降低断言标准或吞噬异常，所有独立复现场景均转化为工程回归测试并通过。

---

## 二、工作包提交记录 (Git Commit Trail)

从 `7cd43e2` 基线开始的原子提交链如下：

| 工作包 | Git Commit SHA | 提交信息 (Commit Subject) | 关联缺陷 |
| :--- | :--- | :--- | :--- |
| **R0** | `3c5523f` | `fix(build): R0 secure candidate packager path bounds and make test tfm resolution deterministic` | F06, F07 |
| **R1** | `0504f64` | `fix(pipeline): R1 prevent false completion on stop timeout and track cleanup ownership` | F01 |
| **R2** | `4d7f46f` | `fix(providers): R2 parse Volcengine error frames per official spec and preserve reader errors` | F02, F03, F11 |
| **R3** | `1cdc240` | `fix(subtitles): R3 strict draft compatibility and accurate paragraph retry binding` | F04, F05, F11 |
| **R4** | `a61bb28` | `fix(audio): R4 accurate Groq overflow tracking, drain preservation, and discontinuity mapping` | F08 |
| **R5** | `e99d695` | `fix(telemetry-desktop): R5 wire pipeline telemetry, normalize audio level, and enforce profile/history sync` | F09, F10, F12 |
| **R6** | `40bd492` | `fix(packaging): R6 ensure PS 5.1 compatibility and robust archive locking in candidate builder` | A0, A6, A7, A8 |

---

## 三、F01～F12 缺陷修复与关闭证据矩阵

| 缺陷编号 | 等级 | 审计发现核心根因 | 本轮代码修复方案与工程合同 | 验证测试与关闭证据 |
| :--- | :--- | :--- | :--- | :--- |
| **F01** | **P1** | `StopAsync` 超时仍假标记 `_stopCompleted = true`，导致后续调用 `DisposeAsync` 假装清理成功，残留后台读写与未释放资源 | `CaptionPipeline.StopAsync` 在超时时不设 `_stopCompleted = true` 并抛出 `TimeoutException`；`DisposeAsync` 仅在 `Completion.IsCompleted` 时置 `_disposed = true` 并释放托管/非托管资源；`MainWindow` 记录 `pipelineCleanupFailed` 并拦截并发会话启动 | `uncooperative ASR write times out StopAsync and preserves failure state on DisposeAsync` PASS；`uncooperative ASR connect times out StopAsync and prevents false completion` PASS |
| **F02** | **P1** | 火山 ASR 错误帧未遵循官方二进制协议（4B Header + 4B Code + 4B Size + UTF-8），错误码映射错误，且误将 1000（Normal Closure）映射为认证错误 | `VolcengineAsrProvider` 按协议解析 type 15 错误帧：提取 Big-Endian 4 字节状态码与 4 字节消息长度；精确映射官方状态码（45000001: Configuration, 45000002: Authentication, 45000003: Quota, 45000004/45000005/1005: RateLimited, 45000081: Timeout）；彻底移除将 1000 误作认证错误的逻辑 | `Providers.Tests` 中 `volcengine binary error frame decoding according to official spec` PASS；覆盖 45000001, 45000002, 45000003, 1005 及畸形帧边界 |
| **F03** | **P1** | 永久错误被并发写入的取消异常掩盖，导致管线陷入 10 次无效重连 | `CaptionPipeline` 在重试判定前优先检查 `reader.Exception` 或等待读取端返回真实异常，确保 `ProviderErrorKind.Authentication/Quota/Configuration` 等永久错误不被 `connection.Cancel()` 触发的 `OperationCanceledException` 掩盖，立即抛出致命异常 | `permanent reader error is preserved and not replaced by concurrent send cancellation` PASS（`FatalReadAsr` 仅创建 1 次 session，重试计数为 0，终态确认为 `Error`） |
| **F04** | **P1** | 字符串相似度（LCS >= 0.75）在单词否定语义反转时仍保留旧草稿译文 | `SubtitleStore` 彻底弃用 LCS 算法，改为**严格词边界前缀扩展**（Latin 按空格/标点边界，CJK 按单字），并在追加文本时扫描否定词（not/no/never/don't/cant/won't/不/没/非/未/别等）、疑问助词及反义转折；草稿超过 5 秒或跨 3 个 revision 强制老化过期 | `incompatible long revision with negation does not retain old translation` PASS；`semantic changes (direction, numbers, entity, units) reject old draft` PASS |
| **F05** | **P1** | 多段落合并后，段落重试按钮绑定了段落末尾成功的 SegmentId，导致错误重试定位偏差 | `SubtitleProjection` 计算段落重试信息时，优先定位段落内首个处于失败状态的 Final Segment（而非盲目取最后一行）；在 `SubtitleParagraph` 中暴露正确的 `SegmentId`、`Revision`、`SessionId` 和 `CanRetry`，并隔离当前会话 | `multisegment paragraph retry points to failed segment not successful segment` PASS；`multisegment paragraph retry ignores old session failure for retry eligibility` PASS |
| **F06** | **P1** | `scripts/verify-all.ps1` 静态写死 TFM，对跨分支/干净工作区测试入口脆弱 | 脚本从 `AntiDbt.Core.csproj` 的 XML 中动态解析 `TargetFramework`，支持 `-TargetFramework` 参数覆盖；对可执行路径和参数进行精确空格转义与安全引用；通过 `Write-SummaryJson` 实时写入单套件进度与结果，防止旧报告残留假全绿 | `scripts/verify-all.ps1` 动态解析 `net10.0` / `net10.0-windows`，12 套件全量正常执行 |
| **F07** | **P1** | `scripts/build-candidate-package.ps1` 存在路径遍历安全隐患，支持危险 Output 参数可能递归删除非受限目录 | 引入严格的白名单安全围栏：`$destination` 必须为 `$taskRoot/artifacts` 或 `$taskRoot/publish` 的直接子目录，禁止任何相对路径逃逸（`.`、`..`、盘根目录）；动态求值 TFM，记录 SDK 版本与 Clean/Dirty 状态至各包独立 manifest | 针对非法路径（如 `.`、`..`、盘根）触发 `Security Error` 拦截测试，合法子目录构建正常 |
| **F08** | **P2** | `GroqWhisperProvider` 在 `DropOldest` 模式下 `TryWrite` 恒为 true 导致丢弃无法计数；管线排空丢弃尾部音频；discontinuity 映射不准确 | `GroqWhisperProvider` 内部任务通道改为 `FullMode = Wait`，当队列满时通过 `TryRead` 显式弹出旧任务并精确自增 `droppedJobs`；`CaptionPipeline` 在 `UserStop` 排空时绕过 `TrimStale`，保留停止预算内的尾部语音；时间戳 mapping 锚点追踪 `_queue.Discontinuities`，有缺口时不计入健康时延样本，移除 `<= 30s` 人为过滤 | `AntiDbt.Providers.Tests` 新增 `Groq Whisper dropped jobs accurately counted under backpressure` PASS（注入 10 任务、容量 4，准确报告丢弃 5 项）；`Core.Tests` 队列统计与排空回归 PASS |
| **F09** | **P2** | 生产 `PipelineTelemetryCollector` 未接入实际音频/ASR/翻译/UI 路径，测试仅手工调用 `Record` | `CaptionPipeline` 将 `Telemetry` 实例传入 `TranslationScheduler`；在 CacheHit、Success、Timeout、Failed 与 Cancelled 真实执行路径打点记录，精确关联 SessionId、SegmentId、Revision、IsFinal、QueueWaitMs、AsrServiceMs、MtDebounceWaitMs、MtServiceMs 与 TotalPipelineMs | `production pipeline wires telemetry and records stage metrics during normal execution` PASS（真实管线执行后 `p.Telemetry.Snapshot().Count > 0`，指标全部捕获） |
| **F10** | **P2** | `AudioLevelChanged` 使用生产 RMS `level > 50.0` 判断有声，由于实际 RMS 处于 `[0.0, 1.0]`，导致界面永久显示“无音频信号” | `MainWindow` 将 `level > 50.0` 归一化修复为 `level > 0.005`；添加真实有声与静音状态测试，合理幅度 PCM 能正常刷新 `lastAudibleTicks` 并清除无音频提示，静音持续超过 3 秒才显示静音警告 | `Desktop.Smoke` 新增 `VerifyPhase5ReliabilityR5Async` 验证 PASS（测试覆盖静音、<0.005 子阈值、>0.005 有声阈值，Tip 状态精准更新） |
| **F11** | **P2** | 429 Retry-After 未设上限保护；重试无 revision 校验与冷却保护 | `CaptionPipeline` 对 429 限流超过 120 秒自动重试上限时显式抛错提示手动重试；`RetryTranslation` 增加最新 revision 校验、已翻译成功拦截、无错误拦截与 2 秒单 segment 冷却保护 | `pipeline retry translation enforces session, revision, final, and cooldown` PASS；火山 1005 映射为 RateLimited 并正确应用退避 |
| **F12** | **P2** | `MaxHistory` 动态修改不生效；`HasPipelineConfigChanged` 遗漏备用 Provider 凭据/端点/模型比对；设置取消缺乏真实窗口测试 | `SubtitleStore.MaxHistory` 实现为带动态尾部截断的属性并保持会话隔离；`MainWindow.ApplySettingsResultAsync` 在应用设置时同步 `store.MaxHistory`；`HasPipelineConfigChanged` 补充对 `FallbackEnabled` 下 `TranslationProfiles[FallbackTranslationProviderId]` 的深度配置比对；`TestOpenSettingsCancel` 实例化真实 `SettingsWindow` 并关闭 | `subtitle store dynamic MaxHistory truncates history and preserves session` PASS；`Desktop.Smoke` 验证 fallback 凭据修改触发热重载 PASS；真实设置取消测试 PASS |

---

## 四、全量测试套件执行证据 (12/12 PASS)

在干净的 `fix/phase5.3-independent-audit` 分支（Commit: `40bd492`）上执行 `powershell -File scripts/verify-all.ps1 -ArtifactsDir artifacts/round2-audit-fixed`，全部 12 个套件通过：

```
==========================================================
 antiDBT Phase 5.3 Unified Verification Suite
 Commit: 40bd49219324dc05865d44921ee4cfda600e49a6 (fix/phase5.3-independent-audit)
 Target: net10.0 (net10.0-windows)
 SDK:    10.0.402
 Output: D:\Desktop\AntiDBT-Gemini\antiDBT\prototype\artifacts\round2-audit-fixed
==========================================================

[1/8] Compiling AntiDbt.sln (using .dotnet/dotnet.exe, target: net10.0)...
  -> Build succeeded.
Running Core [OfflineUnit]...
  -> Core: PASS (11.98s) [62 tests, 0 failures]
Running Windows [PlatformWindows]...
  -> Windows: PASS (0.30s)
Running CloudSpeech [SdkContracts]...
  -> CloudSpeech: PASS (0.24s)
Running CloudSpeech.NativeSmoke [SdkNativeLoad]...
  -> CloudSpeech.NativeSmoke: PASS (0.15s)
Running Providers [ProviderContracts]...
  -> Providers: PASS (3.88s) [34 tests, 0 failures]
Running Providers.Multi [ProviderMockFactory]...
  -> Providers.Multi: PASS (6.78s)
Running Providers.Multi.Localhost [LocalhostCombinations]...
  -> Providers.Multi.Localhost: PASS (6.64s)
Running Language [LanguageRegistry]...
  -> Language: PASS (3.88s)
Running Domestic [DomesticLoopback]...
  -> Domestic: PASS (4.73s)
Running Domestic.Offline [DomesticOffline]...
  -> Domestic.Offline: PASS (1.56s)
Running Desktop.Smoke [WpfAutomation]...
  -> Desktop.Smoke: PASS (21.79s) [150 assertions, 0 failures]
Running Diagnostics.Performance [SyntheticPerformance]...
  -> Diagnostics.Performance: PASS (5.95s)

==========================================================
 All 12 Verification Suites Passed Successfully! (Total: 67.88s)
 Summary: artifacts/round2-audit-fixed/verification-summary.json
==========================================================
```

---

## 五、.NET 10 LTS 候选自包含便携包身份与哈希

使用加固后的 `scripts/build-candidate-package.ps1`，在干净的工作树下构建了 Windows x64 自包含候选包：

- **版本标识**：`0.5.3-beta.1`
- **目标框架**：`net10.0-windows`
- **.NET SDK**：`10.0.402`
- **运行时环境**：`win-x64`
- **构建工作树状态**：`Clean`
- **包输出目录**：`artifacts/phase5.3-round2/dist/antiDBT-v0.5.3-beta.1-win-x64`
- **ZIP 压缩包**：`artifacts/phase5.3-round2/dist/antiDBT-v0.5.3-beta.1-win-x64.zip`
- **SHA256 校验和**：
  - `antiDBT.exe`：`45A8017284FAB63C904485C9242BAEDB966C44707DD39C86F4E6C1F52675A7DE`
  - `Microsoft.CognitiveServices.Speech.core.dll`：`B045D8BA122F8F015ED359B7B3DF3C25C48271FC51282686D4F131E5FC51E91F`
  - `antiDBT-v0.5.3-beta.1-win-x64.zip`：`FD9DC01A0E38A9EAE0D522C5249C7C8CCB8C789A1C71310C2F4B8A3A772444D6`
- **独立 Manifest**：`artifacts/phase5.3-round2/dist/candidate-manifest.json`

---

## 六、测试边界、声明纠正与诚实声明 (R6)

根据审计任务书第 9 条，我们郑重纠正并明确以下技术边界：

1. **运行时生命周期定义纠正**：
   - **.NET 9 是 STS（标准期限支持，支持至 2026 年 5 月）**，而非 LTS。
   - **.NET 10 是长期支持版本（LTS，支持至 2028 年 11 月）**。
2. **测试覆盖边界严格划定**：
   - 12 个自动化套件通过，代表**接口契约、单测与本地回环模拟通过**，不代表 12 类真实生产场景全部验证。
   - 150 条 WPF 桌面 Smoke 断言，代表**WPF 状态机、模板绑定与自动化逻辑正确**，不等于物理多屏、DPI 动态拖拽、高精度触控板在各类 Windows 硬件上的实体验证。
   - 60 秒 Synthetic Soak 与二十多秒 Smoke 仅为冒烟与瞬态指标观测，**不能替代 2 小时以上长周期真实音频播放下的物理声卡与句柄稳定性验证**。
   - 当前 .NET 9 与 .NET 10 的内存/CPU 观测，仅作为单次 Smoke 过程采样记录，**不作为 .NET 10 JIT 带来确定性性能提升的定论**。
3. **真实商用云端凭据状态**：
   - 本地未配置真实商用授权凭据（Volcengine / Azure / Google / DeepL / Baidu / Aliyun）。
   - **真实长时识别准确率、长句翻译质量、国际公网抖动下的退避重连均如实标记为【尚未验证】**。

---

## 七、交付状态与后续建议

- **当前状态**：第二轮专项修复 R0～R6 已经全部实施、测试、验证并原子提交至 `fix/phase5.3-independent-audit`。
- **发布与合并约束**：
  - 未经用户明确许可，**绝不合并 main 分支**。
  - **绝不覆盖既有 Release 资产**（v0.5.3-beta 线上保持原状）。
  - **绝不推送到远程仓库**。
- **交付建议**：请将本报告与分支 `fix/phase5.3-independent-audit` 移交 GPT-6 Astra（Codex）进行第三轮独立复审。待独立复审确认 F01～F12 全部关闭且无新增阻塞缺陷后，再行评估 Phase 5.4 迭代规划。
