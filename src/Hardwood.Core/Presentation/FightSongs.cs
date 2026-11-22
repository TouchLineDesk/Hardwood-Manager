namespace Hardwood.Core.Presentation;

/// <summary>A fight song's feel (fight songs A, run 161; <c>claude/fight-songs-plan.md</c>, tier 2).</summary>
public enum FightStyle
{
    /// <summary>A 2/4 march (the classic: oom-pah tuba, offbeat horns, snare eighths).</summary>
    March,
    /// <summary>A 6/8 march (two big beats a bar, each in three).</summary>
    SixEight,
    /// <summary>A swing tune (4/4, swung eighths, walking tuba, backbeat).</summary>
    Swing,
    /// <summary>A shuffle (4/4, triplet feel, heavy two and four).</summary>
    Shuffle,
    /// <summary>A rock-band chart (4/4, straight eighths, kick and snare).</summary>
    Rock,
    /// <summary>A pep stomp (4/4, the kick on every beat, the brass in unison).</summary>
    Stomp,
}

/// <summary>Who plays a note: the brass, the glockenspiel and the drumline.</summary>
public enum BandPart { Lead, Counter, Harmony, Bass, Glock, Snare, Kick, Cymbal }

/// <summary>One note of a band chart: when (in beats), how long (in beats), the MIDI pitch (drums ignore it) and how hard.</summary>
public sealed record BandNote(double Beat, double Beats, int Midi, BandPart Part, double Vel);

/// <summary>A program's song, as the composer picks it: the style, the key (0 = C … 11 = B, the tonic's pitch class) and
/// the tempo in beats a minute (a beat is a quarter, or a dotted quarter in 6/8).</summary>
public sealed record FightSongSpec(string Program, FightStyle Style, int Key, int Tempo, uint Seed)
{
    public static readonly string[] KeyNames = { "C", "D♭", "D", "E♭", "E", "F", "G♭", "G", "A♭", "A", "B♭", "B" };
    public string Describe() => $"{FightSongs.StyleName(Style)} in {KeyNames[Key]} major, {Tempo} bpm";
}

/// <summary>The whole chart: the notes, its length in beats and where its "hit" (the last strain, for a run or a break) starts.</summary>
public sealed record FightScore(FightSongSpec Spec, double Beats, IReadOnlyList<BandNote> Notes, double HitFromBeat)
{
    public double Seconds => Beats * 60.0 / Spec.Tempo;
    public double HitSeconds => (Beats - HitFromBeat) * 60.0 / Spec.Tempo;
}

/// <summary>
/// Fight songs A (run 161, Danny's note <c>danny-fight-songs-nov22</c>): an original fight song for every program, composed
/// from its name. Pure and deterministic: the same school always gets the same tune, and nothing here touches the game's
/// random stream (a hash of the name seeds a private generator). <see cref="Assign"/> spreads the styles and keys so no two
/// programs in one conference share both. The output is our own generated work, so it can ship in the public repo.
/// Real public-domain songs (tier 1) come as note data in fight songs B; Danny's own files (tier 3) override both.
/// </summary>
public static class FightSongs
{
    /// <summary>Band-friendly keys (the flat keys a marching band plays in): B♭, E♭, F, A♭, C, D♭, G.</summary>
    public static readonly int[] BandKeys = { 10, 3, 5, 8, 0, 1, 7 };

    public static string StyleName(FightStyle s) => s switch
    {
        FightStyle.March => "a 2/4 march",
        FightStyle.SixEight => "a 6/8 march",
        FightStyle.Swing => "a swing tune",
        FightStyle.Shuffle => "a shuffle",
        FightStyle.Rock => "a rock-band chart",
        _ => "a pep stomp",
    };

    /// <summary>FNV-1a over the name (stable across runs and machines, unlike string.GetHashCode).</summary>
    public static uint Hash(string s)
    {
        uint h = 2166136261;
        foreach (char c in s) { h ^= c; h *= 16777619; }
        return h;
    }

    /// <summary>A program's song with no conference to keep apart from (any name).</summary>
    public static FightSongSpec SpecFor(string program) => SpecFor(program, 0);

    private static FightSongSpec SpecFor(string program, int shift)
    {
        uint h = Hash(program);
        var combos = Combos(h);
        var (style, key) = combos[shift % combos.Count];
        return new FightSongSpec(program, style, key, TempoFor(style, h), h);
    }

    /// <summary>Every (style, key) pair in the order this name prefers them (a seeded shuffle).</summary>
    private static List<(FightStyle, int)> Combos(uint h)
    {
        var all = new List<(FightStyle, int)>();
        // Marches are the most common fight song: they get two slots in the draw for every one of the others.
        var styles = new[] { FightStyle.March, FightStyle.March, FightStyle.SixEight, FightStyle.SixEight, FightStyle.Swing, FightStyle.Shuffle, FightStyle.Rock, FightStyle.Stomp };
        var r = new Lcg(h ^ 0x5bd1e995);
        var order = styles.OrderBy(_ => r.Next()).Distinct().ToList();
        var keys = BandKeys.OrderBy(_ => r.Next()).ToList();
        // Interleave so a name's first choices differ in style before they differ in key.
        for (int k = 0; k < keys.Count; k++) foreach (var s in order) all.Add((s, keys[(k + (int)s) % keys.Count]));
        return all;
    }

    private static int TempoFor(FightStyle s, uint h)
    {
        var (lo, hi) = s switch
        {
            FightStyle.March => (122, 144),
            FightStyle.SixEight => (110, 128),
            FightStyle.Swing => (132, 150),
            FightStyle.Shuffle => (112, 128),
            FightStyle.Rock => (128, 148),
            _ => (118, 136),
        };
        return lo + (int)((h >> 8) % (uint)(hi - lo + 1));
    }

    /// <summary>
    /// Every program's song, with no two in one conference sharing both a style and a key: within a conference, programs
    /// pick in name order and each takes its first choice not yet taken.
    /// </summary>
    public static Dictionary<string, FightSongSpec> Assign(IEnumerable<(string Program, string Conference)> programs)
    {
        var result = new Dictionary<string, FightSongSpec>(StringComparer.Ordinal);
        foreach (var conf in programs.GroupBy(p => p.Conference))
        {
            var taken = new HashSet<(FightStyle, int)>();
            foreach (var name in conf.Select(p => p.Program).Distinct().OrderBy(n => n, StringComparer.Ordinal))
            {
                FightSongSpec spec = SpecFor(name);
                // (A conference of more than 42 programs, which no real one is, would share a pair rather than loop forever.)
                for (int shift = 0; taken.Contains((spec.Style, spec.Key)) && shift < 6 * BandKeys.Length; shift++) spec = SpecFor(name, shift + 1);
                taken.Add((spec.Style, spec.Key));
                result[name] = spec;
            }
        }
        return result;
    }

    /// <summary>The program's song within its conference (the members' names, the program among them).</summary>
    public static FightSongSpec For(string program, IEnumerable<string> conferenceMembers) =>
        Assign(conferenceMembers.Append(program).Select(n => (n, "c"))).GetValueOrDefault(program) ?? SpecFor(program);

    // ---------------------------------------------------------------------------------------------------------------
    // The composer. Form: an intro (drum cadence and a fanfare), A, A′, B (the trio), A″ (the big last strain, with the
    // glockenspiel and the countermelody) and a tag. About 30–40 s; the hit is A″ and the tag.

    private static readonly int[] Major = { 0, 2, 4, 5, 7, 9, 11 };

    /// <summary>A chord as scale degrees of its root (0 = I … 6 = vii), with a seventh or not.</summary>
    private readonly record struct Chord(int Root, bool Seventh = false);

    /// <summary>Eight chords per 16-beat strain (two beats each).</summary>
    private static readonly Chord[][] StrainA =
    {
        new Chord[] { new(0), new(0), new(3), new(0), new(1), new(4, true), new(4), new(4, true) },
        new Chord[] { new(0), new(4), new(0), new(3), new(0), new(5), new(1), new(4, true) },
        new Chord[] { new(0), new(0), new(4), new(4, true), new(4), new(4), new(0), new(4, true) },
        new Chord[] { new(0), new(3), new(0), new(5), new(3), new(0), new(1, true), new(4, true) },
    };
    private static readonly Chord[][] StrainB =
    {
        new Chord[] { new(3), new(3), new(0), new(0), new(1, true), new(1, true), new(4), new(4, true) },
        new Chord[] { new(5), new(5), new(1), new(1), new(4), new(4), new(0), new(4, true) },
        new Chord[] { new(3), new(4), new(2), new(5), new(1), new(4, true), new(0), new(4, true) },
    };

    /// <summary>Rhythm cells for two beats, as (start, length) in sub-beats (two a beat, or three in 6/8).</summary>
    private static readonly (int S, int L)[][] DupleCells =
    {
        new[] { (0, 2), (2, 1), (3, 1) },
        new[] { (0, 1), (1, 1), (2, 2) },
        new[] { (0, 3), (3, 1) },
        new[] { (0, 2), (2, 2) },
        new[] { (0, 1), (1, 1), (2, 1), (3, 1) },
        new[] { (0, 4) },
    };
    private static readonly (int S, int L)[][] CompoundCells =
    {
        new[] { (0, 2), (2, 1), (3, 2), (5, 1) },
        new[] { (0, 3), (3, 3) },
        new[] { (0, 1), (1, 1), (2, 1), (3, 3) },
        new[] { (0, 3), (3, 2), (5, 1) },
        new[] { (0, 6) },
    };

    public static FightScore Compose(FightSongSpec spec)
    {
        var r = new Lcg(spec.Seed);
        var notes = new List<BandNote>();
        bool compound = spec.Style == FightStyle.SixEight;
        int sub = compound ? 3 : 2;
        var cells = compound ? CompoundCells : DupleCells;
        var a = StrainA[r.Next(StrainA.Length)];
        var b = StrainB[r.Next(StrainB.Length)];
        // The motif: four rhythm cells and a contour (the first leap up, the fight song's call to arms).
        int[] motifCells = { r.Next(cells.Length - 1), r.Next(cells.Length - 1), r.Next(cells.Length - 1), cells.Length - 1 };
        int leap = 3 + r.Next(2);            // up a fourth or a fifth
        int[] trioCells = { r.Next(cells.Length - 1), r.Next(cells.Length - 1), r.Next(cells.Length - 1), cells.Length - 1 };
        double beat = 0;

        // Intro: a four-beat cadence on the snare (a roll that swells), then a fanfare on V held, and a beat's rest.
        for (int i = 0; i < 16; i++) notes.Add(new BandNote(i * 0.25, 0.25, 0, BandPart.Snare, 0.35 + 0.04 * i));
        notes.Add(new BandNote(4, 3, 0, BandPart.Cymbal, 0.8));
        notes.Add(new BandNote(4, 0.5, 0, BandPart.Kick, 1));
        foreach (int m in ChordTones(spec.Key, new Chord(4, true), 3)) notes.Add(new BandNote(4, 3, m, BandPart.Harmony, 0.9));
        notes.Add(new BandNote(4, 3, Pitch(spec.Key, 4, 4), BandPart.Lead, 0.95));
        notes.Add(new BandNote(4, 3, Pitch(spec.Key, 4, 2), BandPart.Bass, 0.9));
        beat = 8;

        var mel = Strain(spec, a, motifCells, cells, sub, leap, r, endOnTonic: false);
        void Lay(List<(double B, double L, int D)> line, Chord[] chords, double at, bool big)
        {
            foreach (var (nb, nl, d) in line)
            {
                notes.Add(new BandNote(at + Swing(spec.Style, nb), nl * 0.92, Pitch(spec.Key, d, 4), BandPart.Lead, big ? 1.0 : 0.85));
                if (big || spec.Style is FightStyle.March or FightStyle.SixEight && at < 24)
                    notes.Add(new BandNote(at + Swing(spec.Style, nb), Math.Min(nl, 0.5), Pitch(spec.Key, d, 6), BandPart.Glock, big ? 0.7 : 0.45));
            }
            Accompany(notes, spec, chords, at, big);
        }
        Lay(mel, a, beat, false); beat += 16;
        var mel2 = Strain(spec, a, motifCells, cells, sub, leap, new Lcg(spec.Seed ^ 0xA5A5), endOnTonic: true);
        Lay(mel2, a, beat, false); beat += 16;
        // The trio (B): its own motif, softer, the drums lighter.
        var trio = Strain(spec, b, trioCells, cells, sub, leap - 1, new Lcg(spec.Seed ^ 0x3C3C), endOnTonic: false);
        Lay(trio, b, beat, false); beat += 16;
        double hit = beat;
        // A″: the big last strain: everyone, the glockenspiel and the horns' countermelody.
        notes.Add(new BandNote(beat, 2, 0, BandPart.Cymbal, 0.9));
        Lay(mel2, a, beat, true);
        for (int c = 0; c < 8; c++)
        {
            var ch = a[c];
            int third = ch.Root + 2;
            notes.Add(new BandNote(beat + 2 * c, 1.9, Pitch(spec.Key, third, 3), BandPart.Counter, 0.75));
        }
        beat += 16;
        // The tag: "hey!" on I, the cymbal and the kick, held.
        foreach (int m in ChordTones(spec.Key, new Chord(0), 3)) notes.Add(new BandNote(beat, 2.5, m, BandPart.Harmony, 1));
        notes.Add(new BandNote(beat, 2.5, Pitch(spec.Key, 7, 4), BandPart.Lead, 1));
        notes.Add(new BandNote(beat, 2.5, Pitch(spec.Key, 0, 2), BandPart.Bass, 1));
        notes.Add(new BandNote(beat, 3, 0, BandPart.Cymbal, 1));
        notes.Add(new BandNote(beat, 0.5, 0, BandPart.Kick, 1));
        notes.Add(new BandNote(beat, 0.5, 0, BandPart.Snare, 1));
        beat += 4;
        return new FightScore(spec, beat, notes.OrderBy(n => n.Beat).ToList(), hit);
    }

    /// <summary>A strain's melody as (beat, length, scale degree): chord tones on the strong notes, steps between.</summary>
    private static List<(double, double, int)> Strain(FightSongSpec spec, Chord[] chords, int[] motif, (int S, int L)[][] cells, int sub,
        int leap, Lcg r, bool endOnTonic)
    {
        var line = new List<(double, double, int)>();
        int d = 2;   // start on the third, a step under the leap
        // Bars 1–4: the motif; bars 5–8: the motif again, its last cell the cadence.
        for (int c = 0; c < 8; c++)
        {
            var chord = chords[c];
            var cell = cells[motif[c % 4]];
            if (c == 7) cell = new[] { (0, 2 * sub) };
            for (int k = 0; k < cell.Length; k++)
            {
                var (s, l) = cell[k];
                double b = 2 * c + (double)s / sub;
                int target;
                if (c == 0 && k == 0) target = d;
                else if (c % 4 == 0 && k == 1) target = d + leap;   // the leap
                else if (k == 0) target = NearestChordTone(chord, d + (c % 4 < 2 ? 1 : -1));
                else target = d + (r.Next(3) - 1 == 0 ? (c % 2 == 0 ? 1 : -1) : r.Next(2) == 0 ? 1 : -1);
                if (c == 7) target = endOnTonic ? NearestOf(d, 0, 7) : Math.Clamp(NearestChordTone(new Chord(4), d), 1, 11);
                d = Math.Clamp(target, 0, 11);
                line.Add((b, (double)l / sub, d));
            }
        }
        return line;
    }

    private static int NearestOf(int d, params int[] options)
    {
        int best = options[0]; foreach (int o in options) if (Math.Abs(o - d) < Math.Abs(best - d)) best = o;
        return best;
    }

    private static int NearestChordTone(Chord c, int d)
    {
        int best = 1000;
        for (int o = -14; o <= 14; o += 7)
            foreach (int t in new[] { c.Root, c.Root + 2, c.Root + 4 })
                if (Math.Abs(t + o - d) < Math.Abs(best - d)) best = t + o;
        return best;
    }

    /// <summary>The tuba, the horns' chords and the drumline under one strain, by style.</summary>
    private static void Accompany(List<BandNote> notes, FightSongSpec spec, Chord[] chords, double at, bool big)
    {
        double v = big ? 1.0 : 0.8;
        for (int c = 0; c < 8; c++)
        {
            var ch = chords[c];
            double b0 = at + 2 * c;
            int root = Pitch(spec.Key, ch.Root, 2), fifth = Pitch(spec.Key, ch.Root + 4, 2);
            var tones = ChordTones(spec.Key, ch, 3);
            switch (spec.Style)
            {
                case FightStyle.March:
                    notes.Add(new BandNote(b0, 0.45, root, BandPart.Bass, v));
                    notes.Add(new BandNote(b0 + 1, 0.45, fifth - 12 < 33 ? fifth : fifth - 12, BandPart.Bass, v * 0.85));
                    foreach (int m in tones) { notes.Add(new BandNote(b0 + 0.5, 0.3, m, BandPart.Harmony, v * 0.6)); notes.Add(new BandNote(b0 + 1.5, 0.3, m, BandPart.Harmony, v * 0.6)); }
                    for (int e = 0; e < 4; e++) notes.Add(new BandNote(b0 + 0.5 * e, 0.25, 0, BandPart.Snare, (e == 0 ? 0.8 : 0.45) * v));
                    notes.Add(new BandNote(b0, 0.5, 0, BandPart.Kick, v));
                    break;
                case FightStyle.SixEight:
                    notes.Add(new BandNote(b0, 0.6, root, BandPart.Bass, v));
                    notes.Add(new BandNote(b0 + 1, 0.6, fifth - 12 < 33 ? fifth : fifth - 12, BandPart.Bass, v * 0.85));
                    foreach (int m in tones) { notes.Add(new BandNote(b0 + 2.0 / 3, 0.3, m, BandPart.Harmony, v * 0.55)); notes.Add(new BandNote(b0 + 5.0 / 3, 0.3, m, BandPart.Harmony, v * 0.55)); }
                    for (int e = 0; e < 6; e++) notes.Add(new BandNote(b0 + e / 3.0, 0.2, 0, BandPart.Snare, (e % 3 == 0 ? 0.8 : 0.4) * v));
                    notes.Add(new BandNote(b0, 0.5, 0, BandPart.Kick, v));
                    notes.Add(new BandNote(b0 + 1, 0.5, 0, BandPart.Kick, v * 0.7));
                    break;
                case FightStyle.Swing:
                {
                    // A walking tuba: root, third, fifth, a step to the next root.
                    int third = Pitch(spec.Key, ch.Root + 2, 2);
                    notes.Add(new BandNote(b0, 0.9, root, BandPart.Bass, v));
                    notes.Add(new BandNote(b0 + 1, 0.9, c % 2 == 0 ? third : fifth, BandPart.Bass, v * 0.85));
                    foreach (int m in tones) notes.Add(new BandNote(b0 + Swing(spec.Style, 0.5), 0.4, m, BandPart.Harmony, v * 0.6));
                    notes.Add(new BandNote(b0 + 1, 0.25, 0, BandPart.Snare, 0.55 * v));
                    notes.Add(new BandNote(b0, 0.5, 0, BandPart.Kick, 0.6 * v));
                    notes.Add(new BandNote(b0, 0.5, 0, BandPart.Cymbal, 0.2 * v));
                    notes.Add(new BandNote(b0 + 1, 0.5, 0, BandPart.Cymbal, 0.2 * v));
                    break;
                }
                case FightStyle.Shuffle:
                    notes.Add(new BandNote(b0, 0.6, root, BandPart.Bass, v));
                    notes.Add(new BandNote(b0 + 2.0 / 3, 0.3, root + 12, BandPart.Bass, v * 0.7));
                    notes.Add(new BandNote(b0 + 1, 0.6, fifth, BandPart.Bass, v * 0.85));
                    notes.Add(new BandNote(b0 + 5.0 / 3, 0.3, fifth, BandPart.Bass, v * 0.7));
                    foreach (int m in tones) notes.Add(new BandNote(b0, 1.9, m, BandPart.Harmony, v * 0.45));
                    notes.Add(new BandNote(b0 + 1, 0.25, 0, BandPart.Snare, 0.85 * v));
                    notes.Add(new BandNote(b0, 0.5, 0, BandPart.Kick, v));
                    break;
                case FightStyle.Rock:
                    for (int e = 0; e < 4; e++) notes.Add(new BandNote(b0 + 0.5 * e, 0.45, root, BandPart.Bass, v * (e == 0 ? 1 : 0.75)));
                    foreach (int m in tones) { notes.Add(new BandNote(b0, 0.4, m, BandPart.Harmony, v * 0.6)); notes.Add(new BandNote(b0 + 1.5, 0.4, m, BandPart.Harmony, v * 0.6)); }
                    notes.Add(new BandNote(b0, 0.5, 0, BandPart.Kick, v));
                    notes.Add(new BandNote(b0 + 1, 0.25, 0, BandPart.Snare, 0.9 * v));
                    for (int e = 0; e < 4; e++) notes.Add(new BandNote(b0 + 0.5 * e, 0.25, 0, BandPart.Cymbal, 0.12 * v));
                    break;
                default:   // Stomp
                    notes.Add(new BandNote(b0, 0.9, root, BandPart.Bass, v));
                    notes.Add(new BandNote(b0 + 1, 0.9, root, BandPart.Bass, v * 0.9));
                    notes.Add(new BandNote(b0, 0.5, 0, BandPart.Kick, v));
                    notes.Add(new BandNote(b0 + 1, 0.5, 0, BandPart.Kick, v));
                    notes.Add(new BandNote(b0 + 1, 0.25, 0, BandPart.Snare, v));
                    foreach (int m in tones) notes.Add(new BandNote(b0 + 1.5, 0.3, m, BandPart.Harmony, v * 0.6));
                    break;
            }
        }
    }

    /// <summary>Swung offbeats: an eighth on the "and" lands two-thirds through the beat in a swing tune.</summary>
    private static double Swing(FightStyle s, double b)
    {
        if (s != FightStyle.Swing) return b;
        double frac = b - Math.Floor(b);
        return Math.Abs(frac - 0.5) < 1e-6 ? Math.Floor(b) + 2.0 / 3 : b;
    }

    /// <summary>MIDI pitch of a scale degree (any integer; 7 = the tonic an octave up) in <paramref name="octave"/>.</summary>
    public static int Pitch(int key, int degree, int octave)
    {
        int o = (int)Math.Floor(degree / 7.0);
        int d = degree - 7 * o;
        // The key's tonic sits in the given octave, at or below F♯ so high keys don't climb out of the band's range.
        int tonic = 12 * (octave + 1) + key - (key > 6 ? 12 : 0);
        return tonic + Major[d] + 12 * o;
    }

    private static IEnumerable<int> ChordTones(int key, Chord c, int octave)
    {
        yield return Pitch(key, c.Root, octave);
        yield return Pitch(key, c.Root + 2, octave);
        yield return Pitch(key, c.Root + 4, octave);
        if (c.Seventh) yield return Pitch(key, c.Root + 6, octave);
    }

    /// <summary>A tiny private generator (the composer's own; never the game's random stream).</summary>
    private sealed class Lcg
    {
        private uint _s;
        public Lcg(uint seed) { _s = seed == 0 ? 1u : seed; }
        public int Next() { _s = _s * 1664525u + 1013904223u; return (int)(_s >> 1); }
        public int Next(int n) => (int)((uint)Next() % (uint)n);
    }
}
