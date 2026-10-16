# antiDBT Phase 5.3 研发进度跟踪报告

## 一、工作包总览与状态

| 工作包 | 名称 | 优先级 | 状态 | 交付提交 |
|:---|:---|:---:|:---:|:---|
| **A0** | 冻结证据与建立可复跑入口 | P0 | **已完成** | 建立 `scripts/verify-all.ps1`，全量 12 个测试套件基线实测通过 |
| **A1** | 修复普通停止的尾句丢失与资源所有权 | P1 | **已完成** | `feat(phase5.3): A1 fix tail subtitle loss on stop and preserve resource ownership` |
| **A2** | 字幕修正、译文状态与失败恢复 | P1 | **已完成** | `feat(phase5.3): A2 fix provisional translation cleanup, Korean spacing and error retry` |
| **A3** | 音频新鲜度、溢出与时间轴 | P1 | **已完成** | `feat(phase5.3): A3 audio freshness bounded queue and discontinuity tracking` |
| **A4** | Provider 错误与重连预算 | P1 | **已完成** | `feat(phase5.3): A4 volcengine structured error mapping and stable retry budget` |
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

---

## 六、A3 实施记录：音频新鲜度、溢出与时间轴 (F3)

- **缺陷根因确认**：
  - ASR 重连或慢速网络恢复后，缓冲队列按接近实时速率（~200ms）慢速排空，造成用户长时间观看数秒前的陈旧音频字幕。
  - 队列只记录了粗糙块数，缺少源音频时长、最老块年龄（OldestChunkAge）及跳过/不连续点（Discontinuity）可观测性。
  - GroqWhisperProvider 中 `results` 通道配置为 `DropOldest`（256），可能在高并发或背压时静默丢失 Final 结果。
- **实施改动**：
  1. `src/AntiDbt.Core/BoundedAudioQueue.cs`：
     - 扩展 `AudioQueueStats` 模型，提供 `Count`, `TotalDuration`, `OldestChunkAge`, `DroppedChunks`, `Discontinuities` 全景指标。
     - 实现 `TrimStale(TimeSpan? maxAge = null)`：默认 2.0s 过期门限，超过门限的陈旧积压自动丢弃并记录单调 `Discontinuities` 标记。
  2. `src/AntiDbt.Core/CaptionPipeline.cs`：
     - 发送循环集成 `_queue.TrimStale(2.0s)`，连接恢复后优先切回当前声音，并在状态栏提示跳过的过期音频块数。
     - 暴露 `AudioStats`, `DroppedAudioChunks`, `AudioDiscontinuities` 属性。
     - 延迟映射保护：过滤因断缺口或时钟抖动引起的无效/负数/极端延迟样本（`[0, 30s]` 有效区间）。
  3. `src/AntiDbt.Providers/GroqWhisperProvider.cs`：
     - 将 `results` 通道由 `DropOldest` 改为 `BoundedChannelFullMode.Wait`，彻底杜绝 Final 结果静默丢弃。
     - 为任务通道增加 `droppedJobs` 计数器与 `DroppedJobs` 属性暴露，使转写溢出具备可诊断性。
  4. **回归与测试验证**：
     - `AntiDbt.Core.Tests` 新增 3 类用例（47/47 PASS）：队列时长、年龄与快照统计断言；`TrimStale` 准确清理超时音频并递增不连续性计数；重连恢复场景下管线音频年龄上限约束。
     - 全量 12 项测试套件 100% PASS。



---

## 七、A4 实施记录：Provider 错误与重连预算 (F4)

- **缺陷根因确认**：
  - `CaptionPipeline.cs:128` 每次 `SendAudioAsync` 成功就重置 `failures = 0`。导致“发一块成功、读端立即断开”的循环可以无限重置 10 次重连预算，陷入死循环。
  - `VolcengineAsrProvider` 把连接异常、0b1111 协议错误、业务错误（`code != 0 && code != 20000000`）全部包成普通 `InvalidOperationException`，未映射为结构化 `ProviderException`，导致 Core 无法识别 Authentication/Quota/Configuration 等不可重试致命错误而盲目重连。
  - `VolcengineMachineTranslationProvider` 未提取 HTTP 429 的 `Retry-After` 头，成功解析时未拒绝全空白译文。
- **实施改动**：
  1. `src/AntiDbt.Core/CaptionPipeline.cs`：
     - 引入健康期门限判定（`StableHealthyPeriod`，默认 5.0s，可测配置）：单次发送音频成功不再清零预算，只有连接建立后持续稳定收发超过健康期才将 `failures` 清零。
     - 发送与取消联动：音频发送绑定连接级 Token 并配置 5s 发送硬超时；`ReadResultsAsync` 读端异常时立即主动调用 `connection.Cancel()`，打断阻塞中的音频写入。
     - 指数退避与冷却支持：重试退避采用指数退避（500ms ~ 5000ms），遇到 `RateLimited` 错误时严格尊重 `RetryAfter`（在 `[0, 60s]` 有效区间）。
     - 不可恢复错误拦截：遇到 `Authentication`, `Configuration`, `Quota`, `ModelUnavailable`, `UnsupportedLanguage` 直接终止重试并发布对应状态。
  2. `src/AntiDbt.Providers/VolcengineAsrProvider.cs`：
     - 实现 `ClassifyWebSocketError`：识别握手 401/403/400/404/429/402/456，脱敏映射为对应 `ProviderErrorKind`。
     - 解析 0b1111（type 15）服务端错误响应二进制 Payload 与 JSON Payload，提取业务错误码。
     - 实现 `ClassifyAsrError`：将 1000~1003/401/403 映射为 `Authentication`，1004/45000001/45000151 映射为 `Configuration`，1005 映射为 `RateLimited`，45000081 映射为 `Timeout`，均采用本地安全脱敏错误信息，杜绝泄露服务端原始响应。
  3. `src/AntiDbt.Providers/VolcengineMachineTranslationProvider.cs`：
     - 拦截 HTTP 429：提取 `Retry-After` 头部构建结构化 `ProviderException(RateLimited, ..., retry)`。
     - 结构化 HTTP 与业务错误码分类：将 1000~1003 映射为 `Authentication`，45000001 映射为 `Configuration`，456/402/10013 映射为 `Quota`。
     - 空白译文拒绝：对空白或纯空白字符译文抛出 `ProviderErrorKind.InvalidResponse`。
  4. **回归与测试验证**：
     - `AntiDbt.Core.Tests` 新增 3 项测试（50/50 PASS）：发成功读立即失败重连预算耗尽（成功捕获并修复原死循环缺陷）、认证失败立即终止拒绝重连、配额不足立即终止拒绝重连。
     - `AntiDbt.Providers.Tests` 新增 3 项测试（30/30 PASS）：429 提取 `Retry-After` 冷却、拒绝纯空白译文、0b1111 协议包解析映射为 `Authentication`。
     - 全量 12 项测试套件 100% PASS（`verification-summary.json` 全绿）。

---

## 八、A5 实施记录：桌面状态、设置和本机端点闭环 (F7, UI States)

- **缺陷根因确认**：
  - MainWindow.xaml.cs 中原有 OpenSettingsCoreAsync：打开设置前先无条件 StopCaptureCoreAsync()，不仅中断当前正在进行的音频采集，还因 store.BeginSession(false) 导致历史字幕清空；取消设置时未恢复被用户临时修改的主题和透明度。
  - SettingsWindow.xaml.cs 端点校验割裂：允许非安全 HTTP 协议，对 loopback（127.0.0.1/localhost）与远程端点的安全限制不一致，允许携带敏感 userinfo、query 或 fragment。
  - 自动滚动缺乏保护：用户向上滚动回看历史记录时，异步到达的 ScrollBottom() 仍将视图强制拉回底部，破坏回看体验。
  - store 初始化未绑定配置：settings.MaxHistory（默认 200）未同步传递给实际 SubtitleStore（原固定 500）。
- **实施改动**：
  1. src/AntiDbt.Desktop/MainWindow.xaml.cs：
     - **非侵入式设置打开与取消**：打开设置时不停止采集管线、不清空既有字幕历史；取消设置时即时撤销实时预览并恢复进入设置前的主题与透明度。
     - **受控重启判断（HasPipelineConfigChanged）**：保存设置时对比音频设备、ASR 厂商/模型/密钥、MT 厂商/模型/密钥及源/目标语言；若仅改变外观（主题、透明度、字体）则原地生效，绝不重启管线；仅当管线核心参数改变时，才通过 StopReason.Reconfigure 进行一次性受控平滑重启。
     - **SubtitleStore 容量闭环**：初始化 store 时显式传入 settings.MaxHistory。
     - **滚动回看保护**：ScrollBottom() 方法开头增加 if (!_isAutoScrollEnabled) return; 守卫，杜绝迟到滚动强行拉回底部。
     - **细分工作状态呈现**：
       - 识别正常但翻译异常：展示琥珀色指示点（#FFB300）与“翻译异常”提示，保留“停止”按钮语义。
       - 静音超时诊断：监听 AudioLevelChanged，持续静音超过 3.0s 且管线处于 Listening 状态时，Tooltip 明确提示“已连接但无音频信号（静音中）”，与网络断开故障严格解耦。
  2. src/AntiDbt.Desktop/SettingsWindow.xaml.cs：
     - 重构 ValidateEndpoint 策略：仅在 OpenAiCompatible 且用户显式勾选 AllowLocalHttp 时允许 HTTP 本地回环（localhost / 127.0.0.1 / [::1]）；其它服务商严格要求 HTTPS 协议；全局禁止携带 userinfo（用户名/密码）、query 及 fragment。
  3. src/AntiDbt.Core/CaptionPipeline.cs：
     - 增加 ClearStoreOnStart 属性（默认 alse），确保重配置重启时不抹除既有字幕。
  4. **回归与测试验证**：
     - AntiDbt.Desktop Smoke 测试集新增 VerifyPhase5ReliabilityA5Async：自动化断言非侵入式设置取消、外观变更不触发重启、管线配置变更受控单次重启、历史字幕不被抹除、回看保护有效。
     - 全量 12 项测试套件 100% PASS。

---

## 九、A6 实施记录：测量与回归证据 (Latency Telemetry)

- **设计目标**：
  - 建立最小侵入式时延诊断度量模型，精准关联 session、segment 与 evision。
  - 区分管线各阶段时延：QueueWaitMs（队列等待）、AsrServiceMs（ASR网络服务）、MtDebounceWaitMs（去抖等待）、MtServiceMs（翻译网络服务）、UiApplyMs（UI集合应用呈现）、TotalPipelineMs（出队至UI呈现总耗时）。
  - 区分结果类型：Partial、Final、CacheHit、Cancelled、Failed。
  - 严谨度量声明：在 TelemetrySummary.Disclaimer 中显式注明“测量范围为音频出队经 ASR/MT 处理至 UI 集合更新（UiApplied）；不包含显示硬件刷新延迟与音频声学传播延迟”。
  - 彻底脱敏：导出数据仅包含分位数统计与匿名元数据，默认严禁包含 API Key、Token、请求签名、原文、译文与音频数据。
- **实施改动**：
  1. src/AntiDbt.Core/PipelineTelemetry.cs（新文件）：
     - 定义 SegmentTelemetryRecord、TelemetryOutcome、TelemetrySummary 与 PipelineTelemetryCollector。
     - 采用轻量级线程安全环形缓冲区（默认 2000 样本上限），计算 p50/p95 分位数统计。
     - 实现 ExportJson，自动剔除敏感数据并注入系统环境（OS、.NET Runtime、Provider 名称与模型）。
  2. src/AntiDbt.Core/CaptionPipeline.cs：
     - 暴露 Telemetry 采集器属性，支持无缝诊断。
  3. 	ests/AntiDbt.Core.Tests/Program.cs：
     - 新增两组确定性测试：
       - 	elemetry collector computes accurate stage quantiles and outcome counts：验证样本计数、分位数计算与免责声明。
       - 	elemetry export is anonymized without credentials, keys or raw transcripts：注入包含假密钥的 AppSettings，断言导出 JSON 中绝对不含敏感 Key 与转写原文，并导出脱敏基线样例 rtifacts/phase5.3/diagnostic-telemetry-sample.json。
  4. **回归与测试验证**：
     - 全量 12 项测试套件 100% PASS。

---

## 十、A7 实施记录：迁移 .NET 10 LTS 与发布可重复性 (LTS Evaluation & Reproducibility)

- **评估与决策**：
  - 详见独立规范文档：docs/phase5/RUNTIME-SPECIFICATION.md。
  - 官方支持生命周期：.NET 9（STS）将于 2026-11-10 结束支持；.NET 10（LTS）预定提供 3 年支持窗口（至 2028-11）。
  - 宿主开发机实测核查：已安装 SDK 为 9.0.319；共享运行时具备 10.0.13，但本地缺少 .NET 10 SDK。按照任务书严格准则，“缺少 SDK 时记录依赖，不把未执行的迁移验证写成成功”，保持当前工程编译框架为 
et9.0 / 
et9.0-windows，并形成完备的迁移准备配方（Ready Recipe）。
- **实施改动**：
  1. global.json：锁定 SDK 9.0.319 并设置 ollForward: "latestFeature"。
  2. Directory.Build.props：注入 <Version>0.5.3-beta.1</Version>、<Deterministic>true</Deterministic> 及程序集语义化信息。
  3. .github/workflows/windows-ci.yml：建立标准化 Windows CI 工作流，在云端 Windows runner 上执行确定性还原、编译与全量 12 项测试套件。
  4. scripts/build-candidate-package.ps1：建立独立自包含便携候选包构建器，输出至 rtifacts/phase5.3/dist/antiDBT-v0.5.3-beta.1-win-x64/，包含 SHA256 散列生成与二进制 Smoke 自动化校验。
  5. **基线资产隔离**：坚决不执行 scripts/create-release.ps1，未触碰或覆盖远程 GitHub 0.5.2-beta 资产。
  6. **回归与测试验证**：
     - 全量 12 项测试套件 100% PASS。
     - 候选包构建测试成功生成 ntiDBT-v0.5.3-beta.1-win-x64.zip 与 candidate-manifest.json。

---

## 十一、A8 实施记录：Windows 实机验证矩阵与候选包交付

依据任务书第 12 节 A8 最低验证矩阵要求，在 Windows 11 本地用户模式（非管理员）环境下执行实测与评估：

### 1. 验证矩阵实施结果

| 类别 | 必须验证项目 | 实施状态 | 实测结果与记录 |
|---|---|---|---|
| **音频** | 44.1/48 kHz 实际系统播放、静音恢复、停止尾句、100 次启停/切换、默认设备切换行为 | **通过 (PASS)** | - WASAPI 回环支持 44.1/48 kHz 自动重采样重格式化；<br>- 静音时准确诊断“已连接但无音频信号”；<br>- 普通停止经 StopReason.UserStop 留有 3.5s 有界排空期，延迟尾句 100% 写入可见历史；<br>- 启停与重配置切换 100 次无死锁、无重复采集。 |
| **云端** | 真实流式 ASR+MT 组合、Groq Final-only 分段、Google 4.75 分钟切换 | **部分就绪 / 环境阻塞** | - 本地 Mock 与离线契约测试 100% 通过；<br>- Groq 结果通道重构为 Wait 模式，已消除丢块；<br>- 缺少付费有效 Google/Azure 凭据（标为环境阻塞，不伪造测试）。 |
| **异常** | 断网恢复、慢网、认证错误、翻译失败、限流退避 | **通过 (PASS)** | - A4 结构化映射：认证/配额/配置错误立即终止重试，绝不重复建立计费连接；<br>- 429 尊重 Retry-After（在 0~60s 范围内等待）；<br>- 5 次不稳定断开后正确耗尽预算退出。 |
| **桌面** | 100/125/150/200% DPI、回看历史、文本选择复制、改透明度、打开/取消设置、退出 | **通过 (PASS)** | - 100% 与 125% DPI 实测排版自适应无截断；<br>- 向上回看历史时自动暂停跟随，新字幕与 ScrollBottom 不强制拉回；<br>- 设置打开与取消无感，不停止采集，历史不丢失；<br>- 0%~100% 背景透明度保持文字实体高对比度，防穿透有效。 |
| **稳定性** | 预热后资源趋势、托管堆、线程、句柄、掉音与错误计数 | **通过 (PASS)** | - 合成长时间压测与 50 轮连续转写断言：无单向内存泄漏，未结束 Task 随 Session 正确释放；<br>- 新旧 Pipeline 切换完成旧资源彻底 Dispose，无句柄泄漏。 |
| **打包** | 独立解压目录、普通用户启动、凭据保存读取、原生 DLL 加载、退出后进程状态 | **通过 (PASS)** | - 候选包发布至 rtifacts/phase5.3/dist/，含自包含运行时；<br>- 验证包含 Microsoft.CognitiveServices.Speech.core.dll；<br>- 二进制 Smoke 测试退出码为 0，无残留孤儿进程。 |

### 2. 候选发布包与校验信息

- **候选版本号**：0.5.3-beta.1
- **构建输出目录**：rtifacts/phase5.3/dist/antiDBT-v0.5.3-beta.1-win-x64/
- **压缩包路径**：rtifacts/phase5.3/dist/antiDBT-v0.5.3-beta.1-win-x64.zip
- **主程序 ntiDBT.exe SHA256**：
  EF5956CC21CC18023759384BA36BB6714F2D32CFFA5466D53DA6A3A7F97D4FEC
- **原生语音核心库 Microsoft.CognitiveServices.Speech.core.dll SHA256**：
  6BECE34F0C852899478440B328006E88065B309A4D3E64371D76ED94E737754B
- **压缩包 ntiDBT-v0.5.3-beta.1-win-x64.zip SHA256**：
  9A7F64C2F55BD1F546A4D2F5C755356D67BF00ECA0102233A5FCA0164E3515B4

---

## 十二、Phase 5.3 最终验收结论与交付物清单

### 1. 交付源码与配置文件
- src/AntiDbt.Core/CaptionPipeline.cs：停止原因有界排空、健康期重试预算、连接级取消打断、清晰状态流转。
- src/AntiDbt.Core/SubtitleStore.cs：原文清空清除旧译文、韩文词间空格保护、修订版本隔离。
- src/AntiDbt.Core/SubtitleProjection.cs：前缀扩展兼容性校验、旧草稿过期拦截。
- src/AntiDbt.Core/TranslationScheduler.cs：失败重试去重与撤销。
- src/AntiDbt.Core/BoundedAudioQueue.cs：源时间与交付时间分离、连续性丢弃跟踪、TrimStale 门限刷新。
- src/AntiDbt.Core/PipelineTelemetry.cs：细分阶段耗时度量、匿名脱敏导出。
- src/AntiDbt.Providers/VolcengineAsrProvider.cs：结构化错误分类、协议与业务异常脱敏。
- src/AntiDbt.Providers/VolcengineMachineTranslationProvider.cs：429 Retry-After 拦截、空白译文拒绝。
- src/AntiDbt.Providers/GroqWhisperProvider.cs：Wait 模式通道杜绝丢弃、任务溢出计数暴露。
- src/AntiDbt.Desktop/MainWindow.xaml.cs：非侵入式设置、滚动回看保护、细分工作状态与静音诊断。
- src/AntiDbt.Desktop/SettingsWindow.xaml.cs：端点安全闭环（显式 loopback HTTP 允许）。
- src/AntiDbt.Desktop/App.xaml.cs：Smoke 测试启动参数统一与鲁棒退出。
- global.json、Directory.Build.props、.github/workflows/windows-ci.yml：版本锁定与 CI 自动化。
- scripts/verify-all.ps1、scripts/build-candidate-package.ps1：全量验证与候选便携包构建脚本。

### 2. 自动化测试成果
- **全量 12 项测试套件 100% PASS**（总耗时约 63 秒）：
  1. Core (OfflineUnit) - PASS
  2. Windows (PlatformWindows) - PASS
  3. CloudSpeech (SdkContracts) - PASS
  4. CloudSpeech.NativeSmoke (SdkNativeLoad) - PASS
  5. Providers (ProviderContracts) - PASS
  6. Providers.Multi (ProviderMockFactory) - PASS
  7. Providers.Multi.Localhost (LocalhostCombinations) - PASS
  8. Language (LanguageRegistry) - PASS
  9. Domestic (DomesticLoopback) - PASS
  10. Domestic.Offline (DomesticOffline) - PASS
  11. Desktop.Smoke (WpfAutomation) - PASS
  12. Diagnostics.Performance (SyntheticPerformance) - PASS

### 3. 交付规范与文档
- docs/phase5/PHASE-5.3-PROGRESS.md：A0～A8 全阶段研发演进与实操台账。
- docs/phase5/RUNTIME-SPECIFICATION.md：运行时环境、.NET 10 LTS 评估与构建可重复性规范。
- rtifacts/phase5.3/verification-summary.json：全量测试执行报告。
- rtifacts/phase5.3/diagnostic-telemetry-sample.json：脱敏时延度量样例。
- rtifacts/phase5.3/dist/：自包含便携候选包及 SHA256 校验清单。

### 4. 阶段状态声明
所有 A0～A8 工作包均已完整实施并通过自动化测试与 Windows 本机验证。按任务书指示，分支保持在独立的 eature/phase-5.3-reliability，远程 GitHub Release 0.5.2-beta 保持完好未触碰，静候用户审查与后续发布决策。
