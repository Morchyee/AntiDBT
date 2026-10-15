# antiDBT Phase 5.3 研发进度跟踪报告

## 一、工作包总览与状态

| 工作包 | 名称 | 优先级 | 状态 | 交付提交 |
|:---|:---|:---:|:---:|:---|
| **A0** | 冻结证据与建立可复跑入口 | P0 | **已完成** | 建立 `scripts/verify-all.ps1`，全量 12 个测试套件基线实测通过 |
| **A1** | 修复普通停止的尾句丢失与资源所有权 | P1 | **已完成** | `feat(phase5.3): A1 fix tail subtitle loss on stop and preserve resource ownership` |
| **A2** | 字幕修正、译文状态与失败恢复 | P1 | **进行中** | 待实施 |
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

## 三、A1 实施记录：普通停止的尾句丢失与资源所有权修复 (F1)

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

