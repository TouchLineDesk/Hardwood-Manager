using Hardwood.Core;
using Hardwood.Core.College;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Guide;
using Hardwood.Core.Model;
using Hardwood.Core.Presentation;
using Hardwood.Core.Season;
using Hardwood.Desktop.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>Run 224, a polish run: K45's short pro Welcome, K37's live panel, run 223's cup studio set, K43's postgame
/// beat for a held-off rally, H215's bench initials and H214's road dates.</summary>
public class Run224PolishTests
{
    private readonly ITestOutputHelper _out;
    public Run224PolishTests(ITestOutputHelper output) => _out = output;

    private static int ProId(ulong seed) => Hardwood.Core.RealWorld.RealWorldData.NewWorld(seed, 2026).League.Teams[5].Id;

    private static int Words(string s) => s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    [Fact]
    public void K45_the_pro_welcome_is_short_and_names_the_goal_and_the_first_opponent()
    {
        var s = GameSession.NewGame(31, ProId(31), null);
        var welcome = s.Inbox[0];
        _out.WriteLine(welcome.Body);
        Assert.Equal("Welcome to the job", welcome.Title);
        Assert.True(Words(welcome.Body) < 120, $"{Words(welcome.Body)} words");
        var first = Assert.IsType<MyGame>(s.NextMyGame);
        Assert.Contains(first.Opponent.TheFullName, welcome.Body);
        Assert.Contains($"around {s.Job.Current!.TargetWins} wins", welcome.Body);
        Assert.DoesNotContain("checked", welcome.Body);
        // The data's provenance moved to its own message, below the Welcome and the goal.
        var about = Assert.Single(s.Inbox, m => m.Title == "About this world");
        Assert.Contains("rosters are the real ones", about.Body);
        Assert.True(s.Inbox.IndexOf(about) > s.Inbox.IndexOf(welcome));
    }

    [Fact]
    public void K37_the_live_panel_keeps_only_this_games_items()
    {
        GuideTask T(string key) => new(key, 10, "t", "b", null);
        Assert.False(GuideViewModel.HiddenInLiveGame(T("hurt-12")));
        Assert.False(GuideViewModel.HiddenInLiveGame(T("inelig-12")));
        Assert.False(GuideViewModel.HiddenInLiveGame(T("short-2026-11-03")));
        foreach (var k in new[] { "decision-4", "morale-7.9", "inbox-33", "staff-Physio", "board-6", "trail-2027-2-0", "bids-1-x", "scout-x", "visit-Squad" })
            Assert.True(GuideViewModel.HiddenInLiveGame(T(k)), k);
    }

    [Fact]
    public void The_studio_names_a_cup_knockout_and_doesnt_call_las_vegas_from_the_home_arena()
    {
        var s = GameSession.NewGame(31, ProId(31), null);
        var cup = Assert.IsType<CupSeason>(s.Cup);
        var home = s.HumanTeam; var away = s.League.Teams.First(t => t != home);
        var day = s.Today.AddDays(3);
        cup.Ties.Add(new CupTie { Round = NbaCup.SemiFinal, Date = day, HomeId = home.Id, AwayId = away.Id, Neutral = true });
        var show = s.BuildStudioShow(home, away, day);
        _out.WriteLine($"{show.ShowName} · {show.SetLine}\n{string.Join("\n", show.Segments[0].Lines.Select(l => l.Text))}");
        Assert.Equal("HSN NBA Cup", show.ShowName);
        Assert.Contains("semi-final", show.SetLine);
        Assert.Contains("Las Vegas", show.SetLine);
        Assert.True(show.Neutral);
        Assert.Equal("", show.Venue);
        Assert.Contains("live from Las Vegas", show.Segments[0].Lines[0].Text);
        Assert.Contains("neutral floor", show.Segments[0].Lines[0].Text);

        // A quarter-final at the better seed's arena stays at home.
        var q = s.Today.AddDays(5);
        cup.Ties.Add(new CupTie { Round = NbaCup.QuarterFinal, Date = q, HomeId = home.Id, AwayId = away.Id });
        var qs = s.BuildStudioShow(home, away, q);
        Assert.Equal("NBA Cup quarter-final", qs.SetLine);
        Assert.False(qs.Neutral);
        Assert.NotEqual("", qs.Venue);

        // An ordinary night is unchanged.
        Assert.NotEqual("HSN NBA Cup", s.BuildStudioShow(home, away, s.Today.AddDays(9)).ShowName);
    }

    private static Team T(int id, string abbr) =>
        new() { Id = id, City = abbr, Name = abbr + "s", Abbreviation = abbr, Division = "D", PrimaryColor = "#0033A0", SecondaryColor = "#FFFFFF" };

    private static GameResult Game(params (int period, int home, int away)[] flow)
    {
        var r = new GameResult { Home = new TeamBox { Team = T(1, "SAS"), Possessions = 98 }, Away = new TeamBox { Team = T(2, "SEA"), Possessions = 98 }, Periods = 4, PeriodSeconds = 720 };
        var (_, hp, ap) = flow[^1];
        r.Home.Lines.Add(new PlayerLine { Player = new Player { Id = 1, FirstName = "A", LastName = "Home", PrimaryPosition = Position.SF }, Seconds = 1500, Started = true, Points = hp, FGM = hp / 2, FGA = hp });
        r.Away.Lines.Add(new PlayerLine { Player = new Player { Id = 2, FirstName = "B", LastName = "Away", PrimaryPosition = Position.SF }, Seconds = 1500, Started = true, Points = ap, FGM = ap / 2, FGA = ap });
        double t = 0;
        foreach (var (p, h, a) in flow) r.Flow.Add(new FlowPoint(t += 100, p, h, a));
        r.CurrentPeriod = 4; r.Elapsed = 2880;
        return r;
    }

    [Fact]
    public void K43_the_postgame_desk_calls_a_held_off_rally_like_the_final_call()
    {
        // The critic's opener: 96-77 after three, won 125-119.
        var r = Game((1, 10, 9), (2, 60, 55), (3, 96, 77), (4, 125, 119));
        var story = PostgameDesk.Build(r)[0].Line!.Text;
        _out.WriteLine(story);
        Assert.Contains("led by 19 after halftime and had to hold off a late SEAs rally", story);
        // A 15-point lead in the first half only isn't one (as GameCalls.Final).
        var early = PostgameDesk.Build(Game((1, 30, 15), (3, 60, 58), (4, 100, 96)))[0].Line!.Text;
        Assert.DoesNotContain("rally", early);
    }

    [Fact]
    public void H215_a_surname_shared_with_a_man_on_the_other_bench_gets_an_initial()
    {
        var l = new WorldGenerator(new Rng(17)).Generate("L", 2026, new DateOnly(2026, 10, 1));
        var h = l.Teams[0]; var a = l.Teams[1];
        h.Tactic = new Tactic(); a.Tactic = new Tactic();
        var hf = h.Roster.OrderByDescending(p => p.Overall).Take(5).OrderBy(p => (int)p.PrimaryPosition).ToList();
        var af = a.Roster.OrderByDescending(p => p.Overall).Take(5).OrderBy(p => (int)p.PrimaryPosition).ToList();
        var benchTwin = new Player { Id = 999_002, FirstName = "Devante", LastName = af[1].LastName, TeamId = h.Id };
        var ev = new List<PlayEvent> { new(1, 400, PlayKind.Steal, false, af[1], hf[0], ShotZone.Rim, false, 0, 0) };
        PossessionInput Input(IReadOnlyList<Player>? dressed) =>
            new(9, 2026, ev, 410, 400, hf, af, hf.Concat(af).Select((p, i) => (p, i)).ToDictionary(x => x.p, x => new Spot(47 + (x.i < 5 ? -4 : 4), 10 + (x.i % 5) * 7)),
                0, 0, true, "Home", "Away", DefensiveScheme.Man, DefensiveScheme.Man, PossessionStart.DeadBall, 2, false) { Dressed = dressed };
        var plain = PossessionChoreographer.Build(Input(null));
        var dressed = PossessionChoreographer.Build(Input(h.Roster.Append(benchTwin).Concat(a.Roster).ToList()));
        string Said(Choreography c) => string.Join(" | ", c.Lines.Select(x => x.Text));
        _out.WriteLine(Said(plain)); _out.WriteLine(Said(dressed));
        Assert.DoesNotContain($"{af[1].FirstName[0]}. {af[1].LastName}", Said(plain));
        Assert.Contains($"{af[1].FirstName[0]}. {af[1].LastName}", Said(dressed));
        // Wording only: the movement is the same.
        Assert.Equal(plain.Actions.Count, dressed.Actions.Count);
    }

    [Fact]
    public void H214_a_passed_road_event_says_why_and_gives_its_year()
    {
        var s = GameSession.NewGame(31, Hardwood.Tests.Run224Helpers.CollegeId(31), null);
        var rows = s.RoadCalendar();
        Assert.NotEmpty(rows);
        foreach (var r in rows) _out.WriteLine($"{r.Title} · {r.When} · {r.Result}");
        Assert.DoesNotContain(rows, r => r.Result != null && r.Result.Contains("(or before"));
        Assert.All(rows.Where(r => r.Result?.StartsWith("Not run") == true), r => Assert.Equal("Not run: it came before you took the job.", r.Result));
        // The year shows only when the event wasn't this year (hunt 27: Apr 13 2027 showed 2026's "Apr 24–Apr 26 · Passed").
        Assert.All(rows, r => Assert.Equal(CollegeWorld.RoadDay(r.Event, s.TrailWorld!.RecruitingClass(s.Today)).Year != s.Today.Year, r.OtherYear != null));
        var past = rows[0] with { Result = "Not run: it came before you took the job.", Open = false, OtherYear = 2026 };
        Assert.Equal("Passed · 2026", new RoadTile(past, null).State);
        Assert.Equal("Passed", new RoadTile(past with { OtherYear = null }, null).State);
    }
}

internal static class Run224Helpers
{
    /// <summary>A college career's team id (Virginia Tech when it's there, else the first program).</summary>
    public static int CollegeId(ulong seed)
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(seed, 2026, GameSession.DefaultFullSim);
        var c = world.College!;
        return (c.Programs.FirstOrDefault(t => t.FullName == "Virginia Tech Hokies") ?? c.Programs[0]).Id;
    }
}
