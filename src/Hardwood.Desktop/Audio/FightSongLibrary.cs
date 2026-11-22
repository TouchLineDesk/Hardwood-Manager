using Hardwood.Core.Presentation;
using NAudio.Wave;

namespace Hardwood.Desktop.Audio;

/// <summary>One program's fight song, ready to play: the whole song and its hit (the last strain), as 44.1 kHz stereo
/// floats, and where it came from.</summary>
public sealed record BandSong(string Program, float[] Song, float[] Hit, string Source);

/// <summary>
/// Fight songs A (run 161; <c>claude/fight-songs-plan.md</c>): where a program's song comes from.
/// <list type="number">
/// <item>Tier 3: Danny's own file in <see cref="CustomFolder"/> (<c>&lt;school&gt;.ogg</c>, <c>.wav</c> or, on Windows,
/// <c>.mp3</c>). Runs never fill this folder; it lives on his PC and is never committed or shipped.</item>
/// <item>Tier 1 (fight songs B): a public-domain song typed from its score. None yet.</item>
/// <item>Tier 2: the composer's original song for the program (<see cref="FightSongs"/>), rendered on first use and kept
/// for the session.</item>
/// </list>
/// </summary>
public static class FightSongLibrary
{
    /// <summary>Documents\Hardwood Manager\fight-songs (tests point it elsewhere).</summary>
    public static string CustomFolder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Hardwood Manager", "fight-songs");

    public static readonly string[] Extensions = { ".ogg", ".wav", ".mp3" };

    private static readonly Dictionary<string, BandSong> _cache = new(StringComparer.Ordinal);
    private static readonly object _gate = new();

    /// <summary>The file names a program's own song may have: the school as written ("Miami (FL).ogg") or its slug
    /// ("miami-fl.ogg").</summary>
    public static IEnumerable<string> NamesFor(string school)
    {
        var bad = Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\', ':' }).ToHashSet();
        string plain = new string(school.Where(c => !bad.Contains(c)).ToArray()).Trim();
        yield return plain;
        string slug = Slug(school);
        if (!string.Equals(slug, plain, StringComparison.OrdinalIgnoreCase)) yield return slug;
    }

    /// <summary>"Miami (FL)" → "miami-fl"; "Texas A&amp;M" → "texas-am"; "St. John's" → "st-johns".</summary>
    public static string Slug(string school)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in school.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c)) sb.Append(c);
            else if ((c == ' ' || c == '-' || c == '(' || c == '.') && sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        return sb.ToString().Trim('-');
    }

    /// <summary>The custom file for a school, or null.</summary>
    public static string? CustomFile(string school)
    {
        try
        {
            if (!Directory.Exists(CustomFolder)) return null;
            foreach (var name in NamesFor(school))
                foreach (var ext in Extensions)
                {
                    var path = Path.Combine(CustomFolder, name + ext);
                    if (File.Exists(path)) return path;
                }
        }
        catch (Exception) { }
        return null;
    }

    /// <summary>How many song files are in the custom folder (Settings' line).</summary>
    public static int CustomCount()
    {
        try
        {
            if (!Directory.Exists(CustomFolder)) return 0;
            return Directory.EnumerateFiles(CustomFolder).Count(f => Extensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
        }
        catch (Exception) { return 0; }
    }

    /// <summary>The program's song: Danny's file if he has one, else its own composed song (rendered once a session).</summary>
    public static BandSong For(string school, FightSongSpec spec)
    {
        string? custom = CustomFile(school);
        string key = school + "|" + (custom ?? spec.Describe());
        lock (_gate) if (_cache.TryGetValue(key, out var hit)) return hit;
        BandSong song;
        var file = custom == null ? null : Load(custom);
        if (file is { Length: > 0 })
        {
            // A custom file: the whole file at tip-off and the final; its last 10 s for the hit.
            int tail = Math.Min(file.Length, 10 * Synth.Rate * 2);
            var hitClip = file[^tail..];
            Fade(hitClip, Synth.Rate / 10, fadeIn: true);
            song = new BandSong(school, file, hitClip, Path.GetFileName(custom!));
        }
        else
        {
            var score = FightSongs.Compose(spec);
            song = new BandSong(school, BandRenderer.Render(score), BandRenderer.Render(score, score.HitFromBeat), spec.Describe());
        }
        lock (_gate) _cache[key] = song;
        return song;
    }

    /// <summary>Clear the session's cache (a custom file was added; tests).</summary>
    public static void Forget() { lock (_gate) _cache.Clear(); }

    /// <summary>A custom file decoded to 44.1 kHz stereo floats (at most 3 minutes), or null if it can't be read.</summary>
    public static float[]? Load(string path)
    {
        try
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".ogg") return Nonempty(CrowdMixer.Decode(File.OpenRead(path), 180)) ?? Mono(path);
            ISampleProvider? sp = null;
            IDisposable? owner = null;
            if (ext == ".wav") { var w = new WaveFileReader(path); owner = w; sp = w.ToSampleProvider(); }
            else if (ext == ".mp3" && OperatingSystem.IsWindows())
            {
                var m = new Mp3FileReaderBase(path, wf => new NAudio.Wave.AcmMp3FrameDecompressor(wf));
                owner = m; sp = m.ToSampleProvider();
            }
            if (sp == null) return null;
            using (owner)
            {
                int ch = sp.WaveFormat.Channels, rate = sp.WaveFormat.SampleRate;
                if (ch is < 1 or > 2 || rate < 8000) return null;
                var all = new List<float>();
                var buf = new float[8192];
                long max = 180L * rate * ch;
                int got;
                while (all.Count < max && (got = sp.Read(buf, 0, buf.Length)) > 0) all.AddRange(buf.AsSpan(0, got).ToArray());
                var raw = all.ToArray();
                if (ch == 1) { var st = new float[raw.Length * 2]; for (int i = 0; i < raw.Length; i++) st[2 * i] = st[2 * i + 1] = raw[i]; raw = st; }
                return Nonempty(rate == Synth.Rate ? raw : CrowdMixer.Resample(raw, rate, Synth.Rate));
            }
        }
        catch (Exception) { return null; }
    }

    private static float[]? Nonempty(float[] x) => x.Length > 0 ? x : null;

    /// <summary>A mono OGG (review, run 161: the crowd's decoder takes stereo only), doubled to both sides.</summary>
    private static float[]? Mono(string path)
    {
        using var r = new NVorbis.VorbisReader(path);
        if (r.Channels != 1 || r.SampleRate < 8000) return null;
        var all = new List<float>();
        var buf = new float[8192];
        int got;
        while (all.Count < 180L * r.SampleRate && (got = r.ReadSamples(buf, 0, buf.Length)) > 0) all.AddRange(buf.AsSpan(0, got).ToArray());
        var st = new float[all.Count * 2];
        for (int i = 0; i < all.Count; i++) st[2 * i] = st[2 * i + 1] = all[i];
        return Nonempty(r.SampleRate == Synth.Rate ? st : CrowdMixer.Resample(st, r.SampleRate, Synth.Rate));
    }

    private static void Fade(float[] stereo, int frames, bool fadeIn)
    {
        int total = stereo.Length / 2;
        frames = Math.Min(frames, total);
        for (int f = 0; f < frames; f++)
        {
            float g = (float)f / frames;
            int i = fadeIn ? 2 * f : 2 * (total - 1 - f);
            stereo[i] *= g; stereo[i + 1] *= g;
        }
    }
}
