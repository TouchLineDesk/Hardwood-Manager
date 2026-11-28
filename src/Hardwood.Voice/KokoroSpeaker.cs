using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using MisakiSharp;

namespace Hardwood.Voice;

/// <summary>Text to speech for the studio shows (run 239, V1 of <c>claude/studio-voice-plan.md</c>): MisakiSharp
/// (Apache-2.0, Kokoro's own G2P, no espeak inside) turns English into phonemes, <see cref="KokoroVocab"/> turns them
/// into token ids, and Kokoro-82M v1.0 (int8 ONNX, Apache-2.0) on ONNX Runtime's CPU provider turns those into 24 kHz
/// mono audio. The text given here is already what a broadcaster says (<c>Hardwood.Core.Presentation.SpeechText</c>).
/// One call at a time per speaker (the session is shared and locked).</summary>
public sealed class KokoroSpeaker : IDisposable
{
    public const int SampleRate = 24000;

    private readonly InferenceSession _session;
    private readonly VoiceBank _voices;
    private readonly EnglishG2P _g2p;
    private readonly object _gate = new();

    /// <param name="modelPath">kokoro-v1.0.int8.onnx</param>
    /// <param name="voicesPath">voices-v1.0.bin</param>
    /// <param name="threads">ONNX Runtime's intra-op threads (0 = its own default).</param>
    public KokoroSpeaker(string modelPath, string voicesPath, int threads = 0)
    {
        var options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
        if (threads > 0) options.IntraOpNumThreads = threads;
        _session = new InferenceSession(modelPath, options);
        _voices = new VoiceBank(voicesPath);
        _g2p = new EnglishG2P(british: false);
    }

    public IReadOnlyList<string> Voices => _voices.Voices;

    /// <summary>Misaki's phonemes for <paramref name="said"/> (American English).</summary>
    public string Phonemes(string said)
    {
        lock (_gate) return _g2p.Phonemize(said).Item1;
    }

    /// <summary>The audio for <paramref name="said"/> in <paramref name="voice"/>, as samples in -1..1 at
    /// <see cref="SampleRate"/>. A line longer than the model's limit is made in pieces at its pauses and joined with
    /// a short gap. <paramref name="speed"/> 1.0 is the voice's own pace.</summary>
    public float[] Speak(string said, string voice, float speed = 1f)
    {
        if (string.IsNullOrWhiteSpace(said)) return Array.Empty<float>();
        var ids = KokoroVocab.Encode(Phonemes(said));
        var audio = new List<float>();
        foreach (var piece in Split(ids, KokoroVocab.MaxTokens - 2))
        {
            if (audio.Count > 0) audio.AddRange(new float[SampleRate / 8]);
            audio.AddRange(Run(piece, voice, speed));
        }
        return audio.ToArray();
    }

    private float[] Run(List<long> ids, string voice, float speed)
    {
        var style = _voices.Style(voice, ids.Count);
        var tokens = new long[ids.Count + 2];
        ids.CopyTo(tokens, 1);
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("tokens", new DenseTensor<long>(tokens, new[] { 1, tokens.Length })),
            NamedOnnxValue.CreateFromTensor("style", new DenseTensor<float>(style, new[] { 1, VoiceBank.StyleWidth })),
            NamedOnnxValue.CreateFromTensor("speed", new DenseTensor<float>(new[] { speed }, new[] { 1 })),
        };
        lock (_gate)
        {
            using var results = _session.Run(inputs);
            return results.First().AsEnumerable<float>().ToArray();
        }
    }

    /// <summary>Token ids cut into runs of at most <paramref name="max"/>, at the last pause (. ! ? ; : , or a space)
    /// before the limit where there is one.</summary>
    internal static IEnumerable<List<long>> Split(List<long> ids, int max)
    {
        var start = 0;
        while (start < ids.Count)
        {
            var end = Math.Min(ids.Count, start + max);
            if (end < ids.Count)
            {
                var cut = -1;
                for (var i = end - 1; i > start + max / 3 && cut < 0; i--) if (ids[i] is >= 1 and <= 6) cut = i + 1;
                for (var i = end - 1; i > start + max / 3 && cut < 0; i--) if (ids[i] == 16) cut = i + 1;
                if (cut > 0) end = cut;
            }
            yield return ids.GetRange(start, end - start);
            start = end;
        }
    }

    public void Dispose()
    {
        _session.Dispose();
        _voices.Dispose();
    }
}
