# 国内 MT 实施报告

2026-10-15。实现仅修改任务分配的两个 provider、Providers.csproj、新 Domestic.Tests 与本报告；未修改共享语言表、ProviderHttp、catalog、UI、Core；未提交。根代理持有集成与发布。

## 实现

- `BaiduTranslationProvider` 使用官方 HTTPS POST、显式 appId/secret、密码学随机 salt；UTF-8 原文签名后才表单编码。全部 trans_result 以换行组合，保留段内换行。HTTP/业务错误分层且不回显响应、文本、签名或凭据；54003/54005 为实例建立 3 秒冷却，后续等待受调用者取消与总超时约束。业务错误不叠加重试，HTTP 委托现有最多两次助手。
- `AliyunTranslationProvider` 使用真实 `AlibabaCloud.SDK.Alimt20181012` **2.4.2** 的 `TranslateGeneralWithOptionsAsync`；显式 AccessKeyId/AccessKeySecret，不使用默认凭据链；杭州/新加坡 region 与 endpoint 配对校验。FormatType=text、Scene=general、源/目标/原文；不发送未确认上下文。拒绝空白/超过5000个 UTF-16 code unit 的输入。生产 HTTPS，IgnoreSSL=false，Autoretry=false、MaxAttempts=1。
- SDK 不接收 CancellationToken，调用者 WaitAsync 有界取消；**全进程静态最多两个实际 SDK 调用**，仅物理任务完成后释放许可并观察异常。取消调用者的旧结果不返回；不宣称物理网络立即取消。
- 两者在网络前调用共享语言契约；百度权益语言需显式开关。测试只允许百度 HttpClient loopback seam；阿里内部 URI seam 验证 HTTP loopback，生产构造器拒绝 loopback 配置。根目录 zh-HK→zh-Hant 是显式迁移别名，不能描述成独立香港/粤语支持。

## 执行结果与证据

执行目录 `F:\Program\antiDBT\prototype`：

```powershell
dotnet tests/AntiDbt.Domestic.Tests/bin/Debug/net9.0/AntiDbt.Domestic.Tests.dll --offline
dotnet tests/AntiDbt.Domestic.Tests/bin/Debug/net9.0/AntiDbt.Domestic.Tests.dll
dotnet tests/AntiDbt.Domestic.Tests/bin/Debug/net9.0/AntiDbt.Domestic.Tests.dll --redirect-audit --red
```

- `artifacts/phase4/domestic-offline-green.json`：**42/42**，HttpMessageHandler 夹具，明确不是网络 wire。
- `artifacts/phase4/domestic-localhost-green.json`：**64/64**，包含上述离线覆盖、百度真实 localhost 表单/签名、阿里官方 SDK 真实 localhost HTTP 请求。两者覆盖 en→zh、zh→en、ja→zh、zh→ja、de→en、fr→zh、en→es、auto→繁体；阿里实际使用 **ACS3-HMAC-SHA256 HTTP headers**，不是旧 query HMAC-SHA1。测试独立重建 canonical request、SHA256 payload 与 HMAC，验证显式 fixture AccessKey、Action/Version、text/general/源目标原文；未把注入网关说成 SDK wire。
- `artifacts/phase4/domestic-redirect-audit-red.json`：**64/65**，唯一失败为官方 SDK 跟随 localhost 302，安全要求尚未满足。正常64项绿色明确不包含此单独安全门。
- `artifacts/phase4/domestic-localhost-red.json`：早期 sandbox localhost 请求被阻止（实际网络测试失败20项），不是业务失败证据。随后对已编译测试 DLL 的本机执行获得范围批准，成功执行上述 wire 测试。
- `artifacts/phase4/domestic-localhost-behavior-red.json`：早期实际 SDK wire 54/63，8个断言错误假设旧 query Action/Signature，另一个为真实 redirect 缺口。改正断言后验证 ACS3，而非改写 SDK。
- `artifacts/phase4/domestic-offline-red.json`：初始40/42；错误假设 zh-HK 应被拒绝。共享目录将其规范成繁体，因此该 Aliyun 用例**意外尝试官方 endpoint（仅合成凭据）并以 Network 失败**；无成功鉴权/翻译证据。修正后未知码用 xx-unknown/zh-MO，所有阿里 preflight 用内部 loopback seam，保留原 RED。不能将此早期记录称作完全无外部请求尝试；没有访问真实密钥。

鉴权401/403、限流429、HTTP500有限重试/SDK不重试、业务超时/配置/方向/未开通额度/未知服务错误、空内容、取消、百度冷却等待、阿里超时及取消后真实物理并发2上限均有执行。最后一版成功构建0错误，1条缓存 NU1801（sandbox NuGet 源不可访问）警告。build 使用 --no-restore -m:1 -p:UseSharedCompilation=false。释放 build/restore 时隙后补充了 TeaException 公共 StatusCode 的401/403/429/400 fallback；该小分支尚待根代理下一次编译，64项绿色在此分支之前，不夸大覆盖。

## 已确认的 SDK 限制与可维护方案

`tests/AntiDbt.Domestic.Tests/sdk-transport-il.txt` 保存从已恢复 Tea 1.1.3 DLL 读取的 IL。DLL SHA256：`B0AEEE1EE8DE2E7BBB3F5BD4EE71B0AE7A891B736B4B1BC014605525860CDD80`。使用仓库已有 Mono.Cecil（无新工具安装），只读读取元数据/IL，未改写 DLL。

实际 IL 表明 ReadTimeout 与 ConnectTimeout 为**毫秒**，两者相加传给 CancellationTokenSource(int)；当前各设置有限值，因此实际传输总界约为2×配置秒数，调用者总界为配置秒数。SDK HttpClient 默认100秒界也可能更早触发。无需假设字段单位。

Tea 1.1.3 `CreatHttpClient` 创建 HttpClientHandler 但不设置 AllowAutoRedirect=false，公开 RuntimeOptions/Config 未提供该选项；本机302验证确实跟随。端点预校验只能约束初始目的地，**不能保证无重定向**。未修改 SDK，也未反射改内部全局 client cache；未关闭证书校验。此项应保持发布门失败，由根代理统一决策。

可维护替代方案：保留官方请求模型与官方 `AlibabaCloud.OpenApiUtil.Client.GetAuthorization(TeaRequest, algorithm, payloadHash, accessKeyId, accessKeySecret)` 签名器，将最终 TeaRequest 交给自有 HttpClientHandler(AllowAutoRedirect=false) 传输，并复用现有 response大小/超时/错误助手。这样可同时界定重定向、物理取消与响应大小，但需要正式实现和独立 ACS3/signature、完整本机 wire/错误验证；**本轮未实施，不能描述成已经安全解决**。另一方案为官方未来提供公开传输扩展后固定经过验证的新依赖版本。本 SDK 全量响应读取没有应用层4MiB流式门；不宣称现有 ProviderHttp 门覆盖它。

精确公开签名经已恢复 OpenApiUtil **1.1.2** DLL 元数据核实：

```csharp
public static string GetAuthorization(
    Tea.TeaRequest request,
    string signatureAlgorithm,
    string payload,
    string acesskey,
    string accessKeySecret);
```

参数名 `acesskey` 是库中的原始拼写。IL确认 `payload` 为直接拼入 canonical request 最后一行的**已计算小写十六进制SHA256**，不能传原文；signatureAlgorithm=`ACS3-HMAC-SHA256`。实际已验证的请求规范是 HTTPS POST `/`，空 query；UTF-8 application/x-www-form-urlencoded body包含 FormatType=text、Scene=general、SourceLanguage、TargetLanguage、SourceText。签名前确定所有字节；headers包含 host、x-acs-action=TranslateGeneral、x-acs-version=2018-10-12、UTC x-acs-date、每次新随机 x-acs-signature-nonce、x-acs-content-sha256=body哈希；以官方 signer产生 Authorization 后不能再改变已签名 header/body。TeaRequest.Method/Pathname/Query/Headers 均为公共接口；无需反射或改包。当前 localhost 独立测试从Authorization解析SignedHeaders，根据实际header及原始body重建canonical并用HMACSHA256验证，因此可作为该替代实现的回归基准。签名器方案技术上可行，但会自行承担请求装配、状态/JSON响应、HTTP限流、大小、取消等传输职责，不能只更换一个constructor就宣称完成。

根代理后续反馈：扩展语言测试70/70通过，已包含最终 TeaException StatusCode 分支编译；这是根代理反馈，不是本子任务独立执行的新64项重跑。

## NuGet 与分发许可

固定直接依赖2.4.2；恢复完整树（Providers obj/project.assets.json 可核对）：

| 包 | 实际解析版本 | 包内许可证据 |
|---|---|---|
| AlibabaCloud.SDK.Alimt20181012 | 2.4.2 | nuspec未声明license/licenseUrl，未携LICENSE |
| AlibabaCloud.EndpointUtil | 0.1.2 | 同上 |
| AlibabaCloud.OpenApiClient | 0.1.15 | 同上 |
| AlibabaCloud.OpenApiUtil | 1.1.2 | 同上 |
| AlibabaCloud.SDK.TeaFileform | 0.0.2 | 同上 |
| AlibabaCloud.TeaUtil | 0.1.19 | 同上 |
| AlibabaCloud.TeaXML | 0.0.5 | 同上 |
| AlibabaCloud.GatewaySpi | 0.0.3 | 同上 |
| Tea | 1.1.3 | 同上 |
| Aliyun.Credentials | 1.5.2 | nuspec指向官方GitHub LICENSE URL，包内未携文本；本轮未取得该URL文本 |
| Newtonsoft.Json | 13.0.4 | MIT expression，包内LICENSE.md |
| Microsoft.CSharp | 4.5.0 | 包内MIT LICENSE.TXT及THIRD-PARTY-NOTICES.TXT |

分发时应包含已确定的 MIT 版权许可与 Microsoft 第三方 notice。上述阿里包不能因 requireLicenseAcceptance=false 或其他语言SDK为Apache-2.0而推定许可；精确.NET版本的许可仍未解决，外部分发前应取得官方依据和相应notice。未做漏洞审计（NuGetAudit=false仅用于明确授权恢复，不能称无漏洞）。

恢复因 sandbox proxy socket 127.0.0.1:7897 被拒而请求了仅官方 NuGet 的范围提升，随后成功。第一次误用根目录 `.dotnet-home` 触发 CLI 首次运行，输出“Installed an ASP.NET Core HTTPS development certificate”；这是**意外的系统副作用提示**，未广泛检查/删除证书。随后按根代理指示使用已初始化 `prototype/.dotnet-home`、DOTNET_GENERATE_ASPNET_CERTIFICATE=false、DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1、MSBuildEnableWorkloadResolver=false、DOTNET_CLI_TELEMETRY_OPTOUT=1。不声称整个过程没有系统副作用。

## 官方错误依据

本轮阅读[阿里官方FAQ](https://help.aliyun.com/zh/machine-translation/support/faq-1)及[通用调用指南](https://help.aliyun.com/zh/machine-translation/developer-reference/api-reference-machine-translation-universal-version-call-guide)：10005方向不支持、10012调用服务失败、10010未开通、10013未开通或欠费；纠正初始10012误归类后添加对应执行用例。服务权限/额度/计费、真实云端成功、长期稳定性均未验证。
