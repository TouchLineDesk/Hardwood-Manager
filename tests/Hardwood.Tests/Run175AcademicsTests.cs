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
/// Run 175, CS7 part 2c (#1121): academics as drama with a consequence, not a lottery. A midterm report names who could
/// fall under 2.0 (nobody else can); study hall gets an at-risk player through for a recruiting hour a week; a player
/// under the mark at the grades sits out (the fall's until Feb 14). The record is in the program's books and survives a
/// reload; no random number is drawn.
/// </summary>
[Collection(SharedUiState.Name)]
public class Run175AcademicsTests
{
    private readonly ITestOutputHelper _out;
    public Run175AcademicsTests(ITestOutputHelper o) => _out = o;

    private static GameSession Career(string name, ulong seed, string? path = null)
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(seed, 2026, GameSession.DefaultFullSim);
        int id = world.College!.Programs.First(p => p.FullName.StartsWith(name)).Id;
        return GameSession.NewGame(seed, id, path);
    }

    [Fact]
    public void Only_a_flagged_player_can_fail_and_study_hall_always_saves_one_at_risk()
    {
        var term = (Fall: true, Year: 2026);
        int flagged = 0, failIgnored = 0, n = 0;
        for (int id = 1; id <= 4000; id++)
            for (int prof = 1; prof <= 20; prof++)
            {
                var p = new Player { Id = id, FirstName = "A", LastName = "B" };
                p.Hidden.Professionalism = prof;
                var b = new ProgramBooks();
                bool fresh = id % 4 == 0;
                double proj = Academics.Projected(p, b, term, fresh), gpa = Academics.Grades(p, b, term, fresh);
                var st = Academics.StandingOf(proj);
                if (st == Academics.Standing.Fine) Assert.True(gpa >= Academics.PassMark, $"{id}/{prof}: fine at {proj:0.00} but {gpa:0.00}");
                if (st == Academics.Standing.AtRisk)
                {
                    b.StudyHall.Add(id);
                    Assert.True(Academics.Grades(p, b, term, fresh) >= Academics.PassMark, $"{id}/{prof}: at risk {proj:0.00} and still failed in study hall");
                }
                // Weight by how common the professionalism is (about N(10.5, 3.2)).
                double w = Math.Exp(-Math.Pow((prof - 10.5) / 3.2, 2) / 2);
                if (id <= 1000) { n++; }
                if (st != Academics.Standing.Fine) flagged += (int)Math.Round(w * 100);
                if (gpa < Academics.PassMark) failIgnored += (int)Math.Round(w * 100);
            }
        // How often it happens: about one player a roster flagged a term, and under one failure a season if all are ignored.
        double total = 0; for (int prof = 1; prof <= 20; prof++) total += Math.Round(Math.Exp(-Math.Pow((prof - 10.5) / 3.2, 2) / 2) * 100) * 4000;
        double flagRate = flagged / total, failRate = failIgnored / total;
        _out.WriteLine($"flagged {flagRate:P1} of player-terms, failing if ignored {failRate:P1} (13 players: {13 * flagRate:0.0} flagged, {13 * failRate:0.00} failing a term)");
        Assert.InRange(flagRate, 0.03, 0.15);
        Assert.InRange(failRate, 0.01, 0.06);
        // The center lifts everyone; it costs booster money and stops at level 3.
        var books = new ProgramBooks { Fund = 5_000_000 };
        Assert.Contains("level 1 is open", Academics.BuildCenter(books));
        Assert.Equal(4_400_000, books.Fund);
        Assert.Contains("level 2", Academics.BuildCenter(books));
        Assert.Contains("level 3", Academics.BuildCenter(books));
        Assert.Contains("already the best", Academics.BuildCenter(books));
        Assert.Null(Academics.TermOf(new DateOnly(2027, 6, 1)));
        Assert.Equal((true, 2026), Academics.TermOf(new DateOnly(2026, 12, 20)));
        Assert.Equal((false, 2026), Academics.TermOf(new DateOnly(2026, 12, 21)));
    }

    [Fact]
    public void A_failing_player_sits_out_after_the_fall_grades_study_hall_saves_another_and_it_survives_a_reload()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hw-run175-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "career.hwsave");
            var s = Career("Providence", 77, path);
            var c = s.World.College!; var t = s.HumanTeam;
            Assert.True(s.HasAcademics);
            var term = Academics.TermOf(s.Today)!.Value;
            // Make two players poor students: one we leave alone, one we put in study hall.
            var ordered = t.Roster.OrderByDescending(p => p.CurrentAbility).ToList();
            var lazy = ordered.Skip(1).First(p => { int keep = p.Hidden.Professionalism; p.Hidden.Professionalism = 1; bool f = s.AcademicOutlook(p)!.Value.Standing == Academics.Standing.Failing; if (!f) p.Hidden.Professionalism = keep; return f; });
            Assert.Equal(Academics.Standing.Failing, s.AcademicOutlook(lazy)!.Value.Standing);
            // One who would fail without help but is only "at risk": study hall gets him through.
            Player? saved = null;
            foreach (var p in ordered.Where(p => p != lazy))
            {
                int keep = p.Hidden.Professionalism;
                bool fresh = c.ClassYearOf(p, s.Today) is { Year: 1 };
                for (int prof = 1; prof <= 20 && saved == null; prof++)
                {
                    p.Hidden.Professionalism = prof;
                    if (s.AcademicOutlook(p) is { Standing: Academics.Standing.AtRisk } && Academics.Grades(p, t.Money.Program, term, fresh) < Academics.PassMark) saved = p;
                }
                if (saved != null) break;
                p.Hidden.Professionalism = keep;
            }
            Assert.NotNull(saved);
            Assert.Equal(Academics.Standing.AtRisk, s.AcademicOutlook(saved)!.Value.Standing);
            int hours = c.HoursLeft(s.Today);
            _out.WriteLine(s.ToggleStudyHall(saved));
            Assert.Equal(hours - Academics.StudyHallHours, c.TrailHours);
            Assert.Equal(Academics.StudyHallHours, c.HoursHeld);
            Assert.Equal(Academics.Standing.Fine, s.AcademicOutlook(saved)!.Value.Standing);

            int guard = 0;
            while (s.Today <= Academics.FallMidterm(2026) && guard++ < 200) s.AdvanceDay();
            var mid = Assert.Single(s.Inbox, m => m.Title.StartsWith("Midterm grades"));
            _out.WriteLine($"{mid.Date}: {mid.Title}\n{mid.Body}");
            Assert.Contains(lazy.Name, mid.Body);
            Assert.Contains("failing", mid.Body);
            Assert.DoesNotContain(saved.Name, mid.Body);

            while (s.Today <= Academics.FallGrades(2026) && guard++ < 400) s.AdvanceDay();
            var grades = Assert.Single(s.Inbox, m => m.Title.StartsWith("Grades:"));
            _out.WriteLine($"{grades.Date}: {grades.Title}\n{grades.Body}");
            Assert.Contains(lazy.Name, grades.Title);
            Assert.DoesNotContain(saved.Name, grades.Title);
            Assert.Contains("study hall got 1 player through", grades.Body);
            Assert.Equal(Academics.FallBack(2026), lazy.IneligibleUntil);
            Assert.False(lazy.IsAvailable(s.Today));
            Assert.True(saved.IsAvailable(s.Today) || saved.IsHurt(s.Today));
            Assert.Empty(t.Money.Program.StudyHall);
            Assert.Contains(Hardwood.Core.People.PeopleEngine.Factors(lazy, t, 0.5, null, s.League.Finance, s.Today, college: true), f => f.reason.StartsWith("Ineligible"));
            Assert.StartsWith("Ineligible", lazy.OutShort(s.Today));

            // He doesn't play while ineligible, and the record comes back with the save.
            int gamesBefore = s.MyStats.TryGetValue(lazy.Id, out var st0) ? st0.Games : 0;
            int myGames = 0;
            while (s.Today < new DateOnly(2027, 1, 20) && guard++ < 600)
            {
                int before = c.Season.Games.Count(g => g.Played && (g.HomeId == t.Id || g.AwayId == t.Id));
                s.AdvanceDay();
                myGames += c.Season.Games.Count(g => g.Played && (g.HomeId == t.Id || g.AwayId == t.Id)) - before;
            }
            int gamesAfter = s.MyStats.TryGetValue(lazy.Id, out var st1) ? st1.Games : 0;
            _out.WriteLine($"{lazy.Name}: {gamesBefore} → {gamesAfter} games while {t.FullName} played {myGames}");
            Assert.True(myGames >= 4);
            Assert.Equal(gamesBefore, gamesAfter);
            s.SaveNow();
            var back = GameSession.Load(path);
            var lazyBack = back.HumanTeam.Roster.First(p => p.Id == lazy.Id);
            Assert.Equal(Academics.FallBack(2026), lazyBack.IneligibleUntil);
            Assert.False(lazyBack.IsAvailable(back.Today));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void Only_the_program_you_coach_keeps_an_academic_list()
    {
        // Review: a program you've left (now the computer's) must not keep sitting a player out for his grades.
        var s = Career("Providence", 77);
        var c = s.World.College!;
        var other = c.Programs.First(t => t.Id != s.HumanTeam.Id && t.Roster.Count > 0);
        var p = other.Roster[0];
        other.Money.Program.Ineligible[p.Id] = new DateOnly(2027, 2, 14);
        other.Money.Program.StudyHall.Add(p.Id);
        s.SyncAcademics();
        Assert.Empty(other.Money.Program.Ineligible);
        Assert.Empty(other.Money.Program.StudyHall);
        Assert.Null(p.IneligibleUntil);
        Assert.True(p.IsAvailable(new DateOnly(2027, 1, 10)) || p.IsHurt(new DateOnly(2027, 1, 10)));
        // Your own program's list is applied.
        var mine = s.HumanTeam.Roster[0];
        s.HumanTeam.Money.Program.Ineligible[mine.Id] = new DateOnly(2027, 2, 14);
        s.SyncAcademics();
        Assert.Equal(new DateOnly(2027, 2, 14), mine.IneligibleUntil);
    }

    [Fact]
    public void A_pro_club_has_no_academics()
    {
        var s = GameSession.NewGame(4, 3, null);
        Assert.False(s.HasAcademics);
        Assert.Empty(s.AcademicRoster());
        Assert.StartsWith("Only a college program", s.BuildAcademicCenter());
    }

    [AvaloniaFact]
    public void The_board_shows_the_academics_panel_and_study_hall_works_from_it()
    {
        var oldSettings = Hardwood.Desktop.AppSettings.Current; var oldPath = Hardwood.Desktop.AppSettings.FilePath;
        Hardwood.Desktop.AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        Hardwood.Desktop.AppSettings.Use(new Hardwood.Desktop.AppSettings { AutoPortraits = false });
        Hardwood.Desktop.Views.MainWindow? window = null;
        try
        {
            var s = Career("Providence", 77);
            var t = s.HumanTeam;
            var lazy = t.Roster.OrderByDescending(p => p.CurrentAbility).ElementAt(2);
            lazy.Hidden.Professionalism = 3;
            var vm = new MainViewModel();
            window = new Hardwood.Desktop.Views.MainWindow { DataContext = vm, Width = 1280, Height = 800 };
            window.Show();
            vm.AttachSession(s);
            vm.Navigate("Board");
            var board = Assert.IsType<BoardViewModel>(vm.CurrentPage);
            void Settle() { for (int i = 0; i < 3; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window!.UpdateLayout(); } }
            Settle();
            Assert.True(board.HasAcademics);
            Assert.Equal(new[] { "Term", "At risk", "Ineligible", "Study hall", "Academic center" }, board.AcademicChips.Select(c => c.Label));
            _out.WriteLine(board.AcademicSummary);
            foreach (var r in board.AcademicRows) _out.WriteLine($"  {r.Name} · {r.Detail} · {r.Standing} [{r.ButtonText}]");
            var row = board.AcademicRows.First(r => r.Name.StartsWith(lazy.Name));
            Assert.NotEqual("on track", row.Standing);
            row.Toggle.Execute(null); Settle();
            _out.WriteLine(board.AcademicStatus);
            Assert.Contains("in study hall", board.AcademicStatus);
            Assert.Contains(board.AcademicRows, r => r.Name.StartsWith(lazy.Name) && r.ButtonText == "Out of study hall");
            Assert.True(board.CanBuildCenter, board.BuildCenterTip);
            var title = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Avalonia.Controls.TextBlock>().First(x => x.Text == "Academics");
            var scroll = Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<Avalonia.Controls.ScrollViewer>(title)!;
            if (title.TranslatePoint(new Avalonia.Point(0, 0), scroll) is { } at) scroll.Offset = new Avalonia.Vector(0, scroll.Offset.Y + at.Y - 20);
            Settle();
            Assert.Empty(LayoutCheck.Scan(window));
            if (Environment.GetEnvironmentVariable("HW_SCREENSHOT_DIR") is { Length: > 0 } dir)
            {
                Directory.CreateDirectory(dir);
                window.CaptureRenderedFrame()?.Dispose(); Settle(); // headless frames can lag one behind a scroll
                using var frame = window.CaptureRenderedFrame();
                frame?.Save(Path.Combine(dir, "run175-academics.png"));
            }
        }
        finally
        {
            window?.Close();
            Hardwood.Desktop.AppSettings.Use(oldSettings); Hardwood.Desktop.AppSettings.FilePath = oldPath;
        }
    }
}
