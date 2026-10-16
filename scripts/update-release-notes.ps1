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
$existing = Invoke-RestMethod -Uri "https://api.github.com/repos/Morchyee/AntiDBT/releases/tags/$tagName" -Method Get -Headers $headers
if (-not $existing) {
    throw "Release $tagName not found on GitHub"
}

$releaseId = $existing.id
Write-Output "Found release ID: $releaseId"

$body = @"
## antiDBT v0.5.3 Beta 版本说明 / Release Notes

antiDBT 是专为 Windows 10/11 设计的现代化独立桌面实时双语字幕悬浮窗客户端。通过 WASAPI 系统音频回环采集，结合低延迟语音识别（ASR）与机器翻译（MT）服务，提供纯净流畅的双语字幕体验。

本次 **v0.5.3-beta** 是一个重大的可靠性硬化与国际化演进版本，完成了对 Astra 源码级技术审查意见的全面工程落地（A0～A8），并实现了客户端与文档体系的全多语言本地化。

---

### 🌐 1. 全多语言界面与文档体系 / Multilingual UI & Documentation
- **客户端多语言切换**：在「设置 → 外观」中提供界面语言选择，支持简体中文（zh-Hans）、English（en）、日本語（ja）。
- **免重启实时原地生效**：切换语言即刻刷新主界面标题、状态指示徽章、控制按钮、上下文菜单与全部设置项，无需中断正在运行的字幕采集。
- **国际化文档矩阵**：
  - 🇨🇳 简体中文: [README.md](https://github.com/Morchyee/AntiDBT/blob/main/README.md)
  - 🇺🇸 English: [README.en.md](https://github.com/Morchyee/AntiDBT/blob/main/README.en.md)
  - 🇭🇰/🇹🇼 繁體中文: [README.zh-TW.md](https://github.com/Morchyee/AntiDBT/blob/main/README.zh-TW.md)
  - 🇯🇵 日本語: [README.ja.md](https://github.com/Morchyee/AntiDBT/blob/main/README.ja.md)

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
   - 针对网络波动导致音频堆积的问题，引入名义容量为 50 个音频块（每块 6,400 字节，在 16kHz/16-bit 单声道下对应 200ms 音频，队列名义容量约 10 秒）的有界缓冲区；
   - 单块音频存活时效阈值为 2.0 秒，因服务阻塞积压超过 2.0 秒的过期音频块自动丢弃并记录断续统计，杜绝延迟无限滚雪球。

4. **A4 火山引擎 ASR 结构化错误解析与自适应重试**
   - 完整映射 Volcengine WebSocket 二进制协议中的业务状态码；
   - 区分瞬态网络异常与配置错误，提供指数退避的自动重连机制。

5. **A5 桌面非侵入式配置与翻页保护**
   - 窗口置顶、暗黑/明亮主题、背景透明度、语言方向与界面语言持久化保存；
   - 修复长列表向上翻阅历史时因新字幕到达而被强制拉回底部的跳屏问题（智能解耦阅读状态与自动跟随）。

6. **A6-A8 自动化回归验证与性能基准说明**
   - 建立涵盖离线单元测试、Windows 原生音频回环契约、云端协议模拟、多提供商组合、本地离线仿真、WPF 桌面自动化烟测、延迟性能基准等共 **12 项自动化验证套件**，本地及构建流水线通过率 100% PASS。
   - **真实环境免责说明**：离线与 Mock 烟测用于保障状态机与交互契约的严密性，**不代表真实公网环境下的各第三方云端服务商实际配额、限流表现或长周期不间断运行的在线稳定性**；实际在线字幕效果依赖用户配置的云端 Provider 凭据、网络延迟与计费状态。

---

### ⚙️ 3. 运行环境与运行时说明 / Runtime & Prerequisites
- **操作系统**：Windows 10 1809 (Build 17763) 或更高版本 / Windows 11 (x64)
- **.NET 支持状态**：
  - **当前发布版本**：采用 **.NET 9 (net9.0-windows)** 独立自包含（Self-Contained）编译发布，解压即用，**用户无需预装任何 .NET SDK 或运行时**。
  - **.NET 10 LTS 状态**：.NET 10 LTS 迁移目前在独立开发分支中进行技术评估与兼容性验证，待验证充分后再行整合，当前正式包保持稳定的 .NET 9 生产基线。

---

### 📦 资产校验信息 / Release Assets & Verification

| 文件名 | 体积 | SHA256 校验码 | 说明 |
| :--- | :--- | :--- | :--- |
| **`antiDBT-v0.5.3-beta-win-x64.zip`** | 60.7 MB | `865A115D87E2D19AEE25A539882AAAD13347B4908E23F16E95015725614F815D` | 独立免安装绿色便携包（内置自包含运行时） |
| **`antiDBT-v0.5.3-beta-win-x64.sha256`** | 287 B | — | 包含 `antiDBT.exe` 及 ZIP 的 SHA256 校验清单 |

*主执行程序 `antiDBT.exe` SHA256：`E9DA1F05DD49EF3AF19BD308E353A40F6EB715B607344D45E6A923CD786B55B5`*
"@

$payload = @{
    body = $body
} | ConvertTo-Json -Depth 5

Write-Output "Updating release notes for $tagName via GitHub REST API (PATCH)..."
$updated = Invoke-RestMethod -Uri "https://api.github.com/repos/Morchyee/AntiDBT/releases/$releaseId" -Method Patch -Headers $headers -Body ([Text.Encoding]::UTF8.GetBytes($payload)) -ContentType "application/json; charset=utf-8"

Write-Output "Release notes successfully updated!"
Write-Output "URL: $($updated.html_url)"
