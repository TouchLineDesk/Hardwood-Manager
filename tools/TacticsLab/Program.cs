using Hardwood.Core; using Hardwood.Core.Engine; using Hardwood.Core.Generation; using Hardwood.Core.Model; using Hardwood.Core.World;
// Tactics lab across several matchups: each option for team A vs B on auto; win % averaged over matchups.
var sim = new GameSimulator();
var results = new Dictionary<string, List<double>>();
int nGames = int.Parse(args.Length > 0 ? args[0] : "160");
foreach (ulong seed in new ulong[] { 11, 12, 13, 14, 15, 16 })
{
    var lg = new WorldGenerator(new Rng(seed)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));
    var teams = lg.Teams.OrderBy(t => QuickSim.Strength(t)).ToList();
    var a = teams[15]; var b = teams[14];
    double Win(Action<Team> set)
    {
        a.Tactic = new Tactic(); set(a); b.Tactic = new Tactic();
        if (!a.Tactic.Auto) { var (star, dbl) = CoachAi.TheirStar(b, null); a.Tactic.StarStopperId = star != null ? CoachAi.Stopper(CoachAi.Rotation(a, null))?.Id : null; a.Tactic.DoubleStar = dbl; }
        int w = 0;
        for (int i = 0; i < nGames; i++)
        {
            var r = i % 2 == 0 ? sim.Simulate(a, b, lg.Rules, (ulong)(5000 + i)) : sim.Simulate(b, a, lg.Rules, (ulong)(5000 + i));
            var mine = r.Home.Team == a ? r.Home : r.Away; var them = r.Home.Team == a ? r.Away : r.Home;
            if (mine.Points > them.Points) w++;
        }
        return w * 100.0 / nGames;
    }
    void Add(string k, double v) { (results.TryGetValue(k, out var l) ? l : results[k] = new()).Add(v); }
    double auto = Win(_ => { });
    Add("auto", 0);
    foreach (var os in Enum.GetValues<OffensiveSystem>()) Add($"O {os}", Win(t => { t.Tactic.Auto = false; t.Tactic.System = os; t.Tactic.Scheme = CoachAi.BestScheme(CoachAi.Rotation(t, null)); }) - auto);
    foreach (var d in Enum.GetValues<DefensiveScheme>()) Add($"D {d}", Win(t => { t.Tactic.Auto = false; t.Tactic.Scheme = d; t.Tactic.System = CoachAi.BestSystem(CoachAi.Rotation(t, null)); }) - auto);
    Console.Error.WriteLine($"seed {seed} auto {auto:F1}");
}
foreach (var (k, l) in results) Console.WriteLine($"{k,-22} {l.Average():+0.0;-0.0}  [{string.Join(" ", l.Select(x => x.ToString("+0;-0")))}]");
