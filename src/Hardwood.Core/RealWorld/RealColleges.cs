using System.Text.Json;
using Hardwood.Core.College;

namespace Hardwood.Core.RealWorld;

/// <summary>One real Division I program (data pack: Data/ncaa_programs.json). The players stay generated (Danny, #233).</summary>
public sealed record RealProgram(string School, string Nickname, string Conference, string State, string Arena,
    int Appearances, int FinalFours, int Titles, int Recent, string Primary, string Secondary);

/// <summary>One real conference in its 2026-27 alignment.</summary>
public sealed record RealConference(string Key, string Name, string Short, bool Power);

/// <summary>
/// R3 (Danny, #233; full Division I first, #247): every Division I men's program in its 2026-27 conference, with its
/// nickname, state, home arena, colours and NCAA tournament history. A real-world career's college world is built from
/// these (<see cref="CollegeWorld.Generate"/> with <c>real</c>); rosters, recruiting, the AD and March Madness are unchanged,
/// and every real conference's tournament champion takes an automatic bid.
/// </summary>
public static class RealColleges
{
    /// <summary>Real conferences' keys carry this prefix, so they never meet the fictional ones'.</summary>
    public const string KeyPrefix = "r-";

    private sealed record Pack(List<RealConference> Conferences, List<RealProgram> Programs, string Checked, string Source, string Notes);
    private static readonly Lazy<Pack> _pack = new(Load);
    private static readonly Lazy<Dictionary<string, string>> _states = new(() =>
        Programs.ToDictionary(p => p.School, p => p.State, StringComparer.Ordinal));

    public static IReadOnlyList<RealProgram> Programs => _pack.Value.Programs;
    public static IReadOnlyList<RealConference> Conferences => _pack.Value.Conferences;
    public static string Checked => _pack.Value.Checked;
    public static string Source => _pack.Value.Source;

    /// <summary>The game's conference records for the real conferences (keys prefixed).</summary>
    public static IEnumerable<ConferenceInfo> ConferenceInfos() =>
        Conferences.Select(c => new ConferenceInfo(KeyPrefix + c.Key, c.Name, c.Short, c.Power, Programs.Count(p => p.Conference == c.Key), Real: true));

    /// <summary>A real program's home state by its school name, or null for any other name.</summary>
    public static string? StateOf(string school) => _states.Value.TryGetValue(school, out var s) ? s : null;

    private static readonly Lazy<Dictionary<string, string>> _arenas = new(() =>
        Programs.Where(p => !string.IsNullOrWhiteSpace(p.Arena)).ToDictionary(p => p.School, p => p.Arena, StringComparer.Ordinal));

    /// <summary>A real program's home arena by its school name (C5 part 2), or null.</summary>
    public static string? ArenaOf(string school) => _arenas.Value.TryGetValue(school, out var a) ? a : null;

    /// <summary>
    /// Starting prestige from real standing: the conference it plays in today, its NCAA tournament history (appearances,
    /// Final Fours, titles) and its recent record (appearances 2016-2026). Soft-capped near the top, so the blue bloods sit
    /// around 85-90 rather than all at the ceiling.
    /// </summary>
    public static double Prestige(RealProgram p)
    {
        var conf = Conferences.First(c => c.Key == p.Conference);
        double baseLevel = conf.Power ? 45 : TopMid.Contains(p.Conference) ? 28 : 18;
        double raw = baseLevel + 2.6 * Math.Sqrt(p.Appearances) + 0.5 * p.FinalFours + 0.7 * p.Titles + 3.0 * p.Recent;
        double soft = raw <= 70 ? raw : 70 + 25 * (1 - Math.Exp(-(raw - 70) / 25));
        return Math.Round(Math.Clamp(soft, 15, 95), 1);
    }

    /// <summary>The strongest of the other conferences (multi-bid leagues in recent years).</summary>
    private static readonly HashSet<string> TopMid = new() { "aac", "a10", "mwc", "wcc", "pac", "mvc" };

    /// <summary>
    /// H149 (run 133, critic review #18 item 4): the short names fans use for the best-known programs (school initials and
    /// common short forms, as on a scoreboard), so the Top 25's crests read "UK" and "UNC", not "KENT" or a clipped "UCON".
    /// Everyone else keeps the generated form below. New careers only: a saved world keeps its abbreviations.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Known = new Dictionary<string, string>
    {
        ["Kentucky"] = "UK", ["Texas"] = "TEX", ["Houston"] = "HOU", ["UConn"] = "UCONN", ["Miami (FL)"] = "MIA", ["Miami (OH)"] = "M-OH",
        ["Notre Dame"] = "ND", ["Oklahoma State"] = "OKST", ["Duke"] = "DUKE", ["North Carolina"] = "UNC", ["Kansas"] = "KU",
        ["Gonzaga"] = "GONZ", ["Purdue"] = "PUR", ["Wisconsin"] = "WIS", ["Arizona"] = "ARIZ", ["UCLA"] = "UCLA", ["Villanova"] = "NOVA",
        ["Michigan State"] = "MSU", ["Michigan"] = "MICH", ["Ohio State"] = "OSU", ["Indiana"] = "IU", ["Louisville"] = "LOU",
        ["Syracuse"] = "CUSE", ["Tennessee"] = "TENN", ["Auburn"] = "AUB", ["Alabama"] = "BAMA", ["Florida"] = "UF", ["Baylor"] = "BAY",
        ["Texas Tech"] = "TTU", ["Arkansas"] = "ARK", ["Iowa State"] = "ISU", ["Marquette"] = "MARQ", ["Creighton"] = "CREI",
        ["Illinois"] = "ILL", ["Iowa"] = "IOWA", ["Virginia"] = "UVA", ["Virginia Tech"] = "VT", ["Florida State"] = "FSU",
        ["Georgetown"] = "GTWN", ["St. John's"] = "SJU", ["Xavier"] = "XAV", ["Cincinnati"] = "CIN", ["Memphis"] = "MEM",
        ["San Diego State"] = "SDSU", ["Saint Mary's"] = "SMC", ["BYU"] = "BYU", ["Oregon"] = "ORE", ["USC"] = "USC", ["Maryland"] = "MD",
        ["Penn State"] = "PSU", ["Rutgers"] = "RUTG", ["Minnesota"] = "MINN", ["Nebraska"] = "NEB", ["Northwestern"] = "NU",
        ["Clemson"] = "CLEM", ["NC State"] = "NCST", ["Wake Forest"] = "WAKE", ["Pittsburgh"] = "PITT", ["Boston College"] = "BC",
        ["Georgia Tech"] = "GT", ["SMU"] = "SMU", ["Stanford"] = "STAN", ["California"] = "CAL", ["LSU"] = "LSU", ["Ole Miss"] = "MISS",
        ["Mississippi State"] = "MSST", ["Missouri"] = "MIZ", ["South Carolina"] = "SC", ["Georgia"] = "UGA", ["Vanderbilt"] = "VAN",
        ["Texas A&M"] = "TAMU", ["Oklahoma"] = "OU", ["TCU"] = "TCU", ["West Virginia"] = "WVU", ["Kansas State"] = "KSU",
        ["Utah"] = "UTAH", ["Colorado"] = "COLO", ["Arizona State"] = "ASU", ["UCF"] = "UCF", ["Butler"] = "BUT", ["Providence"] = "PROV",
        ["Seton Hall"] = "HALL", ["DePaul"] = "DEP", ["Dayton"] = "DAY", ["VCU"] = "VCU", ["Wichita State"] = "WICH",
        ["Washington"] = "UW", ["Oregon State"] = "ORST", ["Washington State"] = "WSU", ["Utah State"] = "USU", ["New Mexico"] = "UNM",
        ["Nevada"] = "NEV", ["UNLV"] = "UNLV",

        // H149 (run 138): every other Division I program gets its scoreboard short name too, so no crest reads a cut-off
        // word ("ASTA" for Appalachian State, "ASTAT" for Arkansas State).
        ["Abilene Christian"] = "ACU", ["Air Force"] = "AFA", ["Akron"] = "AKR", ["Alabama A&M"] = "AAMU", ["Alabama State"] = "ALST",
        ["Albany"] = "ALB", ["Alcorn State"] = "ALCN", ["American"] = "AMER", ["Appalachian State"] = "APP", ["Arkansas State"] = "ARST",
        ["Arkansas-Pine Bluff"] = "UAPB", ["Army"] = "ARMY", ["Austin Peay"] = "APSU", ["Ball State"] = "BALL", ["Bellarmine"] = "BELL",
        ["Belmont"] = "BEL", ["Bethune-Cookman"] = "BCU", ["Binghamton"] = "BING", ["Boise State"] = "BSU", ["Boston University"] = "BU",
        ["Bowling Green"] = "BGSU", ["Bradley"] = "BRAD", ["Brown"] = "BRWN", ["Bryant"] = "BRY", ["Bucknell"] = "BUCK",
        ["Buffalo"] = "BUFF", ["CSU Bakersfield"] = "CSUB", ["CSUN"] = "CSUN", ["Cal Baptist"] = "CBU", ["Cal Poly"] = "CP",
        ["Cal State Fullerton"] = "CSUF", ["Campbell"] = "CAMP", ["Canisius"] = "CAN", ["Central Arkansas"] = "CARK",
        ["Central Connecticut"] = "CCSU", ["Central Michigan"] = "CMU", ["Charleston"] = "COFC", ["Charleston Southern"] = "CHSO",
        ["Charlotte"] = "CLT", ["Chattanooga"] = "UTC", ["Chicago State"] = "CHIC", ["Cleveland State"] = "CLEV",
        ["Coastal Carolina"] = "CCU", ["Colgate"] = "COLG", ["Colorado State"] = "CSU", ["Columbia"] = "CLMB", ["Coppin State"] = "COPP",
        ["Cornell"] = "COR", ["Dartmouth"] = "DART", ["Davidson"] = "DAV", ["Delaware"] = "DEL", ["Delaware State"] = "DSU",
        ["Denver"] = "DEN", ["Detroit Mercy"] = "DET", ["Drake"] = "DRKE", ["Drexel"] = "DREX", ["Duquesne"] = "DUQ",
        ["East Carolina"] = "ECU", ["East Tennessee State"] = "ETSU", ["East Texas A&M"] = "ETAM", ["Eastern Illinois"] = "EIU",
        ["Eastern Kentucky"] = "EKU", ["Eastern Michigan"] = "EMU", ["Eastern Washington"] = "EWU", ["Elon"] = "ELON",
        ["Evansville"] = "EVAN", ["FIU"] = "FIU", ["Fairfield"] = "FAIR", ["Fairleigh Dickinson"] = "FDU", ["Florida A&M"] = "FAMU",
        ["Florida Atlantic"] = "FAU", ["Florida Gulf Coast"] = "FGCU", ["Fordham"] = "FOR", ["Fresno State"] = "FRES", ["Furman"] = "FUR",
        ["Gardner-Webb"] = "GWEB", ["George Mason"] = "GMU", ["George Washington"] = "GW", ["Georgia Southern"] = "GASO",
        ["Georgia State"] = "GAST", ["Grambling State"] = "GRAM", ["Grand Canyon"] = "GCU", ["Green Bay"] = "GB", ["Hampton"] = "HAMP",
        ["Harvard"] = "HARV", ["Hawaii"] = "HAW", ["High Point"] = "HPU", ["Hofstra"] = "HOF", ["Holy Cross"] = "HC",
        ["Houston Christian"] = "HCU", ["Howard"] = "HOW", ["IU Indy"] = "IUI", ["Idaho"] = "IDHO", ["Idaho State"] = "IDST",
        ["Illinois State"] = "ILST", ["Incarnate Word"] = "UIW", ["Indiana State"] = "INST", ["Iona"] = "IONA", ["Jackson State"] = "JKST",
        ["Jacksonville"] = "JAX", ["Jacksonville State"] = "JVST", ["James Madison"] = "JMU", ["Kansas City"] = "KC",
        ["Kennesaw State"] = "KENN", ["Kent State"] = "KENT", ["LIU"] = "LIU", ["La Salle"] = "LAS", ["Lafayette"] = "LAF",
        ["Lamar"] = "LAM", ["Le Moyne"] = "LEM", ["Lehigh"] = "LEH", ["Liberty"] = "LIB", ["Lindenwood"] = "LIN", ["Lipscomb"] = "LIP",
        ["Little Rock"] = "UALR", ["Long Beach State"] = "LBSU", ["Longwood"] = "LONG", ["Louisiana"] = "ULL", ["Louisiana Tech"] = "LT",
        ["Louisiana-Monroe"] = "ULM", ["Loyola Chicago"] = "LUC", ["Loyola Maryland"] = "LMD", ["Loyola Marymount"] = "LMU",
        ["Maine"] = "ME", ["Manhattan"] = "MAN", ["Marist"] = "MRST", ["Marshall"] = "MRSH", ["Maryland Eastern Shore"] = "UMES",
        ["McNeese"] = "MCN", ["Mercer"] = "MER", ["Mercyhurst"] = "MHU", ["Merrimack"] = "MRMK", ["Middle Tennessee"] = "MTSU",
        ["Milwaukee"] = "MILW", ["Mississippi Valley State"] = "MVSU", ["Missouri State"] = "MOST", ["Monmouth"] = "MONM",
        ["Montana"] = "MONT", ["Montana State"] = "MTST", ["Morehead State"] = "MORE", ["Morgan State"] = "MORG",
        ["Mount St. Mary's"] = "MSM", ["Murray State"] = "MUR", ["NJIT"] = "NJIT", ["Navy"] = "NAVY", ["New Hampshire"] = "UNH",
        ["New Haven"] = "NHVN", ["New Mexico State"] = "NMSU", ["New Orleans"] = "UNO", ["Niagara"] = "NIA", ["Nicholls"] = "NICH",
        ["Norfolk State"] = "NSU", ["North Alabama"] = "UNA", ["North Carolina A&T"] = "NCAT", ["North Carolina Central"] = "NCCU",
        ["North Dakota"] = "UND", ["North Dakota State"] = "NDSU", ["North Florida"] = "UNF", ["North Texas"] = "UNT",
        ["Northeastern"] = "NE", ["Northern Arizona"] = "NAU", ["Northern Colorado"] = "UNCO", ["Northern Illinois"] = "NIU",
        ["Northern Iowa"] = "UNI", ["Northern Kentucky"] = "NKU", ["Northwestern State"] = "NWST", ["Oakland"] = "OAK", ["Ohio"] = "OHIO",
        ["Old Dominion"] = "ODU", ["Omaha"] = "OMA", ["Oral Roberts"] = "ORU", ["Pacific"] = "PAC", ["Penn"] = "PENN",
        ["Pepperdine"] = "PEPP", ["Portland"] = "PORT", ["Portland State"] = "PRST", ["Prairie View A&M"] = "PV", ["Presbyterian"] = "PRE",
        ["Princeton"] = "PRIN", ["Purdue Fort Wayne"] = "PFW", ["Queens"] = "QUC", ["Quinnipiac"] = "QUIN", ["Radford"] = "RAD",
        ["Rhode Island"] = "URI", ["Rice"] = "RICE", ["Richmond"] = "RICH", ["Rider"] = "RID", ["Robert Morris"] = "RMU",
        ["SIU Edwardsville"] = "SIUE", ["Sacramento State"] = "SAC", ["Sacred Heart"] = "SHU", ["Saint Joseph's"] = "JOES",
        ["Saint Louis"] = "SLU", ["Saint Peter's"] = "SPU", ["Sam Houston"] = "SHSU", ["Samford"] = "SAM", ["San Diego"] = "USD",
        ["San Francisco"] = "SF", ["San Jose State"] = "SJSU", ["Santa Clara"] = "SCU", ["Seattle U"] = "SEA", ["Siena"] = "SIE",
        ["South Alabama"] = "USA", ["South Carolina State"] = "SCST", ["South Dakota"] = "SDAK", ["South Dakota State"] = "SDST",
        ["South Florida"] = "USF", ["Southeast Missouri"] = "SEMO", ["Southeastern Louisiana"] = "SELA", ["Southern"] = "SOU",
        ["Southern Illinois"] = "SIU", ["Southern Indiana"] = "SIND", ["Southern Miss"] = "USM", ["Southern Utah"] = "SUU",
        ["St. Bonaventure"] = "SBU", ["St. Thomas"] = "STMN", ["Stephen F. Austin"] = "SFA", ["Stetson"] = "STET", ["Stonehill"] = "STO",
        ["Stony Brook"] = "STBK", ["Tarleton State"] = "TAR", ["Temple"] = "TEM", ["Tennessee State"] = "TNST", ["Tennessee Tech"] = "TNTC",
        ["Texas A&M-Corpus Christi"] = "AMCC", ["Texas Southern"] = "TXSO", ["Texas State"] = "TXST", ["The Citadel"] = "CIT",
        ["Toledo"] = "TOL", ["Towson"] = "TOW", ["Troy"] = "TROY", ["Tulane"] = "TUL", ["Tulsa"] = "TLSA", ["UAB"] = "UAB",
        ["UC Davis"] = "UCD", ["UC Irvine"] = "UCI", ["UC Riverside"] = "UCR", ["UC San Diego"] = "UCSD", ["UC Santa Barbara"] = "UCSB",
        ["UIC"] = "UIC", ["UMBC"] = "UMBC", ["UMass"] = "MASS", ["UMass Lowell"] = "UML", ["UNC Asheville"] = "UNCA",
        ["UNC Greensboro"] = "UNCG", ["UNC Wilmington"] = "UNCW", ["USC Upstate"] = "SCUP", ["UT Arlington"] = "UTA", ["UT Martin"] = "UTM",
        ["UTEP"] = "UTEP", ["UTRGV"] = "RGV", ["UTSA"] = "UTSA", ["Utah Tech"] = "UTU", ["Utah Valley"] = "UVU", ["VMI"] = "VMI",
        ["Valparaiso"] = "VALP", ["Vermont"] = "UVM", ["Wagner"] = "WAG", ["Weber State"] = "WEB", ["West Florida"] = "UWF",
        ["West Georgia"] = "UWG", ["Western Carolina"] = "WCU", ["Western Illinois"] = "WIU", ["Western Kentucky"] = "WKU",
        ["Western Michigan"] = "WMU", ["William & Mary"] = "W&M", ["Winthrop"] = "WIN", ["Wofford"] = "WOF", ["Wright State"] = "WRST",
        ["Wyoming"] = "WYO", ["Yale"] = "YALE", ["Youngstown State"] = "YSU",
    };

    /// <summary>Short, unique abbreviations: a known short name, an acronym kept as is, initials for longer names, else the first four letters.</summary>
    public static Dictionary<string, string> Abbreviations()
    {
        var used = new HashSet<string>();
        var result = new Dictionary<string, string>();
        foreach (var p in Programs.OrderByDescending(Prestige))
        {
            string s = p.School;
            var words = s.Replace("(", "").Replace(")", "").Replace("&", "").Replace("-", " ").Replace(".", "")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w is not ("of" or "The")).ToList();
            string abbr = Known.TryGetValue(s, out var known) ? known
                : s.Length <= 5 && s == s.ToUpperInvariant() ? s.Replace(" ", "")
                : words.Count >= 2 ? string.Concat(words.Select(w => char.ToUpperInvariant(w[0])))
                : new string(s.Where(char.IsLetter).Take(4).ToArray()).ToUpperInvariant();
            if (abbr.Length < 2) abbr = s[..Math.Min(3, s.Length)].ToUpperInvariant();
            string tryAbbr = abbr;
            // Collisions: lengthen with the next letters of the last word.
            string tail = new string(words.Last().Skip(1).Where(char.IsLetter).ToArray()).ToUpperInvariant();
            for (int i = 0; !used.Add(tryAbbr); i++)
                tryAbbr = i < tail.Length ? abbr + tail[..(i + 1)] : abbr + (char)('A' + i - tail.Length);
            result[s] = tryAbbr;
        }
        return result;
    }

    private static Pack Load()
    {
        var asm = typeof(RealColleges).Assembly;
        string name = asm.GetManifestResourceNames().Single(n => n.EndsWith("ncaa_programs.json", StringComparison.Ordinal));
        using var stream = asm.GetManifestResourceStream(name)!;
        return JsonSerializer.Deserialize<Pack>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }
}
