---
license: apache-2.0
---
# LibriSpeech pruned_transducer_stateless7_streaming

This model is based on the icefall `pruned_transducer_stateless7_streaming` recipe,
but it the model parameters are modified to be smaller in size. It can be
considered a streaming version of [this model](https://huggingface.co/Zengwei/icefall-asr-librispeech-pruned-transducer-stateless7-20M-2023-01-28) and follows
the same parameter configuration.

## Performance Record

| Decoding method           | test-clean | test-other | 
|---------------------------|------------|------------|
| greedy search             |    3.94    |   9.79     |
| modified beam search      |    3.88    |   9.53     |
