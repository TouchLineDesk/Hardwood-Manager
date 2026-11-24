using Avalonia.Headless.XUnit;
using Hardwood.Core;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.Presentation;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 183: the launch list's small items (hunt 25 and the launch plan).
/// - The assistant's game-day card ("Next up: vs X") goes while the live game is on, final included.
/// - K25's leftover: GameCalls' milestone and foul lines say "J. Smith" when two players share a surname.
/// - H190: no generated player is called "Brock Brock".
/// - H196: Home's conference snapshot marks the rows it leaves out.
/// - H199/H191: the Board's program tiles and lines.
/// </summary>
[Collection(SharedUiState.Name)]
public class Run183LaunchListTests
{
    private readonly ITestOutputHelper _out;
    public Run183LaunchListTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void H190_no_generated_player_has_the_same_first_and_last_name()
    {
        var gen = new PlayerGenerator(new Rng(183), 7_000_000);
        var today = new DateOnly(2026, 10, 1);
        int same = 0;
        for (int i = 0; i < 20_000; i++)
        {
            var p = gen.Generate((Position)(i % 5), 40, 18, today);
            if (NamePool.SameName(p.FirstName, p.LastName)) { same++; _out.WriteLine(p.Name); }
        }
        Assert.Equal(0, same);
        Assert.True(NamePool.SameName("Brock", "brock"));
        Assert.False(NamePool.SameName("Brock", "Brooks"));
    }

    [Fact]
    public void GameCalls_say_a_first_initial_for_a_shared_surname()
    {
        Player P(string f, string l) => new() { FirstName = f, LastName = l };
        var john = P("John", "Smith"); var kevin = P("Kevin", "Smith"); var lee = P("Lee", "Jones");
        var shared = GameCalls.SharedSurnames(new[] { john, kevin, lee, john });
        Assert.Equal(new[] { "Smith" }, shared);
        Assert.Equal("J. Smith", GameCalls.CallName(john, shared));
        Assert.Equal("K. Smith", GameCalls.CallName(kevin, shared));
        Assert.Equal("Jones", GameCalls.CallName(lee, shared));
    }

    private static void WithCollege(string seed, Action<MainViewModel> body)
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-183-{Guid.NewGuid():N}.save");
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { FaceFolder = oldSettings.FaceFolder, LogoFolder = oldSettings.LogoFolder, AutoPortraits = false, StudioShow = false });
        try
        {
            var vm = new MainViewModel();
            var start = (StartViewModel)vm.CurrentPage!;
            start.SavePath = path; start.SeedText = seed;
            start.PlayLeague = StartViewModel.CollegeChoice;
            start.SelectedTeam = start.Teams[1];
            start.NewGame();
            body(vm);
        }
        finally
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            if (File.Exists(AppSettings.FilePath)) File.Delete(AppSettings.FilePath);
            AppSettings.FilePath = oldPath; AppSettings.Use(oldSettings);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [AvaloniaFact]
    public void The_game_day_card_goes_while_the_live_game_is_on()
    {
        WithCollege("9", vm =>
        {
            var session = vm.Session!;
            while (!session.HumanPlaysToday) session.AdvanceDay();
            vm.Guide.Refresh();
            var before = vm.Guide.Tasks.Select(t => t.Task.Title).ToList();
            _out.WriteLine("before: " + string.Join(" | ", before));
            Assert.Contains(before, t => t.StartsWith("Next up:", StringComparison.Ordinal));
            var (game, played) = session.BeginLiveDay();
            var live = new LiveGameViewModel(vm, game!, played);
            Assert.DoesNotContain(vm.Guide.Tasks, t => t.Task.Title.StartsWith("Next up:", StringComparison.Ordinal));
            live.SimToEnd();
            Assert.True(live.Complete);
            vm.Guide.Refresh(); // the final: still no card about tonight's game
            Assert.DoesNotContain(vm.Guide.Tasks, t => t.Task.Title.StartsWith("Next up:", StringComparison.Ordinal));
            Assert.StartsWith("Final:", vm.Guide.Headline);
        });
    }

    [AvaloniaFact]
    public void H196_Home_marks_the_rows_the_conference_snapshot_leaves_out()
    {
        WithCollege("77", vm =>
        {
            vm.Navigate("Home");
            var home = (HomeViewModel)vm.CurrentPage!;
            var conf = vm.Session!.MyTable;
            var rows = home.Table.ToList();
            _out.WriteLine(string.Join(" | ", rows.Select(r => $"{r.RankText} {r.Name}")));
            int shown = rows.Count(r => !r.Gap);
            Assert.Equal(conf.Count, shown + rows.Where(r => r.Gap).Sum(r => int.Parse(r.Name.Split(' ')[1])));
            if (conf.Count > 8) Assert.Contains(rows, r => r.Gap);
            // no rank ever jumps without a marker between
            for (int i = 1; i < rows.Count; i++)
                if (!rows[i].Gap && !rows[i - 1].Gap) Assert.Equal(rows[i - 1].Rank + 1, rows[i].Rank);
        });
    }

    [AvaloniaFact]
    public void H199_the_Board_program_tiles_read_cleanly()
    {
        WithCollege("77", vm =>
        {
            vm.Navigate("Board");
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var board = (BoardViewModel)vm.CurrentPage!;
            var boosters = board.ProgramBuildChips.Single(c => c.Label == "Boosters");
            Assert.True(char.IsUpper(boosters.Value[0]), boosters.Value);
            Assert.DoesNotContain("A project paid for", board.ProgramLastSummer);
            _out.WriteLine(board.ProgramLastSummer);
            if (board.HasAcademics)
                Assert.DoesNotContain(" h a week", board.AcademicChips.Single(c => c.Label == "Study hall").Sub);
        });
    }
}
