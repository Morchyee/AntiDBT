# 构建、测试与性能结果

日期：2026-10-14。详细机器可读结果保存在 `prototype/artifacts`；测试与报告区分合成、localhost、实际硬件和真实云端。

| 验证 | 结果 / 范围 | 证据 |
|---|---|---|
| Core | 32/32；修订、旧结果、generation、草稿、删除、上下文缓存、持续修订、失败后新 final、重连、延迟尾句、并发释放、释放失败重试、纯零抑制及非零恢复 | artifacts/core-tests.json |
| Windows | 15/15 回归组；转换/有界缓冲/资源/DPAPI/损坏恢复；先前额外空闲硬件检查也通过但当时为0字节 | artifacts/windows-tests.json；tests/AntiDbt.Windows.Tests/review-*.log |
| Providers | 26/26，实际 localhost HTTP/WebSocket；虚构认证，分片/gzip/扩展头/恶意大小/修正/拆并/final/取消/源语言映射 | artifacts/provider-tests.json |
| WPF | 调试构建及最终便携发布均 11/11；原文先显示、待译/草稿、长句尾部、历史、手动滚动/恢复、设置、演示；测试模式不读写实际设置 | artifacts/release-portable-final/desktop-smoke.json 与同目录 PNG |
| 实际回环音频 | PASS；4秒低音量内存测试音，最近一次121600 B、60476个非零样本，RMS约0.0141；未保存或发送录音 | artifacts/hardware-tone.json |
| 核心吞吐 | 40000次合成修正，历史保持2000；最近一次约5.83秒，p50 0.155ms、p95 0.186ms、最大3.25ms；保留托管内存差约1.19MB | artifacts/performance.json |
| 合成持续运行 | PASS；约62.07秒、3次连接（2次故障重连）、166次抖动/失败翻译调用、21行历史其中18行有精确译文，保留托管内存差约114KB | artifacts/soak.json |
| 原版完整性 | 3978文件重新SHA256，0变化、0读取错误；只读遍历第一阶段排除私人状态后的清单 | artifacts/original-integrity.json |
| 原版画面观察 | 没有取得可观察的独立字幕窗口；能列到豆包主窗口不能代替字幕窗 | artifacts/original-visibility.json / native-ui.json |
| 原型原生交互 | Sky读取真实控件、点击展开并观察“收起”和列表，关闭后无原型窗口；第一次无截图几何的点击失败，取得截图后重试成功 | artifacts/native-ui.json；最终画面使用独立WPF渲染图避免背景内容 |
| 独立发布 | win-x64、自带.NET运行时，主EXE与5个配套本机DLL放在同一便携目录；最终启动11/11、退出码0；不得只复制EXE | artifacts/publish-final.log / release-manifest.json |
| 云服务 | **未测试**：未提供/读取任何真实凭据；没有真实 ASR/MT请求 | 本次所有网络测试仅localhost |

## 证据边界

性能数值是本机 synthetic Core 路径的耗时，不含 WASAPI 实时调度、网络、云 ASR/MT 或 WPF 帧。内存差是强制 GC 后托管堆差，不是进程峰值、完整泄漏证明或跨机器性能保证。约一分钟运行不是长期稳定验证。

硬件测试证明这台机器默认输出上的非零 PCM 可经真实 WASAPI→重采样→16k PCM 路径输出；它不能证明实际语音清晰度、模型兼容性或识别准确率。原版静态代码色值/尺寸已经应用，但没有原版运行图作对照，不能给出视觉一致率。

初始 SDK 默认环境检查遇到 SCM 权限错误；构建禁用 workload resolver 后正常。默认沙箱禁止 localhost socket（10013），Provider 集成测试随后在明确的 localhost 范围获准运行。独立发布需要官方运行时包，受限网络还原失败后获准从 NuGet 获取，未安装系统 SDK。最后一次联网发布请求因审批服务返回额度错误而未执行；改用已获组件离线发布，不代表用户实际额度不足。全单文件版的启动在沙箱临时目录重命名时返回EACCES/0x8000809f，未生成测试报告；保留了host-trace，不计为通过。最终便携版将原生DLL放在EXE旁，避免临时自解压。部分受限还原缓存报告 NU1900，表示漏洞元数据获取失败；没有将其记为漏洞审计通过。MSBuild格式化因沙箱命名管道权限失败后，使用不加载项目的文件夹格式化成功。

RED 记录分别位于 artifacts/core-tests-red.json、scheduler-red.txt、pipeline-red.txt、core-review-red.txt、core-finalqueue-red.txt，以及 Windows/Providers 测试目录中的 review-red / behavioral-red / language-red / suffix-red。部分初始 Provider 测试在实现后添加，报告没有声称全面最初 TDD。审查文件保留发现当时的问题；最新解决状态看 final-review.md 和本报告。

复跑：`scripts/test.ps1`（需允许localhost）、`-HardwareTone`、`-Soak`。发布：`scripts/publish.ps1`。不读取或复用用户保存的 API 配置来自动调用付费服务。
