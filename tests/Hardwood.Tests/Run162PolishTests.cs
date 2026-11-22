using Avalonia.Headless.XUnit;
using Hardwood.Core.Engine;
using Hardwood.Core.Model;
using Hardwood.Core.People;
using Hardwood.Core.Season;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 162, the polish list.
/// - Danny's note danny-no-college-transfers-nov22: a college player plays where he starts. He never asks out, so nothing
///   says "thinking of transferring"; an unhappy player stays unhappy (and asks to talk). Old saves are cleared on load.
/// - K22: on graduation day Home's "Roster by class" shows next season's classes, as the inbox message does.
/// - K24: labels that say who: a college leader's school (not "Wildcats"), "vs" for a neutral-court box score, the
///   National Player of the Year named once.
/// </summary>
[Collection(SharedUiState.Name)]
public class Run162PolishTests
{
    private readonly ITestOutputHelper _out;
    public Run162PolishTests(ITestOutputHelper o) => _out = o;

    private static (Team team, Player proud) UnhappyTeam()
    {
        var team = new Team { Id = 7, City = "Clemson", Name = "Tigers", Abbreviation = "CLEM", Division = "ACC" };
        var proud = new Player { Id = 70, FirstName = "John", LastName = "Mercer", TeamId = 7, Morale = 20 };
        proud.Hidden.Ego = 18; proud.Hidden.Ambition = 15;
        team.Roster.Add(proud);
        for (int i = 1; i < 9; i++) team.Roster.Add(new Player { Id = 70 + i, FirstName = "P", LastName = $"L{i}", TeamId = 7, Morale = 60 });
        return (team, proud);
    }

    private static List<PeopleEvent> Weeks(Team team, bool allowRequests, int weeks)
    {
        var events = new List<PeopleEvent>();
        var finance = new Finance(140_000_000, 1_100_000, 15, 13, 10_000_000);
        var day = new DateOnly(2026, 11, 2);
        for (int w = 0; w < weeks; w++, day = day.AddDays(7))
        {
            team.Roster[0].Morale = Math.Min(team.Roster[0].Morale, 20); // keep him miserable
            events.AddRange(PeopleEngine.Weekly(new[] { team }, finance, 1, id => id == team.Id ? team : null, day, _ => 0.5,
                new Dictionary<int, PlayerSeasonStats>(), team.Id, new List<Promise>(), allowRequests));
        }
        return events;
    }

    [Fact]
    public void A_college_player_never_asks_out_but_a_pro_still_does()
    {
        var (college, proud) = UnhappyTeam();
        var ev = Weeks(college, allowRequests: false, weeks: 12);
        Assert.False(proud.TradeRequested);
        Assert.DoesNotContain(ev, e => e.Kind == PeopleEventKind.TradeRequest);
        Assert.Contains(ev, e => e.Kind == PeopleEventKind.WantsTalk && e.Player == proud); // he says he's unhappy instead
        Assert.True(proud.Morale < 38);                                                    // and it still hurts

        // An old flag on a college player is dropped at the next weekly update.
        proud.TradeRequested = true;
        Weeks(college, allowRequests: false, weeks: 1);
        Assert.False(proud.TradeRequested);

        var (pro, star) = UnhappyTeam();
        var proEv = Weeks(pro, allowRequests: true, weeks: 12);
        Assert.Contains(proEv, e => e.Kind == PeopleEventKind.TradeRequest && e.Player == star); // the pros are unchanged
    }

    [Fact]
    public void Old_saves_lose_a_college_players_transfer_flag()
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026, 2026, GameSession.DefaultFullSim);
        int id = world.College!.Programs.First(p => p.FullName.StartsWith("Clemson")).Id;
        var s = GameSession.NewGame(2026, id, null);
        var p = s.HumanTeam.Roster[0];
        p.TradeRequested = true;
        s.Promises.Add(new Promise { PlayerId = p.Id, TeamId = s.HumanTeam.Id, Made = s.Today, Kind = "trade" });
        var pro = s.League.Teams[0].Roster[0];
        pro.TradeRequested = true;
        Assert.Equal(1, s.ClearCollegeRequests());
        Assert.False(p.TradeRequested);
        Assert.Empty(s.Promises);
        Assert.True(pro.TradeRequested); // a pro's request stands
    }

    [Fact]
    public void A_neutral_court_box_score_reads_vs()
    {
        var a = new Team { City = "Miami (FL)", Name = "Hurricanes", Abbreviation = "MIA", Division = "ACC" };
        var b = new Team { City = "Creighton", Name = "Bluejays", Abbreviation = "CREI", Division = "Big East" };
        var r = new GameResult { Home = new TeamBox { Team = b, FixedPoints = 71 }, Away = new TeamBox { Team = a, FixedPoints = 81 } };
        Assert.Equal("MIA 81 @ CREI 71", r.Summary());
        r.Neutral = true;
        Assert.Equal("MIA 81 vs CREI 71", r.Summary());
    }

    [Fact]
    public void A_college_leader_is_named_by_his_school()
    {
        var t = new Team { City = "North Texas", Name = "Mean Green", Abbreviation = "UNT", Division = "AAC" };
        var entries = new[] { new LeaderEntry { Rank = 1, Name = "Henry Bowen", Team = t, Value = "22.1" } };
        Assert.Equal("North Texas", new LeaderCard { Entries = entries, College = true }.TopClub);
        Assert.Equal("Mean Green", new LeaderCard { Entries = entries }.TopClub); // the pros keep the nickname rule (H87)
    }

    [AvaloniaFact]
    public void On_graduation_day_home_and_the_inbox_count_the_same_classes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run162-{Guid.NewGuid():N}.save");
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
            Assert.True(s.InCollege);
            int guard = 0;
            while (!s.Inbox.Any(m => m.Title == "Graduation and enrollment") && guard++ < 330) s.AdvanceDay();
            var msg = s.Inbox.Last(m => m.Title == "Graduation and enrollment");
            _out.WriteLine($"{s.Today:MMM d yyyy}, {s.Phase}: {s.ClassCounts()}");
            Assert.Equal(SessionPhase.Offseason, s.Phase);
            vm.AfterTick();
            vm.Navigate("Home");
            var home = (HomeViewModel)vm.CurrentPage!;
            Assert.Equal("Roster by class", home.PayrollTitle);
            string chart = string.Join(", ", home.Payroll!.Select(b => $"{b.ValueText} {b.Label}"));
            _out.WriteLine($"Home: {chart} | {home.PayrollCaption}");
            Assert.Contains($"Next season by class: {chart}.", msg.Body);
            Assert.StartsWith("Next season (", home.PayrollCaption);
            Assert.DoesNotContain(s.HumanTeam.Roster, p => p.TradeRequested);
        }
        finally
        {
            window?.Close();
            AppSettings.Use(oldSettings); AppSettings.FilePath = oldPath;
            try { File.Delete(path); } catch { }
        }
    }
}
