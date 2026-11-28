using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hardwood.Voice;

/// <summary>One pinned file of the voice pack.</summary>
public sealed record VoicePackFile(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("bytes")] long Bytes,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("urls")] IReadOnlyList<string>? Urls = null);

/// <summary>The voice pack's manifest, <c>voice\voice-pack.json</c> (the same shape as <c>tools/Voice/pin.json</c>, which
/// the <c>voice-pack.yml</c> workflow copies into the pack).</summary>
public sealed record VoicePackManifest(
    [property: JsonPropertyName("pack")] string Pack,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("voices")] string Voices,
    [property: JsonPropertyName("files")] IReadOnlyList<VoicePackFile> Files)
{
    public static VoicePackManifest Read(string path) =>
        JsonSerializer.Deserialize<VoicePackManifest>(File.ReadAllText(path)) ?? throw new InvalidDataException($"{path} is empty.");
}

/// <summary>Finds and checks an installed voice pack. Every failure has a plain reason the game can show in Settings
/// ("no voice pack", "the model's checksum doesn't match"), and the game then stays text only.</summary>
public static class VoicePack
{
    public const string ManifestName = "voice-pack.json";

    /// <summary>The pack in <paramref name="folder"/> checked against its manifest (sizes, then sha256), or the reason
    /// it can't be used.</summary>
    public static (VoicePackManifest? Pack, string Problem) Check(string folder, bool hashes = true)
    {
        var manifestPath = Path.Combine(folder, ManifestName);
        if (!File.Exists(manifestPath)) return (null, "No voice pack is installed.");
        VoicePackManifest manifest;
        try { manifest = VoicePackManifest.Read(manifestPath); }
        catch (Exception e) when (e is JsonException or InvalidDataException or IOException) { return (null, $"The voice pack's manifest can't be read ({e.Message})."); }
        foreach (var name in new[] { manifest.Model, manifest.Voices })
            if (manifest.Files.All(f => f.Name != name)) return (null, $"The voice pack's manifest has no pin for {name}.");
        foreach (var file in manifest.Files)
        {
            var path = Path.Combine(folder, file.Name);
            if (!File.Exists(path)) return (null, $"The voice pack is missing {file.Name}.");
            if (new FileInfo(path).Length != file.Bytes) return (null, $"{file.Name} is the wrong size: reinstall the voice pack.");
            if (hashes && !string.Equals(Sha256(path), file.Sha256, StringComparison.OrdinalIgnoreCase))
                return (null, $"{file.Name}'s checksum doesn't match: reinstall the voice pack.");
        }
        return (manifest, "");
    }

    /// <summary>The checked pack's speaker.</summary>
    public static KokoroSpeaker Open(string folder, VoicePackManifest pack, int threads = 0) =>
        new(Path.Combine(folder, pack.Model), Path.Combine(folder, pack.Voices), threads);

    public static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}

/// <summary>16-bit mono WAV, for the pack's check and the optional audio cache.</summary>
public static class Wav
{
    public static void Write(string path, float[] samples, int sampleRate = KokoroSpeaker.SampleRate)
    {
        using var w = new BinaryWriter(File.Create(path));
        var data = samples.Length * 2;
        w.Write("RIFF"u8); w.Write(36 + data); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(sampleRate); w.Write(sampleRate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(data);
        foreach (var s in samples) w.Write((short)Math.Round(Math.Clamp(s, -1f, 1f) * short.MaxValue));
    }

    /// <summary>Root-mean-square level of the samples (0 = silence).</summary>
    public static double Rms(float[] samples) =>
        samples.Length == 0 ? 0 : Math.Sqrt(samples.Sum(s => (double)s * s) / samples.Length);
}
