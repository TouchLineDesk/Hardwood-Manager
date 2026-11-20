using Avalonia.Headless;
using Hardwood.Core;
using Hardwood.Core.College;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Season;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 135, K12a / CS5 (critic review #18 item 6) and the rest of K14:
/// - the college season's honours are voted the day the champion is crowned: National Player of the Year, the Final Four's
///   Most Outstanding Player, Defensive and Freshman of the Year, Coach of the Year, three All-America teams and every
///   conference's first team and player of the year;
/// - the votes draw from their own stream, so the same world gives the same winners;
/// - the honours are saved, show on the player's page, and a college coach's Awards and History pages are the college game's;
/// - a college Team page names the athletic director, not an owner.
/// </summary>
public class Run135CollegeAwardsTests
{
    private readonly ITestOutputHelper _out;
    public Run135CollegeAwardsTests(ITestOutputHelper o) => _out = o;

    private static CollegeWorld Played(ulong seed, bool real)
    {
        var rng = new Rng(seed);
        var gen = new PlayerGenerator(new Rng(seed ^ 0x51), 2_000_000);
        var c = CollegeWorld.Generate(gen, rng, new DateOnly(2026, 10, 1), real: real);
        c.PlayThrough(new DateOnly(2027, 4, 30), new GameSimulator(), rng);
        return c;
    }

    [Theory]
    [InlineData(25UL, true)]
    [InlineData(7UL, false)]
    public void A_finished_season_has_its_college_honours(ulong seed, bool real)
    {
        var c = Played(seed, real);
        var s = c.Season;
        Assert.NotNull(s.ChampionId);
        var a = Assert.Single(c.Awards);
        Assert.Equal(s.Year, a.Year);
        var players = c.Programs.SelectMany(t => t.Roster).ToDictionary(p => p.Id);

        // National Player of the Year: a regular from a good team, with a real vote.
        var poy = Assert.IsType<int>(a.PlayerOfYearId);
        Assert.True(s.Stats[poy].G >= CollegeWorld.AwardGames);
        Assert.InRange(a.PlayerOfYear[0].Share, 0.05, 1.0);
        var (w, l) = c.RegularSeasonRecord(s.Stats[poy].TeamId);
        Assert.True(w > l, $"the Player of the Year's team went {w}-{l}");
        _out.WriteLine($"POY {a.PlayerOfYear[0].Name} ({a.PlayerOfYear[0].Team}) {a.PlayerOfYear[0].StatLine}, {a.PlayerOfYear[0].Share:P0}");

        // Freshman of the Year is a freshman; the Most Outstanding Player played for the champion.
        Assert.Equal(1, CollegeWorld.ClassYearOf(players[a.FreshmanId!.Value], s.Year)!.Year);
        Assert.NotNull(a.DefensiveId);
        var mop = Assert.IsType<int>(a.OutstandingId);
        Assert.Equal(s.ChampionId, a.ProgramOf[mop]);
        _out.WriteLine($"MOP {a.NameOf(mop)}: {a.OutstandingLine}; COY {a.CoachName}: {a.CoachNote}");

        // Three All-America teams of five different players, the Player of the Year on the first.
        Assert.Equal(3, a.AllAmerica.Count);
        Assert.All(a.AllAmerica, t => Assert.Equal(5, t.Count));
        Assert.Equal(15, a.AllAmerica.SelectMany(t => t).Distinct().Count());
        Assert.Contains(poy, a.AllAmerica[0]);

        // Every conference: a first team from its own programs, its player of the year on it.
        foreach (var conf in c.Active)
        {
            var five = a.AllConference[conf.Key];
            Assert.InRange(five.Count, 1, 5);
            Assert.All(five, id => Assert.Equal(conf.Key, c.ConferenceOf[a.ProgramOf[id]]));
            Assert.Contains(a.ConferencePlayer[conf.Key], five);
        }

        // Coach of the Year finished in the top 30; every program's season is kept for History.
        // (Run 136: the vote uses the poll before March, #973; the page's poll is now the final one, which moves a few places.)
        Assert.InRange(c.PollPosition(a.CoachProgramId!.Value), 1, 40);
        Assert.Equal(c.Programs.Count, a.Records.Count);
        Assert.Equal(7, a.Records[s.ChampionId!.Value][2]);

        // The honours follow the player, and the news names the winner.
        Assert.Contains($"National Player of the Year {CollegeAwards.SeasonText(s.Year)}", c.HonoursOf(poy));
        Assert.Contains(c.HonoursOf(a.AllAmerica[1][0]), h => h.StartsWith("All-America second team"));
        Assert.Contains(c.News, n => n.Title == $"{a.NameOf(poy)} is the National Player of the Year");
    }

    [Fact]
    public void The_same_world_votes_the_same_winners_and_the_awards_survive_a_save()
    {
        var a = Played(25, true).Awards.Single();
        var b = Played(25, true).Awards.Single();
        Assert.Equal(a.PlayerOfYear.Select(v => (v.PlayerId, v.Points)), b.PlayerOfYear.Select(v => (v.PlayerId, v.Points)));
        Assert.Equal(a.AllAmerica.SelectMany(t => t), b.AllAmerica.SelectMany(t => t));

        // The save's own JSON settings (SaveFile.JsonOpts).
        var opts = new System.Text.Json.JsonSerializerOptions { IncludeFields = true };
        var back = System.Text.Json.JsonSerializer.Deserialize<List<CollegeAwards>>(System.Text.Json.JsonSerializer.Serialize(new List<CollegeAwards> { a }, opts), opts)!.Single();
        Assert.Equal(a.PlayerOfYearId, back.PlayerOfYearId);
        Assert.Equal(a.PlayerOfYear[0].StatLine, back.PlayerOfYear[0].StatLine);
        Assert.Equal(a.AllConference.Keys.OrderBy(k => k), back.AllConference.Keys.OrderBy(k => k));
        Assert.Equal(a.Records[a.CoachProgramId!.Value], back.Records[a.CoachProgramId!.Value]);
        Assert.Equal(a.NameOf(a.OutstandingId!.Value), back.NameOf(back.OutstandingId!.Value));
    }
}

/// <summary>Run 135 on screen: a college coach's Awards, History, Team and player pages after a season.</summary>
[Collection(SharedUiState.Name)]   // it swaps AppSettings, app-wide state
public class Run135CollegeAwardsUiTests
{
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void A_college_coach_sees_college_awards_history_and_an_athletic_director()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run135-{Guid.NewGuid():N}.hwsave");
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { AutoPortraits = false });
        MainWindow? window = null;
        try
        {
            var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026, 2026, GameSession.DefaultFullSim);
            int id = world.College!.Programs.First(p => p.FullName.StartsWith("Miami (FL)")).Id;
            var s = GameSession.NewGame(2026, id, path);
            var cw = s.World.College!;
            // Before any season: the page explains itself and shows nothing of the NBA.
            var vm = new MainViewModel();
            window = new MainWindow { DataContext = vm, Width = 1480, Height = 900 };
            window.Show();
            vm.AttachSession(s);
            var w = window;
            var dir = Environment.GetEnvironmentVariable("HW_SCREENSHOT_DIR");
            void Settle() { for (int i = 0; i < 6; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); } }
            void Shot(string file)
            {
                Settle();
                if (string.IsNullOrEmpty(dir)) return;
                Directory.CreateDirectory(dir);
                using var frame = w.CaptureRenderedFrame();
                frame?.Save(Path.Combine(dir, file));
            }
            vm.Navigate("Awards"); Settle();
            var aw = (AwardsViewModel)vm.CurrentPage!;
            Assert.True(aw.IsCollege);
            Assert.Equal("No college awards yet", aw.Headline);
            Assert.False(aw.ShowHall);
            vm.Navigate("History"); Settle();
            var hist = (HistoryViewModel)vm.CurrentPage!;
            Assert.True(hist.IsCollege);
            Assert.False(hist.IsReal);
            Assert.Contains("national championship game", hist.SeasonsNote);

            // K14 (hunt 19): another program's Team page names its athletic director.
            var bama = cw.Programs.First(p => p.FullName.StartsWith("Alabama"));
            vm.ShowTeam(bama); Settle();
            var team = (TeamViewModel)vm.CurrentPage!;
            Assert.Contains(team.Chips, c => c.Label == "Athletic director");
            Assert.DoesNotContain(team.Chips, c => c.Label == "Owner");
            Assert.DoesNotContain("Owner:", team.Club);

            // The college season plays out (the world on its own: quick, and the same games the session would play).
            cw.PlayThrough(new DateOnly(2027, 4, 30), new GameSimulator(), new Rng(135));
            var a = cw.Awards.Single();

            vm.Navigate("Awards"); Settle();
            aw = (AwardsViewModel)vm.CurrentPage!;
            aw.Refresh(); Settle();
            Assert.Equal("2026-27 college awards", aw.Headline);
            Assert.StartsWith("National champions: ", aw.Summary);
            var awards = aw.Winners.Select(x => x.Award).ToList();
            Assert.Equal(new[] { "National Player of the Year", "Defensive Player of the Year", "Freshman of the Year", "Most Outstanding Player", "Coach of the Year" }, awards);
            Assert.Equal(a.NameOf(a.PlayerOfYearId!.Value), aw.Winners[0].Name);
            Assert.Equal(3, aw.AllLeagueTeams.Count);
            Assert.Equal("All-America teams", aw.AllLeagueTitle);
            Assert.Equal("ACC", aw.AllStarTeams[0].Title); // your conference first
            Assert.DoesNotContain(aw.Winners, x => x.Award is "Most Valuable Player" or "Sixth Man of the Year" or "Finals MVP");
            Shot("run135-college-awards.png");

            vm.Navigate("History"); Settle();
            hist = (HistoryViewModel)vm.CurrentPage!;
            hist.Refresh(); Settle();
            var card = Assert.Single(hist.SeasonCards);
            Assert.Equal(cw.History.Single().Champion, card.ChampionName);
            Assert.Equal("Player of the Year ", card.MvpLabel);
            Assert.StartsWith($"{s.HumanTeam.Abbreviation}: ", card.OurRecord);
            Assert.Equal("Player of the Year", hist.MvpHeader);
            Shot("run135-college-history.png");

            // The winner's page carries the honour.
            vm.ShowPlayer(cw.Players.First(p => p.Id == a.PlayerOfYearId)); Settle();
            var page = (PlayerViewModel)vm.CurrentPage!;
            Assert.Contains("National Player of the Year 2026-27", page.Honours);
            Shot("run135-college-player-honours.png");
        }
        finally
        {
            window?.Close();
            AppSettings.FilePath = oldPath; AppSettings.Use(oldSettings);
            try { File.Delete(path); } catch { }
        }
    }
}
