# antiDBT Phase 5.3 研发进度跟踪报告

## 一、工作包总览与状态

| 工作包 | 名称 | 优先级 | 状态 | 交付提交 |
|:---|:---|:---:|:---:|:---|
| **A0** | 冻结证据与建立可复跑入口 | P0 | **已完成** | 建立 `scripts/verify-all.ps1`，全量 12 个测试套件基线实测通过 |
| **A1** | 修复普通停止的尾句丢失与资源所有权 | P1 | **进行中** | 待实施 |
| **A2** | 字幕修正、译文状态与失败恢复 | P1 | 待开始 | 待实施 |
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
