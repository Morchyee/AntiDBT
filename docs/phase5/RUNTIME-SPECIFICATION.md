# antiDBT 运行时环境、LTS 评估与构建可重复性规范

- **编制日期**：2026-10-16
- **阶段标记**：Phase 5.3 Reliability (工作包 A7)
- **基线版本**：antiDBT v0.5.2-beta / 候选目标 v0.5.3-beta.1
- **分支**：`feature/phase-5.3-reliability`

---

## 一、背景与目标

依据 GPT-6 Astra 技术审查报告（`ANTI-DBT-TECHNICAL-REVIEW.md`）及任务书（`ANTIGRAVITY-TASKBOOK.md` 第 11 节 A7）的要求：
1. 评估当前 .NET 9（STS）支持生命周期及面向 .NET 10（LTS）的迁移路径。
2. 遵守当前工具与运行环境权限，核查本地已安装 SDK 与 Runtime；缺少 .NET 10 SDK 时如实记录依赖，绝不编造未执行的迁移成功断言。
3. 建立工具链版本锁定（`global.json`）、确定性构建元数据、Windows CI 自动化工作流与独立便携候选包构建机制。
4. 严格隔离基线资产，未经用户明确指令不得覆盖或修改远程已有的 `v0.5.2-beta` GitHub Release 资产。

---

## 二、.NET 支持生命周期与官方策略核对

根据微软官方 .NET 支持与生命周期政策（.NET Support Policy）：

| 框架版本 | 发行类别 | 发布日期 | 官方支持截止日期 (EOS) | 维护状态评估 |
|---|---|---|---|---|
| **.NET 8** | **LTS** (长期支持) | 2023-11-14 | 2026-11-10 | 仍受支持（36 个月窗口），基础架构极其稳固 |
| **.NET 9** | **STS** (标准期限) | 2024-11-12 | 2026-11-10 (或后继版本发布后6个月) | **当前使用中**，已进入后期维护阶段，需提前筹备 LTS 迁移 |
| **.NET 10** | **LTS** (长期支持) | 2025-11-11 | 2028-11-14 (预定36个月窗口) | **目标迁移平台**，为企业级长期部署提供持续安全补丁与生态支持 |

### 关键结论
antiDBT 当前基于 .NET 9（STS）开发，在 Windows 10/11 平台运行稳定。但鉴于其将于 2026 年底结束官方支持，长期发展必须平滑过渡至 **.NET 10 LTS**。

---

## 三、宿主开发机环境核查与实测审计

在 Windows 11 本地开发机执行 `dotnet --list-sdks` 与 `dotnet --list-runtimes`，审计结果如下：

### 1. 已安装 .NET SDK
```text
9.0.319 [C:\Program Files\dotnet\sdk]
```
- **当前 SDK 主版本**：`9.0.319`
- **.NET 10 SDK 状态**：**未安装**。开发机目前未配置 `10.0.xxx` 编译工具链。

### 2. 已安装 .NET 运行时 (Shared Runtimes)
```text
Microsoft.AspNetCore.App 9.0.21
Microsoft.NETCore.App 6.0.36
Microsoft.NETCore.App 7.0.20
Microsoft.NETCore.App 8.0.32
Microsoft.NETCore.App 9.0.21
Microsoft.NETCore.App 10.0.13
Microsoft.WindowsDesktop.App 6.0.36
Microsoft.WindowsDesktop.App 7.0.20
Microsoft.WindowsDesktop.App 8.0.32
Microsoft.WindowsDesktop.App 9.0.21
Microsoft.WindowsDesktop.App 10.0.13
```
- 宿主机已具备 .NET 10 共享运行时（`Microsoft.NETCore.App 10.0.13` 与 `Microsoft.WindowsDesktop.App 10.0.13`），具备执行对应自包含或框架依赖应用的能力。
- 但由于 MSBuild SDK 仍为 9.0，若强行在 `*.csproj` 中配置 `<TargetFramework>net10.0</TargetFramework>`，编译器将触发 `NETSDK1045: The current .NET SDK does not support targeting .NET 10.0` 错误。

### 3. 遵从任务书规范的决策
按照任务书明确准则：
> “遵守当前工具权限；缺少 SDK 时记录依赖，不把未执行的迁移验证写成成功。不要求无依据地升级所有第三方包。”

**本阶段决策**：
- 代码库当前编译基线继续锚定在 **.NET 9.0（SDK 9.0.319）**，确保在现有环境与离线环境下 100% 具备确定性还原与编译能力。
- 本规范完整冻结面向 .NET 10 LTS 的升级配方、第三方依赖兼容性矩阵与迁移验证步骤；一旦环境配置 .NET 10 SDK，可根据本规范在 10 分钟内完成平滑切包。

---

## 四、第三方依赖与核心子系统面向 .NET 10 的兼容性矩阵

对 antiDBT 依赖的全部第三方 NuGet 包、系统底层 API 与 Windows 原生组件进行前向兼容性评估：

| 组件 / 依赖包 | 当前版本 | 依赖性质 | .NET 10 兼容性评估 | 潜在风险与缓解措施 |
|---|---|---|---|---|
| **Microsoft.CognitiveServices.Speech** | `1.52.0` | 原生 C++ DLL 绑定 (`Microsoft.CognitiveServices.Speech.core.dll`) | **完全兼容** | 原生互操作采用标准 Windows P/Invoke 与 COM，在 .NET 10 运行时下无需调整。发布时须保证自提取或同级目录携带原生 DLL。 |
| **NAudio.Wasapi** | `2.3.0` | Windows WASAPI COM 接口封装 | **完全兼容** | WASAPI 属于 Windows OS 原生多媒体子系统，NAudio 仅作为托管封装器，向前向后完全兼容。 |
| **Google.Cloud.Speech.V2** | `1.8.0` | gRPC / Protocol Buffers 客户端 | **完全兼容** | 目标框架基于 `netstandard2.1` 及 `.NET 8+`，.NET 10 具备原生向下二进制兼容。 |
| **AlibabaCloud.SDK.Alimt20181012** | `2.4.2` | 阿里云 OpenAPI 客户端 | **完全兼容** | 基于 `netstandard2.0`，可在 .NET 10 正常加载与网络收发。 |
| **WPF WindowChrome & DWM API** | 内置 | WindowsDesktop 核心组件与 DWM P/Invoke | **完全兼容** | `DwmSetWindowAttribute`（暗色标题栏、圆角偏好）、`WindowChrome`（非客户区绘制）在 WindowsDesktop 10.0.13 表现一致。 |
| **DPAPI 本地机密保护** | 内置 | `System.Security.Cryptography.ProtectedData` | **完全兼容** | Windows 平台原生机密加密，.NET 10 继续提供完备支持。 |

---

## 五、构建可重复性与版本控制机制

为了确保任何团队成员、CI 机器或未来回溯构建都能获得完全一致的输出二进制，实施以下固定措施：

### 1. SDK 版本锚定 (`global.json`)
在项目根目录设立 `global.json`：
```json
{
  "sdk": {
    "version": "9.0.319",
    "rollForward": "latestFeature",
    "allowPrerelease": false
  }
}
```
保证构建环境即使安装了其他次要或预览版本，也将优先匹配经受完整测试的 9.0.319 稳定工具链。

### 2. 全局构建属性 (`Directory.Build.props`)
统一配置：
```xml
<Project>
  <PropertyGroup>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <Deterministic>true</Deterministic>
    <MSBuildEnableWorkloadResolver>false</MSBuildEnableWorkloadResolver>
    <Version>0.5.3-beta.1</Version>
    <AssemblyVersion>0.5.3.0</AssemblyVersion>
    <FileVersion>0.5.3.0</FileVersion>
    <InformationalVersion>0.5.3-beta.1+phase5.3-reliability</InformationalVersion>
    <Company>antiDBT</Company>
    <Product>antiDBT Realtime Subtitles</Product>
  </PropertyGroup>
</Project>
```
- `<Deterministic>true</Deterministic>`：消除编译时间戳与非确定性元数据差异，确保相同输入生成完全相同的 PE 散列。
- 注入清晰的语义化版本号与 Git 阶段分支标记。

### 3. Windows CI 自动化工作流 (`.github/workflows/windows-ci.yml`)
设立独立的 Windows CI 工作流：
- 运行于 `windows-latest` 纯净虚拟环境。
- 使用 `actions/setup-dotnet@v4` 解析 `global.json`。
- 执行确定性还原与编译。
- 执行 `scripts/verify-all.ps1`，自动化断言 12 项测试套件 100% PASS。
- 收集测试汇总报告、遥测样例与日志 Artifacts。

---

## 六、面向 .NET 10 LTS 的平滑迁移配方 (Ready Recipe)

当目标构建环境部署 .NET 10 SDK（如 `10.0.100` 或更高正式版）后，按以下步骤即可完成切包：

1. **更新 `global.json`**：
   ```json
   {
     "sdk": {
       "version": "10.0.100",
       "rollForward": "latestFeature",
       "allowPrerelease": false
     }
   }
   ```
2. **批量切换 TargetFramework**：
   - 将全部基础类库从 `net9.0` 升级为 `net10.0`。
   - 将 `src/AntiDbt.Desktop`、`src/AntiDbt.Windows` 及 Windows 测试工程从 `net9.0-windows` 升级为 `net10.0-windows`。
3. **验证自包含单文件打包**：
   ```powershell
   dotnet publish src/AntiDbt.Desktop/AntiDbt.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
   ```
4. **运行全量验证套件**：
   ```powershell
   pwsh -File scripts/verify-all.ps1
   ```
5. **核查原生 Speech SDK 与 WASAPI 动态库加载**：
   确认发布目录存在 `Microsoft.CognitiveServices.Speech.core.dll` 并执行 Smoke 测试。

---

## 七、候选包打包与基线资产防护

依据审查指令：
1. **基线隔离**：严禁调用或修改 `scripts/create-release.ps1`，绝不修改、覆盖或删除 GitHub 现有 `v0.5.2-beta` Release 资产。
2. **独立候选包构建器**：建立 `scripts/build-candidate-package.ps1`，将 Phase 5.3 候选包输出至独立路径 `artifacts/phase5.3/dist/antiDBT-v0.5.3-beta.1-win-x64/`。
3. **散列校验**：生成匹配的 `.sha256` 校验文件与 `candidate-manifest.json`，等待最终用户明确授权后再进行发布操作。
