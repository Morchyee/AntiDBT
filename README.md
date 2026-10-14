# antiDBT · 独立实时双语字幕

Windows x64 桌面原型，使用系统音频回环、火山引擎豆包流式 ASR 2.0 和豆包机器翻译公开 API。字幕核心不依赖任何厂商。没有复用豆包程序代码、资源、私有接口或凭据。

**当前状态：可运行、可配置、核心与本地协议测试通过；尚未完成真实云端字幕验收。** Seed LiveInterpret 2.0 尚未启用。实际识别质量、真实端到端延迟、数小时连续运行和原版视觉一致性仍待验证。

## 使用

1. 解压整个便携包，双击 `publish/antiDBT-portable-win-x64/antiDBT.exe`，保留同目录配套 DLL。发布包自带 .NET 运行时，无需安装 SDK，建议 Windows 10/11 x64。
2. 点击 ⚙，分别填写 ASR 和机器翻译的 API Key、资源 ID；也可填写旧版 APP ID / Access Token。两项服务需要各自的开通权限，不能把通用云 AK/SK 当作语音服务的 API Key。密钥只在本机设置窗口输入。
3. 保存后点击“开始”，播放希望识别的系统音频。音频仅在开始后发送到所配置的服务；“停止”会处理有期限的尾部字幕，“×”停止并退出。
4. “展开”查看历史；上滚暂停跟随，点击“回到底部”恢复。“原文”“字号”“中/英”调整显示或目标语言。切换目标语言会停止会话，重新开始时清空当前历史。
5. 右键菜单可切换置顶、复制字幕，或运行**本地演示**。演示使用人工编写的合成事件，不采集音频、不访问网络，不证明识别质量。

默认 ASR：`wss://openspeech.bytedance.com/api/v3/sauc/bigmodel_async`，资源 `volc.seedasr.sauc.duration`；默认翻译：`https://openspeech.bytedance.com/api/v3/machine_translation/matx_translate`，资源 `volc.speech.mt`。并发计费资源等可在设置中修改。服务参数依据公开文档；真实账号权限与兼容性尚未验证。

密钥以 Windows DPAPI CurrentUser 加密，设置文件为 EXE 同目录的 `data/settings.json`。同一 Windows 用户仍可解密自己的设置；复制到其他用户或机器后需要重新填写。运行日志与测试报告不包含真实密钥。当前历史仅保存在内存，退出不自动导出音频或字幕。

## 开发与验证

以下命令在源码工作目录执行。需要 .NET 9 SDK 和 WindowsDesktop 编译环境。项目使用已安装 SDK，没有自动安装系统 SDK。首次还原从 NuGet 获取公开依赖，缓存位于 `.packages`。

```powershell
cd F:\Program\antiDBT\prototype
.\scripts\test.ps1                 # 单元、localhost 协议、WPF、核心性能
.\scripts\test.ps1 -HardwareTone   # 另播放约 4 秒低音量测试音，仅记录采集统计
.\scripts\test.ps1 -Soak           # 另执行约 60 秒合成故障/重连运行
.\scripts\publish.ps1             # 自带运行时的 x64 便携目录发布
```

测试服务器仅绑定 localhost，使用虚构密钥。受限沙箱可能禁止 loopback socket；本次已在明确范围获准后运行。网络受限时 NuGet 审计可能出现 NU1900，不能把该警告当作漏洞检查已通过。

文档：[技术架构](docs/ARCHITECTURE.md)、[完成度与证据](docs/COMPLETION-MATRIX.md)、[测试报告](docs/VALIDATION.md)、[待解决问题及续接](docs/CONTINUATION.md)、[公开服务协议依据](docs/PROVIDER-SPECS.md)。第一阶段只读证据位于源码工作目录上一级 `reports`；原始证据和测试 artifacts 不随便携分发包分发，文档中相应链接需在工作目录查看。
