# Provider protocol notes

Research date: 2026-10-14. Public official documentation only; no credentials or paid calls used. Search-indexed official pages were readable; direct ASR page retrieval returned an empty JavaScript shell and Firecrawl retrieval failed. Consequently this is a documented contract, not live service validation.

## ASR 2.0

[Official ASR protocol](https://docs.volcengine.com/docs/6561/1354869?lang=zh), [current page](https://docs.volcengine.com/docs/DoubaoVoice/LargemodelstreamingautomaticspeechrecognitionAPI?lang=zh).

- WSS host: `openspeech.bytedance.com`; paths `/api/v3/sauc/bigmodel` (bidirectional), `/api/v3/sauc/bigmodel_nostream` (delayed recognition). Optimized bidirectional path `/api/v3/sauc/bigmodel_async` is independently confirmed by [official speaker-separation documentation](https://docs.volcengine.com/docs/DoubaoVoice/speaker-separation?lang=zh).
- Binary header nibbles: byte0 version=1, header-size=1 (multiply by four); byte1 type/flags; byte2 serialization/compression; byte3 reserved=0. Types: request=1, audio=2, response=9, error=15. Serialization: raw=0, JSON=1; compression: none=0, gzip=1. Integers are big-endian.
- Flags: 0=no sequence; 1=positive int32 sequence follows header; 2=last without sequence; 3=last with negative int32 sequence. Payload length is uint32 compressed-byte length. Never assume a fixed payload offset.
- Response: header, optional sequence, uint32 length, payload. Error: header, optional sequence, int32 code, uint32 length, payload. Decompress before JSON parsing.
- Audio: PCM signed16 little-endian, 16000Hz, mono; `format=pcm`, `codec=raw`, `bits=16`, `channel=1`. Recommend 100–200ms chunks paced similarly. JSON `request.model_name=bigmodel`; `show_utterances=true`; `enable_nonstream=true` enables optimized two-pass recognition. `enable_itn`, `enable_punc` control formatting.

### Authentication and model selection

[Official legacy/current authentication reference](https://docs.volcengine.com/docs/6561/2534847?lang=zh) and [official ASR2 single-direction reference](https://docs.volcengine.com/docs/6561/2628951?lang=zh):

- New console: `X-Api-Key`.
- Legacy ASR/MT: `X-Api-App-Key` (APP ID) plus `X-Api-Access-Key` (Access Token). These are distinct from generic cloud AK/SK.
- Both: `X-Api-Resource-Id: volc.seedasr.sauc.duration` (2.0 hourly) and UUID `X-Api-Request-Id`. Concurrent alternative: `volc.seedasr.sauc.concurrent`.
- ASR compatibility header `X-Api-Sequence: -1`; connection UUID `X-Api-Connect-Id`. Capture response `X-Tt-Logid` for troubleshooting, never credentials.
- LiveInterpret legacy auth instead uses `X-Api-App-Id`; its 2.0 resource is `volc.service_type.10053`.

### Result interpretation and application policy

[Official response example](https://docs.volcengine.com/docs/6561/2628951?lang=zh) uses `payload_msg.result` as an object with `text`, `utterances[]`, and `audio_info.duration`. Utterances carry `text`, `start_time`, `end_time`, `definite`, optional `words[]` and `additions`. The older protocol table labels result a list while its example is an object: handle the actual object contract and reject unexpected shapes clearly.

[Official BytePlus bidirectional ASR reference](https://docs.byteplus.com/es/docs/byteplusvoice/asrbidirect) explicitly says `definite=true` will not be updated further. Mainland two-pass docs confirm definite output comes from second-pass recognition. No stable utterance ID or revision counter was found in these official result schemas. Application policy: assign local session/utterance IDs, use timestamps and finalized-prefix order to reconcile snapshots, revise provisional text rather than append every response, and issue MT only for changed/final text. Validate this reconciliation with real provider fixtures before claiming robust live revisions. Packet sequence identifies transport order, not a sentence identity.

Business errors documented: `45000001` invalid request; `45000002` empty audio; `45000081` packet timeout; `45000151` bad audio; `55000031` busy. SDK wrapper `code=0` means no protocol error; service business success `20000000` is a separate convention. Do not conflate them.

## Machine translation large model

[Official matx_translate reference](https://docs.volcengine.com/docs/DoubaoVoice/MachineTranslationLargeModel-APIAccessDocumentation?lang=zh), updated 2026-09-28:

`POST https://openspeech.bytedance.com/api/v3/machine_translation/matx_translate`

Headers: new `X-Api-Key`, or legacy `X-Api-App-Key` + `X-Api-Access-Key`; `X-Api-Resource-Id: volc.speech.mt`; UUID `X-Api-Request-Id`; `Content-Type: application/json`.

Request: required `target_language` and `text_list` (array of strings); optional `source_language` (omitted/empty means detect), `corpus`. Maximum 16 texts, 1024 tokens each. `corpus.glossary_list` is a source-to-target string map; optional `glossary_table_id`, `glossary_table_name`; direct entries override table entries.

Response: `code`, `message`, successful `data.translation_list[]`, one object per input in order. Each object contains `translation`, optional `detected_source_language`, `usage` with `prompt_tokens`, `completion_tokens`, `total_tokens`. Success code `20000000`; parameter error `45000001`; limits `45000130`; internal error `55000001`.

No request field for previous sentences, conversation history, or session context is documented. The marketing description of context awareness does not establish a context API. Do not invent `context`, `messages`, or prompt fields. This endpoint returns completed HTTP translations; incremental subtitle presentation must be application behavior.

## Optional Seed LiveInterpret 2.0 gate

[Mainland official product overview](https://docs.volcengine.com/docs/DoubaoVoice/ProductOverview-3?lang=zh) explicitly identifies Seed LiveInterpret 2.0 and S2T/S2S modes. [Mainland legacy-auth reference](https://docs.volcengine.com/docs/6561/2534847?lang=zh) maps simultaneous interpretation 2.0 to `volc.service_type.10053`.

[Official BytePlus Live Interpretation API](https://docs.byteplus.com/es/docs/byteplusvoice/liveinterpretationapi) confirms separate source and translation subtitle events: source start/partial/end `650/651/652`; translation start/partial/end `653/654/655`. Partial events carry `text`; end events carry `text`, `start_time`, `end_time`. Requests: StartSession=100, TaskRequest=200, FinishSession=102. Wait for SessionStarted=150 before audio; SessionFinished=152 / SessionFailed=153 terminate. This service uses WebSocket Protocol Buffers, not ASR binary framing or generic voice-chat JSON events.

However, BytePlus has a different regional host and resource (`volc.service_type.1000025`). This is evidence that a related official service supports dual incremental text, not proof that mainland `volc.service_type.10053` has the identical contract. Mainland API reference `https://www.volcengine.com/docs/6561/1756902` could not be retrieved and its event contract was not independently indexed. Keep LiveInterpret unavailable until its current mainland API/proto and dual-text behavior are verified. Do not substitute the generic real-time dialog API (`volc.speech.dialog`) or copy unofficial implementations.

## Verification still required

- Credential entitlement, authenticated handshake and a short English PCM fixture against the selected ASR2 optimized endpoint.
- Record real partial/revision/final response shapes and verify local utterance reconciliation, stop/drain, timeout, and reconnect semantics.
- MT authenticated response shape and English-to-Chinese output; undocumented cross-item/context behavior must remain unclaimed.
- LiveInterpret mainland protocol, generated official protobuf schema, model2.0 mapping, and both source/translation partial/final events before enabling the optional provider.
