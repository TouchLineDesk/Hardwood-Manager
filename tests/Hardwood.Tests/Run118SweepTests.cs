using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Hardwood.Core;
using Hardwood.Core.Model;
using Hardwood.Core.RealWorld;
using Hardwood.Core.Season;
using Hardwood.Desktop;
using Hardwood.Core.World;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>Run 118's bug sweep: H110 (the expansion draft), H112 (the relegation banner), H114 (positions on summer cards).</summary>
public class Run118SweepTests
{
    private readonly ITestOutputHelper _out;
    public Run118SweepTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void The_SuperSonics_and_Aces_are_stocked_by_an_expansion_draft_of_real_players()
    {
        var (league, _) = RealWorldData.NewWorld(31);
        var real = RealPlayers.Lines.Select(l => l.Name).ToHashSet();
        var clubs = league.Teams.Take(RealWorldData.Teams.Count).ToList();
        var expansion = league.Teams.Skip(RealWorldData.Teams.Count).ToList();
        Assert.Equal(new[] { "SEA", "LVA" }, expansion.Select(t => t.Abbreviation));
        foreach (var t in expansion)
        {
            _out.WriteLine($"{t.Abbreviation}: " + string.Join(", ", t.Roster.OrderByDescending(p => p.CurrentAbility).Select(p => $"{p.Name} {p.PrimaryPosition} {p.CurrentAbility}")));
            Assert.Equal(RealPlayers.RosterMax, t.Roster.Count);
            Assert.All(t.Roster, p => Assert.Contains(p.Name, real));
            Assert.All(t.Roster, p => Assert.Equal(t.Id, p.TeamId));
            Assert.All(t.Roster, p => Assert.NotNull(p.Contract));
            // A roster that can play: at least two guards and two bigs.
            Assert.True(t.Roster.Count(p => p.PrimaryPosition is Position.PG or Position.SG) >= 2);
            Assert.True(t.Roster.Count(p => p.PrimaryPosition is Position.PF or Position.C) >= 2);
        }
        // Every real club loses exactly one player (two clubs × 15 picks = 30), and keeps a legal roster.
        Assert.All(clubs, t => Assert.Equal(RealPlayers.RosterMax - 1, t.Roster.Count));
        Assert.All(clubs, t => Assert.True(t.Roster.Count >= league.Finance.RosterMin));
        // Every player in the league is on one roster only.
        var ids = league.Teams.SelectMany(t => t.Roster).Select(p => p.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        // The expansion clubs are the league's weakest, as new clubs are; the best real stars stay home.
        var weakest = league.Teams.OrderBy(t => QuickSim.Strength(t)).Take(4).ToList();
        Assert.Contains(expansion[0], weakest);
        Assert.Contains(expansion[1], weakest);
        var stars = league.Teams.SelectMany(t => t.Roster).OrderByDescending(p => p.CurrentAbility).Take(60).ToList();
        Assert.DoesNotContain(stars, p => expansion.Any(e => e.Roster.Contains(p)));
    }

    [Fact]
    public void No_made_up_player_leads_an_NBA_stat_table_in_season_one()
    {
        // H110: on v0.110.2 the Aces' and Sonics' generated guards were 3rd and 5th in assists (seed 31) and 5th (seed 55).
        var (league, _) = RealWorldData.NewWorld(31);
        var real = RealPlayers.Lines.Select(l => l.Name).ToHashSet();
        var rng = new Rng(31UL ^ 0xABCDEF);
        var schedule = ScheduleBuilder.Build(league, new DateOnly(2026, 10, 21), rng);
        var outcome = new SeasonSimulator(rng).Run(league, schedule);
        var agg = new Dictionary<Player, int[]>();
        foreach (var g in outcome.RegularSeason)
            foreach (var box in new[] { g.Home, g.Away })
                foreach (var l in box.Lines.Where(l => l.Seconds > 0))
                {
                    if (!agg.TryGetValue(l.Player, out var a)) agg[l.Player] = a = new int[6];
                    a[0]++; a[1] += l.Points; a[2] += l.Assists; a[3] += l.Rebounds; a[4] += l.Steals; a[5] += l.Blocks;
                }
        var qualified = agg.Where(x => x.Value[0] >= 40).ToList();
        Assert.All(qualified, x => Assert.Contains(x.Key.Name, real));
        foreach (int stat in new[] { 1, 2, 3, 4, 5 })
        {
            var top = qualified.OrderByDescending(x => x.Value[stat] / (double)x.Value[0]).Take(5).ToList();
            Assert.All(top, x => Assert.Contains(x.Key.Name, real));
        }
        var table = outcome.Standings.Conference(Conference.West);
        foreach (var r in table.Where(r => r.Team.Abbreviation is "SEA" or "LVA"))
        {
            _out.WriteLine($"{r.Team.Abbreviation} {r.Wins}-{r.Losses}");
            Assert.InRange(r.Wins, 8, 45);
        }
    }

    [Fact]
    public void A_summer_card_writes_both_positions_like_the_roster()
    {
        var p = new Player { Id = 1, FirstName = "Zach", LastName = "LaVine", PrimaryPosition = Position.SG, SecondaryPosition = Position.SF };
        Assert.Equal("SG/SF", PosText.Of(p));
        Assert.StartsWith("SG/SF · ", new ExpiringRow { Player = p, Age = 31 }.CardLine);
        Assert.Equal("C", PosText.Of(new Player { Id = 2, FirstName = "A", LastName = "B", PrimaryPosition = Position.C }));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void A_club_going_down_sees_one_line_with_how_it_works_behind_it()
    {
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { AutoPortraits = false });
        MainWindow? window = null;
        try
        {
            // The weakest club on seed 77 (an expansion club since H110), to the summer.
            var (probe, _) = RealWorldData.NewWorld(77);
            var teamId = probe.Teams.OrderBy(t => QuickSim.Strength(t)).First().Id;
            var s = GameSession.NewGame(77, teamId, null);
            int guard = 0;
            while (s.Phase != SessionPhase.Offseason && guard++ < 700) s.AdvanceDay();
            _out.WriteLine($"{s.HumanTeam.FullName}: going down {s.GoingDown()}");
            Assert.True(s.GoingDown(), "the weakest club finishes last in its division");
            var vm = new MainViewModel();
            window = new MainWindow { DataContext = vm, Width = 1480, Height = 900 };
            window.Show();
            vm.AttachSession(s);
            vm.Navigate("Offseason");
            void Settle() { for (int i = 0; i < 3; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); } }
            Settle();
            var page = Assert.IsType<OffseasonViewModel>(vm.CurrentPage);
            _out.WriteLine(page.RelegationText);
            _out.WriteLine(page.RelegationDetail);
            Assert.StartsWith("Going down to the ", page.RelegationText);
            Assert.True(page.RelegationText.Length <= 100, page.RelegationText);
            Assert.Contains("Deals you sign now are cut the same way", page.RelegationDetail);
            var banner = window.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "RelegationBanner");
            Assert.True(banner.IsEffectivelyVisible);
            var lines = banner.GetVisualDescendants().OfType<TextBlock>().ToList();
            Assert.All(lines, t => Assert.True(t.Bounds.Height < 30, $"\"{t.Text}\" is {t.Bounds.Height:F0} px tall"));
            Assert.Contains(lines, t => t.Text == "How it works");
            var dir = Environment.GetEnvironmentVariable("HW_SCREENSHOT_DIR");
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
                using var frame = window.CaptureRenderedFrame();
                frame?.Save(Path.Combine(dir, "run118-relegation-banner.png"));
            }
        }
        finally
        {
            window?.Close();
            AppSettings.Use(oldSettings); AppSettings.FilePath = oldPath;
        }
    }
}
