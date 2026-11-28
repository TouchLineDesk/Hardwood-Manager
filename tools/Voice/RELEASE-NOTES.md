**Optional: voices for the HSN studio shows** (Tip-Off, Halftime and Postgame). Not needed to play.

- **What it is:** Kokoro-82M (Apache-2.0) running on your PC through ONNX Runtime, with MisakiSharp (Apache-2.0) for
  pronunciation. No espeak-ng, nothing recorded, no internet needed once it's installed. About 200 MB unzipped.
- **Install:** unzip it into the game's folder, so that a `voice` folder sits beside `HardwoodManager.exe`.
- **Status:** V1 (the engine). The shows start speaking in V2; until then the game doesn't load it.
- **Checks:** the model and voices match the sha256 pins in `voice\voice-pack.json`; the build made one line in each of
  the nine cast voices before publishing.
