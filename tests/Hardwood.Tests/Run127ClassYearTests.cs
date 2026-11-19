using Hardwood.Core.College;
using Hardwood.Core.Season;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 127, CS3 (college plan): the years you can see.
/// - Every rostered college player has a class year (Fr, So, Jr, Sr; players enroll at 18 and turn pro at 22 after four
///   years, D80 A in run 128; the seniors are in their final season). No redshirts (#938).
/// - The preseason: a week before the first game, one message with the Top 25, the conference poll and media day.
/// - Senior Night: the day before the last regular-season home game, the final-season players are named.
/// </summary>
public class Run127ClassYearTests
{
    private readonly ITestOutputHelper _out;
    public Run127ClassYearTests(ITestOutputHelper o) => _out = o;

    private static GameSession Career(string name, ulong seed = 2026)
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(seed, 2026, GameSession.DefaultFullSim);
        int id = world.College!.Programs.First(p => p.FullName.StartsWith(name)).Id;
        return GameSession.NewGame(seed, id, null);
    }

    [Fact]
    public void Class_years_follow_age_and_every_class_is_on_the_roster()
    {
        var s = Career("Clemson");
        var c = s.World.College!;
        var summer = new DateOnly(2026, 7, 1);
        foreach (var p in s.HumanTeam.Roster)
        {
            var cy = s.ClassYearOf(p);
            Assert.NotNull(cy);
            int ageThen = p.Age(summer);
            // 18 when the school year began = freshman ... 21 = senior (his final season; D80 A).
            Assert.Equal(Math.Clamp(ageThen - 17, 1, 4), cy!.Year);
            Assert.Equal(ageThen >= 21, cy.Final);
            if (ageThen == 18) Assert.Equal("Fr", cy.Short);
            if (ageThen == 21) Assert.Equal("Sr", cy.Short);
        }
        var years = s.HumanTeam.Roster.Select(p => s.ClassYearOf(p)!.Year).Distinct().OrderBy(y => y).ToList();
        Assert.Equal(new[] { 1, 2, 3, 4 }, years);
        Assert.Equal(c.FinalYear(s.HumanTeam).Count, s.HumanTeam.Roster.Count(p => s.ClassYearOf(p)!.Final));
        // A high-school prospect and a pro have no class year.
        Assert.Null(s.ClassYearOf(c.Prospects.First()));
        Assert.Null(s.ClassYearOf(s.League.Teams[0].Roster[0]));
        _out.WriteLine(s.ClassCounts());
    }

    [Fact]
    public void The_preseason_and_senior_night_arrive_on_time()
    {
        var s = Career("Clemson");
        var c = s.World.College!;
        var first = c.FirstGameDay!.Value;
        var lastHome = c.LastHomeGame(s.HumanTeam.Id)!;
        var finals = c.FinalYear(s.HumanTeam).Select(p => p.Name).ToList();
        int guard = 0;
        var pre = new List<InboxMessage>();
        while (s.Today <= lastHome.Date && guard++ < 250)
        {
            s.AdvanceDay();
            // Deleting it (as "Delete all read" would) never brings it back (review of PR #191).
            pre.AddRange(s.Inbox.Where(m => m.Title.StartsWith("Preseason")));
            s.Inbox.RemoveAll(m => m.Title.StartsWith("Preseason"));
        }
        var pm = Assert.Single(pre);
        _out.WriteLine($"{pm.Date}: {pm.Title}\n{pm.Body}\n");
        Assert.InRange(pm.Date, first.AddDays(-GameSession.PreseasonLeadDays), first.AddDays(-1));
        Assert.Contains("picked", pm.Title);
        Assert.Contains("The preseason Top 25: 1. ", pm.Body);
        Assert.Contains("Media day:", pm.Body);
        Assert.Contains("The season opens on", pm.Body);
        var sn = Assert.Single(s.Inbox.Where(m => m.Title.StartsWith("Senior Night")));
        _out.WriteLine($"{sn.Date}: {sn.Title}\n{sn.Body}");
        Assert.Equal(lastHome.Date.AddDays(-1), sn.Date);
        foreach (var n in finals) Assert.Contains(n, sn.Body);
    }

    [AvaloniaFact]
    public void Roster_and_home_show_the_classes()
    {
        var oldSettings = Hardwood.Desktop.AppSettings.Current; var oldPath = Hardwood.Desktop.AppSettings.FilePath;
        Hardwood.Desktop.AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        Hardwood.Desktop.AppSettings.Use(new Hardwood.Desktop.AppSettings { AutoPortraits = false });
        Hardwood.Desktop.Views.MainWindow? window = null;
        try
        {
            var s = Career("Clemson");
            var vm = new Hardwood.Desktop.ViewModels.MainViewModel();
            window = new Hardwood.Desktop.Views.MainWindow { DataContext = vm, Width = 1480, Height = 900 };
            window.Show();
            vm.AttachSession(s);
            var dir = Environment.GetEnvironmentVariable("HW_SCREENSHOT_DIR");
            void Shot(string file)
            {
                for (int i = 0; i < 3; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
                if (string.IsNullOrEmpty(dir)) return;
                Directory.CreateDirectory(dir);
                using var frame = window.CaptureRenderedFrame();
                frame?.Save(Path.Combine(dir, file));
            }
            vm.Navigate("Home");
            var home = Assert.IsType<Hardwood.Desktop.ViewModels.HomeViewModel>(vm.CurrentPage);
            Assert.Equal(new[] { "Fr", "So", "Jr", "Sr" }, home.Payroll!.Select(b => b.Label).ToArray());
            Assert.Contains("final season", home.PayrollCaption);
            Shot("run127-college-home.png");
            vm.Navigate("Roster");
            var roster = Assert.IsType<Hardwood.Desktop.ViewModels.RosterViewModel>(vm.CurrentPage);
            Assert.True(roster.ShowClassYear);
            Assert.All(roster.Rows, r => Assert.Contains(r.ClassYear, CollegeWorld.ClassShort));
            Assert.Contains(roster.Chips, ch => ch.Label == "Final season");
            Assert.Contains(roster.Rows, r => r.CardFooter == "Senior · final season");
            Shot("run127-college-roster-cards.png");
            roster.CardView = false;
            Shot("run127-college-roster-table.png");
            var grid = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Avalonia.Controls.DataGrid>().First(g => g.Name == "RosterGrid");
            Assert.True(grid.Columns.First(col => col.Header as string == "Yr").IsVisible);
            var senior = s.HumanTeam.Roster.First(p => s.ClassYearOf(p)!.Final);
            vm.ShowPlayer(senior);
            var page = Assert.IsType<Hardwood.Desktop.ViewModels.PlayerViewModel>(vm.CurrentPage);
            Assert.StartsWith("Senior (final season", page.Bio);
            Assert.Contains(page.Chips, ch => ch.Label == "Roster spot");
            Assert.DoesNotContain(page.Chips, ch => ch.Value == "None");
            Shot("run127-college-player.png");
        }
        finally
        {
            window?.Close();
            Hardwood.Desktop.AppSettings.Use(oldSettings); Hardwood.Desktop.AppSettings.FilePath = oldPath;
        }
    }
}
