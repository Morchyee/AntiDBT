# 功能完成度与复刻证据

评定日期：2026-10-14。这里的“已验证”指本项目的列明测试，**不等于原版运行一致或真实云服务验收**。不采用无法解释的总体百分比。

第一阶段依据：[功能规格](../../reports/FEATURES-AND-PRIORITIES.md)、[证据索引](../../reports/EVIDENCE-INDEX.md)、[JS01–20](../../analysis/subtitle-source-evidence.md)、[S01–24](../../reports/subtitle-settings.md)、[原生 N](../../reports/native-audio.md)。静态证据不作为实际画面的观测。

| 优先级 / 功能 | 实现与状态 | 规格 / 本项目证据 |
|---|---|---|
| P0 系统回环、设备选择 | 已实现；真实非零回环验证，选择器与默认设备接口具备；不同硬件待测 | N；Windows AudioSources；hardware-tone.json |
| P0 下混、重采样、S16LE | 已实现并测试 16/24/32 PCM、float32、44.1/48k、跨回调一致性；语音音质待测 | N；windows-tests.json |
| P0 环形、分块、有界队列 | 640000 B、6400 B、各层 FIFO50、丢旧、尾块与时间轴回归通过 | JS05/06/07/12、N；Windows/Core tests |
| P1 静音检测与发送控制 | 已实现纯零 7 秒抑制及 2 秒零帧保活；非语义 VAD，云端保活待实测 | JS04/05；CaptionPipeline |
| P0 流式发送、断线缓存恢复 | 已实现、合成断线恢复通过；恢复按帧节流，未照搬原版十块合包 | JS07/12；Core reconnect test / soak.json |
| P0 释放、异常、尾部处理 | RecordingStopped 后完成、取消、并发生命周期及句尾预算测试通过；驱动极端故障仍有界限 | JS06/11、N；Windows/Core tests |
| P0 流式 ASR 2.0 | 已实现公开适配器，localhost 协议验证；真实鉴权、模型权限与识别质量未验证 | PROVIDER-SPECS；provider-tests.json |
| P0 双轨与原文优先 | 已实现；原文不等待 MT，人工修订和界面测试通过。原文优先属于独立体验选择 | JS03/14；Core/UI tests |
| P0 增量覆盖、删除和对齐 | 已实现本地 revision、累计 provisional suffix 调和、split/merge tombstone；真实 utterance 形状待确认 | JS03；provider shifted/split/merge tests |
| P0 防过期翻译 | 精确修订与 generation 守卫，草稿独立标记，不覆盖更新译文；迟到/取消测试通过 | Core Store/Scheduler tests |
| P0 断句、段落 | 使用 ASR definite、时间戳和客户端标点/长度投影；不等同原版云端断句 | JS14/16；SubtitleProjection/Core tests |
| P0 重连状态隔离 | 已实现；保留历史、丢弃旧 session 回调、缓冲发送恢复测试通过 | JS03/13/16；Core / soak |
| P1 字幕历史与滚动 | 默认最多 2000 条，虚拟列表、上滚暂停、底部恢复，UI 测试通过；没有持久字幕导出 | JS15/16；11 项 UI tests |
| P0 暗色悬浮窗、紧凑/展开 | 已实现 WPF 两模式；600 DIP、圆角、颜色与字号按静态证据配置。未完成原版逐像素比较 | S/CSS；desktop-final/*.png |
| P1 长句、双语布局、自动尺寸 | 每轨最多两行并显示尾部；宽度可调、字体/内容适应高度；展开固定视区，跨屏 DPI 未验 | JS14/20、S；UI long sentence test |
| P0 字号、zh/en、原文开关 | 已实现，配置持久化；目标切换停止并在下次开始清空历史 | JS02/13、S；MainWindow/SettingsStore |
| P1 拖动、置顶、交互区 | 工具栏拖动、文本可范围选择/复制、Topmost 及设置开关；独占全屏/多屏未验 | S；WPF code / own native UI observation |
| P1 设置面板 | 已实现独立模态设置窗口，API Key/旧认证、资源/地址、设备、显示选项；与原版小浮层的布局/焦点行为不同 | S；desktop-settings.png |
| P0 错误、重试、关闭 | 状态消息、开始/停止/重试、采集/识别错误、翻译错误、期限 drain；原版独立错误页未逐样复现 | JS17/18、S；Core/Windows tests |
| P0 可替换 Provider | Core 仅依赖接口；当前产品包含火山 ASR2/MT 两个正式适配器。新增厂商需写适配器并增加设置选择 | Contracts / ProviderCatalog |
| P0 API 配置与密钥 | 全部通过设置界面；DPAPI CurrentUser、普通 JSON 排除秘密、原子更新及损坏恢复测试通过 | SettingsStore / Windows tests |
| P0 翻译调度、取消、去重 | 固定 350 ms 合并、2 并发、超时、128 缓存、128 待办、过期拒绝；不合作插件最多16未完成请求 | Core Scheduler tests |
| P1 上下文管理 | 核心保存最近 final 上下文；当前 MT 公开协议无上下文字段，**不发送也不宣称云端上下文功能复刻** | PROVIDER-SPECS / Scheduler |
| P0 延迟监测 | 已显示最近 1000 样本 p95；ASR 为采集块/服务音频时间估算，MT 为调用时长，非声学到屏幕精确测量 | LatencyMonitor / performance.json |
| P1 长期稳定 | 有界结构、故障回归及约62秒合成运行通过；数小时真实音频/服务稳定性未验证 | soak.json / known issues |
| 可选 Seed LiveInterpret 2.0 | 未接入、明确门控；国内原文/译文/增量 protobuf 语义尚未核实 | PROVIDER-SPECS / catalog gate test |
| P2 自动播放提醒、全局快捷键 | 本轮没有实现；触发规则/原版键位未验证，也不在本轮用户列出的核心范围内 | S22–24 / UNKNOWN |
| 云内 ASR、VAD、上下文修正和翻译模型 | 使用所选第三方能力；不复用、推断或声称复刻豆包云内部机制 | 第一阶段未知边界 |

## 后续优先级与责任边界

1. **P0 真实 API 闭环验收**：用户在软件设置填写自己的 API 配置后，用公开无私人内容的音频核实首字、partial/final、翻译及时性、重切分、停止尾句及额度错误。无需聊天传递密钥。
2. **P0 真实服务修正样本**：先获同意保留脱敏文本/时间戳测试夹具，再验证累计形状、水位与跨定稿边界；不记录认证头或私人录音。公开协议支持之外的形状目前会错误重连。
3. **P1 原版视觉与交互观察**：只有明确返回的字幕窗口才可只读观察；核对行尾、拖区、设置浮层、错误页、多屏和全屏。当前不能声称一致性完成。
4. **P1 长时体验**：1–8 小时真实运行、网络反复断开、默认输出设备切换、DPI/显示器切换。必要时改进重采样质量和重连策略；均可自主实现。
5. **可选 LiveInterpret**：必须取得官方国内协议和双轨事件语义，先做 localhost 协议夹具，再用独立 Provider 进行 A/B 测试。服务端模型内部机制仍不可获取。
