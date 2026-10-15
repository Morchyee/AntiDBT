$ErrorActionPreference = 'Stop'
$token = ("protocol=https`nhost=github.com`n" | git credential fill | Select-String "password=(.*)").Matches.Groups[1].Value
$headers = @{
    "Authorization" = "Bearer $token"
    "Accept" = "application/vnd.github.v3+json"
    "User-Agent" = "antiDBT-Release"
}

# Check if release already exists
try {
    $existing = Invoke-RestMethod -Uri "https://api.github.com/repos/Morchyee/AntiDBT/releases/tags/v0.5.2-beta" -Method Get -Headers $headers
    if ($existing) {
        Write-Output "Existing release found, ID: $($existing.id)"
        $release = $existing
    }
} catch {
    Write-Output "No existing release for v0.5.2-beta"
}

if (-not $release) {
    $body = @"
## antiDBT v0.5.2 Beta 版本说明

antiDBT 是专为 Windows 10/11 设计的独立桌面实时双语字幕悬浮窗客户端。通过 WASAPI 系统音频回环采集，结合低延迟语音识别（ASR）与机器翻译（MT）服务，提供纯净流畅的双语字幕体验。

本次 v0.5.2-beta 为重要功能与交互优化版本，包含以下核心演进：

### 🌟 核心新特性与优化

1. **🎨 真正的可调半透明悬浮窗**
   - 在「设置 → 外观」中新增 **0%～100% 背景不透明度**调节滑块（默认推荐 80%）。
   - 支持免重启实时无感预览（Live Preview），取消设置时自动回滚。
   - **文字与背景彻底解耦**：原文、译文、按钮及状态指示保持 100% 实体高对比度不透明清晰度，杜绝全局模糊。
   - **防穿透安全保障**：即便拉至 0% 透明度，底层鼠标命中测试依然有效，绝不意外穿透点击后方窗口。

2. **📐 窗口尺寸全高动态自适应**
   - 彻底解除历史字幕固定高度限制（消除原 76px / 320px 硬约束），采用弹性网格布局。
   - 窗口纵向拖拽拉大时，历史字幕列表**全高动态填充**，消除下方大面积空白死区，支持大屏浏览更多历史记录。
   - 窗口横向拉宽时，双语字幕根据新视口宽度**自然折行重排**，文字绝不溢出或被裁切。
   - **「↓ 回到底部」悬浮徽章**修正定位至字幕视口右下方（Overlay），不再随窗口高度漂移至窗口中央。

3. **📜 滚动交互与智能跟随体验升级**
   - 收起（紧凑）模式与展开模式均全面支持**丝滑像素级滚动**（Pixel Scroll Unit），阻尼舒适自然。
   - 支持触控板微小双指滑动（Precision Delta），滚轮单档位移稳定在 1～1.5 行高（~30-34px），消除原生跳页感。
   - **翻阅与跟随智能解耦**：向上滚动浏览历史时自动暂停跟随；返回最底或点击「↓ 回到底部」时自动恢复跟随；新字幕到达或窗口缩放时**绝不强跳底部**。

4. **⚡ 低延迟管线与自然连续排版**
   - 彻底移除「等待译文…」等无意义占位文字，原文识别完成后立即就地呈现，译文到达后平滑原地更新。
   - 连续讲话自动在同一段落内流动排版（不强制断行换行），并智能处理中英文标点空格。

5. **🌐 顶部集成式多语言选择**
   - 顶部工具栏展示当前语言方向（如 ``英语 → 中文（简体） ▾``），点击弹出轻量语言选择卡片。
   - 支持源语言选择、目标语言选择、一键交换（⇄）与自动检测（Auto-Detect）。
   - 智能适配并提示各 Provider 的语言能力约束。

6. **✨ 现代化极简主界面**
   - 从主窗口移除不必要的性能参数与底部冗余状态栏，仅在右上角保留低调工作状态指示（🟢 聆听中 / 🟡 连接中 / 🔴 连接异常 / ⚪ 未开始）。
   - 采用 12px 现代圆角微边框设计，深色与浅色双主题无缝支持。

---

### ⚠️ 当前已知限制

1. **API Key 依赖**：实际使用系统音频进行语音识别与翻译需在设置中配置有效 API Key（支持 Groq、DeepL、OpenAI 兼容端点、火山引擎、Azure、Google Cloud 等）。
2. **WASAPI 独占模式**：如果个别播放软件启用了独占输出模式（Exclusive Mode），系统回环录音可能无法采集，请切换为共享模式（Shared Mode）。
3. **网络与延迟**：端到端延迟受所选云端 API 厂商地理位置及网络环境影响，推荐使用 Groq Whisper + DeepL / 高速 LLM 组合。

---

### 📦 资产校验信息

- **文件名**：``antiDBT-phase5.2-win-x64.zip``
- **文件体积**：约 85.5 MB (内含自包含 .NET 9 运行时，解压即用无需配置运行环境)
- **ZIP SHA256**：``99564B76A607EAC5874455E5D9541758E2E46117A07E25A676444DA30E9602F6``
- **内部主程序 ``antiDBT.exe`` SHA256**：``9BEC28186CEA33BA1E1D394F87305374ABB8E0E5EBB8CB399A8AF86BD8E1C25E``
"@

    $payload = @{
        tag_name = "v0.5.2-beta"
        target_commitish = "main"
        name = "antiDBT v0.5.2 Beta"
        body = $body
        draft = $false
        prerelease = $true
    } | ConvertTo-Json -Depth 5

    Write-Output "Creating release via GitHub API..."
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/Morchyee/AntiDBT/releases" -Method Post -Headers $headers -Body ([Text.Encoding]::UTF8.GetBytes($payload)) -ContentType "application/json; charset=utf-8"
    Write-Output "Created release ID: $($release.id)"
}

Write-Output "Release HTML URL: $($release.html_url)"

$zipPath = "publish/antiDBT-phase5.2-win-x64.zip"
if (-not (Test-Path $zipPath)) { throw "ZIP missing: $zipPath" }

# Check if asset already exists on this release
$assets = Invoke-RestMethod -Uri "https://api.github.com/repos/Morchyee/AntiDBT/releases/$($release.id)/assets" -Method Get -Headers $headers
foreach ($a in $assets) {
    if ($a.name -eq 'antiDBT-phase5.2-win-x64.zip') {
        Write-Output "Deleting pre-existing asset $($a.id)..."
        Invoke-RestMethod -Uri "https://api.github.com/repos/Morchyee/AntiDBT/releases/assets/$($a.id)" -Method Delete -Headers $headers
    }
}

$uploadUrl = "https://uploads.github.com/repos/Morchyee/AntiDBT/releases/$($release.id)/assets?name=antiDBT-phase5.2-win-x64.zip"
Write-Output "Uploading $zipPath to $uploadUrl..."

$uploadHeaders = @{
    "Authorization" = "Bearer $token"
    "Accept" = "application/vnd.github.v3+json"
    "User-Agent" = "antiDBT-Release"
    "Content-Type" = "application/zip"
}

$bytes = [IO.File]::ReadAllBytes((Resolve-Path $zipPath).Path)
Write-Output "Read $($bytes.Length) bytes. Posting..."

$asset = Invoke-RestMethod -Uri $uploadUrl -Method Post -Headers $uploadHeaders -Body $bytes -TimeoutSec 1200
Write-Output "Upload complete! Asset ID: $($asset.id)"
Write-Output "Download URL: $($asset.browser_download_url)"
Write-Output "Asset Size: $($asset.size) bytes"
