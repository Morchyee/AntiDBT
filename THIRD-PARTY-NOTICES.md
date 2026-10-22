# Third-party notices

NAudio.Core and NAudio.Wasapi 2.3.0, © Mark Heath 2026, MIT. Package metadata points to https://github.com/naudio/NAudio at commit c89fee940ee6f8d7374d18714a6b85d8b7a18ab0. The application uses the .NET 10 and WindowsDesktop runtime, © .NET Foundation and Contributors. Runtime license and third-party notices are included in `licenses` beside the executable.

NAudio MIT license:

Copyright (c) Mark Heath 2026

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

Phase3 also includes Microsoft.CognitiveServices.Speech 1.52.0 (Microsoft Speech SDK license), Google.Cloud.Speech.V2 1.8.0 and its transitive Google/gRPC/protobuf/authentication dependencies. Package-supplied licenses/notices and exact resolved NuGet metadata are included in `licenses/packages`; `licenses/DEPENDENCIES.json` inventories them. Google Cloud Speech client library uses Apache-2.0. Use of cloud services is separately subject to each service's account, billing and service terms.

For packages without a license text in the NuGet root, `licenses/upstream` also includes protobuf BSD-3-Clause, grpc-dotnet Apache-2.0, and Azure.Core/System.ClientModel MIT texts fetched from the exact repository commits declared by their packages. `licenses/upstream/SOURCES.json` records source URLs and SHA256. NAudio's MIT text is reproduced above; .NET license and third-party notices are included separately.

This independent prototype does not distribute Doubao application binaries, code or assets. Cloud services are separately configured by the user; this software has no bundled service credentials.


## Bundled Sherpa-ONNX English Zipformer Model

The default bundled local English speech recognition model is sherpa-onnx-streaming-zipformer-en-2023-06-21 from the official k2-fsa/sherpa-onnx asr-models release (https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-streaming-zipformer-en-2023-06-21.tar.bz2).

The model is distributed under the Apache License 2.0. The upstream TorchScript model is derived from marcoyang/icefall-libri-giga-pruned-transducer-stateless7-streaming-2023-04-04 with training code from k2-fsa/icefall (PR #984). Unmodified INT8 quantized encoder (ncoder-epoch-99-avg-1.int8.onnx), decoder (decoder-epoch-99-avg-1.int8.onnx), joiner (joiner-epoch-99-avg-1.int8.onnx), and 	okens.txt are included. Model sources and SHA256 checksums are documented in licenses/upstream/ZIPFORMER-2023-06-21-SOURCES.json, the model card is retained in licenses/upstream/ZIPFORMER-2023-06-21-MODEL-CARD.md, and the full Apache-2.0 license text is provided in licenses/upstream/SHERPA-ONNX-APACHE-2.0.txt. antiDBT's MIT license does not relicense the model.

Sherpa-ONNX runtime is distributed under Apache-2.0; package legal metadata is retained in licenses/packages. ONNX Runtime uses MIT; its full license and ThirdPartyNotices are fetched from the upstream v1.28.2 tag, matching the bundled DLL ProductVersion. .NET and WindowsDesktop legal texts come from the SDK runtime packs used for this build.
