# Phase 3 官方流式 SDK 核对

核对日期：2026-10-14。仅公开文档研究；未安装包、未读取凭据、未调用云服务。以下是实现轮廓，不是已编译/已联网验证的代码。

## 版本

| NuGet 包 | 当前稳定版本 | 官方证据 |
| --- | --- | --- |
| Microsoft.CognitiveServices.Speech | 1.52.0（2026-09-28） | [NuGet](https://www.nuget.org/packages/Microsoft.CognitiveServices.Speech/) |
| Google.Cloud.Speech.V2 | 1.8.0（2026-02-19） | [NuGet](https://www.nuget.org/packages/Google.Cloud.Speech.V2) |

Azure 部分 Microsoft Learn API 页面仍标记 1.51.0；安装 1.52.0 后必须以实际编译确认签名和 Windows 本机运行时资源。现有 Providers 项目为 net9.0，尚无这两个 PackageReference。

## Azure AI Speech

输入采用无 WAV 头的 signed PCM16 little-endian、16 kHz、mono。官方音频格式依据：[输入流规范](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/how-to-use-audio-input-streams)。

关键公开 API（省略命名空间限定）：

```csharp
// AudioInputStream
public static PushAudioInputStream CreatePushStream(AudioStreamFormat format);
// PushAudioInputStream
public void Write(byte[] dataBuffer);
public void Write(byte[] dataBuffer, int size);
// SpeechRecognizer
public Task StartContinuousRecognitionAsync();
public Task StopContinuousRecognitionAsync();
```

签名依据：[CreatePushStream](https://learn.microsoft.com/en-us/dotnet/api/microsoft.cognitiveservices.speech.audio.audioinputstream.createpushstream?view=azure-dotnet)、[Write](https://learn.microsoft.com/en-us/dotnet/api/microsoft.cognitiveservices.speech.audio.pushaudioinputstream.write?view=azure-dotnet)、[SpeechRecognizer](https://learn.microsoft.com/en-us/dotnet/api/microsoft.cognitiveservices.speech.speechrecognizer?view=azure-dotnet)。Write 内部复制数据；ReadOnlyMemory 输入需转成 byte[]，或者在边界显式管理缓冲区。

```csharp
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;

var config = SpeechConfig.FromSubscription(key, region);
config.SpeechRecognitionLanguage = language; // 例如 en-US
using var format = AudioStreamFormat.GetWaveFormatPCM(16000, 16, 1);
using var push = AudioInputStream.CreatePushStream(format);
using var audio = AudioConfig.FromStreamInput(push);
using var recognizer = new SpeechRecognizer(config, audio);
recognizer.Recognizing += (_, e) => OnPartial(e.Result);
recognizer.Recognized += (_, e) => {
    if (e.Result.Reason == ResultReason.RecognizedSpeech) OnFinal(e.Result);
    // NoMatch 不作为空 final 覆盖已有字幕。
};
recognizer.Canceled += (_, e) => OnCanceled(e.Reason, e.ErrorCode, e.ErrorDetails);
recognizer.SessionStopped += (_, _) => OnStopped();
await recognizer.StartContinuousRecognitionAsync();
push.Write(pcmBytes); // SendAudioAsync，串行写入
push.Close();        // CompleteAsync：输入 EOF
// 等待 SessionStopped / Canceled 或有界超时，再停止和释放。
await recognizer.StopContinuousRecognitionAsync();
```

Recognizing 是中间结果；Recognized 是最终结果。Canceled 的 Error 原因应终止结果流并传播脱敏错误；正常 SessionStopped 则完成通道。不要在 SDK 事件回调中阻塞或使用不可观察异常的 async void。用结果 OffsetInTicks + Duration 映射音频时间；保持同一句 partial/final 的本地 SegmentId，Revision 递增；ResultId 是否在同句各事件间保持一致需实测，不应先作保证。API 事件/停止行为依据：[SpeechRecognizer](https://learn.microsoft.com/en-us/dotnet/api/microsoft.cognitiveservices.speech.speechrecognizer?view=azure-dotnet)。

认证输入为资源 key + 对应 region，或者明确配置的 endpoint；不要把 region 当作任意 URL。避免记录 key 和原始 ErrorDetails 中可能含有的敏感数据。

## Google Cloud Speech-to-Text V2

真实方法是双向 gRPC，不能用 REST transport 替代：[SpeechClient](https://docs.cloud.google.com/dotnet/docs/reference/Google.Cloud.Speech.V2/latest/Google.Cloud.Speech.V2.SpeechClient)。

```csharp
public virtual SpeechClient.StreamingRecognizeStream StreamingRecognize(
    Google.Api.Gax.Grpc.CallSettings callSettings = null,
    Google.Api.Gax.Grpc.BidirectionalStreamingSettings streamingSettings = null);
```

请求资源名为 `projects/{project}/locations/{location}/recognizers/{recognizer}`；`_` 可用作隐式 recognizer。首包发送 Recognizer + 完整 StreamingConfig，不带 Audio；随后仅发送 Audio。依据：[StreamingRecognizeRequest](https://docs.cloud.google.com/dotnet/docs/reference/Google.Cloud.Speech.V2/latest/Google.Cloud.Speech.V2.StreamingRecognizeRequest)。

```csharp
using Google.Cloud.Speech.V2;
using Google.Protobuf;
using Google.Api.Gax.Grpc;

var client = await new SpeechClientBuilder {
    Endpoint = endpoint,
    CredentialsPath = serviceAccountJsonPath // 或省略，使用 ADC
}.BuildAsync(token);
using var call = client.StreamingRecognize(CallSettings.FromCancellationToken(token));
await call.WriteAsync(new StreamingRecognizeRequest {
    Recognizer = $"projects/{project}/locations/{location}/recognizers/_",
    StreamingConfig = new StreamingRecognitionConfig {
        Config = new RecognitionConfig {
            ExplicitDecodingConfig = new ExplicitDecodingConfig {
                Encoding = ExplicitDecodingConfig.Types.AudioEncoding.Linear16,
                SampleRateHertz = 16000, AudioChannelCount = 1
            },
            LanguageCodes = { language }, Model = model
        },
        StreamingFeatures = new StreamingRecognitionFeatures { InterimResults = true }
    }
});
// 启动独立接收任务；与下面的写循环并行运行。
// await foreach (var response in call.GetResponseStream().WithCancellation(token))
//     foreach (var result in response.Results) OnResult(result);
await call.WriteAsync(new StreamingRecognizeRequest {
    Audio = ByteString.CopyFrom(pcmBytes)
});
await call.WriteCompleteAsync(); // 半关闭写端后排空响应，再 Dispose
```

PCM 必须使用 ExplicitDecodingConfig；AutoDetect 不适用于没有头部的裸 PCM。[解码字段](https://docs.cloud.google.com/dotnet/docs/reference/Google.Cloud.Speech.V2/latest/Google.Cloud.Speech.V2.ExplicitDecodingConfig)。接收每个 response 的全部 Results；Alternatives 可能为空，IsFinal 决定最终性，Stability 仅用于 interim；ResultEndOffset 是相对当前流的音频位置。V2 没有可直接作为业务 SegmentId 的结果 id，使用本地 utterance 序号，最终结果后推进序号。[结果字段](https://docs.cloud.google.com/dotnet/docs/reference/Google.Cloud.Speech.V2/latest/Google.Cloud.Speech.V2.StreamingRecognitionResult)。

Project、location、model、language 必须显式配置且组合受服务支持；global 使用 speech.googleapis.com，区域使用与 location 匹配的区域 endpoint（例如 us-east1-speech.googleapis.com），不能把 Azure region 复用到 Google。官方区域 endpoint 示例见 [模型使用文档](https://docs.cloud.google.com/speech-to-text/docs/custom-speech-models/use-model)。[Builder](https://docs.cloud.google.com/dotnet/docs/reference/Google.Cloud.Speech.V2/latest/Google.Cloud.Speech.V2.SpeechClientBuilder) 可配置 endpoint 和凭据。ADC 可从 GOOGLE_APPLICATION_CREDENTIALS 指定的 service-account JSON 获取凭据；无需把 JSON 内容存入设置文件。[ADC](https://docs.cloud.google.com/docs/authentication/application-default-credentials)。认证与 project/resource 配置分离；需要启用 API、计费和相应识别权限，离线测试不能证明这些条件。

单流上限为五分钟，音频发送速度应接近实时；实现应提前结束（例如 4 分 50 秒，属于本地策略），明确提示或轮换下一流，不等待服务强制断开。轮换必须累计时间基准且处理旧流迟到结果；重放重叠音频会引入重复字幕，不能未经测试直接加入。当前 [配额页](https://docs.cloud.google.com/speech-to-text/docs/quotas) 写每包 25 KB，但上述 1.8.0 Request.Audio 文档仍写 15 KB：保守采用 <=15,000 bytes，建议 3,200 bytes（100 ms PCM），避免依赖文档冲突中的较大值。

## 离线注入与验证边界

沿用 Core 的 IAsrProvider / IAsrSession，使用小型内部 transport/session factory 隔离 SDK；Azure 的 sealed SpeechRecognizer 不适合直接 mock。测试替身只替代 SDK I/O，不能把脚本结果称为云识别验证。

- Azure seam：Start/Write/Close/Stop/Dispose + partial/final/canceled/stopped 事件；测 raw PCM 原样传递、EOF 后 final 排空、错误完成、重复 stop 和回调迟到。
- Google seam：写 request、完成写端、异步 response、取消/释放；测首包无音频、后包无配置、分包 <=15,000、接收与发送并行、多个 Results、空 Alternatives、IsFinal、RpcException 脱敏传播。
- 注入 TimeProvider 或流期限策略验证五分钟之前停止、取消打断阻塞读写、轮换时间基准与无重复 final；虚拟时间测试只证明策略。
- 之后真正安装固定版本并编译、检查 Azure Windows 本机依赖与发布包，再进行明确授权的真实 PCM/云账户 smoke test；本文件不声称这些验证已通过。
