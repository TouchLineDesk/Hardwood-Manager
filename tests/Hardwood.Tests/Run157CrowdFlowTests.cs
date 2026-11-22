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

/// <summary>Run 157, the sound redo's first step (Danny's play-test note `danny-sound-nov22`,
/// claude/playtest-2026-11-22-sound.md §Scope 2): the crowd follows the game. Boos on a home foul, a cheer on an away foul,
/// a big cheer on a home steal or a dunk, a groan when the home side loses the ball, a crowd that goes quiet in a blowout
/// and comes back to life in a comeback. Presentation only: the rules read the engine's events and the score on screen.</summary>
[Collection(SharedUiState.Name)]
public class Run157CrowdFlowTests
{
    private readonly ITestOutputHelper _out;
    public Run157CrowdFlowTests(ITestOutputHelper output) => _out = output;

    private static (List<Player> Home, List<Player> Away) Fives()
    {
        var l = new WorldGenerator(new Rng(17)).Generate("L", 2026, new DateOnly(2026, 10, 1));
        List<Player> Five(Team t) => t.Roster.OrderByDescending(p => p.Overall).Take(5).OrderBy(p => (int)p.PrimaryPosition).ToList();
        return (Five(l.Teams[0]), Five(l.Teams[1]));
    }

    private static Choreography Build(List<Player> hf, List<Player> af, List<PlayEvent> ev, int homeStart = 0, int awayStart = 0)
    {
        var start = hf.Concat(af).Select((p, i) => (p, i)).ToDictionary(x => x.p, x => new Spot(47 + (x.i < 5 ? -4 : 4), 10 + (x.i % 5) * 7));
        var input = new PossessionInput(7, 2026, ev, 516, 500, hf, af, start, homeStart, awayStart, true, "Home", "Away",
            DefensiveScheme.Man, DefensiveScheme.Man, PossessionStart.DeadBall, 4, false);
        return PossessionChoreographer.Build(input);
    }

    private static List<Cue> Kinds(Choreography c) => Reactions.Plan(c).Select(q => q.Kind).ToList();

    [Fact]
    public void Fouls_steals_turnovers_and_dunks_get_the_crowd_Danny_asked_for()
    {
        var (hf, af) = Fives();
        // A home foul: the whistle, then boos. An away foul: the whistle, then a cheer.
        var homeFoul = Kinds(Build(hf, af, new() { new(1, 505, PlayKind.Foul, true, hf[1], af[2], ShotZone.Rim, false, 10, 10) }));
        Assert.Equal(new[] { Cue.Whistle, Cue.Boo }, homeFoul);
        var awayFoul = Kinds(Build(hf, af, new() { new(1, 505, PlayKind.Foul, false, af[1], hf[2], ShotZone.Rim, false, 10, 10) }));
        Assert.Equal(new[] { Cue.Whistle, Cue.Cheer }, awayFoul);
        // A home steal brings them up; an away steal (the home side losing it) is a groan.
        Assert.Contains(Cue.BigCheer, Kinds(Build(hf, af, new() { new(1, 505, PlayKind.Steal, true, hf[0], af[0], ShotZone.Rim, false, 10, 10) })));
        Assert.Equal(new[] { Cue.Groan }, Kinds(Build(hf, af, new() { new(1, 505, PlayKind.Steal, false, af[0], hf[0], ShotZone.Rim, false, 10, 10) })));
        // A home dead-ball turnover: the whistle and a groan; an away one, just the whistle.
        Assert.Equal(new[] { Cue.Whistle, Cue.Groan }, Kinds(Build(hf, af, new() { new(1, 505, PlayKind.Turnover, true, hf[0], null, ShotZone.Rim, false, 10, 10) })));
        Assert.Equal(new[] { Cue.Whistle }, Kinds(Build(hf, af, new() { new(1, 505, PlayKind.Turnover, false, af[0], null, ShotZone.Rim, false, 10, 10) })));

        // A dunk: a finisher who dunks gets the big cheer, and the commentary calls it; a layup is a normal cheer.
        var big = hf[4]; int oldDunk = big.Attributes[Attr.Dunk];
        try
        {
            big.Attributes[Attr.Dunk] = 18;
            var dunk = Build(hf, af, new() { new(1, 505, PlayKind.Shot, true, big, null, ShotZone.Rim, true, 12, 10) });
            Assert.True(Choreography.IsDunk(dunk.Moments.Single(m => m.E.Kind == PlayKind.Shot).E));
            Assert.Equal(new[] { Cue.BigCheer }, Kinds(dunk));
            Assert.Contains(dunk.Lines, l => l.Text.Contains("throws it down"));
            big.Attributes[Attr.Dunk] = 8;
            var layup = Build(hf, af, new() { new(1, 505, PlayKind.Shot, true, big, null, ShotZone.Rim, true, 12, 10) });
            Assert.Equal(new[] { Cue.Cheer }, Kinds(layup));
            Assert.Contains(layup.Lines, l => l.Text.Contains("finishes at the rim"));
            // An away dunk is a groan, not a cheer.
            var awayBig = af[4]; int oldAway = awayBig.Attributes[Attr.Dunk];
            awayBig.Attributes[Attr.Dunk] = 18;
            try { Assert.Equal(new[] { Cue.Groan }, Kinds(Build(hf, af, new() { new(1, 505, PlayKind.Shot, false, awayBig, null, ShotZone.Rim, true, 10, 12) }))); }
            finally { awayBig.Attributes[Attr.Dunk] = oldAway; }
        }
        finally { big.Attributes[Attr.Dunk] = oldDunk; }
    }

    [Fact]
    public void Beaten_badly_the_crowd_answers_only_the_big_home_plays()
    {
        var (hf, af) = Fives();
        // Down 20: an ordinary home jumper, an away foul and a made home free throw get nothing; a three still gets the big
        // cheer, and a home foul is still booed.
        Assert.Empty(Kinds(Build(hf, af, new() { new(2, 505, PlayKind.Shot, true, hf[2], null, ShotZone.LongMid, true, 42, 60) }, 40, 60)));
        Assert.Equal(new[] { Cue.Whistle }, Kinds(Build(hf, af, new() { new(2, 505, PlayKind.Foul, false, af[1], hf[2], ShotZone.Rim, false, 40, 60) }, 40, 60)));
        Assert.Equal(new[] { Cue.BigCheer }, Kinds(Build(hf, af, new() { new(2, 505, PlayKind.Shot, true, hf[2], null, ShotZone.AboveBreakThree, true, 43, 60) }, 40, 60)));
        var boo = Reactions.Plan(Build(hf, af, new() { new(2, 505, PlayKind.Foul, true, hf[1], af[2], ShotZone.Rim, false, 40, 60) }, 40, 60));
        Assert.Contains(boo, q => q.Kind == Cue.Boo && q.Gain < 1);
        // Down 14 (not yet beaten): the jumper is cheered.
        Assert.Equal(new[] { Cue.Cheer }, Kinds(Build(hf, af, new() { new(2, 505, PlayKind.Shot, true, hf[2], null, ShotZone.LongMid, true, 48, 60) }, 46, 60)));
    }

    [Fact]
    public void The_bed_sinks_as_the_deficit_grows_and_comes_back_in_a_comeback()
    {
        // Whole within 12, then down steadily to 0.45 from 20 down, never rising as the deficit grows.
        Assert.Equal(1, CrowdMix.Beaten(0));
        Assert.Equal(1, CrowdMix.Beaten(-12));
        Assert.Equal(0.65, CrowdMix.Beaten(-15), 3);
        Assert.Equal(0.45, CrowdMix.Beaten(-20), 3);
        Assert.Equal(0.45, CrowdMix.Beaten(-35), 3);
        for (int d = 0; d < 40; d++) Assert.True(CrowdMix.Beaten(-d - 1) <= CrowdMix.Beaten(-d));
        // A home lead never quietens it.
        Assert.Equal(1, CrowdMix.Beaten(25));

        var even = CrowdMix.Target(0.9, 0, false, false, false, 0, 0);
        var downTwenty = CrowdMix.Target(0.9, 0, false, false, false, -20, 20);
        _out.WriteLine($"bed: even {even.Total:0.000} (high {even.High:0.000}); down 20 {downTwenty.Total:0.000} (high {downTwenty.High:0.000})");
        Assert.True(downTwenty.Total < even.Total * 0.5, "a blowout's crowd goes quiet");
        Assert.True(downTwenty.High < even.High * 0.5, "and the roar goes first");
        // A garbage-time run alone doesn't lift a beaten crowd much…
        var runWhileBeaten = CrowdMix.Target(0.9, 8, false, false, false, -22, 22);
        Assert.True(runWhileBeaten.Total < even.Total * 0.6);
        // …but a real comeback does: from 18 down to 6 down, on a run, louder than an even game's first minutes.
        Assert.True(CrowdMix.Comeback(-6, 18, 6));
        Assert.False(CrowdMix.Comeback(-6, 9, 6));    // never down 10
        Assert.False(CrowdMix.Comeback(-12, 18, 6));  // only 6 cut off
        Assert.False(CrowdMix.Comeback(-6, 18, 0));   // no run
        var comeback = CrowdMix.Target(0.9, 6, false, false, false, -6, 18);
        var sameScoreNoHistory = CrowdMix.Target(0.9, 6, false, false, false, -6, 6);
        _out.WriteLine($"bed: comeback {comeback.Total:0.000}; same score without the comeback {sameScoreNoHistory.Total:0.000}");
        Assert.True(comeback.Total > sameScoreNoHistory.Total);
        Assert.True(comeback.Total > even.Total);
        // The old call (run 153's arguments only) is unchanged.
        Assert.Equal(CrowdMix.Target(0.8, 10, true, false, false), CrowdMix.Target(0.8, 10, true, false, false, 0, 0));
    }

    [Fact]
    public void A_boo_swells_the_crowd_until_the_intake_brings_its_file()
    {
        // No crowd-boo-*.ogg is embedded yet: the boo is the bed's own swell, up quickly and back by its end.
        Assert.Empty(Enumerable.Range(1, 4).Select(n => CrowdMixer.Decode($"crowd-boo-{n}.ogg")).Where(x => x.Length > 0));
        Assert.Equal(1, CrowdMix.Swell(0, 1), 3);
        Assert.True(CrowdMix.Swell(0.25, 1) > 1.7);
        Assert.Equal(1, CrowdMix.Swell(CrowdMix.SwellTime, 1), 3);
        using var mix = new CrowdMixer();
        mix.Volume = 1;
        var buf = new float[2048];
        double Rms(int blocks) { double s = 0; int n = 0; for (int b = 0; b < blocks; b++) { mix.Read(buf, 0, buf.Length); foreach (var x in buf) { s += x * x; n++; } } return Math.Sqrt(s / n); }
        mix.SetBed(CrowdMix.Target(0.6, 0, false, false, false));
        Rms(70);
        double settled = Rms(30);
        mix.Play(Cue.Boo, 1);
        Rms(5);
        double booing = Rms(6);
        Rms(60);
        double after = Rms(30);
        _out.WriteLine($"bed rms {settled:0.0000}; booing {booing:0.0000}; after {after:0.0000}; voices {mix.Voices}");
        Assert.True(booing > settled * 1.2, "the boo is heard over the bed");
        Assert.InRange(after, settled * 0.7, settled * 1.3);
        Assert.Equal(0, mix.Voices);
        // At 4× and 8× a boo gives way, as the normal cheers do.
        Assert.False(Reactions.Plays(new SoundCue(0, Cue.Boo), 4, false));
    }
}
