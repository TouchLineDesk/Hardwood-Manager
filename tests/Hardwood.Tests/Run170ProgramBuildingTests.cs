using Hardwood.Core.College;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Desktop.ViewModels;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 170, CS7 part 1 (K19, "the program has nothing to build"): a college program's boosters give money each summer
/// (more at a bigger program, more when they're happy), and the booster fund buys practice gym and arena levels. What's
/// built adds recruiting appeal (and the gym, training quality) for the coached program; nothing changes until something
/// is built, so the computer's world and a program you take over play as before.
/// </summary>
[Collection(SharedUiState.Name)]
public class Run170ProgramBuildingTests
{
    private readonly ITestOutputHelper _out;
    public Run170ProgramBuildingTests(ITestOutputHelper o) => _out = o;

    private static GameSession Career(string name, ulong seed, string? path = null)
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(seed, 2026, GameSession.DefaultFullSim);
        int id = world.College!.Programs.First(p => p.FullName.StartsWith(name)).Id;
        return GameSession.NewGame(seed, id, path);
    }

    [Fact]
    public void The_rules_scale_with_the_program()
    {
        Assert.Equal(1, ProgramBuilding.SeedLevel(12));
        Assert.Equal(2, ProgramBuilding.SeedLevel(40));
        Assert.Equal(3, ProgramBuilding.SeedLevel(62));
        Assert.Equal(4, ProgramBuilding.SeedLevel(95)); // level 5 is only ever built
        // A happy blue blood's boosters give several times a small program's.
        long big = ProgramBuilding.Gift(90, 80), small = ProgramBuilding.Gift(20, 40);
        _out.WriteLine($"gift: blue blood {ProgramBuilding.Money(big)}, small program {ProgramBuilding.Money(small)}");
        Assert.True(big >= 3 * small);
        Assert.True(ProgramBuilding.Gift(50, 90) > ProgramBuilding.Gift(50, 20));
        // Each level costs more than the last; the arena more than the gym.
        Assert.True(ProgramBuilding.Cost(FacilityKind.Practice, 3) > ProgramBuilding.Cost(FacilityKind.Practice, 2));
        Assert.True(ProgramBuilding.Cost(FacilityKind.Arena, 2) > ProgramBuilding.Cost(FacilityKind.Practice, 2));
    }

    [Fact]
    public void Nothing_changes_until_something_is_built_then_appeal_rises_by_the_stated_amount()
    {
        var t = new Team { Id = 5, City = "Test", Name = "State", Abbreviation = "TST", Division = "X" };
        var b = ProgramBuilding.Ensure(t, 40);
        Assert.Equal(2, t.Money.Facilities.Arena.Level);
        Assert.Equal(0, ProgramBuilding.Appeal(t));
        Assert.Equal(0, t.Money.Facilities.Practice.Edge);
        Assert.True(b.Fund >= ProgramBuilding.Cost(FacilityKind.Practice, 2), $"fund {b.Fund}: a gym level on day one");
        b.Fund = 10_000_000;
        string line = ProgramBuilding.Build(t, FacilityKind.Arena, openNow: true);
        _out.WriteLine(line);
        Assert.Equal(3, t.Money.Facilities.Arena.Level);
        Assert.Equal(ProgramBuilding.ArenaAppeal, ProgramBuilding.Appeal(t));
        line = ProgramBuilding.Build(t, FacilityKind.Practice, openNow: false);
        _out.WriteLine(line);
        Assert.Contains("opens next summer", line);
        Assert.Equal(2, t.Money.Facilities.Practice.Level);
        Assert.Contains("already under way", ProgramBuilding.Build(t, FacilityKind.Arena, openNow: false));
    }

    [Fact]
    public void A_season_moves_the_boosters_and_their_gift_and_opens_the_project()
    {
        var s = Career("Providence", 77);
        var c = s.World.College!;
        var t = s.HumanTeam;
        var b = s.MyProgramBooks;
        _out.WriteLine($"{t.FullName}: prestige {c.Prestige[t.Id]:0}, gym {t.Money.Facilities.Practice.Level}, arena {t.Money.Facilities.Arena.Level}, boosters {b.Boosters:0}, fund {ProgramBuilding.Money(b.Fund)}");
        b.Fund = 20_000_000;
        // Mid-season: the gym is built through the year.
        int guard = 0;
        while (!c.Season.Games.Any(g => g.Played && (g.HomeId == t.Id || g.AwayId == t.Id)) && guard++ < 200) s.AdvanceDay();
        Assert.False(s.ProgramBuildsNow);
        int gym = t.Money.Facilities.Practice.Level;
        _out.WriteLine(s.BuildProgramFacility(FacilityKind.Practice));
        Assert.Equal(gym, t.Money.Facilities.Practice.Level);
        double before = b.Boosters; long fund = b.Fund;
        guard = 0;
        while (!s.Inbox.Any(m => m.Title.StartsWith("Boosters:")) && guard++ < 400) s.AdvanceDay();
        var msg = Assert.Single(s.Inbox, m => m.Title.StartsWith("Boosters:"));
        _out.WriteLine($"{msg.Date}: {msg.Title}\n{msg.Body}");
        Assert.Equal(gym + 1, t.Money.Facilities.Practice.Level);
        Assert.Null(b.Pending);
        Assert.Equal(ProgramBuilding.GymEdge, t.Money.Facilities.Practice.Edge);
        Assert.Equal(fund + b.LastGift, b.Fund);
        Assert.True(b.LastGift > 0);
        Assert.NotEqual(before, b.Boosters);
        Assert.Contains("Booster mood", msg.Body);
    }

    [Fact]
    public void Building_before_the_season_opens_at_once_raises_a_recruits_appeal_and_survives_a_reload()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hw-run170-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "career.hwsave");
            var s = Career("Providence", 77, path);
            Assert.True(s.HasProgramBuilding);
            Assert.True(s.ProgramBuildsNow);
            var c = s.World.College!; var t = s.HumanTeam;
            var cls = c.Recruits.Values.Where(r => r.CommittedTo == null).Min(r => r.ClassOf);
            var (p, r) = c.ClassOf(cls).Select(x => (x, c.Recruits[x.Id])).First(x => x.Item2.CommittedTo == null);
            var board = c.BoardFor(cls);
            double was = c.Appeal(t, p, r, board);
            s.MyProgramBooks.Fund = 20_000_000;
            int arena = t.Money.Facilities.Arena.Level;
            string line = s.BuildProgramFacility(FacilityKind.Arena);
            _out.WriteLine(line);
            Assert.Contains("ready for the season", line);
            Assert.Equal(arena + 1, t.Money.Facilities.Arena.Level);
            double now = c.Appeal(t, p, r, board);
            _out.WriteLine($"{p.Name}: appeal {was:0.0} → {now:0.0}");
            Assert.Equal(ProgramBuilding.ArenaAppeal, now - was, 6);
            // A computer program's appeal is untouched.
            var other = c.Programs.First(x => x.Id != t.Id);
            Assert.Equal(0, ProgramBuilding.Appeal(other));

            s.SaveNow();
            var back = GameSession.Load(path);
            Assert.Equal(arena + 1, back.HumanTeam.Money.Facilities.Arena.Level);
            Assert.Equal(s.MyProgramBooks.Fund, back.MyProgramBooks.Fund);
            Assert.Equal(ProgramBuilding.ArenaAppeal, ProgramBuilding.Appeal(back.HumanTeam));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void A_pro_club_has_no_booster_fund()
    {
        var s = GameSession.NewGame(4, 3, null);
        Assert.False(s.HasProgramBuilding);
        Assert.StartsWith("Only a college program", s.BuildProgramFacility(FacilityKind.Arena));
    }

    [AvaloniaFact]
    public void The_board_shows_the_program_building_panel_and_builds_from_it()
    {
        var oldSettings = Hardwood.Desktop.AppSettings.Current; var oldPath = Hardwood.Desktop.AppSettings.FilePath;
        Hardwood.Desktop.AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        Hardwood.Desktop.AppSettings.Use(new Hardwood.Desktop.AppSettings { AutoPortraits = false });
        Hardwood.Desktop.Views.MainWindow? window = null;
        try
        {
            var s = Career("Providence", 77);
            var vm = new MainViewModel();
            window = new Hardwood.Desktop.Views.MainWindow { DataContext = vm, Width = 1480, Height = 900 };
            window.Show();
            vm.AttachSession(s);
            vm.Navigate("Board");
            var board = Assert.IsType<BoardViewModel>(vm.CurrentPage);
            void Settle() { for (int i = 0; i < 3; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window!.UpdateLayout(); } }
            Settle();
            Assert.True(board.HasProgram);
            Assert.False(board.HasBusiness);
            Assert.Equal(new[] { "Booster fund", "Boosters", "Practice gym", "Arena" }, board.ProgramBuildChips.Select(c => c.Label));
            Assert.StartsWith("Nothing built yet", board.ProgramAppeal);
            Assert.True(board.CanBuildGym, board.BuildGymTip);
            _out.WriteLine($"{board.BuildGymText} | {board.BuildArenaText} ({board.BuildArenaTip})");
            board.BuildGymCommand.Execute(null); Settle();
            _out.WriteLine(board.ProgramStatus);
            Assert.Contains("ready for the season", board.ProgramStatus);
            Assert.Contains($"+{ProgramBuilding.GymAppeal:0} to your appeal", board.ProgramAppeal);
            Assert.Empty(LayoutCheck.Scan(window));
            var title = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Avalonia.Controls.TextBlock>().First(x => x.Text == "Program building");
            var scroll = Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<Avalonia.Controls.ScrollViewer>(title)!;
            if (title.TranslatePoint(new Avalonia.Point(0, 0), scroll) is { } at) scroll.Offset = new Avalonia.Vector(0, scroll.Offset.Y + at.Y - 40);
            Settle();
            if (Environment.GetEnvironmentVariable("HW_SCREENSHOT_DIR") is { Length: > 0 } dir)
            {
                Directory.CreateDirectory(dir);
                using var frame = window.CaptureRenderedFrame();
                frame?.Save(Path.Combine(dir, "run170-program-building.png"));
            }
        }
        finally
        {
            window?.Close();
            Hardwood.Desktop.AppSettings.Use(oldSettings); Hardwood.Desktop.AppSettings.FilePath = oldPath;
        }
    }
}
