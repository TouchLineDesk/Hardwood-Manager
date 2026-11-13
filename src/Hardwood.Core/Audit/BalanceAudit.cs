using System.Text;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Core.World;

namespace Hardwood.Core.Audit;

/// <summary>One measured quantity, with the range real basketball lands in (null = for information only).</summary>
public sealed record AuditMetric(string Group, string Name, string Format, List<double> PerSeason, double? Low, double? High, string Source)
{
    public double Mean => PerSeason.Count == 0 ? double.NaN : PerSeason.Average();
    public string Status => Low is null || High is null || double.IsNaN(Mean) ? "info" : Mean < Low ? "LOW" : Mean > High ? "HIGH" : "ok";
    public bool OutOfRange => Status is "LOW" or "HIGH";
}

public sealed class AuditReport
{
    public ulong Seed { get; init; }
    public int Seasons { get; init; }
    public TimeSpan Elapsed { get; set; }
    public List<AuditMetric> Metrics { get; } = new();
    public List<string> Champions { get; } = new();
    public Dictionary<string, List<string>> WorldChampions { get; } = new();
    public List<string> Notes { get; } = new();

    public AuditMetric this[string name] => Metrics.First(m => m.Name == name);
    public IEnumerable<AuditMetric> Flags => Metrics.Where(m => m.OutOfRange);

    public string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Balance audit");
        sb.AppendLine();
        sb.AppendLine($"Seed {Seed} · {Seasons} seasons · {Elapsed.TotalSeconds:F0}s · {Flags.Count()} of {Metrics.Count(m => m.Low != null)} checked metrics out of range");
        sb.AppendLine();
        if (Flags.Any())
        {
            sb.AppendLine("**Out of range:** " + string.Join(", ", Flags.Select(f => $"{f.Name} ({f.Status.ToLower()})")));
            sb.AppendLine();
        }
        foreach (var g in Metrics.GroupBy(m => m.Group))
        {
            sb.AppendLine($"## {g.Key}");
            sb.AppendLine();
            sb.AppendLine("| Metric | Average | Seasons (min–max) | Real range | Status |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var m in g)
            {
                string f(double v) => v.ToString(m.Format);
                var range = m.Low is { } lo && m.High is { } hi ? $"{f(lo)}–{f(hi)}" : "";
                var spread = m.PerSeason.Count > 1 ? $"{f(m.PerSeason.Min())}–{f(m.PerSeason.Max())}" : "";
                var status = m.Status switch { "ok" => "ok", "info" => "", _ => $"**{m.Status}**" };
                sb.AppendLine($"| {m.Name} | {f(m.Mean)} | {spread} | {range} | {status} |");
            }
            sb.AppendLine();
        }
        sb.AppendLine("## Champions");
        sb.AppendLine();
        sb.AppendLine("- NABL: " + string.Join(" · ", Champions));
        foreach (var (k, v) in WorldChampions) sb.AppendLine($"- {k}: " + string.Join(" · ", v));
        sb.AppendLine();
        if (Notes.Count > 0)
        {
            sb.AppendLine("## Notes");
            sb.AppendLine();
            foreach (var n in Notes) sb.AppendLine("- " + n);
            sb.AppendLine();
        }
        sb.AppendLine("Real ranges: NBA 2018–2025 league averages (NABL); EuroLeague, ACB, BSL, NBL and CBA recent seasons (overseas). Sources are noted per metric in `BalanceAudit.cs`.");
        return sb.ToString();
    }
}

/// <summary>
/// Plays a whole career headlessly (the NABL season by season with offseasons, and the world around it) and measures
/// what a basketball fan would notice: scoring and shooting, records, dynasties, star counts, how players age and
/// develop, retirements, the draft, cap health and overseas scoring. Each metric carries the range real basketball
/// lands in, so drift after a change shows up as a flag. Run it with `hardwood audit`.
/// </summary>
public static class BalanceAudit
{
    public static AuditReport Run(ulong seed, int seasons, IEnumerable<string>? fullSimLeagues = null, Action<string>? progress = null, bool realWorld = false)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var report = new AuditReport { Seed = seed, Seasons = seasons };
        var league = new WorldGenerator(new Rng(seed)).Generate(realWorld ? Hardwood.Core.RealWorld.RealWorldData.LeagueName : "NABL", 2026, new DateOnly(2026, 10, 1));
        var world = GameWorld.Generate(league, seed, fullSimLeagues ?? Array.Empty<string>(), new DateOnly(2026, 10, 1));
        // The real world (R2): the NBA's real teams and players on the same generated world (`hardwood audit --real`).
        if (realWorld) { Hardwood.Core.RealWorld.RealWorldData.Apply(league, world, seed); report.Notes.Add("Real world: the NBA's real teams and players (R2)."); }
        var career = new Career(new Rng(seed ^ 0xA0D17));
        var sim = new GameSimulator();
        var fin = league.Finance;

        var pts = new List<double>(); var poss = new List<double>(); var fg = new List<double>(); var tp = new List<double>();
        var tpRate = new List<double>(); var ftRate = new List<double>(); var home = new List<double>(); var ot = new List<double>();
        var rimShare = new List<double>(); var astPer = new List<double>(); var andOnes = new List<double>();
        var best = new List<double>(); var worst = new List<double>(); var spread = new List<double>(); var leader = new List<double>(); var threesLeader = new List<double>();
        var meanCa = new List<double>(); var stars = new List<double>(); var meanAge = new List<double>();
        var devYoung = new List<double>(); var devPrime = new List<double>(); var devOld = new List<double>();
        var retiredN = new List<double>(); var retiredAge = new List<double>(); var firstPick = new List<double>(); var rookieTop5 = new List<double>();
        var overCap = new List<double>(); var payroll = new List<double>(); var faPool = new List<double>(); var taxTeams = new List<double>();
        var devByAge = new Dictionary<int, List<int>>();
        var medBalance = new List<double>(); var richClubs = new List<double>(); var facLevel = new List<double>(); var arenas = new List<double>(); var invested = new List<double>(); var paidOut = new List<double>();
        var overBudget = new List<double>(); var budgetUse = new List<double>(); var gradsSigned = new List<double>(); var bestGrad = new List<double>();
        var buyouts = new List<double>(); var stashes = new List<double>(); var signedAbroad = new List<double>(); var intlPicks = new List<double>(); var cameOver = new List<double>();
        var worldPts = new Dictionary<string, List<double>>(); var worldCa = new Dictionary<string, List<double>>();
        var college = new MadnessTally();
        var nablDown = new List<double>(); var offSize = new List<double>();

        for (int s = 0; s < seasons; s++)
        {
            int year = league.Season;
            var start = new DateOnly(year, 10, 1);
            var players = league.AllActivePlayers.ToList();
            meanCa.Add(players.Average(p => p.CurrentAbility));
            stars.Add(players.Count(p => p.CurrentAbility >= 160));
            meanAge.Add(players.Average(p => p.Age(start)));
            overCap.Add(league.Teams.Count(t => ContractMarket.Payroll(t) > fin.Apron + fin.MinSalary * 2));
            payroll.Add(league.Teams.Average(t => ContractMarket.Payroll(t)) / fin.SalaryCap);
            taxTeams.Add(league.Teams.Count(t => ContractMarket.Payroll(t) > fin.TaxLine));
            if (league.Teams.All(t => t.Money.Ready))
            {
                overBudget.Add(league.Teams.Count(t => ContractMarket.Payroll(t) > t.Money.WageBudget * 1.05));
                budgetUse.Add(league.Teams.Average(t => ContractMarket.Payroll(t) / (double)Math.Max(1, t.Money.WageBudget)));
            }
            faPool.Add(league.FreeAgents.Count);
            if (league.Teams.All(t => t.Money.Ready))
            {
                // Item M (#281): money has somewhere to go. Review #9 found the median balance at 7.5 seasons' revenue after 25 seasons.
                var ratios = league.Teams.Select(t => t.Money.Balance / (double)Math.Max(1, t.Money.Revenue)).OrderBy(x => x).ToList();
                medBalance.Add(ratios[ratios.Count / 2]);
                var everyone = league.Teams.Concat(world.Clubs).Where(t => t.Money.Ready).ToList();
                richClubs.Add(everyone.Count(t => t.Money.Balance > t.Money.Revenue) / (double)Math.Max(1, everyone.Count));
                facLevel.Add(league.Teams.Average(t => Model.ClubFacilities.Kinds.Average(k => t.Money.Facilities.Get(k).Level)));
                arenas.Add(league.Teams.Sum(t => t.Money.Facilities.ArenasBuilt));
                invested.Add(league.Teams.Average(t => t.Money.Facilities.LastInvested + t.Money.Facilities.LastUpkeep) / 1e6);
                paidOut.Add(league.Teams.Average(t => t.Money.Facilities.LastDistribution) / 1e6);
            }
            foreach (var c in world.Competitions.Where(c => c.Clubs.Count > 0))
            {
                // The USA pyramid swaps clubs between its leagues every summer: measure it by level, not by league.
                string label = UsaPyramid.Level(c.Key) is var lv and > 0
                    ? lv switch { 2 => "USA tier 2", 3 => "USA tier 3", 4 => "USA regional leagues", _ => "USA local leagues" } : c.Profile.Name;
                if (UsaPyramid.Level(c.Key) > 0 && worldCa.TryGetValue(label, out var done) && done.Count > s) continue;
                var levelPlayers = (UsaPyramid.Level(c.Key) is var lv2 and > 0
                    ? world.Competitions.Where(x => UsaPyramid.Level(x.Key) == lv2).SelectMany(x => x.Clubs) : c.Clubs).SelectMany(t => t.Roster).ToList();
                (worldCa.TryGetValue(label, out var l) ? l : worldCa[label] = new()).Add(levelPlayers.Average(p => p.CurrentAbility));
            }

            progress?.Invoke($"Season {year}-{(year + 1) % 100:00}");
            // The world: its seasons end before the NABL summer starts; measure them, then the summer moves players.
            world.FinishSeasons(sim);
            if (world.College != null) college.Add(world.College); // C4 step 0: March Madness and college scoring
            foreach (var c in world.Playing)
            {
                var regular = c.Season.Fixtures.Where(f => f.Played && f.Stage == CompetitionSeason.RegularSeason).ToList();
                if (regular.Count > 0) (worldPts.TryGetValue(c.Profile.Name, out var l) ? l : worldPts[c.Profile.Name] = new()).Add(regular.Average(f => (f.HomePts!.Value + f.AwayPts!.Value) / 2.0));
                if (c.Season.ChampionId is { } id) (report.WorldChampions.TryGetValue(c.Profile.Name, out var w) ? w : report.WorldChampions[c.Profile.Name] = new()).Add($"{year}: {world.TeamById(id)?.Abbreviation}");
            }
            var nablBefore = league.Teams.Select(t => t.Id).ToHashSet();
            var played = career.PlaySeason(league, world: world, worldSim: sim);
            if (world.Competitions.Any(c => c.Key == UsaPyramid.Tier2))
            {
                // Divisions (Danny, #318-#319): the last in each division goes down; the pyramid's leagues keep their sizes.
                nablDown.Add(nablBefore.Count(id => league.Teams.All(t => t.Id != id)));
                offSize.Add(world.Competitions.Count(c => UsaPyramid.Level(c.Key) switch { 2 or 3 => c.Clubs.Count != UsaPyramid.NationalSize, 4 => c.Clubs.Count != UsaPyramid.RegionalSize, _ => false })
                    + league.Teams.GroupBy(t => t.Division).Count(g => g.Count() != 5));
            }
            buyouts.Add(world.BuyoutMoves.Count);
            stashes.Add(world.Stashes.Count);
            signedAbroad.Add(world.SignedAbroad.Count);
            cameOver.Add(world.LeftClub.Count(x => x.player.TeamId is { } id && league.Teams.Any(t => t.Id == id)) + world.BuyoutMoves.Count);
            var games = played.Outcome.RegularSeason;
            var boxes = games.SelectMany(g => new[] { g.Home, g.Away }).ToList();
            pts.Add(boxes.Average(b => b.Points));
            poss.Add(boxes.Average(b => b.Possessions));
            fg.Add(boxes.Sum(b => b.FGM) / (double)boxes.Sum(b => b.FGA));
            tp.Add(boxes.Sum(b => b.ThreePM) / (double)boxes.Sum(b => b.ThreePA));
            tpRate.Add(boxes.Sum(b => b.ThreePA) / (double)boxes.Sum(b => b.FGA));
            ftRate.Add(boxes.Sum(b => b.FTA) / (double)boxes.Sum(b => b.FGA));
            rimShare.Add(boxes.Sum(b => b.Lines.Sum(l => l.ZoneAttempts[0])) / (double)boxes.Sum(b => b.FGA));
            astPer.Add(boxes.Average(b => b.Assists));
            andOnes.Add(boxes.Average(b => b.Lines.Sum(l => l.AndOnes)));
            home.Add(games.Count(g => g.Winner == g.Home) / (double)games.Count);
            ot.Add(games.Count(g => g.Overtimes > 0) / (double)games.Count);
            var table = played.Outcome.Standings.All();
            best.Add(table.Max(r => r.Wins)); worst.Add(table.Min(r => r.Wins));
            double mw = table.Average(r => r.Wins);
            spread.Add(Math.Sqrt(table.Average(r => (r.Wins - mw) * (r.Wins - mw))));
            leader.Add(played.Stats.Values.Where(x => x.Games >= 58).Select(x => x.Ppg).DefaultIfEmpty(0).Max());
            threesLeader.Add(played.Stats.Values.Select(x => (double)x.ThreePM).DefaultIfEmpty(0).Max());
            report.Champions.Add($"{year}: {played.Outcome.Champion?.Abbreviation ?? "?"}");

            var off = played.Offseason;
            var dev = off.Development;
            foreach (var d in dev) (devByAge.TryGetValue(d.Age, out var l) ? l : devByAge[d.Age] = new()).Add(d.NewCa - d.OldCa);
            devYoung.Add(dev.Where(d => d.Age <= 22).Select(d => (double)(d.NewCa - d.OldCa)).DefaultIfEmpty(0).Average());
            devPrime.Add(dev.Where(d => d.Age is >= 26 and <= 29).Select(d => (double)(d.NewCa - d.OldCa)).DefaultIfEmpty(0).Average());
            devOld.Add(dev.Where(d => d.Age >= 32).Select(d => (double)(d.NewCa - d.OldCa)).DefaultIfEmpty(0).Average());
            retiredN.Add(off.Retired.Count);
            retiredAge.Add(off.Retired.Select(p => (double)p.Age(new DateOnly(year + 1, 7, 1))).DefaultIfEmpty(0).Average());
            var grads = off.FreeAgentSignings.Where(x => x.player.RookieSeason == year + 1).ToList();
            gradsSigned.Add(grads.Count);
            if (grads.Count > 0) bestGrad.Add(grads.Max(x => x.player.CurrentAbility));
            var draft = off.Draft.OrderBy(d => d.Pick).ToList();
            if (draft.Count > 0) { firstPick.Add(draft[0].Player.CurrentAbility); rookieTop5.Add(draft.Take(5).Average(d => d.Player.CurrentAbility)); }
            intlPicks.Add(draft.Count == 0 ? 0 : draft.Count(d => WorldMarket.IsInternational(d.Player)) / (double)draft.Count);
        }

        var m = report.Metrics;
        // NBA league averages 2018-19 to 2024-25 (basketball-reference): pts 111-118, pace 98-101, FG% .460-.476,
        // 3P% .353-.367, 3PA/FGA .359-.42, FTA/FGA .24-.26, home win% .54-.58 (2021 bubble excluded), OT ~5-6% of games.
        m.Add(new("NABL games", "Points per team per game", "0.0", pts, 108, 120, "NBA 2018-25"));
        m.Add(new("NABL games", "Possessions per team per game", "0.0", poss, 96, 103, "NBA pace 2018-25"));
        m.Add(new("NABL games", "Field goal %", "0.000", fg, 0.450, 0.485, "NBA 2018-25"));
        m.Add(new("NABL games", "Three-point %", "0.000", tp, 0.345, 0.375, "NBA 2018-25"));
        m.Add(new("NABL games", "Threes as share of shots", "0.000", tpRate, 0.34, 0.44, "NBA 2018-25"));
        m.Add(new("NABL games", "Free throws per shot (FTA/FGA)", "0.000", ftRate, 0.22, 0.28, "NBA 2018-25"));
        // Review #6: shots at the rim and assists weren't checked (39% and 24.2 a game); and-ones joined Nov 11.
        // NBA 2018-25: shots within 3 ft ~28-33% of FGA, assists 24.6-26.9 a team, and-ones ~2-3 a team.
        m.Add(new("NABL games", "Shots at the rim (share of FGA)", "0.000", rimShare, 0.27, 0.35, "NBA 2018-25, 0-3 ft"));
        m.Add(new("NABL games", "Assists per team per game", "0.0", astPer, 24, 28, "NBA 2018-25"));
        m.Add(new("NABL games", "And-ones per team per game", "0.0", andOnes, 1.6, 3.6, "NBA 2018-25"));
        m.Add(new("NABL games", "Home win %", "0.000", home, 0.53, 0.60, "NBA 2018-25"));
        m.Add(new("NABL games", "Games going to overtime", "0.000", ot, 0.04, 0.08, "NBA ~5-6%"));
        // Records: NBA best record 57-68 wins, worst 15-25, spread (SD of wins) ~11-14 over recent seasons.
        m.Add(new("NABL competitive balance", "Best record (wins)", "0", best, 56, 68, "NBA 2015-25"));
        m.Add(new("NABL competitive balance", "Worst record (wins)", "0", worst, 12, 26, "NBA 2015-25"));
        m.Add(new("NABL competitive balance", "Spread of wins (std dev)", "0.0", spread, 10, 14.5, "NBA 2015-25"));
        m.Add(new("NABL competitive balance", "Scoring leader (points per game)", "0.0", leader, 28, 36, "NBA 2015-25"));
        // Gated since run 19 (#255): the leader averaged 424-514 before the engine's three-point volume was tuned
        // (seasons up to 618). NBA leaders 2015-25 made about 240-400 threes (record 402). Run 20 (#259, Danny's D15 B):
        // a little looser, so the record falls now and then; the gate is 250-425.
        m.Add(new("NABL competitive balance", "Threes leader (made in a season)", "0", threesLeader, 250, 425, "NBA 2015-25: about 240-400 (record 402)"));
        int distinct = report.Champions.Select(c => c[(c.IndexOf(':') + 2)..]).Distinct().Count();
        // Real leagues have dynasties and repeat winners: the NBA had 7 different champions in 2015-24, 6 in 2005-14.
        // NBA: 6-7 different champions in a decade, but only ~20 franchises in 77 years: repeat winners pile up over
        // long runs, so the band narrows with length (critic review #2: 40-80 per century was impossible).
        double champLo = seasons <= 20 ? Math.Ceiling(seasons * 0.4) : Math.Ceiling(8 + seasons * 0.15), champHi = seasons <= 20 ? Math.Ceiling(seasons * 0.8) : Math.Ceiling(16 + seasons * 0.3);
        m.Add(new("NABL competitive balance", "Different champions", "0", new() { distinct }, seasons >= 5 ? champLo : null, seasons >= 5 ? champHi : null, "NBA: 6-7 per decade; ~20 in 77 years"));
        int mostTitles = report.Champions.GroupBy(c => c[(c.IndexOf(':') + 2)..]).Select(g => g.Count()).DefaultIfEmpty(0).Max();
        m.Add(new("NABL competitive balance", "Most titles by one team", "0", new() { mostTitles }, seasons >= 5 ? 1 : null, seasons >= 5 ? Math.Ceiling(seasons * 0.4) : null, "NBA: 4 in 10 (GSW 2015-24)"));
        // Review #9 (#261): title streaks abroad (review #8 found six leagues with 8+ in 100 seasons). Information for now;
        // the target is at most 2 leagues with an 8+ streak per 100 seasons.
        var streaks = report.WorldChampions.Select(kv => Streak(kv.Value)).ToList();
        m.Add(new("Overseas", "Longest title streak in any league abroad", "0", new() { streaks.DefaultIfEmpty(0).Max() }, null, null, "Maccabi Tel Aviv: 23 (Israel, 1970-92)"));
        m.Add(new("Overseas", "Leagues abroad with a title streak of 8+", "0", new() { streaks.Count(x => x >= 8) }, null, null, "target: at most 2 per 100 seasons"));
        // Players: league talent should neither inflate nor collapse; stars ~1-2 per 3 teams.
        m.Add(new("NABL players", "Mean ability (CA) of rostered players", "0.0", meanCa, meanCa[0] - 5, meanCa[0] + 5, "stable within ±5 of season 1"));
        m.Add(new("NABL players", "Stars (CA 160+)", "0.0", stars, Math.Max(3, stars[0] * 0.6), stars[0] * 1.5 + 1, "stable around season 1"));
        m.Add(new("NABL players", "Mean age", "0.0", meanAge, 25.5, 27.5, "NBA ~26-27"));
        // Peak age: the youngest age at which players, on average, stop improving (a plain "best average ability by age"
        // is skewed by survivorship: only great players are still around at 35).
        var peak = devByAge.Where(kv => kv.Key >= 20 && kv.Value.Count >= 10).OrderBy(kv => kv.Key)
            .FirstOrDefault(kv => kv.Value.Average() <= 0).Key;
        m.Add(new("NABL players", "Age players stop improving", "0", new() { peak }, 26, 30, "NBA aging curves peak ~27"));
        m.Add(new("NABL development", "Yearly ability change, age 22 and under", "+0.0;-0.0", devYoung, 2, 12, "young players improve"));
        m.Add(new("NABL development", "Yearly ability change, age 26-29", "+0.0;-0.0", devPrime, -2, 3, "prime: roughly flat"));
        m.Add(new("NABL development", "Yearly ability change, age 32+", "+0.0;-0.0", devOld, -10, -1, "decline"));
        // Here many veterans finish their careers abroad or down the pyramid instead of retiring from the NABL.
        m.Add(new("NABL development", "Retirements per season", "0", retiredN, 8, 50, "NBA ~20-40; veterans here often move abroad first"));
        if (nablDown.Count > 0)
        {
            m.Add(new("USA pyramid", "Clubs relegated from the NABL a season", "0.0", nablDown, null, null, "the last in each of six divisions (Danny, #319)"));
            m.Add(new("USA pyramid", "Leagues or top-league divisions off their size after the summer", "0", offSize, 0, 0, "must be 0"));
        }
        m.Add(new("NABL development", "Average retirement age", "0.0", retiredAge, 32, 37, "NBA ~33-35"));
        if (fin.OpenMarket)
        {
            // Open market (decisions #80, #88, #90): college graduates instead of a draft; budgets instead of a cap.
            m.Add(new("NABL graduates", "College graduates signed by NABL clubs", "0.0", gradsSigned, 8, 40, "a draft placed ~30-45 rookies a year"));
            m.Add(new("NABL graduates", "Best graduate signed (CA)", "0", bestGrad, null, null, ""));
            m.Add(new("NABL money", "Clubs over their wage budget at season start", "0.0", overBudget, 0, 8, "a few overspend"));
            m.Add(new("NABL money", "Average payroll as share of budget", "0.00", budgetUse, 0.80, 1.05, "clubs spend most of it"));
            // Item M (#281): balances stay realistic once money buys facilities.
            m.Add(new("NABL money", "Median bank balance (seasons of revenue)", "0.00", medBalance, -0.5, 1.2, "item M target: under about one season's revenue"));
            m.Add(new("NABL money", "Clubs anywhere holding more than a season's revenue", "0.00", richClubs, 0, 0.5, "review #9: all 748 by season 15"));
            m.Add(new("NABL money", "Average facility level (1-5)", "0.00", facLevel, 2, 4.6, "an arms race, not inflation: effects are relative"));
            m.Add(new("NABL money", "Spent on facilities a club a season ($M)", "0.0", invested, null, null, "running costs plus projects"));
            m.Add(new("NABL money", "Owners' distributions a club a season ($M)", "0.0", paidOut, null, null, "the last resort when there's nothing to build"));
            m.Add(new("NABL money", "New arenas opened (career total)", "0", new() { arenas.DefaultIfEmpty(0).Max() }, null, null, "NBA: 13 opened 2000-2024"));
        }
        else
        {
            m.Add(new("NABL draft", "No. 1 pick ability (CA)", "0", firstPick, null, null, ""));
            m.Add(new("NABL draft", "Top-5 picks average ability (CA)", "0", rookieTop5, null, null, ""));
            m.Add(new("NABL money", "Teams over the apron at season start", "0", overCap, 0, 0, "must be 0"));
            m.Add(new("NABL money", "Average payroll as share of the cap", "0.00", payroll, fin.SoftCap ? 0.95 : 0.80, fin.SoftCap ? 1.25 : 1.00, fin.SoftCap ? "NBA ~1.05-1.15" : "hard-cap league"));
            m.Add(new("NABL money", "Teams paying luxury tax", "0.0", taxTeams, fin.SoftCap ? 0 : null, fin.SoftCap ? 10 : null, "NBA ~5-8"));
        }
        m.Add(new("NABL money", "Free agents available at season start", "0", faPool, null, null, ""));
        // Movement (decisions #53-56). NBA: roughly 2-6 draft-and-stash picks a year; a few overseas players bought out
        // each summer; international players are about a quarter to a third of draftees.
        if (!fin.OpenMarket)
        {
            m.Add(new("Player movement", "International share of draft picks", "0.00", intlPicks, 0.15, 0.40, "NBA drafts 2015-25"));
            m.Add(new("Player movement", "Draft picks stashed overseas", "0.0", stashes, 1, 8, "NBA ~2-6"));
        }
        m.Add(new("Player movement", "Overseas players bought out by NABL teams", "0.0", buyouts, 0.5, 8, "NBA a few a summer"));
        // With no draft, NABL clubs fill more of their rosters from the shared free-agent pool (open market).
        m.Add(new("Player movement", "Overseas players joining NABL teams (free agency + buyouts)", "0.0", cameOver, 3, fin.OpenMarket ? 40 : 15,
            fin.OpenMarket ? "open market, 45 leagues to come from: more players cross over without a draft" : "rough estimate: NBA ~5-15 a summer incl. players returning from abroad"));
        m.Add(new("Player movement", "Free agents signed by overseas clubs", "0", signedAbroad, null, null, ""));
        // Overseas: EuroLeague ~80-84 pts per team, ACB ~82-85, Turkish BSL ~82-86, NBL ~88-92, CBA ~100-105 (fast, high-scoring).
        foreach (var (name, list) in worldPts)
        {
            // Each league scores at its own level (critic review #2); the check is that level ± 6.
            double target = name switch
            {
                "USA tier 2" => 110, "USA tier 3" => 105, "USA regional leagues" => 100, "USA local leagues" => 96,
                _ => Competitions.All.FirstOrDefault(c => c.Name == name)?.PointsPerTeam ?? 80,
            };
            (double lo, double hi) = (target - 6, target + 6);
            m.Add(new("Overseas", $"{name}: points per team per game", "0.0", list, lo, hi, "recent seasons"));
        }
        foreach (var (name, list) in worldCa)
            m.Add(new("Overseas", $"{name}: mean ability (CA)", "0.0", list, list[0] - 6, list[0] + 6, "stable within ±6 of season 1"));
        if (college.Tournaments > 0) college.AddMetrics(m, seasons);
        report.Elapsed = sw.Elapsed;
        return report;
    }

    /// <summary>The longest run of titles by one club in a "year: ABC" champions list.</summary>
    public static int Streak(IReadOnlyList<string> champions)
    {
        int best = 0, run = 0; string? prev = null;
        foreach (var c in champions)
        {
            var who = c[(c.IndexOf(':') + 2)..];
            run = who == prev ? run + 1 : 1; prev = who;
            if (run > best) best = run;
        }
        return best;
    }
}
