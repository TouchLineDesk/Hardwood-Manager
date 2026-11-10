using Hardwood.Core;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.Persistence;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Xunit;

namespace Hardwood.Tests;

public class WorldTests
{
    private static (League nabl, GameWorld world) Build(ulong seed = 2026, params string[] fullSim)
    {
        var nabl = new WorldGenerator(new Rng(seed)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));
        return (nabl, GameWorld.Generate(nabl, seed, fullSim, new DateOnly(2026, 10, 1)));
    }

    [Fact]
    public void Generates_five_competitions_with_their_own_clubs_and_unique_ids()
    {
        var (nabl, world) = Build();
        Assert.Equal(new[] { 0, 18, 16, 10, 20 }, world.Competitions.Take(5).Select(c => c.Clubs.Count)); // then the USA pyramid and the wider world
        Assert.Equal(18, world[Competitions.Elite].Season.Participants.Count);

        var teamIds = nabl.Teams.Select(t => t.Id).Concat(world.Clubs.Select(t => t.Id)).ToList();
        Assert.Equal(teamIds.Count, teamIds.Distinct().Count());
        var playerIds = nabl.AllActivePlayers.Concat(nabl.FreeAgents).Concat(world.Players).Select(p => p.Id).ToList();
        Assert.Equal(playerIds.Count, playerIds.Distinct().Count());
        Assert.True(nabl.NextPlayerId > playerIds.Max());
        Assert.All(world.Clubs, t => Assert.Equal(GameWorld.RosterSize, t.Roster.Count));
        Assert.All(world.Players, p => Assert.NotNull(p.Contract));
    }

    [Fact]
    public void Clubs_respect_their_league_foreign_limit_and_are_mostly_local()
    {
        var (_, world) = Build();
        foreach (var comp in world.Competitions.Where(c => c.Profile.Kind == CompetitionKind.Domestic))
        {
            Assert.All(comp.Clubs, t => Assert.True(GameWorld.Imports(t, comp.Profile) <= comp.Profile.ForeignLimit, $"{t.FullName} has too many imports"));
            double local = comp.Clubs.SelectMany(t => t.Roster).Count(p => comp.Profile.LocalNationalities.Contains(p.Nationality)) / (double)(comp.Clubs.Count * GameWorld.RosterSize);
            Assert.True(local > 0.5, $"{comp.Profile.Name}: {local:P0} local");
        }
        // Spanish locals draw faces from the Spanish group (decision #36).
        var spain = world[Competitions.Spain].Clubs.SelectMany(t => t.Roster).Where(p => p.Nationality == "Spain").ToList();
        Assert.True(spain.Count(p => p.Ethnicity == Ethnicity.SpanMed) > spain.Count * 0.7);
    }

    [Fact]
    public void The_nabl_built_from_a_seed_is_unchanged_by_the_world()
    {
        var plain = new WorldGenerator(new Rng(77)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));
        var (nabl, _) = Build(77);
        Assert.Equal(plain.AllActivePlayers.Select(p => (p.Id, p.Name, p.CurrentAbility)), nabl.AllActivePlayers.Select(p => (p.Id, p.Name, p.CurrentAbility)));
    }

    [Fact]
    public void Every_competition_plays_a_full_season_to_a_champion_without_double_bookings()
    {
        var (_, world) = Build();
        world.FinishSeasons(new GameSimulator());
        foreach (var comp in world.Playing)
        {
            var s = comp.Season;
            Assert.True(s.IsComplete, comp.Profile.Name);
            Assert.NotNull(s.ChampionId);
            Assert.Single(comp.History);
            int teams = s.Participants.Count;
            int regular = s.Fixtures.Count(f => f.Stage == CompetitionSeason.RegularSeason);
            Assert.Equal(teams * (teams - 1) / 2 * comp.Profile.RoundRobins, regular);
            Assert.All(s.Series, x => Assert.True(x.Complete));
            Assert.Equal(comp.Profile.Playoffs.Sum(r => r.Teams / 2), s.Series.Count);
        }
        // No club plays twice on one day, across all competitions.
        var games = world.Playing.SelectMany(c => c.Season.Fixtures).SelectMany(f => new[] { (f.HomeId, f.Date), (f.AwayId, f.Date) }).ToList();
        Assert.Equal(games.Count, games.Distinct().Count());

        // Euro Elite League: Final Four on a Friday and Sunday in late May.
        var elite = world[Competitions.Elite].Season;
        var ff = elite.Fixtures.Where(f => f.Stage.StartsWith("Final Four")).ToList();
        Assert.Equal(2, ff.Count);
        Assert.All(ff, f => Assert.Equal(DayOfWeek.Friday, f.Date.DayOfWeek));
        var final = elite.Fixtures.Single(f => f.Stage == "Final");
        Assert.Equal(DayOfWeek.Sunday, final.Date.DayOfWeek);
        Assert.Equal(5, final.Date.Month);
        Assert.Contains(world.News, n => n.Title.Contains("Final Four"));

        // Background stats look like basketball.
        var lines = world[Competitions.Spain].Season.Stats.Values.Where(l => l.G > 20).ToList();
        Assert.InRange(lines.Max(l => l.Ppg), 12, 35);
        Assert.InRange(lines.Average(l => l.Mpg), 12, 30);
    }

    [Fact]
    public void A_full_sim_league_runs_through_the_match_engine()
    {
        var (_, world) = Build(5, Competitions.Australia);
        world.FinishSeasons(new GameSimulator());
        var aus = world[Competitions.Australia];
        Assert.True(aus.FullSim);
        Assert.True(aus.Season.IsComplete);
        // Engine lines carry three-point attempts; the quick model doesn't.
        Assert.Contains(aus.Season.Stats.Values, l => l.Tpa > 0);
        Assert.DoesNotContain(world[Competitions.China].Season.Stats.Values, l => l.Tpa > 0);
    }

    [Fact]
    public void Fiba_games_score_like_fiba_games()
    {
        var (_, world) = Build(9, Competitions.Spain);
        world.FinishSeasons(new GameSimulator());
        var games = world[Competitions.Spain].Season.Fixtures.Where(f => f.Stage == CompetitionSeason.RegularSeason).ToList();
        double perTeam = games.Average(f => (f.HomePts!.Value + f.AwayPts!.Value) / 2.0);
        Assert.InRange(perTeam, 72, 92); // 40 minutes at a European pace, not NBA-style 110+
        var quick = world[Competitions.Turkey].Season.Fixtures.Where(f => f.Stage == CompetitionSeason.RegularSeason).Average(f => (f.HomePts!.Value + f.AwayPts!.Value) / 2.0);
        Assert.InRange(quick, 72, 92);
    }

    [Fact]
    public void Rollover_ages_players_refills_rosters_and_starts_the_next_season()
    {
        var (nabl, world) = Build();
        var before = world.Players.ToDictionary(p => p.Id, p => p.CurrentAbility);
        world.Rollover(2027, new GameSimulator());
        Assert.All(world.Playing, c => Assert.Equal(2027, c.Season.Year));
        Assert.All(world.Playing, c => Assert.False(c.Season.IsComplete));
        Assert.All(world.Clubs, t => Assert.Equal(GameWorld.RosterSize, t.Roster.Count));
        Assert.Contains(world.Players, p => before.TryGetValue(p.Id, out var ca) && ca != p.CurrentAbility); // development happened
        Assert.Contains(world.Players, p => !before.ContainsKey(p.Id));                                         // new young players
        var ids = nabl.AllActivePlayers.Concat(world.Players).Concat(nabl.Retired).Select(p => p.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(world.Playing.Where(c => c.Profile.Kind == CompetitionKind.Domestic), c => Assert.Single(c.History));
        // The new Elite field comes from last season's domestic tables.
        Assert.Equal(18, world[Competitions.Elite].Season.Participants.Distinct().Count());
    }

    [Fact]
    public void Overseas_leagues_hold_their_level_over_the_years()
    {
        var (_, world) = Build(31);
        var sim = new GameSimulator();
        double Mean(WorldCompetition c) => c.Clubs.SelectMany(t => t.Roster).Average(p => p.CurrentAbility);
        var start = world.Competitions.Where(c => c.Clubs.Count > 0).ToDictionary(c => c.Key, Mean);
        for (int y = 2027; y <= 2032; y++) world.Rollover(y, sim);
        // (The USA pyramid's leagues swap clubs every summer, so they're checked by level instead.)
        foreach (var c in world.Competitions.Where(c => c.Clubs.Count > 0 && !UsaPyramid.IsPyramid(c.Key)))
            Assert.True(Math.Abs(Mean(c) - start[c.Key]) <= 6, $"{c.Profile.Name}: {start[c.Key]:F1} -> {Mean(c):F1}");
        double Level(int l) => world.Competitions.Where(c => UsaPyramid.Level(c.Key) == l).SelectMany(c => c.Clubs).SelectMany(t => t.Roster).Average(p => p.CurrentAbility);
        Assert.True(Level(2) > Level(3) && Level(3) > Level(4) && Level(4) > Level(5), $"{Level(2):F0} {Level(3):F0} {Level(4):F0} {Level(5):F0}");
        // Imports are replaced by imports, so clubs keep using their foreign slots.
        // (In the pyramid, clubs carry their imports between levels, so only the leagues abroad are checked.)
        foreach (var c in world.Competitions.Where(c => c.Profile.Kind == CompetitionKind.Domestic && !UsaPyramid.IsPyramid(c.Key)))
            Assert.True(c.Clubs.Average(t => GameWorld.Imports(t, c.Profile)) >= c.Profile.ForeignLimit * 0.5, c.Profile.Name);
    }

    [Fact]
    public void A_career_saves_and_loads_the_world_and_older_saves_get_one()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hw-world-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "career.hwsave");
        try
        {
            var session = GameSession.NewGame(2026, 3, path, fullSim: new[] { Competitions.Elite, Competitions.Spain });
            for (int i = 0; i < 30; i++) session.AdvanceDay();
            var played = session.World.Playing.ToDictionary(c => c.Key, c => c.Season.Fixtures.Count(f => f.Played));
            Assert.True(played.Values.Sum() > 100);
            Assert.True(session.World[Competitions.Spain].FullSim);

            var loaded = GameSession.Load(path);
            Assert.Equal(played, loaded.World.Playing.ToDictionary(c => c.Key, c => c.Season.Fixtures.Count(f => f.Played)));
            Assert.Equal(session.World.Players.Select(p => p.Id).OrderBy(x => x), loaded.World.Players.Select(p => p.Id).OrderBy(x => x));
            Assert.True(loaded.World[Competitions.Spain].FullSim);
            Assert.Equal(session.World[Competitions.Elite].Season.Participants, loaded.World[Competitions.Elite].Season.Participants);
            loaded.AdvanceDay(); // keeps going after a load

            // A save made before the world existed: drop the world and its players, then load.
            using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "DROP TABLE world_meta; DROP TABLE world_comp; DROP TABLE world_team; DELETE FROM player WHERE status = 'overseas';";
                cmd.ExecuteNonQuery();
            }
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            var upgraded = GameSession.Load(path);
            Assert.Equal(Competitions.Current.Count(c => c.Kind != CompetitionKind.GuestPool), upgraded.World.Playing.Count());
            Assert.True(upgraded.World.Playing.Sum(c => c.Season.Fixtures.Count(f => f.Played)) > 100); // caught up to today
            var ids = upgraded.League.AllActivePlayers.Concat(upgraded.League.FreeAgents).Concat(upgraded.World.Players).Select(p => p.Id).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dir, true);
        }
    }
}
