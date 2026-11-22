namespace Hardwood.Desktop.Audio;

/// <summary>
/// The two code-made sounds (M6, run 153; design §F), synthesised once at start-up as 44.1 kHz stereo float samples
/// (interleaved L, R). No pack covers them yet; squeaks, dribbles and rim hits wait for one and are not faked.
/// </summary>
public static class Synth
{
    public const int Rate = 44100;

    /// <summary>The period buzzer: a band-limited square wave around 220 Hz with a little 330 Hz, about 1 s, with short
    /// fades so it doesn't click.</summary>
    public static float[] Buzzer(double seconds = 1.0)
    {
        int n = (int)(seconds * Rate);
        var s = new float[n * 2];
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / Rate;
            double v = 0;
            // The odd harmonics of a 220 Hz square, up to 15 kHz, plus a quieter fifth on top for the arena's rasp.
            for (int h = 1; h * 220 < 15000; h += 2) v += Math.Sin(2 * Math.PI * 220 * h * t) / h;
            for (int h = 1; h * 330 < 15000; h += 2) v += 0.35 * Math.Sin(2 * Math.PI * 330 * h * t) / h;
            v *= 0.30 * Fade(t, seconds, 0.012, 0.05);
            s[2 * i] = s[2 * i + 1] = (float)v;
        }
        return s;
    }

    /// <summary>A referee's pea whistle: a tone near 3.1 kHz trilled by the pea (about 28 Hz, amplitude and a little
    /// pitch), about 0.55 s.</summary>
    public static float[] Whistle(double seconds = 0.55)
    {
        int n = (int)(seconds * Rate);
        var s = new float[n * 2];
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / Rate;
            double trill = Math.Sin(2 * Math.PI * 28 * t);
            double f = 3100 + 90 * trill;
            phase += 2 * Math.PI * f / Rate;
            double v = Math.Sin(phase) + 0.18 * Math.Sin(2 * phase);
            v *= (0.62 + 0.38 * trill) * 0.22 * Fade(t, seconds, 0.015, 0.08);
            s[2 * i] = s[2 * i + 1] = (float)v;
        }
        return s;
    }

    private static double Fade(double t, double len, double attack, double release) =>
        Math.Min(1, Math.Min(t / attack, (len - t) / release));
}
