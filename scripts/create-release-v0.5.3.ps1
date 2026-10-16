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

$tagName = "v0.5.3-beta"
$releaseTitle = "antiDBT v0.5.3 Beta · Multilingual UI & Reliability Release"

$body = @"
## antiDBT v0.5.3 Beta 版本说明 / Release Notes

antiDBT 是专为 Windows 10/11 设计的现代化独立桌面实时双语字幕悬浮窗客户端。通过 WASAPI 系统音频回环采集，结合低延迟语音识别（ASR）与机器翻译（MT）服务，提供纯净流畅的双语字幕体验。

本次 **v0.5.3-beta** 是一个重大的可靠性与国际化里程碑版本，完成了对 Astra 源码级技术审查意见的全面工程落地（A0～A8），并实现了客户端与文档体系的全多语言本地化。

---

### 🌐 1. 全多语言界面与文档体系 / Multilingual UI & Documentation
- **客户端多语言切换**：在「设置 → 外观」中提供界面语言选择，支持 **简体中文 (`zh-Hans`)**、**English (`en`)**、**日本語 (`ja`)**。
- **免重启实时原地生效**：切换语言即刻刷新主界面标题、状态指示徽章、控制按钮、上下文菜单与全部设置项，无需中断正在运行的字幕采集。
- **国际化文档矩阵**：
  - 🇨🇳 简体中文：[README.md](README.md)
  - 🇺🇸 English: [README.en.md](README.en.md)
  - 🇭🇰/🇹🇼 繁體中文: [README.zh-TW.md](README.zh-TW.md)
  - 🇯🇵 日本語: [README.ja.md](README.ja.md)

---

### 🛡️ 2. 技术审查工程落地与可靠性硬化 (A0～A8)

1. **A1 尾句防截断与资源生命周期安全**
   - 修复停止采集时最后一句语音或翻译被强制截断丢失的缺陷；
   - 增加排空缓冲机制，保证停止后尾句完整渲染并进入历史；
   - 修复音频源对象多重所有权隐患，保障会话重构时的资源安全释放。

2. **A2 译文闪烁根治、韩文断句与单句重试**
   - 彻底移除“等待译文…”占位文字，保留有效中间译文以平滑过渡到终稿；
   - 针对韩文/日文增量更新中的词间空格合并问题引入专用语言分词保护；
   - 列表中针对单段翻译失败引入独立重试按钮（↻ 重试），无需重启整个采集管线。

3. **A3 音频时效性有界队列与延迟兜底**
   - 针对网络波动导致音频堆积的问题，引入上限 5 秒的有界音频缓冲区；
   - 超时音频块智能丢弃并精确记录丢弃块数，右上角状态提示可悬浮查看诊断详情，彻底杜绝延迟滚雪球。

4. **A4 火山引擎 ASR 结构化错误解析与自适应重试**
   - 完整映射 Volcengine WebSocket 二进制协议中的业务状态码；
   - 区分瞬态网络异常与配置错误，提供指数退避的自动重连机制。

5. **A5 桌面非侵入式配置与翻页保护**
   - 窗口置顶、暗黑/明亮主题、背景透明度、语言方向与界面语言持久化保存；
   - 修复长列表向上翻阅历史时因新字幕到达而被强制拉回底部的跳屏问题（智能解耦阅读状态与自动跟随）。

6. **A6-A8 全量回归验证与性能基准**
   - 建立涵盖离线单元测试、Windows 原生音频回环、云端协议模拟、多提供商组合、本地离线仿真、WPF 桌面自动化烟测、延迟性能基准等共 **12 项自动化验证套件**，回归通过率 **100% PASS**。

---

### 📦 资产校验信息 / Release Assets & Verification

| 文件名 | 体积 | SHA256 校验码 | 说明 |
| :--- | :--- | :--- | :--- |
| **`antiDBT-v0.5.3-beta-win-x64.zip`** | 60.7 MB | `865A115D87E2D19AEE25A539882AAAD13347B4908E23F16E95015725614F815D` | 独立免安装绿色便携包（内含自包含 .NET 9 运行时） |
| **`antiDBT-v0.5.3-beta-win-x64.sha256`** | 287 B | — | 包含 `antiDBT.exe` 及 ZIP 的 SHA256 校验清单 |

*主执行程序 `antiDBT.exe` SHA256：`E9DA1F05DD49EF3AF19BD308E353A40F6EB715B607344D45E6A923CD786B55B5`*

---

### 🚀 运行要求
- **系统**：Windows 10 1809 (Build 17763) 或更高版本 / Windows 11
- **环境**：自包含便携包，无需预装 .NET 运行时，解压后双击 `antiDBT.exe` 即可直接运行。
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
        prerelease = $true
    } | ConvertTo-Json -Depth 5

    Write-Output "Creating release via GitHub API..."
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/Morchyee/AntiDBT/releases" -Method Post -Headers $headers -Body ([Text.Encoding]::UTF8.GetBytes($payload)) -ContentType "application/json; charset=utf-8"
    Write-Output "Created release ID: $($release.id)"
}

Write-Output "Release HTML URL: $($release.html_url)"

$uploadHeaders = @{
    "Authorization" = "Bearer $token"
    "Accept" = "application/vnd.github.v3+json"
    "User-Agent" = "antiDBT-Release"
}

# Upload files: ZIP and SHA256
$filesToUpload = @(
    @{ Path = "artifacts/phase5.3/dist/antiDBT-v0.5.3-beta-win-x64.zip"; Name = "antiDBT-v0.5.3-beta-win-x64.zip"; ContentType = "application/zip" },
    @{ Path = "artifacts/phase5.3/dist/antiDBT-v0.5.3-beta-win-x64.sha256"; Name = "antiDBT-v0.5.3-beta-win-x64.sha256"; ContentType = "text/plain" }
)

$existingAssets = Invoke-RestMethod -Uri "https://api.github.com/repos/Morchyee/AntiDBT/releases/$($release.id)/assets" -Method Get -Headers $headers

foreach ($f in $filesToUpload) {
    $filePath = Resolve-Path $f.Path
    $fileName = $f.Name
    $contentType = $f.ContentType

    # Check if pre-existing
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
    $asset = Invoke-RestMethod -Uri $uploadUrl -Method Post -Headers $fileHeaders -Body $bytes -TimeoutSec 1200
    Write-Output "Upload complete! Asset ID: $($asset.id) ($($asset.browser_download_url))"
}

Write-Output "`nRelease publication completed successfully!"
