using Hardwood.Core.College;
using Hardwood.Core.Season;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 125, CS1 (college plan): a class to chase from day one, and classes worth signing.
/// - On a new college career, the computer-made commitments to your program for the senior class are handed back
///   (interest kept, on your board, no offer), so the Recruiting tab has scholarships open from the first day.
/// - The coached program gets the computer schools' baseline for prospects inside its reach (<see cref="CollegeWorld.ReachCredit"/>),
///   so your calls and visits are an edge, not a debt.
/// </summary>
public class Run125RecruitingTests
{
    private readonly ITestOutputHelper _out;
    public Run125RecruitingTests(ITestOutputHelper o) => _out = o;

    private static GameSession Career(string name, ulong seed = 2026)
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(seed, 2026, GameSession.DefaultFullSim);
        int id = world.College!.Programs.First(p => p.FullName.StartsWith(name)).Id;
        return GameSession.NewGame(seed, id, null);
    }

    [Fact]
    public void Day_one_has_open_scholarships_and_the_handed_back_recruits_still_like_you()
    {
        var s = Career("Clemson");
        var c = s.TrailWorld!;
        var me = s.HumanTeam;
        int cls = c.RecruitingClass(s.Today);
        int open = c.OpenScholarships(me, cls);
        var fits = c.Fits(me, cls, null, 60);
        var back = c.ClassOf(cls).Select(p => c.Recruits[p.Id]).Where(r => r.CourtedBy == me.Id && r.OnBoard && r.CommittedTo == null).ToList();
        _out.WriteLine($"{s.Today}: Class of {cls}, {open} open, {fits.Count} best fits, {back.Count} handed back, commits {c.Commits(me, cls)}");
        // 13 scholarships over five age groups: a class is 2-3 places, all of them open on day one (none pre-filled).
        Assert.True(open >= 2, $"only {open} scholarships open on day one");
        Assert.Equal(0, c.Commits(me, cls));
        Assert.True(fits.Count >= 30, $"only {fits.Count} best fits");
        Assert.All(back, r => Assert.True(r.Interest >= CollegeWorld.HandedBackInterest));
        Assert.All(back, r => Assert.False(r.Offered));
        if (back.Count > 0) Assert.Contains(s.Inbox, m => m.Title.Contains("reopened"));
    }

    [Fact]
    public void Hand_back_keeps_commitments_you_won_yourself_and_does_nothing_twice()
    {
        var s = Career("Clemson");
        var c = s.TrailWorld!;
        var me = s.HumanTeam;
        int cls = c.RecruitingClass(s.Today);
        // One prospect you offered and who committed: he stays.
        var p = c.ClassOf(cls).First(x => c.Recruits[x.Id].CommittedTo == null);
        var r = c.Recruits[p.Id];
        CollegeWorld.Courting(r, me); r.Offered = true; r.CommittedTo = me.Id; r.CommittedOn = s.Today;
        Assert.Equal(0, c.HandBackCommitments(me, s.Today));
        Assert.Equal(me.Id, r.CommittedTo);
    }

    [Fact]
    public void Reach_credit_is_the_baseline_inside_the_reach_and_fades_to_nothing_at_twice_it()
    {
        var s = Career("Kentucky");
        var c = s.TrailWorld!;
        var me = s.HumanTeam;
        var ranks = c.PrestigeRanks();
        int reach = CollegeWorld.Reach(ranks[me.Id], c.ReachScale);
        var five = new Recruit { Stars = 5, Rank = 1 };
        Assert.Equal(CollegeWorld.Baseline(five) * CollegeWorld.WeightOf(five), c.ReachCredit(me, five, ranks), 6);
        var far = new Recruit { Stars = 3, Rank = reach * 2 + 1 };
        Assert.Equal(0, c.ReachCredit(me, far, ranks), 6);
        var mid = new Recruit { Stars = 3, Rank = reach + reach / 2 };
        double m = c.ReachCredit(me, mid, ranks);
        Assert.InRange(m, 0.1, CollegeWorld.Baseline(mid) * CollegeWorld.WeightOf(mid) - 0.1);
    }

    [Fact]
    public void A_blue_blood_that_courts_a_five_star_hard_leads_an_equal_rival_that_never_called()
    {
        var s = Career("Kentucky");
        var c = s.TrailWorld!;
        var me = s.HumanTeam;
        int cls = c.RecruitingClass(s.Today);
        var board = c.BoardFor(cls);
        var (p, r) = c.ClassOf(cls).Select(x => (x, c.Recruits[x.Id])).First(x => x.Item2.Stars == 5 && x.Item2.CommittedTo == null);
        CollegeWorld.Courting(r, me); r.Interest = 45; r.Offered = true;
        double ours = c.Appeal(me, p, r, board);
        // The same program as a computer school sees it: no courting term and no credit.
        var (pr, near, min, win) = c.AppealParts(me, p, r, board);
        double asComputer = ours - CollegeWorld.CourtingEdge(r, me.Id, 45) - c.ReachCredit(me, r);
        _out.WriteLine($"ours {ours:F1}, as a computer school {asComputer:F1}");
        Assert.True(ours - asComputer >= 8, $"edge {ours - asComputer:F1}");
        // Zero effort is no worse than a computer school of the same prestige.
        r.Interest = 0;
        Assert.True(c.Appeal(me, p, r, board) >= asComputer - 0.01);
    }

    [AvaloniaFact]
    public void The_recruiting_tab_on_day_one_shows_open_scholarships_and_the_calendar()
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
            vm.Navigate("College");
            var college = Assert.IsType<Hardwood.Desktop.ViewModels.CollegeViewModel>(vm.CurrentPage);
            college.Tab = 5;
            for (int i = 0; i < 3; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
            Assert.True(college.IsCollegeCoach);
            Assert.Contains(college.TrailChips, ch => ch.Label == "Scholarships open" && ch.Value != "0");
            Assert.Contains("open in the Class of 2027", college.TrailNote);
            Assert.Contains("signing day (Apr 15)", college.TrailNote);
            Assert.True(college.TrailRows.Count >= 30);
            _out.WriteLine(college.TrailNote);
            var dir = Environment.GetEnvironmentVariable("HW_SCREENSHOT_DIR");
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
                using var frame = window.CaptureRenderedFrame();
                frame?.Save(Path.Combine(dir, "run125-recruiting-day-one.png"));
            }
        }
        finally
        {
            window?.Close();
            Hardwood.Desktop.AppSettings.Use(oldSettings); Hardwood.Desktop.AppSettings.FilePath = oldPath;
        }
    }
}
