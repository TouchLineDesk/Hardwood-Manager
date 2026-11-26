using System.IO.Compression;
using Hardwood.Core.RealWorld;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// Run 217 (K5, Danny's `danny-world-leagues-nov26`): the EuroLeague's seven strongest domestic leagues have their real clubs,
/// cities, arenas and capacities; every league abroad has its real name in a real-world career; the rosters stay generated;
/// an older save's made-up clubs are renamed when it loads.
/// </summary>
public class Run217RealClubsTests
{
    private static readonly string[] Seven = { Competitions.Spain, Competitions.Turkey, WorldLeagues.Greece, WorldLeagues.Italy, WorldLeagues.France, WorldLeagues.Germany, WorldLeagues.Lithuania };

    [Fact]
    public void A_new_world_has_the_real_clubs_names_and_arenas()
    {
        var (league, world) = RealWorldData.NewWorld(31, players: false);
        var spain = world.Competitions.First(c => c.Key == Competitions.Spain);
        Assert.Equal("Liga ACB", spain.Profile.Name);
        Assert.Equal("EuroLeague", world.Competitions.First(c => c.Key == Competitions.Elite).Profile.Name);
        Assert.Equal("EuroCup", world.Competitions.First(c => c.Key == WorldLeagues.EuroCup).Profile.Name);
        Assert.Equal("Basketball Bundesliga", world.Competitions.First(c => c.Key == WorldLeagues.Germany).Profile.Name);

        var barca = spain.Clubs.Single(t => t.Name == "Barça");
        Assert.Equal("FC Barcelona", barca.FullName);
        Assert.Equal("FC Barcelona", barca.TheFullName); // no "the" for a club's own name
        Assert.Equal(7786, barca.ArenaCapacity);
        Assert.Equal("Palau Blaugrana", RealClubs.ArenaOf(barca));
        Assert.Equal("Real Madrid", spain.Clubs.Single(t => t.Abbreviation == "RMA").FullName);
        var hamburg = world.Competitions.First(c => c.Key == WorldLeagues.Germany).Clubs.Single(t => t.City == "Hamburg");
        Assert.Equal("the Hamburg Towers", hamburg.TheFullName);
        Assert.Equal("Zalgiris Kaunas", world.Competitions.First(c => c.Key == WorldLeagues.Lithuania).Clubs.Single(t => t.Abbreviation == "ZAL").FullName);

        // Every club of the seven is a real one, and the rosters are still full and generated.
        foreach (var key in Seven)
        {
            var comp = world.Competitions.First(c => c.Key == key);
            Assert.All(comp.Clubs, t => Assert.NotNull(RealClubs.Of(t)));
            Assert.All(comp.Clubs, t => Assert.True(t.Roster.Count >= 12, $"{t.FullName} has {t.Roster.Count}"));
        }
        // No made-up name survives in them.
        Assert.DoesNotContain(world.Clubs, t => t.Name is "Capitales" or "Kartallar" or "Shamrocks" or "Green Knights" or "Olimpians" or "Albatrosses" or "Seine");
    }

    [Theory]
    [InlineData(2026ul)]
    [InlineData(31ul)]
    [InlineData(77ul)]
    public void Abbreviations_and_names_stay_unique_across_the_world(ulong seed)
    {
        var (league, world) = RealWorldData.NewWorld(seed, players: false);
        // The clubs abroad (outside the USA pyramid) and the NBA: the studio finds an NBA arena by abbreviation, so Baskonia isn't BKN.
        var all = world.Competitions.Where(c => !UsaPyramid.IsPyramid(c.Key) && c.Profile.Kind == CompetitionKind.Domestic).SelectMany(c => c.Clubs).Concat(league.Teams).ToList();
        var dupAbbr = all.GroupBy(t => t.Abbreviation).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(dupAbbr.Count == 0, "duplicate abbreviations: " + string.Join(", ", dupAbbr));
        var dupName = all.GroupBy(t => t.FullName).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(dupName.Count == 0, "duplicate names: " + string.Join(", ", dupName));
        // Review: and no real club shares its abbreviation with any other club, the pyramid's and college's included.
        var everyone = world.Clubs.Concat(league.Teams).Concat(world.College?.Programs ?? new()).DistinctBy(t => t.Id).ToList();
        var shared = everyone.Where(t => RealClubs.Of(t) != null && everyone.Count(o => o.Abbreviation == t.Abbreviation) > 1)
            .Select(t => $"{t.FullName} {t.Abbreviation} ({string.Join("/", everyone.Where(o => o != t && o.Abbreviation == t.Abbreviation).Select(o => o.FullName))})").ToList();
        Assert.True(shared.Count == 0, "shared: " + string.Join("; ", shared));
        // Each real club replaced one made-up club in its own league.
        foreach (var key in Seven)
        {
            var p = Competitions.Get(key);
            Assert.All(p.Clubs, c => Assert.NotNull(c.Was));
            Assert.Equal(p.Clubs.Length, p.Clubs.Select(c => c.Was).Distinct().Count());
        }
    }

    [Fact]
    public void The_giants_are_stronger_than_the_newcomers()
    {
        double giants = 0, small = 0; int g = 0, n = 0;
        foreach (ulong seed in new ulong[] { 31, 2026, 77 })
        {
            var (_, world) = RealWorldData.NewWorld(seed, players: false);
            foreach (var key in Seven)
            foreach (var t in world.Competitions.First(c => c.Key == key).Clubs)
            {
                double top = t.Roster.OrderByDescending(p => p.CurrentAbility).Take(8).Average(p => p.CurrentAbility);
                int standing = RealClubs.Of(t)!.Standing;
                if (standing >= 5) { giants += top; g++; }
                else if (standing <= -2) { small += top; n++; }
            }
        }
        Assert.True(giants / g > small / n + 5, $"giants {giants / g:0.0}, small clubs {small / n:0.0}");
    }

    [Fact]
    public void A_real_league_with_one_club_renamed_by_a_pack_is_left_alone()
    {
        // Review (run 217): "Thessaloniki Iraklis" is a real club and the made-up name in Aris's slot. A pack that renames
        // one Greek club must not send the league back through the rename (Iraklis would become a second Aris).
        var (league, world) = RealWorldData.NewWorld(31, players: false);
        var greece = world.Competitions.First(c => c.Key == WorldLeagues.Greece);
        var aek = greece.Clubs.Single(t => t.Name == "AEK");
        aek.City = "Nea Filadelfia"; aek.Name = "Union";
        Assert.Equal(0, RealClubs.Rename(world, league.Teams));
        Assert.Single(greece.Clubs, t => t.Name == "Aris");
        Assert.Single(greece.Clubs, t => t.Name == "Iraklis");
    }

    [Fact]
    public void A_pack_for_the_made_up_name_still_fits_the_real_club()
    {
        var (league, world) = RealWorldData.NewWorld(31, players: false);
        var pack = Hardwood.Core.Names.NamePack.Parse("""{ "clubs": { "Madrid Capitales": { "city": "Madrid", "name": "Royals", "abbr": "MRY" } } }""");
        try
        {
            pack.ApplyToClubs(league, world);
            Assert.Contains(world.Clubs, t => t.FullName == "Madrid Royals" && t.Abbreviation == "MRY");
        }
        finally { Hardwood.Core.Names.NamePack.ResetStatic(); }
    }

    [Fact]
    public void An_older_save_gets_the_real_clubs_on_load()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"hw-run217-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "career.hwsave");
            using (var src = new GZipStream(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "SaveFixtures", CollegeSaveCompatibilityTests.LaunchFixtureName)), CompressionMode.Decompress))
            using (var dst = File.Create(path)) src.CopyTo(dst);
            var s = GameSession.Load(path);
            var spain = s.World.Competitions.First(c => c.Key == Competitions.Spain);
            Assert.Contains(spain.Clubs, t => t.FullName == "Real Madrid");
            Assert.Contains(spain.Clubs, t => t.FullName == "Força Lleida"); // took Sevilla's place
            Assert.DoesNotContain(s.World.Clubs, t => t.Name is "Capitales" or "Kartallar" or "Green Knights");
            Assert.All(spain.Clubs, t => Assert.True(t.Roster.Count >= 8, $"{t.FullName} {t.Roster.Count}"));
            Assert.Equal(15415, s.World.Competitions.First(c => c.Key == WorldLeagues.Lithuania).Clubs.Single(t => t.Name == "Zalgiris").ArenaCapacity);
            Assert.Equal("BAS", spain.Clubs.Single(t => t.Name == "Baskonia").Abbreviation);
            // A second load changes nothing more.
            Assert.Equal(0, RealClubs.Rename(s.World, s.League.Teams));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}
