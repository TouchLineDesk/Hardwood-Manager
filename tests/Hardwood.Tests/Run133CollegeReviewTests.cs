using Hardwood.Core.College;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 133: critic review #18 (the college deep-dive), its small tier, and the hunter's college items.
/// - K14: a college career keeps the pros out (college staff titles, NBA records only in the digest).
/// - K13: unsigned graduates go to the lower leagues and abroad before clubs make up strangers; those who don't read
///   "Out of pro basketball", not "Retired".
/// - Item 3: shooting leaders need the NCAA's volume (2.5 threes made a game).
/// - H149: the best-known programs wear the short names fans use.
/// - H148: the poll weighs the record (no unbeaten power team behind a three-loss one).
/// - H130: no at-large team at .500 or worse.
/// </summary>
public class Run133CollegeReviewTests
{
    private readonly ITestOutputHelper _out;
    public Run133CollegeReviewTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void Known_programs_wear_their_short_names()
    {
        var ab = Hardwood.Core.RealWorld.RealColleges.Abbreviations();
        Assert.Equal("UK", ab["Kentucky"]);
        Assert.Equal("UNC", ab["North Carolina"]);
        Assert.Equal("MIA", ab["Miami (FL)"]);
        Assert.Equal("ND", ab["Notre Dame"]);
        Assert.Equal(ab.Count, ab.Values.Distinct().Count());
    }

    [Fact]
    public void Shooting_leaders_need_volume()
    {
        var p = new Player { Id = 1, FirstName = "A", LastName = "B" };
        // 10 games, 26 threes taken, 17 made (65%): 1.7 made a game is under the NCAA's 2.5.
        var few = new StandingsLeadersLine(p, 10, 26, 17.0 / 26);
        Assert.Equal(0, StandingsViewModelKey("3P%", college: true)(few.Line));
        Assert.True(StandingsViewModelKey("3P%", college: false)(few.Line) > 0); // the pros' minimum is one made a game
        var many = new StandingsLeadersLine(p, 10, 70, 30.0 / 70);
        Assert.True(StandingsViewModelKey("3P%", college: true)(many.Line) > 0);
    }

    private static Func<Hardwood.Desktop.ViewModels.LeadersViewModel.Line, double> StandingsViewModelKey(string cat, bool college) =>
        Hardwood.Desktop.ViewModels.LeadersViewModel.LineKey(cat, college);

    private sealed record StandingsLeadersLine(Player P, int Games, int Tpa, double TpPct)
    {
        public Hardwood.Desktop.ViewModels.LeadersViewModel.Line Line =>
            new(P, 0, Games, 30, 15, 5, 3, 1, 0.5, 0.45, TpPct, Games * 12, Tpa);
    }

    [Fact]
    public void A_college_season_reads_like_college()
    {
        var (_, world0) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026, 2026, GameSession.DefaultFullSim);
        int id = world0.College!.Programs.First(p => p.FullName.StartsWith("Miami (FL)")).Id;
        var s = GameSession.NewGame(2026, id, null);
        var c = s.World.College!;
        // K14: college staff titles.
        Assert.True(s.CollegeWords);
        Assert.Equal("Athletic trainer", Staff.RoleName(StaffRole.Physio, s.CollegeWords));
        Assert.Equal("Recruiting coordinator", Staff.RoleName(StaffRole.Scout, s.CollegeWords));
        Assert.Equal("Physio", Staff.RoleName(StaffRole.Physio));

        // H148: by Christmas, no unbeaten team sits behind a team with three or more losses.
        int guard = 0;
        while (s.Today < new DateOnly(2026, 12, 24) && guard++ < 200) s.AdvanceDay();
        var poll = c.Poll();
        _out.WriteLine(string.Join(", ", poll.Take(25).Select(e => $"{e.Rank} {e.Team.City} {e.Wins}-{e.Losses}")));
        // K35 (run 193): the poll on show is Monday's, so a team's record can have moved since; the voters' rule holds in
        // the running order they vote from (it was taken from that order on Monday).
        var live = c.LivePollOrder().Select((tid, i) => (Rank: i + 1, Rec: c.Record(tid))).ToList();
        foreach (var a in live.Where(e => e.Rec.losses == 0 && e.Rec.wins >= 8))
            Assert.DoesNotContain(live, b => b.Rank < a.Rank && b.Rec.losses >= 3);
        Assert.InRange(c.PollPosition(id), 1, c.Programs.Count);

        // H130: the field's at-large teams all have winning records.
        while (!c.Season.Selected && guard++ < 400) s.AdvanceDay();
        Assert.True(c.Season.Selected);
        var auto = c.Season.ConferenceChampions.Values.ToHashSet();
        foreach (var t in c.Season.Seeds.Keys.Where(t => !auto.Contains(t)))
        {
            var (w, l) = c.Record(t);
            Assert.True(w > l, $"{c.Program(t)!.FullName} {w}-{l} got an at-large bid");
        }

        // K13: through the summer. Graduates mostly find clubs; those who don't read "Out of pro basketball".
        while (s.Phase != SessionPhase.Offseason && guard++ < 600) s.AdvanceDay();
        int summer = s.Offseason!.Season + 1;
        while (s.Phase == SessionPhase.Offseason && guard++ < 1400) s.AdvanceOffseason();
        var grads = c.Alumni.Where(kv => kv.Value.Year == summer).Select(kv => kv.Key).ToHashSet();
        var retired = s.League.Retired.Count(p => grads.Contains(p.Id));
        var signed = s.League.AllActivePlayers.Concat(s.World.Players).Count(p => grads.Contains(p.Id));
        var good = s.League.Retired.Count(p => grads.Contains(p.Id) && p.CurrentAbility >= 75);
        _out.WriteLine($"graduates {grads.Count}: signed {signed}, left the game {retired} (CA 75+: {good})");
        // No college starter-level graduate (CA 72+) leaves the game in his first summer: he signs or waits on the market.
        Assert.Equal(0, s.League.Retired.Count(p => grads.Contains(p.Id) && p.CurrentAbility >= Offseason.GraduateFloor));
        var waiting = s.League.FreeAgents.Count(p => grads.Contains(p.Id));
        _out.WriteLine($"waiting on the market: {waiting}");
        var rec = s.ProRecordOf(s.HumanTeam);
        Assert.DoesNotContain(rec.Lines, l => l.Club == "Retired" && l.Player.Age(s.Today) < 28);
        _out.WriteLine(rec.Summary("NBA") + " | " + string.Join("; ", rec.Lines.Select(l => $"{l.Player.Name} {l.Player.CurrentAbility} {l.Club} {l.League}")));

        // K14: no NBA record reached the college coach's own inbox (the world digest only).
        Assert.DoesNotContain(s.Inbox, m => m.Title.StartsWith("NBA record") && m.Category == "League");
    }

    [Fact]
    public void The_AD_reads_March_against_the_seed()
    {
        // H137: Duke (goal: the title, target 27). An Elite Eight as a 1-seed at 25-5 must read better than a Round-of-32 exit at 29-2.
        var league = new Hardwood.Core.Generation.WorldGenerator(new Hardwood.Core.Rng(9)).Generate("North American Basketball League", 2026, new DateOnly(2026, 10, 1));
        var world = Hardwood.Core.World.GameWorld.Generate(league, 9, GameSession.DefaultFullSim, new DateOnly(2026, 10, 1));
        var cw = world.College!;
        var program = cw.Programs[0];
        var ctx = Hardwood.Core.Jobs.LeagueContext.College(cw, program);
        Hardwood.Core.Jobs.JobState Job() => new() { Confidence = 70, HiredSeason = 2024, Current = new Hardwood.Core.Jobs.Expectations { Season = 2026, Goal = Hardwood.Core.Jobs.SeasonGoal.Title, TargetWins = 27 } };
        var a = Job(); var b = Job();
        var (_, eliteEight, _) = Hardwood.Core.Jobs.Board.SeasonReview(a, new Hardwood.Core.Jobs.Coach(), ctx, program, Hardwood.Core.Jobs.SeasonGoal.ConferenceFinals, 25, 5,
            new Dictionary<int, PlayerSeasonStats>(), new DateOnly(2027, 4, 7), 2026, madnessRound: 4, seed: 1);
        var (_, roundOf32, _) = Hardwood.Core.Jobs.Board.SeasonReview(b, new Hardwood.Core.Jobs.Coach(), ctx, program, Hardwood.Core.Jobs.SeasonGoal.Playoffs, 29, 2,
            new Dictionary<int, PlayerSeasonStats>(), new DateOnly(2027, 4, 7), 2026, madnessRound: 2, seed: 1);
        _out.WriteLine($"{a.Confidence:0}: {eliteEight}\n{b.Confidence:0}: {roundOf32}");
        Assert.True(a.Confidence > b.Confidence, $"Elite Eight {a.Confidence:0} vs Round of 32 {b.Confidence:0}");
        Assert.Contains("about what the seed promised", eliteEight);
        Assert.Contains("out in the Round of 32 is well short", roundOf32);
    }
}
