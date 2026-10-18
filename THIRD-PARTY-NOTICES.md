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


## Bundled Sherpa-ONNX English Zipformer 20M model

The unmodified int8 encoder, decoder, joiner and tokens are from the official k2-fsa/sherpa-onnx asr-models release. The exported model card (csukuangfj) declares Apache-2.0 and credits desh2608/icefall-asr-librispeech-pruned-transducer-stateless7-streaming-small. Both sources are recorded in licenses/upstream/ZIPFORMER-20M-SOURCES.json with SHA256 hashes verified against the downloaded official archive. The archive contains no LICENSE or NOTICE file. We retain the upstream model card and supply the full Apache-2.0 text in licenses/upstream/SHERPA-ONNX-APACHE-2.0.txt. Only required model files are redistributed; test audio and export scripts are omitted. antiDBT's MIT license does not relicense the model.

Sherpa-ONNX runtime is distributed under Apache-2.0; package legal metadata is retained in licenses/packages. ONNX Runtime uses MIT; its shipped ThirdPartyNotices and license are retained from the runtime package. .NET and WindowsDesktop legal texts come from the SDK runtime packs used for this build.
