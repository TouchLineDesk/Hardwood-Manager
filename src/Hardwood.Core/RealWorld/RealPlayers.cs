using System.Text.Json;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;

namespace Hardwood.Core.RealWorld;

/// <summary>One real NBA player's line in the data pack (Data/nba_players.json): this season so far and last season (with last season's team; "2TM" when traded).
/// <paramref name="NotYet"/>: on a real roster but hadn't played in 2026-27 when the data was checked (R2 part 2b); <paramref name="Cur"/> is then null,
/// and a player with no 2025-26 NBA line either (a 2026 rookie, or out all last season) has no <paramref name="Prev"/> and is rated at bench level.</summary>
public sealed record RealPlayerLine(string Name, int Age, string Team, string Pos, double?[]? Cur, double?[]? Prev, string? PrevTeam = null, bool NotYet = false);

/// <summary>A team's real record in a season (data pack).</summary>
public sealed record RealRecordLine(string Team, int W, int L)
{
    public double Pct => W + L == 0 ? 0.5 : (double)W / (W + L);
}

/// <summary>What the stats say about one real player, before he is turned into game ratings.</summary>
public sealed record RealRating(RealPlayerLine Line, double Per36, double Minutes, double Score)
{
    /// <summary>Per-36 box numbers, blended across this season and last (last season counts 70%).</summary>
    public required Dictionary<string, double> Rates { get; init; }
}

/// <summary>
/// The real NBA's players (R2 part 1, decisions #204-#209): every player who has played in 2026-27, on his real team,
/// rated from his real box-score numbers. The stats-to-ratings converter ranks the players by production per minute
/// (Game Score per 36, shrunk toward a bench level for small samples) plus the minutes their coaches give them, then
/// hands out the generated league's own Current Ability values in that order, so the real league has exactly the
/// spread of talent the game is balanced for. The attributes are then shaped by what each player does: shooting from
/// his percentages, playmaking from assists, rebounding, steals and blocks from his rates. Since R2 part 2a his
/// contract, birthday, height and birth country are real (Data/nba_rosters.json), and each club has its real head coach.
/// Since R2 part 2b the players on real rosters who hadn't played yet this season are in too (rated from last season),
/// and the leading veterans' careers carry in (<see cref="RealCareers"/>).
/// </summary>
public static class RealPlayers
{
    private static readonly Lazy<Pack> _pack = new(Load);
    public static IReadOnlyList<RealPlayerLine> Lines => _pack.Value.Players;
    public static IReadOnlyList<RealRecordLine> LastSeason => _pack.Value.Last;
    public static IReadOnlyList<RealRecordLine> ThisSeason => _pack.Value.This;
    public static string Checked => _pack.Value.Checked;
    public static string Source => _pack.Value.Source;

    /// <summary>A full roster; extra real players (two-way and end-of-bench) are left out, the weakest first.</summary>
    public const int RosterMax = 15;
    /// <summary>Last season's numbers count this much against this season's, minute for minute.</summary>
    public const double PrevWeight = 0.7;
    /// <summary>Small samples are pulled toward a bench player's production (Game Score 7 per 36) by this many minutes.</summary>
    public const double PriorMinutes = 250, PriorPer36 = 7;
    /// <summary>Each minute per game his coaches give him is worth this much Game Score per 36 in the ranking.</summary>
    public const double MinutesWeight = 0.35;
    /// <summary>Team context, as box plus-minus does it: box scores miss defence and team play, so a player's ranking
    /// moves with how his team did while he played (win % above .500, times his minutes share).</summary>
    public const double ContextWeight = 16;
    /// <summary>A real player's room to grow is this share of what the generator would give a made-up player of his age
    /// and ability (#227): a real 22-year-old already playing like an All-Star is a known quantity, not a lottery ticket,
    /// and full headroom put two real 22-year-olds at PA 200 and made dynasties of their clubs in the audit.</summary>
    public const double PotentialHeadroom = 0.75;

    private static readonly string[] Cols = { "G", "MP", "FGA", "FG%", "3PA", "3P%", "FTA", "FT%", "ORB", "DRB", "AST", "STL", "BLK", "TOV", "PTS" };

    /// <summary>Rate every real player from his lines.</summary>
    public static List<RealRating> Rate() => Lines.Select(Rate).ToList();

    public static RealRating Rate(RealPlayerLine p)
    {
        double totMin = 0, totGs = 0, games = 0, mpgW = 0, ctx = 0;
        var tot = Cols.Skip(2).ToDictionary(c => c, _ => 0.0);
        void Add(double?[]? row, double w, double context)
        {
            if (row == null) return;
            double g = row[0] ?? 0, mp = row[1] ?? 0;
            if (g <= 0) return;
            double V(string c) => row[Array.IndexOf(Cols, c)] ?? 0;
            double fga = V("FGA"), fgm = fga * V("FG%"), fta = V("FTA"), ftm = fta * V("FT%");
            double gs = V("PTS") + 0.4 * fgm - 0.7 * fga - 0.4 * (fta - ftm) + 0.7 * V("ORB") + 0.3 * V("DRB")
                      + V("STL") + 0.7 * V("AST") + 0.7 * V("BLK") - V("TOV");
            totMin += w * g * mp; totGs += w * g * gs; games += w * g; mpgW += w * g * mp; ctx += w * g * mp * context;
            foreach (var c in tot.Keys.ToList())
            {
                // Percentages are weighted by attempts; counts by games.
                tot[c] += c switch
                {
                    "FG%" => w * g * fgm, "FT%" => w * g * ftm, "3P%" => w * g * V("3PA") * V("3P%"),
                    _ => w * g * V(c),
                };
            }
        }
        Add(p.Cur, 1.0, Context(ThisSeason, p.Team, 10));
        Add(p.Prev, PrevWeight, Context(LastSeason, p.PrevTeam, 0));
        double per36 = (totGs + PriorPer36 / 36 * PriorMinutes) / (totMin + PriorMinutes) * 36;
        double mpg = games > 0 ? mpgW / games : 0;
        var rates = new Dictionary<string, double>();
        double per = totMin > 0 ? 36 / totMin : 0;
        foreach (var c in new[] { "FGA", "3PA", "FTA", "ORB", "DRB", "AST", "STL", "BLK", "TOV", "PTS" }) rates[c] = tot[c] * per;
        rates["FG%"] = tot["FGA"] > 0 ? tot["FG%"] / tot["FGA"] : 0.45;
        rates["FT%"] = tot["FTA"] > 0 ? tot["FT%"] / tot["FTA"] : 0.75;
        rates["3P%"] = tot["3PA"] > 0 ? tot["3P%"] / tot["3PA"] : 0.33;
        rates["Min"] = totMin;
        double context = totMin > 0 ? ctx / totMin : 0;
        return new RealRating(p, per36, mpg, per36 + MinutesWeight * mpg + ContextWeight * context * Math.Min(1, mpg / 30)) { Rates = rates };
    }

    /// <summary>How far above .500 a team was (a short record is pulled toward .500 by <paramref name="pseudo"/> games each way).</summary>
    private static double Context(IReadOnlyList<RealRecordLine> records, string? team, int pseudo)
    {
        var r = records.FirstOrDefault(x => x.Team == team);
        return r == null ? 0 : (r.W + pseudo / 2.0) / (r.W + r.L + pseudo) - 0.5;
    }

    /// <summary>
    /// Put the real players on a freshly generated league whose teams are already the real franchises (see
    /// <see cref="RealWorldData.Apply"/>). Each real player takes over a generated player's id (so nothing else in the
    /// world can clash with him); a team with fewer than 15 real players keeps its weakest generated players to fill up.
    /// </summary>
    public static void Apply(League league, ulong seed)
    {
        var teams = league.Teams.Take(RealWorldData.Teams.Count).ToList();
        var byAbbr = teams.ToDictionary(t => t.Abbreviation);
        // The talent ladder the game is balanced for: every CA the generator dealt the 30 teams, best first.
        var ladder = teams.SelectMany(t => t.Roster).Select(p => p.CurrentAbility).OrderByDescending(x => x).ToList();
        var rated = Rate().Where(r => byAbbr.ContainsKey(r.Line.Team)).ToList();
        var kept = rated.GroupBy(r => r.Line.Team).SelectMany(g => g.OrderByDescending(r => r.Score).Take(RosterMax)).ToList();
        // The ladder is dealt to the players who make a roster (R2 part 2b: with the 68 not yet seen this season there
        // are more real players than places, and the ones left out mustn't take the bottom of the ladder with them).
        var order = kept.OrderByDescending(r => r.Score).ToList();
        var caOf = new Dictionary<RealRating, int>();
        for (int i = 0; i < order.Count; i++)
            caOf[order[i]] = ladder[(int)Math.Round((double)i / Math.Max(1, order.Count - 1) * (ladder.Count - 1))];

        var ageDay = new DateOnly(league.Season + 1, 2, 1); // Basketball-Reference ages are on February 1 of the season
        var rng = new Rng(seed ^ 0x5245414CUL);
        var ids = new Dictionary<string, int>();
        foreach (var team in teams)
        {
            var mine = kept.Where(r => r.Line.Team == team.Abbreviation).OrderByDescending(r => r.Score).ToList();
            if (mine.Count == 0) continue;
            // The generated players give up their places (and ids) best first; the weakest stay on as the end of the bench.
            var slots = team.Roster.OrderByDescending(p => p.CurrentAbility).Take(mine.Count).ToList();
            foreach (var (old, real) in slots.Zip(mine))
            {
                var p = Build(real, caOf[real], old.Id, rng, ageDay, league);
                p.TeamId = team.Id;
                p.Contract = RealContract(real.Line.Name, league) ?? new Contract
                {
                    // Not on the contracts page: a two-way or Exhibit 10 deal. The league minimum for this season.
                    Years = 1, Salary = league.Finance.MinSalary, StartSeason = league.Season,
                };
                int at = team.Roster.IndexOf(old);
                team.Roster[at] = p;
                ids[real.Line.Name] = p.Id;
                // His real career so far (R2 part 2b, #224): the records book, milestones and Hall of Fame start from it.
                if (RealCareers.Totals(real.Line, p.Id) is { } career) league.Careers[p.Id] = career;
                team.Tactic.Roles.Remove(old.Id);
            }
            team.DepthChart = null;
            // Real salaries are kept as they are (#217): the real 2026-27 cap ($164.96M) is the game's own $165M, so a
            // real payroll means the same thing here as in the NBA. Clubs open over their owners' wage share and work
            // back down as deals run out, as real teams over the cap do.
            if (CoachesByTeam.TryGetValue(team.Abbreviation, out var coach)) team.CoachName = coach;
        }
        // The real record book, with LeBron's id on his career points (#225).
        RealRecordChase.Seed(league.Records, ids);
    }

    /// <summary>His real deal (#217): this season's salary, for every season the contracts page lists. Null if the
    /// source has no salary for him.</summary>
    public static Contract? RealContract(string name, League league) =>
        Bios.TryGetValue(name, out var b) && b.Salary is long salary
            ? new Contract
            {
                Years = Math.Clamp(b.Years ?? 1, 1, 6),
                Salary = Math.Max(league.Finance.MinSalary, (long)Math.Round(salary / 10_000.0) * 10_000),
                StartSeason = league.Season,
            }
            : null;

    private static Position PosOf(string pos) => pos switch
    {
        "PG" => Position.PG, "SG" => Position.SG, "SF" => Position.SF, "PF" => Position.PF, _ => Position.C,
    };

    /// <summary>Build the game's player for one real player: a generated body at his CA, then attributes and tendencies
    /// shaped by his numbers, re-centred so his attribute mean still matches his CA.</summary>
    public static Player Build(RealRating r, int ca, int id, Rng rng, DateOnly ageDay, League? league = null)
    {
        var pos = PosOf(r.Line.Pos);
        var gen = new PlayerGenerator(rng, id);
        var t = gen.Generate(pos, ca, r.Line.Age, ageDay);
        var (first, last) = Split(r.Line.Name);
        Bios.TryGetValue(r.Line.Name, out var bio);
        string nat = Nationality(r.Line.Name, bio?.Birth);
        // Real birthday and height when the roster page has them (#218); the generated build moves with the height.
        int height = bio?.HeightCm ?? t.HeightCm, grow = height - t.HeightCm;
        var p = new Player
        {
            Id = id, FirstName = first, LastName = last,
            BirthDate = bio?.Born ?? t.BirthDate, Nationality = nat, Ethnicity = Heritages.Backfill(id, nat),
            HeightCm = height, WingspanCm = t.WingspanCm + grow, WeightKg = Math.Max(70, t.WeightKg + (int)Math.Round(grow * 1.1)),
            PrimaryPosition = pos, SecondaryPosition = t.SecondaryPosition,
            CurrentAbility = t.CurrentAbility, PotentialAbility = t.CurrentAbility + (int)Math.Round((t.PotentialAbility - t.CurrentAbility) * PotentialHeadroom),
            Attributes = t.Attributes, Hidden = t.Hidden, Tendencies = t.Tendencies,
        };
        Shape(p, r);
        return p;
    }

    /// <summary>Nudge the attributes toward what his numbers show, then restore the attribute mean his CA implies.</summary>
    public static void Shape(Player p, RealRating r)
    {
        var a = p.Attributes;
        double before = AttrInfo.All.Average(x => (double)a[x]);
        var R = r.Rates;
        double min = R["Min"];
        double Shrink(double rate, double prior, double weightMin = 600) => (rate * min + prior * weightMin) / (min + weightMin);
        // League-typical rates per 36 by position (2025-26 shape): assists, offensive and defensive boards, steals, blocks.
        var (ast, orb, drb, stl, blk) = p.PrimaryPosition switch
        {
            Position.PG => (6.8, 0.8, 3.6, 1.3, 0.4),
            Position.SG => (3.8, 0.8, 3.6, 1.2, 0.4),
            Position.SF => (2.8, 1.2, 4.5, 1.1, 0.6),
            Position.PF => (2.6, 2.0, 5.8, 1.0, 0.9),
            _ => (2.6, 3.2, 7.6, 0.9, 1.5),
        };
        void Nudge(Attr x, double z, double scale = 2.0) => a[x] = (int)Math.Round(Math.Clamp(a[x] + Math.Clamp(z, -2.5, 2.5) * scale, 1, 20));
        double tpPct = Shrink(R["3P%"] * Math.Min(1, R["3PA"] / 4.0) + 0.33 * (1 - Math.Min(1, R["3PA"] / 4.0)), 0.34);
        double tpVol = R["3PA"];
        Nudge(Attr.ThreePoint, (tpPct - 0.35) / 0.035 * 0.7 + (tpVol - 5) / 3.0 * 0.5);
        Nudge(Attr.CatchAndShoot, (tpPct - 0.35) / 0.035 * 0.7 + (tpVol - 5) / 3.0 * 0.3);
        Nudge(Attr.FreeThrow, (Shrink(R["FT%"], 0.77, 300) - 0.77) / 0.07);
        double astZ = (Shrink(R["AST"], ast) - ast) / Math.Max(1.2, ast * 0.4);
        Nudge(Attr.Passing, astZ); Nudge(Attr.CourtVision, astZ); Nudge(Attr.PickAndRollHandler, astZ * 0.7);
        Nudge(Attr.OffensiveRebounding, (Shrink(R["ORB"], orb) - orb) / Math.Max(0.6, orb * 0.45));
        Nudge(Attr.DefensiveRebounding, (Shrink(R["DRB"], drb) - drb) / Math.Max(1.0, drb * 0.3));
        Nudge(Attr.Steal, (Shrink(R["STL"], stl) - stl) / 0.4);
        Nudge(Attr.Block, (Shrink(R["BLK"], blk) - blk) / Math.Max(0.35, blk * 0.5));
        double usage = Shrink(R["FGA"] + 0.44 * R["FTA"], 13);
        Nudge(Attr.ShotCreation, (usage - 14) / 3.5);
        Nudge(Attr.Composure, (usage - 14) / 5.0, 1.0);
        double twoA = Math.Max(0.1, R["FGA"] - R["3PA"]);
        double twoPct = (R["FG%"] * R["FGA"] - R["3P%"] * R["3PA"]) / twoA;
        double finish = Shrink(twoPct, 0.53);
        Nudge(Attr.Layup, (finish - 0.53) / 0.05); Nudge(Attr.CloseShot, (finish - 0.53) / 0.05);
        Nudge(Attr.MidRange, (Shrink(R["FT%"], 0.77, 300) - 0.77) / 0.08, 1.5);

        // Back to his CA's attribute mean, spread over every attribute.
        for (int pass = 0; pass < 3; pass++)
        {
            double shift = before - AttrInfo.All.Average(x => (double)a[x]);
            if (Math.Abs(shift) < 0.15) break;
            foreach (var x in AttrInfo.All) a[x] = (int)Math.Round(Math.Clamp(a[x] + shift, 1, 20));
        }

        var t = p.Tendencies;
        double fga = Math.Max(0.5, R["FGA"]);
        t.ThreePointRate = Math.Clamp((int)Math.Round(R["3PA"] / fga * 100), 0, 80);
        t.PassFirst = Math.Clamp((int)Math.Round(50 + (R["AST"] - 0.25 * fga) * 6), 10, 90);
        t.CrashGlass = Math.Clamp((int)Math.Round(20 + R["ORB"] * 14), 5, 90);
        t.DriveRate = Math.Clamp((int)Math.Round(15 + R["FTA"] / fga * 60 + (1 - R["3PA"] / fga) * 15), 5, 70);
        t.IsolationRate = Math.Clamp((int)Math.Round((usage - 10) * 2.2), 0, 50);
        t.GambleSteals = Math.Clamp((int)Math.Round(15 + R["STL"] * 15), 5, 80);
        if (p.PrimaryPosition is Position.C or Position.PF)
            t.PostUpRate = Math.Clamp((int)Math.Round((1 - R["3PA"] / fga) * 30), 0, 60);
    }

    private static (string First, string Last) Split(string name)
    {
        int i = name.IndexOf(' ');
        return i < 0 ? (name, "") : (name[..i], name[(i + 1)..]);
    }

    /// <summary>His nationality (#218): his national team for well-known internationals; otherwise his country of birth
    /// from the roster page; USA when neither is known.</summary>
    public static string Nationality(string name, string? birthCountry = null) =>
        Intl.TryGetValue(name, out var n) ? n
        : birthCountry != null && Countries.TryGetValue(birthCountry, out var c) ? c
        : "USA";

    /// <summary>Basketball-Reference's birth-country codes, as the game names the countries.</summary>
    private static readonly Dictionary<string, string> Countries = new(StringComparer.OrdinalIgnoreCase)
    {
        ["US"] = "USA", ["FR"] = "France", ["CA"] = "Canada", ["AU"] = "Australia", ["ES"] = "Spain", ["NG"] = "Nigeria",
        ["DE"] = "Germany", ["GB"] = "Great Britain", ["RS"] = "Serbia", ["CH"] = "Switzerland", ["BA"] = "Bosnia and Herzegovina",
        ["BE"] = "Belgium", ["BS"] = "Bahamas", ["SN"] = "Senegal", ["DO"] = "Dominican Republic", ["IL"] = "Israel",
        ["SS"] = "South Sudan", ["RU"] = "Russia", ["CM"] = "Cameroon", ["CD"] = "Congo DR", ["NL"] = "Netherlands",
        ["IT"] = "Italy", ["TR"] = "Turkey", ["ZA"] = "South Africa", ["GR"] = "Greece", ["GE"] = "Georgia", ["BR"] = "Brazil",
        ["EE"] = "Estonia", ["AT"] = "Austria", ["MX"] = "Mexico", ["LT"] = "Lithuania", ["LV"] = "Latvia", ["FI"] = "Finland",
        ["SI"] = "Slovenia", ["HR"] = "Croatia", ["GN"] = "Guinea", ["PT"] = "Portugal", ["JM"] = "Jamaica", ["NI"] = "Nicaragua",
        ["SE"] = "Sweden", ["JP"] = "Japan", ["NZ"] = "New Zealand", ["UA"] = "Ukraine", ["CZ"] = "Czechia", ["CN"] = "China",
        ["PR"] = "Puerto Rico",
    };

    private static readonly Dictionary<string, string> Intl = new()
    {
        ["Luka Dončić"] = "Slovenia", ["Shai Gilgeous-Alexander"] = "Canada", ["Victor Wembanyama"] = "France",
        ["Giannis Antetokounmpo"] = "Greece", ["Nikola Jokić"] = "Serbia", ["Deni Avdija"] = "Israel",
        ["Pascal Siakam"] = "Cameroon", ["Franz Wagner"] = "Germany", ["Nickeil Alexander-Walker"] = "Canada",
        ["RJ Barrett"] = "Canada", ["Alperen Şengün"] = "Turkey", ["Jamal Murray"] = "Canada",
        ["Lauri Markkanen"] = "Finland", ["Andrew Nembhard"] = "Canada", ["Dyson Daniels"] = "Australia",
        ["Karlo Matković"] = "Croatia", ["Moussa Diabaté"] = "France", ["Domantas Sabonis"] = "Lithuania",
        ["Jakob Poeltl"] = "Austria", ["Rudy Gobert"] = "France", ["Egor Dëmin"] = "Russia",
        ["Mario Hezonja"] = "Croatia", ["Ivica Zubac"] = "Croatia", ["Josh Giddey"] = "Australia",
        ["Dennis Schröder"] = "Germany", ["Nikola Vučević"] = "Montenegro", ["Bogdan Bogdanović"] = "Serbia",
        ["Al Horford"] = "Dominican Republic", ["Moritz Wagner"] = "Germany", ["Sandro Mamukelashvili"] = "Georgia",
        ["Zaccharie Risacher"] = "France", ["Alex Sarr"] = "France", ["Bilal Coulibaly"] = "France",
        ["Tidjane Salaün"] = "France", ["Nolan Traoré"] = "France", ["Noa Essengue"] = "France",
        ["Joan Beringer"] = "France", ["Ousmane Dieng"] = "France", ["Pacôme Dadiet"] = "France",
        ["Mohamed Diawara"] = "France", ["Rayan Rupert"] = "France", ["Sidy Cissoko"] = "France",
        ["Yves Missi"] = "Cameroon", ["Khaman Maluach"] = "South Sudan", ["Kasparas Jakučionis"] = "Lithuania",
        ["Yang Hansen"] = "China", ["Hugo González"] = "Spain", ["Sergio de Larrea"] = "Spain",
        ["Simone Fontecchio"] = "Italy", ["Goga Bitadze"] = "Georgia", ["Vít Krejčí"] = "Czechia",
        ["Tristan Enaruna"] = "Netherlands", ["Svi Mykhailiuk"] = "Ukraine", ["Dillon Brooks"] = "Canada",
        ["Luguentz Dort"] = "Canada", ["Trey Lyles"] = "Canada", ["Bennedict Mathurin"] = "Canada",
        ["Andrew Wiggins"] = "Canada", ["Zach Edey"] = "Canada", ["Precious Achiuwa"] = "Nigeria",
        ["Jock Landale"] = "Australia", ["Josh Green"] = "Australia", ["Duop Reath"] = "Australia",
        ["Tyrese Proctor"] = "Australia", ["Rocco Zikarsky"] = "Australia", ["Neemias Queta"] = "Portugal",
        ["Gui Santos"] = "Brazil", ["Steven Adams"] = "New Zealand", ["Nikola Jović"] = "Serbia",
        ["Santi Aldama"] = "Spain", ["Bogoljub Markovic"] = "Serbia", ["Ben Saraf"] = "Israel",
        ["Karim López"] = "Mexico", ["Toumani Camara"] = "Belgium", ["Olivier-Maxence Prosper"] = "Canada",
        ["Leonard Miller"] = "Canada", ["Ryan Nembhard"] = "Canada", ["Pelle Larsson"] = "Sweden",
        ["Ajay Mitchell"] = "Belgium", ["Quinten Post"] = "Netherlands", ["Charles Bassey"] = "Nigeria",
        ["Malevy Leons"] = "Netherlands", ["Aday Mara"] = "Spain", ["Hannes Steinbach"] = "Germany",
        ["Kevin Durant"] = "USA",
        // Born abroad, but they play for the USA; born in the USA, but they play for another country.
        ["Kyrie Irving"] = "USA", ["Joel Embiid"] = "USA", ["Isaiah Hartenstein"] = "Germany", ["OG Anunoby"] = "Great Britain",
        ["Karl-Anthony Towns"] = "Dominican Republic",
    };

    /// <summary>One real player's roster-page and contract facts (Data/nba_rosters.json).</summary>
    public sealed record Bio(string Team, int? HeightCm, DateOnly? Born, string? Birth, long? Salary, int? Years);

    private static readonly Lazy<(Dictionary<string, Bio> Bios, Dictionary<string, string> Coaches, string Checked, string Source, long Cap)> _rosters = new(LoadRosters);
    /// <summary>Real birthdays, heights, birth countries and contracts, by player name (R2 part 2a).</summary>
    public static IReadOnlyDictionary<string, Bio> Bios => _rosters.Value.Bios;
    public static IReadOnlyDictionary<string, string> CoachesByTeam => _rosters.Value.Coaches;
    public static string RostersChecked => _rosters.Value.Checked;
    public static string RostersSource => _rosters.Value.Source;
    /// <summary>The real 2026-27 salary cap, from the source.</summary>
    public static long RealCap => _rosters.Value.Cap;

    /// <summary>"6-11" → 211 cm.</summary>
    public static int? HeightCm(string? ht)
    {
        if (string.IsNullOrEmpty(ht)) return null;
        var parts = ht.Split('-');
        return parts.Length == 2 && int.TryParse(parts[0], out int ft) && int.TryParse(parts[1], out int inch)
            ? (int)Math.Round((ft * 12 + inch) * 2.54) : null;
    }

    private static (Dictionary<string, Bio>, Dictionary<string, string>, string, string, long) LoadRosters()
    {
        var asm = typeof(RealPlayers).Assembly;
        string name = asm.GetManifestResourceNames().Single(n => n.EndsWith("nba_rosters.json", StringComparison.Ordinal));
        using var stream = asm.GetManifestResourceStream(name)!;
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;
        string? Str(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        long? Num(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : null;
        var bios = new Dictionary<string, Bio>();
        foreach (var p in root.GetProperty("players").EnumerateObject())
        {
            var e = p.Value;
            DateOnly? born = DateOnly.TryParseExact(Str(e, "born") ?? "", "yyyy-MM-dd", out var d) ? d : null;
            bios[p.Name] = new Bio(Str(e, "team") ?? "", HeightCm(Str(e, "ht")), born, Str(e, "birth"), Num(e, "salary"), (int?)Num(e, "years"));
        }
        var coaches = root.GetProperty("coaches").EnumerateObject().ToDictionary(c => c.Name, c => c.Value.GetString() ?? "");
        return (bios, coaches, Str(root, "checked") ?? "", Str(root, "source") ?? "", Num(root, "salaryCap") ?? 0);
    }

    private sealed record Pack(List<RealPlayerLine> Players, List<RealRecordLine> Last, List<RealRecordLine> This, string Checked, string Source);

    private static Pack Load()
    {
        var asm = typeof(RealPlayers).Assembly;
        string name = asm.GetManifestResourceNames().Single(n => n.EndsWith("nba_players.json", StringComparison.Ordinal));
        using var stream = asm.GetManifestResourceStream(name)!;
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;
        double?[] Row(JsonElement e) => e.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.Null ? (double?)null : v.GetDouble()).ToArray();
        var players = root.GetProperty("players").EnumerateArray().Select(e => new RealPlayerLine(
            e.GetProperty("name").GetString()!, e.GetProperty("age").GetInt32(), e.GetProperty("team").GetString()!, e.GetProperty("pos").GetString()!,
            e.GetProperty("cur").ValueKind == JsonValueKind.Null ? null : Row(e.GetProperty("cur")),
            e.GetProperty("prev").ValueKind == JsonValueKind.Null ? null : Row(e.GetProperty("prev")),
            e.TryGetProperty("prevTeam", out var pt) && pt.ValueKind == JsonValueKind.String ? pt.GetString() : null,
            e.TryGetProperty("notYet", out var ny) && ny.ValueKind == JsonValueKind.True)).ToList();
        List<RealRecordLine> Rec(string k) => root.GetProperty(k).GetProperty("records").EnumerateArray()
            .Select(e => new RealRecordLine(e.GetProperty("team").GetString()!, e.GetProperty("w").GetInt32(), e.GetProperty("l").GetInt32())).ToList();
        return new Pack(players, Rec("lastSeason"), Rec("thisSeason"), root.GetProperty("checked").GetString() ?? "", root.GetProperty("source").GetString() ?? "");
    }
}
