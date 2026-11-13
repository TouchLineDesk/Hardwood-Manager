using Hardwood.Core.Model;

namespace Hardwood.Core.Season;

/// <summary>What a facility is, what it does, what it costs and how long it lasts.</summary>
public sealed record FacilityInfo(FacilityKind Kind, string Name, string Short, string Pitch, string Effect,
    double CostPerLevel, int Lifespan, int BuildSeasons);

/// <summary>
/// Money buys things, part 1 (item M; Danny's #237, D10 #249, D17 A #273). Every pro club owns four facilities rated
/// 1-5. Each summer the books close, the club pays its facilities' running costs, projects move on, old facilities date,
/// and the owner puts surplus cash into upgrades, renewals or a new arena, by his appetite. Your club gets the front
/// office's proposals instead, to approve or decline (or you let the GM decide).
/// <para>Effects use each facility's edge on its league's average level (<see cref="Facility.Edge"/>): spending keeps
/// a club level with its rivals or puts it ahead, but a league where everyone builds doesn't inflate.</para>
/// <para>Prices are in league money (an NBA club's revenue is about $245M): a practice facility upgrade is $59-98M, an
/// arena renovation $118-196M, a new arena about $640M over three summers (the owner's share; NBA arenas cost $1-2B with
/// public money and naming rights).</para>
/// </summary>
public static class Franchise
{
    public static readonly FacilityInfo[] All =
    {
        new(FacilityKind.Practice, "Practice facility", "Practice",
            "Courts, a weight room, film rooms and a players' lounge. Free agents tour it; your players live in it.",
            "Training quality: +0.4 a level above the league's average", 0.08, 12, 1),
        new(FacilityKind.Medical, "Sports science & medical", "Medical",
            "Load management, a recovery centre, imaging on site and a full-time sports science staff.",
            "Injuries: about 5% fewer and 6% shorter a level above average", 0.045, 9, 1),
        new(FacilityKind.Analytics, "Analytics & scouting", "Analytics",
            "Tracking data, a video team and scouts on every continent.",
            "Scouting knowledge: +3 points a level above average", 0.03, 7, 1),
        new(FacilityKind.Arena, "Arena & premium seating", "Arena",
            "Suites, courtside clubs, a new scoreboard and concourses: the fan experience, and the money it brings.",
            "Revenue +2% and the fan base +0.6 a season, a level above average", 0.16, 15, 1),
        new(FacilityKind.NewArena, "A new arena", "New arena",
            "A new building: more seats, every premium option, and an arena that won't date for decades. Three summers to build.",
            "More seats, and the arena at level 5 for 15 seasons", 2.6, 15, 3),
    };

    public static FacilityInfo Info(FacilityKind k) => Array.Find(All, i => i.Kind == k) ?? All[0];

    /// <summary>Running costs a season, as a share of the league's revenue, times the level squared: the best costs most to run
    /// (all four at 3: about 5%, $11M in the NBA; all at 5: about 13%, $32M).</summary>
    public const double UpkeepPerLevel = 0.0013;
    /// <summary>An arena is replaced after about this many seasons (NBA arenas last 25-35 years).</summary>
    public const int ArenaLife = 30;
    /// <summary>Renewing a dated facility (keeping its level) costs this share of the price of building that level.</summary>
    public const double RenewShare = 0.6;

    /// <summary>The price of building a facility up to a level (or a new arena, whatever the level).</summary>
    public static long CostTo(FacilityKind k, int level, long leagueRevenue) =>
        Round(Info(k).CostPerLevel * (k == FacilityKind.NewArena ? 1 : level) * leagueRevenue);

    /// <summary>Running costs a season.</summary>
    public static long Upkeep(ClubFacilities f, long leagueRevenue) =>
        Round(ClubFacilities.Kinds.Sum(k => f.Get(k).Level * f.Get(k).Level) * UpkeepPerLevel * leagueRevenue);

    /// <summary>The cash the owner keeps in the bank, as a share of revenue: ambitious owners keep less.</summary>
    public static double ReserveShare(Jobs.Owner o) => Math.Clamp(0.55 - (o.Ambition - 10) * 0.025, 0.3, 0.8);
    public static long Reserve(Team t) => Round(t.Money.Revenue * ReserveShare(t.Owner));
    /// <summary>What the owner will put into facilities right now.</summary>
    public static long Spendable(Team t) => Math.Max(0, t.Money.Balance - Reserve(t));

    /// <summary>How keen this owner is on each facility: win-now owners want the practice facility and analytics, careful ones the arena (it pays back).</summary>
    public static double Appetite(Jobs.Owner o, FacilityKind k)
    {
        bool winNow = o.Ambition >= 14, careful = o.Ambition <= 7;
        return k switch
        {
            FacilityKind.Practice => winNow ? 1.3 : 1.0,
            FacilityKind.Analytics => winNow ? 1.2 : careful ? 0.85 : 1.0,
            FacilityKind.Medical => winNow ? 1.15 : 1.0,
            FacilityKind.Arena => careful ? 1.4 : 1.05,
            _ => 1.0,
        };
    }

    /// <summary>First sight of a club: levels from its fan base and arena (deterministic per club, so the career's random stream doesn't move).</summary>
    public static void Seed(Team t, double typicalArena)
    {
        var f = t.Money.Facilities;
        f.DropRetired();
        if (f.Ready) return;
        var rng = new Rng((ulong)t.Id * 0x9E3779B97F4A7C15UL + 0x51A7);
        double baseLevel = 2.2 + Math.Clamp(t.Money.FanBase, 0, 100) / 50.0 + (t.ArenaCapacity / Math.Max(1, typicalArena) - 1) * 2;
        foreach (var k in ClubFacilities.Kinds)
        {
            var fac = f.Get(k);
            fac.Level = Math.Clamp((int)Math.Round(baseLevel + rng.NextGaussian(0, 0.7)), 1, 5);
            fac.Age = rng.Next(Info(k).Lifespan);
        }
        f.ArenaYears = rng.Next(ArenaLife);
        f.Ready = true;
    }

    /// <summary>Each facility's edge on the average of the clubs it competes with.</summary>
    public static void SetEdges(IReadOnlyList<Team> clubs)
    {
        if (clubs.Count == 0) return;
        foreach (var k in ClubFacilities.Kinds)
        {
            double avg = clubs.Average(t => (double)t.Money.Facilities.Get(k).Level);
            foreach (var t in clubs) t.Money.Facilities.Get(k).Edge = t.Money.Facilities.Get(k).Level - avg;
        }
    }

    /// <summary>
    /// A club's summer, after the books close: running costs, projects move on (and open), facilities date, then the owner
    /// invests (or, for your club, the front office proposes).
    /// </summary>
    public static void Summer(Team t, long leagueRevenue, double typicalArena, int season)
    {
        var m = t.Money; var f = m.Facilities;
        Seed(t, typicalArena);
        f.LastNews.Clear(); f.LastInvested = 0; f.LastDistribution = 0;
        f.LastUpkeep = Upkeep(f, leagueRevenue);
        m.Balance -= f.LastUpkeep;

        // Projects: one season nearer; finished ones open, the rest take their next instalment.
        foreach (var p in f.Building.ToList())
        {
            p.SeasonsLeft--;
            if (p.SeasonsLeft <= 0)
            {
                f.Building.Remove(p);
                if (p.Kind == FacilityKind.NewArena)
                {
                    t.ArenaCapacity = Math.Max(t.ArenaCapacity, p.Capacity);
                    f.Arena.Level = 5; f.Arena.Age = 0; f.ArenaYears = 0; f.ArenasBuilt++;
                    f.LastNews.Add($"The new arena opened: {t.ArenaCapacity:N0} seats.");
                }
                else
                {
                    var fac = f.Get(p.Kind);
                    bool renewal = p.Renewal;
                    fac.Level = Math.Max(fac.Level, p.ToLevel); fac.Age = 0;
                    f.LastNews.Add(renewal ? $"{Info(p.Kind).Name}: the renovation is finished (level {fac.Level}, good for {Info(p.Kind).Lifespan} seasons)."
                                           : $"{Info(p.Kind).Name}: the upgrade opened (level {fac.Level}).");
                }
            }
            else Pay(t, p.Instalment);
        }

        // Ageing: a facility past its lifespan dates and loses a level (unless it's being renewed).
        foreach (var k in ClubFacilities.Kinds)
        {
            var fac = f.Get(k);
            fac.Age++;
            if (fac.Age > Info(k).Lifespan && !f.IsBuilding(k))
            {
                fac.Age = 0;
                if (fac.Level > 1) { fac.Level--; f.LastNews.Add($"{Info(k).Name} has dated: down to level {fac.Level}."); }
            }
        }
        f.ArenaYears++;

        // Deep in debt: the owner cuts running costs, and the dearest facility to run slips a level.
        if (m.Balance < -m.Revenue * 0.25)
        {
            var dear = ClubFacilities.Kinds.Select(f.Get).Where(x => x.Level > 1).OrderByDescending(x => x.Level).FirstOrDefault();
            if (dear != null)
            {
                var k = ClubFacilities.Kinds.First(x => f.Get(x) == dear);
                dear.Level--;
                f.LastNews.Add($"In debt, the owner cut running costs: {Info(k).Name.ToLower()} down to level {dear.Level}.");
            }
        }

        f.Proposals = Candidates(t, leagueRevenue, typicalArena, season, Spendable(t)).Take(3).ToList();
        if (f.YouDecide)
        {
            // Your club: the front office has proposals for you on the Board page. The owner keeps enough for all of them on top
            // of the usual reserve and takes the rest as a distribution, as every other owner does (D24 A; review #11 found a
            // managed club holding $606M in tier 2, 15 seasons of its revenue, because only computer clubs paid out).
            long paid = Distribute(t, Reserve(t) + f.Proposals.Sum(p => p.Instalment));
            if (paid > 0) f.LastNews.Add($"The owner took {Money(paid)} out of the club as a distribution: the club keeps about a season's revenue, his reserve and the first payment on each project on the table.");
            return;
        }
        // The owner invests: the best project he'll fund, then the next while money lasts (at most three a summer).
        f.Proposals.Clear();
        for (int i = 0; i < 3; i++)
        {
            var best = Candidates(t, leagueRevenue, typicalArena, season, Spendable(t)).FirstOrDefault();
            if (best == null) break;
            Start(t, best);
        }
        Distribute(t);
    }

    /// <summary>
    /// The last resort: once the summer's projects are funded, a club holding more than 90% of a season's revenue pays the
    /// owner the excess as a distribution (NBA owners take profits too). Facilities come first; this keeps balances realistic.
    /// </summary>
    public const double KeepShare = 0.9;
    public static long Distribute(Team t, long alsoKeep = 0)
    {
        var m = t.Money;
        long excess = m.Balance - (long)(m.Revenue * KeepShare) - Math.Max(0, alsoKeep);
        if (excess <= 0) return 0;
        long paid = Round(excess);
        m.Balance -= paid;
        m.Facilities.LastDistribution = paid;
        m.Facilities.TotalDistributed += paid;
        return paid;
    }

    /// <summary>The projects this owner would fund now, best first: upgrades, renewals of dated facilities, a new arena.</summary>
    public static List<FacilityProject> Candidates(Team t, long leagueRevenue, double typicalArena, int season, long spendable)
    {
        var f = t.Money.Facilities; var o = t.Owner;
        var list = new List<(FacilityProject p, double score)>();
        if (spendable <= 0 || t.Money.Embargo) return new();
        foreach (var k in ClubFacilities.Kinds)
        {
            if (f.IsBuilding(k)) continue;
            var fac = f.Get(k); var info = Info(k);
            if (fac.Level < 5)
            {
                long cost = CostTo(k, fac.Level + 1, leagueRevenue);
                // Behind the league hurts most: catching up comes first.
                double score = Appetite(o, k) * (1.2 - 0.25 * fac.Edge) * (6 - fac.Level) / 3.0;
                list.Add((new FacilityProject { Kind = k, ToLevel = fac.Level + 1, Cost = cost, SeasonsLeft = info.BuildSeasons, Instalments = 1, Season = season }, score));
            }
            if (fac.Age >= info.Lifespan * 3 / 4)
            {
                long cost = Round(CostTo(k, fac.Level, leagueRevenue) * RenewShare);
                double score = Appetite(o, k) * 0.9 * fac.Level / 3.0;
                list.Add((new FacilityProject { Kind = k, ToLevel = fac.Level, Cost = cost, SeasonsLeft = info.BuildSeasons, Instalments = 1, Season = season, Renewal = true }, score));
            }
        }
        if (!f.IsBuilding(FacilityKind.NewArena))
        {
            long cost = CostTo(FacilityKind.NewArena, 5, leagueRevenue);
            bool old = f.ArenaYears >= ArenaLife, small = t.ArenaCapacity < typicalArena * 0.93 && f.ArenaYears >= 10,
                 flush = spendable >= cost * 0.7 && f.ArenaYears >= 20;
            if (old || small || flush)
            {
                int cap = Math.Max(t.ArenaCapacity, (int)Math.Round(typicalArena * 1.06 / 100) * 100);
                var p = new FacilityProject { Kind = FacilityKind.NewArena, ToLevel = 5, Cost = cost, SeasonsLeft = 3, Instalments = 3, Capacity = cap, Season = season };
                list.Add((p, (old ? 2.5 : small ? 1.8 : 1.2) * Appetite(o, FacilityKind.Arena)));
            }
        }
        return list.Where(x => x.p.Instalment <= spendable).OrderByDescending(x => x.score).Select(x => x.p).ToList();
    }

    /// <summary>Approve a project: pay its first instalment and start building.</summary>
    public static void Start(Team t, FacilityProject p)
    {
        var f = t.Money.Facilities;
        Pay(t, p.Instalment);
        f.Building.Add(p);
        f.Proposals.RemoveAll(x => x.Kind == p.Kind || (x.Kind is FacilityKind.Arena or FacilityKind.NewArena && p.Kind is FacilityKind.Arena or FacilityKind.NewArena));
        f.LastNews.Add($"Approved: {Describe(p)}.");
    }

    private static void Pay(Team t, long amount)
    {
        t.Money.Balance -= amount;
        t.Money.Facilities.LastInvested += amount;
        t.Money.Facilities.TotalInvested += amount;
    }

    /// <summary>"Practice facility to level 4 ($78M, opens next summer)".</summary>
    public static string Describe(FacilityProject p)
    {
        var info = Info(p.Kind);
        string what = p.Kind == FacilityKind.NewArena ? $"a new arena ({p.Capacity:N0} seats)" : $"{info.Name.ToLower()} {(p.Renewal ? $"renewed at level {p.ToLevel}" : $"to level {p.ToLevel}")}";
        string when = p.SeasonsLeft <= 1 ? "opens next summer" : $"opens in {p.SeasonsLeft} summers";
        string pay = p.Instalments > 1 ? $"{Money(p.Cost)}, {p.Instalments} payments of {Money(p.Instalment)}" : Money(p.Cost);
        return $"{what} ({pay}, {when})";
    }

    public static string Money(long v) => Math.Abs(v) >= 10_000_000 ? $"${v / 1e6:F0}M" : $"${v / 1e6:F1}M";
    private static long Round(double v) => (long)Math.Round(v / 100_000) * 100_000;
}
