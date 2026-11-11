using Hardwood.Core.College;
using Hardwood.Core.Names;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Name packs change static names (competitions, conferences, the top league), so these run on their own.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class NamePackCollection { public const string Name = "Name packs"; }

/// <summary>Name packs: a personal file renames clubs, leagues and conferences (the game ships fictional; made-up names here).</summary>
[Collection(NamePackCollection.Name)]
public class NamePackTests
{
    private const string Sample = """
    {
      // comments and trailing commas are fine
      "name": "Test pack",
      "topLeague": { "name": "PBL", "short": "PBL" },
      "competitions": { "esp": { "name": "Liga Prueba", "short": "LPR" }, },
      "conferences": { "aca": { "name": "Test Coast Conference", "short": "TCC" } },
      "divisions": { "Atlantic": "Seaboard" },
      "clubs": {
        "Boston Harbor": { "city": "Port City", "name": "Gulls", "abbr": "pcg", "primary": "#112233", "secondary": "#445566" },
        "new york empire": { "city": "Metro", "name": "Towers", "abbr": "MET" },
        "Madrid Capitales": { "city": "Real", "name": "Capital", "abbr": "RCA" },
      },
      "tiers": { "usa2": [ { "city": "Alpha", "name": "Ones", "abbr": "AON" }, { "city": "Beta", "name": "Twos", "abbr": "BTW" }, { "city": "Gamma", "name": "Threes", "abbr": "GTH" } ] },
    }
    """;

    [Fact]
    public void A_pack_renames_clubs_leagues_and_conferences_and_the_career_keeps_them()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-names-{Guid.NewGuid():N}.hwsave");
        try
        {
            var pack = NamePack.Parse(Sample);
            Assert.Equal("Test pack", pack.Name);
            var s = GameSession.NewGame(2026, 1, path, names: pack);

            var boston = s.League.Teams.First(t => t.Id == 1);
            Assert.Equal("Port City Gulls", boston.FullName);
            Assert.Equal("PCG", boston.Abbreviation);
            Assert.Equal("#112233", boston.PrimaryColor);
            Assert.Equal("Seaboard", boston.Division);
            Assert.Equal("Metro Towers", s.League.Teams.First(t => t.Id == 2).FullName);
            Assert.Contains(s.World.Clubs, t => t.FullName == "Real Capital" && t.Abbreviation == "RCA");
            var tier2 = s.World.Competitions.First(c => c.Key == UsaPyramid.Tier2).Clubs;
            Assert.Equal(new[] { "Alpha Ones", "Beta Twos", "Gamma Threes" }, tier2.Take(3).Select(t => t.FullName));

            Assert.Equal("PBL", Brand.Top);
            Assert.Equal("Liga Prueba", Competitions.All.First(c => c.Key == Competitions.Spain).Name);
            Assert.Equal("TCC", CollegeWorld.Conferences.First(c => c.Key == "aca").Short);
            Assert.Equal("PBL", LeagueBook.Get(s, LeagueBook.NablKey)!.Name);
            Assert.StartsWith("Test pack", s.NamePackApplied);
            // The welcome is written after the renaming.
            Assert.DoesNotContain(s.Inbox, m => m.Body.Contains("Boston Harbor"));

            var loaded = GameSession.Load(path);
            Assert.Equal("Port City Gulls", loaded.League.Teams.First(t => t.Id == 1).FullName);
            Assert.Contains(loaded.World.Clubs, t => t.FullName == "Real Capital");
            Assert.Equal("Alpha Ones", loaded.World.Competitions.First(c => c.Key == UsaPyramid.Tier2).Clubs.First().FullName);
            Assert.StartsWith("Test pack", loaded.NamePackApplied);

            NamePack.ResetStatic();
            Assert.Equal("NABL", Brand.Top);
            Assert.Equal("Spanish League", Competitions.All.First(c => c.Key == Competitions.Spain).Name);
            Assert.Equal("Atlantic Coast Alliance", CollegeWorld.Conferences.First(c => c.Key == "aca").Name);
        }
        finally
        {
            NamePack.ResetStatic();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void A_pack_applied_to_a_running_career_renames_what_is_there_now()
    {
        try
        {
            var s = GameSession.NewGame(77, 1, null);
            Assert.Null(s.NamePackApplied);
            Assert.Equal("Boston Harbor", s.League.Teams.First(t => t.Id == 1).FullName);
            int n = NamePack.Parse(Sample).ApplyToCareer(s);
            Assert.True(n >= 6, $"{n} renamed");
            Assert.Equal("Port City Gulls", s.HumanTeam.FullName);
            Assert.StartsWith("Test pack", s.NamePackApplied);
            // A broken file is reported, not thrown.
            var bad = Path.Combine(Path.GetTempPath(), $"hw-bad-{Guid.NewGuid():N}.json");
            File.WriteAllText(bad, "{ not json");
            Assert.Null(NamePack.Load(bad, out var error));
            Assert.Contains("Couldn't read", error);
            File.Delete(bad);
        }
        finally { NamePack.ResetStatic(); }
    }
}
