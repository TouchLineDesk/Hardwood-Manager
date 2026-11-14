using System.Reflection;
using System.Text.Json;
using Hardwood.Core.Model;
using Hardwood.Core.Names;

namespace Hardwood.Core.RealWorld;

/// <summary>One real NBA franchise (data pack: Data/nba_teams.json).</summary>
public sealed record RealTeam(string City, string Name, string Abbr, string Primary, string Secondary, string Arena, int Capacity, string Conference, string Division)
{
    public string FullName => $"{City} {Name}";
}

/// <summary>A real NBA season's outcome. Franchises are today's (Minneapolis Lakers = LAL).</summary>
public sealed record RealSeason(int Season, string Champion, string? ChampionFranchise, string RunnerUp, string? RunnerUpFranchise, string? FinalsMvp, string? Mvp)
{
    public string Label => $"{Season}-{(Season + 1) % 100:00}";
}

/// <summary>A real NBA record: the mark, who holds it, for whom and when.</summary>
public sealed record RealRecord(string Label, string Value, string Holder, string Team, string When);

/// <summary>
/// The real world (Danny, Nov 12, decisions #191-#197; `claude/real-world-design.md`, slice R1): the NBA's 30 real teams
/// at the top of the USA pyramid, and the NBA's real history and records. Since R2 the players are real too (<see cref="RealPlayers"/>). The data
/// lives in embedded JSON files that record when and where it was checked. A career is a real-world one when its top
/// league is named <see cref="LeagueName"/>; the fictional world (the NABL) stays as a New career option.
/// </summary>
public static class RealWorldData
{
    public const string LeagueName = "National Basketball Association", Short = "NBA";

    private static readonly Lazy<(List<RealTeam> Teams, string Checked, string Source)> _teams = new(LoadTeams);
    private static readonly Lazy<History> _history = new(LoadHistory);

    /// <summary>The 30 franchises in the game's franchise-slot order (the same cities, conferences and divisions).</summary>
    public static IReadOnlyList<RealTeam> Teams => _teams.Value.Teams;

    /// <summary>
    /// Danny's two expansion franchises (#433), not real NBA clubs today: the Seattle SuperSonics (the old name, which Danny
    /// kept) and the Las Vegas Aces (his name). They take the two places that make the top league 32. Arenas and capacities
    /// are approximate (Climate Pledge Arena, T-Mobile Arena).
    /// </summary>
    public static readonly IReadOnlyList<RealTeam> Expansion = new[]
    {
        new RealTeam("Seattle", "SuperSonics", "SEA", "#00653A", "#FFC200", "Climate Pledge Arena", 17300, "West", "Cascade"),
        new RealTeam("Las Vegas", "Aces", "LVA", "#BA0C2F", "#000000", "T-Mobile Arena", 17500, "West", "Mojave"),
    };
    public static string TeamsChecked => _teams.Value.Checked;
    public static IReadOnlyList<RealSeason> Seasons => _history.Value.Seasons;
    public static IReadOnlyList<RealRecord> GameRecords => _history.Value.Game;
    public static IReadOnlyList<RealRecord> SeasonRecords => _history.Value.Season;
    public static IReadOnlyList<RealRecord> CareerRecords => _history.Value.Career;
    public static string HistoryChecked => _history.Value.Checked;
    public static string HistorySource => _history.Value.Source;

    /// <summary>Whether a career plays in the real world (its top league is the NBA).</summary>
    public static bool IsReal(League league) => league.Name == LeagueName;

    /// <summary>Real NBA titles of a franchise, by today's abbreviation.</summary>
    public static int Titles(string abbr) => Seasons.Count(s => s.ChampionFranchise == abbr);

    /// <summary>Every franchise's real titles, most first (only those with one).</summary>
    public static List<(RealTeam Team, int Titles, int LastSeason)> TitleTable() =>
        Teams.Select(t => (t, Titles(t.Abbr), Seasons.Where(s => s.ChampionFranchise == t.Abbr).Select(s => s.Season + 1).DefaultIfEmpty(0).Max()))
             .Where(x => x.Item2 > 0).OrderByDescending(x => x.Item2).ThenByDescending(x => x.Item3).ToList();

    /// <summary>The real NBA as a name pack: the top league and its 30 clubs by position (the user's own pack is applied on top).</summary>
    public static NamePack Pack() => new()
    {
        Name = "Real world (NBA)",
        TopLeague = new PackName { Name = Short, Short = Short },
        Tiers = new Dictionary<string, List<PackClub>>(StringComparer.OrdinalIgnoreCase)
        {
            [Hardwood.Core.Season.LeagueBook.NablKey] = Teams.Select(t => new PackClub { City = t.City, Name = t.Name, Abbr = t.Abbr, Primary = t.Primary, Secondary = t.Secondary }).ToList(),
        },
    };

    /// <summary>Turn a freshly generated top league (generated under <see cref="LeagueName"/>) into the NBA: its teams,
    /// colours, conferences, divisions and arena sizes, and (R2) its real players, rated from their stats.</summary>
    public static void Apply(League league, Hardwood.Core.World.GameWorld world, ulong seed = 0, bool players = true)
    {
        if (!IsReal(league)) throw new ArgumentException($"Generate the league as \"{LeagueName}\" first.", nameof(league));
        Brand.Real = true;
        // The tiers below are laid out with Danny's division names (#432), whatever the world was generated under.
        foreach (var c in world.Competitions.Where(c => Hardwood.Core.World.UsaPyramid.IsNational(c.Key))) Hardwood.Core.World.UsaPyramid.LabelDivisions(c, real: true, force: true);
        // Renamed here rather than through Pack().ApplyToClubs, which would also rename the static names (Brand.Top)
        // for every later career; Brand.Real does that job and follows whichever career is open.
        void Name(Team team, RealTeam real)
        {
            team.City = real.City; team.Name = real.Name; team.Abbreviation = real.Abbr;
            team.PrimaryColor = real.Primary; team.SecondaryColor = real.Secondary;
            team.Conference = real.Conference == "East" ? Conference.East : Conference.West;
            team.Division = real.Division;
            team.ArenaCapacity = real.Capacity;
        }
        foreach (var (team, real) in league.Teams.Take(Teams.Count).Zip(Teams)) Name(team, real);
        // Danny's expansion clubs (#433): the two clubs that come up to make 32 become the Seattle SuperSonics and the Las Vegas
        // Aces, with the rosters they came up with. A club in the pyramid already using their abbreviation takes another.
        foreach (var (team, real) in league.Teams.Skip(Teams.Count).Zip(Expansion))
        {
            Name(team, real);
            foreach (var other in world.Clubs.Where(c => c != team && c.Abbreviation == real.Abbr))
                other.Abbreviation = Enumerable.Range(2, 8).Select(i => real.Abbr[..2] + i).First(a => world.Clubs.All(c => c.Abbreviation != a) && league.Teams.All(c => c.Abbreviation != a));
        }
        // Danny's eight divisions (#432); anyone left over is placed by RealignTop.
        Hardwood.Core.World.UsaPyramid.RealignTop(league, league.Teams.Skip(Teams.Count + Expansion.Count));
        if (players) RealPlayers.Apply(league, seed);
        Brand.Real = true;
    }

    private sealed record History(List<RealSeason> Seasons, List<RealRecord> Game, List<RealRecord> Season, List<RealRecord> Career, string Checked, string Source);

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    private static JsonDocument Read(string file)
    {
        var asm = typeof(RealWorldData).Assembly;
        string name = asm.GetManifestResourceNames().Single(n => n.EndsWith(file, StringComparison.Ordinal));
        using var stream = asm.GetManifestResourceStream(name)!;
        return JsonDocument.Parse(stream);
    }

    private static (List<RealTeam>, string, string) LoadTeams()
    {
        using var doc = Read("nba_teams.json");
        var root = doc.RootElement;
        var teams = root.GetProperty("teams").EnumerateArray().Select(e => e.Deserialize<RealTeam>(Options)!).ToList();
        return (teams, root.GetProperty("checked").GetString() ?? "", root.GetProperty("source").GetString() ?? "");
    }

    private static History LoadHistory()
    {
        using var doc = Read("nba_history.json");
        var root = doc.RootElement;
        var seasons = root.GetProperty("seasons").EnumerateArray().Select(e => e.Deserialize<RealSeason>(Options)!).ToList();
        var rec = root.GetProperty("records");
        List<RealRecord> R(string k) => rec.GetProperty(k).EnumerateArray().Select(e => e.Deserialize<RealRecord>(Options)!).ToList();
        return new History(seasons, R("game"), R("season"), R("career"), root.GetProperty("checked").GetString() ?? "", root.GetProperty("source").GetString() ?? "");
    }
}
