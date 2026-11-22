using Hardwood.Core;
using Hardwood.Core.College;
using Hardwood.Core.Generation;
using Hardwood.Core.Jobs;
using Hardwood.Core.Model;
using Hardwood.Core.RealWorld;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 138: the twentieth hunt's college findings (`claude/playtest-2026-11-20-fourth.md`).
/// - H161: a blue blood's Your pros is full on day one, with real drafts and real All-Stars (Kentucky: 15+ NBA players, 5+ All-Stars);
/// - H137: the AD's words and number agree with where the season began; a losing season is never "Secure";
/// - H150: famous former NBA names (Tristan Thompson, Shawn Kemp) are never generated;
/// - H149: every Division I program has a scoreboard short name ("APP", "ARST"), never a cut-off word.
/// </summary>
public class Run138HuntFixesTests
{
    private readonly ITestOutputHelper _out;
    public Run138HuntFixesTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void Kentucky_starts_with_a_full_your_pros_and_real_all_stars()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hw-run138-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var (_, world) = RealWorldData.NewWorld(31, 2026, GameSession.DefaultFullSim);
            int id = world.College!.Programs.First(p => p.City == "Kentucky").Id;
            var s = GameSession.NewGame(31, id, Path.Combine(dir, "uk.hwsave"));
            var rec = s.ProRecordOf(s.HumanTeam);
            _out.WriteLine($"{rec.TopLeague} in the NBA, {rec.AllStars} All-Stars: {string.Join(", ", rec.Lines.Select(l => $"{l.Player.Name} ({l.Real?.DraftText ?? "?"}{(l.EverAllStar ? ", AS" : "")})"))}");
            Assert.True(rec.TopLeague >= 15, $"only {rec.TopLeague} Kentucky alumni in the NBA");
            Assert.True(rec.AllStars >= 5, $"only {rec.AllStars} All-Stars");
            var ad = rec.Lines.Single(l => l.Player.Name == "Anthony Davis");
            Assert.True(ad.EverAllStar);
            Assert.Equal("2012 · pick 1", ad.Real!.DraftText);
            Assert.Contains(rec.Lines, l => l.Player.Name == "Tyler Herro");
            // Every real row knows its draft: nobody reads "-".
            Assert.All(rec.Lines, l => Assert.NotNull(l.Real));
            // The pitch counts the real All-Stars too.
            Assert.True(s.World.College!.ProPitchText.Contains("All-Star"), s.World.College.ProPitchText);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void Every_listed_alumnus_has_a_draft_and_every_star_is_listed()
    {
        int facts = 0;
        foreach (var (school, names) in RealCollegeHistory.Alumni)
            foreach (var n in names)
            {
                var f = RealCollegeHistory.FactOf(n);
                Assert.True(f != null, $"{school}: {n} has no draft fact");
                Assert.True(f!.Undrafted || (f.Draft[0] is >= 2003 and <= 2025 && f.Draft[1] is >= 1 and <= 60), $"{n}: {f.DraftText}");
                facts++;
            }
        Assert.True(facts >= 200, $"{facts} facts");
        Assert.Equal("Undrafted", RealCollegeHistory.FactOf("Fred VanVleet")!.DraftText);
    }

    private static (JobState job, Team team, LeagueContext ctx) CollegeJob(double start, SeasonGoal goal, int target)
    {
        var league = new WorldGenerator(new Rng(9)).Generate("North American Basketball League", 2026, new DateOnly(2026, 10, 1));
        var world = GameWorld.Generate(league, 9, GameSession.DefaultFullSim, new DateOnly(2026, 10, 1));
        var program = world.College!.Programs[0];
        var ctx = LeagueContext.College(world.College, program);
        var job = new JobState
        {
            Confidence = start, HiredSeason = 2024, SeasonStartSeason = 2026, SeasonStartConfidence = start,
            Current = new Expectations { Season = 2026, Goal = goal, TargetWins = target },
        };
        return (job, program, ctx);
    }

    [Fact]
    public void A_delighted_ad_never_ends_the_season_lower()
    {
        // Duke, seed 77: a 1-seed won the title, "delighted", and confidence went 95 → 80 (the weekly drift had pulled it down).
        var (job, team, ctx) = CollegeJob(95, SeasonGoal.Title, 30);
        job.Confidence = 70; // where the weekly drift left it by April
        var (_, body, sacked) = Board.SeasonReview(job, new Coach(), ctx, team, SeasonGoal.Title, 32, 3, new Dictionary<int, PlayerSeasonStats>(),
            new DateOnly(2027, 4, 8), 2026, madnessRound: 7, seed: 1, overall: (38, 3));
        _out.WriteLine(body);
        Assert.False(sacked);
        Assert.Contains("delighted", body);
        Assert.True(job.Confidence >= 95, $"{job.Confidence}");
    }

    [Fact]
    public void A_disappointed_ad_never_ends_the_season_higher()
    {
        // Hunt 19: "disappointed" with confidence 44 → 57. A 1-seed out in the first weekend after a season on target.
        var (job, team, ctx) = CollegeJob(44, SeasonGoal.Finals, 24);
        job.Confidence = 60;
        var (_, body, _) = Board.SeasonReview(job, new Coach(), ctx, team, SeasonGoal.Playoffs, 27, 4, new Dictionary<int, PlayerSeasonStats>(),
            new DateOnly(2027, 4, 8), 2026, madnessRound: 2, seed: 1);
        _out.WriteLine(body);
        Assert.Matches("disappointed|expected more", body);
        Assert.True(job.Confidence <= 44, $"{job.Confidence}");
    }

    [Fact]
    public void A_losing_college_season_is_never_secure()
    {
        // App State, seed 77: 12-13 against a target of about 9 wins read "Secure (95/100)".
        var (job, team, ctx) = CollegeJob(90, SeasonGoal.Develop, 9);
        Board.SeasonReview(job, new Coach(), ctx, team, SeasonGoal.Develop, 12, 13, new Dictionary<int, PlayerSeasonStats>(), new DateOnly(2027, 4, 8), 2026);
        Assert.True(job.Confidence <= Board.LosingCap, $"{job.Confidence}");
        Assert.NotEqual("Secure", Board.Mood(job.Confidence, false).Label);
    }

    [Fact]
    public void Satisfied_short_of_the_target_says_so()
    {
        // Miami, seed 2026: missed the 20-win target and March at 18-11, and the AD "is satisfied" with nothing else.
        var (job, team, ctx) = CollegeJob(60, SeasonGoal.Develop, 20);
        var (_, body, _) = Board.SeasonReview(job, new Coach(), ctx, team, SeasonGoal.Develop, 18, 11, new Dictionary<int, PlayerSeasonStats>(), new DateOnly(2027, 4, 8), 2026);
        _out.WriteLine(body);
        Assert.DoesNotContain("is satisfied.\n", body + "\n");
        if (body.Contains("satisfied")) Assert.Contains("short of the 20-win target", body);
    }

    [Theory]
    [InlineData("Tristan Thompson")]
    [InlineData("Shawn Kemp")]
    [InlineData("Michael Jordan")]
    [InlineData("Hakeem Olajuwon")]
    [InlineData("Christian Laettner")]
    public void Famous_former_names_are_real(string name) => Assert.True(RealNames.IsReal(name), name);

    [Fact]
    public void Generated_players_never_carry_a_legend_name()
    {
        var legends = Hardwood.Core.RealWorld.RealNames.Count;
        Assert.True(legends > 1300, $"{legends} real names");
        foreach (ulong seed in new ulong[] { 31, 77, 2026 })
        {
            var gen = new PlayerGenerator(new Rng(seed), 1) { Names = new NameBook() };
            var us = Heritages.All.First(h => h.Key == "us");
            for (int i = 0; i < 4000; i++)
            {
                var p = gen.Generate((Position)(i % 5), 60 + i % 100, 18 + i % 5, new DateOnly(2026, 10, 1), us, "USA");
                Assert.False(RealNames.IsReal(p.FirstName, p.LastName), $"seed {seed}: {p.Name}");
            }
        }
    }

    [Fact]
    public void Every_program_has_a_scoreboard_short_name()
    {
        var abbr = RealColleges.Abbreviations();
        Assert.Equal(RealColleges.Programs.Count, abbr.Values.Distinct().Count());
        Assert.All(RealColleges.Programs, p => Assert.True(RealColleges.Known.ContainsKey(p.School), $"{p.School} falls back to {abbr[p.School]}"));
        Assert.All(abbr.Values, a => Assert.InRange(a.Length, 2, 5));
        Assert.Equal("APP", abbr["Appalachian State"]);
        Assert.Equal("ARST", abbr["Arkansas State"]);
    }
    [Theory]
    [InlineData(31UL)]
    [InlineData(2026UL)]
    [InlineData(77UL)]
    public void Big_conferences_play_a_full_conference_season(ulong seed)
    {
        // H162: Providence (Big East, 11 programs) played 21-23 games with 10 in the conference; the SEC played 15.
        var (_, world) = RealWorldData.NewWorld(seed, 2026, GameSession.DefaultFullSim);
        var c = world.College!;
        c.StartSeason(2026, new Rng(seed));
        var games = c.Season.Games;
        foreach (var conf in c.Active)
        {
            var members = c.InConference(conf.Key).Select(t => t.Id).ToList();
            foreach (var id in members)
            {
                int n = games.Count(g => g.Stage == "Conference" && (g.HomeId == id || g.AwayId == id));
                if (members.Count == 11) Assert.Equal(20, n);
                else if (members.Count > 11) Assert.InRange(n, 18, 19);
                else Assert.Equal(2 * (members.Count - 1), n);
                int all = games.Count(g => g.Stage is "Conference" or "Non-conference" && (g.HomeId == id || g.AwayId == id));
                Assert.InRange(all, 28, 31); // H159 (run 156): about 30 for everyone, less two for an invited program (its bracket), one more for a fixed rival
            }
        }
        // One game a day at most, and the conference season ends before the conference tournaments (March 10).
        var byDay = games.SelectMany(g => new[] { (g.Date, g.HomeId), (g.Date, g.AwayId) }).GroupBy(x => x).Where(g => g.Count() > 1).ToList();
        Assert.Empty(byDay);
        Assert.All(games.Where(g => g.Stage == "Conference"), g => Assert.True(g.Date < new DateOnly(2027, 3, 10) && g.Date > new DateOnly(2026, 12, 31), g.Date.ToString()));
        var prov = c.Programs.First(t => t.City == "Providence");
        var bigEast = c.InConference(c.ConferenceOf[prov.Id]).Select(t => t.Id).ToList();
        Assert.Equal(11, bigEast.Count);
        _out.WriteLine($"seed {seed}: Big East conference games {string.Join(",", bigEast.Select(id => games.Count(g => g.Stage == "Conference" && (g.HomeId == id || g.AwayId == id))))}");
    }
    [Fact]
    public void The_words_never_leave_a_kept_coach_below_the_sack_line()
    {
        // The review of PR #204: a coach who began at 15, drifted up and survived the review was dropped back to 15 by
        // "expected more", below the line he had just cleared.
        var (job, team, ctx) = CollegeJob(15, SeasonGoal.Playoffs, 20);
        job.Confidence = 45;
        var (_, body, sacked) = Board.SeasonReview(job, new Coach(), ctx, team, SeasonGoal.Develop, 12, 17, new Dictionary<int, PlayerSeasonStats>(), new DateOnly(2027, 4, 8), 2026);
        _out.WriteLine($"{body}\n→ {job.Confidence}");
        if (!sacked) Assert.True(job.Confidence >= Math.Min(45, 25 - (team.Owner.Patience - 10)) - 0.01, $"{job.Confidence}");
    }

    [Theory]
    [InlineData(2026)]
    [InlineData(2027)]
    [InlineData(2030)]
    public void Conference_dates_fit_before_the_tournaments(int year)
    {
        foreach (int rounds in new[] { 19, 20, 22 })
        {
            var d = CollegeWorld.ConferenceDates(year, rounds);
            Assert.Equal(rounds, d.Count);
            Assert.Equal(rounds, d.Distinct().Count());
            Assert.True(d[0] >= new DateOnly(year + 1, 1, 2) && d[^1] <= new DateOnly(year + 1, 3, 9));
        }
        Assert.Equal(22, CollegeWorld.BigConferenceRounds(11));
    }
}
