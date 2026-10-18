# antiDBT 本地英文语音识别：安装与使用指南

> 适用范围：集成 **sherpa-onnx Streaming Zipformer English 20M** 的 antiDBT Windows x64 版本。
> 本教程只涉及**本地语音识别（ASR）**。把英文转换为中文字幕仍需要配置机器翻译（MT）服务；是否可以完全离线翻译，取决于你选择的翻译 Provider。

## 方案 A：下载完整版（推荐）

1. 打开 https://github.com/Morchyee/AntiDBT/releases ，下载明确标为**包含本地英文识别模型**的 Windows x64 便携 ZIP。不要误把“Source code (zip)”当作可执行程序。
2. 右键解压整个 ZIP，不要直接在压缩包内运行程序。
3. 打开解压后的文件夹，找到并双击 `antiDBT.exe`。
4. 在设置中选择 **本地流式识别 (sherpa-onnx)**（实际选项名称以对应版本界面为准）。如果版本提供模型目录选择器，选择下面示意的 `models\sherpa-onnx-streaming-zipformer-en-20M-2023-02-17`；如果已自动发现模型，则不需要手动选择。
5. 根据需要设置翻译 Provider 和它所需的凭据。仅需英文时，把源语言和目标语言都选为 English（en），此时使用原文直通，不需要任何翻译 API；需要中文字幕时，把目标语言选为简体中文并配置 MT 服务。
6. 播放英文视频、会议音频或网课，再点击 antiDBT 的“开始”，检查原文是否持续出现，随后确认译文正常更新。

> **不需要**安装 Python、Go、CUDA、ONNX 开发工具链或单独下载模型。这个模型可以在 CPU 上运行。

## 完整版目录示例

```text
antiDBT/
├── antiDBT.exe
├── docs/
│   └── LOCAL_ASR_SETUP.zh-CN.md
├── models/
│   └── sherpa-onnx-streaming-zipformer-en-20M-2023-02-17/
│       ├── encoder-epoch-99-avg-1.int8.onnx
│       ├── decoder-epoch-99-avg-1.int8.onnx
│       ├── joiner-epoch-99-avg-1.int8.onnx
│       └── tokens.txt
└── （其他随程序附带的 DLL、资源和第三方许可证）
```

本完整版只附带程序实际使用的 int8 模型与 tokens，不附带测试音频；不要随意改名或移动 `encoder`、`decoder`、`joiner`、`tokens` 四项。程序需要的原生 DLL 由发布包负责提供。

## 方案 B：手动安装模型（仅适用于不含模型的精简包）

1. 从 sherpa-onnx 官方发布页下载模型：
   https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-streaming-zipformer-en-20M-2023-02-17.tar.bz2
2. 使用 7-Zip 等解压工具先解开 `.bz2`，再解开 `.tar`，得到名为 `sherpa-onnx-streaming-zipformer-en-20M-2023-02-17` 的目录。
3. 把整个目录放入 **antiDBT.exe 所在文件夹的 `models` 子目录**。不要多套一层同名目录。
4. 重新启动 antiDBT。选择本地英文 ASR；如果版本支持选择模型目录，指定该目录。

确认最终文件布局类似：

```text
antiDBT\models\sherpa-onnx-streaming-zipformer-en-20M-2023-02-17\tokens.txt
antiDBT\models\sherpa-onnx-streaming-zipformer-en-20M-2023-02-17\encoder-epoch-99-avg-1.int8.onnx
antiDBT\models\sherpa-onnx-streaming-zipformer-en-20M-2023-02-17\decoder-epoch-99-avg-1.int8.onnx
antiDBT\models\sherpa-onnx-streaming-zipformer-en-20M-2023-02-17\joiner-epoch-99-avg-1.int8.onnx
```

## 常见问题

**Q：本地识别需要 Groq API Key 吗？**  不需要。本地 ASR 不调用 Groq。但如果你选择云端翻译 Provider，翻译功能仍需要对应配置。

**Q：可以识别中文吗？**  不能。本教程中的 **en-20M** 是英文专用模型，不是中英双语模型。

**Q：没有字幕怎么办？**  检查是否选择了正确的本地 ASR Provider、`models` 目录是否存在、四个必要模型文件是否完整；检查播放软件是否正在向 Windows 默认输出设备播放音频，以及是否开启了 WASAPI 独占模式。

**Q：双语字幕只有英文，没有中文？**  说明 ASR 与 MT 是两条独立链路。请检查翻译服务配置、网络/API 额度或本地翻译 Provider 的可用性，避免把翻译错误当作识别失败。

**Q：将软件移到其他盘符以后模型找不到？**  应让程序相对 `antiDBT.exe` 的安装目录寻找模型；如果某版本仍保存了绝对模型路径，请重新选择模型目录并保存配置。

**Q：杀毒软件拦截或程序无法启动？**  从项目 GitHub Releases 获取文件、验证随发布提供的 SHA256，再查看 Windows 安全中心告警详情。不要为了运行程序直接关闭安全防护。

## 来源与许可

- sherpa-onnx 官方文档：https://k2-fsa.github.io/sherpa/onnx/pretrained_models/online-transducer/zipformer-transducer-models.html
- 模型上游来源：https://huggingface.co/desh2608/icefall-asr-librispeech-pruned-transducer-stateless7-streaming-small
- 上游模型卡标示 **Apache License 2.0**。打包分发前应核对具体模型文件来源、附带的许可和 NOTICE 条款，随分发物提供对应许可证与归属声明；antiDBT 自身的 MIT 许可证不覆盖第三方模型和运行库。

本文不包含任何 API 密钥、私人设置或开发者电脑路径。


## 首次配置与排障细节

在设置中选择本地流式识别 (sherpa-onnx)，识别语言选 en。模型目录保持 models/sherpa-onnx（程序会扫描 EXE 旁的 models 子目录），或填写 models/sherpa-onnx-streaming-zipformer-en-20M-2023-02-17。目录输入框支持手动填写绝对路径。纯本地模式不要选混合识别模式。保存设置后点击开始，并向 Windows 默认扬声器播放英文。

MT 与 ASR 的 API Key 独立；例如 Azure Translator 填写翻译资源 Key 和区域，或选择已拥有账号的其他翻译服务。不要把语音识别 Key 填到翻译配置中。错误信息可用于检查额度和网络。

模型缺失或损坏：退出程序，重新下载完整 ZIP 并校验 SHA256，再完整解压。确保四个文件非空，保留两个原生文件 sherpa-onnx-c-api.dll 和 onnxruntime.dll。手动下载归档可能与量化完整版布局不同，请保留归档中所有 ONNX 文件。无模型会显示错误；可手动更改目录。

升级时先停止旧程序，将新版解压至新目录；不要把个人 data 文件夹上传给其他人。移动盘符时同时移动整个 antiDBT 文件夹，并优先保留相对模型路径。

## Windows 版本与中文路径

本次便携包支持 Windows 10 1903（Build 18362）及以上版本和 Windows 11。程序清单启用进程级 UTF-8，使 Sherpa-ONNX 原生模型路径能正确处理中文目录；不需要修改 Windows 系统区域或全局代码页。Windows 10 更早版本不在本次验证与支持范围内。
