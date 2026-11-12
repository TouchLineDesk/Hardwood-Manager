using System.Text.Json;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;

namespace Hardwood.Core.RealWorld;

/// <summary>One real NBA player's line in the data pack (Data/nba_players.json): this season so far and last season (with last season's team; "2TM" when traded).</summary>
public sealed record RealPlayerLine(string Name, int Age, string Team, string Pos, double?[] Cur, double?[]? Prev, string? PrevTeam = null);

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
/// his percentages, playmaking from assists, rebounding, steals and blocks from his rates. Heights, birthdays and
/// contracts are generated; nationality is set for well-known internationals and is otherwise USA.
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
        var order = rated.OrderByDescending(r => r.Score).ToList();
        var caOf = new Dictionary<RealRating, int>();
        for (int i = 0; i < order.Count; i++)
            caOf[order[i]] = ladder[(int)Math.Round((double)i / Math.Max(1, order.Count - 1) * (ladder.Count - 1))];

        var today = new DateOnly(league.Season, 10, 1);
        var ageDay = new DateOnly(league.Season + 1, 2, 1); // Basketball-Reference ages are on February 1 of the season
        var rng = new Rng(seed ^ 0x5245414CUL);
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
                p.Contract = new Contract
                {
                    Years = Math.Clamp(1 + (int)Math.Round(rng.NextDouble() * 3), 1, 4),
                    Salary = Season.ContractMarket.MarketSalary(p.CurrentAbility, p.Age(today), league.Finance),
                    StartSeason = league.Season,
                };
                int at = team.Roster.IndexOf(old);
                team.Roster[at] = p;
                team.Tactic.Roles.Remove(old.Id);
            }
            team.DepthChart = null;
            // The same payroll band the generator opens with (82-98% of the cap).
            long cap = league.Finance.HardCap, payroll = team.Roster.Sum(x => x.Contract!.Salary);
            double factor = cap * (0.82 + rng.NextDouble() * 0.16) / Math.Max(1, payroll);
            foreach (var x in team.Roster)
                x.Contract!.Salary = Math.Max(league.Finance.MinSalary, (long)Math.Round(x.Contract.Salary * factor / 10_000) * 10_000);
        }
    }

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
        string nat = Nationality(r.Line.Name);
        var p = new Player
        {
            Id = id, FirstName = first, LastName = last,
            BirthDate = t.BirthDate, Nationality = nat, Ethnicity = Heritages.Backfill(id, nat),
            HeightCm = t.HeightCm, WingspanCm = t.WingspanCm, WeightKg = t.WeightKg,
            PrimaryPosition = pos, SecondaryPosition = t.SecondaryPosition,
            CurrentAbility = t.CurrentAbility, PotentialAbility = t.PotentialAbility,
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

    /// <summary>Well-known internationals (their national team); everyone else is listed as USA.</summary>
    public static string Nationality(string name) => Intl.TryGetValue(name, out var n) ? n : "USA";

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
    };

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
            Row(e.GetProperty("cur")), e.GetProperty("prev").ValueKind == JsonValueKind.Null ? null : Row(e.GetProperty("prev")),
            e.TryGetProperty("prevTeam", out var pt) && pt.ValueKind == JsonValueKind.String ? pt.GetString() : null)).ToList();
        List<RealRecordLine> Rec(string k) => root.GetProperty(k).GetProperty("records").EnumerateArray()
            .Select(e => new RealRecordLine(e.GetProperty("team").GetString()!, e.GetProperty("w").GetInt32(), e.GetProperty("l").GetInt32())).ToList();
        return new Pack(players, Rec("lastSeason"), Rec("thisSeason"), root.GetProperty("checked").GetString() ?? "", root.GetProperty("source").GetString() ?? "");
    }
}
