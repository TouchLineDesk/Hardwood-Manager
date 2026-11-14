using Hardwood.Core; using Hardwood.Core.Engine; using Hardwood.Core.Generation; using Hardwood.Core.Model; using Hardwood.Core.World;
// Tactics lab across several matchups: each option for team A vs B on auto; win % averaged over matchups.
if (args.Length > 0 && args[0] == "centers")
{
    // Fit centres on real rosters (run 48): mean raw score of each NBA club's top-9 rotation, and 4 standard deviations.
    var offRaw = new Dictionary<OffensiveSystem, List<double>>(); var defRaw = new Dictionary<DefensiveScheme, List<double>>();
    foreach (ulong seed in new ulong[] { 11, 12, 13, 14 })
        foreach (var t in Hardwood.Core.RealWorld.RealWorldData.NewWorld(seed).League.Teams)
        {
            var rot = CoachAi.Rotation(t, null);
            foreach (var s in Enum.GetValues<OffensiveSystem>()) (offRaw.TryGetValue(s, out var l) ? l : offRaw[s] = new()).Add(Systems.Raw(s, rot));
            foreach (var d in Enum.GetValues<DefensiveScheme>()) (defRaw.TryGetValue(d, out var l) ? l : defRaw[d] = new()).Add(Systems.Raw(d, rot));
        }
    static (double m, double sd4) Ms(List<double> l) { double m = l.Average(); return (m, 4 * Math.Sqrt(l.Average(x => (x - m) * (x - m)))); }
    foreach (var (k, l) in offRaw) { var (m, s4) = Ms(l); Console.WriteLine($"O {k,-14} centre {m:F2} spread {s4:F2}"); }
    foreach (var (k, l) in defRaw) { var (m, s4) = Ms(l); Console.WriteLine($"D {k,-14} centre {m:F2} spread {s4:F2}"); }
    return;
}
var sim = new GameSimulator();
var results = new Dictionary<string, List<double>>();
int nGames = int.Parse(args.Length > 0 ? args[0] : "160");
foreach (ulong seed in new ulong[] { 11, 12, 13, 14, 15, 16 })
{
    var lg = Hardwood.Core.RealWorld.RealWorldData.NewWorld(seed).League; // real teams only (D61 B, #478)
    var teams = lg.Teams.OrderBy(t => QuickSim.Strength(t)).ToList();
    var a = teams[15]; var b = teams[14];
    double Win(Action<Team> set, bool everyGame = false)
    {
        a.Tactic = new Tactic(); set(a); b.Tactic = new Tactic();
        if (!a.Tactic.Auto) { var (star, dbl) = CoachAi.TheirStar(b, null); a.Tactic.StarStopperId = star != null ? CoachAi.Stopper(CoachAi.Rotation(a, null))?.Id : null; a.Tactic.DoubleStar = dbl; }
        int w = 0;
        for (int i = 0; i < nGames; i++)
        {
            if (everyGame) set(a);
            var r = i % 2 == 0 ? sim.Simulate(a, b, lg.Rules, (ulong)(5000 + i)) : sim.Simulate(b, a, lg.Rules, (ulong)(5000 + i));
            var mine = r.Home.Team == a ? r.Home : r.Away; var them = r.Home.Team == a ? r.Away : r.Home;
            if (mine.Points > them.Points) w++;
        }
        return w * 100.0 / nGames;
    }
    void Add(string k, double v) { (results.TryGetValue(k, out var l) ? l : results[k] = new()).Add(v); }
    double auto = Win(_ => { });
    Add("auto", 0);
    // The halftime read is on by default (D20), so every row but "scout+half" turns it off to measure the choice alone (review #10).
    // A manual coach who copies, before the game, exactly what auto would pick: measures the cost of being "read".
    Add("M mirror-auto", Win(t => { t.Tactic.Auto = true; CoachAi.Prepare(t, b, null); t.Tactic.Auto = false; t.Tactic.HalftimeAdjust = false; }, everyGame: true) - auto);
    // A manual coach who takes the assistant's suggestion before every game (the Tactics page's "Use suggestion").
    Add("M scout", Win(t => { t.Tactic.Auto = false; t.Tactic.HalftimeAdjust = false; var rot = CoachAi.Rotation(t, null); var (ts, td) = CoachAi.TheirPlan(b, t, null);
        t.Tactic.System = CoachAi.BestSystem(rot, td); t.Tactic.Scheme = CoachAi.BestScheme(rot, ts); }, everyGame: true) - auto);
    // The same, plus the halftime read (#268): the assistant counters what they're really running for the second half.
    Add("M scout+half", Win(t => { t.Tactic.Auto = false; t.Tactic.HalftimeAdjust = true; var rot = CoachAi.Rotation(t, null); var (ts, td) = CoachAi.TheirPlan(b, t, null);
        t.Tactic.System = CoachAi.BestSystem(rot, td); t.Tactic.Scheme = CoachAi.BestScheme(rot, ts); }, everyGame: true) - auto);
    foreach (var os in Enum.GetValues<OffensiveSystem>()) Add($"O {os}", Win(t => { t.Tactic.Auto = false; t.Tactic.HalftimeAdjust = false; t.Tactic.System = os; t.Tactic.Scheme = CoachAi.BestScheme(CoachAi.Rotation(t, null)); }) - auto);
    foreach (var d in Enum.GetValues<DefensiveScheme>()) Add($"D {d}", Win(t => { t.Tactic.Auto = false; t.Tactic.HalftimeAdjust = false; t.Tactic.Scheme = d; t.Tactic.System = CoachAi.BestSystem(CoachAi.Rotation(t, null)); }) - auto);
    Console.Error.WriteLine($"seed {seed} auto {auto:F1}");
}
foreach (var (k, l) in results) Console.WriteLine($"{k,-22} {l.Average():+0.0;-0.0}  [{string.Join(" ", l.Select(x => x.ToString("+0;-0")))}]");
