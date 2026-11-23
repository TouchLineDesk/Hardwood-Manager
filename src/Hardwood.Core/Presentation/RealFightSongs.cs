using System.Reflection;

namespace Hardwood.Core.Presentation;

/// <summary>A real school's fight song, its music public domain, typed from its score (tier 1; one
/// <c>tools/Assets/fightsongs/*.song</c> file, embedded).</summary>
/// <param name="Bar">Beats (quarters) in a bar.</param>
/// <param name="Pickup">Beats before the first full bar.</param>
/// <param name="Melody">The tune as (beat, length, MIDI pitch), from the first note (beat 0); rests are left out and ties merged.</param>
/// <param name="Chords">The arrangement's chords as (beat, length, root pitch class, intervals above the root).</param>
public sealed record RealFightSong(
    string Program, string Title, string Composer, int Published, string Source,
    int Bar, double Pickup, int Tempo, int Key, FightStyle Style,
    IReadOnlyList<(double Beat, double Beats, int Midi)> Melody,
    IReadOnlyList<(double Beat, double Beats, int Root, int[] Tones)> Chords,
    double Length)
{
    public string Describe() => $"\"{Title}\" ({Composer.Split(" (")[0]}, {Published})";
}

/// <summary>
/// Fight songs B (run 167; <c>claude/fight-songs-plan.md</c>, tier 1): the real fight songs whose music was published in
/// 1930 or earlier, played from new note data (never a recording). Each file holds the tune (checked against the score bar
/// by bar), our own chords and the song's source. <see cref="Arrange"/> turns one into a band chart in the same shape as a
/// composed song (an intro, the chorus, a big last strain with the glockenspiel and the horns, a tag), so the renderer and
/// the cues don't change. Pure: no game random stream.
/// </summary>
public static class RealFightSongs
{
    private static readonly Lazy<IReadOnlyDictionary<string, RealFightSong>> _all = new(Load);

    /// <summary>Every tier 1 song, by the program's name as <c>ncaa_programs.json</c> writes it.</summary>
    public static IReadOnlyDictionary<string, RealFightSong> All => _all.Value;

    public static RealFightSong? For(string program) => All.GetValueOrDefault(program);

    private static IReadOnlyDictionary<string, RealFightSong> Load()
    {
        var asm = typeof(RealFightSongs).Assembly;
        var d = new Dictionary<string, RealFightSong>(StringComparer.Ordinal);
        foreach (var name in asm.GetManifestResourceNames().Where(n => n.StartsWith("Hardwood.FightSongs.") && n.EndsWith(".song")).OrderBy(n => n))
        {
            using var s = asm.GetManifestResourceStream(name)!;
            using var r = new StreamReader(s);
            var song = Parse(r.ReadToEnd(), name);
            d[song.Program] = song;
        }
        return d;
    }

    private static readonly string[] Letters = { "C", "D", "E", "F", "G", "A", "B" };
    private static readonly int[] Steps = { 0, 2, 4, 5, 7, 9, 11 };

    /// <summary>A pitch class from "Eb", "F#", "C" (any case for the accidental's letter).</summary>
    public static int PitchClass(string name, out int used)
    {
        int i = Array.IndexOf(Letters, name[..1].ToUpperInvariant());
        if (i < 0) throw new FormatException($"not a note name: {name}");
        int pc = Steps[i]; used = 1;
        while (used < name.Length && (name[used] == 'b' || name[used] == '#')) { pc += name[used] == '#' ? 1 : -1; used++; }
        return ((pc % 12) + 12) % 12;
    }

    /// <summary>A note like "Eb4" (C4 = MIDI 60).</summary>
    public static int Midi(string note)
    {
        PitchClass(note, out int used);
        int octave = int.Parse(note[used..], System.Globalization.CultureInfo.InvariantCulture);
        // The letter's octave as written (so B#3 is C4's pitch and Cb4 is B3's).
        int letter = Steps[Array.IndexOf(Letters, note[..1].ToUpperInvariant())];
        int alter = note[1..used].Count(c => c == '#') - note[1..used].Count(c => c == 'b');
        return 12 * (octave + 1) + letter + alter;
    }

    /// <summary>A chord symbol: "Eb", "Cm", "Bb7", "F#dim", "Am7", "C6", "Cmaj7", "C+", with an optional "/E" bass ignored.</summary>
    public static (int Root, int[] Tones) Chord(string sym)
    {
        string s = sym.Split('/')[0];
        int root = PitchClass(s, out int used);
        int[] tones = s[used..] switch
        {
            "" => new[] { 0, 4, 7 },
            "m" => new[] { 0, 3, 7 },
            "7" => new[] { 0, 4, 7, 10 },
            "m7" => new[] { 0, 3, 7, 10 },
            "maj7" => new[] { 0, 4, 7, 11 },
            "6" => new[] { 0, 4, 7, 9 },
            "m6" => new[] { 0, 3, 7, 9 },
            "dim" => new[] { 0, 3, 6 },
            "dim7" => new[] { 0, 3, 6, 9 },
            "+" => new[] { 0, 4, 8 },
            "9" => new[] { 0, 4, 7, 10, 14 },
            var q => throw new FormatException($"unknown chord quality '{q}' in {sym}"),
        };
        return (root, tones);
    }

    private static double Beats(string s)
    {
        var parts = s.Split('/');
        double v = double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
        return parts.Length == 2 ? v / double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : v;
    }

    /// <summary>Read one song file. Throws a <see cref="FormatException"/> naming the bar when a bar doesn't add up, so a
    /// typing slip fails the build's tests rather than playing a wrong note.</summary>
    public static RealFightSong Parse(string text, string file = "song")
    {
        var head = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var melodyText = new List<string>(); var chordText = new List<string>();
        List<string>? into = null;
        foreach (var raw in text.Split('\n'))
        {
            string line = raw.TrimStart().StartsWith('#') ? "" : raw;   // a comment line ('#' elsewhere is a sharp)
            line = line.Trim();   // ('%' is a chord repeat, and URLs carry it, so it never starts a comment)
            if (line.Length == 0) continue;
            if (line.Equals("melody:", StringComparison.OrdinalIgnoreCase)) { into = melodyText; continue; }
            if (line.Equals("chords:", StringComparison.OrdinalIgnoreCase)) { into = chordText; continue; }
            if (into == null)
            {
                int c = line.IndexOf(':');
                if (c > 0) head[line[..c].Trim()] = line[(c + 1)..].Trim();
                continue;
            }
            into.Add(line);
        }
        string Need(string k) => head.TryGetValue(k, out var v) ? v : throw new FormatException($"{file}: no '{k}:' line");
        int bar = int.Parse(Need("meter"));
        double pickup = head.TryGetValue("pickup", out var pu) ? Beats(pu) : 0;
        var style = Enum.Parse<FightStyle>(Need("style"), ignoreCase: true);

        // The melody: bars split by '|'; every bar but the pickup must add up to the meter.
        var bars = string.Join(" ", melodyText).Split('|').Select(b => b.Trim()).Where(b => b.Length > 0).ToList();
        var melody = new List<(double, double, int)>();
        double at = 0;
        int? last = null;
        for (int i = 0; i < bars.Count; i++)
        {
            double sum = 0;
            foreach (var tok in bars[i].Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                int colon = tok.LastIndexOf(':');
                if (colon <= 0) throw new FormatException($"{file}: bar {i + 1}: '{tok}' needs a length (note:beats)");
                string n = tok[..colon]; double len = Beats(tok[(colon + 1)..]);
                if (n == "_")
                {
                    if (last == null || melody.Count == 0) throw new FormatException($"{file}: bar {i + 1}: a tie with nothing to hold");
                    var (b0, l0, m0) = melody[^1]; melody[^1] = (b0, l0 + len, m0);
                }
                else if (n == "r") last = null;
                else { int m = Midi(n); melody.Add((at + sum, len, m)); last = m; }
                sum += len;
            }
            double want = i == 0 && pickup > 0 ? pickup : bar;
            if (Math.Abs(sum - want) > 1e-6) throw new FormatException($"{file}: melody bar {i + 1} adds up to {sum} beats, not {want}");
            at += sum;
        }
        double length = at;

        // The chords: one line of bars the same shape as the melody's; each bar's chords split it evenly; '%' repeats.
        var cbars = string.Join(" ", chordText).Split('|').Select(b => b.Trim()).Where(b => b.Length > 0).ToList();
        if (cbars.Count != bars.Count) throw new FormatException($"{file}: {cbars.Count} chord bars for {bars.Count} melody bars");
        var chords = new List<(double, double, int, int[])>();
        double cat = 0;
        string prev = "";
        for (int i = 0; i < cbars.Count; i++)
        {
            double span = i == 0 && pickup > 0 ? pickup : bar;
            var syms = cbars[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (int k = 0; k < syms.Length; k++)
            {
                string sym = syms[k] == "%" ? prev : syms[k];
                var (root, tones) = Chord(sym);
                chords.Add((cat + k * span / syms.Length, span / syms.Length, root, tones));
                prev = sym;
            }
            cat += span;
        }

        return new RealFightSong(Need("program"), Need("title"), Need("composer"), int.Parse(Need("published")), Need("source"),
            bar, pickup, int.Parse(Need("tempo")), PitchClass(Need("key"), out _), style, melody, chords, length);
    }

    /// <summary>The program's song spec for the renderer (its tempo and its seed) when it has a tier 1 song.</summary>
    public static FightSongSpec SpecOf(RealFightSong s) =>
        new(s.Program, s.Style, s.Key, s.Tempo, FightSongs.Hash("tier1|" + s.Program));

    /// <summary>
    /// The band chart: a drum roll and a fanfare on the dominant, the chorus, the chorus again as the big last strain (when
    /// the chorus is short; a long one plays once, its second half big), and a tag on the tonic. The hit is the big strain
    /// and the tag.
    /// </summary>
    public static FightScore Arrange(RealFightSong song)
    {
        var spec = SpecOf(song);
        var notes = new List<BandNote>();
        double seconds = song.Length * 60.0 / song.Tempo;
        bool twice = seconds < 20;

        // Intro: the snare roll swelling over four beats, then the dominant seventh held (as tier 2's), a beat's rest.
        for (int i = 0; i < 16; i++) notes.Add(new BandNote(i * 0.25, 0.25, 0, BandPart.Snare, 0.35 + 0.04 * i));
        int dom = (song.Key + 7) % 12;
        notes.Add(new BandNote(4, 3, 0, BandPart.Cymbal, 0.8));
        notes.Add(new BandNote(4, 0.5, 0, BandPart.Kick, 1));
        foreach (int t in new[] { 0, 4, 7, 10 }) notes.Add(new BandNote(4, 3, Near(dom + t, 52), BandPart.Harmony, 0.9));
        notes.Add(new BandNote(4, 3, Near(dom + 4, 67), BandPart.Lead, 0.95));
        notes.Add(new BandNote(4, 3, Near(dom, 40), BandPart.Bass, 0.9));
        double start = 8 - song.Pickup;   // the pickup leads into beat 8

        double hit;
        if (twice)
        {
            Strain(notes, song, start, bigFrom: double.MaxValue);
            // The second pass's bars line up with the first's: with a pickup, it sits at the end of an added bar.
            double second = start + song.Length + (song.Pickup > 0 ? song.Bar - song.Pickup : 0);
            hit = second;
            Strain(notes, song, second, bigFrom: 0);
            start = second;
        }
        else
        {
            // The big half starts at the bar nearest the middle.
            double mid = song.Pickup + Math.Round((song.Length - song.Pickup) / 2 / song.Bar) * song.Bar;
            Strain(notes, song, start, bigFrom: mid);
            // The hit takes in the pickup notes that lead into the big half (review, run 167).
            hit = start + mid - song.Pickup;
        }
        double end = start + song.Length;
        // The tag: the tonic chord, everyone, held.
        foreach (int t in new[] { 0, 4, 7 }) notes.Add(new BandNote(end, 2.5, Near(song.Key + t, 55), BandPart.Harmony, 1));
        notes.Add(new BandNote(end, 2.5, Near(song.Key, 72), BandPart.Lead, 1));
        notes.Add(new BandNote(end, 2.5, Near(song.Key, 38), BandPart.Bass, 1));
        notes.Add(new BandNote(end, 3, 0, BandPart.Cymbal, 1));
        notes.Add(new BandNote(end, 0.5, 0, BandPart.Kick, 1));
        notes.Add(new BandNote(end, 0.5, 0, BandPart.Snare, 1));
        return new FightScore(spec, end + 4, notes.OrderBy(n => n.Beat).ToList(), hit);
    }

    /// <summary>The pitch of class <paramref name="pc"/> nearest <paramref name="around"/> (MIDI).</summary>
    private static int Near(int pc, int around)
    {
        pc = ((pc % 12) + 12) % 12;
        int m = around - ((around - pc) % 12 + 12) % 12;
        return around - m > 6 ? m + 12 : m;
    }

    /// <summary>One pass of the chorus at <paramref name="at"/>: the tune (and the glockenspiel and the horns from
    /// <paramref name="bigFrom"/> on), with the tuba, the trombones' chords and the drumline from the chords.</summary>
    private static void Strain(List<BandNote> notes, RealFightSong song, double at, double bigFrom)
    {
        // Keep the trumpets in a band's range: the tune sits with its middle around B♭4–F5.
        double avg = song.Melody.Average(m => m.Midi);
        int shift = avg < 64 ? 12 : avg > 78 ? -12 : 0;
        bool crashed = false;
        foreach (var (b, l, m) in song.Melody)
        {
            bool big = b >= bigFrom - 1e-9;
            if (big && !crashed) { notes.Add(new BandNote(at + b, 2, 0, BandPart.Cymbal, 0.9)); crashed = true; }
            notes.Add(new BandNote(at + b, l * 0.92, m + shift, BandPart.Lead, big ? 1.0 : 0.85));
            if (big) notes.Add(new BandNote(at + b, Math.Min(l, 0.5), m + shift + 12, BandPart.Glock, 0.65));
        }
        foreach (var (b, l, root, tones) in song.Chords)
        {
            bool big = b >= bigFrom - 1e-9;
            double v = big ? 1.0 : 0.8;
            int bass = Near(root, 41), fifth = Near(root + 7, 41);
            var chord = tones.Select(t => Near(root + t, 55)).Distinct().ToList();
            for (double k = 0; k < l - 1e-9; k += 1)
            {
                double t0 = at + b + k;
                bool down = Math.Abs((b + k - song.Pickup) % song.Bar) < 1e-6 || song.Pickup > 0 && b + k < song.Pickup;
                switch (song.Style)
                {
                    case FightStyle.Swing:
                    case FightStyle.Shuffle:
                        notes.Add(new BandNote(t0, 0.9, (int)k % 2 == 0 ? bass : fifth, BandPart.Bass, v));
                        foreach (int c in chord) notes.Add(new BandNote(t0 + 2.0 / 3, 0.3, c, BandPart.Harmony, v * 0.55));
                        if ((int)k % 2 == 1) notes.Add(new BandNote(t0, 0.25, 0, BandPart.Snare, 0.6 * v));
                        if (down) notes.Add(new BandNote(t0, 0.5, 0, BandPart.Kick, 0.7 * v));
                        notes.Add(new BandNote(t0, 0.5, 0, BandPart.Cymbal, 0.15 * v));
                        break;
                    default:
                        // A march: the tuba's oom on the beat (root, then fifth), the trombones' pah on the offbeat, snare eighths.
                        notes.Add(new BandNote(t0, 0.45, (int)k % 2 == 0 ? bass : fifth, BandPart.Bass, v * ((int)k % 2 == 0 ? 1 : 0.85)));
                        foreach (int c in chord) notes.Add(new BandNote(t0 + 0.5, 0.3, c, BandPart.Harmony, v * 0.6));
                        notes.Add(new BandNote(t0, 0.25, 0, BandPart.Snare, (down ? 0.8 : 0.5) * v));
                        notes.Add(new BandNote(t0 + 0.5, 0.25, 0, BandPart.Snare, 0.4 * v));
                        if (down) notes.Add(new BandNote(t0, 0.5, 0, BandPart.Kick, v));
                        break;
                }
            }
            // In the big strain the horns hold each chord's third (the countermelody).
            if (big) notes.Add(new BandNote(at + b, l * 0.95, Near(root + tones[1], 58), BandPart.Counter, 0.75));
        }
    }
}
