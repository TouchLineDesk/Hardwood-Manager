using Hardwood.Core.Model;
using Hardwood.Core.Names;
using Hardwood.Core.RealWorld;
using Hardwood.Core.Season;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// The real world, slice R1 (Danny, Nov 12, decisions #191-#197): the NBA's 30 real teams at the top of the pyramid and
/// the NBA's real history. Runs with the name-pack tests because it changes the static top-league name.
/// </summary>
[Collection(NamePackCollection.Name)]
public class RealWorldTests
{
    [Fact]
    public void The_pack_holds_the_thirty_real_franchises_in_their_real_divisions()
    {
        var teams = RealWorldData.Teams;
        Assert.Equal(30, teams.Count);
        Assert.Equal(30, teams.Select(t => t.Abbr).Distinct().Count());
        // Danny's eight divisions (#432) with the SuperSonics and Aces (#433): four in each.
        Assert.All(teams.Concat(RealWorldData.Expansion).GroupBy(t => t.Division), g => Assert.Equal(4, g.Count()));
        Assert.Equal(8, teams.Concat(RealWorldData.Expansion).Select(t => t.Division).Distinct().Count());
        Assert.Equal(16, teams.Count(t => t.Conference == "East"));
        Assert.Equal("Boston Celtics", teams[0].FullName);
        Assert.Contains(teams, t => t.FullName == "Los Angeles Lakers" && t.Division == "Mojave");
        Assert.Contains(teams, t => t.FullName == "Oklahoma City Thunder" && t.Conference == "West");
        Assert.All(teams, t => Assert.Matches("^#[0-9A-F]{6}$", t.Primary));
        Assert.False(string.IsNullOrEmpty(RealWorldData.TeamsChecked));
    }

    [Fact]
    public void Real_history_runs_from_1946_47_to_last_season_with_the_real_title_counts()
    {
        var seasons = RealWorldData.Seasons;
        Assert.Equal(Enumerable.Range(1946, 80), seasons.Select(s => s.Season));
        Assert.Equal("Philadelphia Warriors", seasons[0].Champion);
        var last = seasons[^1];
        Assert.Equal("2025-26", last.Label);
        Assert.Equal(("New York Knicks", "San Antonio Spurs", "Jalen Brunson", "Shai Gilgeous-Alexander"), (last.Champion, last.RunnerUp, last.FinalsMvp, last.Mvp));
        Assert.Equal(18, RealWorldData.Titles("BOS"));
        Assert.Equal(17, RealWorldData.Titles("LAL"));
        Assert.Equal(7, RealWorldData.Titles("GSW"));
        Assert.Equal(6, RealWorldData.Titles("CHI"));
        Assert.Equal(3, RealWorldData.Titles("NYK"));
        Assert.Equal(2, RealWorldData.Titles("OKC")); // Seattle 1979 and 2025
        // Every franchise named in the history is one of today's 30.
        var abbrs = RealWorldData.Teams.Select(t => t.Abbr).ToHashSet();
        Assert.All(seasons, s => Assert.True(s.ChampionFranchise == null || abbrs.Contains(s.ChampionFranchise), s.ChampionFranchise));
        Assert.Equal(79, seasons.Count(s => s.ChampionFranchise != null)); // all but the 1948 Baltimore Bullets
        Assert.All(seasons.Where(s => s.Season >= 1955), s => Assert.NotNull(s.Mvp));      // the MVP began in 1955-56
        Assert.All(seasons.Where(s => s.Season >= 1968), s => Assert.NotNull(s.FinalsMvp)); // the Finals MVP in 1968-69
        Assert.Contains(RealWorldData.GameRecords, r => r.Label == "Points" && r.Value == "100" && r.Holder == "Wilt Chamberlain");
        Assert.Contains(RealWorldData.SeasonRecords, r => r.Label == "Best record" && r.Value == "73-9");
        Assert.Contains(RealWorldData.CareerRecords, r => r.Label == "Points" && r.Holder == "LeBron James");
    }

    [Fact]
    public void A_real_world_career_plays_in_the_NBA_and_keeps_it_after_a_reload()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-real-{Guid.NewGuid():N}.hwsave");
        try
        {
            var s = GameSession.NewGame(2026, 1, path);
            Assert.True(RealWorldData.IsReal(s.League));
            Assert.Equal("NBA", Brand.Top);
            Assert.Equal("Boston Celtics", s.HumanTeam.FullName);
            Assert.Equal(("BOS", Conference.East, "Revolution", 19156), (s.HumanTeam.Abbreviation, s.HumanTeam.Conference, s.HumanTeam.Division, s.HumanTeam.ArenaCapacity));
            Assert.Equal("Los Angeles Lakers", s.League.Teams.First(t => t.Id == 22).FullName);
            Assert.Contains(s.Inbox, m => m.Title == "Welcome to the job" && m.Body.Contains("New York Knicks"));
            Assert.DoesNotContain(s.League.Teams, t => t.FullName.Contains("Harbor"));
            for (int i = 0; i < 5; i++) s.AdvanceDay();
            s.SaveNow();

            // Loading the career brings the NBA's names back even after the brand was reset (the made-up world is gone, D61 B).
            Brand.Real = false;
            var loaded = GameSession.Load(path);
            Assert.Equal("NBA", Brand.Top);
            Assert.Equal("Boston Celtics", loaded.HumanTeam.FullName);
        }
        finally
        {
            Brand.Real = false;
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void A_name_pack_still_goes_on_top_of_the_real_world()
    {
        try
        {
            var pack = NamePack.Parse("""{ "clubs": { "Boston Celtics": { "city": "Boston", "name": "Green", "abbr": "BGR" } } }""");
            var s = GameSession.NewGame(5, 1, null, names: pack);
            Assert.Equal("Boston Green", s.HumanTeam.FullName);
            Assert.Equal("New York Knicks", s.League.Teams.First(t => t.Id == 2).FullName);
        }
        finally { Brand.Real = false; NamePack.ResetStatic(); }
    }
}
