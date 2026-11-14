using Hardwood.Core;
using Hardwood.Core.College;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Xunit;

namespace Hardwood.Tests;

/// <summary>College basketball (decisions #95-98): programs, the season and March Madness, graduation at 23, saving.</summary>
public class CollegeTests
{
    private static (League nabl, GameWorld world) NewWorld(ulong seed = 9)
    {
        var nabl = new WorldGenerator(new Rng(seed)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));
        return (nabl, GameWorld.Generate(nabl, seed, Array.Empty<string>(), new DateOnly(2026, 10, 1)));
    }

    [Fact]
    public void There_are_148_programs_in_ten_conferences_with_players_from_14_to_22()
    // Since decision #162: rosters of 15 (ages 18-22) and high-school prospects (14-17) in a recruiting pool.
    {
        var (nabl, world) = NewWorld();
        var c = world.College!;
        Assert.True(nabl.CollegeActive);
        Assert.Equal(6 * 14 + 4 * 16, c.Programs.Count); // decision #121: two more conferences
        foreach (var conf in CollegeWorld.Conferences.Where(x => !x.Real)) Assert.Equal(conf.Size, c.InConference(conf.Key).Count());
        Assert.True(c.Recruiting);
        Assert.All(c.Programs, t => Assert.Equal(CollegeWorld.Squad, t.Roster.Count));
        var on = new DateOnly(2026, 7, 1); // ages as of the summer the school year began (classes enroll and graduate then)
        Assert.All(c.Players, p => Assert.InRange(p.Age(on), 14, 22));
        Assert.All(c.Programs.SelectMany(t => t.Roster), p => Assert.InRange(p.Age(on), 18, 22));
        Assert.All(c.Prospects, p => { Assert.InRange(p.Age(on), 14, 17); Assert.Null(p.TeamId); });
        // Ids don't clash with the pros, and every program can be found by id.
        var pros = world.Players.Select(p => p.Id).Concat(nabl.Teams.SelectMany(t => t.Roster).Select(p => p.Id)).ToHashSet();
        Assert.DoesNotContain(c.Players, p => pros.Contains(p.Id));
        Assert.All(c.Programs, t => Assert.Same(t, world.TeamById(t.Id)));
        // Older players are better; power conferences recruit better.
        double At(int age) => c.Players.Where(p => p.Age(on) == age).Average(p => p.CurrentAbility);
        Assert.True(At(22) > At(18) && At(18) > At(14), $"{At(14):F0} {At(18):F0} {At(22):F0}");
        double Conf(bool power) => c.Programs.Where(t => CollegeWorld.Conferences.First(x => x.Key == c.ConferenceOf[t.Id]).Power == power)
            .SelectMany(t => t.Roster).Average(p => p.PotentialAbility);
        Assert.True(Conf(true) > Conf(false));
    }

    [Fact]
    public void A_season_runs_from_november_to_a_national_champion()
    {
        var (_, world) = NewWorld();
        var c = world.College!;
        world.FinishSeasons(new GameSimulator());
        var s = c.Season;
        Assert.All(s.Games, g => Assert.True(g.Played));
        Assert.True(s.Selected);
        Assert.Equal(68, s.Seeds.Count);
        Assert.Equal(67, s.Games.Count(g => g.Event == CollegeWorld.Madness)); // First Four + 63
        Assert.NotNull(s.ChampionId);
        Assert.Equal(c.Active.Count(), s.ConferenceChampions.Count);
        Assert.All(s.ConferenceChampions.Values, id => Assert.Contains(id, s.Seeds.Keys)); // automatic bids
        Assert.Equal(8, s.Games.Where(g => g.Stage == "Invitational").Select(g => g.Event).Distinct().Count());
        var records = c.Programs.Select(t => c.Record(t.Id)).Select(r => r.wins + r.losses).ToList();
        Assert.InRange(records.Average(), 24, 32);
        Assert.Contains(c.History, h => h.Year == s.Year && h.ChampionId == s.ChampionId);
        Assert.Equal(25, c.Top25().Count);
        Assert.All(c.Active, conf => Assert.Equal(conf.Size, c.ConferenceTable(conf.Key).Count));
        Assert.Contains(world.News, n => n.Title.EndsWith("win March Madness"));
    }

    [Fact]
    public void Graduates_turn_pro_at_23_and_programs_recruit_14_year_olds()
    {
        var (_, world) = NewWorld();
        var c = world.College!;
        world.FinishSeasons(new GameSimulator());
        var pool = new List<Player>();
        world.BeginSummer(2027, new GameSimulator(), pool);
        var summer = new DateOnly(2027, 7, 1);
        var grads = pool.Where(p => p.RookieSeason == 2027).ToList();
        Assert.InRange(grads.Count, 150, 600);
        Assert.All(grads, p => Assert.Null(p.TeamId));
        Assert.DoesNotContain(c.Players, p => grads.Contains(p));
        Assert.All(c.Programs, t => Assert.Equal(CollegeWorld.Squad, t.Roster.Count));
        Assert.Contains(c.Prospects, p => p.Age(summer) <= 14);
        Assert.Contains(grads, p => p.CurrentAbility >= 100); // a few are ready for a good league
        world.EndSummer(2027, new GameSimulator(), pool);
        // The pyramid signs graduates, and a new college season is scheduled.
        Assert.Contains(world.Competitions.Where(x => UsaPyramid.IsPyramid(x.Key)).SelectMany(x => x.Clubs).SelectMany(t => t.Roster), p => p.RookieSeason == 2027);
        Assert.Equal(2027, c.Season.Year);
        Assert.NotEmpty(c.Season.Games);
    }

    [Fact]
    public void College_survives_save_and_load()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-college-{Guid.NewGuid():N}.hwsave");
        try
        {
            var s = GameSession.NewGame(2026, 5, path);
            while (s.Today < new DateOnly(2026, 12, 1)) s.AdvanceDay();
            s.SaveNow();
            var c = s.World.College!;
            var loaded = GameSession.Load(path);
            var l = loaded.World.College!;
            Assert.True(loaded.League.CollegeActive);
            Assert.Equal(c.Programs.Count, l.Programs.Count);
            Assert.Equal(c.Players.Count(), l.Players.Count());
            Assert.Equal(c.Season.Games.Count(g => g.Played), l.Season.Games.Count(g => g.Played));
            Assert.Equal(c.Top25().Select(t => t.Id), l.Top25().Select(t => t.Id));
            var p = c.Players.OrderByDescending(x => x.PotentialAbility).First();
            var lp = l.Players.First(x => x.Id == p.Id);
            Assert.Equal((p.CurrentAbility, p.PotentialAbility, p.TeamId), (lp.CurrentAbility, lp.PotentialAbility, lp.TeamId));
            Assert.Equal(c.Destined[p.Id], l.Destined[p.Id]);
            // It keeps playing after loading.
            while (loaded.Today < new DateOnly(2026, 12, 10)) loaded.AdvanceDay();
            Assert.True(l.Season.Games.Count(g => g.Played) > c.Season.Games.Count(g => g.Played));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
