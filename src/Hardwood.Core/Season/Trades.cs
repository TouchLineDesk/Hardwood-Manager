using Hardwood.Core.Model;

namespace Hardwood.Core.Season;

public enum TeamDirection { Contending, Competitive, Rebuilding }

public sealed class TradeProposal
{
    public required Team From { get; init; }
    public required Team To { get; init; }
    public List<Player> FromPlayers { get; } = new();
    public List<Player> ToPlayers { get; } = new();
}

public sealed record TradeEvaluation(
    bool Legal, string LegalityNote,
    double ValueGivenByTo, double ValueReceivedByTo, double GainForTo,
    bool Accepted, string Verdict);

/// <summary>
/// Player-for-player trades under a hard cap. The AI values players by ability, upside and age, weighted by its
/// team direction: contenders pay for the present, rebuilders pay for youth. It accepts when it gains ~5% or more.
/// </summary>
public sealed class TradeEngine
{
    private readonly League _league;
    private readonly DateOnly _date;
    private readonly Standings? _standings;

    public double AcceptanceMargin { get; set; } = 0.05;

    public TradeEngine(League league, DateOnly date, Standings? standings = null)
    {
        _league = league; _date = date; _standings = standings;
    }

    /// <summary>Contending, competitive or rebuilding, from record (if a season is under way) and roster age/quality.</summary>
    public TeamDirection Direction(Team team)
    {
        double core = team.Roster.OrderByDescending(p => p.CurrentAbility).Take(7).Average(p => p.CurrentAbility);
        double coreAge = team.Roster.OrderByDescending(p => p.CurrentAbility).Take(7).Average(p => p.Age(_date));
        double winPct = _standings != null && _standings[team].Games >= 15 ? _standings[team].WinPct : double.NaN;

        if (!double.IsNaN(winPct))
        {
            if (winPct >= 0.58) return TeamDirection.Contending;
            if (winPct <= 0.38) return TeamDirection.Rebuilding;
            return TeamDirection.Competitive;
        }
        var leagueCore = _league.Teams.Average(t => t.Roster.OrderByDescending(p => p.CurrentAbility).Take(7).Average(p => p.CurrentAbility));
        if (core >= leagueCore + 6) return TeamDirection.Contending;
        if (core <= leagueCore - 6 || coreAge >= 30) return TeamDirection.Rebuilding;
        return TeamDirection.Competitive;
    }

    /// <summary>What a player is worth to a particular team.</summary>
    public double ValueTo(Team team, Player p)
    {
        int age = p.Age(_date);
        double now = p.CurrentAbility;
        double upside = Math.Max(0, p.PotentialAbility - p.CurrentAbility);
        var dir = Direction(team);

        double v = dir switch
        {
            TeamDirection.Contending => now * 1.0 + upside * 0.10 - Math.Max(0, age - 31) * 2.0,
            TeamDirection.Rebuilding => now * 0.75 + upside * 0.55 - Math.Max(0, age - 26) * 3.0,
            _ => now * 0.9 + upside * 0.3 - Math.Max(0, age - 29) * 2.5,
        };
        // Contract: cheap production is worth more, overpays less; years matter to a rebuilder.
        long salary = p.Contract?.Salary ?? ContractMarket.MarketSalary(p.CurrentAbility, age, _league.Finance);
        long market = ContractMarket.MarketSalary(p.CurrentAbility, age, _league.Finance);
        v += (market - salary) / 1_000_000.0 * 0.8;
        if (p.Injury != null && p.Injury.IsOut(_date)) v -= Math.Min(25, p.Injury.DaysOut(_date) / 6.0);
        // Value is convex: one star beats two role players.
        return Math.Max(0, Math.Pow(Math.Max(v, 1), 1.35) / 10.0);
    }

    public (bool legal, string note) CheckLegality(TradeProposal t)
    {
        var fin = _league.Finance;
        if (t.FromPlayers.Count == 0 && t.ToPlayers.Count == 0) return (false, "Nothing is being traded.");
        if (t.FromPlayers.Any(p => !t.From.Roster.Contains(p)) || t.ToPlayers.Any(p => !t.To.Roster.Contains(p)))
            return (false, "A player in the deal is not on that roster.");

        foreach (var (team, outgoing, incoming) in new[] { (t.From, t.FromPlayers, t.ToPlayers), (t.To, t.ToPlayers, t.FromPlayers) })
        {
            int size = team.Roster.Count - outgoing.Count + incoming.Count;
            if (size > fin.RosterMax) return (false, $"{team.Abbreviation} would have {size} players (max {fin.RosterMax}).");
            if (size < fin.RosterMin - 2) return (false, $"{team.Abbreviation} would have {size} players (too few).");
            long payroll = ContractMarket.Payroll(team) - outgoing.Sum(p => p.Contract?.Salary ?? 0) + incoming.Sum(p => p.Contract?.Salary ?? 0);
            if (payroll > fin.HardCap) return (false, $"{team.Abbreviation} would be ${(payroll - fin.HardCap) / 1e6:F1}M over the hard cap.");
        }
        return (true, "Legal under the hard cap and roster limits.");
    }

    /// <summary>Evaluate from the AI side (the "To" team receives FromPlayers and gives ToPlayers).</summary>
    public TradeEvaluation Evaluate(TradeProposal t)
    {
        var (legal, note) = CheckLegality(t);
        double given = t.ToPlayers.Sum(p => ValueTo(t.To, p));
        double received = t.FromPlayers.Sum(p => ValueTo(t.To, p));
        // Roster-spot friction: taking back more bodies than you send costs a little.
        received -= Math.Max(0, t.FromPlayers.Count - t.ToPlayers.Count) * 1.5;
        double gain = given <= 0 ? received : (received - given) / given;
        bool accepted = legal && received > 0 && gain >= AcceptanceMargin;
        string verdict = !legal ? note
            : accepted ? (gain > 0.25 ? "They jump at it." : "They accept.")
            : gain >= 0 ? "Close, but they want a little more."
            : gain >= -0.15 ? "They decline; the value isn't there yet."
            : "They laugh you off the phone.";
        return new TradeEvaluation(legal, note, given, received, gain, accepted, verdict);
    }

    /// <summary>Execute a trade (caller should have checked acceptance).</summary>
    public void Execute(TradeProposal t)
    {
        var (legal, note) = CheckLegality(t);
        if (!legal) throw new InvalidOperationException(note);
        foreach (var p in t.FromPlayers) { t.From.Roster.Remove(p); t.To.Roster.Add(p); p.TeamId = t.To.Id; }
        foreach (var p in t.ToPlayers) { t.To.Roster.Remove(p); t.From.Roster.Add(p); p.TeamId = t.From.Id; }
        t.From.DepthChart = null;
        t.To.DepthChart = null;
    }

    /// <summary>Suggest what the other team would want added from the human roster to accept, if anything simple works.</summary>
    public Player? SuggestSweetener(TradeProposal t)
    {
        var eval = Evaluate(t);
        if (eval.Accepted || !eval.Legal) return null;
        double need = eval.ValueGivenByTo * (1 + AcceptanceMargin) - eval.ValueReceivedByTo + 1.5;
        return t.From.Roster.Where(p => !t.FromPlayers.Contains(p))
            .Select(p => (p, v: ValueTo(t.To, p)))
            .Where(x => x.v >= need)
            .OrderBy(x => x.v)
            .Select(x => x.p)
            .FirstOrDefault();
    }

    /// <summary>AI-to-AI trades: a handful per season to keep the league alive.</summary>
    public List<TradeProposal> AiTradeRound(Rng rng, int? humanTeamId, int attempts = 40)
    {
        var done = new List<TradeProposal>();
        var teams = _league.Teams.Where(t => t.Id != humanTeamId).ToList();
        for (int i = 0; i < attempts; i++)
        {
            var a = rng.Pick(teams); var b = rng.Pick(teams);
            if (a == b) continue;
            var da = Direction(a); var db = Direction(b);
            if (da == db) continue; // trades happen across directions: vets for youth

            var contender = da == TeamDirection.Contending || (da == TeamDirection.Competitive && db == TeamDirection.Rebuilding) ? a : b;
            var rebuilder = contender == a ? b : a;

            // Rebuilder sells a prime-age or older productive player; contender sends back youth.
            var sell = rebuilder.Roster.Where(p => p.Age(_date) >= 27).OrderByDescending(p => p.CurrentAbility).Skip(rng.Next(0, 2)).FirstOrDefault();
            if (sell == null) continue;
            var young = contender.Roster.Where(p => p.Age(_date) <= 24).OrderByDescending(p => ValueTo(rebuilder, p)).ToList();
            if (young.Count == 0) continue;

            var proposal = new TradeProposal { From = contender, To = rebuilder };
            proposal.ToPlayers.Add(sell);
            proposal.FromPlayers.Add(young[0]);
            // Add salary filler / second piece until the rebuilder accepts or it stops making sense for the contender.
            for (int k = 1; k < Math.Min(3, young.Count) && !Evaluate(proposal).Accepted; k++) proposal.FromPlayers.Add(young[k]);
            var eval = Evaluate(proposal);
            if (!eval.Accepted) continue;

            // Contender must also come out ahead by its own lights.
            double contenderGain = proposal.ToPlayers.Sum(p => ValueTo(contender, p)) - proposal.FromPlayers.Sum(p => ValueTo(contender, p));
            if (contenderGain <= 0) continue;

            Execute(proposal);
            done.Add(proposal);
            if (done.Count >= 4) break;
        }
        return done;
    }
}
