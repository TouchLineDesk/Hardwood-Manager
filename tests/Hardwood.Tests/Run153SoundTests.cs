using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Hardwood.Core.Engine;
using Hardwood.Core.Model;
using Hardwood.Core.Presentation;
using Hardwood.Desktop;
using Hardwood.Desktop.Audio;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using NAudio.Wave;
using Cue = Hardwood.Desktop.Audio.Cue;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>Run 153, M6 of the 2D match engine (claude/match-engine-2d-design.md §F): the crowd's sound. Two beds mixed by
/// the house and the momentum, reactions from the engine's events at their playback time, a code-made buzzer and whistle,
/// the speed rules, mute and volume, and silence (not a crash) with no audio device.</summary>
[Collection(SharedUiState.Name)]
public class Run153SoundTests
{
    private readonly ITestOutputHelper _out;
    public Run153SoundTests(ITestOutputHelper output) => _out = output;

    /// <summary>Records what the live game asks of the sound.</summary>
    private sealed class RecordingAudio : IGameAudio
    {
        public readonly List<(Cue Cue, double Gain)> Played = new();
        public readonly List<BedLevels> Beds = new();
        public int Stops;
        public void SetBed(BedLevels target) => Beds.Add(target);
        public void Play(Cue cue, double gain) => Played.Add((cue, gain));
        public void StopReactions() => Stops++;
        public double Volume { get; set; }
        public bool Muted { get; set; }
        public bool Suspended { get; set; }
        public bool HasDevice => true;
        public bool Disposed;
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void The_speed_rules_keep_only_the_biggest_moments_at_4x_and_8x()
    {
        var cheer = new SoundCue(1, Cue.Cheer); var big = new SoundCue(1, Cue.BigCheer); var buzz = new SoundCue(1, Cue.Buzzer);
        var whistle = new SoundCue(1, Cue.Whistle); var groan = new SoundCue(1, Cue.Groan);
        foreach (var rate in new double[] { 1, 2 }) Assert.All(new[] { cheer, big, buzz, whistle, groan }, c => Assert.True(Reactions.Plays(c, rate, false)));
        foreach (var rate in new double[] { 4, 8, 16 })
        {
            Assert.True(Reactions.Plays(big, rate, false));
            Assert.True(Reactions.Plays(buzz, rate, false));
            Assert.False(Reactions.Plays(cheer, rate, false));
            Assert.False(Reactions.Plays(whistle, rate, false));
            Assert.False(Reactions.Plays(groan, rate, false));
        }
        // Key moments' watched possessions play everything.
        Assert.True(Reactions.Plays(whistle, 2, true));
    }

    [Fact]
    public void The_bed_follows_the_house_and_swells_late_and_eases_over_one_to_two_seconds()
    {
        var thin = CrowdMix.Target(0.35, 0, false, false, false);
        var sellout = CrowdMix.Target(1.0, 0, false, false, false);
        Assert.True(thin.Low > thin.High, "a thin house leans to the low bed");
        Assert.True(sellout.High > sellout.Low, "a sell-out leans to the high bed");
        Assert.True(sellout.Total > thin.Total);
        // A home run and a close finish lift it; a pause and 8× quieten it.
        var early = CrowdMix.Target(0.8, 0, false, false, false);
        Assert.True(CrowdMix.Target(0.8, 10, false, false, false).Total > early.Total);
        Assert.True(CrowdMix.Target(0.8, 0, true, false, false).Total > early.Total);
        Assert.True(CrowdMix.Target(0.8, 0, false, true, false).Total < early.Total * 0.5);
        Assert.True(CrowdMix.Target(0.8, 0, false, false, true).Total < early.Total * 0.6);
        // The easing: from a murmur to the late swell, frame by frame at 60 fps: 90% of the way in 1–2 s, never a jump.
        var from = CrowdMix.Target(0.8, 0, false, true, false);
        var to = CrowdMix.Target(1.0, 12, true, false, false);
        var now = from; double t = 0, ninety = -1, maxStep = 0;
        while (t < 4)
        {
            var next = CrowdMix.Ease(now, to, 1.0 / 60);
            maxStep = Math.Max(maxStep, Math.Abs(next.Total - now.Total));
            now = next; t += 1.0 / 60;
            if (ninety < 0 && Math.Abs(now.Total - to.Total) <= 0.1 * Math.Abs(to.Total - from.Total)) ninety = t;
        }
        _out.WriteLine($"bed {from.Total:0.00} -> {to.Total:0.00}: 90% at {ninety:0.00} s, biggest frame step {maxStep:0.000}");
        Assert.InRange(ninety, 1.0, 2.0);
        Assert.True(maxStep < 0.05 * Math.Abs(to.Total - from.Total) + 0.001);
        // The groan: down, then back by its end.
        Assert.Equal(1, CrowdMix.Groan(0, 1), 3);
        Assert.True(CrowdMix.Groan(0.2, 1) < 0.6);
        Assert.Equal(1, CrowdMix.Groan(CrowdMix.GroanTime, 1), 3);
    }

    [Fact]
    public void The_code_made_buzzer_and_whistle_are_what_they_say()
    {
        var buzz = Synth.Buzzer();
        Assert.Equal(Synth.Rate * 2, buzz.Length);   // 1 s of stereo
        Assert.InRange(buzz.Max(Math.Abs), 0.2f, 0.7f);
        Assert.True(Math.Abs(buzz[0]) < 0.01 && Math.Abs(buzz[^1]) < 0.02, "no click at either end");
        // A square wave: its odd harmonics are there (660 Hz at about a third of 220 Hz), an even one isn't.
        double Bin(double hz) { double re = 0, im = 0; for (int i = 0; i < Synth.Rate / 2; i++) { double a = 2 * Math.PI * hz * i / Synth.Rate; re += buzz[2 * (i + 1000)] * Math.Cos(a); im += buzz[2 * (i + 1000)] * Math.Sin(a); } return Math.Sqrt(re * re + im * im); }
        double f1 = Bin(220), f3 = Bin(660), f2 = Bin(440);
        _out.WriteLine($"buzzer bins: 220 Hz {f1:0}, 440 Hz {f2:0}, 660 Hz {f3:0}");
        Assert.InRange(f3 / f1, 0.2, 0.5);
        Assert.True(f2 < 0.05 * f1);
        var wh = Synth.Whistle();
        Assert.InRange(wh.Length / 2.0 / Synth.Rate, 0.4, 0.8);
        Assert.InRange(wh.Max(Math.Abs), 0.1f, 0.5f);
        // The trill: the loudness over 5 ms windows swings by more than half.
        var env = Enumerable.Range(0, wh.Length / 2 / 220).Select(w => wh.Skip(w * 440).Take(440).Max(Math.Abs)).Skip(4).SkipLast(8).ToList();
        Assert.True(env.Min() < 0.5 * env.Max(), $"trill {env.Min():0.000}..{env.Max():0.000}");
    }

    [Fact]
    public void The_mixer_decodes_the_crowd_and_renders_offline()
    {
        foreach (var f in new[] { "crowd-cheer-1.ogg", "crowd-cheer-2.ogg", "crowd-cheer-3.ogg", "crowd-applause.ogg", "crowd-fan-whistle.ogg" })
            Assert.True(CrowdMixer.Decode(f).Length > Synth.Rate, f);
        using var mix = new CrowdMixer();
        mix.Volume = 1;
        var buf = new float[2048];
        double Rms(int blocks) { double s = 0; int n = 0; for (int b = 0; b < blocks; b++) { mix.Read(buf, 0, buf.Length); foreach (var x in buf) { s += x * x; n++; } } return Math.Sqrt(s / n); }
        Assert.Equal(0, Rms(4), 6);   // no bed asked for: silence
        mix.SetBed(CrowdMix.Target(1.0, 0, true, false, false));
        Rms(70);   // ~1.6 s to settle
        double settled = Rms(40);
        Assert.True(settled > 0.01, $"the bed plays ({settled:0.0000})");
        // A groan dips it, and it's back after.
        mix.Play(Cue.Groan, 1);
        Rms(6);
        double dipped = Rms(4);
        Rms(30);
        double back = Rms(20);
        _out.WriteLine($"bed rms {settled:0.0000}, in a groan {dipped:0.0000}, after {back:0.0000}");
        Assert.True(dipped < settled * 0.8);
        Assert.True(back > settled * 0.8);
        // A cheer is added on top, and goes when it ends; mute is silence.
        mix.Play(Cue.BigCheer, 1);
        Assert.Equal(1, mix.Voices);
        Rms(240);   // 5.6 s: longer than cheer-2's 5.4 s
        Assert.Equal(0, mix.Voices);
        // Stopping the reactions fades them out over 10 ms rather than cutting mid-wave.
        mix.Play(Cue.Applause, 1);
        Rms(4);
        mix.StopReactions();
        Assert.Equal(1, mix.Voices);
        Rms(1);
        Assert.Equal(0, mix.Voices);
        // Mute and minimise glide out over about 20 ms (no click), then it's silence.
        mix.Muted = true;
        Rms(1);
        Assert.Equal(0, Rms(2), 6);
        mix.Muted = false; Rms(2); mix.Suspended = true;
        Rms(1);
        Assert.Equal(0, Rms(2), 6);
        // The knee is continuous: no step at its threshold, and it never passes 1.
        Assert.True(Math.Abs(CrowdMixer.Knee(0.8001f) - CrowdMixer.Knee(0.7999f)) < 0.001f);
        Assert.True(CrowdMixer.Knee(5f) <= 1f && CrowdMixer.Knee(-5f) >= -1f);
        float prev = 0; for (float x = 0; x < 3; x += 0.001f) { float y = CrowdMixer.Knee(x); Assert.True(y >= prev && y - prev < 0.0015f); prev = y; }
        mix.Suspended = false;
        // The cost: 60 s of game audio with a reaction every second.
        var sw = Stopwatch.StartNew();
        for (int s = 0; s < 60; s++) { mix.Play(s % 3 == 0 ? Cue.Cheer : s % 3 == 1 ? Cue.Whistle : Cue.Applause, 1); Rms(Synth.Rate * 2 / buf.Length); }
        double perSecond = sw.Elapsed.TotalMilliseconds / 60;
        _out.WriteLine($"mixing: {perSecond:0.00} ms of CPU per second of audio");
        Assert.True(perSecond < 50, "the mixer costs well under 5% of a core");
    }

    private sealed class FakePlayer : IWavePlayer
    {
        public bool FailInit;
        public IWaveProvider? Source;
        public void Init(IWaveProvider waveProvider) { if (FailInit) throw new InvalidOperationException("No audio device"); Source = waveProvider; }
        public void Play() => PlaybackState = PlaybackState.Playing;
        public void Stop() => PlaybackState = PlaybackState.Stopped;
        public void Pause() => PlaybackState = PlaybackState.Paused;
        public PlaybackState PlaybackState { get; private set; }
        public float Volume { get; set; } = 1;
        public WaveFormat OutputWaveFormat => Source?.WaveFormat ?? WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
        public event EventHandler<StoppedEventArgs>? PlaybackStopped { add { } remove { } }
        public void Dispose() { }
    }

    [Fact]
    public void No_audio_device_means_silence_not_a_crash()
    {
        // Not Windows (CI) or no device: the factory hands back silence.
        if (!OperatingSystem.IsWindows()) Assert.IsType<SilentAudio>(GameAudio.Open());
        Assert.Null(DeviceAudio.TryOpen(() => throw new DllNotFoundException("winmm")));
        Assert.Null(DeviceAudio.TryOpen(() => new FakePlayer { FailInit = true }));
        // A device that opens gets the mixer.
        var ok = new FakePlayer();
        using (var dev = DeviceAudio.TryOpen(() => ok))
        {
            Assert.NotNull(dev);
            Assert.Equal(PlaybackState.Playing, ok.PlaybackState);
            Assert.Equal(44100, ok.Source!.WaveFormat.SampleRate);
            dev!.Play(Cue.Buzzer, 1);
            dev.Play(Cue.Whistle, 1);
        }
        Assert.Equal(PlaybackState.Stopped, ok.PlaybackState);
        var silent = new SilentAudio();
        silent.Play(Cue.Cheer, 1); silent.SetBed(new BedLevels(1, 1)); silent.Dispose();
        Assert.False(silent.HasDevice);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void A_whole_game_reacts_only_to_its_events_and_changes_no_result()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run153-{Guid.NewGuid():N}.hwsave");
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath; var oldFactory = GameAudio.Factory;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { AutoPortraits = false, StudioShow = false });
        string? dir = Environment.GetEnvironmentVariable("HW_SCREENSHOT_DIR") is { Length: > 0 } d ? d : null;
        var rec = new RecordingAudio();
        GameAudio.Factory = () => rec;
        MainWindow? window = null;
        try
        {
            var vm = new MainViewModel();
            window = new MainWindow { DataContext = vm, Width = 1480, Height = 900 };
            window.Show();
            var start = (StartViewModel)vm.CurrentPage!;
            start.SavePath = path; start.SeedText = "2026";
            start.SelectedTeam = start.Teams.First(t => t.Team.Abbreviation == "BOS");
            start.NewGame();
            var s = vm.Session!;
            for (int i = 0; i < 30; i++) s.AdvanceDay();
            vm.AfterTick();
            var w = window;
            void Settle() { for (int i = 0; i < 5; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); } }
            void Shot(string name) { if (dir == null) return; Directory.CreateDirectory(dir); using var f = w.CaptureRenderedFrame(); f?.Save(Path.Combine(dir, $"run153-{name}.png")); }

            var home = s.HumanTeam; var away = s.League.Teams.First(t => t != home);
            home.Tactic = new Tactic(); away.Tactic = new Tactic();
            var plain = new GameSimulator().StartLive(home, away, s.League.Rules, 153, s.Today); plain.Finish();
            home.Tactic = new Tactic(); away.Tactic = new Tactic();
            var game = new GameSimulator().StartLive(home, away, s.League.Rules, 153, s.Today);
            var page = new LiveGameViewModel(vm, game, new());
            vm.InLiveGame = true;
            vm.CurrentPage = page; Settle();
            var view = w.GetVisualDescendants().OfType<LiveGameView>().First(v => v.IsEffectivelyVisible);

            // The speaker button and slider sit in the controls and fit.
            var button = view.FindControl<Button>("SoundButton")!;
            Assert.True(button.IsEffectivelyVisible);
            Assert.True(view.FindControl<Slider>("SoundSlider")!.IsEffectivelyVisible);
            Assert.Empty(LayoutCheck.Scan(w).Select(x => x.ToString()));
            Assert.Equal("sound:", LiveGameView.KeyAction(Avalonia.Input.Key.M));
            Shot("sound-controls");

            // The whole game at 2×, frame by frame; every possession's planned reactions are checked against its events.
            page.PlayPause();
            page.SetSpeedCommand.Execute("2");
            page.PauseAtBreaks = false;
            Choreography? last = null;
            int planned = 0, frames = 0;
            var lateBeds = new List<double>(); var earlyBeds = new List<double>();
            void Check(Choreography c)
            {
                var cues = Reactions.Plan(c);
                planned += cues.Count;
                foreach (var q in cues)
                {
                    // Run 157: a crowd's answer to a whistle comes 0.15–0.2 s after it.
                    var at = c.Moments.Where(m => q.T - m.T > -1e-9 && q.T - m.T < 0.25).Select(m => m.E).ToList();
                    switch (q.Kind)
                    {
                        case Cue.Cheer or Cue.BigCheer:
                            Assert.Contains(at, e => (e.Kind is PlayKind.Shot or PlayKind.FreeThrow && e.Made && e.Home) || (e.Blocked && !e.Home) || (e.Kind == PlayKind.Steal && e.Home) || (e.Kind == PlayKind.Foul && !e.Home));
                            break;
                        case Cue.Groan:
                            Assert.Contains(at, e => (e.Kind is PlayKind.Shot or PlayKind.FreeThrow && e.Made && !e.Home) || (e.Blocked && e.Home) || (e.Kind == PlayKind.Steal && !e.Home) || (e.Kind == PlayKind.Turnover && e.Home));
                            break;
                        case Cue.Boo: Assert.Contains(at, e => e.Kind == PlayKind.Foul && e.Home); break;
                        case Cue.Whistle: Assert.Contains(at, e => e.Kind is PlayKind.Foul or PlayKind.Turnover); break;
                        case Cue.Buzzer: Assert.Contains(at, e => e.Kind == PlayKind.PeriodEnd); break;
                        case Cue.Applause: Assert.True(c.Actions.Any(a => a.Tag == "sub") || c.Moments.Any(m => m.E.Kind == PlayKind.PeriodEnd)); break;
                        case Cue.FanWhistle: Assert.Contains(c.Moments, m => m.E.Kind == PlayKind.FreeThrow && !m.E.Home); break;
                    }
                }
                // Every event the crowd should hear has its moment: each shot, foul, turnover, steal and period end.
                foreach (var e in c.Input.Events.Where(e => e.Kind is PlayKind.Shot or PlayKind.Foul or PlayKind.Turnover or PlayKind.Steal or PlayKind.PeriodEnd or PlayKind.FreeThrow))
                    Assert.Contains(c.Moments, m => ReferenceEquals(m.E, e));
            }
            while (!page.Complete && frames < 2_000_000)
            {
                page.Play(1.0 / 60); frames++;
                if (page.Script != last) { last = page.Script; if (last != null) Check(last); }
                if (rec.Beds.Count > 0 && frames % 30 == 0)
                {
                    if (game.Period == 1) earlyBeds.Add(rec.Beds[^1].Total);
                    else if (page.MatchFrame is { } mf && game.Period >= game.Result.Periods && mf.Clock < 120 && Math.Abs(page.BarHomeScore - page.BarAwayScore) <= 6) { lateBeds.Add(rec.Beds[^1].Total); }
                }
            }
            Assert.True(page.Complete);
            var plays = game.Result.Plays;
            var played = rec.Played.GroupBy(p => p.Cue).ToDictionary(g => g.Key, g => g.Count());
            _out.WriteLine($"{frames} frames; planned {planned}, played {rec.Played.Count}: " + string.Join(", ", played.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value}")));
            _out.WriteLine($"crowd {page.FloorDress.Crowd:0.00}; early range {earlyBeds.DefaultIfEmpty().Min():0.000}-{earlyBeds.DefaultIfEmpty().Max():0.000}; late range {lateBeds.DefaultIfEmpty().Min():0.000}-{lateBeds.DefaultIfEmpty().Max():0.000}");
            _out.WriteLine($"bed total: first period avg {(earlyBeds.Count > 0 ? earlyBeds.Average() : 0):0.000}; late and close avg {(lateBeds.Count > 0 ? lateBeds.Average() : 0):0.000} over {lateBeds.Count} samples");
            // At 2× every planned cue plays, once.
            Assert.Equal(planned, rec.Played.Count);
            // Every period end has a buzzer; every foul and dead-ball turnover a whistle; a cheer for each home basket at least.
            Assert.Equal(plays.Count(p => p.Kind == PlayKind.PeriodEnd), played.GetValueOrDefault(Cue.Buzzer));
            Assert.Equal(plays.Count(p => p.Kind is PlayKind.Foul or PlayKind.Turnover), played.GetValueOrDefault(Cue.Whistle));
            // Run 157: down 15 or more, an ordinary home basket gets no cheer (only the big plays do).
            int homeBaskets = 0, hs = 0, aws = 0;
            foreach (var p in plays) { if (p.Kind == PlayKind.Shot && p.Made && p.Home && aws - hs < Reactions.BlowoutDeficit) homeBaskets++; hs = p.HomeScore; aws = p.AwayScore; }
            Assert.True(played.GetValueOrDefault(Cue.Cheer) + played.GetValueOrDefault(Cue.BigCheer) >= homeBaskets);
            Assert.True(played.GetValueOrDefault(Cue.Groan) >= plays.Count(p => p.Kind == PlayKind.Shot && p.Made && !p.Home));
            Assert.Equal(plays.Count(p => p.Kind == PlayKind.Foul && p.Home), played.GetValueOrDefault(Cue.Boo));
            if (lateBeds.Count > 0) Assert.True(lateBeds.Average() > earlyBeds.Average(), "the bed swells late in a close game");

            // Mute and volume go to the sound and into the settings.
            page.ToggleSoundCommand.Execute(null);
            Assert.True(rec.Muted);
            Assert.False(AppSettings.Current.SoundOn);
            Assert.Equal("VolumeMute", page.SoundIcon);
            page.ToggleSoundCommand.Execute(null);
            Assert.False(rec.Muted);
            page.SoundVolume = 40;
            Assert.Equal(0.4, rec.Volume, 3);
            Assert.Equal(0.4, AppSettings.Load().SoundVolume, 3);
            page.SetMinimised(true);
            Assert.True(rec.Suspended);
            page.SetMinimised(false);

            // Nothing the sound did changed the game.
            Assert.Equal(plain.Result.Home.Points, game.Result.Home.Points);
            Assert.Equal(plain.Result.Away.Points, game.Result.Away.Points);
            Assert.Equal(plain.Result.Home.Lines.Select(l => (l.Points, l.Rebounds, l.Assists)), game.Result.Home.Lines.Select(l => (l.Points, l.Rebounds, l.Assists)));
            Settle();
            Shot("final");
            page.ContinueCommand.Execute(null);
            Assert.True(rec.Disposed, "the crowd goes home when the game is recorded");
            // Review (run 153): once released, nothing reopens the device (a frame that comes after gets silence).
            Assert.IsType<SilentAudio>(page.Audio);
        }
        finally
        {
            window?.Close();
            GameAudio.Factory = oldFactory;
            AppSettings.Use(oldSettings); AppSettings.FilePath = oldPath;
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void Settings_has_the_sound_switch_and_volume()
    {
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings());
        try
        {
            var vm = new MainViewModel();
            var settings = new SettingsViewModel(vm);
            Assert.True(settings.SoundOn);
            Assert.Equal(70, settings.SoundVolume);
            settings.SoundOn = false; settings.SoundVolume = 25;
            var back = AppSettings.Load();
            Assert.False(back.SoundOn);
            Assert.Equal(0.25, back.SoundVolume, 3);
            Assert.Equal("25%", settings.SoundVolumeText);
        }
        finally { AppSettings.Use(oldSettings); AppSettings.FilePath = oldPath; }
    }
}
