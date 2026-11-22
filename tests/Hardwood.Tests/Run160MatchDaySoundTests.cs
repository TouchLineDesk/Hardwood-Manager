using Hardwood.Core;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.Presentation;
using Hardwood.Desktop.Audio;
using Cue = Hardwood.Desktop.Audio.Cue;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>Run 160, the sound redo's intake step (Danny's play-test note `danny-sound-nov22`,
/// claude/sound-intake-2026-11-22.md): the licensed Splice pack (an arena crowd, the court and the band) is wired into the
/// mixer when the release build embeds it, and the court's sounds and the band play from the choreography, panned to the
/// play. The pack itself is never in this repo (#1070): these tests use stand-in tones at the pack's 48 kHz
/// (AudioFixtures, made with ffmpeg's noise and sine sources), and a build without the pack keeps M6's crowd.</summary>
[Collection(SharedUiState.Name)]
public class Run160MatchDaySoundTests
{
    private readonly ITestOutputHelper _out;
    public Run160MatchDaySoundTests(ITestOutputHelper output) => _out = output;

    private static Stream? StandIn(string file)
    {
        var p = Path.Combine(AppContext.BaseDirectory, "AudioFixtures", file);
        return File.Exists(p) ? File.OpenRead(p) : null;
    }

    private static (List<Player> Home, List<Player> Away) Fives()
    {
        var l = new WorldGenerator(new Rng(17)).Generate("L", 2026, new DateOnly(2026, 10, 1));
        List<Player> Five(Team t) => t.Roster.OrderByDescending(p => p.Overall).Take(5).OrderBy(p => (int)p.PrimaryPosition).ToList();
        return (Five(l.Teams[0]), Five(l.Teams[1]));
    }

    private static Choreography Build(List<Player> hf, List<Player> af, List<PlayEvent> ev, int index = 7, int homeStart = 0, int awayStart = 0)
    {
        var start = hf.Concat(af).Select((p, i) => (p, i)).ToDictionary(x => x.p, x => new Spot(47 + (x.i < 5 ? -4 : 4), 10 + (x.i % 5) * 7));
        var input = new PossessionInput(index, 2026, ev, 516, 500, hf, af, start, homeStart, awayStart, true, "Home", "Away",
            DefensiveScheme.Man, DefensiveScheme.Man, PossessionStart.DeadBall, 4, false);
        return PossessionChoreographer.Build(input);
    }

    private static double Rms(CrowdMixer mix, float[] buf, int blocks, int channel = -1)
    {
        double s = 0; int n = 0;
        for (int b = 0; b < blocks; b++)
        {
            mix.Read(buf, 0, buf.Length);
            for (int i = 0; i < buf.Length; i++)
            {
                if (channel >= 0 && i % 2 != channel) continue;
                Assert.False(float.IsNaN(buf[i]));
                s += buf[i] * buf[i]; n++;
            }
        }
        return Math.Sqrt(s / Math.Max(1, n));
    }

    [Fact]
    public void The_pack_loads_resampled_and_trimmed_and_the_beds_loop()
    {
        // Decoded at 48 kHz, played at 44.1 kHz: a 0.6 s swish stays 0.6 s.
        var swish = CrowdMixer.Decode(StandIn("court-swish-1.ogg"));
        Assert.InRange(swish.Length / 2.0 / Synth.Rate, 0.55, 0.65);
        // The 9 s eruption is cut to 7 s with a fade, so it never runs on under the next play.
        var erupt = CrowdMixer.Decode(StandIn("crowd-erupt-1.ogg"), 7);
        Assert.InRange(erupt.Length / 2.0 / Synth.Rate, 6.9, 7.05);
        Assert.True(Math.Abs(erupt[^1]) < 0.01 && Math.Abs(erupt[^2]) < 0.01, "the cut fades out");
        // Resampling keeps a tone's pitch: 1 kHz at 48 kHz is still about 1 kHz at 44.1 kHz (zero crossings).
        int crossings = 0; for (int i = 2; i < swish.Length; i += 2) if (swish[i - 2] < 0 && swish[i] >= 0) crossings++;
        double hz = crossings / (swish.Length / 2.0 / Synth.Rate);
        _out.WriteLine($"swish {swish.Length / 2.0 / Synth.Rate:0.000} s at {hz:0} Hz; erupt {erupt.Length / 2.0 / Synth.Rate:0.00} s");
        Assert.InRange(hz, 950, 1050);

        using var mix = new CrowdMixer(StandIn);
        Assert.True(mix.HasPack);
        mix.Volume = 1;
        var buf = new float[2048];
        mix.SetBed(CrowdMix.Target(1.0, 0, true, false, false));
        Rms(mix, buf, 70);
        // The stand-in beds are 2 s: 5 s of play loops them more than twice and they keep playing.
        double a = Rms(mix, buf, 40), b = Rms(mix, buf, 60);
        _out.WriteLine($"pack bed rms {a:0.0000} then {b:0.0000}");
        Assert.True(a > 0.01 && b > 0.01);
    }

    [Fact]
    public void Court_sounds_are_panned_to_the_play_and_missing_ones_are_never_faked()
    {
        using var mix = new CrowdMixer(StandIn);
        mix.Volume = 1;
        var buf = new float[2048];
        Rms(mix, buf, 4);   // the master's glide up from silence
        mix.Play(Cue.Swish, 1, 1);   // at the right-hand rim
        Assert.Equal(1, mix.Voices);
        double left = Rms(mix, new float[1024], 1, 0);
        mix.StopReactions(); Rms(mix, buf, 4);
        mix.Play(Cue.Swish, 1, 1);
        double right = Rms(mix, new float[1024], 1, 1);
        _out.WriteLine($"swish panned right: left {left:0.0000}, right {right:0.0000}");
        Assert.True(right > 3 * left, "panned right, the right channel carries it");
        // The stand-in pack has no dribble, squeak or drumline: they play nothing (no voice), rather than a code-made noise.
        mix.StopReactions(); Rms(mix, buf, 4);
        mix.Play(Cue.Bounce, 1, 0); mix.Play(Cue.Squeak, 1, 0); mix.Play(Cue.Drumline, 1, 0);
        Assert.Equal(0, mix.Voices);

        // A build without the pack (tests, CI, forks) is M6's crowd: no court or band sounds and no "ohh",
        // and the eruption is the big cheer.
        using var m6 = new CrowdMixer(_ => null);
        Assert.False(m6.HasPack);
        m6.Play(Cue.Swish, 1, 0); m6.Play(Cue.Oh, 1, 0);
        Assert.Equal(0, m6.Voices);
        m6.Play(Cue.Erupt, 1, 0);
        Assert.Equal(1, m6.Voices);
    }

    [Fact]
    public void The_court_and_the_band_follow_the_choreography()
    {
        var (hf, af) = Fives();
        // A made home jumper: dribbles while it's carried, a swish at the rim (panned to that end), no band.
        var jumper = Build(hf, af, new() { new(1, 505, PlayKind.Shot, true, hf[1], null, ShotZone.LongMid, true, 12, 10) });
        var court = Reactions.Court(jumper);
        _out.WriteLine(string.Join(", ", court.Select(q => $"{q.T:0.00} {q.Kind} {q.Pan:+0.00;-0.00}")));
        Assert.Contains(court, q => q.Kind == Cue.Bounce);
        Assert.InRange(court.Count(q => q.Kind == Cue.Bounce), 1, 12);
        var swish = Assert.Single(court, q => q.Kind == Cue.Swish);
        var shot = jumper.Moments.Single(m => m.E.Kind == PlayKind.Shot);
        Assert.Equal(shot.T, swish.T, 6);
        var rim = jumper.Ball.Last(b => b.Mode == BallMode.Shot).Dest;
        Assert.Equal(Math.Sign(rim.X - 47), Math.Sign(swish.Pan));
        Assert.DoesNotContain(court, q => q.Kind is Cue.Drumline or Cue.Brass);
        // Every dribble is while the ball is held, at the carrier's side of the floor.
        foreach (var q in court.Where(q => q.Kind == Cue.Bounce))
            Assert.Contains(jumper.Ball, b => b.Mode == BallMode.Held && b.T0 <= q.T && q.T <= b.T1);

        // A miss: the rim or the glass, never the net.
        var miss = Reactions.Court(Build(hf, af, new() { new(1, 505, PlayKind.Shot, true, hf[1], null, ShotZone.LongMid, false, 10, 10) }));
        Assert.Contains(miss, q => q.Kind is Cue.Rim or Cue.Backboard);
        Assert.DoesNotContain(miss, q => q.Kind is Cue.Swish or Cue.Finish or Cue.Slam);

        // A home dunk: the backboard slam and the brass; the crowd erupts and the students join in.
        var big = hf[4]; int old = big.Attributes[Attr.Dunk];
        try
        {
            big.Attributes[Attr.Dunk] = 18;
            var dunk = Build(hf, af, new() { new(1, 505, PlayKind.Shot, true, big, null, ShotZone.Rim, true, 12, 10) });
            Assert.Contains(Reactions.Court(dunk), q => q.Kind == Cue.Slam);
            Assert.Contains(Reactions.Court(dunk), q => q.Kind == Cue.Brass);
            Assert.Equal(new[] { Cue.Erupt, Cue.Student }, Reactions.Plan(dunk).Select(q => q.Kind));
            // Down 20, the house still erupts for a dunk, but the students and the band stay quiet.
            var beaten = Build(hf, af, new() { new(2, 505, PlayKind.Shot, true, big, null, ShotZone.Rim, true, 42, 60) }, 7, 40, 60);
            Assert.Equal(new[] { Cue.Erupt }, Reactions.Plan(beaten).Select(q => q.Kind));
            Assert.DoesNotContain(Reactions.Court(beaten), q => q.Kind == Cue.Brass);
        }
        finally { big.Attributes[Attr.Dunk] = old; }

        // A home three that rims out in a close game: "ohh". Down 10, or an away miss: nothing from the home crowd.
        Assert.Equal(new[] { Cue.Oh }, Reactions.Plan(Build(hf, af, new() { new(1, 505, PlayKind.Shot, true, hf[2], null, ShotZone.AboveBreakThree, false, 10, 10) })).Select(q => q.Kind));
        Assert.Empty(Reactions.Plan(Build(hf, af, new() { new(1, 505, PlayKind.Shot, false, af[2], null, ShotZone.AboveBreakThree, false, 10, 10) })));
        Assert.Empty(Reactions.Plan(Build(hf, af, new() { new(1, 505, PlayKind.Shot, true, hf[2], null, ShotZone.AboveBreakThree, false, 10, 20) }, 7, 10, 20)));

        // The band: the fight song before the tip (run 161; the mixer plays the drumline when there's no band) and the
        // drumline at a period's end, after the buzzer.
        Assert.Contains(Reactions.Court(Build(hf, af, new() { new(1, 700, PlayKind.Shot, true, hf[1], null, ShotZone.LongMid, false, 0, 0) }, index: 0)), q => q.Kind == Cue.FightSong && q.T < 0.5);
        var end = Build(hf, af, new() { new(1, 0, PlayKind.PeriodEnd, true, null, null, ShotZone.Rim, false, 20, 18) });
        var buzzer = Reactions.Plan(end).Single(q => q.Kind == Cue.Buzzer);
        Assert.Contains(Reactions.Court(end), q => q.Kind == Cue.Drumline && q.T > buzzer.T);

        // The speed rules: at 4× and 8× the court's small sounds go; the eruption and the break's drumline stay.
        Assert.False(Reactions.Plays(new SoundCue(1, Cue.Bounce), 4, false));
        Assert.False(Reactions.Plays(new SoundCue(1, Cue.Swish), 8, false));
        Assert.True(Reactions.Plays(new SoundCue(1, Cue.Erupt), 8, false));
        Assert.True(Reactions.Plays(new SoundCue(1, Cue.Drumline), 4, false));
        Assert.True(Reactions.Plays(new SoundCue(1, Cue.Bounce), 2, false));
    }
}
