using System.Diagnostics;
using Hardwood.Core; using Hardwood.Core.Engine; using Hardwood.Core.Model; using Hardwood.Core.Season; using Hardwood.Core.World;
using Hardwood.Core.Guide; using Hardwood.Core.Generation; using Hardwood.Core.People;

// ---------------------------------------------------------------- 1. Tactics and coaching experiment
if (args[0] == "tactics")
{
    var lg = new WorldGenerator(new Rng(11)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));
    var teams = lg.Teams.OrderBy(t => QuickSim.Strength(t)).ToList();
    var a = teams[14]; var b = teams[15];
    var sim = new GameSimulator();
    double WinPct(Action<Team> setA, int n = 300)
    {
        var saved = (a.Tactic.Auto, a.Tactic.System, a.Tactic.Scheme, a.Tactic.Pace, a.Tactic.ThreePointEmphasis, a.Tactic.CrashGlass, a.Tactic.DefensiveAggression);
        setA(a); int w = 0; double margin = 0;
        for (int i = 0; i < n; i++) { var r = i % 2 == 0 ? sim.Simulate(a, b, lg.Rules, (ulong)(1000 + i)) : sim.Simulate(b, a, lg.Rules, (ulong)(1000 + i)); var mine = r.Home.Team == a ? r.Home : r.Away; var them = r.Home.Team == a ? r.Away : r.Home; if (mine.Points > them.Points) w++; margin += mine.Points - them.Points; }
        (a.Tactic.Auto, a.Tactic.System, a.Tactic.Scheme, a.Tactic.Pace, a.Tactic.ThreePointEmphasis, a.Tactic.CrashGlass, a.Tactic.DefensiveAggression) = saved;
        return Math.Round(w * 100.0 / n, 1) + margin / n / 1000; // pct + tiny margin hint
    }
    Console.WriteLine($"A={a.FullName} str {QuickSim.Strength(a):F1}  B={b.FullName} str {QuickSim.Strength(b):F1}");
    Console.WriteLine($"baseline auto: {WinPct(_ => { }):F3}");
    foreach (var os in Enum.GetValues<OffensiveSystem>()) Console.WriteLine($"offense {os}: {WinPct(t => { t.Tactic.Auto = false; t.Tactic.System = os; }):F3}");
    foreach (var d in Enum.GetValues<DefensiveScheme>()) Console.WriteLine($"defense {d}: {WinPct(t => { t.Tactic.Auto = false; t.Tactic.Scheme = d; }):F3}");
    foreach (var p in new[] { 90.0, 99, 108 }) Console.WriteLine($"pace {p}: {WinPct(t => { t.Tactic.Auto = false; t.Tactic.Pace = p; }):F3}");
    foreach (var e in new[] { 20, 50, 80 }) Console.WriteLine($"3pt emphasis {e}: {WinPct(t => { t.Tactic.Auto = false; t.Tactic.ThreePointEmphasis = e; }):F3}");
    // Morale
    Console.WriteLine($"morale all 20: {WinPct(t => { foreach (var p in t.Roster) p.Morale = 20; }):F3}");
    foreach (var p in a.Roster) p.Morale = 60;
    Console.WriteLine($"morale all 95: {WinPct(t => { foreach (var p in t.Roster) p.Morale = 95; }):F3}");
    foreach (var p in a.Roster) p.Morale = 60;
    // Home edge, raw talent: swap in a +10 CA player
    var star = a.Roster.OrderByDescending(p => p.CurrentAbility).First();
    var ca = star.CurrentAbility;
    Console.WriteLine($"best player removed (injured): {WinPct(t => { star.Injury = new Injury { Name = "x", Occurred = new DateOnly(2026,1,1), ReturnDate = new DateOnly(2030,1,1) }; }):F3}");
    star.Injury = null;
    // Rotation: starters 40 minutes
    Console.WriteLine($"starters-heavy rotation: {WinPct(t => { var ids = t.Roster.OrderByDescending(p => p.CurrentAbility).Select(p => p.Id).ToList(); t.DepthChart = new DepthChart { Starters = ids.Take(5).ToList(), MinutesTargets = ids.Select((id, i) => (id, i < 5 ? 40.0 : i < 7 ? 20.0 : 0.0)).ToDictionary(x => x.id, x => x.Item2) }; }):F3}");
    a.DepthChart = null;
    return;
}

// ---------------------------------------------------------------- 2. Careers played by a scripted manager
string label = args[0]; int seasons = int.Parse(args[1]); ulong seed = ulong.Parse(args[2]);
var probe = new WorldGenerator(new Rng(seed)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));
var probeWorld = GameWorld.Generate(probe, seed, Array.Empty<string>(), new DateOnly(2026, 10, 1));
// Review #8: real-world careers (R1-R2). The real NBA's strongest and weakest teams after the real players are placed.
bool real = label.StartsWith("nba-real");
if (real)
{
    probe = new WorldGenerator(new Rng(seed)).Generate(Hardwood.Core.RealWorld.RealWorldData.LeagueName, 2026, new DateOnly(2026, 10, 1));
    probeWorld = GameWorld.Generate(probe, seed, Array.Empty<string>(), new DateOnly(2026, 10, 1));
    Hardwood.Core.RealWorld.RealWorldData.Apply(probe, probeWorld, seed);
}
int teamId = label switch
{
    "nabl-weak" => probe.Teams.OrderBy(t => QuickSim.Strength(t)).First().Id,
    "nabl-strong" => probe.Teams.OrderByDescending(t => QuickSim.Strength(t)).First().Id,
    "nba-real-strong" => probe.Teams.OrderByDescending(t => QuickSim.Strength(t)).First().Id,
    "nba-real-weak" => probe.Teams.OrderBy(t => QuickSim.Strength(t)).First().Id,
    "nba-real-mid" => probe.Teams.OrderBy(t => QuickSim.Strength(t)).ElementAt(14).Id,
    // A chosen real franchise: nba-real-OKC, nba-real-MIA, ...
    _ when label.StartsWith("nba-real-") && label.Length == 12 => probe.Teams.First(t => t.Abbreviation == label[9..]).Id,
    "lithuania" => probeWorld[WorldLeagues.Lithuania].Clubs.OrderBy(t => QuickSim.Strength(t)).First().Id,
    "local" => probeWorld[UsaPyramid.LocalKey("txn")].Clubs.OrderByDescending(t => QuickSim.Strength(t)).First().Id,
    "japan" => probeWorld[WorldLeagues.Japan].Clubs.OrderBy(t => QuickSim.Strength(t)).ElementAt(4).Id,
    // College sim C2 (#168): a mid-table program (the 50th strongest of 148) and a blue blood.
    "college" => probeWorld.College!.Programs.OrderByDescending(t => QuickSim.Strength(t)).ElementAt(49).Id,
    "college-strong" => probeWorld.College!.Programs.OrderByDescending(t => QuickSim.Strength(t)).First().Id,
    _ => throw new Exception(label),
};
var path = Path.Combine(Path.GetTempPath(), $"critic-{label}.hwsave"); if (File.Exists(path)) File.Delete(path);
var sw = Stopwatch.StartNew();
var s = GameSession.NewGame(seed, teamId, path, realWorld: real);
var progress = new GuideProgress();
var log = new List<string>();
void L(string x) { log.Add(x); Console.WriteLine($"[{label}] {x}"); }
L($"start: {s.HumanTeam.FullName} in {s.HumanLeagueName}, level {s.MyLevel}, budget ${s.HumanTeam.Money.WageBudget / 1e6:F2}M");
int longestUnhappy = 0, unhappyPlayerWeeks = 0, overBudgetWeeks = 0, weeksEmployed = 0;
int recruitCommits = 0;
int decisions = 0, signings = 0, talks = 0, bidsMade = 0, bidsWon = 0, sold = 0, renewed = 0, released = 0, urgentSeen = 0, tipsSeen = 0, sackings = 0, jobs = 0;
var assistantTitles = new Dictionary<string, int>();
var mediaAnswers = new Dictionary<string, int>();
int staffExtended = 0;
var bidOutcomes = new Dictionary<string, int>();
var rng = new Rng(seed + 7);
int startYear = s.League.Season;
int lastDay = 0;
// Review #8: in a real career, where the real stars are and what they're paid.
var realNames = Hardwood.Core.RealWorld.RealPlayers.Lines.Select(l => l.Name).ToHashSet();
void Stars(string when)
{
    if (!real) return;
    var top = s.League.Teams.SelectMany(t => t.Roster.Select(p => (p, t))).OrderByDescending(x => x.p.CurrentAbility).Take(8);
    L($"{when} top players: " + string.Join("; ", top.Select(x => $"{x.p.Name} {x.t.Abbreviation} CA {x.p.CurrentAbility} age {x.p.Age(s.Today)} ${(x.p.Contract?.Salary ?? 0) / 1e6:F1}M x{x.p.Contract?.Years}")));
    L($"{when} real players still in the league: {s.League.Teams.Sum(t => t.Roster.Count(p => realNames.Contains(p.Name)))}; ages 35+: {s.League.Teams.Sum(t => t.Roster.Count(p => p.Age(s.Today) >= 35))}");
    var where = s.World.Clubs.Concat(s.League.Teams).Distinct().SelectMany(t => t.Roster.Where(p => realNames.Contains(p.Name)).Select(p => s.League.Teams.Contains(t) ? "NBA" : s.World.LeagueOf(t)?.Profile.Name ?? "?"))
        .Concat(s.League.FreeAgents.Where(p => realNames.Contains(p.Name)).Select(_ => "free agent")).GroupBy(x => x).OrderByDescending(g => g.Count());
    L($"{when} real players by league: " + string.Join(", ", where.Select(g => $"{g.Key} {g.Count()}")) + $"; real teams in the top league: {s.League.Teams.Count(t => t.Id <= 30)}");
}
Stars("start");
// Review #9 (#235): a long career's health, season by season: messages created (the inbox keeps only 400),
// save size and load time every 5 seasons, and club balances against a season's revenue.
int msgMark = s.Inbox.Count == 0 ? 0 : s.Inbox.Max(m => m.Id), overMark = 0, seasonNo = 0;
void Health()
{
    seasonNo++;
    int top = s.Inbox.Count == 0 ? msgMark : s.Inbox.Max(m => m.Id);
    var clubs = s.League.Teams.Concat(s.World.Clubs).Distinct().Where(t => t.Money.Revenue > 0).ToList();
    var bal = s.League.Teams.Select(t => t.Money.Balance / 1e6).OrderBy(x => x).ToList();
    string line = $"health season {s.League.Season} (#{seasonNo}): messages created {top - msgMark}, over budget weeks {overBudgetWeeks - overMark}; top-league balances median ${bal[bal.Count / 2]:F0}M max ${bal[^1]:F0}M min ${bal[0]:F0}M; clubs holding over a season's revenue {clubs.Count(t => t.Money.Balance > t.Money.Revenue)} of {clubs.Count}; save {new FileInfo(path).Length / 1e6:F1} MB";
    msgMark = top; overMark = overBudgetWeeks;
    if (seasonNo % 5 == 0)
    {
        var lw = Stopwatch.StartNew();
        try { var again = GameSession.Load(path); line += $"; load {lw.Elapsed.TotalSeconds:F1} s ({again.League.Teams.Count} teams, {again.World.Players.Count()} world players)"; }
        catch (Exception e) { line += $"; LOAD FAILED: {e.GetType().Name}: {e.Message}"; }
    }
    L(line);
}
while (s.League.Season < startYear + seasons)
{
    // --- weekly management
    if (s.Today.DayNumber - lastDay >= 7)
    {
        lastDay = s.Today.DayNumber;
        var adv = Assistant.Advise(s, "Home", GuideLevel.Auto, progress);
        foreach (var t in adv.Tasks) { var k = System.Text.RegularExpressions.Regex.Replace(t.Title.Split(':')[0], @"\d+(\.\d+)?", "#").Trim(); assistantTitles[k] = assistantTitles.GetValueOrDefault(k) + 1; if (t.Priority < 30) urgentSeen++; }
        tipsSeen += adv.Strategy.Count;
        // Staff part 2: extend the good staff whose contracts end this summer.
        if (s.Employed && s.Today.Month is >= 2 and <= 6)
            foreach (var st in s.StaffExpiring.Where(x => x.KeyAttribute >= 9).ToList())
                if (s.ExtendStaff(st).StartsWith("Done")) { decisions++; staffExtended++; }
        // Press conferences and locker-room moments (people part 2): answer like a player who reads the hints.
        foreach (var d in s.OpenDecisions.ToList())
        {
            int pick = d.Options.FindIndex(o => o.Hint.StartsWith("Spirit up") || o.Hint.Contains("; spirit up"));
            if (pick < 0) pick = d.Options.FindIndex(o => o.Hint.StartsWith("Squad morale up") || o.Hint.StartsWith("Morale up"));
            if (pick < 0) pick = d.SafeOption;
            s.Answer(d.Id, pick); decisions++;
            mediaAnswers[d.Topic + ":" + pick] = mediaAnswers.GetValueOrDefault(d.Topic + ":" + pick) + 1;
        }
        // Like a player, read the mail the assistant points at (so the count measures new mail, not a growing backlog).
        foreach (var m in s.Inbox.Where(m => !m.Read && m.Category is "Board" or "Medical" or "Transactions" or "Squad" or "Staff")) m.Read = true;
        progress.DaysPlayed += 7;
        if (s.Employed)
        {
            foreach (var p in s.HumanTeam.Roster) { int w = p.UnhappyWeeks; if (w > longestUnhappy) longestUnhappy = w; if (p.Morale < 35) unhappyPlayerWeeks++; }
            if (s.OverWageBudget) overBudgetWeeks++;
            weeksEmployed++;
        }
        if (s.Employed)
        {
            foreach (var p in s.HumanTeam.Roster.Where(p => p.Morale < 40).Take(2)) { s.Talk(p, TalkTopic.AskWhatsWrong); talks++; decisions++; }
            foreach (var bid in s.IncomingBids.ToList())
            {
                var pl = s.HumanTeam.Roster.FirstOrDefault(p => p.Id == bid.PlayerId);
                decisions++;
                if (pl != null && !TransferMarket.KeyPlayer(pl, s.HumanTeam) && bid.Fee >= s.AskingPrice(pl)) { s.AcceptBid(bid); sold++; } else s.RejectBid(bid);
            }
            // Transfer window: one serious bid a week for an upgrade we can afford.
            if (s.WindowOpen && s.HumanTeam.Money.Ready)
            {
                long room = s.HumanTeam.Money.WageBudget - s.MyCommittedWages;
                var worst = s.HumanTeam.Roster.OrderBy(p => p.CurrentAbility).Skip(2).First().CurrentAbility;
                // Roadmap 2b: a manager sees the likely fee and whether he'd come; the bot uses only what the screen shows.
                var target = s.Market().Where(x => x.player.CurrentAbility > worst + 8 && x.player.Age(s.Today) <= 30 && s.WontCome(x.player) == null)
                    .Select(x => (x.player, x.club, likely: s.LikelyFee(x.player))).Where(x => x.likely.High <= s.HumanTeam.Money.TransferBudget && s.PeekDemand(x.player).Wage < room)
                    .OrderByDescending(x => x.player.CurrentAbility).FirstOrDefault();
                if (target.player != null && s.HumanTeam.Roster.Count < (s.InNabl ? s.League.Finance.RosterMax : 15))
                {
                    bidsMade++; decisions++;
                    var r = s.Bid(target.player, target.likely.Mid);
                    if (r.Answer == BidAnswer.Countered && r.Fee <= s.HumanTeam.Money.TransferBudget) { decisions++; r = s.Bid(target.player, r.Fee); }
                    string Why(string t) => System.Text.RegularExpressions.Regex.Replace(t.Replace(target.player.Name, "X").Replace(target.club.FullName, "C"), @"\$[\d.]+M", "$#M").Split(". ")[0];
                    if (r.Answer == BidAnswer.Accepted)
                    {
                        var d = s.DemandOf(target.player); var reply = s.Offer(target.player, d.Wage, d.Years);
                        if (reply.Accepted) { bidsWon++; signings++; bidOutcomes["won"] = bidOutcomes.GetValueOrDefault("won") + 1; L($"{s.Today}: bought {target.player.Name} (CA {target.player.CurrentAbility}) from {target.club.FullName}"); }
                        else { var k = "terms: " + Why(reply.Text); bidOutcomes[k] = bidOutcomes.GetValueOrDefault(k) + 1; }
                    }
                    else { var k = $"{r.Answer}: " + Why(r.Text); bidOutcomes[k] = bidOutcomes.GetValueOrDefault(k) + 1; }
                }
            }
        }
        // Senior recruiting (decision #157): court three seniors who'd start for us, and offer their ask once they're keen.
        if (s.Employed && s.RecruitingOpen)
        {
            var targets = s.Seniors().Take(80).Where(p => s.InterestIn(p).Blocked == null && s.Recruiting!.Find(p.Id)?.CommittedTeamId == null && s.InterestIn(p).Minutes >= 70 && s.InterestIn(p).Need - s.InterestIn(p).Total <= 28)
                .OrderByDescending(p => p.CurrentAbility).Take(2).ToList(); // two at a time: the best seniors take real courting
            foreach (var p in targets)
            {
                var st = s.Recruiting!.Find(p.Id);
                if (st == null || st.Visits < 2) s.VisitRecruit(p); else s.CallRecruit(p);
                if (s.InterestIn(p).Enough)
                {
                    var d = s.RecruitDemand(p);
                    var r = s.OfferRecruit(p, d.Wage, 3); decisions++;
                    if (r.Contains("commits to the")) { recruitCommits++; L($"{s.Today}: {p.Name} (CA {p.CurrentAbility}) commits"); }
                }
            }
        }
        else if (!s.Employed)
        {
            foreach (var v in s.Job.Vacancies.Where(v => v.Status == "open").Take(3)) { s.Apply(v.TeamId); decisions++; }
            foreach (var v in s.Job.Vacancies.Where(v => v.Status == "offered").ToList()) { if (s.AcceptOffer(v.TeamId) == null) { jobs++; L($"{s.Today}: hired by {s.HumanTeam.FullName} ({s.HumanLeagueName})"); } decisions++; break; }
        }
    }
    // --- the offseason
    if (s.Phase == SessionPhase.Offseason && s.Offseason is { } off)
    {
        if (off.Phase == OffseasonPhase.ReSigning && s.Employed)
        {
            var keep = s.HumanTeam.Roster.OrderByDescending(p => p.CurrentAbility).Take(10).ToHashSet();
            foreach (var p in s.MyExpiring.ToList())
            {
                decisions++;
                if (keep.Contains(p) && s.CannotTalk(p) == null) { var d = s.DemandOf(p); if (s.Offer(p, d.Wage, d.Years).Accepted) { renewed++; continue; } }
                s.ReleaseExpiring(p); released++;
            }
        }
        if (off.Phase == OffseasonPhase.FreeAgency && s.Employed)
        {
            int min = s.InNabl ? s.League.Finance.RosterMin : GameWorld.ManagedMinimum + 2;
            foreach (var fa in s.League.FreeAgents.Where(p => p.Age(s.Today) >= (s.HumanLeague?.Profile.MinAge ?? 0)).OrderByDescending(p => p.CurrentAbility).Take(60).ToList())
            {
                if (s.HumanTeam.Roster.Count >= min + 1) break;
                if (s.CannotTalk(fa) != null) continue;
                var d = s.DemandOf(fa); decisions++;
                var reply = s.Offer(fa, d.Wage, d.Years);
                if (reply.Accepted) signings++;
            }
        }
        int season = s.League.Season;
        var before = s.Phase;
        if (off.Phase is OffseasonPhase.FreeAgency or OffseasonPhase.Complete) progress.SeasonsPlayed++;
        s.AdvanceOffseason();
        if (s.Phase != SessionPhase.Offseason) { Health(); Stars($"season {s.League.Season}"); }
        if (s.Phase != SessionPhase.Offseason)
            L($"season {s.League.Season} begins: {s.HumanTeam.FullName} ({s.HumanLeagueName}, level {s.MyLevel}) roster {s.HumanTeam.Roster.Count}, wages ${s.MyCommittedWages / 1e6:F2}M / ${s.HumanTeam.Money.WageBudget / 1e6:F2}M, board {s.Job.Confidence:F0}, goal {s.Job.Current?.Goal}, protected {s.Job.ProtectedSeason} {s.Job.ProtectedReason}");
        if (s.Phase != SessionPhase.Offseason && s.MyCommittedWages > s.HumanTeam.Money.WageBudget)
            L("  over budget at the start; roster: " + string.Join(", ", s.HumanTeam.Roster.OrderByDescending(p => p.Contract?.Salary ?? 0)
                .Select(p => $"{p.Name} ${(p.Contract?.Salary ?? 0) / 1e6:F2}M x{p.Contract?.Years} from {p.Contract?.StartSeason}")));
        continue;
    }
    bool wasEmployed = s.Employed;
    var team = s.HumanTeam;
    s.AdvanceDay();
    // The recruiting trail (C3): the day after signing day, how our class turned out.
    if (s.TrailOpen && s.World.College is { } tw && s.Today == Hardwood.Core.College.CollegeWorld.SigningDay(tw.Season.Year + 1).AddDays(1))
    {
        int cls = tw.Season.Year + 1;
        var ours = tw.ClassOf(cls).Select(p => tw.Recruits[p.Id]).Where(r => r.CommittedTo == team.Id).ToList();
        var ranking = tw.ClassRanking(cls);
        L($"recruiting class of {cls}: {ours.Count} signed ({ours.Count(r => r.Stars == 5)}x5, {ours.Count(r => r.Stars == 4)}x4, {ours.Count(r => r.Stars == 3)}x3, {ours.Count(r => r.Stars <= 2)}x2), best No. {(ours.Count > 0 ? ours.Min(r => r.Rank) : 0)}, class rank {ranking.FindIndex(x => x.Program == team) + 1} of {ranking.Count}; offered {tw.Recruits.Values.Count(r => r.ClassOf == cls && r.CourtedBy == team.Id && r.Offered)}; five-stars in class signed {tw.ClassOf(cls).Count(p => tw.Recruits[p.Id].Stars == 5 && tw.Recruits[p.Id].CommittedTo != null)}; {s.Delegated.LastRecruiting}");
        var bd = tw.BoardFor(cls);
        foreach (var p in tw.ClassOf(cls).Where(p => tw.Recruits[p.Id].CommittedTo == team.Id).OrderBy(p => tw.Recruits[p.Id].Rank))
        {
            var r = tw.Recruits[p.Id];
            var ranks = tw.PrestigeRanks(); // the best rival that was recruiting him (C4 step 0: computer schools have a reach)
            var best = tw.Programs.Where(o => o != team && r.Rank <= Hardwood.Core.College.CollegeWorld.Reach(ranks[o.Id], 2)).Select(o => (o, a: tw.Appeal(o, p, r, bd))).DefaultIfEmpty((team, 0)).OrderByDescending(x => x.a).First();
            L($"  recruit No. {r.Rank} {r.Stars}-star on {r.CommittedOn}: interest {Hardwood.Core.College.CollegeWorld.InterestIn(r, team):F0}, offered {r.Offered}, cares about {Hardwood.Core.College.CollegeWorld.PriorityOf(p)}, our appeal {tw.Appeal(team, p, r, bd):F1} vs best rival {best.o.City} {best.a:F1}");
        }
    }
    if (wasEmployed && !s.Employed) { sackings++; L($"{s.Today}: SACKED by {team.FullName} at {s.MyRecord.Wins}-{s.MyRecord.Losses}"); }
    if (s.Phase == SessionPhase.Offseason && s.Offseason?.Phase == OffseasonPhase.ReSigning && s.Employed && !log.Last().StartsWith($"end {s.League.Season}"))
    {
        var rec = s.MyRecord;
        L($"end {s.League.Season}: {rec.Wins}-{rec.Losses}, rank {s.MyRank} in {s.MyTableName}, board {s.Job.Confidence:F0}, level {s.MyLevel}, balance ${s.HumanTeam.Money.Balance / 1e6:F1}M");
        if (s.InCollege && s.World.College is { } cw)
        {
            var conf = s.MyStandings[s.HumanTeam];
            int reached = cw.MadnessReached(s.HumanTeam.Id);
            L($"college {cw.Season.Year}: conference {conf.Wins}-{conf.Losses}, seed {(cw.Season.Seeds.TryGetValue(s.HumanTeam.Id, out var sd) ? sd.ToString() : "-")}, March Madness {(reached < 0 ? "missed" : reached == 7 ? "CHAMPIONS" : Hardwood.Core.College.CollegeWorld.RoundName(reached))}, champion {cw.Program(cw.Season.ChampionId ?? 0)?.FullName}, roster {s.HumanTeam.Roster.Count}, full-engine box scores {s.CollegeResults.Count}");
        }
    }
}
L($"done in {sw.Elapsed.TotalMinutes:F1} min. decisions {decisions}, signings {signings}, renewals {renewed}, releases {released}, talks {talks}, bids {bidsMade} (won {bidsWon}), sold {sold}, sackings {sackings}, new jobs {jobs}");
L("bids: " + string.Join("; ", bidOutcomes.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} x{kv.Value}")));
L($"assistant: urgent tasks seen {urgentSeen}, strategy tips {tipsSeen}; tasks: " + string.Join(", ", assistantTitles.OrderByDescending(kv => kv.Value).Take(12).Select(kv => $"{kv.Key} x{kv.Value}")));
L($"people: longest unhappy spell {longestUnhappy} weeks, unhappy player-weeks {unhappyPlayerWeeks}, squad messages: " + string.Join(", ", s.Inbox.Where(m => m.Category == "Squad").GroupBy(m => m.Title.Contains("settles") ? "settles" : m.Title.Contains("requests a trade") ? "trade request" : m.Title.Contains("wants a word") ? "wants a word" : "other").Select(g => $"{g.Key} x{g.Count()}")));
L($"money: over the wage budget {overBudgetWeeks} of {weeksEmployed} weeks employed");
L("board titles: " + string.Join(", ", s.Inbox.Where(m => m.Category == "Board").GroupBy(m => new string(m.Title.TakeWhile(c => c != ':').ToArray())).OrderByDescending(g => g.Count()).Take(10).Select(g => $"{g.Key} x{g.Count()}")));
L($"recruiting: {recruitCommits} seniors committed to us");
L($"staff: extended {staffExtended}, on staff now {s.HumanTeam.Staff.Count}; coach " + string.Join(" ", Enum.GetValues<Hardwood.Core.Jobs.CoachAttr>().Select(a => $"{a} {s.Coach[a]}")));
L($"media: {s.Media.Decisions.Count(d => d.Kind == "press")} press conferences, {s.Media.Decisions.Count(d => d.Kind == "locker")} locker-room moments; answers " + string.Join(", ", mediaAnswers.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} x{kv.Value}")));
L($"inbox messages: {s.Inbox.Count}, unread {s.Inbox.Count(m => !m.Read)}; categories: " + string.Join(", ", s.Inbox.GroupBy(m => m.Category).Select(g => $"{g.Key} {g.Count()}")));
// R2 part 2b: the real record chase (records broken, record watch, legends passed), in full.
foreach (var m in s.Inbox.Where(m => m.Title.StartsWith("NBA record") || m.Title.StartsWith("Record watch") || m.Title.Contains("all-time scoring list") || m.Title.Contains("ties an NBA record")).Reverse())
    L($"record chase {m.Date:MM/dd/yyyy}: {m.Title} | {m.Body}");
L("inbox titles: " + string.Join(", ", s.Inbox.GroupBy(m => System.Text.RegularExpressions.Regex.Replace(m.Title.Split(':')[0], @"\d+(\.\d+)?", "#")).OrderByDescending(g => g.Count()).Take(25).Select(g => $"{g.Key} x{g.Count()}")));
foreach (var m in s.Inbox.Where(m => m.Category == "Board" && (m.Title.StartsWith("Season review") || m.Title.StartsWith("Expectations") || m.Title.Contains("sacked"))).OrderBy(m => m.Date))
    L($"board message {m.Date:MM/dd/yyyy} {m.Title}: {m.Body.Replace('\n', ' ')}");
L($"save size {new FileInfo(path).Length / 1e6:F1} MB");
