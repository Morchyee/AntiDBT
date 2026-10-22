$ErrorActionPreference = 'Stop'
$token = ("protocol=https`nhost=github.com`n" | git credential fill | Select-String "password=(.*)").Matches.Groups[1].Value
if (-not $token) {
    throw "GitHub token not found via git credential helper"
}

$headers = @{
    "Authorization" = "Bearer $token"
    "Accept" = "application/vnd.github.v3+json"
    "User-Agent" = "antiDBT-Release"
}

$tagName = "v0.5.5"
$releaseTitle = "antiDBT v0.5.5 - 独立桌面实时双语字幕正式稳定版"

$distDir = "artifacts/dist"
$zipPath = Join-Path $distDir "antiDBT-v0.5.5-win-x64-full.zip"
$shaFile = Join-Path $distDir "antiDBT-v0.5.5-win-x64-full.sha256"

if (-not (Test-Path -LiteralPath $zipPath)) {
    throw "Release package ZIP not found: $zipPath. Run scripts/build-candidate-package.ps1 first."
}
if (-not (Test-Path -LiteralPath $shaFile)) {
    throw "Release SHA256 checksum file not found: $shaFile."
}

$zipBytes = (Get-Item -LiteralPath $zipPath).Length
$zipSizeMB = [Math]::Round($zipBytes / 1MB, 1)
$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
$manifestPath = Join-Path $distDir "release-manifest.json"
$manifest = if (Test-Path $manifestPath) { Get-Content $manifestPath -Raw | ConvertFrom-Json } else { $null }
$exeHash = if ($manifest) { $manifest.ExecutableHash } else { "N/A" }

$body = @"
## antiDBT v0.5.5 正式稳定版发布说明

antiDBT 是专为 Windows 10/11 设计的独立桌面实时双语字幕悬浮窗客户端。通过 Windows WASAPI 系统音频回环捕获正在播放的声音，结合低延迟语音识别（ASR）与机器翻译（MT）服务，在屏幕上呈现低延迟、排版自然的双语对照字幕。

v0.5.5 是首个正式稳定版（非 beta）。本版本完成了 Phase 5.5.5 字幕状态机与动态时序保序硬化，升级了默认本地英文语音识别模型，并通过了全套 12 项自动化验证套件及真实视频播放测试。

---

### 主要更新与改进

#### 1. 默认本地英文语音识别模型升级
- **升级模型**：内置 `sherpa-onnx-streaming-zipformer-en-2023-06-21`（INT8 量化）。
- **准确率提升**：相较此前轻量 20M 模型，复杂长句、口语化及技术词汇的识别准确率明显提高。
- **低延迟流式响应**：维持近实时的流式首字上屏速度。
- **平稳资源开销**：完全在本地 x64 CPU 上推理，内存占用平稳（实际测试约 260 MB 级别），无需配置 ASR API Key，无额外云端调用费用。
- **解耦设计**：本地 ASR 仅负责英文语音转写；中文翻译属于独立管线，需另行配置翻译服务（如 Groq、DeepL 或本地 Ollama）。

#### 2. 字幕状态机与语义排序硬化（Phase 5.5.5）
- **动态时间线保序**：依据声学时间锚点对异步到达的识别与翻译事件进行动态排序，彻底解决网络抖动或并发回调导致的历史字幕跳动与闪烁。
- **粘性翻译上下文（Sticky Translation）**：流式语音修正期间平滑保留前序句子的有效译文，避免译文出现短暂空白。
- **分组修订一致性隔离**：当语音识别前缀发生语义合并或拆分时，精准回收过期的翻译请求，保证视口展示与底层数据状态严格同步。
- **智能分句与段落衔接**：基于声学静音阈值与标点断句算法，对连续语音自然聚合成段，长停顿处自然划分段落。

#### 3. 稳定可靠的桌面交互
- **可调半透明悬浮窗**：背景 0%～100% 透明度自由平滑调节，文字与按钮保持高对比度实体渲染，窗口支持透视且具备防穿透保护。
- **自适应视口与自然折行**：纵向拖拉自动填充可用高度，横向缩放自然折行，文字绝不溢出或裁切。
- **平滑滚动与智能跟随**：滚轮与触摸板像素级平滑阻尼；向上回看历史时自动锁定视图，回到最底或点击底部浮标时恢复自动跟随。
- **本地凭据安全**：所有 API Key 经由 Windows DPAPI 本地加密存储于 `data/settings.json`，不上云端。

---

### 发布文件与校验信息

| 文件名 | 大小 | SHA256 校验码 | 说明 |
| :--- | :--- | :--- | :--- |
| **`antiDBT-v0.5.5-win-x64-full.zip`** | $zipSizeMB MB ($zipBytes 字节) | `$zipHash` | Windows x64 完整自包含便携包（内置 .NET 10 运行时及 Zipformer 模型，单一顶层目录 antiDBT/） |
| **`antiDBT-v0.5.5-win-x64-full.sha256`** | 文本 | SHA256 校验列表 | 包含 ZIP 及主程序 `antiDBT.exe` 的哈希校验 |

*主执行程序 `antiDBT.exe` SHA256：`$exeHash`*

---

### 系统要求与安装运行

- **操作系统**：Windows 10 64 位（版本 1903 / Build 18362 及以上）或 Windows 11 64 位。
- **音频架构**：支持 WASAPI 回环捕获的系统默认音频输出设备（需工作在共享模式 Shared Mode）。
- **便携免安装**：发布包内置独立自包含 .NET 10 运行时及 Sherpa-ONNX 原生动态库，**终端用户无需安装 .NET SDK、.NET Runtime、Python、Go、CUDA 或 C++ 编译环境**。
- **快速启动**：
  1. 下载 `antiDBT-v0.5.5-win-x64-full.zip` 并校验 SHA256。
  2. 解压压缩包至本地目录（解压后为单一 `antiDBT` 文件夹）。
  3. 双击 `antiDBT.exe` 启动。首次使用在设置中确认音频设备与翻译服务（如需中文翻译可配置 Groq 或 DeepL），保存后点击“开始”即可。

---

### 验证与已知边界

- **自动化验证**：全套 12 项自动化验证套件（共 125 项单元及回归测试，涵盖 Core 状态机、Windows WASAPI 契约、CloudSpeech、Providers、WPF Desktop Smoke、Diagnostics 等）100% PASS。
- **本地模型测试**：通过真实音频流式推流与中文路径运行验证。
- **已知边界**：
  - 不支持 WASAPI 独占模式（Exclusive Mode），使用时播放软件需设为共享输出。
  - 本地 Zipformer 模型仅支持英文语音识别，不支持中文语音识别。
  - 云端翻译服务的实际可用性受服务商网络连接与账户配额影响。
"@

$release = $null
try {
    $existing = Invoke-RestMethod -Uri "https://api.github.com/repos/Morchyee/AntiDBT/releases/tags/$tagName" -Method Get -Headers $headers
    if ($existing) {
        Write-Output "Existing release found, ID: $($existing.id)"
        $release = $existing
    }
} catch {
    Write-Output "No existing release for $tagName"
}

if (-not $release) {
    $payload = @{
        tag_name = $tagName
        target_commitish = "main"
        name = $releaseTitle
        body = $body
        draft = $false
        prerelease = $false
    } | ConvertTo-Json -Depth 5

    Write-Output "Creating official stable release via GitHub API..."
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/Morchyee/AntiDBT/releases" -Method Post -Headers $headers -Body ([Text.Encoding]::UTF8.GetBytes($payload)) -ContentType "application/json; charset=utf-8"
    Write-Output "Created release ID: $($release.id)"
} else {
    # Update release body and title to ensure official non-prerelease metadata
    $updatePayload = @{
        name = $releaseTitle
        body = $body
        draft = $false
        prerelease = $false
    } | ConvertTo-Json -Depth 5
    Write-Output "Updating existing release $($release.id)..."
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/Morchyee/AntiDBT/releases/$($release.id)" -Method Patch -Headers $headers -Body ([Text.Encoding]::UTF8.GetBytes($updatePayload)) -ContentType "application/json; charset=utf-8"
}

Write-Output "Release HTML URL: $($release.html_url)"

$filesToUpload = @(
    @{ Path = $zipPath; Name = "antiDBT-v0.5.5-win-x64-full.zip"; ContentType = "application/zip" },
    @{ Path = $shaFile; Name = "antiDBT-v0.5.5-win-x64-full.sha256"; ContentType = "text/plain" }
)

$existingAssets = Invoke-RestMethod -Uri "https://api.github.com/repos/Morchyee/AntiDBT/releases/$($release.id)/assets" -Method Get -Headers $headers

foreach ($f in $filesToUpload) {
    $filePath = Resolve-Path $f.Path
    $fileName = $f.Name
    $contentType = $f.ContentType

    foreach ($a in $existingAssets) {
        if ($a.name -eq $fileName) {
            Write-Output "Deleting pre-existing asset $($a.id) ($fileName)..."
            Invoke-RestMethod -Uri "https://api.github.com/repos/Morchyee/AntiDBT/releases/assets/$($a.id)" -Method Delete -Headers $headers
        }
    }

    $uploadUrl = "https://uploads.github.com/repos/Morchyee/AntiDBT/releases/$($release.id)/assets?name=$fileName"
    Write-Output "Uploading $filePath to $uploadUrl..."
    $fileHeaders = @{
        "Authorization" = "Bearer $token"
        "Accept" = "application/vnd.github.v3+json"
        "User-Agent" = "antiDBT-Release"
        "Content-Type" = $contentType
    }

    $bytes = [IO.File]::ReadAllBytes($filePath)
    Write-Output "Read $($bytes.Length) bytes for $fileName. Posting..."
    $asset = Invoke-RestMethod -Uri $uploadUrl -Method Post -Headers $fileHeaders -Body $bytes -TimeoutSec 1800
    Write-Output "Upload complete! Asset ID: $($asset.id) ($($asset.browser_download_url))"
}

Write-Output "`nOfficial stable release v0.5.5 publication completed successfully!"
