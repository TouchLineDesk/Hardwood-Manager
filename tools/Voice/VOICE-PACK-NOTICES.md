# Hardwood Manager voice pack v1: third-party notices

This optional pack (run 239, `claude/studio-voice-plan.md`) gives the HSN studio shows their voices. It ships on its own
release (`voicepack-v1`), never inside the game zip, and the game works unchanged without it. Nothing in it was recorded
from a real person: every line is made on your PC from the show's text.

| Piece | Licence | Source |
|---|---|---|
| Kokoro-82M v1.0 (model and voices) by hexgrad | Apache-2.0 | https://huggingface.co/hexgrad/Kokoro-82M |
| `kokoro-v1.0.int8.onnx`, `voices-v1.0.bin` (the ONNX export) | Apache-2.0 | https://github.com/thewh1teagle/kokoro-onnx (release `model-files-v1.0`); sha256 pins in `voice-pack.json` |
| MisakiSharp 2.2.0 by Lyrcaxis (a C# port of hexgrad's misaki, Kokoro's text-to-phoneme engine) | Apache-2.0 | https://github.com/Lyrcaxis/MisakiSharp |
| Microsoft.ML.OnnxRuntime 1.20.1 | MIT | https://github.com/microsoft/onnxruntime |
| Hardwood.Voice (the game's own wrapper) | the game's licence | this repository, `src/Hardwood.Voice` |

**MisakiSharp's own NOTICE** lists the work its code and data derive from: misaki (Apache-2.0), PaddleSpeech
(Apache-2.0), jieba (MIT), pypinyin (MIT), spaCy (MIT; its English tokenizer rules and a tagger distilled from
`en_core_web_sm`), num2words (LGPL-2.1; English number wording, ported), phonemizer (GPL-3.0; its espeak back end's
punctuation and post-processing behaviour, reproduced via misaki), cutlet (MIT), Convert-Numbers-to-Japanese (MIT),
MeCab (BSD/LGPL/GPL), UniDic 3.1.0 (BSD-3-Clause) and misaki's English lexicons. MisakiSharp contains no espeak-ng
code or data, and the pack runs no espeak-ng.

Apache License 2.0: https://www.apache.org/licenses/LICENSE-2.0 — MIT licence: https://opensource.org/license/mit
