# Phase 5.5.3：智能分句、字幕防吞与连续翻译

日期：2026-10-18（Asia/Shanghai）。目录：`D:\Desktop\AntiDBT-Gemini\antiDBT\prototype`。

## 交付结果与基线

本地流式 ASR 已加入按音频时间推进的分句管理；Final 与下一片段 Partial 原子交接；已确认原文保留；译文修订继续使用 Phase 5.5.2 的显示回退。长译文返回时，自动跟随状态为原文和译文分别保留可见空间，回看历史时恢复完整内容。

- 基线 HEAD：`1556dfdbd839baec0341351e83934f9a7450700e`；分支：`feature/phase5.4-realtime-subtitles`。
- 开始时工作树已有 Phase 5.5.2 未提交成果，沿用并增量修改相关文件；未 reset、未创建提交、未上传 Release。初始文件清单：`artifacts/phase5.5.3/baseline-files.txt`；备份：`baseline-5.5.2-uncommitted.zip` 与 `baseline-source/`。后者含早期 Smoke 接入，不能作为完全未修改的基线快照。
- SDK：10.0.402；自动化框架 net10.0；桌面 net10.0-windows；便携包 win-x64、自包含。
- 最终全量验证：12/12 套件通过，全部退出码 0，耗时 92.921 秒。Core 75 项；源码 WPF Smoke 215 项；便携包独立 WPF Smoke 216 项。Smoke 数量包含按实际渲染回调生成的检查，运行间可变化。

## 确认根因与修复

1. **长片段没有主动结束机制。** 原 Provider 主要等待 native endpoint/输入结束，原生静音规则默认 1.2 秒、连续语音规则 20 秒，没有稳定边界分句管理。加入 `StreamingSentenceManager`，为每段生成新逻辑 ID，独立递增 Revision，不等待翻译完成再处理后续音频。
2. **长译文让原文滚出可见窗口。** 修改前真实 WPF 几何复现中，Store 仍保存 2210 字符原文和 2410 字符译文，但 Compact 的原文末字坐标为 -918.8px（视口 93.2px），Expanded 为 -654px。证据：`artifacts/phase5.5.3/red-desktop/sentence-visibility.json`。此次确认了裁剪/滚动缺陷，不能据此声称已复现原文和译文同时从 Store 删除。
3. **已确认片段可被同 ID 的后续 Partial 覆盖或删除。** 新回归先失败于 `a next Partial overwrote a confirmed segment`，见 `red-core.log`。Store 现在拒绝 Final 之后的 Partial 和 Provider 删除；仍允许混合 ASR 对同片段提交更高 Revision 的 Final 校正。
4. **混合 ASR 在本地分句时消费整个 PCM 缓冲。** 缓冲可能已有下一句音频。现在依据词边界对应的音频时间只取本句 PCM，后缀继续保留。本地 Final 和下一 Partial 先以完整批次发布，云端改写仅更新对应 ID 的更高 Final Revision；相同文本不重复发布。
5. **冷启动时先采集、后初始化模型。** 首轮现场测试出现 52 个队列过期掉块。新增门控回归确认初始化尚未完成时已启动采集；修复为 ASR 连接完成后启动捕获，避免初始化占满既有两秒队列。正常运行的过载边界未改动。
6. **首次简单切分会截断英文词。** 真实样本出现 `squa` / `Lid`。改为稳定词前缀提交、整个尾词保留，并使用原生 token 时间关联音频边界；修正后的真实样本将 `squalid` 保留完整。未添加猜词、过滤或删除识别结果的规则。

WPF 保持原字幕控件和视觉样式，TextChanged、渲染及窗口尺寸变化后更新最新段的内部滚动。只有自动跟随时限制最新段两条文本的视口高度并滚到末尾；用户回看时解除限制，不强制回到底部。Store 始终保留完整文本。

## 分句策略与配置

优先级：原生 Endpoint/输入结束 → 静音 → 稳定标点 → 建议时长/字符数达到后的稳定边界 → 最大时长。以实际输入采样数计时，每 100ms 音频推进一次；最大时长判断存在一个处理块的粒度。

| 配置键 | 默认值 | 含义 |
| --- | ---: | --- |
| `sentenceSilenceMs` | 500 | 静音分句阈值 |
| `sentencePreferredSeconds` | 5 | 建议分句时长 |
| `sentenceMaxSeconds` | 9 | 最大连续分句目标 |
| `sentenceMaxChars` | 120 | 辅助字符边界，仍寻找安全词边界 |
| `sentenceStabilityMs` | 300 | 稳定前缀观察窗口 |
| `sentenceVadThreshold` | 0.008 | 20ms PCM 窗口 RMS 静音判定 |

以上字段同时用于本地 sherpa 和混合 ASR。原生 rule2 默认改为 0.5 秒、rule3 默认 9 秒；配置进行有限数值校验，并约束建议时长不超过最大时长。字符数为辅助条件，不直接截断文本。

- Transducer 使用原生 Reset 保留上下文缓存、未读样本和特征；不销毁重建流、不丢弃或重复播放 PCM。尾词的已解码部分随下一段继续显示/识别。
- Paraformer 在主动逻辑分句时继续使用累计解码器，并记录已提交前缀；仅原生 endpoint 时重置，避免清空其 CIF/cache。此次未运行 Paraformer 真实模型验证。
- 通过很小的 `TimedSherpaRecognizer` C ABI 封装读取当前 NuGet C# 结果未暴露的 `start_time`，结合 token timestamps 取得绝对词边界。SafeHandle 管理原生资源，无新增依赖。
- EOF 的 600ms 零填充仅供模型 lookahead，不计入捕获/处理的有效音频账本。
- 同批旧 Partial → Final + 新尾段 Partial，音频覆盖连续且可靠关联时共享显示回退；新子段准确译文全部就绪后整体替换，避免旧整句译文与新尾句译文重复。

原生行为依据 1.13.8 官方源码核对：[Transducer Reset](https://raw.githubusercontent.com/k2-fsa/sherpa-onnx/v1.13.8/sherpa-onnx/csrc/online-recognizer-transducer-impl.h)、[Paraformer Reset](https://raw.githubusercontent.com/k2-fsa/sherpa-onnx/v1.13.8/sherpa-onnx/csrc/online-recognizer-paraformer-impl.h)、[C API 结果布局](https://raw.githubusercontent.com/k2-fsa/sherpa-onnx/v1.13.8/sherpa-onnx/c-api/c-api.h)。

## 连续翻译与诊断

保留 Phase 5.5.2 的准确译文、兼容草稿、最后有效可见译文分离。旧显示回退不算当前 Revision 完成，不参与新的翻译缓存；取消、超时、失败和重试不清空它。Scheduler 跳过已完成的准确 Revision；过期请求不能覆盖新版本或其他片段。Session、语言切换、显式清空及拆分/合并边界由现有 Sticky 回归覆盖。

`CaptionDiagnostics` 记录 ASR 空输出、分句、Store 更新/删除/清空/拒绝、MT 接受/错误、Projection 文本长度及 WPF 跟随/滚动/可见性。环形记录最多 2048 条，仅元数据，不写转写正文或密钥。设置 `ANTIDBT_CAPTION_TRACE=1` 可将诊断写入程序目录下 `data/diagnostics/captions.jsonl`，单文件约 2MB 后轮换；写日志失败不会中断字幕。Smoke 自动导出 `caption-trace.json`。

## 验证与前后对比

最终执行：`scripts/verify-all.ps1 -SkipRestore -ArtifactsDir artifacts/phase5.5.3/verified-final`。

| 套件 | 结果 |
| --- | --- |
| Core / Windows / CloudSpeech / CloudSpeech.NativeSmoke | 全部 PASS，退出码 0 |
| Providers / Providers.Multi / Providers.Multi.Localhost | 全部 PASS，退出码 0 |
| Language / Domestic / Domestic.Offline | 全部 PASS，退出码 0 |
| Desktop.Smoke / Diagnostics.Performance | 全部 PASS，退出码 0 |

总表：`artifacts/phase5.5.3/verified-final/verification-summary.json`。新增回归覆盖 30 秒/120 秒快速连续假设流、稳定/无停顿/无标点边界、Final 后下一段、尾部保留、乱序 MT、重复 Revision、启动门控、拆分显示组及混合 PCM 所有权。既有 Sticky 回归继续覆盖慢译文、失败/取消/超时、Session/语言及清空。

| 验证 | 实际结果 | 证据 |
| --- | --- | --- |
| 修改前长 MT 可见性 | Store 有数据，原文末字在 Compact/Expanded 视口上方 | `red-desktop/sentence-visibility.json` |
| 修改后长 MT 可见性 | 两种模式原文和译文末字均可见；手动历史位置保留 | `verified-final/desktop-smoke/sentence-visibility.json` |
| 实际 WPF 渲染回调 | 源码 51 个样本、便携包 52 个样本，长 MT 返回期间原文可见检查全通过 | 对应 `sentence-render-frames.json` |
| 原配置模拟、真实模型 30 秒 | 2 个 Final，最长 20.275 秒 | `model/baseline-30s.json` |
| 新配置、真实模型 30 秒 | 11 个 Final，最长 4.48 秒；480000 样本接收=处理 | `model-final/segmented-30s.json` |
| 新配置、真实模型 120 秒 | 42 个 Final，最长 4.62 秒；1920000 样本接收=处理 | `model-final/segmented-120s.json` |
| 强制边界、真实模型 120 秒 | 15 个 Final，最长 8.80 秒；1920000 样本接收=处理 | `model-forced/segmented-120s.json` |
| 授权现场视频 + DeepL | 59.46 秒有效采集，23 Final / 96 Partial，最长 4.54 秒；掉块 0、已显示原文空白采样 0、Final 覆盖 0；23 段全部取得对应准确译文 | `live-video-verified/live-video.json` |

表中相对证据均位于 `artifacts/phase5.5.3/`。真实模型离线测试使用捆绑的人声 WAV 重复构成 30/120 秒并加速送入，不能当作连续两分钟新自然语音或两分钟墙钟实播。旧配置对比是在新测试驱动中模拟旧阈值，不是旧版本整机 A/B。

现场复核使用真实 WASAPI、真实英语 Zipformer 和用户已配置 DeepL，68 次 MT 请求、51 次成功，其余修订请求正常取消/抢占，最终错误 0。951360 个有效采样接收=处理。71.54 秒总墙钟包含模型初始化和结束后的翻译排空，不等于捕获时长。私有内存初始 11.46MB、峰值 129.22MB、结束 55.47MB，仅为短时观测，不能据此证明长期无泄漏。

现场测试的授权与失误：自动审批最初因未明确获准向 DeepL 披露视频转写而拒绝，用户随后明确授权 120 秒。首轮还发现计时从初始化后开始，实际捕获超过原定上限；该测试不能作为时限合规的验收。已告知用户并修复为采集启动时独立截止及采样上限。用户追加授权最多 60 秒后，完成上述 59.46 秒复核。首轮失败日志保留在 `live-video/live-video.json`，52 个启动掉块没有隐去。

现场成功测试发生于最后一次 Store 拆分显示组和混合 Provider 批次细化之前；这些后续改动由最终全量回归覆盖。现场测试没有驱动实际可见的 WPF 窗口，真实服务和 WPF 可见性证据分别报告。

## 文件变更范围

本阶段新增：

- `src/AntiDbt.Core/StreamingSentenceManager.cs`、`CaptionDiagnostics.cs`。
- `src/AntiDbt.Providers/TimedSherpaRecognizer.cs`。
- `src/AntiDbt.Desktop/SentenceDesktopSmoke.cs`。
- `tests/AntiDbt.Core.Tests/SentenceContinuityTests.cs`、`SentencePolicyTests.cs`。
- `tests/AntiDbt.Providers.Tests/HybridSentenceTests.cs`。
- `tests/AntiDbt.Sticky.Live/SentenceFile.cs`、`SentenceLive.cs`。

本阶段扩展：Sherpa/Hybrid Provider、ProviderDescriptors、CaptionPipeline、SubtitleStore、SubtitleProjection、TranslationScheduler、Desktop App/MainWindow/DesktopSmoke、Core/Provider 测试入口、已有 Sticky 测试夹具、Live 入口，以及本报告和 `docs/phase5/PROGRESS.md`。

工作树中的 `Contracts.cs`、Volcengine 的 BatchSize、Phase4DesktopSmoke 及 `PHASE-5.5.2-REPORT.*` 等已有未提交内容属于前一阶段，保留其成果。对已存在文件的本阶段修改围绕本任务进行，没有将整个 Git diff 误当作本阶段独有成果。

## 便携包与 SHA256

- ZIP：`artifacts/phase5.5.3-dist/antiDBT-0.5.5.3-win-x64.zip`，112504396 字节。
- ZIP SHA256：`BF2ADA4DFB1EC5B08D890C144B13B3F0B48DB86C1E6FDB4B94E4454851B56BE7`。
- EXE SHA256：`C2C85DB92B03E1679E0266A9775283A849227D6B0F6F7E6CDC5AC4280CAC6095`。
- 含自包含运行时、Sherpa/ONNX 原生 DLL、英语 int8 模型、README 和许可证；不含用户设置、密钥及现场字幕。版本和基线见包内 `release-manifest.json`。
- 发布日志：`artifacts/phase5.5.3/publish.log`。实际启动包内 EXE 执行 WPF Smoke，退出码 0、216 项通过，结果在 `artifacts/phase5.5.3/package-smoke/`。
- ZIP 内 EXE 哈希与发布 EXE、必需原生 DLL/模型条目及无用户 data 条目再次核对，见 `artifacts/phase5.5.3/package-integrity.json`。

## 未验证边界

未执行多小时视频 soak、人工逐词 WER/漏词标注、Paraformer 实模、所有语言/硬件设备的真实服务组合或物理多 DPI 显示器验证；DPI Smoke 仍是明确标注的模拟缩放。此次采样账本一致、历史保留及测试词流守恒，不能推导出模型对任意语音零识别错误。既有实际过载/重连的有限队列策略仍存在，不能承诺任意负载下绝不掉音频。未新增云端费用授权范围外的测试。
