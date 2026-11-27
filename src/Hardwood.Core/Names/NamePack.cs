using System.Text.Json;
using System.Text.Json.Serialization;
using Hardwood.Core.College;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Core.World;

namespace Hardwood.Core.Names;

/// <summary>The top league's display name. "NABL" in the fictional world; a name pack can rename it.</summary>
public static class Brand
{
    public const string DefaultTop = "NABL";
    private static string _top = DefaultTop, _topShort = DefaultTop;

    /// <summary>The current career (or New career preview) plays in the real world: the top league is the NBA unless a
    /// name pack renames it (#191). Set by <see cref="Hardwood.Core.RealWorld.RealWorldData.Apply"/> and on loading a career.</summary>
    public static bool Real { get; set; }

    public static string Top { get => Real && _top == DefaultTop ? Hardwood.Core.RealWorld.RealWorldData.Short : _top; set => _top = value; }
    public static string TopShort { get => Real && _topShort == DefaultTop ? Hardwood.Core.RealWorld.RealWorldData.Short : _topShort; set => _topShort = value; }
}

/// <summary>A display name and short name.</summary>
public sealed class PackName
{
    public string Name { get; set; } = "";
    public string? Short { get; set; }
}

/// <summary>A club's new identity.</summary>
public sealed class PackClub
{
    public string City { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Abbr { get; set; }
    public string? Primary { get; set; }
    public string? Secondary { get; set; }
    public string FullName => $"{City} {Name}".Trim();
}

/// <summary>
/// A name pack (like a Football Manager real-names file): renames clubs, leagues, conferences and the top league from a
/// JSON file the player keeps outside the game. The game ships fictional; the pack is personal. Clubs are matched by their
/// original fictional full name ("clubs"), or by position within a competition ("tiers", for a whole league at once).
/// </summary>
public sealed class NamePack
{
    public string Name { get; set; } = "Name pack";
    public PackName? TopLeague { get; set; }
    public Dictionary<string, PackName> Competitions { get; set; } = new();
    public Dictionary<string, PackName> Conferences { get; set; } = new();
    public Dictionary<string, string> Divisions { get; set; } = new();
    public Dictionary<string, PackClub> Clubs { get; set; } = new();
    public Dictionary<string, List<PackClub>> Tiers { get; set; } = new();

    /// <summary>How many names the pack holds (for the settings line).</summary>
    [JsonIgnore]
    public int Count => Clubs.Count + Tiers.Values.Sum(t => t.Count) + Competitions.Count + Conferences.Count + (TopLeague != null ? 1 : 0);

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
    };

    public static NamePack Parse(string json)
    {
        var pack = JsonSerializer.Deserialize<NamePack>(json, Options) ?? new NamePack();
        // Keys match regardless of case.
        pack.Clubs = new Dictionary<string, PackClub>(pack.Clubs ?? new(), StringComparer.OrdinalIgnoreCase);
        pack.Competitions = new Dictionary<string, PackName>(pack.Competitions ?? new(), StringComparer.OrdinalIgnoreCase);
        pack.Conferences = new Dictionary<string, PackName>(pack.Conferences ?? new(), StringComparer.OrdinalIgnoreCase);
        pack.Divisions = new Dictionary<string, string>(pack.Divisions ?? new(), StringComparer.OrdinalIgnoreCase);
        pack.Tiers = new Dictionary<string, List<PackClub>>(pack.Tiers ?? new(), StringComparer.OrdinalIgnoreCase);
        return pack;
    }

    /// <summary>Load a pack, or null when the file is missing or unreadable (the reason in <paramref name="error"/>).</summary>
    public static NamePack? Load(string? path, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) { error = "No name pack file."; return null; }
        try { return Parse(File.ReadAllText(path)); }
        catch (Exception ex) { error = $"Couldn't read {Path.GetFileName(path)}: {ex.Message}"; return null; }
    }

    // ---------------------------------------------------------------- static names (every launch)

    private static Dictionary<string, (string Name, string Short)>? _profileDefaults;
    private static ConferenceInfo[]? _conferenceDefaults;

    private static IEnumerable<CompetitionProfile> Profiles => World.Competitions.All;

    private static void Remember()
    {
        // The names as built, not Name/Short: those read the real-world names while a real career is open (run 108), and a
        // first reset then would make "NBA G League" the fictional default for every later career.
        _profileDefaults ??= Profiles.GroupBy(p => p.Key).ToDictionary(g => g.Key, g => (g.First().BaseName, g.First().BaseShort));
        _conferenceDefaults ??= CollegeWorld.Conferences.ToArray();
    }

    /// <summary>Rename the competitions, college conferences and the top league (these aren't saved with a career).</summary>
    public static void ApplyStatic(NamePack pack)
    {
        ResetStatic();
        if (pack.TopLeague is { Name.Length: > 0 } top) { Brand.Top = top.Name; Brand.TopShort = top.Short ?? top.Name; }
        foreach (var p in Profiles)
            if (pack.Competitions.TryGetValue(p.Key, out var n) && n.Name.Length > 0) { p.Name = n.Name; p.Short = n.Short ?? p.Short; }
        var confs = CollegeWorld.Conferences;
        for (int i = 0; i < confs.Length; i++)
            if (pack.Conferences.TryGetValue(confs[i].Key, out var n) && n.Name.Length > 0)
                confs[i] = confs[i] with { Name = n.Name, Short = n.Short ?? confs[i].Short };
    }

    /// <summary>Back to the fictional names.</summary>
    public static void ResetStatic()
    {
        Remember();
        Brand.Top = Brand.DefaultTop; Brand.TopShort = Brand.DefaultTop;
        foreach (var p in Profiles)
            if (_profileDefaults!.TryGetValue(p.Key, out var d)) p.ResetNames(d.Name, d.Short);
        _conferenceDefaults!.CopyTo(CollegeWorld.Conferences, 0);
    }

    // ---------------------------------------------------------------- a career's clubs (new careers, or on request)

    /// <summary>
    /// Rename a career's clubs (or a new-career preview's): by original name first, then whole competitions by position,
    /// then the top league's divisions. Also applies the static names. Returns how many clubs changed.
    /// </summary>
    public int ApplyToClubs(League league, GameWorld world)
    {
        ApplyStatic(this);
        int changed = 0;
        var college = world.College?.Programs ?? new List<Team>();
        var all = league.Teams.Concat(world.Clubs).Concat(college).DistinctBy(t => t.Id).ToList();
        foreach (var t in all)
            if (Clubs.TryGetValue(t.FullName, out var c)
                || FormerNames.TryGetValue(t.FullName, out var former) && Clubs.TryGetValue(former, out c)
                || NewNames.TryGetValue(t.FullName, out var renamed) && Clubs.TryGetValue(renamed, out c)
                || UsaPyramid.FormerNames.TryGetValue(t.FullName, out var was) && Clubs.TryGetValue(was, out c)
                || PyramidNewNames.Value.TryGetValue(t.FullName, out var now) && Clubs.TryGetValue(now, out c)
                || RealClubs.FormerFullName(t) is { } madeUp && Clubs.TryGetValue(madeUp, out c)) { Rename(t, c); changed++; } // run 217

        foreach (var (key, list) in Tiers)
        {
            IEnumerable<Team> members;
            if (key.Equals(LeagueBook.NablKey, StringComparison.OrdinalIgnoreCase)) members = league.Teams;
            else if (key.StartsWith("college-", StringComparison.OrdinalIgnoreCase) && world.College is { } col)
            {
                string conf = key["college-".Length..];
                members = col.Programs.Where(p => col.ConferenceOf.TryGetValue(p.Id, out var k) && k.Equals(conf, StringComparison.OrdinalIgnoreCase));
            }
            else members = world.Competitions.FirstOrDefault(c => c.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Clubs ?? Enumerable.Empty<Team>();
            foreach (var (t, c) in members.ToList().Zip(list)) { Rename(t, c); changed++; }
        }
        foreach (var t in league.Teams)
            if (Divisions.TryGetValue(t.Division, out var d) && d.Length > 0) t.Division = d;
        return changed;
    }

    /// <summary>Rename a career's clubs and remember which pack it wears (GameSession.NamePackApplied).</summary>
    public int ApplyToCareer(GameSession s)
    {
        int changed = ApplyToClubs(s.League, s.World);
        s.NamePackApplied = $"{Name} (applied {s.Today:MMM d, yyyy})";
        return changed;
    }

    /// <summary>H79 (run 107): clubs whose shipped name changed, so a pack written for the old name still finds them.</summary>
    public static readonly IReadOnlyDictionary<string, string> FormerNames = new Dictionary<string, string>
    {
        ["Munich Bavarians"] = "Munich Lions", ["Yokohama B-Corsairs"] = "Yokohama Bay Stars",
        // Run 218 (K5 slice 2): the clubs the run-107 names belonged to are real now; their own pre-107 names still point at them.
        ["Busan KCC Egis"] = "Busan Pegasus", ["Blackwater Bossing"] = "Batangas Beer Men",
    };

    /// <summary>The reverse: a career saved before the rename still finds a pack written for the new name.</summary>
    private static readonly IReadOnlyDictionary<string, string> NewNames = FormerNames.ToDictionary(kv => kv.Value, kv => kv.Key);

    /// <summary>H62 (run 108): the pyramid's renamed clubs, old name → new, for a career saved before the rename.</summary>
    private static readonly Lazy<Dictionary<string, string>> PyramidNewNames = new(() =>
        UsaPyramid.FormerNames.GroupBy(kv => kv.Value).ToDictionary(g => g.Key, g => g.First().Key));

    private static void Rename(Team t, PackClub c)
    {
        if (c.City.Length + c.Name.Length == 0) return;
        t.City = c.City; t.Name = c.Name;
        if (!string.IsNullOrWhiteSpace(c.Abbr)) t.Abbreviation = c.Abbr!.Trim().ToUpperInvariant();
        if (IsColour(c.Primary)) t.PrimaryColor = c.Primary!;
        if (IsColour(c.Secondary)) t.SecondaryColor = c.Secondary!;
    }

    private static bool IsColour(string? s) => s is { Length: 7 } && s[0] == '#' && s.Skip(1).All(Uri.IsHexDigit);
}
