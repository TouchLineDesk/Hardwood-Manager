using Hardwood.Core.Engine;
using Hardwood.Core.Model;
using Hardwood.Core.Season;

namespace Hardwood.Core.International;

/// <summary>How a groups-then-knockouts tournament is shaped (international plan I4, run 211).</summary>
/// <param name="GroupSize">About this many teams a group (snaked by rank, best first).</param>
/// <param name="Knockout">How many go through to the knockouts: 16, 8 or 4 (group places first, then the best of the next place).</param>
/// <param name="Bronze">Whether the semi-final losers play for bronze.</param>
/// <param name="Quick">Quick results (#109: the Olympic qualifying tournaments) instead of the full engine.</param>
public sealed record TournamentFormat(int GroupSize, int Knockout, bool Bronze = true, bool Quick = false);

/// <summary>
/// International plan I4 (run 211): one engine for the Olympics, the Olympic qualifying tournaments and the continental
/// championships. Groups snaked by rank play a single round robin; the group places (winners first, then runners-up, …, each
/// place ordered by record, point difference and ranking points) fill a seeded bracket, in which two teams from one group don't
/// meet in the first knockout round when a swap can avoid it. Full-engine games use FIBA rules at a neutral site, as the World Cup
/// does (I2); quick games use the qualifiers' result model (I3). It reads players and never writes them: everything is kept in the
/// returned <see cref="WorldCupRecord"/> by code and name.
/// </summary>
public static class Tournament
{
    public const string RoundOf16 = "Round of 16";

    /// <summary>The knockout size for a field: 16 from 20 teams, 8 from 8, else 4 (a field under 4 isn't played).</summary>
    public static int KnockoutFor(int teams) => teams >= 20 ? 16 : teams >= 8 ? 8 : 4;

    /// <summary>Snake the field (best first) into groups of about <paramref name="size"/>: "Group A", "Group B", ….</summary>
    public static List<List<string>> Groups(IReadOnlyList<NationalTeam> field, int size)
    {
        int groups = Math.Max(1, (int)Math.Round(field.Count / (double)size, MidpointRounding.AwayFromZero));
        var lists = Enumerable.Range(0, groups).Select(_ => new List<string>()).ToList();
        for (int i = 0; i < field.Count; i++)
        {
            int row = i / groups, col = i % groups;
            lists[row % 2 == 0 ? col : groups - 1 - col].Add(field[i].Nation.Code);
        }
        return lists;
    }

    /// <summary>The standard seeded order of a bracket of <paramref name="size"/> (1-based seeds): 8 → 1 8 4 5 2 7 3 6.</summary>
    public static List<int> SeedOrder(int size)
    {
        var order = new List<int> { 1 };
        while (order.Count < size)
        {
            int n = order.Count * 2;
            order = order.SelectMany(s => new[] { s, n + 1 - s }).ToList();
        }
        return order;
    }

    /// <summary>Play a field (best first, ranked) as <paramref name="format"/> says, from <paramref name="ev"/>'s first day to its
    /// last. Null when the field is under four teams.</summary>
    public static WorldCupRecord? Play(string name, string kind, IReadOnlyList<NationalTeam> field, FibaEvent ev, TournamentFormat format,
        Rng rng, GameSimulator? sim)
    {
        if (field.Count < 4) return null;
        int ko = Math.Min(format.Knockout, field.Count);
        ko = ko >= 16 ? 16 : ko >= 8 ? 8 : 4;
        var rec = new WorldCupRecord { Year = ev.Start.Year, Host = ev.Where, Dates = ev.Dates, Name = name, Kind = kind };
        var byCode = field.ToDictionary(t => t.Nation.Code);
        var teams = new Dictionary<string, Team>();
        int id = -910_000;
        foreach (var n in field)
        {
            rec.Teams.Add(new CupTeam(n.Nation.Code, n.Name, NationalTeams.ZoneName(n.Nation.Zone), n.Strength, n.Rank, n.Squad.Select(x => x.Player.Id).ToList()));
            if (format.Quick) continue;
            var t = new Team { Id = id--, City = n.Name, Name = "", Abbreviation = n.Nation.Code, Division = "FIBA" };
            foreach (var x in n.Squad) t.Roster.Add(x.Player);
            teams[n.Nation.Code] = t;
        }
        var rules = Rules.Fiba with { NeutralSite = true };
        var engine = format.Quick ? null : sim ?? new GameSimulator();
        var lines = new Dictionary<int, (string Code, string Name, int G, int Pts, int Reb, int Ast)>();
        var inv = System.Globalization.CultureInfo.InvariantCulture;

        CupGame Game(string round, string home, string away, DateOnly day)
        {
            int hp, ap;
            if (engine == null)
                (hp, ap) = Qualifying.QuickResult(byCode[home].Strength, byCode[away].Strength, rng, homeEdge: 0);
            else
            {
                var r = engine.Simulate(teams[home], teams[away], rules, rng.NextULong());
                hp = r.Home.Points; ap = r.Away.Points;
                foreach (var (box, code) in new[] { (r.Home, home), (r.Away, away) })
                    foreach (var l in box.Lines.Where(l => l.Seconds > 0))
                    {
                        var c = lines.TryGetValue(l.Player.Id, out var was) ? was : (Code: code, Name: l.Player.Name, G: 0, Pts: 0, Reb: 0, Ast: 0);
                        lines[l.Player.Id] = (code, l.Player.Name, c.G + 1, c.Pts + l.Points, c.Reb + l.Rebounds, c.Ast + l.Assists);
                    }
            }
            var g = new CupGame(round, home, away, hp, ap, day.ToString("MMM d", inv));
            rec.Games.Add(g);
            return g;
        }

        // The knockout days, counted back from the last day: the final, bronze the day before, the semis two days before, …
        int rounds = ko == 16 ? 4 : ko == 8 ? 3 : 2;
        var koDays = new DateOnly[rounds];
        koDays[rounds - 1] = ev.End;
        for (int r = rounds - 2; r >= 0; r--) koDays[r] = koDays[r + 1].AddDays(-2);
        var groupEnd = koDays[0].AddDays(-2);

        // The groups: a single round robin, spread over the days before the knockouts.
        rec.Groups = Groups(field, format.GroupSize);
        var plans = rec.Groups.Select(g => Qualifying.Rounds(g).Take(g.Count % 2 == 0 ? g.Count - 1 : g.Count).ToList()).ToList();
        int groupRounds = plans.Max(p => p.Count);
        int span = Math.Max(0, groupEnd.DayNumber - ev.Start.DayNumber);
        for (int r = 0; r < groupRounds; r++)
        {
            var day = ev.Start.AddDays(groupRounds <= 1 ? 0 : r * span / (groupRounds - 1));
            for (int gi = 0; gi < rec.Groups.Count; gi++)
                if (r < plans[gi].Count)
                    foreach (var (home, away) in plans[gi][r]) Game(WorldCup.GroupName(gi), home, away, day);
        }

        // Through to the knockouts: every group's places in turn, each place ordered by record, point difference and ranking.
        var placed = new List<(string Code, int Group, int Place, double Pct, int Diff, double Pts)>();
        for (int gi = 0; gi < rec.Groups.Count; gi++)
        {
            string gname = WorldCup.GroupName(gi);
            var table = WorldCup.Table(rec, rec.Groups[gi], g => g.Round == gname);
            for (int i = 0; i < table.Count; i++)
            {
                var r = WorldCup.Record(rec, table[i], g => g.Round == gname);
                placed.Add((table[i], gi, i, r.W + r.L == 0 ? 0 : r.W / (double)(r.W + r.L), r.Diff, byCode[table[i]].Points));
            }
        }
        var through = placed.OrderBy(x => x.Place).ThenByDescending(x => x.Pct).ThenByDescending(x => x.Diff).ThenByDescending(x => x.Pts)
            .ThenBy(x => x.Code, StringComparer.Ordinal).Take(ko).ToList();
        var groupOf = through.ToDictionary(x => x.Code, x => x.Group);
        var slots = SeedOrder(ko).Select(seed => through[seed - 1].Code).ToList();
        AvoidRematches(slots, groupOf);

        // The knockouts.
        var alive = slots;
        for (int r = 0; r < rounds; r++)
        {
            string round = alive.Count switch { 16 => RoundOf16, 8 => WorldCup.QuarterRound, 4 => WorldCup.SemiRound, _ => WorldCup.FinalRound };
            if (alive.Count == 2)
            {
                var final = Game(WorldCup.FinalRound, alive[0], alive[1], ev.End);
                rec.Gold = final.Winner; rec.Silver = final.Loser;
                break;
            }
            var next = new List<string>();
            var losers = new List<string>();
            for (int i = 0; i + 1 < alive.Count; i += 2)
            {
                // A round of 16 or a quarter-final is played over two days, the top half first.
                var day = alive.Count >= 8 && i >= alive.Count / 2 ? koDays[r].AddDays(1) : koDays[r];
                if (alive.Count == 4) day = koDays[r];
                var g = Game(round, alive[i], alive[i + 1], day);
                next.Add(g.Winner); losers.Add(g.Loser);
            }
            if (alive.Count == 4 && format.Bronze)
            {
                var bronze = Game(WorldCup.BronzeRound, losers[0], losers[1], ev.End.AddDays(-1));
                rec.Bronze = bronze.Winner; rec.Fourth = bronze.Loser;
            }
            alive = next;
        }

        if (!format.Quick)
        {
            var all = lines.Select(kv => new CupPlayer(kv.Key, kv.Value.Name, kv.Value.Code, kv.Value.G, kv.Value.Pts, kv.Value.Reb, kv.Value.Ast)).ToList();
            rec.TopScorers = all.Where(p => p.Games >= 3).OrderByDescending(p => p.Ppg).ThenByDescending(p => p.Pts).ThenBy(p => p.PlayerId).Take(10).ToList();
            var lastFour = rec.Games.Where(g => g.Round == WorldCup.SemiRound).SelectMany(g => new[] { g.Home, g.Away }).ToHashSet();
            var lastEight = rec.Games.Where(g => g.Round == WorldCup.QuarterRound).SelectMany(g => new[] { g.Home, g.Away }).ToHashSet();
            var pool = lastEight.Count > 0 ? lastEight : lastFour;
            rec.AllStarFive = all.Where(p => p.Games >= 4 && pool.Contains(p.Code)).OrderByDescending(p => p.Impact).ThenBy(p => p.PlayerId).Take(5).ToList();
            rec.Mvp = rec.AllStarFive.FirstOrDefault(p => p.Code == rec.Gold) ?? all.Where(p => p.Code == rec.Gold).OrderByDescending(p => p.Impact).ThenBy(p => p.PlayerId).FirstOrDefault();
        }
        return rec;
    }

    /// <summary>Swap the lower seeds of two first-round pairs when both pairs would be a group rematch and the swap fixes one.</summary>
    public static void AvoidRematches(List<string> slots, IReadOnlyDictionary<string, int> groupOf)
    {
        bool Clash(int pair) => groupOf[slots[pair * 2]] == groupOf[slots[pair * 2 + 1]];
        int pairs = slots.Count / 2;
        for (int p = 0; p < pairs; p++)
        {
            if (!Clash(p)) continue;
            for (int q = 0; q < pairs; q++)
            {
                if (q == p) continue;
                (slots[p * 2 + 1], slots[q * 2 + 1]) = (slots[q * 2 + 1], slots[p * 2 + 1]);
                if (!Clash(p) && !Clash(q)) break;
                (slots[p * 2 + 1], slots[q * 2 + 1]) = (slots[q * 2 + 1], slots[p * 2 + 1]);
            }
        }
    }

    /// <summary>The order teams finished in a tournament: gold, silver, bronze, fourth, then by the round they went out in, then by
    /// ranking points.</summary>
    public static List<string> FinishOrder(WorldCupRecord rec, IReadOnlyDictionary<string, double> points)
    {
        int Tier(string c)
        {
            if (c == rec.Gold) return 0;
            if (c == rec.Silver) return 1;
            if (c == rec.Bronze) return 2;
            if (c == rec.Fourth) return 3;
            bool In(string round) => rec.Games.Any(g => g.Round == round && (g.Home == c || g.Away == c));
            if (In(WorldCup.SemiRound)) return 3;
            if (In(WorldCup.QuarterRound)) return 4;
            if (In(RoundOf16) || In(WorldCup.SecondRound)) return 5;
            return 6;
        }
        return rec.Teams.Select(t => t.Code).OrderBy(Tier).ThenByDescending(c => points.GetValueOrDefault(c)).ThenBy(c => c, StringComparer.Ordinal).ToList();
    }
}

/// <summary>
/// International plan I4 (run 211): the Olympic basketball tournament (#106; 2028 Los Angeles, 2032 Brisbane). Twelve teams, as
/// FIBA picks them: the host; the previous summer's World Cup's best per zone (Europe 2, the Americas 2, Africa 1, Asia-Pacific 2,
/// FIBA's Asia and Oceania places); and the winners of four Olympic qualifying tournaments of six, played early in July by quick
/// result (#109) among the best-ranked teams left. The Olympics themselves are three groups of four, eight through to the
/// quarter-finals, every game in the full engine (#109). Its own streams; pure towards the session.
/// </summary>
public static class Olympics
{
    public const int Teams = 12, QualifyingTournaments = 4, QualifyingSize = 6;
    public const double OlympicK = 36, QualifierK = 24;

    /// <summary>The World Cup places by zone (7 in all).</summary>
    public static readonly IReadOnlyDictionary<FibaZone, int> WorldCupPlaces = new Dictionary<FibaZone, int>
    {
        [FibaZone.Europe] = 2, [FibaZone.Americas] = 2, [FibaZone.Africa] = 1, [FibaZone.AsiaPacific] = 2,
    };

    /// <summary>The host nation of an Olympic city, when the game knows it.</summary>
    public static string HostNation(string city) => city switch { "Los Angeles" => "USA", "Brisbane" => "Australia", _ => "" };

    public static FibaEvent? EventIn(int year) =>
        FibaCalendar.IsOlympicYear(year) ? FibaCalendar.InYear(year).First(e => e.Kind == FibaEventKind.Olympics) : null;

    public static FibaEvent? QualifyingIn(int year) =>
        FibaCalendar.IsOlympicYear(year) ? FibaCalendar.InYear(year).First(e => e.Kind == FibaEventKind.OlympicQualifying) : null;

    /// <summary>The Olympic year's result: the qualifying tournaments and the Olympics.</summary>
    public sealed record Result(List<WorldCupRecord> Qualifiers, WorldCupRecord Olympics);

    /// <summary>Play the Olympic year with the session's world as it stands (players fit on the qualifiers' first day). Null when
    /// there's no Olympics that year or the world has under twelve ranked teams.</summary>
    public static Result? Play(GameSession s, int year, GameSimulator? sim = null)
    {
        if (EventIn(year) is not { } ev || QualifyingIn(year) is not { } oqt) return null;
        var h = s.World.International;
        var built = NationalTeams.Build(WorldCup.Entries(s, oqt.Start), oqt.Start);
        var points = FibaRanking.Current(h, built);
        var ranked = FibaRanking.Apply(built, points).Where(t => t.Rank > 0).ToList();
        if (ranked.Count < Teams) return null;
        var cup = h.WorldCups.Where(w => w.Year == year - 1).LastOrDefault();
        var direct = Direct(ranked, HostNation(ev.Where), cup, points);
        // Review (run 211): with no known host, or a zone short of World Cup teams, the open direct places go by ranking before
        // the qualifying pools are drawn, so a qualifying tournament's loser never takes one.
        foreach (var t in ranked)
        {
            if (direct.Count >= Teams - QualifyingTournaments) break;
            if (!direct.Contains(t)) direct.Add(t);
        }
        var qrng = new Rng(s.Seed ^ 0xF1BA_0A7C_0000_0011UL ^ ((ulong)(uint)year * 0x9E3779B97F4A7C15UL));
        var qualifiers = PlayQualifiers(ranked, direct, oqt, qrng);
        var field = direct.Concat(qualifiers.Select(q => ranked.First(t => t.Nation.Code == q.Gold))).ToList();
        foreach (var t in ranked) // a world too small for four qualifying tournaments: the best of the rest
        {
            if (field.Count >= Teams) break;
            if (!field.Contains(t)) field.Add(t);
        }
        field = field.OrderBy(t => t.Rank).ToList();
        // The Olympic twelves are picked on the Olympics' first day, from players fit then.
        var onDay = FibaRanking.Apply(NationalTeams.Build(WorldCup.Entries(s, ev.Start), ev.Start), points)
            .Where(t => t.Rank > 0).ToDictionary(t => t.Nation.Code);
        field = field.Select(t => onDay.GetValueOrDefault(t.Nation.Code) ?? t).OrderBy(t => t.Rank).ToList();
        var rng = new Rng(s.Seed ^ 0xF1BA_0A7C_0000_0012UL ^ ((ulong)(uint)year * 0x9E3779B97F4A7C15UL));
        var rec = Tournament.Play($"Olympic basketball tournament {year}", "Olympics", field, ev, new TournamentFormat(4, 8), rng, sim)!;
        return new Result(qualifiers, rec);
    }

    /// <summary>The host and the World Cup's best per zone (by finishing order; by ranking when no World Cup was played).</summary>
    public static List<NationalTeam> Direct(IReadOnlyList<NationalTeam> ranked, string host, WorldCupRecord? cup, IReadOnlyDictionary<string, double> points)
    {
        var direct = new List<NationalTeam>();
        if (ranked.FirstOrDefault(t => t.Name == host) is { } h) direct.Add(h);
        var order = cup == null ? ranked.Select(t => t.Nation.Code).ToList() : Tournament.FinishOrder(cup, points);
        foreach (var (zone, places) in WorldCupPlaces)
        {
            var picks = order.Select(c => ranked.FirstOrDefault(t => t.Nation.Code == c)).OfType<NationalTeam>()
                .Where(t => t.Nation.Zone == zone && !direct.Contains(t)).Take(places).ToList();
            direct.AddRange(picks);
        }
        return direct;
    }

    /// <summary>Four tournaments of six (snaked by rank from the best teams not yet in), quick results; each winner goes through.</summary>
    public static List<WorldCupRecord> PlayQualifiers(IReadOnlyList<NationalTeam> ranked, IReadOnlyList<NationalTeam> direct, FibaEvent oqt, Rng rng)
    {
        var rest = ranked.Where(t => !direct.Contains(t)).Take(QualifyingTournaments * QualifyingSize).ToList();
        int count = Math.Min(QualifyingTournaments, Math.Max(0, Teams - direct.Count));
        if (count == 0 || rest.Count < 4 * count) count = Math.Min(count, rest.Count / 4);
        var pools = Enumerable.Range(0, count).Select(_ => new List<NationalTeam>()).ToList();
        for (int i = 0; i < rest.Count && count > 0; i++)
        {
            int row = i / count, col = i % count;
            pools[row % 2 == 0 ? col : count - 1 - col].Add(rest[i]);
        }
        var list = new List<WorldCupRecord>();
        for (int i = 0; i < count; i++)
        {
            // Each tournament is hosted by its top seed (its name is on the record; the games are at a neutral rating).
            var pool = pools[i].OrderBy(t => t.Rank).ToList();
            var ev = oqt with { Where = pool[0].Name };
            var rec = Tournament.Play($"Olympic qualifying tournament {oqt.Start.Year}, {pool[0].Name}", "Olympic qualifying", pool, ev,
                new TournamentFormat(3, 4, Bronze: false, Quick: true), rng, null);
            if (rec != null) list.Add(rec);
        }
        return list;
    }
}

/// <summary>
/// International plan I4 (run 211): the continental championships in the odd years between World Cups (2029, 2033): EuroBasket (24
/// teams), the AmeriCup (12), AfroBasket (16) and the Asia Cup (16), as many as each zone has teams (#105 counts 40–60 nations in
/// all, so the Americas, Africa and Asia-Pacific often field fewer). The field comes from the continental qualifying cycle's tables
/// (I3: group winners first), then the ranking. Every game is the full engine (#109 keeps quick results for qualifiers only). Each
/// event draws from its own stream; pure towards the session.
/// </summary>
public static class Continentals
{
    public const double ContinentalK = 30;

    /// <summary>The championships by zone: name prefix, field size and group size.</summary>
    public static readonly IReadOnlyList<(FibaZone Zone, string Prefix, int Size, int GroupSize)> Events = new[]
    {
        (FibaZone.AsiaPacific, "FIBA Asia Cup", 16, 4),
        (FibaZone.Africa, "FIBA AfroBasket", 16, 4),
        (FibaZone.Americas, "FIBA AmeriCup", 12, 4),
        (FibaZone.Europe, "EuroBasket", 24, 6),
    };

    public static string Target(int year) => $"{year} continental championships";

    /// <summary>Play the four championships of <paramref name="year"/> in calendar order (each moves the ranking the next one seeds
    /// from). Returns them, or an empty list in a year without them.</summary>
    public static List<WorldCupRecord> Play(GameSession s, int year, GameSimulator? sim = null, Action<WorldCupRecord>? played = null)
    {
        var list = new List<WorldCupRecord>();
        if (!FibaCalendar.IsContinentalYear(year)) return list;
        var h = s.World.International;
        var events = FibaCalendar.InYear(year).Where(e => e.Kind == FibaEventKind.Continental).OrderBy(e => e.Start).ToList();
        foreach (var ev in events)
        {
            var spec = Events.First(x => ev.Name.StartsWith(x.Prefix, StringComparison.Ordinal));
            var built = NationalTeams.Build(WorldCup.Entries(s, ev.Start), ev.Start);
            var points = FibaRanking.Current(h, built);
            var ranked = FibaRanking.Apply(built, points).Where(t => t.Rank > 0 && t.Nation.Zone == spec.Zone).ToList();
            var order = Qualifying.ZoneOrder(h, Target(year), points).GetValueOrDefault(spec.Zone) ?? new List<string>();
            var field = ranked.OrderBy(t => order.IndexOf(t.Nation.Code) is var i && i >= 0 ? i : int.MaxValue).ThenBy(t => t.Rank)
                .Take(spec.Size).OrderBy(t => t.Rank).ToList();
            var rng = new Rng(s.Seed ^ 0xF1BA_C0A7_0000_0021UL ^ ((ulong)(uint)(year * 10 + (int)spec.Zone) * 0x9E3779B97F4A7C15UL));
            var rec = Tournament.Play(ev.Name, "Continental", field, ev, new TournamentFormat(spec.GroupSize, Tournament.KnockoutFor(field.Count)), rng, sim);
            if (rec == null) continue;
            rec.Zone = NationalTeams.ZoneName(spec.Zone);
            list.Add(rec);
            played?.Invoke(rec);
        }
        return list;
    }
}
