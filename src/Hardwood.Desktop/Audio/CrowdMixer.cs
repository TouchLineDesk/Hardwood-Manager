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
    private readonly Dictionary<Cue, int> _turns = new();
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

    /// <summary>True when the licensed match-day pack (the Splice crowd, court and band, run 160) is in this build: the
    /// release build fetches it from the private repo; dev, CI and fork builds play the M6 files instead.</summary>
    public bool HasPack { get; }

    /// <param name="pack">Where the licensed pack's files come from (tests pass their own; null = the embedded
    /// <c>Licensed.*</c> resources, present only in a release build).</param>
    public CrowdMixer(Func<string, Stream?>? pack = null)
    {
        pack ??= Licensed;
        var bedLow = LoopingOgg.Open(pack("crowd-bed-1.ogg"));
        var bedHigh = bedLow == null ? null : LoopingOgg.Open(pack("crowd-bed-3.ogg"));
        HasPack = bedLow != null && bedHigh != null;
        if (HasPack) { _low = bedLow; _high = bedHigh; }
        else
        {
            bedLow?.Dispose(); bedHigh?.Dispose();
            _low = LoopingOgg.Open(Resource("crowd-bed-low.ogg"));
            _high = LoopingOgg.Open(Resource("crowd-bed-high.ogg"));
        }
        float[][] Pack(double maxSeconds, params string[] files) =>
            HasPack ? files.Select(f => Decode(pack(f), maxSeconds)).Where(x => x.Length > 0).ToArray() : Array.Empty<float[]>();
        float[][] Own(params string[] files) => files.Select(f => Decode(f)).Where(x => x.Length > 0).ToArray();
        float[][] Either(float[][] a, float[][] b) => a.Length > 0 ? a : b;
        string[] Numbered(string stem, int n) => Enumerable.Range(1, n).Select(i => $"{stem}-{i}.ogg").ToArray();

        // The crowd: the Splice arena crowd when the pack is here, else M6's (run 153).
        _shots[Cue.Cheer] = Either(Pack(4.5, "crowd-yeah-1.ogg"), Own("crowd-cheer-1.ogg", "crowd-cheer-3.ogg"));
        _shots[Cue.BigCheer] = Either(Pack(6, "crowd-cheer-1.ogg"), Own("crowd-cheer-2.ogg"));
        _shots[Cue.Erupt] = Either(Pack(7, "crowd-erupt-1.ogg"), _shots[Cue.BigCheer]);
        _shots[Cue.Student] = Pack(7, Numbered("crowd-student", 2));
        _shots[Cue.Oh] = Pack(3, "crowd-oh-1.ogg");
        _shots[Cue.Applause] = Own("crowd-applause.ogg");
        _shots[Cue.FanWhistle] = Own("crowd-fan-whistle.ogg");
        // Boos: the pack's four; until it's here, the bed swells (run 157).
        _shots[Cue.Boo] = Either(Pack(7.5, Numbered("crowd-boo", 4)), Own(Numbered("crowd-boo", 4)));
        _shots[Cue.Whistle] = new[] { Synth.Whistle() };
        _shots[Cue.Buzzer] = new[] { Synth.Buzzer() };
        // The court and the band (run 160): only from the pack, never faked in code (Danny's note).
        _shots[Cue.Bounce] = Pack(1, "court-bounce-1.ogg");
        _shots[Cue.Squeak] = Pack(1, Numbered("court-squeak", 6));
        _shots[Cue.Swish] = Pack(2, "court-swish-1.ogg");
        _shots[Cue.Finish] = Pack(2, "court-shot-bounce-1.ogg");
        _shots[Cue.Rim] = Pack(2.5, Numbered("court-rim", 2));
        _shots[Cue.Backboard] = Pack(2.5, "court-backboard-2.ogg");
        _shots[Cue.Slam] = Pack(2, "court-backboard-1.ogg");
        _shots[Cue.Drumline] = Pack(10, Numbered("band-drumline", 2));
        _shots[Cue.Brass] = Pack(1, "band-brass-stab-1.ogg");
        _shots[Cue.FightSong] = Array.Empty<float[]>();
        _shots[Cue.FightHit] = Array.Empty<float[]>();
    }

    /// <summary>The home band's song (fight songs A, run 161): the whole song and its hit. Without one (the pros, or
    /// while it renders) a fight-song cue plays the drumline instead and a hit plays nothing.</summary>
    public void SetBand(BandSong? song)
    {
        lock (_gate)
        {
            _shots[Cue.FightSong] = song == null ? Array.Empty<float[]>() : new[] { song.Song };
            _shots[Cue.FightHit] = song == null ? Array.Empty<float[]>() : new[] { song.Hit };
            _bandExpected = false;
            // Review (run 161): the tip's song was asked for while the band was still rendering: it starts now, if the
            // ask was in the last few seconds.
            if (song != null && _pendingSong is { } at && (DateTime.UtcNow - at.When).TotalSeconds < PendingSeconds)
            {
                _pendingSong = null;
                Play(Cue.FightSong, at.Gain);
            }
            _pendingSong = null;
        }
    }

    /// <summary>A band is on its way (rendering): until it arrives, a fight-song cue waits rather than play the drumline.</summary>
    public void ExpectBand() { lock (_gate) _bandExpected = true; }
    private bool _bandExpected;
    private (DateTime When, double Gain)? _pendingSong;
    /// <summary>A song asked for this long before the band is ready still plays; later, it's let go.</summary>
    public const double PendingSeconds = 8;

    /// <summary>The bed's target mix; the mixer eases toward it.</summary>
    public void SetBed(BedLevels target) { lock (_gate) _target = target; }

    /// <summary>Play one reaction now, in the middle.</summary>
    public void Play(Cue cue, double gain) => Play(cue, gain, 0);

    /// <summary>Play one sound now. <paramref name="pan"/> is −1 (left) to 1 (right): the court's sounds come from where
    /// the play is (run 160); the crowd is all round.</summary>
    public void Play(Cue cue, double gain, double pan)
    {
        lock (_gate)
        {
            if (cue == Cue.Groan) { _groans.Add((0, gain)); return; }
            if (cue == Cue.Boo && _shots[Cue.Boo].Length == 0) { _swells.Add((0, gain)); return; }
            if (cue == Cue.FightSong && _shots[Cue.FightSong].Length == 0)
            {
                if (_bandExpected) { _pendingSong = (DateTime.UtcNow, gain); return; }
                cue = Cue.Drumline;
            }
            // One song at a time: a new song or hit takes over from one still playing.
            if (cue is Cue.FightSong or Cue.FightHit) foreach (var v in _voices.Where(v => v.Band)) v.Cut();
            if (!_shots.TryGetValue(cue, out var set) || set.Length == 0) return;
            int turn = _turns.GetValueOrDefault(cue);
            _turns[cue] = turn + 1;
            var clip = set[turn % set.Length];
            // Applause and cheers sit a little under the code-made whistle and buzzer, which cut through; the court's
            // sounds and the band sit under the crowd (the starting point is the approved demo mix, run 160).
            float g = (float)(gain * Level(cue));
            // At most 8 sounds at once; the oldest court sound gives way first (review: a run of dribbles mustn't cut the
            // eruption or the band), else the oldest.
            var live = _voices.Where(v => v.FadeLeft < 0).ToList();
            if (live.Count >= MaxVoices) (live.FirstOrDefault(v => v.Court) ?? live.FirstOrDefault(v => !v.Band) ?? live[0]).Cut();
            _voices.Add(new Voice(clip, g, pan) { Band = cue is Cue.FightSong or Cue.FightHit, Court = cue is Cue.Bounce or Cue.Squeak or Cue.Swish or Cue.Finish or Cue.Rim or Cue.Backboard or Cue.Slam });
        }
    }

    /// <summary>At most this many one-shots at once (6 in M6; the court's sounds need a little more room).</summary>
    public const int MaxVoices = 8;

    /// <summary>Each sound's level under its cue's gain.</summary>
    public static double Level(Cue cue) => cue switch
    {
        Cue.Whistle or Cue.Buzzer => 0.9,
        Cue.Bounce or Cue.Squeak => 0.45,
        Cue.Swish or Cue.Finish or Cue.Rim or Cue.Backboard or Cue.Slam => 0.6,
        Cue.Drumline or Cue.Brass => 0.6,
        Cue.FightSong or Cue.FightHit => 0.55,
        _ => 0.75,
    };

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
                    float g = voice.Gain * ((voice.Pos + i) % 2 == 0 ? voice.Left : voice.Right);
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
        /// <summary>The balance: the middle is whole on both sides; panned, the far side fades (to 0.2 at the edge).</summary>
        public readonly float Left, Right;
        /// <summary>A court sound (stolen first when the voices run out).</summary>
        public bool Court { get; init; }
        /// <summary>The fight song or its hit (one at a time).</summary>
        public bool Band { get; init; }
        /// <summary>Frames left in a cut's fade, or −1 while it plays out.</summary>
        public int FadeLeft = -1;
        public Voice(float[] clip, float gain, double pan)
        {
            Clip = clip; Gain = gain;
            pan = Math.Clamp(pan, -1, 1);
            Left = (float)(pan > 0 ? 1 - 0.8 * pan : 1); Right = (float)(pan < 0 ? 1 + 0.8 * pan : 1);
        }
        public void Cut() { if (FadeLeft < 0) FadeLeft = CutFade; }
    }

    /// <summary>An embedded crowd OGG (Assets/Audio/Crowd), or null if it's missing.</summary>
    public static Stream? Resource(string file) => Embedded("Crowd." + file);

    /// <summary>A file of the licensed match-day pack, embedded only in a release build (run 160: the release workflow
    /// fetches it from the private <c>hardwood-licensed-audio</c> repo; it's never in the public repo, #1070).</summary>
    public static Stream? Licensed(string file) => Embedded("Licensed." + file);

    private static Stream? Embedded(string suffix)
    {
        var asm = typeof(CrowdMixer).Assembly;
        var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            && (n.Length == suffix.Length || n[n.Length - suffix.Length - 1] == '.'));
        return name == null ? null : asm.GetManifestResourceStream(name);
    }

    /// <summary>Decode a whole embedded crowd OGG to 44.1 kHz stereo floats (the reactions are short: 2–7 s).</summary>
    public static float[] Decode(string file) => Decode(Resource(file));

    /// <summary>Decode an OGG to 44.1 kHz stereo floats, resampled if it's another rate (the pack is 48 kHz), at most
    /// <paramref name="maxSeconds"/> long with a half-second fade where it's cut. Empty if it's missing or unreadable.</summary>
    public static float[] Decode(Stream? stream, double maxSeconds = double.PositiveInfinity)
    {
        if (stream == null) return Array.Empty<float>();
        using var s = stream;
        try
        {
            using var r = new VorbisReader(s, closeOnDispose: false);
            if (r.Channels != 2 || r.SampleRate < 8000) return Array.Empty<float>();
            long want = double.IsInfinity(maxSeconds) ? long.MaxValue : (long)(maxSeconds * r.SampleRate) * 2;
            var all = new List<float>((int)Math.Min(r.TotalSamples * 2, Math.Min(want, int.MaxValue)));
            var buf = new float[8192];
            int got;
            while (all.Count < want && (got = r.ReadSamples(buf, 0, buf.Length)) > 0)
                all.AddRange(buf.AsSpan(0, (int)Math.Min(got, want - all.Count)).ToArray());
            var raw = all.ToArray();
            if (all.Count >= want) FadeOut(raw, (int)(0.5 * r.SampleRate));
            return r.SampleRate == Synth.Rate ? raw : Resample(raw, r.SampleRate, Synth.Rate);
        }
        catch (Exception) { return Array.Empty<float>(); }
    }

    /// <summary>A linear fade over the last <paramref name="frames"/> frames.</summary>
    private static void FadeOut(float[] stereo, int frames)
    {
        int total = stereo.Length / 2;
        frames = Math.Min(frames, total);
        for (int f = 0; f < frames; f++)
        {
            float g = (float)f / frames;
            int i = 2 * (total - 1 - f);
            stereo[i] *= g; stereo[i + 1] *= g;
        }
    }

    /// <summary>Linear resampling of interleaved stereo (48 → 44.1 kHz for the pack; crowd and court sounds, where it's
    /// inaudible).</summary>
    public static float[] Resample(float[] stereo, int from, int to)
    {
        int inFrames = stereo.Length / 2;
        if (inFrames == 0 || from == to) return stereo;
        int outFrames = (int)((long)inFrames * to / from);
        var o = new float[outFrames * 2];
        double step = (double)from / to;
        for (int f = 0; f < outFrames; f++)
        {
            double x = f * step;
            int a = (int)x; double u = x - a;
            int b = Math.Min(a + 1, inFrames - 1);
            o[2 * f] = (float)(stereo[2 * a] * (1 - u) + stereo[2 * b] * u);
            o[2 * f + 1] = (float)(stereo[2 * a + 1] * (1 - u) + stereo[2 * b + 1] * u);
        }
        return o;
    }

    /// <summary>A bed that loops forever, decoded as it plays (decoding the beds whole would hold 35–70 MB), resampled
    /// to 44.1 kHz as it goes if it's another rate (the pack's beds are 48 kHz).</summary>
    private sealed class LoopingOgg : IDisposable
    {
        private readonly VorbisReader _r;
        private readonly Stream _s;
        private readonly double _step;
        private float[] _src = new float[8192];
        /// <summary>Frames decoded into <see cref="_src"/>, and the read position in source frames (fractional).</summary>
        private int _have; private double _pos;
        private LoopingOgg(Stream s, VorbisReader r) { _s = s; _r = r; _step = (double)r.SampleRate / Synth.Rate; }
        public static LoopingOgg? Open(Stream? s)
        {
            if (s == null) return null;
            try
            {
                var r = new VorbisReader(s, closeOnDispose: false);
                if (r.Channels != 2 || r.SampleRate < 8000) { r.Dispose(); s.Dispose(); return null; }
                return new LoopingOgg(s, r);
            }
            catch (Exception) { s.Dispose(); return null; }
        }

        /// <summary>Read source samples straight from the decoder, looping at the end.</summary>
        private int Raw(float[] into, int at, int count)
        {
            int done = 0, spins = 0;
            while (done < count && spins < 3)
            {
                int got = _r.ReadSamples(into, at + done, count - done);
                if (got <= 0) { _r.SeekTo(0); spins++; continue; }
                done += got; spins = 0;
            }
            return done;
        }

        public void Read(float[] into, int count)
        {
            if (Math.Abs(_step - 1) < 1e-9)
            {
                int done = Raw(into, 0, count);
                if (done < count) Array.Clear(into, done, count - done);
                return;
            }
            int frames = count / 2;
            for (int f = 0; f < frames; f++)
            {
                int a = (int)_pos;
                // Keep two source frames ahead of the read position; slide the window when it runs out.
                if (a + 1 >= _have)
                {
                    int keep = Math.Max(0, _have - a);
                    if (keep > 0) Array.Copy(_src, 2 * a, _src, 0, 2 * keep);
                    _pos -= a; a = 0;
                    int got = Raw(_src, 2 * keep, _src.Length - 2 * keep) / 2;
                    _have = keep + got;
                    if (_have < 2) { Array.Clear(into, 2 * f, count - 2 * f); return; }
                }
                double u = _pos - a;
                into[2 * f] = (float)(_src[2 * a] * (1 - u) + _src[2 * a + 2] * u);
                into[2 * f + 1] = (float)(_src[2 * a + 1] * (1 - u) + _src[2 * a + 3] * u);
                _pos += _step;
            }
        }
        public void Dispose() { _r.Dispose(); _s.Dispose(); }
    }
}
