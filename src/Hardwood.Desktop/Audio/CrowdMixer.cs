using NAudio.Wave;
using NVorbis;

namespace Hardwood.Desktop.Audio;

/// <summary>
/// The live game's sound, as one NAudio sample provider (44.1 kHz stereo float): the two crowd beds looping all game
/// (streamed from the embedded OGGs with NVorbis), eased toward the mix the view model asks for, the one-shot reactions
/// on top, and the master volume. It needs no audio device, so tests render it offline. Thread-safe: the view model
/// calls <see cref="SetBed"/>/<see cref="Play"/> on the UI thread while the device's thread calls <see cref="Read"/>.
/// </summary>
public sealed class CrowdMixer : ISampleProvider, IDisposable
{
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(Synth.Rate, 2);

    private readonly LoopingOgg? _low, _high;
    private readonly Dictionary<Cue, float[][]> _shots = new();
    private readonly List<Voice> _voices = new();
    private readonly object _gate = new();
    private BedLevels _bed, _target;
    private readonly List<(double Age, double Depth)> _groans = new();
    /// <summary>Boos while no boo file is embedded: the low bed swells under the whistle (run 157).</summary>
    private readonly List<(double Age, double Depth)> _swells = new();
    private int _cheerTurn;
    private float[] _lowBuf = Array.Empty<float>(), _highBuf = Array.Empty<float>();
    private bool _disposed;
    /// <summary>The master gain as it ramps (mute, minimise and volume changes glide over about 20 ms, so they don't click).</summary>
    private float _master;
    /// <summary>A clip cut short (the oldest voice stolen, or the reactions stopped) fades over this many frames.</summary>
    private const int CutFade = 441;

    /// <summary>0–1, applied to everything.</summary>
    public double Volume { get; set; } = 0.7;
    public bool Muted { get; set; }
    /// <summary>The window is minimised: silence (the beds keep their place).</summary>
    public bool Suspended { get; set; }
    /// <summary>The bed's current (eased) levels, for tests.</summary>
    public BedLevels Bed { get { lock (_gate) return _bed; } }
    public int Voices { get { lock (_gate) return _voices.Count; } }

    public CrowdMixer()
    {
        _low = LoopingOgg.Open("crowd-bed-low.ogg");
        _high = LoopingOgg.Open("crowd-bed-high.ogg");
        _shots[Cue.Cheer] = new[] { Decode("crowd-cheer-1.ogg"), Decode("crowd-cheer-3.ogg") }.Where(x => x.Length > 0).ToArray();
        _shots[Cue.BigCheer] = new[] { Decode("crowd-cheer-2.ogg") }.Where(x => x.Length > 0).ToArray();
        _shots[Cue.Applause] = new[] { Decode("crowd-applause.ogg") }.Where(x => x.Length > 0).ToArray();
        _shots[Cue.FanWhistle] = new[] { Decode("crowd-fan-whistle.ogg") }.Where(x => x.Length > 0).ToArray();
        // Run 157: boos come from the intake's files (crowd-boo-1.ogg …) once they're embedded; until then, the bed swells.
        _shots[Cue.Boo] = Enumerable.Range(1, 4).Select(n => Decode($"crowd-boo-{n}.ogg")).Where(x => x.Length > 0).ToArray();
        _shots[Cue.Whistle] = new[] { Synth.Whistle() };
        _shots[Cue.Buzzer] = new[] { Synth.Buzzer() };
    }

    /// <summary>The bed's target mix; the mixer eases toward it.</summary>
    public void SetBed(BedLevels target) { lock (_gate) _target = target; }

    /// <summary>Play one reaction now.</summary>
    public void Play(Cue cue, double gain)
    {
        lock (_gate)
        {
            if (cue == Cue.Groan) { _groans.Add((0, gain)); return; }
            if (cue == Cue.Boo && _shots[Cue.Boo].Length == 0) { _swells.Add((0, gain)); return; }
            if (!_shots.TryGetValue(cue, out var set) || set.Length == 0) return;
            var clip = set[cue is Cue.Cheer or Cue.Boo ? _cheerTurn++ % set.Length : 0];
            // Applause and cheers sit a little under the code-made whistle and buzzer, which cut through.
            float g = (float)(gain * (cue is Cue.Whistle or Cue.Buzzer ? 0.9 : 0.75));
            // At most 6 reactions at once; the oldest gives way.
            var live = _voices.Where(v => v.FadeLeft < 0).ToList();
            if (live.Count >= 6) live[0].Cut();
            _voices.Add(new Voice(clip, g));
        }
    }

    /// <summary>Stop the one-shots (a jump in the game, the final, leaving the page).</summary>
    public void StopReactions() { lock (_gate) { foreach (var v in _voices) v.Cut(); _groans.Clear(); _swells.Clear(); } }

    public int Read(float[] buffer, int offset, int count)
    {
        int frames = count / 2;
        lock (_gate)
        {
            if (_disposed) { Array.Clear(buffer, offset, count); return count; }
            if (_lowBuf.Length < count) { _lowBuf = new float[count]; _highBuf = new float[count]; }
            _low?.Read(_lowBuf, count);
            _high?.Read(_highBuf, count);
            // The bed eases once per 64 frames (1.5 ms): smooth, and cheap.
            const int Block = 64;
            float masterTarget = Muted || Suspended ? 0f : (float)Math.Clamp(Volume, 0, 1);
            for (int f0 = 0; f0 < frames; f0 += Block)
            {
                int f1 = Math.Min(frames, f0 + Block);
                double dt = (double)(f1 - f0) / Synth.Rate;
                _bed = CrowdMix.Ease(_bed, _target, dt);
                double dip = 1;
                for (int g = _groans.Count - 1; g >= 0; g--)
                {
                    var (age, depth) = _groans[g];
                    dip = Math.Min(dip, CrowdMix.Groan(age, depth));
                    age += dt;
                    if (age >= CrowdMix.GroanTime) _groans.RemoveAt(g); else _groans[g] = (age, depth);
                }
                double swell = 1;
                for (int g = _swells.Count - 1; g >= 0; g--)
                {
                    var (age, depth) = _swells[g];
                    swell = Math.Max(swell, CrowdMix.Swell(age, depth));
                    age += dt;
                    if (age >= CrowdMix.SwellTime) _swells.RemoveAt(g); else _swells[g] = (age, depth);
                }
                // The swell lifts the low bed by part of the whole bed (the murmur turns to a rumble).
                float lo = (float)((_bed.Low + (swell - 1) * 0.6 * _bed.Total) * dip), hi = (float)(_bed.High * dip);
                for (int f = f0; f < f1; f++)
                {
                    int i = 2 * f;
                    buffer[offset + i] = (_lowBuf[i] * lo + _highBuf[i] * hi);
                    buffer[offset + i + 1] = (_lowBuf[i + 1] * lo + _highBuf[i + 1] * hi);
                }
            }
            for (int v = _voices.Count - 1; v >= 0; v--)
            {
                var voice = _voices[v];
                int n = Math.Min(count, voice.Clip.Length - voice.Pos);
                if (voice.FadeLeft >= 0) n = Math.Min(n, voice.FadeLeft * 2);
                for (int i = 0; i < n; i++)
                {
                    float g = voice.Gain;
                    if (voice.FadeLeft >= 0) g *= (voice.FadeLeft - i / 2) / (float)CutFade;
                    buffer[offset + i] += voice.Clip[voice.Pos + i] * g;
                }
                voice.Pos += n;
                if (voice.FadeLeft >= 0) voice.FadeLeft -= n / 2;
                if (voice.Pos >= voice.Clip.Length || voice.FadeLeft == 0) _voices.RemoveAt(v);
            }
            // The master glides to its target over about 20 ms (mute, minimise, the slider), then a soft knee above 0.8,
            // continuous, so a cheer on a loud bed never clips or crackles.
            float step = 1f / 880;
            for (int i = 0; i < count; i += 2)
            {
                _master = _master < masterTarget ? Math.Min(masterTarget, _master + step) : Math.Max(masterTarget, _master - step);
                buffer[offset + i] = Knee(buffer[offset + i] * _master);
                buffer[offset + i + 1] = Knee(buffer[offset + i + 1] * _master);
            }
        }
        return count;
    }

    /// <summary>Unity below 0.8, then a tanh knee toward 1.0: continuous and smooth at the threshold.</summary>
    public static float Knee(float x)
    {
        float a = MathF.Abs(x);
        if (a <= 0.8f) return x;
        return MathF.Sign(x) * (0.8f + 0.2f * MathF.Tanh((a - 0.8f) / 0.2f));
    }

    /// <summary>Under the lock, so the device's thread is never decoding while the readers go.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _low?.Dispose(); _high?.Dispose();
        }
    }

    private sealed class Voice
    {
        public readonly float[] Clip; public readonly float Gain; public int Pos;
        /// <summary>Frames left in a cut's fade, or −1 while it plays out.</summary>
        public int FadeLeft = -1;
        public Voice(float[] clip, float gain) { Clip = clip; Gain = gain; }
        public void Cut() { if (FadeLeft < 0) FadeLeft = CutFade; }
    }

    /// <summary>An embedded crowd OGG (Assets/Audio/Crowd), or null if it's missing.</summary>
    public static Stream? Resource(string file)
    {
        var asm = typeof(CrowdMixer).Assembly;
        var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("Crowd." + file, StringComparison.OrdinalIgnoreCase));
        return name == null ? null : asm.GetManifestResourceStream(name);
    }

    /// <summary>Decode a whole OGG to 44.1 kHz stereo floats (the reactions are short: 2–7 s).</summary>
    public static float[] Decode(string file)
    {
        using var s = Resource(file);
        if (s == null) return Array.Empty<float>();
        using var r = new VorbisReader(s, closeOnDispose: false);
        if (r.SampleRate != Synth.Rate || r.Channels != 2) return Array.Empty<float>();
        var all = new List<float>((int)(r.TotalSamples * 2));
        var buf = new float[8192];
        int got;
        while ((got = r.ReadSamples(buf, 0, buf.Length)) > 0) all.AddRange(buf.AsSpan(0, got).ToArray());
        return all.ToArray();
    }

    /// <summary>A bed that loops forever, decoded as it plays (the beds are 45 and 55 s; decoding them whole would hold
    /// about 35 MB).</summary>
    private sealed class LoopingOgg : IDisposable
    {
        private readonly VorbisReader _r;
        private readonly Stream _s;
        private LoopingOgg(Stream s, VorbisReader r) { _s = s; _r = r; }
        public static LoopingOgg? Open(string file)
        {
            var s = Resource(file);
            if (s == null) return null;
            var r = new VorbisReader(s, closeOnDispose: false);
            if (r.SampleRate != Synth.Rate || r.Channels != 2) { r.Dispose(); s.Dispose(); return null; }
            return new LoopingOgg(s, r);
        }
        public void Read(float[] into, int count)
        {
            int done = 0, spins = 0;
            while (done < count && spins < 3)
            {
                int got = _r.ReadSamples(into, done, count - done);
                if (got <= 0) { _r.SeekTo(0); spins++; continue; }
                done += got; spins = 0;
            }
            if (done < count) Array.Clear(into, done, count - done);
        }
        public void Dispose() { _r.Dispose(); _s.Dispose(); }
    }
}
