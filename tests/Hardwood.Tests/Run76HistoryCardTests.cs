using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Desktop;
using Hardwood.Desktop.Controls;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Run 76 (review #13 rank 15): History's Seasons opens on champion cards (crest, the club's colour, who they
/// beat, the MVPs), and a new real career shows the real NBA's last ten champions instead of an empty table. The records
/// book and the NBA history tiles lead with the holder's photo (or the club's crest) under the club's colour.
/// And H24 (bug hunt): Waive, Release selected and the talks panel's Release ask before they cut anyone.</summary>
[Collection(SharedUiState.Name)]   // it swaps AppSettings, app-wide state
public class Run76HistoryCardTests
{
    private static (GameSession s, MainViewModel vm, MainWindow window, Action settle) Open(string path)
    {
        var (probe, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026);
        var s = GameSession.NewGame(2026, probe.Teams.First(t => t.Abbreviation == "BOS").Id, path);
        var vm = new MainViewModel();
        var window = new MainWindow { DataContext = vm, Width = 1480, Height = 900 };
        window.Show();
        vm.AttachSession(s);
        void Settle() { for (int i = 0; i < 6; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); } }
        return (s, vm, window, Settle);
    }

    private static void Snap(MainWindow window, Action settle, string name)
    {
        if (Environment.GetEnvironmentVariable("HW_SCREENSHOT_DIR") is not { Length: > 0 } dir) return;
        settle();
        using var f = window.CaptureRenderedFrame();
        f?.Save(Path.Combine(dir, name));
    }

    private static void Clean(string message, MainWindow window)
    {
        var problems = LayoutCheck.Scan(window);
        Assert.True(problems.Count == 0, message + ":\n" + string.Join("\n", problems));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void History_opens_on_champion_cards_and_the_real_past_with_photos_and_crests()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run76-{Guid.NewGuid():N}.hwsave");
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { AutoPortraits = false });
        MainWindow? window = null;
        try
        {
            var (s, vm, w, settle) = Open(path);
            window = w;
            vm.Navigate("History"); settle();
            var h = (HistoryViewModel)vm.CurrentPage!;
            var view = window.GetVisualDescendants().OfType<HistoryView>().First(v => v.DataContext == h && v.Bounds.Width > 0);

            // A new real career: no seasons of its own, so the real NBA's last ten champions fill the page.
            Assert.True(h.ShowCards);
            Assert.False(h.HasSeasonCards);
            Assert.True(h.HasSeasonsNote);
            Assert.Equal(10, h.RealPastCards.Count);
            var lastReal = Hardwood.Core.RealWorld.RealWorldData.Seasons.Where(x => x.Season < s.League.Season).MaxBy(x => x.Season)!;
            var first = h.RealPastCards[0];
            Assert.Equal(lastReal.Label, first.Season);
            Assert.Equal(lastReal.Champion, first.ChampionName);
            Assert.Equal(lastReal.ChampionFranchise, first.Champion!.Abbreviation);   // the franchise's crest today
            Assert.Contains(lastReal.RunnerUp, first.Beat);
            Assert.Equal(lastReal.FinalsMvp, first.FinalsMvp);
            Assert.True(h.RealPastCards.Zip(h.RealPastCards.Skip(1)).All(x => string.CompareOrdinal(x.First.Season, x.Second.Season) > 0));
            // Boston's real 2023-24 title is marked as yours.
            Assert.Contains(h.RealPastCards, c => c.Mine && c.Champion == s.HumanTeam);
            Assert.True(view.FindControl<ItemsControl>("RealPastCards")!.IsEffectivelyVisible);
            var crests = view.FindControl<ItemsControl>("RealPastCards")!.GetVisualDescendants().OfType<TeamLogo>().Count(l => l.IsEffectivelyVisible);
            var ic = view.FindControl<ItemsControl>("RealPastCards")!;
            Assert.True(crests == 10, $"crests {crests}; logos {ic.GetVisualDescendants().OfType<TeamLogo>().Count()}; borders {ic.GetVisualDescendants().OfType<Border>().Count()}; bounds {ic.Bounds}; items {ic.ItemCount}; vis {string.Join(",", ic.GetVisualDescendants().OfType<TeamLogo>().Take(3).Select(l => $"{l.IsVisible}/{l.Team?.Abbreviation}"))}");
            Snap(window, settle, "run76-history-new-career.png");
            Clean("Seasons, new real career", window);

            // A finished season of this career: your title, as the first card above the real past.
            var others = s.League.Teams.Where(t => t != s.HumanTeam && t.Roster.Count > 0).ToList();
            s.League.History.Add(new SeasonSummary
            {
                Season = s.League.Season, ChampionTeamId = s.HumanTeam.Id, RunnerUpTeamId = others[0].Id,
                MvpPlayerId = s.HumanTeam.Roster[0].Id, FinalsMvpPlayerId = s.HumanTeam.Roster[0].Id, ScoringLeaderPlayerId = others[0].Roster[0].Id,
                Records = new() { (s.HumanTeam.Id, 61, 21) },
            });
            h.Refresh(); settle();
            Assert.Single(h.SeasonCards);
            var mine = h.SeasonCards[0];
            Assert.True(mine.Mine);
            Assert.Equal(s.HumanTeam, mine.Champion);
            Assert.Equal($"beat the {others[0].FullName}", mine.Beat);
            Assert.Equal(s.HumanTeam.Roster[0].Name, mine.FinalsMvp);
            Assert.Equal("You: 61-21 · Champions", mine.OurRecord);
            Assert.False(h.HasSeasonsNote);
            Assert.Equal(10, h.RealPastCards.Count);   // the real past stays below, before this career's first season
            Assert.True(h.RealPastCards.All(c => string.CompareOrdinal(c.Season, mine.Season) < 0));
            var card = view.FindControl<ItemsControl>("SeasonCards")!.GetVisualDescendants().OfType<Border>().First(b => b.DataContext is SeasonCard && b.Classes.Contains("panel"));
            Assert.True(card.Classes.Contains("mine"));
            Assert.True(card.Bounds.Width > 250 && card.Bounds.Height > 100, $"{card.Bounds}");
            Snap(window, settle, "run76-history-cards.png");
            Clean("Seasons cards", window);

            h.View = "Full table"; settle();
            Assert.True(view.FindControl<DataGrid>("SeasonsTable")!.IsEffectivelyVisible);
            Assert.False(view.FindControl<ScrollViewer>("SeasonCardsView")!.IsEffectivelyVisible);
            Assert.Single(h.Rows);
            Snap(window, settle, "run76-history-table.png");
            Clean("Seasons table", window);
            h.View = "Cards"; settle();

            // The records book: each record with a holder leads with his photo or his club's crest, in its colour.
            h.Tab = 1; settle();
            foreach (var t in h.GameRecords.Concat(h.SeasonRecords).Concat(h.CareerRecords).Where(t => t.HasTop))
            {
                Assert.True(t.HasPhoto || t.HasCrest || t.LeaderClub == null, t.Title);
                Assert.Equal(t.Rows.Count - 1, t.Rest.Count);
            }
            // Records not yet set are one line per section, not empty cards.
            Assert.All(h.GameRecordsSet.Concat(h.SeasonRecordsSet).Concat(h.CareerRecordsSet), t => Assert.False(t.Empty));
            foreach (var (all, line) in new[] { (h.GameRecords, h.GameRecordsToSet), (h.SeasonRecords, h.SeasonRecordsToSet), (h.CareerRecords, h.CareerRecordsToSet) })
                foreach (var t in all.Where(t => t.Empty)) Assert.Contains(t.Title, line);
            Snap(window, settle, "run76-records-book.png");
            Clean("Records book", window);

            // NBA history: LeBron (in the game) shows his photo; the Pistons' 186 the crest; Wilt's Philadelphia Warriors the trophy.
            h.Tab = 2; settle();
            var points = h.RealCareerRecords.First(r => r.Label == "Points");
            Assert.True(points.HasPhoto, points.Holder);
            Assert.Equal("LeBron James", points.Player!.Name);
            var team = h.RealGameRecords.First(r => r.Label == "Team points");
            Assert.True(team.HasCrest);
            Assert.Equal("DET", team.Club!.Abbreviation);
            // Wilt's Philadelphia Warriors are today's Golden State franchise; a record shared by two players has the trophy.
            var wilt = h.RealGameRecords.First(r => r.Label == "Points");
            Assert.True(wilt.HasCrest);
            Assert.Equal("GSW", wilt.Club!.Abbreviation);
            Assert.True(h.RealGameRecords.First(r => r.Label == "Steals").HasIcon);
            var celtics = h.RealTitles.First(t => t.Ours);
            Assert.Equal(s.HumanTeam, celtics.Club);
            Assert.All(h.RealTitles, t => Assert.NotNull(t.Club));
            Snap(window, settle, "run76-nba-history.png");
            Clean("NBA history", window);
        }
        finally
        {
            window?.Close();
            AppSettings.Use(oldSettings); AppSettings.FilePath = oldPath;
            foreach (var f in new[] { path, path + ".bak" }) if (File.Exists(f)) File.Delete(f);
        }
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void Waive_and_Release_ask_before_cutting_anyone()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run76-waive-{Guid.NewGuid():N}.hwsave");
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { AutoPortraits = false });
        MainWindow? window = null;
        try
        {
            var (s, vm, w, settle) = Open(path);
            window = w;
            var team = s.HumanTeam;
            while (team.Roster.Count <= s.League.Finance.RosterMin + 2 && s.League.FreeAgents.Count > 0)
            {
                var fa = s.League.FreeAgents[0];
                s.League.FreeAgents.Remove(fa); fa.TeamId = team.Id; fa.Contract = new Contract { Salary = 1_000_000, Years = 1 }; team.Roster.Add(fa);
            }
            int count = team.Roster.Count;

            // Transactions → Waive: the first press asks; Keep him leaves him; Waive him cuts him.
            vm.Navigate("Transactions"); settle();
            var tx = (TransactionsViewModel)vm.CurrentPage!;
            var star = tx.Ours[0];
            tx.Waive(star); settle();
            Assert.True(tx.ConfirmingWaive);
            Assert.Contains(star.Name, tx.WaiveQuestion);
            tx.Tab = 4;   // the Waive tab
            Snap(window, settle, "run76-waive-confirm.png");
            Clean("Waive confirm", window);
            Assert.Contains(star.Player, team.Roster);
            Assert.Equal(count, team.Roster.Count);
            tx.CancelWaive();
            Assert.False(tx.ConfirmingWaive);
            Assert.Contains(star.Player, team.Roster);
            Assert.Equal($"{star.Name} stays.", tx.FaStatus);

            var cut = tx.Ours[^1];
            tx.Waive(cut);
            tx.ConfirmWaive(); settle();
            Assert.False(tx.ConfirmingWaive);
            Assert.DoesNotContain(cut.Player, team.Roster);
            Assert.Contains(cut.Player, s.League.FreeAgents);
            Assert.Equal(count - 1, team.Roster.Count);

            // Offseason → Your roster → Release selected: the same.
            vm.Navigate("Offseason"); settle();
            var off = (OffseasonViewModel)vm.CurrentPage!;
            var pick = team.Roster.OrderBy(p => p.CurrentAbility).First();
            off.SelectedRosterPlayer = PlayerRow.From(pick, s, team.Abbreviation);
            off.WaivePlayer();
            Assert.True(off.ConfirmingRelease);
            Assert.Contains(pick.Name, off.ReleaseQuestion);
            Assert.Contains(pick, team.Roster);
            off.CancelRelease();
            Assert.False(off.ConfirmingRelease);
            Assert.Contains(pick, team.Roster);
            off.SelectedRosterPlayer = PlayerRow.From(pick, s, team.Abbreviation);
            off.WaivePlayer();
            off.ConfirmRelease();
            Assert.DoesNotContain(pick, team.Roster);
            Assert.Single(s.League.FreeAgents, x => x.Id == pick.Id);

            // The talks panel's Release asks too.
            var talk = off.Talk;
            var renew = team.Roster[0];
            talk.Show(renew);
            if (talk.CanRelease)
            {
                talk.ReleaseCommand.Execute(null);
                Assert.True(talk.ConfirmingRelease);
                Assert.Contains(renew, team.Roster);
                talk.CancelReleaseCommand.Execute(null);
                Assert.False(talk.ConfirmingRelease);
                Assert.Contains(renew, team.Roster);
            }
            Clean("Offseason after the release", window);
        }
        finally
        {
            window?.Close();
            AppSettings.Use(oldSettings); AppSettings.FilePath = oldPath;
            foreach (var f in new[] { path, path + ".bak" }) if (File.Exists(f)) File.Delete(f);
        }
    }
}
