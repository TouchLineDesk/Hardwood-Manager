using Hardwood.Core.International;
using Hardwood.Core.People;
using Hardwood.Core.Season;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>International plan I5 (run 213, #110): injuries carried to the clubs, morale for call-ups, medals and being left out, one
/// call-ups message for your own players, and the U23 Nations Cup (a game event in 2030, 2034, …).</summary>
[CollectionDefinition(nameof(Run213InternationalDutyTests), DisableParallelization = true)]
public sealed class Run213Serial { }

/// <remarks>Not run in parallel with any other class: two tests set <see cref="U23Cup.EligibleShift"/> (review, run 213).</remarks>
[Collection(nameof(Run213InternationalDutyTests))]
public class Run213InternationalDutyTests
{
    private readonly ITestOutputHelper _out;
    public Run213InternationalDutyTests(ITestOutputHelper output) => _out = output;

    private static GameSession NewCareer(string? path = null, string program = "Virginia Tech")
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(31, 2026, GameSession.DefaultFullSim);
        var me = world.College!.Programs.First(t => t.FullName.StartsWith(program));
        return GameSession.NewGame(31, me.Id, path);
    }

    [Fact]
    public void A_world_cup_rolls_its_injuries_from_its_own_results_and_writes_no_player()
    {
        var s = NewCareer();
        var before = Run198ReloadRolloverTests.Print(s);
        var cup = WorldCup.Play(s, 2027)!;
        var again = WorldCup.Play(s, 2027)!;
        Assert.Equal(before, Run198ReloadRolloverTests.Print(s)); // nothing shared moved, no player hurt yet
        Assert.Equal(cup.Games, again.Games);
        Assert.Equal(cup.Injuries, again.Injuries);
        foreach (var i in cup.Injuries) _out.WriteLine($"{i.Code} {i.Name}: {i.Injury} ({i.Severity}) {i.Occurred:MMM d} → {i.Return:MMM d}");
        int players = cup.Teams.Sum(t => t.Squad.Count);
        _out.WriteLine($"{cup.Injuries.Count} injuries among {players} players; {cup.Injuries.Count(i => i.Return > new DateOnly(2027, 10, 14))} still out at the jump");
        Assert.InRange(cup.Injuries.Count, 3, 60);
        Assert.All(cup.Injuries, i => Assert.Contains(i.PlayerId, cup.Teams.First(t => t.Code == i.Code).Squad));
        Assert.Equal(cup.Injuries.Count, cup.Injuries.Select(i => i.PlayerId).Distinct().Count());
        Assert.All(cup.Injuries, i => Assert.InRange(i.Occurred, new DateOnly(2027, 8, 27), new DateOnly(2027, 9, 12)));
    }

    [Fact]
    public void The_summer_carries_injuries_and_moods_and_a_reload_derives_the_same_lines()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hw-run213-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "career.hwsave");
        try
        {
            var s = NewCareer(path);
            var h = s.World.International;
            s.PlayInternationalOverSummer(new DateOnly(2027, 5, 8), new DateOnly(2027, 10, 14));
            var cup = Assert.Single(h.WorldCups);
            var people = AllPlayers(s);

            // Every injury is in the player's history; one still running on the career's day is his current injury.
            Assert.NotEmpty(cup.Injuries);
            foreach (var i in cup.Injuries)
            {
                var p = people[i.PlayerId];
                Assert.Contains(p.InjuryHistory, x => x.Name == i.Injury && x.Occurred == i.Occurred && x.ReturnDate == i.Return);
                if (i.Return > new DateOnly(2027, 10, 14)) Assert.True(p.Injury != null && p.Injury.ReturnDate >= i.Return);
            }

            // Gold, a medal and a call-up each have their own mood line until Dec 1; nobody outside the twelves has one.
            var gold = cup.Teams.First(t => t.Code == cup.Gold);
            var champ = people[gold.Squad[0]];
            Assert.NotNull(champ.International);
            Assert.Equal(8, champ.International!.Effect);
            Assert.StartsWith("Back from World Cup 2027 with gold for", champ.International.Reason);
            var other = cup.Teams.First(t => t.Code != cup.Gold && t.Code != cup.Silver && t.Code != cup.Bronze);
            Assert.Equal(3, people[other.Squad[0]].International!.Effect);
            var inSquads = cup.Teams.SelectMany(t => t.Squad).ToHashSet();
            Assert.All(people.Values.Where(p => !inSquads.Contains(p.Id)), p => Assert.Null(p.International));

            var team = s.League.Teams.Concat(s.World.Clubs).Concat(s.World.College!.Programs).First(t => t.Roster.Contains(champ));
            var oct = PeopleEngine.Factors(champ, team, 0.5, null, Hardwood.Core.Model.Finance.HardCapDefault, new DateOnly(2027, 10, 20));
            Assert.Contains(oct, f => f.reason == champ.International.Reason && f.effect == 8);
            var dec = PeopleEngine.Factors(champ, team, 0.5, null, Hardwood.Core.Model.Finance.HardCapDefault, new DateOnly(2027, 12, 2));
            Assert.DoesNotContain(dec, f => f.reason == champ.International.Reason);

            // The 2029 continentals: a player in his nation's 2027 twelve, fit and not picked, is left out (and stung).
            s.PlayInternationalOverSummer(new DateOnly(2029, 5, 8), new DateOnly(2029, 10, 14));
            int leftOut = h.Continentals.Sum(c => c.LeftOut.Count);
            _out.WriteLine($"2029: left out {leftOut}; injuries {h.Continentals.Sum(c => c.Injuries.Count)}");
            foreach (var c in h.Continentals)
                foreach (var (id, code) in c.LeftOut)
                {
                    Assert.DoesNotContain(id, c.Teams.First(t => t.Code == code).Squad);
                    var m = people[id].International;
                    Assert.NotNull(m); // a snub, or a lift if he played somewhere else that summer
                }

            var moods = people.Values.Where(p => p.International != null).ToDictionary(p => p.Id, p => p.International);
            s.SaveNow();
            SqliteConnection.ClearAllPools();
            var back = GameSession.Load(path);
            var backPeople = AllPlayers(back);
            Assert.Equal(moods, backPeople.Values.Where(p => p.International != null).ToDictionary(p => p.Id, p => p.International));
            Assert.Equal(h.WorldCups[0].Injuries, back.World.International.WorldCups[0].Injuries);
            Assert.Equal(h.Continentals.Select(c => c.LeftOut.Count), back.World.International.Continentals.Select(c => c.LeftOut.Count));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void A_jump_mid_tournament_carries_only_the_injuries_already_picked_up()
    {
        var s = NewCareer();
        // Run 218: Aug 30 (was Sep 5): in the real-clubs world no injury falls after Sep 5, so the jump comes earlier in the cup.
        var jump = new DateOnly(2027, 8, 30); // a league that opens before the World Cup ends (run 209's review)
        s.PlayInternationalOverSummer(new DateOnly(2027, 5, 8), jump);
        var cup = Assert.Single(s.World.International.WorldCups);
        var people = AllPlayers(s);
        Assert.Contains(cup.Injuries, i => i.Occurred > jump);
        foreach (var i in cup.Injuries)
        {
            var p = people[i.PlayerId];
            bool carried = p.InjuryHistory.Any(x => x.Name == i.Injury && x.Occurred == i.Occurred);
            Assert.Equal(i.Occurred <= jump, carried);
            if (i.Occurred > jump) Assert.False(p.Injury is { } cur && cur.Occurred == i.Occurred && cur.Name == i.Injury);
        }
    }

    [Fact]
    public void The_u23_nations_cup_is_played_in_2030_by_under_23s_only()
    {
        Assert.Null(U23Cup.EventIn(2028));
        Assert.Null(U23Cup.EventIn(2031));
        var ev = U23Cup.EventIn(2030)!;
        Assert.Equal(FibaEventKind.U23, ev.Kind);
        Assert.Contains(FibaCalendar.InYear(2034), e => e.Kind == FibaEventKind.U23);
        Assert.DoesNotContain(FibaCalendar.InYear(2032), e => e.Kind == FibaEventKind.U23); // an Olympic summer

        Assert.Null(U23Cup.Play(NewCareer(), 2030)); // a new 2026 world: its 2030 under-23s aren't born into it yet
        U23Cup.EligibleShift = 4; // count ages as in 2026 (born 2003 or later): about 26 nations have twelve
        try { U23At2030(); } finally { U23Cup.EligibleShift = 0; }
    }

    private void U23At2030()
    {
        var s = NewCareer();
        var before = Run198ReloadRolloverTests.Print(s);
        var rec = U23Cup.Play(s, 2030)!;
        Assert.Equal(before, Run198ReloadRolloverTests.Print(s));
        var people = AllPlayers(s);
        _out.WriteLine($"{rec.Teams.Count} teams: {string.Join(" ", rec.Teams.Select(t => t.Code))}; gold {rec.Gold}, silver {rec.Silver}, bronze {rec.Bronze}; "
                       + $"{rec.Games.Count} games, {rec.Games.Average(g => g.HomePts + g.AwayPts) / 2.0:F1} a side; MVP {rec.Mvp?.Name}");
        Assert.InRange(rec.Teams.Count, 8, U23Cup.Teams);
        Assert.Equal(U23Cup.Teams, rec.Teams.Count);
        Assert.All(rec.Teams.SelectMany(t => t.Squad), id => Assert.True(U23Cup.Eligible(people[id], 2030)));
        Assert.All(rec.Teams.SelectMany(t => t.Squad), id => Assert.True(people[id].BirthDate.Year >= 2003));
        Assert.Equal(4, new[] { rec.Gold, rec.Silver, rec.Bronze, rec.Fourth }.Distinct().Count());
        Assert.Equal("U23 Nations Cup 2030", rec.Short);

        // At the jump: played once, its own inbox story, no ranking move.
        var ranking = new Dictionary<string, double>(s.World.International.Ranking);
        s.PlayInternationalOverSummer(new DateOnly(2030, 5, 8), new DateOnly(2030, 10, 14));
        s.PlayInternationalOverSummer(new DateOnly(2030, 5, 8), new DateOnly(2030, 10, 14));
        var played = Assert.Single(s.World.International.U23Cups);
        Assert.Equal(rec.Games, played.Games);
        Assert.Single(s.Inbox, m => m.Title.StartsWith("U23 Nations Cup 2030: "));
        Assert.Equal(ranking, s.World.International.Ranking);
        foreach (var m in s.Inbox.Where(m => m.Title.StartsWith("U23") || m.Title.StartsWith("International duty"))) _out.WriteLine($"{m.Title}\n{m.Body}\n");
    }

    [Fact]
    public void Your_own_players_get_one_call_ups_message()
    {
        // Denver (31): Jokic plays for Serbia at the 2027 World Cup.
        var (league, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(31, 2026, GameSession.DefaultFullSim);
        var den = league.Teams.First(t => t.Name == "Nuggets");
        var s = GameSession.NewGame(31, den.Id, null);
        s.PlayInternationalOverSummer(new DateOnly(2027, 5, 8), new DateOnly(2027, 10, 14));
        var rec = Assert.Single(s.World.International.WorldCups);
        var mine = s.HumanTeam.Roster.Select(p => p.Id).ToHashSet();
        int ours = rec.Teams.Sum(t => t.Squad.Count(mine.Contains));
        Assert.True(ours >= 1);
        var msg = Assert.Single(s.Inbox, m => m.Title.StartsWith("International duty"));
        _out.WriteLine($"{msg.Title}\n{msg.Body}");
        Assert.Contains("World Cup 2027: ", msg.Body);
        Assert.Contains(ours == 1 ? "one of ours" : $"{ours} of ours", msg.Title);
    }

    [Fact]
    public void A_player_in_last_times_twelve_who_is_fit_but_not_picked_is_left_out()
    {
        var s = NewCareer();
        var cup = WorldCup.Play(s, 2027)!;
        var first = new DateOnly(2027, 8, 27);
        var pools = NationalTeams.Build(WorldCup.Entries(s, first), first);
        var team = cup.Teams.First(t => t.Code == "SRB");
        var pool = pools.First(n => n.Nation.Code == "SRB");
        var spare = pool.Pool.Select(x => x.Player.Id).Where(id => !team.Squad.Contains(id)).Take(3).ToList();
        var h = new InternationalHistory();
        h.Continentals.Add(new WorldCupRecord { Year = 2025, Kind = "Continental", Teams = { team with { Squad = team.Squad.Take(9).Concat(spare).ToList() } } });
        var left = Callups.LeftOut(h, cup, pools, first);
        Assert.Equal(spare.OrderBy(x => x), left.Keys.OrderBy(x => x));
        Assert.All(left.Values, c => Assert.Equal("SRB", c));
        Assert.Empty(Callups.LeftOut(new InternationalHistory(), cup, pools, first)); // a first tournament leaves nobody out

        // His mood line: stung, unless he played (for someone) that summer.
        h.WorldCups.Add(cup);
        cup.LeftOut = left;
        var p = pool.Pool.First(x => x.Player.Id == spare[0]).Player;
        var m = Callups.MoodFor(h, p)!;
        Assert.Equal(Callups.LeftOutHit, m.Effect);
        Assert.Equal("Left out of Serbia's twelve for World Cup 2027", m.Reason);
    }

    private static Dictionary<int, Hardwood.Core.Model.Player> AllPlayers(GameSession s)
    {
        var d = new Dictionary<int, Hardwood.Core.Model.Player>();
        foreach (var t in s.League.Teams.Concat(s.World.Clubs).Concat(s.World.College?.Programs ?? Enumerable.Empty<Hardwood.Core.Model.Team>()))
            foreach (var p in t.Roster) d.TryAdd(p.Id, p);
        foreach (var p in s.League.FreeAgents) d.TryAdd(p.Id, p);
        return d;
    }
}
