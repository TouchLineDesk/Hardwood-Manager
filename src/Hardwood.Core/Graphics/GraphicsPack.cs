using Hardwood.Core.Model;
using Hardwood.Core.RealWorld;

namespace Hardwood.Core.Graphics;

/// <summary>
/// Danny's own graphics packs, read from his PC at start-up (never shipped in the repo or the release; decision #350).
/// It looks for two folders inside the face-pack folder (or the logo folder):
/// <list type="bullet">
/// <item><c>prographics</c>: <c>logos\NBA_&lt;City&gt;.png</c> (Los Angeles as <c>LosAngelesC</c>/<c>LosAngelesL</c>),
///   <c>logos\banners\NBA_&lt;City&gt;.png</c>, <c>tokens\NBA_&lt;city&gt;_home|road|alt1|alt2.png</c> (jerseys),
///   <c>players\first_last.png</c> (photos) and <c>nonplayers\first_last.png</c> (coaches and executives);</item>
/// <item><c>collegegraphics</c>: <c>logos\1CM_&lt;school&gt;.png</c>, <c>jerseys\1CM_&lt;school&gt;-1|2.png</c>,
///   <c>courts_prior\1CM_&lt;school&gt;.jpg</c> and <c>trophies\*.png</c>.</item>
/// </list>
/// File names are matched with <see cref="LogoPack.Key"/> (letters and digits, lower case). Anything that doesn't match is ignored.
/// </summary>
public sealed class GraphicsPack
{
    public const string ProFolder = "prographics";
    public const string CollegeFolder = "collegegraphics";

    public string Root { get; }
    public IReadOnlyDictionary<string, string> ProLogos => _proLogos;
    public IReadOnlyDictionary<string, string> ProBanners => _proBanners;
    public IReadOnlyDictionary<string, string> ProPhotos => _proPhotos;
    public IReadOnlyDictionary<string, string> StaffPhotos => _staff;
    public IReadOnlyDictionary<string, string> CollegeLogos => _colLogos;
    public IReadOnlyDictionary<string, string> CollegeCourts => _colCourts;
    public IReadOnlyList<string> Trophies => _trophies;
    /// <summary>Jersey icons by city key, then kind ("home", "road", "alt1", "alt2"; college "1", "2").</summary>
    public IReadOnlyDictionary<string, Dictionary<string, string>> ProJerseys => _proJerseys;
    public IReadOnlyDictionary<string, Dictionary<string, string>> CollegeJerseys => _colJerseys;

    private readonly Dictionary<string, string> _proLogos = new(), _proBanners = new(), _proPhotos = new(), _staff = new(),
        _colLogos = new(), _colCourts = new();
    private readonly Dictionary<string, Dictionary<string, string>> _proJerseys = new(), _colJerseys = new();
    private readonly List<string> _trophies = new();

    private GraphicsPack(string root) { Root = root; }

    public static GraphicsPack Empty { get; } = new("");

    public bool IsEmpty => _proLogos.Count + _proPhotos.Count + _colLogos.Count + _proJerseys.Count == 0;

    /// <summary>Finds the pack under any of the folders (the first that has a prographics or collegegraphics subfolder).</summary>
    public static GraphicsPack Find(params string?[] folders)
    {
        foreach (var f in folders)
        {
            if (string.IsNullOrWhiteSpace(f) || !Directory.Exists(f)) continue;
            if (Directory.Exists(Path.Combine(f, ProFolder)) || Directory.Exists(Path.Combine(f, CollegeFolder))) return Scan(f);
        }
        return Empty;
    }

    public static GraphicsPack Scan(string root)
    {
        var g = new GraphicsPack(root);
        try
        {
            var pro = Path.Combine(root, ProFolder);
            if (Directory.Exists(pro))
            {
                Index(Path.Combine(pro, "logos"), "NBA_", g._proLogos);
                Index(Path.Combine(pro, "logos", "banners"), "NBA_", g._proBanners);
                Index(Path.Combine(pro, "players"), "", g._proPhotos);
                Index(Path.Combine(pro, "nonplayers"), "", g._staff);
                foreach (var f in Files(Path.Combine(pro, "tokens")))
                {
                    var n = Path.GetFileNameWithoutExtension(f);
                    if (!n.StartsWith("NBA_", StringComparison.OrdinalIgnoreCase)) continue;
                    int us = n.LastIndexOf('_');
                    if (us <= 4) continue;
                    var city = LogoPack.Key(n[4..us]);
                    var kind = n[(us + 1)..].ToLowerInvariant();
                    if (!g._proJerseys.TryGetValue(city, out var d)) g._proJerseys[city] = d = new();
                    d[kind] = f;
                }
            }
            var col = Path.Combine(root, CollegeFolder);
            if (Directory.Exists(col))
            {
                Index(Path.Combine(col, "logos"), "1CM_", g._colLogos);
                Index(Path.Combine(col, "courts_prior"), "1CM_", g._colCourts);
                foreach (var f in Files(Path.Combine(col, "jerseys")))
                {
                    var n = Path.GetFileNameWithoutExtension(f);
                    if (!n.StartsWith("1CM_", StringComparison.OrdinalIgnoreCase)) continue;
                    int dash = n.LastIndexOf('-');
                    if (dash <= 4) continue;
                    var school = LogoPack.Key(n[4..dash]);
                    if (!g._colJerseys.TryGetValue(school, out var d)) g._colJerseys[school] = d = new();
                    d[n[(dash + 1)..]] = f;
                }
                g._trophies.AddRange(Files(Path.Combine(col, "trophies")).OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
            }
        }
        catch (Exception) { /* an unreadable folder just matches fewer things */ }
        return g;
    }

    private static IEnumerable<string> Files(string dir) =>
        Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg")
            : Enumerable.Empty<string>();

    private static void Index(string dir, string prefix, Dictionary<string, string> into)
    {
        foreach (var f in Files(dir))
        {
            var n = Path.GetFileNameWithoutExtension(f);
            if (prefix.Length > 0)
            {
                if (!n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                n = n[prefix.Length..];
            }
            into.TryAdd(LogoPack.Key(n), f);
        }
    }

    // ---- matching ---------------------------------------------------------------------------

    private static readonly Lazy<Dictionary<string, RealTeam>> RealByName = new(() =>
    {
        try { return RealWorldData.Teams.ToDictionary(t => LogoPack.Key(t.FullName)); }
        catch (Exception) { return new(); }
    });

    private static readonly Lazy<HashSet<string>> RealPlayerNames = new(() =>
    {
        try { return RealPlayers.Lines.Select(l => LogoPack.Key(l.Name)).ToHashSet(); }
        catch (Exception) { return new(); }
    });

    private static readonly Lazy<HashSet<string>> RealSchools = new(() =>
    {
        try { return RealColleges.Programs.Select(p => LogoPack.Key(p.School)).ToHashSet(); }
        catch (Exception) { return new(); }
    });

    /// <summary>The key the pro pack files use for a real NBA team: its city, with C/L for the two Los Angeles teams.</summary>
    public static string? ProKey(Team t)
    {
        if (!RealByName.Value.TryGetValue(LogoPack.Key(t.FullName), out var real)) return null;
        var city = LogoPack.Key(real.City);
        return city == "losangeles" ? city + char.ToLowerInvariant(real.Name[0]) : city;
    }

    /// <summary>Real programs whose short name differs from the college pack's file name ("UConn" is 1CM_connecticut).</summary>
    private static readonly Dictionary<string, string> SchoolAliases = new(StringComparer.Ordinal)
    {
        ["UMBC"] = "mdbaltimorecounty",
        ["UMass Lowell"] = "masslowell",
        ["UAB"] = "alabamabirmingham",
        ["UTSA"] = "texassanantonio",
        ["SMU"] = "southernmethodist",
        ["Queens"] = "queensnc",
        ["Loyola Chicago"] = "loyolail",
        ["VCU"] = "vacommonwealth",
        ["UConn"] = "connecticut",
        ["UNC Asheville"] = "ncasheville",
        ["USC Upstate"] = "socarolinaupstate",
        ["USC"] = "southerncalifornia",
        ["BYU"] = "brighamyoung",
        ["UCF"] = "centralflorida",
        ["TCU"] = "texaschristian",
        ["Cal Baptist"] = "californiabaptist",
        ["CSU Bakersfield"] = "calstbakersfield",
        ["Cal State Fullerton"] = "calstfullerton",
        ["CSUN"] = "calstnorthridge",
        ["UNC Wilmington"] = "ncwilmington",
        ["FIU"] = "floridainternational",
        ["Sam Houston"] = "samhoustonstate",
        ["Penn"] = "pennsylvania",
        ["Bowling Green"] = "bowlinggreenstate",
        ["UMass"] = "massachusetts",
        ["Maryland Eastern Shore"] = "mdeasternshore",
        ["North Carolina Central"] = "nccentral",
        ["UIC"] = "illinoischicago",
        ["UNLV"] = "nevadalasvegas",
        ["UTEP"] = "texaselpaso",
        ["LIU"] = "longisland",
        ["Southeast Missouri"] = "semissouristate",
        ["UT Martin"] = "tennesseemartin",
        ["Loyola Maryland"] = "loyolamd",
        ["LSU"] = "louisianastate",
        ["Ole Miss"] = "mississippi",
        ["The Citadel"] = "citadel",
        ["UNC Greensboro"] = "ncgreensboro",
        ["McNeese"] = "mcneesestate",
        ["Nicholls"] = "nichollsstate",
        ["Southeastern Louisiana"] = "selouisiana",
        ["Texas A&M-Corpus Christi"] = "texasamcc",
        ["UTRGV"] = "texasriogrande",
        ["Grambling State"] = "grambling",
        ["Mississippi Valley State"] = "mississippivalley",
        ["Prairie View A&M"] = "prairieview",
        ["North Dakota State"] = "northdakotast",
        ["Louisiana"] = "louisianalafayette",
        ["Southern Miss"] = "southernmississippi",
        ["UT Arlington"] = "texasarlington",
        ["Seattle U"] = "seattle"
    };

    /// <summary>The key the college pack uses for a real program: its school name, or the pack's spelling of it.</summary>
    public static string? SchoolKey(Team t)
    {
        if (SchoolAliases.TryGetValue(t.City, out var a) && RealSchools.Value.Contains(LogoPack.Key(t.City))) return a;
        var k = LogoPack.Key(t.City);
        return RealSchools.Value.Contains(k) ? k : null;
    }

    /// <summary>A logo for a real NBA franchise or a real college program, or null.</summary>
    public string? LogoFor(Team t)
    {
        if (ProKey(t) is { } pk && _proLogos.TryGetValue(pk, out var f)) return f;
        if (SchoolKey(t) is { } sk && _colLogos.TryGetValue(sk, out f)) return f;
        return null;
    }

    public string? BannerFor(Team t) => ProKey(t) is { } k && _proBanners.TryGetValue(k, out var f) ? f : null;

    /// <summary>Jersey icons for a team, home first (pro: home, road, alt1, alt2; college: 1, 2).</summary>
    public IReadOnlyList<string> JerseysFor(Team t)
    {
        if (ProKey(t) is { } pk)
        {
            if (_proJerseys.TryGetValue(pk, out var d))
                return new[] { "home", "road", "alt1", "alt2" }.Where(d.ContainsKey).Select(k => d[k]).ToList();
            return Array.Empty<string>();
        }
        if (SchoolKey(t) is { } sk && _colJerseys.TryGetValue(sk, out var c))
            return c.OrderBy(x => x.Key).Select(x => x.Value).ToList();
        return Array.Empty<string>();
    }

    /// <summary>A photo for a real NBA player (only names in the real-player data, so a generated namesake never gets one).</summary>
    public string? PhotoFor(Player p)
    {
        var k = LogoPack.Key(p.Name);
        return RealPlayerNames.Value.Contains(k) && _proPhotos.TryGetValue(k, out var f) ? f : null;
    }

    /// <summary>A photo for a real person by name (head coaches, executives).</summary>
    public string? StaffPhotoFor(string? name) =>
        name != null && _staff.TryGetValue(LogoPack.Key(name), out var f) ? f : null;

    /// <summary>One line for Settings: what the pack holds.</summary>
    public string Summary() => IsEmpty ? "No graphics pack found"
        : $"{_proLogos.Count} NBA logos · {_proJerseys.Count} NBA jersey sets · {_proBanners.Count} banners · {_proPhotos.Count} player photos · "
          + $"{_staff.Count} staff photos · {_colLogos.Count} college logos · {_colJerseys.Count} college kits · {_colCourts.Count} courts · {_trophies.Count} trophies";
}
