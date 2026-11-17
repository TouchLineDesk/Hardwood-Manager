using Hardwood.Core.Model;

namespace Hardwood.Core.Engine;

/// <summary>
/// A ready-made tactical setup the Tactics page offers as a card (Danny, #475, D60 A): a system, a scheme and the
/// sliders, with how well the roster fits it and one line on why. Applying it never touches roles, matchups or presets.
/// </summary>
public sealed record TacticSetup(
    string Key, string Title, string Pitch, OffensiveSystem System, DefensiveScheme Scheme,
    double Pace, int ThreePointEmphasis, int CrashGlass, int DefensiveAggression, int StarUsage,
    double SystemFit, double SchemeFit, double Edge, bool Recommended)
{
    /// <summary>
    /// The setup's value on the one matchup model (<see cref="MatchupRead.Value"/>, K2): the two fits, plus the matchup
    /// edge against the next opponent. The assistant's pick is the card with the highest value.
    /// </summary>
    public double Value => MatchupRead.Value(SystemFit, SchemeFit, Edge, System, Scheme);

    /// <summary>The setup's rating for its bar and stars, 0..1 (the value, clamped).</summary>
    public double Score => Math.Clamp(Value, 0, 1);

    public void ApplyTo(Tactic t)
    {
        t.System = System; t.Scheme = Scheme; t.Pace = Pace; t.ThreePointEmphasis = ThreePointEmphasis;
        t.CrashGlass = CrashGlass; t.DefensiveAggression = DefensiveAggression; t.StarUsage = StarUsage;
    }

    /// <summary>Whether a tactic already runs this setup (system, scheme and sliders).</summary>
    public bool Matches(Tactic t) => t.System == System && t.Scheme == Scheme && Math.Abs(t.Pace - Pace) < 0.5
        && t.ThreePointEmphasis == ThreePointEmphasis && t.CrashGlass == CrashGlass && t.DefensiveAggression == DefensiveAggression && t.StarUsage == StarUsage;
}

/// <summary>One line of the matchup board: our player against theirs at a position.</summary>
public sealed record PositionMatchup(Position Position, Player? Ours, Player? Theirs)
{
    /// <summary>Positive when ours is better (current ability).</summary>
    public int Edge => (Ours?.CurrentAbility ?? 0) - (Theirs?.CurrentAbility ?? 0);
}

public static class TacticSetups
{
    /// <summary>The five a coach starts: the depth chart's starters when set, otherwise the best player at each position.</summary>
    public static List<Player> StartingFive(Team team, DateOnly? on)
    {
        var healthy = team.Roster.Where(p => on == null || p.IsAvailable(on.Value)).ToList();
        var chart = team.DepthChart?.Starters.Select(id => healthy.FirstOrDefault(p => p.Id == id)).Where(p => p != null).Cast<Player>().ToList();
        if (chart is { Count: 5 }) return chart;
        var five = new List<Player>();
        foreach (var pos in Enum.GetValues<Position>())
        {
            var best = healthy.Where(p => !five.Contains(p)).OrderByDescending(p => p.PrimaryPosition == pos ? 1 : p.SecondaryPosition == pos ? 0.5 : 0)
                .ThenByDescending(p => p.CurrentAbility).FirstOrDefault();
            if (best != null) five.Add(best);
        }
        return five;
    }

    /// <summary>Our starting five against theirs, PG to C.</summary>
    public static List<PositionMatchup> Board(Team us, Team them, DateOnly? on)
    {
        var a = StartingFive(us, on); var b = StartingFive(them, on);
        return Enum.GetValues<Position>().Select((pos, i) => new PositionMatchup(pos, i < a.Count ? a[i] : null, i < b.Count ? b[i] : null)).ToList();
    }

    /// <summary>
    /// The suggested setups, assistant's pick first. <paramref name="opp"/> (the next opponent, if any) adds a game-plan
    /// card and each card's matchup edge against what he expects them to run.
    /// </summary>
    public static List<TacticSetup> Suggest(Team team, Team? opp, DateOnly? on)
    {
        var rot = CoachAi.Rotation(team, on);
        var five = StartingFive(team, on);
        var read = opp == null ? null : MatchupRead.Of(team, opp, on);
        double Edge(OffensiveSystem s, DefensiveScheme d) => read?.Edge(s, d) ?? 0;
        // K2: every card is rated on the same rotation and the same model, so the cards, the board and the assistant agree.
        TacticSetup Make(string key, string title, string pitch, OffensiveSystem s, DefensiveScheme d, double pace = 99, int three = 50, int glass = 50, int aggr = 50, int star = 50) =>
            new(key, title, pitch, s, d, pace, three, glass, aggr, star, Systems.Fit(s, rot), Systems.Fit(d, rot), Edge(s, d), false);

        var list = new List<TacticSetup>();
        var bestS = CoachAi.BestSystem(rot); var bestD = CoachAi.BestScheme(rot);
        list.Add(Make("fit", "Best fit", $"What your rotation suits most: {Systems.Name(bestS).ToLower()} and {Systems.Name(bestD).ToLower()}.", bestS, bestD));
        if (opp != null && read != null)
        {
            var (cs, cd) = CoachAi.Counter(rot, read.TheirSystem, read.TheirScheme);
            list.Add(Make("plan", $"Game plan: {opp.Name}", read.PlanPitch(cs, cd), cs, cd));
        }
        var fs = CoachAi.BestSystem(five); var fd = CoachAi.BestScheme(five);
        list.Add(Make("five", "Built on your starting five", $"Suits {string.Join(", ", five.Take(5).Select(p => p.LastName))}.", fs, fd));
        list.Add(Make("threes", "Three-point barrage", "Push the pace and let it fly: spacing, corner threes and drives.", OffensiveSystem.PaceAndSpace, CoachAi.BestScheme(rot), pace: 104, three: 82, glass: 35));
        list.Add(Make("inside", "Inside power", "Pound it inside, crash the glass and protect the rim.", OffensiveSystem.PostCentric, DefensiveScheme.Drop, pace: 94, three: 28, glass: 75));
        var lockScheme = Enum.GetValues<DefensiveScheme>().Where(x => x != DefensiveScheme.Man).OrderByDescending(x => Systems.Fit(x, rot)).First();
        list.Add(Make("lock", "Defensive lockdown", $"Slow it down and defend: {Systems.Name(lockScheme).ToLower()}, get back in transition.", bestS, lockScheme, pace: 93, glass: 30, aggr: 68));
        var go = rot.OrderByDescending(p => p.Attributes[Attr.ShotCreation] + p.Attributes[Attr.MidRange]).FirstOrDefault();
        list.Add(Make("star", go != null ? $"Ride {go.LastName}" : "Ride the star", "Clear out for your best scorer and feed him.", OffensiveSystem.Isolation, bestD, pace: 96, star: 82));
        list.Add(Make("move", "Ball movement", "Cut, screen and share it: more assists and open looks.", OffensiveSystem.Motion, bestD, star: 30));

        // On a tie (the best fit is already the counter) the game-plan card carries the pick, so the badge sits on the plan.
        var best = list.OrderByDescending(x => x.Value).ThenByDescending(x => x.Key == "plan").First();
        return list.Select(x => x == best ? x with { Recommended = true } : x).ToList();
    }
}
