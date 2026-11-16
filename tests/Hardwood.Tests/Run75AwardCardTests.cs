using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Run 75 (review #13 rank 12): Awards opens on the winners as trophy cards (portrait, team colour, stat line,
/// share of the vote), each vote as a ladder of bars, and the All-League teams and All-Stars as chips. The vote tables
/// are one switch away. A new real career shows last season's real winners instead of an empty page.</summary>
[Collection(SharedUiState.Name)]   // it swaps AppSettings, app-wide state
public class Run75AwardCardTests
{
    private static SeasonSummary Seed(GameSession s)
    {
        var mine = s.HumanTeam.Roster;
        var others = s.League.Teams.Where(t => t != s.HumanTeam && t.Roster.Count >= 5).Take(8).ToList();
        var pool = others.SelectMany(t => t.Roster.Take(3)).ToList();
        VoteLine V(Player p, double share, int i) => new(p.Id, p.Name, s.FindTeam(p.TeamId!.Value)!.Abbreviation, 1000 * share, share, $"{30 - i}.1 pts, 8.0 reb, 6.{i} ast");
        List<VoteLine> Votes(IEnumerable<Player> ps, params double[] shares) => ps.Zip(shares).Select((x, i) => V(x.First, x.Second, i)).ToList();
        var awards = new SeasonAwards
        {
            Season = 2025,
            // Your own player wins the MVP; seven got votes, so the ladder shows five and says two more.
            MvpVoting = Votes(new[] { mine[0] }.Concat(pool.Take(6)), 0.62, 0.21, 0.08, 0.04, 0.03, 0.01, 0.01),
            DpoyVoting = Votes(pool.Skip(6).Take(3), 0.5, 0.3, 0.2),
            RoyVoting = Votes(pool.Skip(9).Take(2), 0.9, 0.1),
            SixthManVoting = Votes(pool.Skip(11).Take(2), 0.7, 0.3),
            CoachOfYearTeamId = others[0].Id, CoachOfYearNote = "58-24, up 17 wins",
            AllLeague = new() { pool.Take(5).Select(p => p.Id).ToList(), pool.Skip(5).Take(5).Select(p => p.Id).ToList(), new List<int> { mine[0].Id }.Concat(pool.Skip(10).Take(4).Select(p => p.Id)).ToList() },
            AllStarsEast = pool.Take(6).Select(p => p.Id).ToList(),
            AllStarsWest = pool.Skip(6).Take(6).Select(p => p.Id).ToList(),
            AllStarGame = "East 181, West 174",
            AllStarMvpId = pool[2].Id,
        };
        var summary = new SeasonSummary { Season = 2025, ChampionTeamId = others[1].Id, RunnerUpTeamId = others[2].Id, FinalsMvpPlayerId = others[1].Roster[0].Id, Awards = awards };
        s.League.History.Add(summary);
        return summary;
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void Awards_open_on_winner_cards_ladders_and_chips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run75-{Guid.NewGuid():N}.hwsave");
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { AutoPortraits = false });
        MainWindow? window = null;
        try
        {
            var (probe, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026);
            var s = GameSession.NewGame(2026, probe.Teams.First(t => t.Abbreviation == "BOS").Id, path);
            var vm = new MainViewModel();
            window = new MainWindow { DataContext = vm, Width = 1480, Height = 900 };
            window.Show();
            vm.AttachSession(s);
            void Settle() { for (int i = 0; i < 6; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); } }
            string? dir = Environment.GetEnvironmentVariable("HW_SCREENSHOT_DIR") is { Length: > 0 } d ? d : null;
            void Snap(string name) { if (dir != null) { Settle(); using var f = window.CaptureRenderedFrame(); f?.Save(Path.Combine(dir, name)); } }

            // A new real career: no awards of its own yet, so last season's real winners stand in.
            vm.Navigate("Awards"); Settle();
            var aw = (AwardsViewModel)vm.CurrentPage!;
            Assert.Equal("No awards yet", aw.Headline);
            Assert.True(aw.HasEmptyNote);
            var real = Hardwood.Core.RealWorld.RealWorldData.Seasons.Where(x => x.Season < 2026).MaxBy(x => x.Season)!;
            Assert.Contains($"MVP {real.Mvp}", aw.EmptyNote);
            Assert.Contains(real.Champion, aw.EmptyNote);
            Assert.False(aw.HasWinners);
            Assert.False(aw.HasAllStarPanel);   // no empty All-Star box
            Assert.False(aw.HasSeason);
            Snap("run75-awards-empty.png");

            var summary = Seed(s);
            var mvp = s.HumanTeam.Roster[0];
            aw.Refresh(); Settle();
            var view = window.GetVisualDescendants().OfType<AwardsView>()
                .First(v => v.DataContext == aw && v.GetVisualDescendants().OfType<Border>().Any(b => b.DataContext is WinnerCard));
            Assert.Equal("2025-26 awards", aw.Headline);
            Assert.False(aw.HasEmptyNote);

            // Cards first; the tables hidden.
            Assert.True(aw.ShowCards);
            Assert.True(view.FindControl<ItemsControl>("WinnerCards")!.IsEffectivelyVisible);
            Assert.False(view.FindControl<StackPanel>("TableView")!.IsEffectivelyVisible);

            // Six winners: the four votes, then the Finals MVP and the Coach of the Year.
            Assert.Equal(new[] { "Most Valuable Player", "Defensive Player of the Year", "Rookie of the Year", "Sixth Man of the Year", "Finals MVP", "Coach of the Year" },
                aw.Winners.Select(w => w.Award));
            var mvpCard = aw.Winners[0];
            Assert.Equal(mvp, mvpCard.Player);
            Assert.Equal(s.HumanTeam, mvpCard.Club);
            Assert.True(mvpCard.Mine);                       // your player: the accent border
            Assert.Equal("62% of the vote", mvpCard.Vote);
            Assert.Equal(s.HumanTeam.FullName, mvpCard.TeamLine);
            Assert.False(aw.Winners[1].Mine);
            // The club is the one he played for that season (from the vote), not wherever he is today (code review).
            Assert.Equal(summary.Awards!.DpoyVoting[0].Team, aw.Winners[1].Club!.Abbreviation);
            Assert.False(aw.Winners[4].IsCoach);
            Assert.True(aw.Winners[4].HasPortrait);
            Assert.Equal(summary.FinalsMvpPlayerId, aw.Winners[4].Player!.Id);
            Assert.Contains("Champions:", aw.Winners[4].TeamLine);
            var coy = aw.Winners[5];
            Assert.True(coy.IsCoach);
            Assert.False(coy.HasPortrait);
            Assert.Equal("58-24, up 17 wins", coy.StatLine);
            var cards = view.FindControl<ItemsControl>("WinnerCards")!.GetVisualDescendants().OfType<Border>().Where(b => b.DataContext is WinnerCard && b.Classes.Contains("panel")).ToList();
            Assert.Equal(6, cards.Count);
            Assert.All(cards, c => Assert.True(c.Bounds.Width > 200 && c.Bounds.Height > 100, $"{c.Bounds}"));
            Assert.True(cards[0].Classes.Contains("mine"));

            // The ladders: one per vote, the top five as bars scaled to 100% of the vote, the rest counted.
            Assert.Equal(4, aw.Ladders.Count);
            var mvpLadder = aw.Ladders[0];
            Assert.Equal(5, mvpLadder.Rows.Count);
            Assert.Equal("2 more got votes (Full table)", mvpLadder.More);
            Assert.True(mvpLadder.Rows[0].Winner && mvpLadder.Rows[0].Mine);
            Assert.Equal(Math.Round(0.62 * AwardsViewModel.LadderBarMax), mvpLadder.Rows[0].BarWidth);
            Assert.True(mvpLadder.Rows.Zip(mvpLadder.Rows.Skip(1)).All(x => x.First.BarWidth >= x.Second.BarWidth));
            Assert.All(aw.Ladders.SelectMany(l => l.Rows), r => Assert.InRange(r.BarWidth, 2, AwardsViewModel.LadderBarMax));
            Assert.Equal(2, aw.Ladders[2].Rows.Count);
            Assert.False(aw.Ladders[2].HasMore);

            // The honour chips: three All-League teams of five (yours marked), two All-Star rosters with the MVP starred.
            Assert.Equal(new[] { "First team", "Second team", "Third team" }, aw.AllLeagueTeams.Select(t => t.Title));
            Assert.All(aw.AllLeagueTeams, t => Assert.Equal(5, t.Players.Count));
            Assert.True(aw.AllLeagueTeams[2].Players[0].Mine);
            Assert.Equal(new[] { "East", "West" }, aw.AllStarTeams.Select(t => t.Title));
            Assert.Single(aw.AllStarTeams.SelectMany(t => t.Players), c => c.Star);
            Assert.True(aw.AllStarTeams[0].Players[2].Star);
            Assert.Contains("East 181, West 174", aw.AllStarLine);
            Assert.Equal(15 + 12, view.GetVisualDescendants().OfType<Border>().Count(b => b.Classes.Contains("honourChip") && b.IsEffectivelyVisible));
            Snap("run75-awards-cards.png");
            Assert.True(LayoutCheck.Scan(window) is var p1 && p1.Count == 0, string.Join("\n", LayoutCheck.Scan(window)));

            // The tables, one switch away; back to the cards.
            aw.View = "Full table"; Settle();
            Assert.True(view.FindControl<StackPanel>("TableView")!.IsEffectivelyVisible);
            Assert.False(view.FindControl<StackPanel>("CardsView")!.IsEffectivelyVisible);
            Assert.Equal(7, aw.Mvp.Count);
            Assert.Contains(mvp.Name, aw.AllLeague);
            Snap("run75-awards-table.png");
            aw.View = "Cards"; Settle();
            Assert.True(view.FindControl<StackPanel>("CardsView")!.IsEffectivelyVisible);

            // The in-progress season: All-Star weekend only, no winner cards.
            s.Midseason = new SeasonAwards { Season = 2026, AllStarsEast = summary.Awards!.AllStarsEast, AllStarsWest = summary.Awards.AllStarsWest, AllStarGame = "East 170, West 168", AllStarMvpId = summary.Awards.AllStarsWest[0] };
            aw.Refresh(); Settle();
            if (aw.Season!.EndsWith("(in progress)"))
            {
                Assert.False(aw.HasWinners);
                Assert.False(aw.HasLadders);
                Assert.True(aw.HasAllStars);
                Assert.True(aw.AllStarTeams[1].Players[0].Star);
            }
            var problems = LayoutCheck.Scan(window);
            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }
        finally
        {
            window?.Close();
            AppSettings.Use(oldSettings); AppSettings.FilePath = oldPath;
            foreach (var f in new[] { path, path + ".bak" }) if (File.Exists(f)) File.Delete(f);
        }
    }
}
