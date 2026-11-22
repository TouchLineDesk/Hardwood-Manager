using Hardwood.Core.Presentation;

namespace Hardwood.Desktop.Audio;

/// <summary>
/// Fight songs A (run 161): plays a <see cref="FightScore"/> as a small marching band, offline, to 44.1 kHz stereo floats.
/// Brass from wavetables (a bright and a mellow table per section, crossfaded by a swell on each note, so a note blats on
/// and settles), a glockenspiel from two inharmonic partials, and the drumline from noise and swept sines. A short room
/// (a few comb filters) puts it in an arena. Deterministic: the noise is a fixed generator, so the same score renders the
/// same samples. This is the interim band until VSCO 2 CE's CC0 samples can be fetched (the plan's preferred sampler).
/// </summary>
public static class BandRenderer
{
    private const int Rate = Synth.Rate;
    private const int Table = 2048;

    /// <summary>One wavetable: a cycle with harmonics falling off by <paramref name="roll"/> (higher = darker).</summary>
    private static float[] Wave(int harmonics, double roll, double oddBoost = 1)
    {
        var t = new float[Table];
        for (int h = 1; h <= harmonics; h++)
        {
            double a = Math.Pow(h, -roll) * (h % 2 == 1 ? oddBoost : 1);
            for (int i = 0; i < Table; i++) t[i] += (float)(a * Math.Sin(2 * Math.PI * h * i / Table));
        }
        float peak = t.Max(MathF.Abs);
        for (int i = 0; i < Table; i++) t[i] /= peak;
        return t;
    }

    private sealed record Timbre(float[] Bright, float[] Mellow, double Attack, double Release, double Gain, double Pan, double Detune, double Vibrato);

    private static readonly Lazy<Dictionary<BandPart, Timbre>> Timbres = new(() => new()
    {
        // Trumpets: bright, a quick blat, two players a few cents apart, a little vibrato on long notes.
        [BandPart.Lead] = new(Wave(18, 0.9), Wave(8, 1.4), 0.025, 0.07, 0.30, -0.15, 0.004, 0.006),
        // Horns: round, slow to speak.
        [BandPart.Counter] = new(Wave(8, 1.5), Wave(4, 2.0), 0.06, 0.12, 0.20, 0.35, 0.003, 0.004),
        // Trombones: a darker brass.
        [BandPart.Harmony] = new(Wave(12, 1.15), Wave(6, 1.7), 0.03, 0.08, 0.11, 0.3, 0.003, 0.0),
        // Tuba / sousaphones: fat and low.
        [BandPart.Bass] = new(Wave(8, 1.4, 1.2), Wave(4, 2.2), 0.035, 0.08, 0.34, 0.0, 0.0, 0.0),
    });

    /// <summary>Render the whole chart (or from <paramref name="fromBeat"/> on: the hit).</summary>
    public static float[] Render(FightScore score, double fromBeat = 0, double gain = 0.8)
    {
        double spb = 60.0 / score.Spec.Tempo;
        double length = (score.Beats - fromBeat) * spb + 1.6;   // the tail of the room
        int frames = (int)(length * Rate);
        var l = new float[frames]; var r = new float[frames];
        var noise = new Noise(score.Spec.Seed);
        foreach (var n in score.Notes)
        {
            if (n.Beat < fromBeat - 1e-9) continue;
            double t0 = (n.Beat - fromBeat) * spb, dur = n.Beats * spb;
            switch (n.Part)
            {
                case BandPart.Glock: Glock(l, r, t0, n.Midi, n.Vel); break;
                case BandPart.Snare: Snare(l, r, t0, n.Vel, noise); break;
                case BandPart.Kick: Kick(l, r, t0, n.Vel); break;
                case BandPart.Cymbal: Cymbal(l, r, t0, Math.Max(0.25, dur), n.Vel, noise); break;
                default: Brass(l, r, t0, dur, n.Midi, n.Vel, Timbres.Value[n.Part]); break;
            }
        }
        Room(l, r);
        // Normalise the peak to the gain, then interleave.
        float peak = 1e-6f;
        for (int i = 0; i < frames; i++) peak = Math.Max(peak, Math.Max(MathF.Abs(l[i]), MathF.Abs(r[i])));
        float k = (float)(gain / peak);
        var o = new float[frames * 2];
        for (int i = 0; i < frames; i++) { o[2 * i] = l[i] * k; o[2 * i + 1] = r[i] * k; }
        // A short fade at the very end, so the room's tail never clicks.
        int fade = Math.Min(frames, Rate / 4);
        for (int f = 0; f < fade; f++) { float g = (float)f / fade; int i = 2 * (frames - 1 - f); o[i] *= g; o[i + 1] *= g; }
        return o;
    }

    private static double Hz(int midi) => 440 * Math.Pow(2, (midi - 69) / 12.0);

    private static void Add(float[] l, float[] r, int i, double v, double pan)
    {
        if ((uint)i >= (uint)l.Length) return;
        l[i] += (float)(v * (pan > 0 ? 1 - 0.7 * pan : 1));
        r[i] += (float)(v * (pan < 0 ? 1 + 0.7 * pan : 1));
    }

    private static void Brass(float[] l, float[] r, double t0, double dur, int midi, double vel, Timbre tb)
    {
        int s0 = (int)(t0 * Rate), n = (int)((dur + tb.Release) * Rate);
        double f = Hz(midi);
        double[] players = tb.Detune > 0 ? new[] { 1 - tb.Detune, 1 + tb.Detune } : new[] { 1.0 };
        double amp = tb.Gain * vel / players.Length;
        var ph = new double[players.Length];
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / Rate;
            double env = t < tb.Attack ? t / tb.Attack : t < dur ? 1 - 0.15 * Math.Min(1, (t - tb.Attack) / 0.4) : Math.Max(0, 0.85 * (1 - (t - dur) / tb.Release));
            if (env <= 0 && t > dur) break;
            // The blat: bright as the note speaks, mellowing over a fifth of a second; brighter when played hard.
            double bright = Math.Clamp(0.35 + 0.65 * vel * Math.Exp(-t / 0.2) + 0.25 * vel, 0, 1);
            double vib = t > 0.25 ? 1 + tb.Vibrato * Math.Sin(2 * Math.PI * 5.5 * t) : 1;
            double v = 0;
            for (int p = 0; p < players.Length; p++)
            {
                ph[p] += f * players[p] * vib / Rate;
                ph[p] -= Math.Floor(ph[p]);
                int idx = (int)(ph[p] * Table) & (Table - 1);
                v += tb.Bright[idx] * bright + tb.Mellow[idx] * (1 - bright);
            }
            Add(l, r, s0 + i, v * env * amp, tb.Pan);
        }
    }

    private static void Glock(float[] l, float[] r, double t0, int midi, double vel)
    {
        int s0 = (int)(t0 * Rate), n = (int)(1.2 * Rate);
        double f = Hz(midi);
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / Rate;
            double v = Math.Sin(2 * Math.PI * f * t) * Math.Exp(-t * 3.5) + 0.35 * Math.Sin(2 * Math.PI * f * 2.76 * t) * Math.Exp(-t * 9);
            Add(l, r, s0 + i, 0.09 * vel * v * Math.Min(1, t / 0.002), 0.45);
        }
    }

    private static void Snare(float[] l, float[] r, double t0, double vel, Noise noise)
    {
        int s0 = (int)(t0 * Rate), n = (int)(0.16 * Rate);
        double prev = 0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / Rate;
            double w = noise.Next();
            double hp = w - prev; prev = w;   // the snares' rattle: noise, high-passed
            double v = 0.6 * hp * Math.Exp(-t / 0.05) + 0.5 * Math.Sin(2 * Math.PI * 190 * t) * Math.Exp(-t / 0.03);
            Add(l, r, s0 + i, 0.16 * vel * v, 0.05);
        }
    }

    private static void Kick(float[] l, float[] r, double t0, double vel)
    {
        int s0 = (int)(t0 * Rate), n = (int)(0.35 * Rate);
        double ph = 0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / Rate;
            ph += (48 + 70 * Math.Exp(-t / 0.04)) / Rate;
            Add(l, r, s0 + i, 0.32 * vel * Math.Sin(2 * Math.PI * ph) * Math.Exp(-t / 0.12), 0);
        }
    }

    private static void Cymbal(float[] l, float[] r, double t0, double dur, double vel, Noise noise)
    {
        int s0 = (int)(t0 * Rate), n = (int)(Math.Min(2.5, dur + 0.6) * Rate);
        double p1 = 0, p2 = 0;
        double decay = dur < 0.5 ? 0.12 : 0.8;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / Rate;
            double w = noise.Next();
            double hp = w - 2 * p1 + p2; p2 = p1; p1 = w;   // brighter still: a second difference
            Add(l, r, s0 + i, 0.05 * vel * hp * Math.Exp(-t / decay) * Math.Min(1, t / 0.003), -0.2);
        }
    }

    /// <summary>A small arena: four feedback combs per side, mixed under the dry band.</summary>
    private static void Room(float[] l, float[] r)
    {
        int[] dl = { 1557, 1617, 1491, 1422 }, dr = { 1580, 1640, 1514, 1445 };
        var wl = Comb(l, dl); var wr = Comb(r, dr);
        for (int i = 0; i < l.Length; i++) { l[i] += 0.22f * wl[i]; r[i] += 0.22f * wr[i]; }
    }

    private static float[] Comb(float[] x, int[] delays)
    {
        var o = new float[x.Length];
        foreach (int d in delays)
        {
            var buf = new float[x.Length];
            for (int i = 0; i < x.Length; i++) buf[i] = x[i] + (i >= d ? 0.78f * buf[i - d] : 0);
            for (int i = 0; i < x.Length; i++) o[i] += buf[i] * 0.25f;
        }
        return o;
    }

    /// <summary>The drumline's noise: a fixed generator, so a song renders the same every time.</summary>
    private sealed class Noise
    {
        private uint _s;
        public Noise(uint seed) { _s = seed | 1; }
        public double Next() { _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5; return _s / (double)uint.MaxValue * 2 - 1; }
    }
}
