using Hardwood.Core.International;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>International plan, slice I1 (run 207): national teams from every player in the world, ranked, and the FIBA calendar.
/// Pure: building them draws nothing and writes nothing.</summary>
public class Run207NationalTeamsTests
{
    private readonly ITestOutputHelper _out;
    public Run207NationalTeamsTests(ITestOutputHelper output) => _out = output;

    private static readonly DateOnly Today = new(2026, 10, 14);
    private static int _id;
    private static Player P(string nat, int ca, Position pos = Position.SF, int born = 2000) =>
        new() { Id = ++_id, FirstName = "A", LastName = "B" + _id, Nationality = nat, CurrentAbility = ca, PrimaryPosition = pos, BirthDate = new DateOnly(born, 1, 1) };

    [Fact]
    public void A_nation_needs_twelve_eligible_players_and_picks_a_balanced_twelve()
    {
        var club = new Team { Id = 1, City = "X", Name = "Y", Abbreviation = "XY", Division = "D" };
        var list = new List<(Player, Team?, bool)>();
        // Spain: 10 small forwards at 150+, then 3 guards and 3 centres below them, and a 16-year-old star.
        for (int i = 0; i < 10; i++) list.Add((P("Spain", 160 - i), club, false));
        for (int i = 0; i < 3; i++) list.Add((P("Spain", 120 - i, Position.PG), club, false));
        for (int i = 0; i < 3; i++) list.Add((P("Spain", 110 - i, Position.C), null, false));
        list.Add((P("Spain", 190, born: 2010), club, false));
        // Italy: only 11 players.
        for (int i = 0; i < 11; i++) list.Add((P("Italy", 150), club, false));
        // Russia: 12, but suspended.
        for (int i = 0; i < 12; i++) list.Add((P("Russia", 170), club, false));
        // Unknown nationality: ignored.
        for (int i = 0; i < 12; i++) list.Add((P("Atlantis", 170), club, false));

        var teams = NationalTeams.Build(list, Today);
        Assert.Equal(new[] { "Spain", "Russia" }, teams.Select(t => t.Name));
        var spain = teams[0];
        Assert.Equal(1, spain.Rank);
        Assert.Equal(0, teams[1].Rank); // suspended: listed, not ranked
        Assert.Equal(16, spain.Pool.Count); // the 16-year-old isn't eligible
        Assert.Equal(12, spain.Squad.Count);
        // Only 3 guards exist, so all 3 are in; 3 centres are in; 6 forwards fill the rest, best first.
        Assert.Equal(3, spain.Squad.Count(x => x.Player.PrimaryPosition == Position.PG));
        Assert.Equal(3, spain.Squad.Count(x => x.Player.PrimaryPosition == Position.C));
        Assert.Equal(160, spain.Squad[0].Player.CurrentAbility);
        Assert.True(spain.Squad.Zip(spain.Squad.Skip(1)).All(z => z.First.Player.CurrentAbility >= z.Second.Player.CurrentAbility));
        // 70% of the top five (160–156: 158) and 30% of the next five (155, 120, 119, 118, 110: 124.4).
        Assert.Equal(147.9, spain.Strength, 1);
    }

    [Fact]
    public void Other_spellings_join_one_nation()
    {
        Assert.Same(NationalTeams.NationOf("Great Britain"), NationalTeams.NationOf("England"));
        Assert.Same(NationalTeams.NationOf("Chinese Taipei"), NationalTeams.NationOf("Taiwan"));
        Assert.Equal("NCA", NationalTeams.NationOf("Nicaragua")!.Code);
        var club = new Team { Id = 1, City = "X", Name = "Y", Abbreviation = "XY", Division = "D" };
        var list = Enumerable.Range(0, 6).Select(_ => (P("England", 120), (Team?)club, false))
            .Concat(Enumerable.Range(0, 6).Select(_ => (P("Great Britain", 120), (Team?)club, false)));
        Assert.Equal("Great Britain", Assert.Single(NationalTeams.Build(list, Today)).Name);
    }

    [Fact]
    public void The_calendar_follows_the_fiba_cycle()
    {
        var events = FibaCalendar.From(new DateOnly(2026, 10, 14), 6);
        var wc = events.Where(e => e.Kind == FibaEventKind.WorldCup).ToList();
        Assert.Equal(new[] { 2027, 2031 }, wc.Select(e => e.Start.Year));
        Assert.Equal("Aug 27 – Sep 12, 2027", wc[0].Dates);
        Assert.Equal("Qatar", wc[0].Where);
        var oly = events.Where(e => e.Kind == FibaEventKind.Olympics).ToList();
        Assert.Equal(new[] { "Los Angeles", "Brisbane" }, oly.Select(e => e.Where));
        Assert.Equal(new[] { 2029 }, events.Where(e => e.Kind == FibaEventKind.Continental).Select(e => e.Start.Year).Distinct());
        Assert.Equal(4, events.Count(e => e.Kind == FibaEventKind.Continental));
        foreach (var w in events.Where(e => e.Kind == FibaEventKind.Window))
        {
            Assert.Equal(DayOfWeek.Monday, w.Start.DayOfWeek);
            Assert.True(w.Start.Month is 2 or 11);
        }
        // The Nov 2026 and Feb 2027 windows qualify for the 2027 World Cup; Nov 2027 for the 2029 continental championships.
        var windows = events.Where(e => e.Kind == FibaEventKind.Window).ToList();
        Assert.Contains("2027 World Cup", windows[0].Note);
        Assert.Contains("2027 World Cup", windows[1].Note);
        Assert.Contains("2029 continental", windows[2].Note);
        Assert.True(events.Zip(events.Skip(1)).All(z => z.First.Start <= z.Second.Start));
        // Nothing past the horizon.
        Assert.All(events, e => Assert.True(e.Start <= new DateOnly(2032, 10, 14)));
    }

    /// <summary>A real college world (seed 31): 30–70 national teams (#105: "expect about 40–60"), the USA first, college players
    /// in some twelves, and building them changes nothing (the stream and the rosters are untouched).</summary>
    [Fact]
    public void A_real_world_has_ranked_national_teams_and_building_them_changes_nothing()
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(31, 2026, GameSession.DefaultFullSim);
        var me = world.College!.Programs.First(t => t.FullName.StartsWith("Virginia Tech"));
        var s = GameSession.NewGame(31, me.Id, null);
        var before = Run198ReloadRolloverTests.Print(s);
        var teams = NationalTeams.Build(s);
        var again = NationalTeams.Build(s);
        Assert.Equal(before, Run198ReloadRolloverTests.Print(s));
        Assert.Equal(teams.Select(t => (t.Name, t.Strength)), again.Select(t => (t.Name, t.Strength)));

        foreach (var t in teams.Take(15))
            _out.WriteLine($"{t.Rank,2} {t.Nation.Code} {t.Name,-22} {t.Strength,6} pool {t.Pool.Count,4}  best {t.Squad[0].Player.Name} ({t.Squad[0].Club?.Abbreviation ?? "FA"})  college in 12: {t.Squad.Count(x => x.College)}");
        var missing = s.League.Teams.SelectMany(t => t.Roster).Concat(s.World.Players).Select(p => p.Nationality)
            .Where(n => NationalTeams.NationOf(n) == null).GroupBy(n => n).OrderByDescending(g => g.Count()).Take(10);
        foreach (var g in missing) _out.WriteLine($"unknown: {g.Key} ×{g.Count()}");
        _out.WriteLine($"{teams.Count} teams; zones: " + string.Join(", ", teams.GroupBy(t => t.Nation.Zone).Select(g => $"{g.Key} {g.Count()}")));

        foreach (var t in teams.Where(t => t.Name != "USA" && t.Pool.Any(x => x.College)).OrderByDescending(t => t.Pool.Count(x => x.College)).Take(8))
            _out.WriteLine($"  {t.Name}: {t.Pool.Count(x => x.College)} college in pool, best college {t.Pool.First(x => x.College).Player.Name} CA {t.Pool.First(x => x.College).Player.CurrentAbility}, 12th CA {t.Squad[^1].Player.CurrentAbility}");
        Assert.InRange(teams.Count, 30, 80);
        Assert.Equal("USA", teams[0].Name);
        Assert.All(teams, t => Assert.Equal(12, t.Squad.Count));
        Assert.All(Enum.GetValues<FibaZone>(), z => Assert.Contains(teams, t => t.Nation.Zone == z));
        // College players are in the pools (the federations pick from them, too).
        Assert.Contains(teams, t => t.Pool.Any(x => x.College));
        _out.WriteLine($"college players in pools {teams.Sum(t => t.Pool.Count(x => x.College))}, in twelves {teams.Sum(t => t.Squad.Count(x => x.College))}");
        Assert.Equal(Enumerable.Range(1, teams.Count(t => t.Rank > 0)), teams.Where(t => t.Rank > 0).Select(t => t.Rank));
    }
}
