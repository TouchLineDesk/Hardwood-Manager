using Hardwood.Core;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.International;
using Hardwood.Core.Model;
using Hardwood.Core.Presentation;
using Hardwood.Core.Season;
using Hardwood.Desktop;
using Hardwood.Desktop.Audio;
using Hardwood.Desktop.ViewModels;
using Cue = Hardwood.Desktop.Audio.Cue;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>Run 216, a polish run: K44 (the college band played at NBA and national games), K42 (NBA stars played the
/// qualifying windows) and K45's International-page half (accepting a job left another nation selected).</summary>
[Collection(SharedUiState.Name)]
public class Run216PolishTests
{
    private readonly ITestOutputHelper _out;
    public Run216PolishTests(ITestOutputHelper output) => _out = output;

    private static (List<Player> Home, List<Player> Away) Fives()
    {
        var l = new WorldGenerator(new Rng(17)).Generate("L", 2026, new DateOnly(2026, 10, 1));
        List<Player> Five(Team t) => t.Roster.OrderByDescending(p => p.Overall).Take(5).OrderBy(p => (int)p.PrimaryPosition).ToList();
        return (Five(l.Teams[0]), Five(l.Teams[1]));
    }

    private static Choreography Build(List<Player> hf, List<Player> af, List<PlayEvent> ev, int index)
    {
        var start = hf.Concat(af).Select((p, i) => (p, i)).ToDictionary(x => x.p, x => new Spot(47 + (x.i < 5 ? -4 : 4), 10 + (x.i % 5) * 7));
        var input = new PossessionInput(index, 2026, ev, 516, 500, hf, af, start, 0, 0, true, "Home", "Away",
            DefensiveScheme.Man, DefensiveScheme.Man, PossessionStart.DeadBall, 4, false);
        return PossessionChoreographer.Build(input);
    }

    private static GameSession NewCareer()
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(31, 2026, GameSession.DefaultFullSim);
        var me = world.College!.Programs.First(t => t.FullName.StartsWith("Virginia Tech"));
        return GameSession.NewGame(31, me.Id, null);
    }

    [Fact]
    public void K44_no_band_cues_without_a_band_and_the_college_band_keeps_its_cues()
    {
        var (hf, af) = Fives();
        static bool BandCue(SoundCue q) => q.Kind is Cue.FightSong or Cue.Drumline or Cue.Brass or Cue.FightHit;
        var tip = Build(hf, af, new() { new(1, 700, PlayKind.Shot, true, hf[1], null, ShotZone.LongMid, false, 0, 0) }, 0);
        var dunk = Build(hf, af, new() { new(4, 30, PlayKind.Shot, true, hf[4], null, ShotZone.Rim, true, 2, 0) }, 40);
        var half = Build(hf, af, new() { new(2, 0, PlayKind.PeriodEnd, true, null, null, ShotZone.Rim, false, 40, 38) }, 60);
        var quarter = Build(hf, af, new() { new(1, 0, PlayKind.PeriodEnd, true, null, null, ShotZone.Rim, false, 20, 18) }, 30);
        var win = Build(hf, af, new() { new(4, 0, PlayKind.PeriodEnd, true, null, null, ShotZone.Rim, false, 100, 90) }, 120);
        foreach (var c in new[] { tip, dunk, half, quarter, win })
        {
            var pro = Reactions.Court(c, band: false);
            Assert.DoesNotContain(pro, BandCue);
            // The court itself still sounds (the buzzer, the rim and net).
            Assert.Equal(Reactions.Court(c).Where(q => !BandCue(q)).Select(q => q.Kind), pro.Select(q => q.Kind));
        }
        Assert.Contains(Reactions.Court(tip), q => q.Kind == Cue.FightSong);
        Assert.Contains(Reactions.Court(quarter), q => q.Kind == Cue.Drumline);
        Assert.Contains(Reactions.Court(half), q => q.Kind == Cue.FightSong);
        Assert.Contains(Reactions.Court(win), q => q.Kind == Cue.FightSong);
    }

    [Fact]
    public void K42_window_twelves_have_no_NBA_players_and_summer_twelves_are_unchanged()
    {
        var s = NewCareer();
        var nov = FibaCalendar.InYear(2026).Single(e => e.Kind == FibaEventKind.Window && e.Start.Month == 11);
        var nba = s.League.Teams.SelectMany(t => t.Roster).Select(p => p.Id).ToHashSet();
        var summer = NationalTeams.For(s, nov.Start);
        var window = NationalTeams.For(s, nov.Start, window: true);
        int heldInSummer = summer.Sum(t => t.Squad.Count(x => nba.Contains(x.Player.Id)));
        _out.WriteLine($"NBA players in the summer twelves: {heldInSummer}; teams {summer.Count} → {window.Count} in the window");
        Assert.True(heldInSummer > 20);
        // Review (run 216): no nation drops out of the window; one short of twelve released players calls up its NBA players
        // to make up the twelve (and only then).
        Assert.Equal(summer.Select(t => t.Nation.Code), window.Select(t => t.Nation.Code));
        var short_ = window.Where(t => t.Squad.Any(x => nba.Contains(x.Player.Id))).ToList();
        _out.WriteLine("short of released players: " + string.Join(", ", short_.Select(t => $"{t.Name} ({t.Squad.Count(x => !nba.Contains(x.Player.Id))} released)")));
        Assert.All(short_, t => Assert.True(t.Pool.Count(x => !nba.Contains(x.Player.Id)) < NationalTeams.SquadSize));
        Assert.All(window.Except(short_), t => Assert.DoesNotContain(t.Squad, x => nba.Contains(x.Player.Id)));
        Assert.All(window, t => Assert.Equal(NationalTeams.SquadSize, t.Squad.Count));
        // The default (summer) build is the same as before: everyone.
        Assert.Equal(NationalTeams.Build(WorldCup.Entries(s, nov.Start), nov.Start).Select(t => (t.Nation.Code, t.Strength)),
            summer.Select(t => (t.Nation.Code, t.Strength)));

        var fra = summer.FirstOrDefault(t => t.Nation.Code == "FRA");
        var fraW = window.FirstOrDefault(t => t.Nation.Code == "FRA");
        if (fra != null && fraW != null)
        {
            _out.WriteLine($"France: {fra.Strength:F1} ({fra.Squad[0].Player.Name}) → window {fraW.Strength:F1} ({fraW.Squad[0].Player.Name})");
            Assert.True(fraW.Strength < fra.Strength);
        }
        Assert.Contains(s.League.Teams.SelectMany(t => t.Roster), p => NationalTeams.HeldForWindows(s, s.League.Teams.First(t => t.Roster.Contains(p))));
        Assert.False(NationalTeams.HeldForWindows(s, null));
        Assert.False(NationalTeams.HeldForWindows(s, s.World.Clubs.First()));
    }

    [Fact]
    public void K42_the_window_plays_at_the_released_strength_and_ranks_and_draws_as_before()
    {
        var s = NewCareer();
        var nov = FibaCalendar.InYear(2026).Single(e => e.Kind == FibaEventKind.Window && e.Start.Month == 11);
        var teams = NationalTeams.For(s, nov.Start);
        var same = teams.ToDictionary(t => t.Nation.Code, t => t.Strength);
        var h0 = new InternationalHistory(); var a = Qualifying.Play(h0, nov, teams, s.Seed);
        var h1 = new InternationalHistory(); var b = Qualifying.Play(h1, nov, teams, s.Seed, null, same);
        Assert.Equal(a.Games.Select(g => (g.Home, g.Away, g.HomePts, g.AwayPts)), b.Games.Select(g => (g.Home, g.Away, g.HomePts, g.AwayPts)));

        var released = NationalTeams.WindowStrengths(s, nov.Start);
        var h2 = new InternationalHistory(); var c = Qualifying.Play(h2, nov, teams, s.Seed, null, released);
        Assert.Equal(h0.Cycles.Single().Groups.SelectMany(g => g.Value), h2.Cycles.Single().Groups.SelectMany(g => g.Value)); // the draw holds
        Assert.Equal(a.Games.Select(g => (g.Home, g.Away)), c.Games.Select(g => (g.Home, g.Away)));
        Assert.NotEqual(a.Games.Select(g => g.HomePts - g.AwayPts), c.Games.Select(g => g.HomePts - g.AwayPts));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void K45_accepting_a_national_job_opens_the_page_on_your_nation()
    {
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { AutoPortraits = false });
        try
        {
            var s = NewCareer();
            var job = s.NationalJobState;
            job.Offers = NationalJob.OffersFor(NationalTeams.Ranked(s), 30, s.Seed, s.Today);
            var vm = new MainViewModel();
            vm.AttachSession(s);
            vm.Navigate("International");
            var page = Assert.IsType<InternationalViewModel>(vm.CurrentPage);
            var other = page.Nations.First(n => n.Code != job.Offers[0].Code);
            page.Selected = other;
            string code = job.Offers[0].Code;
            page.Offers[0].Accept.Execute(null);
            Assert.Equal(code, s.NationalJobState.Code);
            Assert.Equal(code, page.Selected?.Code);
            Assert.True(page.EditingMine);
        }
        finally { AppSettings.FilePath = oldPath; AppSettings.Use(oldSettings); }
    }
}
