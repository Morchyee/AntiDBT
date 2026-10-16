# antiDBT Phase 5.3 研发进度跟踪报告

## 一、工作包总览与状态

| 工作包 | 名称 | 优先级 | 状态 | 交付提交 |
|:---|:---|:---:|:---:|:---|
| **A0** | 冻结证据与建立可复跑入口 | P0 | **已完成** | 建立 `scripts/verify-all.ps1`，全量 12 个测试套件基线实测通过 |
| **A1** | 修复普通停止的尾句丢失与资源所有权 | P1 | **已完成** | `feat(phase5.3): A1 fix tail subtitle loss on stop and preserve resource ownership` |
| **A2** | 字幕修正、译文状态与失败恢复 | P1 | **已完成** | `feat(phase5.3): A2 fix provisional translation cleanup, Korean spacing and error retry` |
| **A3** | 音频新鲜度、溢出与时间轴 | P1 | 待开始 | 待实施 |
| **A4** | Provider 错误与重连预算 | P1 | 待开始 | 待实施 |
| **A5** | 桌面状态、设置和本机端点闭环 | P1 | 待开始 | 待实施 |
| **A6** | 测量与回归证据 | P2 | 待开始 | 待实施 |
| **A7** | 运行时评估与受支持运行时环境 | P2 | 待开始 | 待实施 |
| **A8** | Windows 实机验证与阶段交付报告 | P1 | 待开始 | 待实施 |

---

## 二、A0 基线与可复跑入口记录

- **基线分支**：`feature/phase-5.3-reliability`
- **起点 Commit**：`30165fb230c9a84fd05ed824c3ef7ee0e7a37579` (基于 `v0.5.2-beta`)
- **执行环境**：Windows 11 x64, .NET SDK 9.0.319, .NET Runtime 9.0.21 & 10.0.13
- **统一可复跑脚本**：`scripts/verify-all.ps1`
- **12 项套件基线实测**：
  1. `AntiDbt.Core.Tests` [OfflineUnit] -> PASS (38/38)
  2. `AntiDbt.Windows.Tests` [PlatformWindows] -> PASS (19/19)
  3. `AntiDbt.CloudSpeech.Tests` [SdkContracts] -> PASS (36/36)
  4. `AntiDbt.CloudSpeech.Tests --native-smoke` [SdkNativeLoad] -> PASS
  5. `AntiDbt.Providers.Tests` [ProviderContracts] -> PASS (26/26)
  6. `AntiDbt.Providers.Multi.Tests` [ProviderMockFactory] -> PASS (86/86)
  7. `AntiDbt.Providers.Multi.Tests --localhost` [LocalhostCombinations] -> PASS (13/13)
  8. `AntiDbt.Language.Tests` [LanguageRegistry] -> PASS (78/78)
  9. `AntiDbt.Domestic.Tests` [DomesticLoopback] -> PASS (74/74)
  10. `AntiDbt.Domestic.Tests --offline` [DomesticOffline] -> PASS (42/42)
  11. `antiDBT.exe --smoke-test` [WpfAutomation] -> PASS (76/76)
  12. `AntiDbt.Diagnostics.Tests` [SyntheticPerformance] -> PASS (40000 cycles, retained < 1.2MB)

---

## 三、自动化测试基础设施缺陷记录与加固

- **问题现象**：
  - `App.xaml.cs` 原本只检查 `--smoke-test` 参数。若开发/自动化脚本传入 `--smoke`，WPF 应用将以标准 GUI 模式启动并持续运行，导致测试执行器无限等待或阻塞。
  - 原 `scripts/verify-all.ps1` 采用 `Start-Process ... -Wait`，缺乏超时保护、子进程树治理以及标准错误捕获机制，一旦子进程未正常退出便会导致构建挂起。
- **加固实施**：
  1. `src/AntiDbt.Desktop/App.xaml.cs`：统一参数解析，兼容识别 `--smoke-test` 与 `--smoke`；捕获所有冒烟异常输出至 `desktop-smoke-error.txt` 与控制台 stderr，杜绝无声以 code 1 退出。
  2. `scripts/verify-all.ps1`：
     - 重构 `Run-Suite` 与 `Desktop.Smoke` 调用方式，采用 `System.Diagnostics.ProcessStartInfo` 显式重定向 stdout/stderr。
     - 增加严格的超时防护（普通套件 60 秒，桌面 Smoke 60 秒）。
     - 超时后强制调用 `taskkill.exe /PID $proc.Id /T /F` 递归清理完整进程树，杜绝僵尸进程。
     - 失败时自动提取并高亮显示 `desktop-smoke-error.txt` 及控制台异常堆栈，支持精确断言排查。

---

## 四、A1 实施记录：普通停止的尾句丢失与资源所有权修复 (F1)

- **缺陷根因确认**：
  - 之前 `MainWindow.StopCaptureCoreAsync` 在停止前直接执行 `store.BeginSession(false)`，导致 `_session` 递增。`CaptionPipeline` 内部排空所得的延迟 Final ASR 结果与翻译因 session 不匹配被 `SubtitleStore.ApplySource` 和 `ApplyTranslation` 静默拒绝。
  - `MainWindow` 立即将 `pipeline = null`，导致清理失败时资源句柄丢失，重试时可能发生硬件设备占用冲突。
- **实施改动**：
  1. `Contracts.cs`：引入 `StopReason` 枚举（`UserStop`, `Reconfigure`, `Shutdown`）。
  2. `CaptionPipeline.cs`：
     - `StopAsync(StopReason reason = StopReason.UserStop)`：支持原因区分。
     - `UserStop`：保留当前 generation，执行有界排空（统一 10s 截止，ASR 4s 排水 + MT 3s 排水），排空完毕后再封锁 session。
     - `Reconfigure` / `Shutdown`：立即取消 `_lifetime`，切断旧数据流，2s 内快速退出。
  3. `MainWindow.cs`：
     - `StopCaptureCoreAsync(StopReason reason)`：普通停止时不提前重置 session，允许尾句排空写入并渲染到 UI；重配置时立即阻断旧会话。
     - 引入 `pipelineCleanupFailed` 状态跟踪，保留 resource ownership 直至成功释放；清理失败时禁止启动冲突管线，并在重试时尝试强制清理。
     - 完善 `PipelineState.Retrying` 与 `PipelineState.Stopping` UI 状态分支映射。
  4. **回归与测试验证**：
     - `AntiDbt.Core.Tests`：新增 `reconfigure stop immediately isolates session and skips delayed final` 测试，总用例 39/39 全部通过。
     - `AntiDbt.Desktop.Smoke`：新增 5 类桌面入口验证（延迟尾句完整排空渲染、切语言即时隔离、重复停止幂等性、资源释放失败防冲突、100 次启停切换压测），全量 12 项测试套件 100% 通过。

---

## 五、A2 实施记录：字幕修正、译文状态与失败恢复 (F2, F5, F6)

- **缺陷根因确认**：
  - **F2 幽灵译文与不兼容草稿残留**：`SubtitleStore.ApplySource` 在原文改写甚至变空时无条件将旧译文移入 provisional；`SubtitleProjection` 将其与空 source 拼接，导致已清除的原文仍残留原译文。
  - **F6 韩文词间空格丢失**：`NeedsSpaceBetween` 原逻辑将 Hangul（`0xAC00..0xD7AF`）并入 CJK 统一判断，导致两个韩文词拼接时被误当成汉字/假名而省略了空格。
  - **F5 翻译错误不可见且无法恢复**：翻译失败仅记录在 segment 中，UI 状态仍为绿色“聆听中”，字幕模板未呈现错误提示，且没有提供针对 Final 失败结果的手动重试入口。
- **实施改动**：
  1. `src/AntiDbt.Core/SubtitleProjection.cs`：
     - 拆分 `IsHanOrKana` 与 `IsHangul`，确保韩文与韩文、韩文与英文之间保留词间空格。
     - 幽灵译文清除：当段落原文为空时强制译文为空，`Expanded` 过滤掉空白原文段落。
     - 扩展 `SubtitleParagraph` 模型，引入 `TranslationPending`, `TranslationError`, `CanRetry`, `ErrorDisplay`, `TranslationOpacity` 字段。
  2. `src/AntiDbt.Core/SubtitleStore.cs`：
     - 实现兼容草稿规则 `IsCompatibleDraft`（前缀扩展与 LCS 长句相似度精细判定），原文为空或发生破坏性重写（如 `turn left -> turn right`）时彻底清空 provisional 译文。
     - `ApplyProvisionalTranslation` 校验传入来源文本是否兼容当前最新原文，阻断迟到陈旧草稿。
     - 增加 `ClearTranslationError` 供重试重置状态。
  3. `src/AntiDbt.Core/TranslationScheduler.cs`：
     - 调用 `ApplyProvisionalTranslation` 时传入当前 sourceText 以供兼容性校验。
  4. `src/AntiDbt.Core/CaptionPipeline.cs`：
     - 暴露 `RetryTranslation(long session, string segmentId, int revision)`，重试当前会话中失败的 Final 字幕。
  5. `src/AntiDbt.Desktop/MainWindow.cs`：
     - 字幕模板绑定 `TranslationOpacity`（pending 时 0.65 半透明展示，不插入多余文字）。
     - 增加错误提示徽章（`⚠️ 翻译异常: ...`）和“重试”按钮，点击调用 `RetryTranslation`。
     - 暴露 `TestVisibleParagraphs` 供桌面自动化测试进行真实控件与模板断言。
  6. **回归与测试验证**：
     - `AntiDbt.Core.Tests` 新增 5 类用例（44/44 PASS）：韩文空格与汉字无空格对比、空白修正彻底清除译文、语义冲突清空草稿与前缀扩展继承、Final 翻译失败错误记录与重试成功、新 revision 到达自动取消/抢占旧重试。
     - `AntiDbt.Desktop.Smoke` 新增 3 项桌面实测：真实 UI 呈现韩文词间空格、空白修正桌面无幽灵译文、翻译失败红字警告呈现及点击重试恢复。全量 12 项测试套件 100% PASS。

