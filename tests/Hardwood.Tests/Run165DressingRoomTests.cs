using Avalonia.Headless;
using Avalonia.Headless.XUnit;
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
/// Run 165, K9: the dressing room. A college player can't leave (Danny, no transfers), so unhappiness on a winning team
/// has to be real and cost something:
/// - winning lifts a player only as far as he plays a part in it (<see cref="PeopleEngine.Involvement"/>), and a senior
///   weighs his minutes more than a freshman (<see cref="PeopleEngine.ClassWeight"/>);
/// - every unhappy college player pulls the chemistry target down, by name;
/// - a restless dressing room (two or more of the rotation unhappy) is news, with who, why and what it costs;
/// - a new college talk, "His time will come", works on a patient underclassman and backfires on a senior.
/// The pros are unchanged.
/// </summary>
[Collection(SharedUiState.Name)]
public class Run165DressingRoomTests
{
    private readonly ITestOutputHelper _out;
    public Run165DressingRoomTests(ITestOutputHelper o) => _out = o;

    private static readonly Finance Money = new(140_000_000, 1_100_000, 15, 13, 10_000_000);
    private static readonly DateOnly Day = new(2026, 12, 7); // the 2026-27 season: seniors were born in 2005, freshmen in 2008

    /// <summary>Ten players, CA 90 down to 72; the sixth-best (who expects about 22 minutes) is the one we watch.</summary>
    private static (Team team, Player sixth) Program(int birthYear)
    {
        var team = new Team { Id = 9, City = "Wake Forest", Name = "Demon Deacons", Abbreviation = "WAKE", Division = "ACC", Chemistry = 60 };
        for (int i = 0; i < 10; i++)
            team.Roster.Add(new Player { Id = 90 + i, FirstName = "P", LastName = $"L{i}", TeamId = 9, CurrentAbility = 90 - 2 * i, Morale = 60,
                                         BirthDate = new DateOnly(i == 5 ? birthYear : 2006, 3, 1) });
        return (team, team.Roster[5]);
    }

    private static PlayerSeasonStats Mins(Player p, double mpg, int games = 10) => new() { PlayerId = p.Id, TeamId = 9, Games = games, Minutes = mpg * games };

    [Fact]
    public void Winning_no_longer_hides_a_senior_stuck_on_the_bench()
    {
        var (team, senior) = Program(2005);
        var st = Mins(senior, 8);
        Assert.Equal(1.4, PeopleEngine.ClassWeight(senior, Day));
        Assert.Equal(22, PeopleEngine.ExpectedMinutes(senior, team));
        double college = PeopleEngine.Target(PeopleEngine.Factors(senior, team, 0.8, st, Money, Day, college: true));
        double pro = PeopleEngine.Target(PeopleEngine.Factors(senior, team, 0.8, st, Money, Day));
        var (team2, freshman) = Program(2008);
        double fresh = PeopleEngine.Target(PeopleEngine.Factors(freshman, team2, 0.8, Mins(freshman, 8), Money, Day, college: true));
        _out.WriteLine($"8 mpg on an .800 team: college senior {college:0.0}, college freshman {fresh:0.0}, pro {pro:0.0}");
        var why = PeopleEngine.Factors(senior, team, 0.8, st, Money, Day, college: true);
        _out.WriteLine(string.Join("; ", why.Select(x => $"{x.reason} {x.effect:+0.0;-0.0}")));
        Assert.True(college < PeopleEngine.UnhappyLine, $"senior target {college:0.0}");
        Assert.True(pro > college + 12, $"pro target {pro:0.0} (the pros keep the whole lift)");
        Assert.True(fresh > college + 10, "a freshman waits his turn more easily");
        Assert.Contains(why, x => x.reason.StartsWith("A senior who wants his turn (8 a game; expects about 22)"));
        Assert.Contains(why, x => x.reason == "Winning, but he isn't part of it" && x.effect > 0 && x.effect < 6);
        // A starter playing his minutes enjoys the whole lift.
        var star = team.Roster[0];
        Assert.Equal(1, PeopleEngine.Involvement(star, team, Mins(star, 34)));
        Assert.Contains(PeopleEngine.Factors(star, team, 0.8, Mins(star, 34), Money, Day, college: true), x => x.reason == "Enjoying winning" && x.effect > 10);
    }

    [Fact]
    public void Unhappy_college_players_cost_the_locker_room_by_name()
    {
        var (team, sixth) = Program(2005);
        sixth.Morale = 30;
        var one = PeopleEngine.ChemistryFactors(team, college: true);
        Assert.Contains(one, x => x.reason == "P L5 is sulking" && x.effect == -PeopleEngine.SulkCost);
        team.Roster[6].Morale = 25; team.Roster[7].Morale = 20;
        var three = PeopleEngine.ChemistryFactors(team, college: true);
        Assert.Contains(three, x => x.reason == "3 unhappy players in the locker room" && x.effect == -3 * PeopleEngine.SulkCost);
        Assert.DoesNotContain(PeopleEngine.ChemistryFactors(team), x => x.reason.Contains("sulking") || x.reason.Contains("locker room")); // the pros unchanged
    }

    [Fact]
    public void A_restless_dressing_room_is_news_on_a_winning_college_team()
    {
        var (team, _) = Program(2005);
        // The sixth to ninth men are seniors playing 6 minutes a game for a team that wins 80%.
        var stats = new Dictionary<int, PlayerSeasonStats>();
        for (int i = 0; i < 10; i++)
        {
            var p = team.Roster[i];
            if (i is >= 5 and <= 8) p = team.Roster[i] = new Player { Id = p.Id, FirstName = p.FirstName, LastName = p.LastName, TeamId = 9, CurrentAbility = p.CurrentAbility, Morale = 60, BirthDate = new DateOnly(2005, 3, 1) };
            stats[p.Id] = Mins(p, i < 5 ? 32 : 6);
        }
        var events = new List<PeopleEvent>();
        var proTeam = Program(2005).team;
        var proEvents = new List<PeopleEvent>();
        var day = new DateOnly(2026, 11, 2);
        for (int w = 0; w < 10; w++, day = day.AddDays(7))
        {
            events.AddRange(PeopleEngine.Weekly(new[] { team }, Money, 1, id => team, day, _ => 0.8, stats, team.Id, new List<Promise>(), allowRequests: false, college: true));
            proEvents.AddRange(PeopleEngine.Weekly(new[] { proTeam }, Money, 1, id => proTeam, day, _ => 0.8, stats, proTeam.Id, new List<Promise>()));
        }
        foreach (var e in events) _out.WriteLine($"{e.Kind}: {e.Text}");
        _out.WriteLine($"college chemistry {team.Chemistry:0}, pro chemistry {proTeam.Chemistry:0}; unhappy {PeopleEngine.Sulking(team).Count}");
        var rooms = events.Where(e => e.Kind == PeopleEventKind.DressingRoom).ToList();
        Assert.InRange(rooms.Count, 2, 3); // about once in four weeks, not every week
        var room = rooms[0];
        Assert.Contains("Winning hasn't fixed it", room.Text);
        Assert.Contains("P L5 (a senior who wants his turn) and P L6 (a senior who wants his turn) are unhappy.", room.Text);
        Assert.Contains("an unhappy player plays below himself on the floor", room.Text);
        Assert.DoesNotContain(events, e => e.Kind == PeopleEventKind.TradeRequest);
        Assert.DoesNotContain(proEvents, e => e.Kind == PeopleEventKind.DressingRoom);
        Assert.True(team.Chemistry < proTeam.Chemistry - 5, "the mood costs a college room more");
        Assert.True(PeopleEngine.DressingRoomNote(Program(2005).team, 0.8, _ => null, Money, day) == null); // a calm room says nothing
    }

    [Fact]
    public void His_time_will_come_works_on_a_patient_underclassman_not_a_senior()
    {
        var fresh = new Player { Id = 1, FirstName = "Ty", LastName = "A", BirthDate = new DateOnly(2008, 3, 1) };
        fresh.Hidden.Professionalism = 14;
        var brash = new Player { Id = 2, FirstName = "Jo", LastName = "B", BirthDate = new DateOnly(2008, 3, 1) };
        brash.Hidden.Professionalism = 6; brash.Hidden.Loyalty = 6;
        var junior = new Player { Id = 3, FirstName = "Al", LastName = "C", BirthDate = new DateOnly(2006, 3, 1) };
        junior.Hidden.Loyalty = 15;
        var senior = new Player { Id = 4, FirstName = "Ed", LastName = "D", BirthDate = new DateOnly(2005, 3, 1) };
        senior.Hidden.Loyalty = 20;
        Assert.Equal((8.0, true), (Talks.Patience(fresh, Day).MoraleChange, Talks.Patience(fresh, Day).Positive));
        Assert.False(Talks.Patience(brash, Day).Positive);
        Assert.True(Talks.Patience(junior, Day).Positive);
        var no = Talks.Patience(senior, Day);
        Assert.Equal(-6, no.MoraleChange);
        Assert.Contains("this is my last year", no.Reply);
        Assert.Contains(TalkTopic.AskForPatience, Talks.Available(fresh, college: true));
        Assert.DoesNotContain(TalkTopic.AskForPatience, Talks.Available(fresh));
        Assert.Equal("His time will come", Talks.Label(TalkTopic.AskForPatience));
    }

    [AvaloniaFact]
    public void The_squad_page_shows_the_sulking_player_and_the_new_talk()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run165-{Guid.NewGuid():N}.save");
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
            start.SavePath = path; start.SeedText = "77";
            start.PlayLeague = "College basketball";
            start.SelectedTeam = start.Teams.First(t => t.Team.City == "Providence");
            start.NewGame();
            var s = vm.Session!;
            Assert.True(s.InCollege);
            var sulker = s.HumanTeam.Roster.OrderBy(p => p.CurrentAbility).First();
            sulker.Morale = 25;
            vm.AfterTick();
            vm.Navigate("Squad");
            var squad = (SquadViewModel)vm.CurrentPage!;
            Assert.Contains(squad.ChemistryFactors, f => f.Reason == $"{sulker.Name} is sulking");
            squad.Selected = squad.Players.First(r => r.Player == sulker);
            Assert.Contains(squad.Topics, t => t.Label == "His time will come");
            for (int i = 0; i < 3; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
            var shots = Path.Combine(AppContext.BaseDirectory, "screenshots"); Directory.CreateDirectory(shots);
            window.CaptureRenderedFrame()?.Save(Path.Combine(shots, "run165-squad-dressing-room.png"));
            Assert.Empty(LayoutCheck.Scan(window));
        }
        finally
        {
            window?.Close();
            AppSettings.Use(oldSettings); AppSettings.FilePath = oldPath;
            try { File.Delete(path); } catch { }
        }
    }
}
