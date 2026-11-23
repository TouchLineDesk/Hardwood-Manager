using Hardwood.Core.Jobs;
using Hardwood.Core.Season;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Avalonia.Headless.XUnit;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 172 (polish, college findings):
/// - H179: an ambitious AD at a program far from the bubble asks for progress, not March Madness and 60% of the schedule.
/// - H180: after a summer hire, the Board doesn't judge the new coach on the season before he came, and once his own
///   season starts the line is his.
/// - H181: on graduation day the Graduating chip counts next season's seniors, as Home's chart does.
/// - H169: the AD's bars say "of 20" with one word each, from the same lines as the style and the "impatient" sentence.
/// </summary>
[Collection(SharedUiState.Name)]
public class Run172PolishTests
{
    private readonly ITestOutputHelper _out;
    public Run172PolishTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void An_ambitious_ad_asks_a_bottom_program_for_progress_and_a_contender_for_the_tournament()
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026, 2026, GameSession.DefaultFullSim);
        var s = GameSession.NewGame(2026, world.College!.Programs.First(p => p.FullName.StartsWith("Sacred Heart")).Id, null);
        var bottom = s.HumanTeam;
        var ctx = s.ContextOf(bottom);
        bottom.Owner.Patience = 10; bottom.Owner.Ambition = 10;
        var plain = Board.SetExpectations(ctx, bottom, s.League.Season);
        bottom.Owner.Ambition = 15;
        var ambitious = Board.SetExpectations(ctx, bottom, s.League.Season);
        _out.WriteLine($"{bottom.FullName}: plain {plain.Goal} {plain.TargetWins}, ambitious {ambitious.Goal} {ambitious.TargetWins} of {ctx.SeasonGames}");
        Assert.Equal(SeasonGoal.Develop, ambitious.Goal);
        Assert.True(ambitious.TargetWins > plain.TargetWins, "an ambitious AD still asks for more");
        Assert.True(ambitious.TargetWins <= Math.Max(plain.TargetWins, (int)Math.Round(ctx.SeasonGames * 0.5)));
        Assert.True(ambitious.TargetWins <= plain.TargetWins + Board.AmbitiousExtraWins);

        // A program inside the reach still gets the tournament from an ambitious AD (#168).
        var mid = ctx.Teams.Where(t => t != bottom)
            .Select(t => (t, r: Board.CollegeRank(ctx, t, Board.ProjectedRank(ctx, t))))
            .First(x => x.r is > 50 and <= 100).t;
        mid.Owner.Patience = 10; mid.Owner.Ambition = 15;
        var e = Board.SetExpectations(ctx, mid, s.League.Season);
        _out.WriteLine($"{mid.FullName}: {e.Goal} {e.TargetWins}");
        Assert.Equal(SeasonGoal.Playoffs, e.Goal);
    }

    [Fact]
    public void The_owner_words_come_from_one_set_of_lines()
    {
        var o = new Owner { Patience = 7, Ambition = 7 };
        Assert.Equal("Reasonable", o.Style);
        Assert.Equal("average", o.PatienceWord);
        Assert.False(o.Impatient); // was "An impatient athletic director" at 7 beside "Reasonable" (H169)
        o.Patience = 6;
        Assert.Equal("Impatient", o.Style);
        Assert.Equal("impatient", o.PatienceWord);
        Assert.True(o.Impatient);
        o = new Owner { Patience = 11, Ambition = 15 };
        Assert.Equal("Wants to win now", o.Style);
        Assert.Equal("wants to win now", o.AmbitionWord);
        o = new Owner { Patience = 15, Ambition = 5 };
        Assert.Equal("Patient and careful with money", o.Style);
        Assert.Equal("patient", o.PatienceWord);
        Assert.Equal("careful", o.AmbitionWord);
    }

    [AvaloniaFact]
    public void After_graduation_and_a_summer_hire_the_board_counts_next_seasons_seniors_and_judges_only_your_games()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run172-{Guid.NewGuid():N}.save");
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { FaceFolder = oldSettings.FaceFolder, LogoFolder = oldSettings.LogoFolder, AutoPortraits = false });
        MainWindow? window = null;
        try
        {
            var vm = new MainViewModel();
            window = new MainWindow { DataContext = vm, Width = 1480, Height = 900 };
            window.Show();
            var start = (StartViewModel)vm.CurrentPage!;
            start.SavePath = path; start.SeedText = "2026";
            start.PlayLeague = "College basketball";
            start.SelectedTeam = start.Teams.First(t => t.Team.City == "Miami (FL)");
            start.NewGame();
            var s = vm.Session!;
            s.Delegated.Recruiting = true;
            int guard = 0;
            while (!s.Inbox.Any(m => m.Title == "Graduation and enrollment") && guard++ < 330) s.AdvanceDay();
            Assert.Equal(SessionPhase.Offseason, s.Phase);
            vm.AfterTick();

            // H181: the chip and Home's chart agree.
            vm.Navigate("Home");
            var home = (HomeViewModel)vm.CurrentPage!;
            string homeSeniors = home.Payroll!.Last().ValueText;
            vm.Navigate("Board");
            var board = (BoardViewModel)vm.CurrentPage!;
            var chip = Assert.Single(board.FinanceChips, c => c.Label == "Graduating");
            _out.WriteLine($"{s.Today:MMM d yyyy}: Home seniors {homeSeniors}; chip {chip.Value} · {chip.Sub}");
            Assert.Equal(homeSeniors, chip.Value);
            Assert.Equal("next season's seniors", chip.Sub);
            // H169: the bars say what they are out of.
            Assert.Matches(@"^\d+ of 20 · ", board.PatienceText);
            Assert.Matches(@"^\d+ of 20 · ", board.AmbitionText);

            // H180: a summer hire at another program.
            var target = s.ContextOf(s.HumanTeam).Teams.First(t => t != s.HumanTeam && s.CollegeRegularRecord(t).Games > 20);
            s.Job.Vacancies.Add(new Vacancy { TeamId = target.Id, Opened = s.Today, Status = "offered" });
            Assert.Null(s.AcceptOffer(target.Id));
            Assert.Equal(target, s.HumanTeam);
            board.Refresh();
            _out.WriteLine($"Hired {target.FullName}: {board.Pace} | {board.ConfidenceHeading}");
            Assert.StartsWith("Last season, before you came", board.Pace);
            Assert.Contains("starts judging the season after five games", board.ConfidenceHeading);
            Assert.Empty(board.ConfidenceDrivers);

            // Once his own season has started, the line and the judging are his.
            guard = 0;
            while (s.Phase == SessionPhase.Offseason && guard++ < 520) s.AdvanceOffseason();
            guard = 0;
            while (s.MyBoardRecord.Games < 6 && guard++ < 200) s.AdvanceDay();
            Assert.True(s.MyBoardRecord.Games >= 6, "the new season's games were played");
            board.Refresh();
            _out.WriteLine($"{s.Today:MMM d yyyy}: {board.Pace} | {board.ConfidenceHeading}");
            _out.WriteLine($"phase {s.Phase}, league season {s.League.Season}, hired season {s.Job.HiredSeason} on {s.Job.HiredOn}, college year {s.World.College!.Season.Year}, record {s.MyBoardRecord.Wins}-{s.MyBoardRecord.Losses}, team {s.HumanTeam.FullName}");
            Assert.StartsWith("Regular season ", board.Pace);
            Assert.DoesNotContain("before you came", board.Pace);
            Assert.StartsWith("Heading toward", board.ConfidenceHeading);
        }
        finally
        {
            window?.Close();
            AppSettings.Use(oldSettings); AppSettings.FilePath = oldPath;
            try { File.Delete(path); } catch { }
        }
    }
}
