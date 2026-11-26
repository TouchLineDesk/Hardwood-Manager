using Hardwood.Core.International;
using Hardwood.Core.Season;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>International plan I6b (run 215, #108, #109): you coach your nation's window qualifiers live on days your club is idle
/// (your assistant takes them in the full engine on club days); the window keeps those scores; national results move your
/// reputation; a tournament far below the seeding ends the job.</summary>
public class Run215NationalGamesTests
{
    private readonly ITestOutputHelper _out;
    public Run215NationalGamesTests(ITestOutputHelper output) => _out = output;

    private static GameSession NewCareer(string program = "Virginia Tech")
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(31, 2026, GameSession.DefaultFullSim);
        var me = world.College!.Programs.First(t => t.FullName.StartsWith(program));
        return GameSession.NewGame(31, me.Id, null);
    }

    [Fact]
    public void You_coach_the_November_window_and_the_window_keeps_your_scores()
    {
        var s = NewCareer();
        while (s.Today < new DateOnly(2026, 10, 16)) s.AdvanceDay();
        var job = s.NationalJobState;
        Assert.NotEmpty(job.Offers);
        Assert.True(s.AcceptNationalOffer(job.Offers[0].Code));
        string code = job.Code!;
        double repBefore = s.Coach.Reputation;
        var nov = FibaCalendar.InYear(2026).First(e => e.Kind == FibaEventKind.Window && e.Start.Month == 11);
        int live = 0, assistant = 0;
        var coached = new List<CoachedGame>();
        while (!s.World.International.Windows.Any(w => w.Key == "2026-11"))
        {
            Assert.True(s.Today <= nov.End, "the window should be played by its last day");
            if (s.MyNationalGameToday is { } f)
            {
                Assert.True(f.Home == code || f.Away == code);
                Assert.True(s.HumanPlaysToday);
                if (!s.ClubPlaysToday)
                {
                    var (game, played) = s.BeginLiveDay();
                    Assert.NotNull(game);
                    Assert.Equal(f, s.LiveNational);
                    Assert.Equal(f.Home, game!.Result.Home.Team.Abbreviation);
                    Assert.Equal(f.Away, game.Result.Away.Team.Abbreviation);
                    Assert.Equal(10, game.MinutesPerPeriod); // FIBA rules
                    Assert.All(game.Result.Home.Team.Roster.Concat(game.Result.Away.Team.Roster), p => Assert.NotNull(p));
                    s.FinishLiveDay(game, played);
                    Assert.Null(s.LiveNational);
                    Assert.DoesNotContain(played, r => r.Home.Team.Abbreviation == f.Home && r.Away.Team.Abbreviation == f.Away);
                    live++;
                }
                else
                {
                    s.AdvanceDay();
                    assistant++;
                }
                coached.Add(job.Games.Last());
                continue;
            }
            s.AdvanceDay();
        }
        _out.WriteLine($"{job.Name}: {live} live, {assistant} by the assistant; rep {repBefore:0.0} -> {s.Coach.Reputation:0.0}");
        Assert.True(live + assistant >= 1, "your nation plays in its first window");
        var rec = s.World.International.Windows.Single(w => w.Key == "2026-11");
        foreach (var c in coached)
        {
            var g = rec.Games.Single(x => x.Home == c.Home && x.Away == c.Away);
            Assert.Equal((c.HomePts, c.AwayPts), (g.HomePts, g.AwayPts));
            _out.WriteLine($"  {c.Day:MMM d} {c.Home} {c.HomePts}-{c.AwayPts} {c.Away} ({(c.Live ? "live" : "assistant")})");
        }
        Assert.Equal(coached.Count, rec.Games.Count(g => g.Home == code || g.Away == code));
        Assert.Empty(job.Games); // folded into the window's record
        Assert.Contains(s.Inbox, m => m.Title.StartsWith($"{job.Name}: ") && m.Title.Contains("qualifiers' window"));
        Assert.Equal(Math.Round(s.Coach.Reputation - repBefore, 1), Math.Round(job.ReputationEarned, 1), 1);
    }

    [Fact]
    public void A_coached_score_replaces_only_its_own_pairing()
    {
        var s = NewCareer();
        var nov = FibaCalendar.InYear(2026).First(e => e.Kind == FibaEventKind.Window && e.Start.Month == 11);
        var teams = NationalTeams.Build(WorldCup.Entries(s, nov.Start), nov.Start);
        var plain = Qualifying.Play(new InternationalHistory(), nov, teams, s.Seed);
        var h = new InternationalHistory();
        var cycle = Qualifying.CycleFor(h, nov, FibaRanking.Apply(teams, FibaRanking.Current(h, teams)), keep: false);
        var f = Qualifying.FixturesOf(cycle, nov)[0];
        Assert.Equal(plain.Games.Count, Qualifying.FixturesOf(cycle, nov).Count);
        var mine = new CoachedGame(f.Key, f.Group, f.Home, f.Away, 61, 99, f.Day, true);
        var with = Qualifying.Play(h, nov, NationalTeams.Build(WorldCup.Entries(s, nov.Start), nov.Start), s.Seed, new[] { mine });
        Assert.Equal(plain.Games.Count, with.Games.Count);
        for (int i = 0; i < plain.Games.Count; i++)
        {
            var (a, b) = (plain.Games[i], with.Games[i]);
            Assert.Equal((a.Home, a.Away, a.Day), (b.Home, b.Away, b.Day));
            if (a.Home == f.Home && a.Away == f.Away) Assert.Equal((61, 99), (b.HomePts, b.AwayPts));
            else Assert.Equal((a.HomePts, a.AwayPts), (b.HomePts, b.AwayPts));
        }
    }

    private static WorldCupRecord Field(int n)
    {
        var rec = new WorldCupRecord { Year = 2028, Kind = "Olympics" };
        for (int i = 1; i <= n; i++) rec.Teams.Add(new CupTeam($"T{i:00}", $"Team {i}", "Europe", 100 - i, i, new List<int>()));
        return rec;
    }

    [Fact]
    public void The_federation_judges_a_tournament_against_the_seeding()
    {
        // A medal from the 6th seed: delighted, a big lift.
        var gold = Field(12);
        gold.Games.Add(new CupGame(WorldCup.QuarterRound, "T06", "T03", 80, 75, "Aug 4"));
        gold.Games.Add(new CupGame(WorldCup.FinalRound, "T06", "T01", 80, 78, "Aug 10"));
        gold.Gold = "T06"; gold.Silver = "T01"; gold.Bronze = "T02"; gold.Fourth = "T03";
        var (rep, sacked, verdict) = NationalGames.Review(gold, "T06", major: true);
        Assert.False(sacked);
        Assert.Equal("delighted", verdict);
        Assert.True(rep >= 7, $"gold from 6th: {rep}");

        // The top seed out in the group: sacked.
        var flop = Field(12);
        flop.Games.Add(new CupGame("Group A", "T01", "T09", 70, 80, "Aug 1"));
        flop.Games.Add(new CupGame(WorldCup.QuarterRound, "T02", "T09", 80, 70, "Aug 4"));
        flop.Gold = "T02"; flop.Silver = "T03"; flop.Bronze = "T04"; flop.Fourth = "T05";
        (rep, sacked, verdict) = NationalGames.Review(flop, "T01", major: true);
        Assert.True(sacked);
        Assert.True(rep < 0);

        // A low seed out in the group as expected: no harm done.
        (rep, sacked, verdict) = NationalGames.Review(flop, "T12", major: true);
        Assert.False(sacked);
        Assert.Equal("satisfied", verdict);
        Assert.InRange(rep, -1, 1);
    }

    [Fact]
    public void A_window_beats_or_misses_the_rankings_expectation()
    {
        var pts = new Dictionary<string, double> { ["AAA"] = 1500, ["BBB"] = 1900 };
        var upset = new[] { new CoachedGame("2026-11", "Europe A", "AAA", "BBB", 80, 70, new DateOnly(2026, 11, 13), true) };
        Assert.True(NationalGames.WindowReputation(upset, "AAA", pts) > 1);
        var expected = new[] { new CoachedGame("2026-11", "Europe A", "BBB", "AAA", 80, 70, new DateOnly(2026, 11, 13), true) };
        Assert.InRange(NationalGames.WindowReputation(expected, "BBB", pts), 0, 0.5);
        Assert.True(NationalGames.WindowReputation(expected, "AAA", pts) < 0);
    }
}
