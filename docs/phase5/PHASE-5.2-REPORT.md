# antiDBT Phase 5.2 交付报告：窗口自适应与可调半透明悬浮窗专项修复

## 一、完成目标概览

本轮优化围绕 Phase 5.2 核心体验与两大交互痛点进行，已完成代码修改、全量自动化验证、桌面 Smoke 截图捕获与便携包发布：

1. **彻底修复窗口拉伸后字幕区域不匹配**：
   - **自适应视口高度**：主布局网格中将字幕区域定义为弹性星号行（`GridLength(1, GridUnitType.Star)`），移除写死的 Row 2 底部工具栏。
   - **历史字幕全高填充**：消除原来固定的 `MaxHeight = 76` 或 `Height = 320` 限制。当用户手动向下拖拽拉大窗口时，历史字幕列表动态铺满全部可用高度，容纳更多历史记录，彻底消除窗口下半部的未利用大面积空白。
   - **动态折行排版**：窗口横向拉宽或拉窄时，原文与译文依新视口宽度自然排版换行，不裁切、不溢出。
   - **“↓ 回到底部”悬浮徽章位置修正**：将“↓ 回到底部”移至字幕视口右下角（Row 1 内部，`HorizontalAlignment.Right`、`VerticalAlignment.Bottom`、`Margin=(0,0,18,12)`、`Panel.ZIndex=10`），成为视口右下角常驻浮动徽章。无论窗口拉长、缩短或字幕多少，绝不漂移至窗口中央。
   - **滚动与跟随机制优化**：
     - 用户向上滚动浏览历史时，自动暂停滚动跟随，保持视口位置；新字幕到达或窗口缩放时**绝不强制跳回底部**。
     - 开启自动跟随时，最新字幕始终保持在视口底部。
     - 点击“↓ 回到底部”或滚动回最下方时立即恢复自动跟随。
     - 收起模式与展开模式均支持丝滑的像素级滚动与动态布局。

2. **实现真正的可调半透明悬浮窗**：
   - **背景不透明度滑块**：在“设置 → 外观”中新增 0%～100% 的“背景不透明度”滑块（推荐默认 80%），附带动态百分比指示。
   - **无感知实时预览**：滑块拖动过程中通过 `ApplyLiveOpacity` 毫秒级响应，主窗口背景半透明度即时变化，无需重启软件；若用户点击取消/关闭对话框，自动还原原透明度。
   - **持久化与启动恢复**：在 `AppSettings` 中新增 `BackgroundOpacity` 配置项，随设置自动持久化并在应用启动时平滑恢复。
   - **背景与文字彻底解耦**：严禁使用全局 `Window.Opacity`。主窗体采用 `Window.Background = Brushes.Transparent`，仅在 12px 圆角 `Border` 的 `Surface` 笔刷上应用 Alpha 通道。原文、译文、按钮、图标均保持 100% 实体高对比度不透明清晰渲染。
   - **真实桌面透视与防点击穿透**：背景完全透视后方桌面或应用程序窗口，四角 12px 圆角无黑角、黑底板或白边；透明度即使调至 0%，底层 Alpha 亦钳制在最小安全值 1，杜绝 Win32/WPF 鼠标命中测试失效导致的点击穿透。

3. **保留 Phase 5.1 成果与独立发布**：
   - 完整保留 Phase 5.1 所有代码成果及发布包。
   - 成功编译并打包为独立的 `publish/antiDBT-phase5.2-win-x64` 及 ZIP 压缩包。
   - 严禁提交 `.agents/` 目录。

---

## 二、测试与实机验证结果

### 2.1 单元与集成测试（全绿）
- **Core / Windows / CloudSpeech / Providers / Multi / Language / Domestic**：全部 7 个测试套件均 100% 通过。
- **自动化测试脚本**（`pwsh -File .\scripts\test-phase4.ps1 -SkipRestore`）：一次性全绿通过。

### 2.2 桌面 Smoke 测试（实机渲染与截图）
通过 `antiDBT.exe --smoke-test` 完成 75+ 项全链路断言与截图捕获（存放于 `artifacts/phase4/desktop-smoke/`）：
- `desktop-stretched-tall.png`：窗口拉长至 650px 高度，字幕容器高度达到 500px+，全高填充历史记录，无多余空白，回到底部徽章固定在视口右下角。
- `desktop-stretched-wide.png`：窗口拉宽至 1100px，文本依新宽度自然折行排版。
- `desktop-opacity-80pct.png`：默认 80% 半透明背景，文字与控制台清晰不透明。
- `desktop-opacity-50pct.png`：50% 半透明背景即时预览。
- `desktop-opacity-20pct.png`：20% 高透背景即时预览。
- `desktop-settings.png`：设置界面“外观”选项卡中滑块与百分比标签正常展示。
- 断言包含：`history stretches to fill available window height without empty void`、`bottom button overlays at bottom-right`、`window adapts to wide layout`、`opacity slider present`、`default opacity is 80%`、`live opacity 50% reflects in surface alpha`、`opacity 0% maintains minimum alpha 1 to prevent click-through`、`saved settings includes background opacity`、`reopened settings retains background opacity` 等。

---

## 三、产物校验信息

- **便携包目录**：`publish/antiDBT-phase5.2-win-x64`
- **压缩包路径**：`publish/antiDBT-phase5.2-win-x64.zip`
- **antiDBT.exe SHA256**：
  `9BEC28186CEA33BA1E1D394F87305374ABB8E0E5EBB8CB399A8AF86BD8E1C25E`
- **ZIP SHA256**：
  `99564B76A607EAC5874455E5D9541758E2E46117A07E25A676444DA30E9602F6`
