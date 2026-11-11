using Avalonia.Headless.XUnit;
using Hardwood.Core;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Jobs;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Roadmap 4: the assistant explains "why" — tactic fit reasons, what moves board confidence, page tips once.</summary>
[Collection(SharedUiState.Name)]
public class AssistantWhyTests
{
    private static League NewLeague(ulong seed = 41) => new WorldGenerator(new Rng(seed)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));

    [Fact]
    public void Fit_reasons_add_up_to_the_fit_and_name_real_players()
    {
        var league = NewLeague();
        int checkedOptions = 0;
        foreach (var team in league.Teams.Take(12))
        {
            var rot = CoachAi.Rotation(team, new DateOnly(2026, 11, 1));
            foreach (var s in Enum.GetValues<OffensiveSystem>().Where(x => x != OffensiveSystem.Balanced))
            {
                var reasons = Systems.Reasons(s, rot);
                double fit = Systems.Fit(s, rot);
                Assert.NotEmpty(reasons);
                // The reasons' effects are the fit, before it's clamped to 0-1.
                Assert.Equal(fit, Math.Clamp(0.5 + reasons.Sum(r => r.Effect), 0, 1), 9);
                Assert.Equal(Systems.Raw(s, rot), Systems.Parts(s, rot).Sum(p => p.Value * p.Weight), 9);
                foreach (var p in Systems.Parts(s, rot).Where(p => p.Best != null)) Assert.Contains(p.Best!, rot);
                // A good fit has a strength first; a poor one leads with a weakness.
                if (fit >= 0.7) Assert.True(reasons[0].Effect > 0, $"{team.Abbreviation} {s}");
                if (fit <= 0.3) Assert.True(reasons[0].Effect < 0, $"{team.Abbreviation} {s}");
                checkedOptions++;
            }
            foreach (var d in Enum.GetValues<DefensiveScheme>().Where(x => x != DefensiveScheme.Man))
            {
                var reasons = Systems.Reasons(d, rot);
                Assert.Equal(Systems.Fit(d, rot), Math.Clamp(0.5 + reasons.Sum(r => r.Effect), 0, 1), 9);
                checkedOptions++;
            }
        }
        Assert.True(checkedOptions >= 12 * 8);
        Assert.Empty(Systems.Reasons(OffensiveSystem.Balanced, CoachAi.Rotation(league.Teams[0], new DateOnly(2026, 11, 1))));
    }

    [Fact]
    public void Board_outlook_is_exactly_what_the_weekly_review_applies()
    {
        var league = NewLeague(3);
        Board.EnsureOwnersAndCoaches(league, 3);
        var stats = new Dictionary<int, PlayerSeasonStats>();
        foreach (var team in league.Teams.Take(8))
            foreach (var (w, l) in new[] { (3, 1), (10, 10), (4, 16), (18, 2) })
            {
                var job = new JobState { Current = Board.SetExpectations(league, team, 2026), Confidence = 55, HiredOn = new DateOnly(2026, 1, 1) };
                var ctx = LeagueContext.Nabl(league);
                var today = new DateOnly(2026, 12, 15);
                var outlook = Board.Outlook(job, ctx, team, w, l, stats, today);
                if (w + l < 5) { Assert.Null(outlook); continue; }
                Assert.NotNull(outlook);
                Assert.Equal(Math.Clamp(Board.BaseLevel + outlook!.Drivers.Sum(d => d.Points), 0, 100), outlook.Level, 9);
                double expected = Math.Clamp(55 + (outlook.Level - 55) * outlook.Rate, 0, 100);
                Board.Weekly(job, ctx, team, w, l, stats, today, 2026);
                Assert.Equal(expected, job.Confidence, 9);
                // The pace line quotes the projection the board used.
                double projected = w / (double)(w + l) * ctx.SeasonGames;
                Assert.StartsWith($"On pace for {projected:0} wins against a target of {job.Current!.TargetWins}", outlook.Drivers[0].Text);
                Assert.Equal(job.Current.Demands.Count + 1, outlook.Drivers.Count);
                // Behind the target pulls down, ahead pushes up.
                if (projected < job.Current.TargetWins - 1) Assert.True(outlook.Drivers[0].Points < 0);
                if (projected > job.Current.TargetWins + 1) Assert.True(outlook.Drivers[0].Points > 0);
            }
    }

    [Fact]
    public void Board_chips_and_recent_change_read_the_outlook()
    {
        var outlook = new BoardOutlook(43, 0.05, new[] { new BoardDriver("Pace", "On pace for 30 wins against a target of 39 (9 short)", -14), new BoardDriver("Owner's demand", "Play young players: not being done", -5) });
        var chips = BoardViewModel.DriverChips(outlook);
        Assert.Equal(new[] { "62", "-14", "-5", "43" }, chips.Select(c => c.Value));
        Assert.Equal("bad", chips[1].Tone);
        var job = new JobState();
        foreach (var c in new[] { 60.0, 58, 55, 52, 50 }) job.History.Add(new ConfidencePoint(new DateOnly(2026, 12, 1), c));
        Assert.Equal("Down 10 over the last 4 weeks.", BoardViewModel.Recent(job));
    }

    [AvaloniaFact]
    public void A_page_tip_shows_on_the_first_visit_only()
    {
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { AutoPortraits = false });
        try
        {
            var vm = new MainViewModel();
            vm.Guide.OnPage("Tactics");
            Assert.True(vm.Guide.ShowPageTip);
            vm.Guide.OnPage("Board");
            Assert.True(vm.Guide.ShowPageTip);
            vm.Guide.OnPage("Tactics");
            Assert.False(vm.Guide.ShowPageTip);
            vm.Guide.RevealPageTipCommand.Execute(null);
            Assert.True(vm.Guide.ShowPageTip);
            // Remembered with the app settings, so it holds across careers and restarts.
            Assert.Contains("Tactics", AppSettings.Load().GuideProgress.PagesSeen);
        }
        finally
        {
            if (File.Exists(AppSettings.FilePath)) File.Delete(AppSettings.FilePath);
            AppSettings.FilePath = oldPath; AppSettings.Use(oldSettings);
        }
    }
}
