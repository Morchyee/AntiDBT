# Phase 5.5.2：译文常驻与无缝替换

日期：2026-10-18（Asia/Shanghai）。实施目录：`D:\Desktop\AntiDBT-Gemini\antiDBT\prototype`。

## 基线与范围

- 基于实际当前稳定工作树：`1556dfdbd839baec0341351e83934f9a7450700e`，分支 `feature/phase5.4-realtime-subtitles`；开始时 `git status --short` 为空。
- 未覆盖用户未提交修改，未创建提交、上传或发布远程 Release。
- 保留 ASR 实时 Partial、翻译防抖/合并/上下文/缓存、UI 样式和滚动策略。不在生产流程延迟原文或翻译。

## 已验证根因

1. `SubtitleStore.ApplySource` 在原文变化时清空准确译文；只有通过语义/前缀兼容性、5 秒或版本差限制的草稿才能保留。非前缀修订因此立即丢失全部可见译文。
2. `SubtitleProjection.Join` 再次检查草稿的 5 秒期限，导致等待较久的译文即使未被新结果替换也变空。
3. `UpdateTranslation` 被翻译错误路径调用时，同时清空准确译文和暂定译文；失败提示与显示内容没有分离。
4. 火山 ASR 的拆分/合并逐条输出删除和新建，原管线逐条提交，可能让 UI 看到仅删除完成的中间状态。
5. Scheduler 内层超时捕获原先只记录取消，未设置可重试超时错误。

`QueueRender` 和 `SubtitleParagraph.UpdateFrom` 原本就在原位更新；它们接收到的投影已经为空。因此修复发生在状态流，而非通过 UI 忽略空值掩盖问题。

修改前的自动复现记录：`artifacts/sticky-red.log`，新增回归失败为 `compact blank or incorrect fallback`（其余 73 项通过）。修改后，同样的非前缀修订在 Compact、Expanded 和 UI 属性更新中持续显示旧译文。

## 实现

- `TranslationText` 仍代表当前原文的准确译文，`ProvisionalTranslationText` 仍受兼容性校验；新增 `LastVisibleTranslation` 只承担显示回退，投影统一读取 `DisplayTranslation`。
- 修订使准确译文失效时保留显示回退；失败仅更新错误状态；重试清理错误时不清空文本。成功结果通过 Session、Segment、Revision 校验后，在同一锁内直接替换。
- 显示回退不参与 MT 缓存，不作为当前版本完成或成功的依据。保留翻译版本水位，防止更旧的兼容草稿覆盖较新显示回退。空白成功结果被拒绝，不写缓存。
- Provider 通过首条事件的 `BatchSize` 标记完整拆分/合并；管线收齐该批次后，Store 在同一锁内应用，最后只通知一次。普通 Partial 的 `BatchSize=1`，立即处理。
- 仅在同一 Session、同语言、明确删除并新建、非 Final 旧片段且音频起止覆盖完全一致时关联回退。新片段共享显示组：全部准确译文就绪前只显示一次旧组译文，全部就绪后整体切换，避免旧整句与新子句重复。
- 不猜测无关联的删除/新建身份；音频覆盖变化、语言变化或无法可靠关联的片段不会继承旧译文。显式删除、清空、新 Session 不把回退传给新片段。
- 设置窗口修改源/目标语言会开始清空的 Session；只修改 Provider 或外观继续遵守原历史保留策略。顶部语言选择原有 Session 清空机制保持有效。

## 自动回归覆盖

`tests/AntiDbt.Core.Tests/StickyTranslationTests.cs` 通过真实 Store、Projection、Scheduler 和 Pipeline 验证以下场景：

| 场景 | 断言 |
|---|---|
| Partial A → MT A → Partial B → MT B | 更新期间旧译文非空，成功后直接替换 |
| Partial → Final、非前缀修订 | 准确译文失效但 Compact/Expanded 继续显示回退 |
| 取消、超时、失败、重试 | 旧译文保留；超时错误可重试；迟到结果不进入显示 |
| 过期结果、较旧兼容草稿 | 不覆盖较新译文或显示水位 |
| 高频连续修订 | 同一片段 100 次修订，Changed 回调均检查可见文本 |
| 多句、新 Session、同 ID 重用 | 新片段无旧译文继承，保留原历史策略 |
| Split / merge | 单批通知、无空中间态、无重复译文，退休片段拒绝迟到结果 |
| 批次经过实际 CaptionPipeline | 故意间隔发送删除/新建事件，UI 不观察到未完成批次 |
| 源语言改变、空原文、显式清空 | 回退被移除，不残留幽灵译文 |
| 慢速翻译 | 1.5 秒等待期间每 50ms 检查可见译文，完成后替换；另测 1 秒超时 |

原有语义兼容矩阵仍验证严格兼容规则；其显示断言改为保留回退。原来的 5 秒清空断言改为：显示仍存在，准确译文仍未完成。

## 验证证据

- 最终完整验证命令：`./scripts/verify-all.ps1 -SkipRestore -ArtifactsDir artifacts/phase5.5.2-verified`。
- 统一结果：12/12 套件通过，失败 0，总运行 92.365 秒。`artifacts/phase5.5.2-verified/verification-summary.json` 包含 Core、Windows、CloudSpeech、CloudSpeech.NativeSmoke、Providers、Providers.Multi、Providers.Multi.Localhost、Language、Domestic、Domestic.Offline、Desktop.Smoke、Diagnostics.Performance。
- Core：74 项通过。Provider localhost 组合覆盖 15 组；不把 localhost 夹具当作真实云服务证据。
- 最终便携包 WPF Smoke：155 项通过，`artifacts/phase5.5.2-package-verified/desktop-smoke.json`。
- 包内 WPF 的渲染回调采样：1.5 秒等待并切换 Compact / Expanded，共 88 帧，空译文帧 0；段落实例未更换，译文直接原位替换。记录：`artifacts/phase5.5.2-package-verified/sticky-wpf-frames.json`。
- WPF Smoke 同时检查手动历史滚动、自动跟随、尺寸/外观、失败重试、取消设置、语言切换和只更换 Provider 的历史保留。
- 最终真实服务测试：`artifacts/phase5.5.2-final/live-sticky.json`，结果 `passed`。本地真实 sherpa 模型识别仓库附带 `test_wavs/0.wav`，13 个 Partial、1 个 Final；真实 DeepL 调用 2 次；首次译文出现后空白采样 0（总采样 33 条，包括首次等待期间的空译文）。
- 真实服务测试明确使用真实 ASR 事件的确定性回放（80ms 间隔），第二次真实 MT 请求人为增加 1500ms 延迟；这是连续性压力测试，不是自然实时链路延迟测量。未读取用户音频或在日志中输出凭据。
- `git diff --check` 通过。使用已有 SDK 10.0.402 和已有依赖，没有安装额外工具或新增 NuGet 依赖。

执行中的限制与修正：初次沙箱运行出现 localhost Socket 10013；允许联网的执行环境重跑通过。旧 WPF Smoke 将语言改变与 Provider 改变混在一项“保留历史”测试中，现已明确固定语言基线并分开验证。旧失败记录保留在 `artifacts/phase5.5.2*` 的早期目录，最终验收以 `phase5.5.2-verified` 和 `phase5.5.2-package-verified` 为准。

本次没有执行真人语音的 WASAPI 现场验收、真实 Groq/其他云 ASR、真实火山云拆分/合并、物理多 DPI 或长时间硬件 soak；这些不由夹具、WPF 绑定文本采样或录音样本回放推断为通过。拆分/合并的保证范围是上述可靠关联条件。

## Windows x64 便携包

- ZIP：`artifacts/phase5.5.2-dist/antiDBT-0.5.5.2-win-x64.zip`，112,492,912 字节。
- 解压目录：`artifacts/phase5.5.2-dist/antiDBT-0.5.5.2-win-x64`；启动 `antiDBT.exe`。
- 自包含 .NET 10 Windows x64；FileVersion `0.5.5.2`；包含 `onnxruntime.dll`、`sherpa-onnx-c-api.dll`、三个 ONNX 模型文件、tokens、许可证和说明。用户需自行配置翻译服务，包内无 `data/settings.json` 或用户凭据。
- 已运行包内 EXE 的 WPF Smoke。直接读取 ZIP 内 EXE 并计算 SHA256，与发布 EXE 一致；原生库、模型条目及无用户数据断言通过：`artifacts/phase5.5.2-dist/archive-validation.json`。

SHA256：

```text
B31559AB2B439B58C51D65A4B5E2E01D2023CB9AADE0A4540D669B3FF0D5FC22  antiDBT-0.5.5.2-win-x64.zip
E3921D5A90906983158992C876C028C5A6B595DE5021C9CA471C283C42C51D7E  antiDBT.exe
```

校验文件：`artifacts/phase5.5.2-dist/antiDBT-0.5.5.2-win-x64.sha256`。

## 变更文件

- `src/AntiDbt.Core/SubtitleStore.cs`：显示回退、版本水位、错误保留、原子批次和关联显示组。
- `src/AntiDbt.Core/SubtitleProjection.cs`：统一显示回退、保持关联组连续。
- `src/AntiDbt.Core/TranslationScheduler.cs`：空结果拒绝、超时可重试错误。
- `src/AntiDbt.Core/Contracts.cs`、`CaptionPipeline.cs`：完整 Provider 批次提交。
- `src/AntiDbt.Providers/VolcengineAsrProvider.cs`：标记拆分/合并批次。
- `src/AntiDbt.Desktop/MainWindow.cs`：设置窗口语言切换隔离。
- `src/AntiDbt.Desktop/DesktopSmoke.cs`、`Phase4DesktopSmoke.cs`：连续显示、绑定实例、语言隔离与 Provider 历史保留回归。
- `tests/AntiDbt.Core.Tests/Program.cs`、`StickyTranslationTests.cs`：自动回归及既有断言更新。
- `tests/AntiDbt.Providers.Tests/ReviewCases.cs`：Provider 拆分/合并批次契约。
- `tests/AntiDbt.Sticky.Live/AntiDbt.Sticky.Live.csproj`、`Program.cs`：显式执行的真实 ASR + MT 连续性验收；不进入默认离线测试，不自动消费服务。
- 本报告与 `docs/phase5/PROGRESS.md`：交付和验证记录。

真实服务复跑（会调用配置中的 MT 服务）：先构建 `tests/AntiDbt.Sticky.Live`，再执行其 DLL，参数为已有配置目录及输出 JSON 路径。测试只发送仓库 WAV 的识别结果；不要把密钥作为命令行参数。
