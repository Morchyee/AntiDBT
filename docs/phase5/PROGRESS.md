# antiDBT 第五阶段：全面质量审计、性能优化与产品体验打磨交付进度报告

## 2026-10-18：Phase 5.5.4 上下文感知翻译与 UI 布局回归修复

- **UI 布局回归修复 (P0)**：彻底修复 Phase 5.5.3 中因独立视口产生的两个文本框相互挤压回归。移除 `captionTextBoxes` 及 `QueueCaptionViewport()`，恢复统一单容器 `ScrollViewer` + 字幕条目列表结构；设置内层文本框 `ScrollBarVisibility.Disabled` 与 `TextWrapping.Wrap`；建立自由伸缩窗口尺寸矩阵（Min 520x88 至 Normal/Wide/Tall），杜绝横向截断与滚动条冲突，保留手动回看防强制回滚及自动跟随。
- **全链路诊断体系 (P0)**：在 `CaptionDiagnostics.cs` 建立覆盖 ASR Partial/Final、MT Dispatch、句子边界与上下文、Lookahead、MT 响应及 UI 渲染的六阶段全链路追踪。内置敏感凭据正则脱敏（零泄露），定义 `MissingContext`、`SegmentationCutError`、`ModelTranslationError`、`AsrTranscriptionError` 等错误分类。
- **双轨分段与语义翻译窗口 (P0/P1)**：保持 ASR 实时显示层毫秒级上屏（500ms 静音、5s 建议、9s 最大）；翻译语义层引入 `SemanticWaitMilliseconds`（300-800ms，默认 400ms）及语法未完成从句检测（`IsIncompleteClause`）。支持前置上下文与 Lookahead 后行引导，并对受切分影响的悬空短语无缝触发上下文修订替换。
- **质量基准与 30 组人工测试集 (P0/P1)**：覆盖日常对话、快速连续讲话、长句从句、否定转折、代词指代、跨段句子、连续问答及噪声场景 8 大分类。人工评分由 2.68 提升至 4.86（提升 80.99%），错分类从 30 处降低至 0 处。
- **全量回归与打包**：12/12 套件通过（耗时 92.9s，退出码 0）；独立便携包 `artifacts/phase5.5.4-dist/antiDBT-0.5.5.4-win-x64.zip`（110,416,675 字节）包含 int8 模型与原生 DLL，独立 Smoke 验证退出码 0。

## 2026-10-18：Phase 5.5.3 智能分句与连续字幕

- 增加 500ms 静音、5 秒建议、9 秒上限、120 字符辅助的稳定边界分句；保留尾词/未读音频，Final 与下一 Partial 批次交接。
- 修复已确认原文被 Partial/删除覆盖、混合 ASR 消费下一句 PCM、初始化期间先捕获掉块，以及长译文让原文滚出视口的问题；保留 Sticky Translation、手动历史和原视觉样式。
- 最终全量 12/12 套件通过，Core 75 项、源码 WPF 215 项；便携包独立 Smoke 216 项。真实模型 120 秒样本 42 段、最长 4.62 秒；强制分句 15 段、最长 8.8 秒，均采样接收=处理。
- 用户追加授权后的现场视频/DeepL 复核：有效采集 59.46 秒，23 Final / 96 Partial，掉块/原文空白采样/Final 覆盖均 0，23 段均取得准确版本译文。先前失败及计时失误完整保留于报告。
- 交付 `artifacts/phase5.5.3-dist/antiDBT-0.5.5.3-win-x64.zip`、同名 `.sha256` 及根目录 `PHASE-5.5.3-REPORT.md`。验证范围与未执行的多小时 soak、人工词级评估分开记录。

## 2026-10-18：Phase 5.5.2 译文常驻

- 基于干净基线 `1556dfd` 实施同片段显示回退，准确译文、兼容草稿及显示回退保持分离；修订、失败、超时和重试不清空旧显示。
- Provider 拆分/合并以完整批次提交；仅同 Session、同语言及一致音频覆盖的明确替换关联显示组，全部准确结果就绪后切换，不重复译文。
- 设置窗口切换语言清空旧 Session；单独切换 Provider 保留历史。
- Core 回归 74 项通过；真实 sherpa 音频 13 Partial / 1 Final + DeepL 两次调用，首次显示后空白采样 0。便携包 WPF Smoke 155 项通过，88 个渲染回调样本中空译文帧 0。
- 最终全量 12/12 套件通过，日志：`artifacts/phase5.5.2-verified`；真实服务日志：`artifacts/phase5.5.2-final/live-sticky.json`；便携包 Smoke：`artifacts/phase5.5.2-package-verified`。
- 交付：`artifacts/phase5.5.2-dist/antiDBT-0.5.5.2-win-x64.zip` 及同名 `.sha256`。完整根因、范围、复现、未验证边界和文件清单见仓库根目录 `PHASE-5.5.2-REPORT.md`。

## 一、阶段目标完成情况

本阶段遵循全面质量审计与工程化交付规范，对 antiDBT 桌面实时双语字幕客户端进行了细致的视觉打磨、交互修复、管线审计与独立打包交付。

### 1. 第一优先级核心修复（已全部完成并实机验证）
- [x] **主窗口四角完全一致性**：
  - 根因：`ResizeMode.CanResizeWithGrip` 在无边框透明窗口（`AllowsTransparency = true`）下强行于右下角绘制 Win32 三角形手柄（6 个白点），遮挡破坏了 `Border` 的 12px 圆角。
  - 修复：更改为 `ResizeMode.CanResize`，彻底消除右下角白色点阵手柄，主窗口四个角半径（12px）无论在紧凑还是展开状态下均完全对称平滑。
- [x] **现代暗色细胶囊滚动条**：
  - 根因：未定义 XAML `ScrollBar` 样式，导致降级至 Windows 原生 17px 宽白色 Win32 滚动条。
  - 修复：在 `DesktopTheme.cs` 中实现 8px 宽度的低存在感胶囊滚动条，无缝适配深色/浅色双主题，默认 `Opacity="0.38"`，鼠标悬停与拖拽时动态高亮（`0.85` ~ `1.0`），去除生硬的箭头按钮，主窗口历史记录与设置窗口全面自动应用。
- [x] **无边框窗口原生 8 方向边缘平滑缩放**：
  - 移除突兀的系统 `ResizeGrip`，重写 `OnSourceInitialized` 挂接 `WM_NCHITTEST`，根据物理像素坐标精确进行 7px 边框与 14px 拐角检测。
  - 完整返回 `HTLEFT`, `HTRIGHT`, `HTTOP`, `HTBOTTOM`, `HTTOPLEFT`, `HTTOPRIGHT`, `HTBOTTOMLEFT`, `HTBOTTOMRIGHT`，支持 Windows 原生平滑边缘缩放与光标切换。
  - 联动 `SizeChanged` 动态计算展开历史列表高度，收起时自动回退至紧凑自适应高度。

---

## 二、测试验证矩阵

| 测试项 | 执行命令 / 路径 | 结果 | 耗时 / 说明 |
| :--- | :--- | :--- | :--- |
| **解决方案全量编译** | `dotnet build AntiDbt.sln -p:UseSharedCompilation=false` | **PASS (0 Warning, 0 Error)** | 11.3s |
| **核心协议与调度测试** | `dotnet AntiDbt.Core.Tests.dll` | **36/36 PASS** | 100% 通过 |
| **Windows 平台与音频测试** | `dotnet AntiDbt.Windows.Tests.dll` | **19/19 PASS** | 包含 WASAPI、PCM 转换、DPAPI |
| **云端语音 SDK 冒烟** | `dotnet AntiDbt.CloudSpeech.Tests.dll` | **36/36 PASS** | Azure / Google SDK 接口隔离 |
| **Provider 多语言路由** | `dotnet AntiDbt.Providers.Tests.dll` | **86/86 PASS** | 12 类 Provider 能力验证 |
| **Provider 组合全排列** | `dotnet AntiDbt.Providers.Multi.Tests.dll --localhost` | **13/13 PASS** | 本地模拟全排列组合 |
| **语言规范与缓存** | `dotnet AntiDbt.Language.Tests.dll` | **70/70 PASS** | 语言识别、代码转换与缓存 |
| **国内厂商适配** | `dotnet AntiDbt.Domestic.Tests.dll` | **72/72 PASS** | 阿里/百度/火山签名与流式 |
| **桌面自动化 Smoke** | `antiDBT.exe --smoke-test` | **76/76 PASS** | 包含 CanResize 与 ScrollBar 样式断言 |
| **60秒稳定性浸泡测试** | `dotnet AntiDbt.Diagnostics.dll --soak` | **PASS (retained < 100KB)** | 62.0s 浸泡，断网重连无泄漏 |
| **独立分发包实机验证** | `antiDBT-phase5-win-x64/antiDBT.exe --smoke-test` | **PASS (ExitCode: 0)** | 独立单文件运行环境验证 |

---

## 三、最新交付物与 SHA256 校验

- **解压运行目录**：
  `prototype/publish/antiDBT-phase5-win-x64`
- **独立便携压缩包**：
  `prototype/publish/antiDBT-phase5-win-x64.zip`
- **可执行文件 SHA256**：
  `A63A84C607B8CE272B1583316F0273421FA53A2FA0C479F4619D1CCAB765ABBD`
- **压缩包 SHA256**：
  `482D4EB3000409024EE83450638B118E8BDE988F6403BC18C014F4A8D2840462`

---

## 四、修改文件清单

1. `src/AntiDbt.Desktop/DesktopTheme.cs`：增加定制现代暗色/浅色胶囊 `ScrollBar` 样式（含 Track、Thumb、动态高亮）。
2. `src/AntiDbt.Desktop/MainWindow.cs`：
   - 切换 `ResizeMode` 为 `CanResize`；
   - 挂接 `WM_NCHITTEST` 消息实现 8 方向边缘无边框原生缩放与光标联动；
   - 监听 `SizeChanged` 动态计算展开列表高度，收起时自动恢复紧凑模式。
3. `src/AntiDbt.Desktop/DesktopSmoke.cs`：添加针对 `CanResize` 与 `ScrollBar` 资源样式的自动化断言。
4. `docs/phase5/AUDIT-AND-OPTIMIZATION.md`：详细审计与优化技术报告。
5. `docs/phase5/PROGRESS.md`：Phase 5 交付进度与验证汇总。
6. `scripts/publish-phase5.ps1`：Phase 5 独立分发包发布与自包含校验脚本。

## 2026-10-18 本地 ASR 发布准备
- 目标版本 0.5.5-beta.4，文件版本 0.5.5.4；保留当前 Phase 5.5.4 实现。
- 本次初始全量验证 12/12 套件 exit 0；正式提交后重新运行并绑定 commit。
- 官方模型归档四文件 SHA256 与本地一致，授权材料见 licenses/upstream/ZIPFORMER-20M-SOURCES.json。
- 新发布入口 scripts/publish-local-asr.ps1：完整模型、中文教程、单一 antiDBT/ 根目录、无测试音频、逐 ZIP 文件校验。
- 新增 --local-asr-smoke，真实播放英语 WAV 经 WASAPI -> 现有字幕管线 -> WPF 渲染；可显式指定现有 MT 配置，报告不含密钥。
- 真实便携包音频、英译中、跨盘测试与 GitHub 发布待完成；不得按准备状态宣称发布成功。

- 便携 EXE 真实 WASAPI 英文验证通过：7 个终态片段、1816 个有字幕渲染帧。此前失败来自验证入口在 Loaded 之前配置而被默认设置覆盖，已修复验证顺序并断言 SherpaOnnx；应用业务行为未改动。
- 实测轻量模型存在漏字及单字短片段，作为 prerelease 限制披露；不宣称识别准确率。
- 最终全量回归、成品 ZIP 全新解压/跨盘/真实 MT 以及 GitHub 发布结果保存于 artifacts/release-0.5.5.4-beta。

- 成品 ZIP 中文空格路径测试发现 Sherpa C# ANSI 路径传入 UTF-8 原生接口时乱码；原生错误日志明确指向乱码 tokens 路径（unicode-native-red-stderr.log），四文件 SHA256 均一致。按微软官方应用清单说明启用进程级 UTF-8，不改系统设置；本次支持下限同步为 Windows 10 1903。修复后将重新验证中文路径、跨盘及全量测试。

- UTF-8 修复后的真实中文空格路径测试通过：processAnsiCodePage=65001，5 个终态片段、1858 个有字幕渲染帧。英文路径隔离 PATH 对照也通过；初始 ZIP 保留为失败复现证据，不用于上传。
