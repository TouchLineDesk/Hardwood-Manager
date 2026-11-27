using Hardwood.Core.Jobs;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Hardwood.Desktop.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>Run 228, the first of Danny's three bug-only runs (`danny-bug-sweep-nov27`): H225, H143, H118, H121, H226, H228,
/// K47's relegation wording and K49's doubled quotes.</summary>
public class Run228BugSweepTests
{
    private readonly ITestOutputHelper _out;
    public Run228BugSweepTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void K49_an_answer_is_quoted_once()
    {
        Assert.Equal("\"One bad night\"", GameSession.Quoted("\"One bad night\""));
        Assert.Equal("\"Back the players\"", GameSession.Quoted("Back the players"));
    }

    [Fact]
    public void H118_the_draft_button_says_why_it_is_greyed()
    {
        Assert.Equal("Draft Cooper Flagg", OffseasonViewModel.DraftButtonText("Cooper Flagg", true, false, new[] { 3 }));
        Assert.Equal("Pick a graduate", OffseasonViewModel.DraftButtonText(null, true, false, new[] { 3 }));
        Assert.Equal("Your pick: #12", OffseasonViewModel.DraftButtonText("Cooper Flagg", false, false, new[] { 12, 40 }));
        Assert.Equal("No pick this year", OffseasonViewModel.DraftButtonText("Cooper Flagg", false, false, Array.Empty<int>()));
        Assert.Equal("Programs don't pick", OffseasonViewModel.DraftButtonText("Cooper Flagg", false, true, Array.Empty<int>()));
    }

    [Fact]
    public void H143_the_rotation_total_counts_only_fit_players()
    {
        Assert.Equal("240 of 240 minutes", RotationViewModel.TotalLine(240, 0));
        var line = RotationViewModel.TotalLine(176, 64);
        Assert.StartsWith("176 of 240 minutes (under", line);
        Assert.Contains("64 more set for injured players, who won't play", line);
    }

    [Fact]
    public void H121_the_period_line_is_labelled()
    {
        Assert.Equal("By quarter:  SAS 28 31   |   SEA 25 30", LiveGameViewModel.PeriodLineFor("SAS", new[] { 28, 31 }, "SEA", new[] { 25, 30 }, 4));
        Assert.StartsWith("By half:", LiveGameViewModel.PeriodLineFor("DUKE", new[] { 40 }, "UNC", new[] { 38 }, 2));
    }

    [Fact]
    public void H226_a_pyramid_champion_who_stays_down_is_told_why()
    {
        var clubs = Enumerable.Range(1, 32).Select(i => new Team { Id = i, City = "C" + i, Name = "N" + i, Abbreviation = "A" + i, Conference = Conference.East, Division = "D" + (i % 4) }).ToList();
        var ctx = new LeagueContext("National League Two", clubs, 62, 8, false, null, 0, PyramidLevel: 4);
        Assert.True(ctx.DivisionFormat);
        Assert.Equal("won the championship, but only division winners go up", Board.DescribeResult(SeasonGoal.Title, ctx, wonDivision: false));
        Assert.StartsWith("won your division and go up; won the championship", Board.DescribeResult(SeasonGoal.Title, ctx, wonDivision: true, goingUp: true));
        Assert.DoesNotContain("only division", Board.DescribeResult(SeasonGoal.Title, ctx with { PyramidLevel = 0, IsNabl = true }, wonDivision: false));
    }

    /// <summary>H225: a coach who leaves one club mid-season and joins another gets a line for each, with his own record.</summary>
    [Fact]
    public void H225_your_season_has_a_line_per_club_with_your_own_record()
    {
        var (probe, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(55, 2026);
        var ordered = probe.Teams.OrderBy(t => QuickSim.Strength(t)).ToList();
        var s = GameSession.NewGame(55, ordered[14].Id, savePath: null);
        s.Job.Current!.TargetWins = 0;
        var first = s.HumanTeam;
        int guard = 0;
        while (s.MyRecord.Wins + s.MyRecord.Losses < 12 && guard++ < 200) s.AdvanceDay();
        var atLeaving = (s.MyRecord.Wins, s.MyRecord.Losses);
        Assert.Null(s.Resign());
        for (int i = 0; i < 12; i++) s.AdvanceDay();
        var next = s.League.Teams.First(t => t != first && t.Id == ordered[3].Id);
        s.Job.Vacancies.Add(new Vacancy { TeamId = next.Id, Opened = s.Today, Status = "offered" });
        Assert.Null(s.AcceptOffer(next.Id));
        Assert.Equal(next, s.HumanTeam);
        var atHire = (s.MyRecord.Wins, s.MyRecord.Losses);
        Assert.Equal(atHire.Item1, s.Job.HiredWins);
        Assert.Equal(atHire.Item2, s.Job.HiredLosses);
        Assert.Equal(atLeaving, (s.Coach.History.Last().Wins, s.Coach.History.Last().Losses));

        guard = 0;
        while (s.Phase != SessionPhase.Offseason && guard++ < 600) s.AdvanceDay();
        var e = Assert.Single(Almanac.Editions(s));
        var lines = e.Section(Almanac.Yours)!.Lines;
        foreach (var l in lines) _out.WriteLine(l);
        Assert.Equal(2, lines.Count);
        var h = s.League.History[0];
        var full1 = h.Records.First(r => r.teamId == first.Id);
        Assert.Equal($"{first.TheFullNameCap} were {atLeaving.Wins}-{atLeaving.Losses} when you resigned; they finished {full1.wins}-{full1.losses}.", lines[0]);
        var full2 = h.Records.First(r => r.teamId == next.Id);
        Assert.Equal($"{next.TheFullNameCap} went {full2.wins - atHire.Item1}-{full2.losses - atHire.Item2} after you arrived on {s.Job.HiredOn:MMM d} ({full2.wins}-{full2.losses} in all).", lines[1]);
    }
}
