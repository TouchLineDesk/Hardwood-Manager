using Avalonia.Headless;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Run 71: the bug hunter's Medium findings.
/// H13 League Leaders shows your own league first (a G League coach saw the NBA's leaders, unlabelled);
/// H14 Tactics' stopper box says it lists your players; H10 the top bar says when your season is over while the
/// league plays on; H15 out of work, the top bar shows no club, record, next game or Next game button.</summary>
[Collection(SharedUiState.Name)]   // it swaps AppSettings, app-wide state
public class Run71BugHuntTests
{
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void A_G_League_career_sees_its_own_leaders_and_its_own_season_end()
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(31);
        int teamId = world[UsaPyramid.Tier2].Clubs.OrderBy(t => QuickSim.Strength(t)).ElementAt(15).Id;
        var path = Path.Combine(Path.GetTempPath(), $"hw-run71-{Guid.NewGuid():N}.hwsave");
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { AutoPortraits = false });
        try
        {
            var s = GameSession.NewGame(31, teamId, path);
            var vm = new MainViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1480, Height = 900 };
            window.Show();
            vm.AttachSession(s);
            void Settle() { for (int i = 0; i < 6; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); } }
            string? dir = Environment.GetEnvironmentVariable("HW_SCREENSHOT_DIR") is { Length: > 0 } d ? d : null;
            void Snap(string name) { if (dir != null) { Settle(); using var f = window.CaptureRenderedFrame(); f?.Save(Path.Combine(dir, name)); } }
            var hl = s.HumanLeague!;
            string home = LeagueBook.HomeOf(s, s.HumanTeam)!.Name, top = LeagueBook.Get(s, LeagueBook.NablKey)!.Name;
            Assert.NotEqual(home, top);
            var mine = hl.Clubs.Select(t => t.Id).ToHashSet();

            // A month into the season.
            while (s.Phase != SessionPhase.RegularSeason || hl.Season.Fixtures.Count(f => f.Played) < 200) s.AdvanceDay();
            vm.AfterTick();

            // H13: your league first, named; the NBA one pick away.
            vm.Navigate("League Leaders"); Settle();
            var lv = (LeadersViewModel)vm.CurrentPage!;
            Assert.Equal(home, lv.LeagueName);
            Assert.Equal(new[] { home, top }, lv.LeagueNames);
            Assert.True(lv.HasLeagueChoice);
            Assert.StartsWith(home, lv.Note);
            Assert.All(lv.Cards, c => Assert.True(c.HasAny, c.Title));
            Assert.All(lv.Cards.SelectMany(c => c.Entries), e => Assert.Contains(e.Team!.Id, mine));
            Snap("run71-leaders-gleague.png");
            lv.View = "Full table"; Settle();
            Assert.NotEmpty(lv.Rows);
            Assert.All(lv.Rows, r => Assert.Contains(hl.Clubs, t => t.Abbreviation == r.Team));
            Assert.Equal(lv.Rows.Max(r => r.PPG), lv.Rows[0].PPG);
            lv.View = "Cards";
            lv.LeagueName = top; Settle();
            Assert.StartsWith(top, lv.Note);
            var nba = s.League.Teams.Select(t => t.Id).ToHashSet();
            Assert.All(lv.Cards.SelectMany(c => c.Entries), e => Assert.Contains(e.Team!.Id, nba));
            Assert.Contains(window.GetVisualDescendantsOf<Avalonia.Controls.ComboBox>(), b => b.Name == "LeagueBox" && b.IsEffectivelyVisible);

            // H14: the stopper box is labelled as your player.
            vm.Navigate("Tactics"); Settle();
            Assert.Contains(window.GetVisualDescendantsOf<Avalonia.Controls.TextBlock>(), t => t.Text == "Your stopper on their best scorer");
            Assert.DoesNotContain(window.GetVisualDescendantsOf<Avalonia.Controls.TextBlock>(), t => t.Text == "Their best scorer");
            Snap("run71-tactics.png");

            // H10: once your club has nothing left to play, the top bar says so while the league plays on.
            int guard = 0;
            string? seen = null;
            while (!hl.Season.IsComplete && s.Phase != SessionPhase.Offseason && guard++ < 260)
            {
                s.AdvanceDay();
                if (hl.Season.Stage == CompetitionSeason.RegularSeason) continue;
                var text = MainViewModel.SeasonOverText(s);
                bool stillIn = hl.Season.Fixtures.Any(f => !f.Played && (f.HomeId == teamId || f.AwayId == teamId))
                    || hl.Season.Series.Any(x => !x.Complete && (x.HighId == teamId || x.LowId == teamId));
                if (stillIn) Assert.Null(text);
                if (text != null && seen == null)
                {
                    seen = text;
                    vm.AfterTick();
                    Assert.Equal($"{hl.Profile.Name} · {text}", vm.PhaseText);
                    Assert.DoesNotContain(hl.Season.Stage.ToLower(), vm.PhaseText.Replace(hl.Profile.Name, ""));
                    vm.Navigate("Home"); Snap("run71-topbar-out.png");
                }
            }
            Assert.NotNull(seen);

            // H15: out of work, no club in the top bar.
            s.Fire();
            vm.AfterTick(); Settle();
            Assert.False(s.Employed);
            Assert.Null(vm.HeaderTeam);
            Assert.Equal("", vm.RecordText);
            Assert.Equal("", vm.NextGameText);
            Assert.False(vm.ShowNextGameButton);
            Assert.Equal("Looking for work", vm.JobText);
            Snap("run71-topbar-fired.png");
        }
        finally
        {
            AppSettings.Use(oldSettings); AppSettings.FilePath = oldPath;
            foreach (var f in new[] { path, path + ".bak" }) if (File.Exists(f)) File.Delete(f);
        }
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void An_NBA_career_keeps_one_league_and_a_sacked_coach_loses_the_club_line()
    {
        var (probe, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(55);
        int teamId = probe.Teams.OrderBy(t => QuickSim.Strength(t)).ElementAt(10).Id;
        var path = Path.Combine(Path.GetTempPath(), $"hw-run71b-{Guid.NewGuid():N}.hwsave");
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { AutoPortraits = false });
        try
        {
            var s = GameSession.NewGame(55, teamId, path);
            var vm = new MainViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1480, Height = 900 };
            window.Show();
            vm.AttachSession(s);
            void Settle() { for (int i = 0; i < 6; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); } }
            while (s.Phase != SessionPhase.RegularSeason || s.Stats.Values.Count(x => x.Games >= 10) < 100) s.AdvanceDay();
            vm.AfterTick();
            vm.Navigate("League Leaders"); Settle();
            var lv = (LeadersViewModel)vm.CurrentPage!;
            string top = LeagueBook.Get(s, LeagueBook.NablKey)!.Name;
            Assert.Equal(new[] { top }, lv.LeagueNames);
            Assert.False(lv.HasLeagueChoice);
            Assert.StartsWith(top, lv.Note);
            Assert.All(lv.Cards, c => Assert.True(c.HasAny, c.Title));
            Assert.Null(MainViewModel.SeasonOverText(s));   // the regular season: no line

            Assert.True(vm.ShowNextGameButton);
            s.Fire();
            vm.AfterTick(); Settle();
            Assert.Null(vm.HeaderTeam);
            Assert.Equal("", vm.RecordText);
            Assert.Equal("", vm.NextGameText);
            Assert.False(vm.ShowNextGameButton);
            Assert.True(vm.ShowSimButtons);   // Sim week still moves time on while you look for work
        }
        finally
        {
            AppSettings.Use(oldSettings); AppSettings.FilePath = oldPath;
            foreach (var f in new[] { path, path + ".bak" }) if (File.Exists(f)) File.Delete(f);
        }
    }
}
