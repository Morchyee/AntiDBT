# antiDBT 第五阶段：全面质量审计、性能优化与产品体验打磨报告

## 一、审计与优化目标

antiDBT 在第四阶段已完成多语言选择、Provider 联动、现代极简悬浮窗 UI 与基础端到端字幕管线。第五阶段的核心目标是将软件从“功能可用”提升到“稳定、精致、低延迟、适合长期日常使用”的状态。

针对实际运行和截图审计，第一轮重点完成了：
1. **主窗口四角圆角完全一致性**：彻底消除右下角因 ResizeGrip 导致的直角与三角形白点残损，保证四个角半径严格保持 12px 圆角。
2. **滚动条现代化重构**：移除古老突兀的 Win32 白色 17px 宽滚动条，定制现代细胶囊滚动条（8px 宽、透明轨道、低存在感圆角 Thumb、悬浮与拖拽动态反馈、无生硬箭头按钮），并适配深色/浅色双主题与 DPI 缩放。
3. **原生 8 方向边缘无缝缩放**：移除系统 ResizeGrip，改用 `WM_NCHITTEST` 边缘与四角命中检测（7px 边缘 + 14px 拐角），支持 Windows 原生 8 方向拖拽缩放与系统指针切换，兼顾紧凑模式自适应内容高度与展开模式动态调整历史高度。

---

## 二、问题根因剖析与修复实现

### 1. 窗口四角不一致与 ResizeGrip 残损
- **根因分析**：
  在 `MainWindow.cs` 中，窗口属性配置了 `ResizeMode = ResizeMode.CanResizeWithGrip`。在 WPF 无边框透明窗口（`WindowStyle.None` + `AllowsTransparency = true`）下，WPF 原生会在客户区右下角强行绘制 Win32 系统的白色点阵三角形 Grip，该原生元素直接绘制在 `Border` 上层，遮挡破坏了右下角的 12px 曲线，导致四角视觉严重不对称。
- **修复方案**：
  - 将 `ResizeMode` 改为 `ResizeMode.CanResize`；
  - 彻底移除了右下角的原生三角形 Grip；
  - 验证：主窗口四个角半径（12px）无论在紧凑模式还是展开模式下均完全对称平滑。

### 2. 滚动条古老、刺眼且与主题不协调
- **根因分析**：
  `DesktopTheme.cs` 的主题 XAML 字典中仅定义了 `Button`、`TextBox`、`ComboBox`、`ListBox` 等基本控件样式，未覆盖 `ScrollBar` 样式。导致 `history` ListBox 及 `SettingsWindow` 降级使用 Windows 系统默认的 17px 宽亮白色 Win32 滚动条，在深色背景下极度突兀，且容易遮挡字幕文本边缘。
- **修复方案**：
  在 `DesktopTheme.cs` 的资源字典中注入定制的现代 XAML `ScrollBar` 模板：
  - 尺寸精简：垂直与水平方向宽度/高度均设为 8px；
  - 极简轨道：`PART_Track` 背景完全透明，首尾翻页 RepeatButton 设为不可见；
  - 现代胶囊 Thumb：圆角为 4px（`CornerRadius="4"`），边距 1px；
  - 主题动态联动：常规状态下透明度为 `0.38`（绑定 `{DynamicResource Muted}`），鼠标悬停（Hover）时切换至 `{DynamicResource Accent}` 且透明度提升至 `0.85`，拖拽（Dragging）时透明度提升至 `1.0`；
  - 自动继承：所有嵌套在 `ScrollViewer` 与 `ListBox` 中的滚动条（包括主窗口历史列表和设置页面各设置页）均自动应用此现代样式。

### 3. 无边框窗口 8 方向平滑缩放
- **机制设计**：
  移除 `ResizeGrip` 后，在 `MainWindow.cs` 中重写 `OnSourceInitialized`，利用 `HwndSource.AddHook` 挂接 `WndProc`，拦截 `WM_NCHITTEST (0x0084)` 消息：
  - 读取物理屏幕坐标并结合 `GetWindowRect` 进行物理像素对齐计算，完全规避了浮点误差与多 DPI 缩放下的坐标漂移；
  - 设置 7px 边框敏感区与 14px 拐角敏感区，动态返回 `HTTOPLEFT`, `HTTOPRIGHT`, `HTBOTTOMLEFT`, `HTBOTTOMRIGHT`, `HTLEFT`, `HTRIGHT`, `HTTOP`, `HTBOTTOM`；
  - 当鼠标进入边缘或拐角时，Windows 自动切换为原生缩放光标，点击并拖拽触发系统标准的非客户区平滑缩放；
  - 在内部安全区内返回 `IntPtr.Zero`，不拦截正常控件点击与已实现的空白区 `DragMove()` 窗口移动。
- **自适应高度与展开联动**：
  - 紧凑模式下继续由 `SizeToContent.Height` 紧密包裹两行双语字幕；
  - 展开历史记录时，监听 `SizeChanged` 动态根据窗口拉伸高度自适应计算 `history.Height`，收起时自动恢复 `SizeToContent.Height` 与默认 320px 高度。

---

## 三、代码质量与可靠性审计

1. **异步取消与资源释放 (IDisposable / IAsyncDisposable)**：
   - `CaptionPipeline` 与 `WasapiAudioSource` 使用双向联动 `CancellationToken`，在会话切换、语言变更或窗口关闭时，能于 1 秒内安全退出后台采集与流式识别循环，无音频线程悬挂。
   - `TranslationScheduler` 的分词与调度队列具有抢占式取消（`Attempt.Cancel()`），当识别服务发送新的最终分句或中间修正时，旧的不必要翻译请求被立即中断，避免浪费网络调用与费用。
2. **凭据安全隔离**：
   - API 密钥全部经 Windows DPAPI 加密隔离存储，明文仅在用户显式点击显隐切换时短暂展示，切换服务提供商或窗口关闭即刻清空显式明文视图。
3. **滚动穿透与触控板支持**：
   - `ScrollRelay` 在文本框（单行/多行/密码框）与外层容器之间建立了标准的冒泡与边界借力机制，用户双指触控滚动设置页面时不会因指针滑过文本输入框而卡死停顿。

---

## 四、自动化测试与验收结果

- 解决方案编译：0 Warning(s), 0 Error(s)
- 核心与平台测试（全量通过）：
  - `AntiDbt.Core.Tests`: 36/36 PASS
  - `AntiDbt.Windows.Tests`: 19/19 PASS
  - `AntiDbt.CloudSpeech.Tests`: 36/36 PASS
  - `AntiDbt.Providers.Tests`: 86/86 PASS
  - `AntiDbt.Providers.Multi.Tests`: 13/13 PASS
  - `AntiDbt.Language.Tests`: 70/70 PASS
  - `AntiDbt.Domestic.Tests`: 72/72 PASS
  - Localhost 组合全排列: 全部通过
  - Azure 资源冒烟: 全部通过
  - 桌面自动化 Smoke: 76/76 全部通过（新增 `CanResize` 与 `ScrollBar` 样式断言），退出码 0。
  - 性能与 60 秒稳定性浸泡测试（`--soak`）：通过，托管内存增长小于 100 KB。
