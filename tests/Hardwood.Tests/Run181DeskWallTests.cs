using Hardwood.Core.Engine;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// Run 181, K29 (critic review #22; hunt 24): the studio show looked amateur. The hosts stood in the rig's arms-out pose (the
/// render is redone with a relaxed idle in <c>tools/Render/mpfb_people.py</c>), and the big screen was two-thirds empty ("The
/// final · Leading scorer"). Now every data beat fills the wall (at least 6 rows), the session adds the matchup (records,
/// ranks, the conference, form, the series), and an ordinary game gets a short show while a ranked, rivalry or March game
/// gets the full one.
/// </summary>
public class Run181DeskWallTests
{
    private static Team T(int id, string city, string name, string abbr) =>
        new() { Id = id, City = city, Name = name, Abbreviation = abbr, Division = "D", PrimaryColor = "#0033A0", SecondaryColor = "#FFFFFF" };

    private static PlayerLine L(int id, string last, int pts, int fgm, int fga, int tpm = 0, int tpa = 0, int ftm = 0, int fta = 0, int dreb = 0, int ast = 0,
                                int tov = 0, int fouls = 0, int rim = 0, bool started = true)
    {
        var l = new PlayerLine { Player = new Player { Id = id, FirstName = "A", LastName = last, PrimaryPosition = Position.SF }, Seconds = 1200, Started = started,
            Points = pts, FGM = fgm, FGA = fga, ThreePM = tpm, ThreePA = tpa, FTM = ftm, FTA = fta, DReb = dreb, Assists = ast, Turnovers = tov, Fouls = fouls };
        l.ZoneMakes[0] = rim; l.ZoneAttempts[0] = rim + 2;
        return l;
    }

    /// <summary>Kentucky win at Louisville 85-80, with a first half and a timeline.</summary>
    private static GameResult Game()
    {
        var r = new GameResult { Home = new TeamBox { Team = T(1, "Louisville", "Cardinals", "LOU"), Possessions = 70 },
            Away = new TeamBox { Team = T(2, "Kentucky", "Wildcats", "UK"), Possessions = 70 }, Periods = 2, PeriodSeconds = 1200 };
        r.Home.Lines.Add(L(1, "Hopkins", 30, 11, 22, tpm: 3, tpa: 8, ftm: 5, fta: 6, dreb: 6, fouls: 3, rim: 5));
        r.Home.Lines.Add(L(2, "Big", 26, 11, 20, ftm: 4, fta: 5, dreb: 9, rim: 8));
        r.Home.Lines.Add(L(3, "Bench", 24, 9, 22, tpm: 2, tpa: 7, ftm: 4, fta: 4, rim: 3, started: false));
        r.Away.Lines.Add(L(4, "Guard", 33, 12, 23, tpm: 5, tpa: 10, ftm: 4, fta: 4, ast: 7, tov: 2, rim: 3));
        r.Away.Lines.Add(L(5, "Wing", 28, 10, 19, tpm: 2, tpa: 6, ftm: 6, fta: 7, tov: 3, fouls: 4, rim: 5));
        r.Away.Lines.Add(L(6, "Sub", 24, 9, 21, tpm: 1, tpa: 4, ftm: 5, fta: 6, rim: 4, started: false));
        Assert.Equal(80, r.Home.Points); Assert.Equal(85, r.Away.Points);
        int h = 0, a = 0;
        void Score(double t, int p, bool home, int pts) { if (home) h += pts; else a += pts; r.Flow.Add(new FlowPoint(t, p, h, a)); }
        for (int i = 0; i < 20; i++) { Score(30 + i * 55, 1, i % 2 == 0, 2); }   // trading baskets: ties along the way
        for (int i = 0; i < 6; i++) Score(1250 + i * 60, 2, false, 3);
        while (h < 80 || a < 85) { if (h < 80) Score(1700 + h, 2, true, Math.Min(2, 80 - h)); if (a < 85) Score(1701 + a, 2, false, Math.Min(2, 85 - a)); }
        r.CurrentPeriod = 2; r.Elapsed = 2400;
        return r;
    }

    private static readonly DeskRow[] Matchup =
    {
        new("Record", "4-0", "3-1"), new("Top 25", "-", "No. 12"), new("Conference", "-", "-"), new("Last 5", "4-0", "3-1"), new("Series", "1", "0", -1),
    };
    private static DeskContext Rivalry => new(DeskShowLength.Full, "Rivalry · Battle of the Bluegrass", Matchup, "the Battle of the Bluegrass", null, 12);
    private static DeskContext Ranked => new(DeskShowLength.Full, "Top 25", Matchup, null, null, 12);
    private static DeskContext Ordinary => new(DeskShowLength.Short, "", Matchup);

    private static readonly string[] Around = { "Final: #3 Duke 70, Clemson 61", "Later: #8 Kansas at Iowa State" };

    private static void Walls(IEnumerable<DeskBeat> beats)
    {
        foreach (var b in beats.Where(b => b.Kicker != "AROUND COLLEGE"))
        {
            Assert.True(b.Rows.Count >= HalftimeDesk.MinRows, $"{b.Kicker} · {b.Title}: {b.Rows.Count} rows on the wall");
            Assert.True(b.Rows.Count <= HalftimeDesk.MaxRows, $"{b.Kicker} · {b.Title}: {b.Rows.Count} rows overflow the wall");
            Assert.Equal(b.Rows.Count, b.Rows.Where(x => x.Label != "").Select(x => x.Label).Distinct().Count() + b.Rows.Count(x => x.Label == ""));
        }
    }

    [Fact]
    public void Every_postgame_beat_fills_the_wall_and_the_show_length_follows_the_game()
    {
        var r = Game();
        var table = new List<DeskRow> { new("", "Kentucky Wildcats", "Louisville Cardinals"), new("Record", "5-0", "3-2", Key: true), new("Next", "home to DUKE · Nov 7", "") };
        var full = PostgameDesk.Build(r, table, "Kentucky Wildcats win and move to 5-0.", Around, Rivalry);
        var shortShow = PostgameDesk.Build(r, table, "Kentucky Wildcats win and move to 5-0.", Around, Ordinary);
        Walls(full); Walls(shortShow);
        // Segment counts by game type: the full show has the turning point and around college; the short one doesn't.
        Assert.Equal(new[] { "POSTGAME", "PLAYER OF THE GAME", "BY THE NUMBERS", "TURNING POINT", "WHAT IT MEANS", "AROUND COLLEGE" }, full.Select(b => b.Kicker));
        Assert.Equal(new[] { "POSTGAME", "PLAYER OF THE GAME", "BY THE NUMBERS", "WHAT IT MEANS" }, shortShow.Select(b => b.Kicker));
        // The final: the score, each half, the leading scorers, the biggest leads.
        var fin = full[0];
        Assert.Equal("The final · Rivalry · Battle of the Bluegrass", fin.Title);
        Assert.Equal(new[] { "FINAL", "H1", "H2", "Leading scorer" }, fin.Rows.Take(4).Select(x => x.Label));
        var (hs, aw) = GameFlow.Splits(r);
        Assert.Equal(aw[0].ToString(), fin.Rows[1].Away); Assert.Equal(hs[1].ToString(), fin.Rows[2].Home);
        Assert.Contains(fin.Rows, x => x.Label == "Biggest lead");
        // H193: the host names the rivalry (and, without one, the ranked road win).
        Assert.Contains("That's the Battle of the Bluegrass, and it goes to Wildcats.", fin.Line!.Text);
        Assert.EndsWith("A road win at No. 12 Cardinals.", PostgameDesk.Build(r, table, null, null, Ranked)[0].Line!.Text);
        Assert.DoesNotContain("No. 12", shortShow[0].Line!.Text);
        // Review (run 181): on a neutral floor (a tournament, March) it's a win over a ranked side, not a road win.
        Assert.EndsWith("A win over No. 12 Cardinals.", PostgameDesk.Build(r, table, null, null, Ranked with { Neutral = true })[0].Line!.Text);
        // What it means: the table's rows, then the matchup it didn't have, and Next stays last.
        var means = full.Single(b => b.Kicker == "WHAT IT MEANS");
        Assert.Equal("5-0", means.Rows.Single(x => x.Label == "Record").Away);
        Assert.Equal(new[] { "", "Record", "Top 25", "Conference", "Last 5", "Series", "Next" }, means.Rows.Select(x => x.Label));
        // The turning point counts the ties.
        Assert.Equal(GameFlow.TimesTied(r).ToString(), full.Single(b => b.Kicker == "TURNING POINT").Rows.Single(x => x.Label == "Times tied").Away);
        Assert.True(GameFlow.TimesTied(r) >= 5);
    }

    [Fact]
    public void Every_halftime_beat_fills_the_wall_and_an_ordinary_game_skips_around_college()
    {
        var r = Game();
        var full = HalftimeDesk.Build(r, 0.4, Around, null, Rivalry);
        var shortShow = HalftimeDesk.Build(r, 0.4, Around, null, Ordinary);
        Walls(full); Walls(shortShow);
        Assert.Contains(full, b => b.Kicker == "AROUND COLLEGE");
        Assert.DoesNotContain(shortShow, b => b.Kicker == "AROUND COLLEGE");
        Assert.Equal(full.Count - 1, shortShow.Count);
        // The first half's screen carries the matchup.
        Assert.Equal("No. 12", full[0].Rows.Single(x => x.Label == "Top 25").Home);
        // The adjustments: both benches side by side, the deciding stat among the rows.
        var adj = full.First(b => b.Kicker == "ADJUSTMENTS");
        Assert.Equal(new[] { "", "Score", "Offense", "Defense" }, adj.Rows.Take(4).Select(x => x.Label));
        Assert.Equal("up 5", adj.Rows[1].Away); Assert.Equal("down 5", adj.Rows[1].Home);
        Assert.Contains(adj.Rows, x => x.Label == HalftimeDesk.Decided(r).Label);
        // The pick: foul trouble and timeouts left.
        var pick = full.Single(b => b.Kicker == "SECOND HALF");
        Assert.Equal("1", pick.Rows.Single(x => x.Label == "Foul trouble (3+)").Away);
        Assert.Equal(r.Home.Timeouts.ToString(), pick.Rows.Single(x => x.Label == "Timeouts left").Home);
    }

    [Fact]
    public void A_show_without_a_session_or_a_timeline_still_fills_the_wall()
    {
        var r = Game();
        r.Flow.Clear();
        Walls(PostgameDesk.Build(r).Where(b => b.Kicker != "WHAT IT MEANS"));
        Walls(HalftimeDesk.Build(r, 0.5));
    }

    [Fact]
    public void A_college_career_live_game_gets_the_matchup_and_a_full_wall()
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026, 2026, GameSession.DefaultFullSim);
        var cw = world.College!;
        var id = cw.Programs.OrderByDescending(p => cw.Prestige[p.Id]).First().Id;
        var s = GameSession.NewGame(2026, id, null);
        int counted = 0;
        for (int g = 0; g < 3; g++)
        {
            while (!s.HumanPlaysToday) s.AdvanceDay();
            var before = s.MyRecord;
            var (live, played) = s.BeginLiveDay();
            Assert.NotNull(live);
            live!.Finish();
            var r = live.Result;
            var ctx = s.DeskContextFor(r, final: true);
            Assert.Equal(new[] { "Record", "Top 25", "Conference", "Last 5" }, ctx.Matchup.Take(4).Select(x => x.Label));
            // Tonight counts in the postgame's record before Continue records it.
            bool won = r.Winner.Team == s.HumanTeam;
            var row = ctx.Matchup[0];
            Assert.Equal($"{before.Wins + (won ? 1 : 0)}-{before.Losses + (won ? 0 : 1)}", s.HumanTeam == r.Home.Team ? row.Home : row.Away);
            // A ranked opponent or a rivalry makes it the full show (the first games are all regular season).
            var opp = r.Home.Team == s.HumanTeam ? r.Away.Team : r.Home.Team;
            bool ranked = cw.Rank(opp.Id) != null;
            bool rival = cw.RivalryOf(r.Home.Team.Id, r.Away.Team.Id) != null;
            Assert.Equal(ranked || rival, ctx.Full);
            var (rows, line) = s.PostgameTable(r);
            var beats = PostgameDesk.Build(r, rows, line, s.HalftimeAround(r.Home.Team, r.Away.Team), ctx);
            Walls(beats);
            Assert.Equal(ctx.Full, beats.Any(b => b.Kicker == "TURNING POINT"));
            counted++;
            s.FinishLiveDay(live, played);
        }
        Assert.Equal(3, counted);
    }
}
