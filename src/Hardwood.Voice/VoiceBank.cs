using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace Hardwood.Voice;

/// <summary>Kokoro's voices (<c>voices-v1.0.bin</c>): a NumPy .npz (a zip of .npy arrays), one array per voice of shape
/// (510, 1, 256) float32. Row n is the style for a line of n phoneme tokens. Read once, on demand, voice by voice.</summary>
public sealed class VoiceBank : IDisposable
{
    public const int StyleWidth = 256;
    private readonly ZipArchive _zip;
    private readonly Dictionary<string, float[,]> _loaded = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public VoiceBank(string path) => _zip = ZipFile.OpenRead(path);

    /// <summary>Every voice in the file, by name ("am_michael", …).</summary>
    public IReadOnlyList<string> Voices => _zip.Entries.Select(e => e.FullName).Where(n => n.EndsWith(".npy", StringComparison.Ordinal))
        .Select(n => n[..^4]).OrderBy(n => n, StringComparer.Ordinal).ToList();

    /// <summary>The 256 style values for <paramref name="voice"/> at a line of <paramref name="tokenCount"/> tokens.</summary>
    public float[] Style(string voice, int tokenCount)
    {
        var rows = Rows(voice);
        var r = Math.Clamp(tokenCount, 0, rows.GetLength(0) - 1);
        var style = new float[StyleWidth];
        for (var i = 0; i < StyleWidth; i++) style[i] = rows[r, i];
        return style;
    }

    private float[,] Rows(string voice)
    {
        lock (_gate)
        {
            if (_loaded.TryGetValue(voice, out var cached)) return cached;
            var entry = _zip.GetEntry(voice + ".npy") ?? throw new ArgumentException($"No voice \"{voice}\" in the voice file.", nameof(voice));
            using var stream = entry.Open();
            var rows = ReadNpy(stream);
            _loaded[voice] = rows;
            return rows;
        }
    }

    /// <summary>A little-endian float32 .npy whose shape ends in 256 (any leading shape is flattened into rows).</summary>
    internal static float[,] ReadNpy(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        var magic = reader.ReadBytes(6);
        if (magic.Length != 6 || magic[0] != 0x93 || Encoding.ASCII.GetString(magic, 1, 5) != "NUMPY") throw new InvalidDataException("Not a .npy array.");
        var major = reader.ReadByte();
        reader.ReadByte();
        int headerLength = major == 1 ? reader.ReadUInt16() : (int)reader.ReadUInt32();
        var header = Encoding.ASCII.GetString(reader.ReadBytes(headerLength));
        if (!header.Contains("'<f4'")) throw new InvalidDataException("The voice array isn't little-endian float32.");
        if (header.Contains("'fortran_order': True")) throw new InvalidDataException("The voice array is in Fortran order.");
        var shape = Regex.Match(header, @"'shape':\s*\(([^)]*)\)").Groups[1].Value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(int.Parse).ToArray();
        if (shape.Length == 0 || shape[^1] != StyleWidth) throw new InvalidDataException($"Unexpected voice shape ({string.Join(", ", shape)}).");
        var count = shape.Aggregate(1, (a, b) => a * b);
        var rows = new float[count / StyleWidth, StyleWidth];
        var bytes = reader.ReadBytes(count * 4);
        if (bytes.Length != count * 4) throw new InvalidDataException("The voice array is cut short.");
        Buffer.BlockCopy(bytes, 0, rows, 0, bytes.Length);
        return rows;
    }

    public void Dispose() => _zip.Dispose();
}
