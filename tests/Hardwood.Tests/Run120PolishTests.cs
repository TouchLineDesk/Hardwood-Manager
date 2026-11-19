using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 120 (a polish run): the studio show's contract-year line read the end season as StartSeason + Years - 1, which is
/// wrong once a deal has been through a summer (Years counts down); the press asked about the same streak every fortnight
/// with one question (critic #16: losing-streak ×9, winning-streak ×9); the Offseason page said one sentence all season.
/// </summary>
public class Run120PolishTests
{
    private readonly ITestOutputHelper _out;
    public Run120PolishTests(ITestOutputHelper o) => _out = o;

    private static IEnumerable<string> AllLines(StudioShow show) =>
        show.Segments.SelectMany(seg => seg.Lines).Select(l => l.Text);

    [Fact]
    public void The_studio_show_names_a_player_in_the_final_year_of_a_long_deal_and_not_one_with_years_left()
    {
        var s = GameSession.NewGame(31, 1, null);
        var us = s.HumanTeam;
        var them = s.League.Teams.First(t => t != us);
        int season = s.League.Season;
        var date = s.Today;
        // Every core player is signed long; one is in the last season of a four-year deal (signed three summers ago, counted
        // down to 1), and one is in year two of a three-year deal (the old formula took him for the walk year).
        var core = us.Roster.Where(p => p.IsAvailable(date) && p.Age(date) <= 34).OrderByDescending(p => p.CurrentAbility).Take(6).ToList();
        foreach (var p in us.Roster.Concat(them.Roster).Where(p => p.Contract != null)) { p.Contract!.Years = 4; p.Contract.StartSeason = season; }
        var walk = core[1];
        walk.Contract!.Years = 1; walk.Contract.StartSeason = season - 3;
        var decoy = core[0];
        decoy.Contract!.Years = 3; decoy.Contract.StartSeason = season - 2;   // old formula: season - 2 + 3 - 1 == season

        int named = 0;
        for (int d = 0; d < 40; d++)
        {
            var show = s.BuildStudioShow(us, them, date.AddDays(d));
            var lines = AllLines(show).Where(t => t.Contains("last year of his deal")).ToList();
            Assert.DoesNotContain(lines, t => t.StartsWith(decoy.Name));
            named += lines.Count(t => t.StartsWith(walk.Name));
        }
        _out.WriteLine($"{walk.Name} named in the walk-year line on {named} of 40 shows");
        Assert.True(named > 0);
    }

    [Theory]
    [InlineData(-4, 8, 15, "defeats in a row")]
    [InlineData(-5, 2, 15, "near the top")]
    [InlineData(-9, 8, 15, "9 straight defeats")]
    [InlineData(4, 8, 15, "")]
    [InlineData(5, 14, 15, "nobody tipped")]
    [InlineData(10, 1, 15, "10 wins in a row")]
    public void Streak_questions_vary_with_the_run_and_the_table(int streak, int rank, int teams, string expect)
    {
        var texts = Enumerable.Range(0, 3).Select(v => GameSession.StreakQuestion(streak, rank, teams, v)).ToList();
        Assert.All(texts, t => Assert.Contains(Math.Abs(streak).ToString(), t));
        if (expect != "") Assert.All(texts.Take(1), t => Assert.Contains(expect, t));
        // A mid-table run has three wordings; a team's place or a long run picks its own question.
        if (rank == 8 && Math.Abs(streak) < 8) Assert.Equal(3, texts.Distinct().Count());
    }

    [Fact]
    public void The_press_asks_about_a_run_once_and_waits_five_weeks_for_the_next()
    {
        // Toronto, seed 2026: on v0.110.4 five winning-streak press conferences in season one, the same question each time.
        var s = GameSession.NewGame(2026, 5, null);
        var ids = new HashSet<int>();
        var asked = new List<(DateOnly Day, DateOnly Start, string Topic, string Text)>();
        int seen = 0;
        while (s.Phase is SessionPhase.Preseason or SessionPhase.RegularSeason && seen++ < 400)
        {
            s.AdvanceDay();
            foreach (var d in s.Media.Decisions.Where(d => ids.Add(d.Id) && d.TeamId == s.HumanTeam.Id && d.Topic is "winning-streak" or "losing-streak"))
                asked.Add((s.Today, s.MyStreakStart()!.Value, d.Topic, d.Text));
            foreach (var d in s.OpenDecisions.ToList()) s.Answer(d.Id, d.SafeOption);
        }
        foreach (var a in asked) _out.WriteLine($"{a.Day} (run from {a.Start}) {a.Topic}: {a.Text}");
        Assert.NotEmpty(asked);
        Assert.Equal(asked.Count, asked.Select(a => a.Start).Distinct().Count());     // one question per run
        Assert.Equal(asked.Count, asked.Select(a => a.Text).Distinct().Count());      // never the same words twice
        foreach (var g in asked.GroupBy(a => a.Topic))
            foreach (var (a, b) in g.Zip(g.Skip(1)))
                Assert.True(b.Day.DayNumber - a.Day.DayNumber >= 35, $"{g.Key}: {a.Day} then {b.Day}");
    }

    [Fact]
    public void In_season_the_Offseason_page_previews_the_summer()
    {
        var s = GameSession.NewGame(31, 1, null);
        var us = s.HumanTeam;
        var preview = s.SummerPreview();
        _out.WriteLine(preview);
        var ending = us.Roster.Where(p => s.EndsThisSummer(p, us)).ToList();
        if (ending.Count == 0) Assert.StartsWith("No deals end this summer", preview);
        else
        {
            Assert.StartsWith($"Deals ending this summer ({ending.Count}):", preview);
            Assert.Contains(ending.OrderByDescending(p => p.CurrentAbility).First().Name, preview);
            Assert.Contains($"The other {us.Roster.Count(p => p.Contract != null) - ending.Count}", preview);
        }
        // Nobody's deals: the page says so plainly.
        foreach (var p in us.Roster.Where(p => p.Contract != null)) p.Contract!.Years = 3;
        Assert.StartsWith($"No deals end this summer: all {us.Roster.Count(p => p.Contract != null)} players with deals", s.SummerPreview());
    }
}
