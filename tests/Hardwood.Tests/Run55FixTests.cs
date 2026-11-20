using Hardwood.Core.Season;
using Hardwood.Core.World;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Run 55: the bug list's #17 (a college career's balance after a reload), #18 and #19 (box scores of your
/// college and lower-tier games after a reload), #5 (buttons that did nothing with nothing selected, D71 A), #6 (the Inbox
/// header after a new job), #7 (talk topics in the week after a talk) and #8 (Settings buttons with no confirmation).</summary>
[Collection(SharedUiState.Name)] // touches app settings or the portrait service (run 131: they raced PortraitServiceTests)
public class Run55FixTests
{
    private static GameSession Career(Func<Hardwood.Core.Model.League, GameWorld, int> pick, out string path)
    {
        var (probe, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026);
        path = Path.Combine(Path.GetTempPath(), $"hw-run55-{Guid.NewGuid():N}.hwsave");
        return GameSession.NewGame(2026, pick(probe, world), path);
    }

    private static void PlayUntilGames(GameSession s, int games)
    {
        int guard = 0;
        while (s.MyGames().Count(g => g.Played) < games && guard++ < 200) s.AdvanceDay();
    }

    [Fact]
    public void A_college_career_keeps_its_balance_and_box_scores_after_a_reload()
    {
        var s = Career((_, w) => w.College!.Programs.OrderByDescending(t => QuickSim.Strength(t)).ElementAt(49).Id, out var path);
        try
        {
            Assert.True(s.InCollege);
            PlayUntilGames(s, 3);
            // A team dinner (the media decision) costs $40,000: the bug finder saw it come back to $0 on a reload.
            s.HumanTeam.Money.Balance -= GameSession.DinnerCost;
            long balance = s.HumanTeam.Money.Balance;
            var played = s.MyGames().Where(g => g.Played).ToList();
            Assert.All(played, g => Assert.True(g.Result is { } r && r.Home.Lines.Count > 0, "a box score before the reload"));
            s.SaveNow();

            var back = GameSession.Load(path);
            Assert.Equal(balance, back.HumanTeam.Money.Balance);
            var again = back.MyGames().Where(g => g.Played).ToList();
            Assert.Equal(played.Count, again.Count);
            for (int i = 0; i < played.Count; i++)
            {
                var (a, b) = (played[i].Result!, again[i].Result);
                Assert.NotNull(b);
                Assert.True(b!.Home.Lines.Count > 0, "the box score survives the reload (#18)");
                Assert.Equal(a.Home.Points, b.Home.Points);
                Assert.Equal(a.Away.Points, b.Away.Points);
                Assert.Equal(a.Home.Lines.Sum(l => l.Assists), b.Home.Lines.Sum(l => l.Assists));
            }
            // Saved and loaded twice, nothing drifts.
            back.SaveNow();
            var third = GameSession.Load(path);
            Assert.Equal(balance, third.HumanTeam.Money.Balance);
            Assert.Equal(played.Count, third.MyGames().Count(g => g.Played && g.Result is { } r && r.Home.Lines.Count > 0));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void A_G_League_career_keeps_its_box_scores_after_a_reload()
    {
        var s = Career((_, w) => w[UsaPyramid.Tier2].Clubs.OrderBy(t => QuickSim.Strength(t)).ElementAt(15).Id, out var path);
        try
        {
            Assert.False(s.InNabl);
            PlayUntilGames(s, 3);
            var played = s.MyGames().Where(g => g.Played && g.Result is { } r && r.Home.Lines.Count > 0).ToList();
            Assert.NotEmpty(played);
            s.SaveNow();
            var back = GameSession.Load(path);
            var again = back.MyGames().Where(g => g.Played && g.Result is { } r && r.Home.Lines.Count > 0).ToList();
            Assert.Equal(played.Count, again.Count);
            Assert.Equal(played.Select(g => g.Result!.Home.Points + g.Result.Away.Points), again.Select(g => g.Result!.Home.Points + g.Result.Away.Points));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void Buttons_that_need_a_selection_are_greyed_out_until_there_is_one()
    {
        var (probe, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026);
        var path = Path.Combine(Path.GetTempPath(), $"hw-run55-ui-{Guid.NewGuid():N}.hwsave");
        try
        {
            var s = GameSession.NewGame(2026, probe.Teams.First(t => t.Abbreviation == "BOS").Id, path);
            var vm = new MainViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1480, Height = 900 };
            window.Show();
            vm.AttachSession(s);
            void Settle() { for (int i = 0; i < 6; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); } }
            Avalonia.Controls.Button Button(string label) => window.GetVisualDescendantsOf<Avalonia.Controls.Button>()
                .First(b => b.IsEffectivelyVisible && (b.Content as string) == label);

            // Schedule: no game played yet, so Open box score is greyed out and says why.
            vm.Navigate("Schedule & Results"); Settle();
            var open = Button("Open box score");
            Assert.False(open.IsEffectivelyEnabled);
            Assert.Contains("played", Avalonia.Controls.ToolTip.GetTip(open) as string);

            // Roster: nothing picked, then a player picked.
            vm.Navigate("Roster"); Settle();
            var roster = (RosterViewModel)vm.CurrentPage!;
            roster.Selected = null; Settle();
            Assert.False(Button("Open player").IsEffectivelyEnabled);
            roster.Selected = roster.Rows.First(); Settle();
            Assert.True(Button("Open player").IsEffectivelyEnabled);

            // Staff & Training: Release selected and Interview wait for a pick.
            vm.Navigate("Staff & Training"); Settle();
            if (vm.CurrentPage is StaffViewModel staff)
            {
                staff.SelectedOurs = null; staff.SelectedMarket = null; Settle();
                Assert.False(Button("Release selected").IsEffectivelyEnabled);
                Assert.False(Button("Interview").IsEffectivelyEnabled);
            }

            // Squad: in the week after a talk every topic is greyed out, with the reason as its tooltip (#7).
            vm.Navigate("Squad"); Settle();
            var squad = (SquadViewModel)vm.CurrentPage!;
            var who = squad.Selected?.Player ?? squad.Players.First().Player;
            who.LastTalk = s.Today;
            squad.Refresh(); Settle();
            Assert.NotEmpty(squad.Topics);
            Assert.All(squad.Topics, t => { Assert.False(t.Command.CanExecute(null)); Assert.Contains("give it a week", t.Tip); });

            // Inbox: the header follows the club you coach (#6).
            vm.Navigate("Inbox"); Settle();
            var inbox = (InboxViewModel)vm.CurrentPage!;
            Assert.Equal(s.HumanTeam, inbox.Team);
            window.Close();
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}

internal static class VisualHelpers
{
    public static IEnumerable<T> GetVisualDescendantsOf<T>(this Avalonia.Visual v) where T : Avalonia.Visual =>
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(v).OfType<T>();
}
