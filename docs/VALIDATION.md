# 构建、测试与性能结果

## 第三阶段（2026-10-15）

| 层次 | 实际结果 | 证据 |
|---|---|---|
| 全解决方案构建 | 最终复跑PASS，0编译错误、10个NU1900；初轮为8个。无法获取漏洞元数据，不是审计通过 | artifacts/test-build.log；phase3/full-build.log |
| 核心回归 | 36/36，保留原32项；新增Final抢占不合作预览、唤醒合并等待、排队优先与同修订Final升级 | artifacts/core-tests.json；phase3/final-fixes-core-green.json |
| Windows/密钥 | 19/19；含v1迁移、多个Provider独立保存/删除、密钥目的绑定及选中profile篡改 | artifacts/windows-tests.json；phase3/Windows-green.log |
| 原有火山协议 | 26/26，实际localhost HTTP/WebSocket；原协议算法文件未改 | artifacts/provider-tests.json；phase3/volc-regression.log |
| 新HTTP Provider | 86/86，保留原64项；新增畸形错误响应、Azure中文别名、跨Groq自动会话冷却；含认证/401/403/429/5xx/空响应/超时/网络/模型/语言/取消、VAD/WAV | artifacts/phase3/http-tests.json；final-fixes-http-green.json |
| SDK适配器 | 36/36，官方SDK响应对象与注入式transport；Partial/Final、multi-interim、失败/取消/EOF/资源清理 | artifacts/phase3/CloudSpeech-green.log；sdk-rereview.md |
| Azure本机SDK | PASS，实际原生格式/PushStream/AudioConfig/Recognizer创建、PCM写入与释放；没有Start识别 | artifacts/phase3/native-smoke.log |
| WPF设置/原字幕 | 26/26实际WPF执行；原11项+选择器隔离/保存Result/重开/主机变更/删除/lifetime取消 | artifacts/phase3-ui-review/desktop-smoke.json及PNG |
| 本机组合集成 | 13/13检查，10个真实CaptionPipeline组合，覆盖全部7个用户示例；实际HTTP/multipart/VolcWS | artifacts/phase3/combinations.json；combinations-green.log |
| 性能/合成稳定性 | 最终40000修订/2000历史：6.93s，p95 0.2301ms，托管保留差1186736B；61.88s合成运行3连接/168MT调用/21行，18行精确译文，托管差101248B | artifacts/phase3/final-performance.json；final-soak.json |
| Phase3发布验收 | 最终r4独立目录，release WPF26/26、实际无凭据开始→设置→取消→UI关闭，0残留进程；相同发布命令连续生成r3/r4，EXE哈希一致、旧目录保留 | phase3/final-release-smoke.json；final-native-ui.json；repeat-publish-first/second.json |
| 凭据/提交/包检查 | DPAPI与profile测试通过；源码、测试、文档、Phase3日志及发行二进制未发现已知密钥格式或PEM私钥标记；发行目录无data/settings/pem/pfx/key | phase3/security-scan.json；仅已知格式扫描，不声称识别任意秘密字符串 |
| 保留边界 | 重新SHA2563978个原安装文件，0变化/读取错误；Phase2 EXE/ZIP哈希不变，原Volc协议算法文件不变 | phase3/preservation.json；original-integrity.json |
| 真实云 | **未执行**，没有读取真实Key/JSON；各Provider认证/区域、真实首字延迟/质量、账单/小时级稳定性未知 | 本轮网络测试仅localhost及公开文档/依赖下载 |

本机组合的Azure/Google使用注入的SDK session gateway，包含实际localhost HTTP transport，但不是Azure native云事件或Google gRPC-wire服务器。SDK对象测试使用真正StreamingRecognizeResponse/RpcException转换；仍不能等同官方云服务验收。所有4×6配置工厂有构造检查，实际管线运行的是10组合，不把24工厂构造声称为24端到端组合。

完整脚本 `scripts/test.ps1 -SkipRestore -Soak` 已执行通过，包含Core、Windows、原Volc、新HTTP、SDK、实际localhost组合、原生SDK资源、WPF、Core吞吐及约62秒合成故障运行。续接后按用户要求再执行一次；结果与退出码保存在 `artifacts/phase3/resumed-full-validation.log`。整体验收3项P2及Azure别名已集中修复、行为RED→GREEN且 `phase3/final-rereview.md` 批准。发布脚本重复运行自动选择空闲编号目录，不覆盖任何既有输出；r2构建时会话进程句柄失效，但日志/产物齐全，最终以两次返回0的r3/r4执行与r4启动证据为准。

原协议算法文件保持不变，核心接口及进程内事件ID语义保持；项目原本没有独立IPC。约一分钟soak和Core吞吐不代表新服务的长时质量。硬件回环在Phase2已验证，本轮未因只扩展Provider重复播放测试音。SDK/HTTP初轮审查发现的问题已记录RED→修复→GREEN；并发冷却首次修复后的10/20ms测试存在响应排序抖动，现改为双请求屏障和受控返回顺序，不隐藏原失败日志。

下面保留Phase2历史结果，不能当作本轮重新执行的证据。

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
