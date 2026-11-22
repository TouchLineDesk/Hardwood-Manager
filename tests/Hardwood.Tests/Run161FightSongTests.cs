using System.Diagnostics;
using Hardwood.Core;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.Presentation;
using Hardwood.Core.RealWorld;
using Hardwood.Desktop.Audio;
using Cue = Hardwood.Desktop.Audio.Cue;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>Run 161, fight songs A (Danny's note `danny-fight-songs-nov22`, claude/fight-songs-plan.md): every program
/// gets an original song from the composer (tier 2), no two in a conference share a style and a key, Danny's own files in
/// the fight-songs folder play instead (tier 3), and the home band plays at the tip, halftime, a home run of 8-0 and a
/// home win. Presentation only: no engine, RNG or save change.</summary>
[Collection(SharedUiState.Name)]
public class Run161FightSongTests
{
    private readonly ITestOutputHelper _out;
    public Run161FightSongTests(ITestOutputHelper output) => _out = output;

    private static Dictionary<string, FightSongSpec> AllReal() =>
        FightSongs.Assign(RealColleges.Programs.Select(p => (p.School, p.Conference)));

    [Fact]
    public void Every_program_has_a_song_and_no_two_in_a_conference_share_a_style_and_key()
    {
        var all = AllReal();
        Assert.Equal(RealColleges.Programs.Count, all.Count);
        foreach (var conf in RealColleges.Programs.GroupBy(p => p.Conference))
        {
            var pairs = conf.Select(p => (all[p.School].Style, all[p.School].Key)).ToList();
            Assert.Equal(pairs.Count, pairs.Distinct().Count());
        }
        // Across the whole of Division I, every style is used and no style takes more than a third of the programs.
        var byStyle = all.Values.GroupBy(s => s.Style).ToDictionary(g => g.Key, g => g.Count());
        _out.WriteLine(string.Join(", ", byStyle.Select(kv => $"{kv.Key} {kv.Value}")));
        Assert.Equal(Enum.GetValues<FightStyle>().Length, byStyle.Count);
        Assert.All(byStyle.Values, n => Assert.True(n <= all.Count / 3 + 5, $"{n}"));
        // The same school always gets the same song, and a conference's song for a school matches the full assignment.
        var duke = all["Duke"];
        Assert.Equal(duke, AllReal()["Duke"]);
        var acc = RealColleges.Programs.Where(p => p.Conference == "acc").Select(p => p.School);
        Assert.Equal(duke, FightSongs.For("Duke", acc));
        _out.WriteLine($"Duke: {duke.Describe()}; Kentucky: {all["Kentucky"].Describe()}; Miami (FL): {all["Miami (FL)"].Describe()}");
    }

    [Fact]
    public void Each_score_is_a_full_chorus_with_a_short_hit_in_the_band_s_range()
    {
        foreach (var spec in AllReal().Values)
        {
            var score = FightSongs.Compose(spec);
            Assert.InRange(score.Seconds, 25, 45);
            Assert.InRange(score.HitSeconds, 7, 14);
            var lead = score.Notes.Where(n => n.Part == BandPart.Lead).ToList();
            Assert.True(lead.Count >= 40, $"{spec.Program}: {lead.Count} melody notes");
            // Trumpets from about F♯3 to C6; the tuba from about C1 to E4.
            Assert.All(lead, n => Assert.InRange(n.Midi, 54, 84));
            Assert.All(score.Notes.Where(n => n.Part == BandPart.Bass), n => Assert.InRange(n.Midi, 24, 64));
            Assert.All(score.Notes, n => Assert.InRange(n.Beat, 0, score.Beats));
            // Every melody note sits in the key (no wrong notes).
            int[] major = { 0, 2, 4, 5, 7, 9, 11 };
            Assert.All(lead, n => Assert.Contains((n.Midi - spec.Key + 120) % 12, major));
        }
        // Two schools' tunes differ (the melody, not only the key).
        var a = FightSongs.Compose(FightSongs.SpecFor("Duke")).Notes.Where(n => n.Part == BandPart.Lead).Select(n => n.Midi).Take(24);
        var b = FightSongs.Compose(FightSongs.SpecFor("Kentucky")).Notes.Where(n => n.Part == BandPart.Lead).Select(n => n.Midi).Take(24);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void A_song_renders_quickly_the_same_every_time_and_never_clips()
    {
        var spec = AllReal()["Duke"];
        var score = FightSongs.Compose(spec);
        var sw = Stopwatch.StartNew();
        var song = BandRenderer.Render(score);
        sw.Stop();
        _out.WriteLine($"{spec.Describe()}: {score.Seconds:0.0} s rendered in {sw.ElapsedMilliseconds} ms");
        // The budget: a few seconds even on a slow CI machine (it renders off the UI thread, once a session).
        Assert.True(sw.ElapsedMilliseconds < 8000, $"{sw.ElapsedMilliseconds} ms");
        Assert.InRange(song.Length / 2.0 / Synth.Rate, score.Seconds, score.Seconds + 2);
        float peak = song.Max(MathF.Abs);
        Assert.InRange(peak, 0.5f, 0.81f);
        Assert.DoesNotContain(song, float.IsNaN);
        double rms = Math.Sqrt(song.Average(x => (double)x * x));
        Assert.InRange(rms, 0.03, 0.4);
        Assert.Equal(song, BandRenderer.Render(score));
        var hit = BandRenderer.Render(score, score.HitFromBeat);
        Assert.InRange(hit.Length / 2.0 / Synth.Rate, score.HitSeconds, score.HitSeconds + 2);
    }

    [Fact]
    public void Danny_s_own_file_plays_instead_of_the_composed_song()
    {
        string old = FightSongLibrary.CustomFolder;
        string dir = Path.Combine(Path.GetTempPath(), "hw-fight-" + Guid.NewGuid().ToString("N"));
        try
        {
            FightSongLibrary.CustomFolder = dir;
            FightSongLibrary.Forget();
            Assert.Equal(0, FightSongLibrary.CustomCount());
            var spec = FightSongs.SpecFor("Miami (FL)");
            Assert.Equal(spec.Describe(), FightSongLibrary.For("Miami (FL)", spec).Source);
            // A 2-second 22.05 kHz mono WAV, named by the school's slug.
            Directory.CreateDirectory(dir);
            using (var w = new NAudio.Wave.WaveFileWriter(Path.Combine(dir, "miami-fl.wav"), new NAudio.Wave.WaveFormat(22050, 16, 1)))
                for (int i = 0; i < 44100; i++) w.WriteSample((float)(0.3 * Math.Sin(2 * Math.PI * 440 * i / 22050.0)));
            Assert.Equal("miami-fl", FightSongLibrary.Slug("Miami (FL)"));
            Assert.Equal("texas-am", FightSongLibrary.Slug("Texas A&M"));
            Assert.Equal("st-johns", FightSongLibrary.Slug("St. John's"));
            Assert.Equal(1, FightSongLibrary.CustomCount());
            FightSongLibrary.Forget();
            var song = FightSongLibrary.For("Miami (FL)", spec);
            Assert.Equal("miami-fl.wav", song.Source);
            Assert.InRange(song.Song.Length / 2.0 / Synth.Rate, 1.95, 2.05);   // resampled to 44.1 kHz stereo
            Assert.True(song.Hit.Length > 0 && song.Hit.Length <= song.Song.Length);
            // Another school still gets its own composed song.
            Assert.Null(FightSongLibrary.CustomFile("Duke"));
        }
        finally
        {
            FightSongLibrary.CustomFolder = old;
            FightSongLibrary.Forget();
            try { Directory.Delete(dir, true); } catch (Exception) { }
        }
    }

    private static (List<Player> Home, List<Player> Away) Fives()
    {
        var l = new WorldGenerator(new Rng(17)).Generate("L", 2026, new DateOnly(2026, 10, 1));
        List<Player> Five(Team t) => t.Roster.OrderByDescending(p => p.Overall).Take(5).OrderBy(p => (int)p.PrimaryPosition).ToList();
        return (Five(l.Teams[0]), Five(l.Teams[1]));
    }

    private static Choreography Build(List<Player> hf, List<Player> af, List<PlayEvent> ev, int index = 7, int homeStart = 0, int awayStart = 0, int periods = 2)
    {
        var start = hf.Concat(af).Select((p, i) => (p, i)).ToDictionary(x => x.p, x => new Spot(47 + (x.i < 5 ? -4 : 4), 10 + (x.i % 5) * 7));
        var input = new PossessionInput(index, 2026, ev, 516, 500, hf, af, start, homeStart, awayStart, true, "Home", "Away",
            DefensiveScheme.Man, DefensiveScheme.Man, PossessionStart.DeadBall, periods, false);
        return PossessionChoreographer.Build(input);
    }

    [Fact]
    public void The_band_plays_at_the_tip_halftime_a_home_run_and_a_home_win()
    {
        var (hf, af) = Fives();
        // The tip: the song.
        Assert.Contains(Reactions.Court(Build(hf, af, new() { new(1, 1180, PlayKind.Shot, true, hf[1], null, ShotZone.LongMid, false, 0, 0) }, index: 0)),
            q => q.Kind == Cue.FightSong);
        // College halftime (period 1 of 2): the song; the final with the home side ahead: the song; behind: nothing.
        var half = Build(hf, af, new() { new(1, 0, PlayKind.PeriodEnd, true, null, null, ShotZone.Rim, false, 30, 28) });
        Assert.Contains(Reactions.Court(half), q => q.Kind == Cue.FightSong);
        var won = Build(hf, af, new() { new(2, 0, PlayKind.PeriodEnd, true, null, null, ShotZone.Rim, false, 70, 64) }, 7, 70, 64);
        Assert.Contains(Reactions.Court(won), q => q.Kind == Cue.FightSong);
        var lost = Build(hf, af, new() { new(2, 0, PlayKind.PeriodEnd, true, null, null, ShotZone.Rim, false, 60, 64) }, 7, 60, 64);
        Assert.DoesNotContain(Reactions.Court(lost), q => q.Kind is Cue.FightSong or Cue.Drumline);
        // A tie at the end of regulation goes to overtime: the drumline, not the song.
        var tied = Build(hf, af, new() { new(2, 0, PlayKind.PeriodEnd, true, null, null, ShotZone.Rim, false, 64, 64) }, 7, 64, 64);
        Assert.Contains(Reactions.Court(tied), q => q.Kind == Cue.Drumline);
        // A pro game's first quarter (4 periods): still the drumline at the break.
        var q1 = Build(hf, af, new() { new(1, 0, PlayKind.PeriodEnd, true, null, null, ShotZone.Rim, false, 30, 28) }, periods: 4);
        Assert.DoesNotContain(Reactions.Court(q1), q => q.Kind == Cue.FightSong);

        // A home run: 6 points, then 3 more, then the next dead ball (a foul) brings the hit, once.
        var run = new BandRun();
        Assert.Empty(Reactions.Band(Build(hf, af, new()
        {
            new(1, 600, PlayKind.Shot, true, hf[1], null, ShotZone.AboveBreakThree, true, 13, 10),
            new(1, 580, PlayKind.Shot, true, hf[2], null, ShotZone.AboveBreakThree, true, 16, 10),
        }, 7, 10, 10), run));
        Assert.Equal(6, run.Points);
        var hit = Reactions.Band(Build(hf, af, new()
        {
            new(1, 560, PlayKind.Shot, true, hf[2], null, ShotZone.AboveBreakThree, true, 19, 10),
            new(1, 540, PlayKind.Foul, false, af[1], null, ShotZone.Rim, false, 19, 10),
        }, 8, 16, 10), run);
        Assert.Single(hit, q => q.Kind == Cue.FightHit);
        Assert.Empty(Reactions.Band(Build(hf, af, new() { new(1, 520, PlayKind.Foul, false, af[2], null, ShotZone.Rim, false, 19, 10) }, 9, 19, 10), run));
        // The visitors score: the run is over.
        Reactions.Band(Build(hf, af, new() { new(1, 500, PlayKind.Shot, false, af[1], null, ShotZone.Rim, true, 19, 12) }, 10, 19, 10), run);
        Assert.Equal(0, run.Points);
        Assert.False(run.Played);
        // At 4× and 8×, the song still plays at the tip and the final; the hit doesn't.
        Assert.True(Reactions.Plays(new SoundCue(0, Cue.FightSong), 8, false));
        Assert.False(Reactions.Plays(new SoundCue(0, Cue.FightHit), 4, false));
    }

    [Fact]
    public void The_mixer_plays_the_song_and_falls_back_to_the_drumline_without_a_band()
    {
        var spec = FightSongs.SpecFor("Duke");
        var score = FightSongs.Compose(spec);
        var song = new BandSong("Duke", BandRenderer.Render(score), BandRenderer.Render(score, score.HitFromBeat), spec.Describe());
        using var mix = new CrowdMixer(_ => null) { Volume = 1 };
        mix.SetBed(new BedLevels(0, 0));
        var buf = new float[4410 * 2];
        // No band and no pack: the fight-song cue becomes the drumline, which this build doesn't have, so nothing plays.
        mix.Play(Cue.FightSong, 1);
        Assert.Equal(0, mix.Voices);
        mix.Play(Cue.FightHit, 1);
        Assert.Equal(0, mix.Voices);
        mix.SetBand(song);
        mix.Play(Cue.FightSong, 1);
        Assert.Equal(1, mix.Voices);
        double rms = 0;
        for (int b = 0; b < 10; b++) { mix.Read(buf, 0, buf.Length); rms += buf.Sum(x => (double)x * x); }
        Assert.True(rms > 1, $"{rms}");
        // The hit takes over from the song (one song at a time), and court sounds never push the song out.
        mix.Play(Cue.FightHit, 1);
        mix.Read(buf, 0, buf.Length);
        Assert.Equal(1, mix.Voices);
        for (int i = 0; i < 12; i++) mix.Play(Cue.Whistle, 1);
        mix.Read(buf, 0, buf.Length);   // the voices stolen fade out within the block
        Assert.True(mix.Voices <= CrowdMixer.MaxVoices, $"{mix.Voices}");
        Assert.DoesNotContain(buf, float.IsNaN);
    }

    /// <summary>Danny's demo reel (fight-songs plan, verification): with HW_FIGHT_DEMO set to a folder, writes ten
    /// programs' songs, one after another, to fight-songs-demo.wav there (convert with ffmpeg). Does nothing otherwise.</summary>
    [Fact]
    public void Demo_reel_when_asked()
    {
        var dir = Environment.GetEnvironmentVariable("HW_FIGHT_DEMO");
        if (string.IsNullOrEmpty(dir)) return;
        var all = AllReal();
        string[] schools = { "Duke", "Kentucky", "Miami (FL)", "Kansas", "Gonzaga", "Butler", "Purdue", "Vermont", "Saint Mary's", "Houston" };
        using var w = new NAudio.Wave.WaveFileWriter(Path.Combine(dir, "fight-songs-demo.wav"), NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(Synth.Rate, 2));
        foreach (var school in schools)
        {
            var spec = all[school];
            _out.WriteLine($"{school}: {spec.Describe()}");
            var song = BandRenderer.Render(FightSongs.Compose(spec));
            w.WriteSamples(song, 0, song.Length);
            var gap = new float[Synth.Rate * 2];
            w.WriteSamples(gap, 0, gap.Length);
        }
    }
}
