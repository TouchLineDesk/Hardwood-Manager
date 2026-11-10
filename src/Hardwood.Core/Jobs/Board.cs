using Hardwood.Core.Model;
using Hardwood.Core.Season;

namespace Hardwood.Core.Jobs;

/// <summary>A club owner (decision #71): patience (how long he waits) and ambition (how much he wants, and spends).</summary>
public sealed class Owner
{
    public string Name { get; set; } = "";
    public int Patience { get; set; } = 10;
    public int Ambition { get; set; } = 10;

    public string Style => (Patience, Ambition) switch
    {
        ( <= 6, >= 14) => "Ruthless and demanding",
        ( <= 6, _) => "Impatient",
        ( >= 15, <= 8) => "Patient and careful with money",
        ( >= 15, _) => "Patient",
        (_, >= 15) => "Wants to win now",
        (_, <= 6) => "Careful with money",
        _ => "Reasonable",
    };
}

/// <summary>How far a team got: also the scale for season goals.</summary>
public enum SeasonGoal { Develop, PlayIn, Playoffs, SecondRound, ConferenceFinals, Finals, Title }
public enum Demand { StayUnderTax, PlayYoungsters, MakePlayoffs }

public sealed class Expectations
{
    public int Season { get; set; }
    public SeasonGoal Goal { get; set; }
    public int TargetWins { get; set; }
    public List<Demand> Demands { get; set; } = new();
}

/// <summary>A job you could apply for while out of work (decision #74).</summary>
public sealed class Vacancy
{
    public int TeamId { get; set; }
    public DateOnly Opened { get; set; }
    /// <summary>open · applied · offered · rejected · filled</summary>
    public string Status { get; set; } = "open";
    public DateOnly? DecideOn { get; set; }
}

/// <summary>Your job (decisions #71-74): employed or not, the board's confidence, this season's expectations, open jobs.</summary>
public sealed class JobState
{
    public bool Employed { get; set; } = true;
    public double Confidence { get; set; } = 65;
    public Expectations? Current { get; set; }
    public DateOnly HiredOn { get; set; }
    public int HiredSeason { get; set; }
    public List<Vacancy> Vacancies { get; set; } = new();
    public List<ConfidencePoint> History { get; set; } = new();
    /// <summary>The last warning level the board gave (0 none, 1 concerned, 2 under threat), so warnings aren't repeated.</summary>
    public int WarningLevel { get; set; }
}

public sealed record ConfidencePoint(DateOnly Date, double Confidence);

public static class Board
{
    private static readonly string[] OwnerFirst = { "Richard", "Victoria", "Gerald", "Margaret", "Howard", "Diane", "Leonard", "Catherine", "Stanley", "Patricia", "Walter", "Eleanor", "Franklin", "Judith", "Raymond", "Sandra" };
    private static readonly string[] OwnerLast = { "Whitmore", "Kessler", "Abernathy", "Lindqvist", "Holloway", "Pemberton", "Castellano", "Ashford", "Delgado", "Thornbury", "Okafor-Hale", "Vance", "Merriweather", "Calloway", "Rutherford", "Benedetti" };
    private static readonly string[] CoachFirst = { "Mike", "Steve", "Doc", "Erik", "Rick", "Monty", "Quin", "Nate", "Tyronn", "Dwane", "Jerry", "Larry", "Gregg", "Phil", "Pat", "Chuck" };
    private static readonly string[] CoachLast = { "Brennan", "Hollis", "Mercer", "Draper", "Fontaine", "Waverly", "Kingsley", "Barlow", "Prentice", "Ashby", "Coleridge", "Danvers", "Ellery", "Garland", "Hartley", "Langston" };

    /// <summary>Give every team an owner and a computer coach if it doesn't have one (new careers and older saves).</summary>
    public static void EnsureOwnersAndCoaches(League league, ulong seed)
    {
        foreach (var t in league.Teams)
        {
            var rng = new Rng(seed ^ (ulong)(t.Id * 7919 + 17));
            if (string.IsNullOrEmpty(t.Owner.Name))
                t.Owner = new Owner
                {
                    Name = $"{rng.Pick(OwnerFirst)} {rng.Pick(OwnerLast)}",
                    Patience = Math.Clamp((int)Math.Round(rng.NextGaussian(10.5, 4)), 1, 20),
                    Ambition = Math.Clamp((int)Math.Round(rng.NextGaussian(11, 4)), 1, 20),
                };
            if (string.IsNullOrEmpty(t.CoachName)) t.CoachName = NewCoachName(rng);
        }
    }

    public static string NewCoachName(Rng rng) => $"{rng.Pick(CoachFirst)} {rng.Pick(CoachLast)}";

    /// <summary>Where the squad and payroll put a team in the league, 1 = best (squad 70%, spending 30%).</summary>
    public static int ProjectedRank(League league, Team team)
    {
        double Score(Team t) =>
            t.Roster.OrderByDescending(p => p.CurrentAbility).Take(9).Select(p => (double)p.CurrentAbility).DefaultIfEmpty(0).Average() * 0.7
            + ContractMarket.Payroll(t) / 1e6 * 0.3 * 0.25;
        return league.Teams.OrderByDescending(Score).ToList().IndexOf(team) + 1;
    }

    public static string Describe(SeasonGoal g) => g switch
    {
        SeasonGoal.Title => "Win the championship",
        SeasonGoal.Finals => "Reach the Finals",
        SeasonGoal.ConferenceFinals => "Reach the conference finals",
        SeasonGoal.SecondRound => "Win a playoff round",
        SeasonGoal.Playoffs => "Make the playoffs",
        SeasonGoal.PlayIn => "Reach the play-in",
        _ => "Develop the squad and show progress",
    };

    public static string Describe(Demand d) => d switch
    {
        Demand.StayUnderTax => "Keep the payroll under the luxury tax line",
        Demand.PlayYoungsters => "Give the young players real minutes",
        _ => "Make the playoffs, whatever it takes",
    };

    /// <summary>
    /// Season expectations (decision #72, "both"): a goal from the squad and the payroll, plus the owner's own demands.
    /// </summary>
    public static Expectations SetExpectations(League league, Team team, int season)
    {
        int r = ProjectedRank(league, team);
        var goal = r <= 2 ? SeasonGoal.Title : r <= 5 ? SeasonGoal.ConferenceFinals : r <= 10 ? SeasonGoal.SecondRound
                 : r <= 16 ? SeasonGoal.Playoffs : r <= 20 ? SeasonGoal.PlayIn : SeasonGoal.Develop;
        var o = team.Owner;
        var e = new Expectations { Season = season, Goal = goal, TargetWins = Math.Clamp((int)Math.Round(60 - (r - 1) * 1.25), 22, 62) };
        if (o.Ambition <= 7 && league.Finance.SoftCap) e.Demands.Add(Demand.StayUnderTax);
        if (o.Ambition >= 15 && goal < SeasonGoal.Playoffs) { e.Demands.Add(Demand.MakePlayoffs); e.Goal = SeasonGoal.Playoffs; e.TargetWins = Math.Max(e.TargetWins, 43); }
        var start = new DateOnly(season, 10, 1);
        if (o.Patience >= 12 && team.Roster.Count(p => p.Age(start) <= 23 && p.PotentialAbility >= 120) >= 2) e.Demands.Add(Demand.PlayYoungsters);
        return e;
    }

    /// <summary>How the board feels about each demand right now (true = being met).</summary>
    public static bool Meeting(Demand d, League league, Team team, IReadOnlyDictionary<int, PlayerSeasonStats> stats, DateOnly today, int wins, int games) => d switch
    {
        Demand.StayUnderTax => ContractMarket.Payroll(team) <= league.Finance.TaxLine,
        Demand.PlayYoungsters => team.Roster.Where(p => p.Age(today) <= 23).Sum(p => stats.TryGetValue(p.Id, out var s) && s.TeamId == team.Id ? s.Mpg : 0) >= 25,
        _ => games == 0 || wins / (double)games >= 0.5,
    };

    /// <summary>
    /// Weekly: confidence follows your pace against the target, faster with impatient owners; unmet demands chip away.
    /// Returns a message for the inbox when the board's mood crosses a line, and whether you're sacked mid-season.
    /// </summary>
    public static (string? title, string? body, bool sacked) Weekly(JobState job, League league, Team team, int wins, int losses,
        IReadOnlyDictionary<int, PlayerSeasonStats> stats, DateOnly today)
    {
        var e = job.Current;
        int games = wins + losses;
        if (e == null || games < 5) return (null, null, false);
        double impatience = 1.3 - team.Owner.Patience / 40.0;
        double projected = wins / (double)games * 82;
        // Confidence drifts toward a level set by the pace: on target ≈ 62, 10 wins short ≈ 42, 20 short ≈ 22.
        // Early in the season the record says little, so the board moves slowly; impatient owners move faster.
        double paceLevel = 62 + (projected - e.TargetWins) * 2;
        foreach (var d in e.Demands) if (!Meeting(d, league, team, stats, today, wins, games)) paceLevel -= 5;
        double rate = Math.Clamp(games / 82.0, 0.1, 1) * 0.18 * impatience;
        job.Confidence = Math.Clamp(job.Confidence + (Math.Clamp(paceLevel, 0, 100) - job.Confidence) * rate, 0, 100);
        job.History.Add(new ConfidencePoint(today, Math.Round(job.Confidence, 1)));
        if (job.History.Count > 200) job.History.RemoveAt(0);

        bool honeymoon = today.DayNumber - job.HiredOn.DayNumber < 60;
        // A new coach is only sacked mid-way through his first season if it's a catastrophe.
        double line = job.HiredSeason == league.Season && job.HiredSeason != 0 ? 3 : 12 + (10 - team.Owner.Patience) * 0.8;
        if (!honeymoon && games >= 30 && job.Confidence < line)
            return ("You have been sacked", $"{team.Owner.Name} has run out of patience. With the team {wins}-{losses} against a target of {e.TargetWins} wins, you have been relieved of your duties.", true);
        int level = job.Confidence < 20 ? 2 : job.Confidence < 35 ? 1 : 0;
        if (level > job.WarningLevel)
        {
            job.WarningLevel = level;
            return level == 2
                ? ("Your job is under threat", $"{team.Owner.Name} wants to see results immediately. Board confidence: {job.Confidence:0}/100.", false)
                : ("The owner is concerned", $"{team.Owner.Name} is unhappy with how the season is going ({wins}-{losses}). Board confidence: {job.Confidence:0}/100.", false);
        }
        if (level < job.WarningLevel) job.WarningLevel = level;
        return (null, null, false);
    }

    /// <summary>How far a team got in the postseason.</summary>
    public static SeasonGoal Achieved(Team team, Postseason? post)
    {
        if (post == null) return SeasonGoal.Develop;
        if (post.Champion == team) return SeasonGoal.Title;
        var last = post.Series.LastOrDefault(s => s.Higher == team || s.Lower == team);
        if (last == null) return SeasonGoal.Develop;
        return last.Round switch
        {
            "Finals" => SeasonGoal.Finals,
            "Conference Finals" => SeasonGoal.ConferenceFinals,
            "Semifinals" => SeasonGoal.SecondRound,
            "First Round" => SeasonGoal.Playoffs,
            _ => post.Series.Any(s => s.Round == "First Round" && (s.Higher == team || s.Lower == team)) ? SeasonGoal.Playoffs : SeasonGoal.PlayIn,
        };
    }

    /// <summary>
    /// End of season: the board judges the result against the goal and the demands, then decides your future.
    /// Also moves your reputation.
    /// </summary>
    public static (string title, string body, bool sacked) SeasonReview(JobState job, Coach coach, League league, Team team, SeasonGoal achieved,
        int wins, int losses, IReadOnlyDictionary<int, PlayerSeasonStats> stats, DateOnly today)
    {
        var e = job.Current ?? SetExpectations(league, team, league.Season);
        double impatience = 1.3 - team.Owner.Patience / 40.0;
        int diff = (int)achieved - (int)e.Goal;
        double delta = diff >= 0 ? 10 + 5 * diff : -12 * -diff * impatience;
        var lines = new List<string> { $"Goal: {Describe(e.Goal).ToLower()}. Result: {Describe(achieved).ToLower()} ({wins}-{losses})." };
        foreach (var d in e.Demands)
        {
            bool met = Meeting(d, league, team, stats, today, wins, wins + losses);
            delta += met ? 4 : -8 * impatience;
            lines.Add($"{Describe(d)}: {(met ? "done" : "not done")}.");
        }
        job.Confidence = Math.Clamp(job.Confidence + delta, 0, 100);
        coach.Reputation = Math.Clamp(coach.Reputation + (wins / Math.Max(1.0, wins + losses) - 0.5) * 10
            + achieved switch { SeasonGoal.Title => 10, SeasonGoal.Finals => 6, SeasonGoal.ConferenceFinals => 4, SeasonGoal.SecondRound => 2, SeasonGoal.Playoffs => 1, _ => 0 }, 0, 100);
        // A new coach gets his first season: a bad one earns a final warning, not the sack.
        bool firstSeason = job.HiredSeason == league.Season;
        bool sacked = !firstSeason && job.Confidence < 25 - (team.Owner.Patience - 10);
        if (firstSeason && job.Confidence < 25 - (team.Owner.Patience - 10)) job.Confidence = Math.Max(job.Confidence, 20);
        string verdict = sacked ? $"{team.Owner.Name} has decided to make a change. You have been sacked."
            : firstSeason && diff < 0 && job.Confidence <= 25 ? $"{team.Owner.Name} is giving you another season, but expects much better. Consider this a final warning."
            : diff >= 1 ? $"{team.Owner.Name} is delighted." : diff == 0 ? $"{team.Owner.Name} is satisfied." : $"{team.Owner.Name} expected more. Board confidence: {job.Confidence:0}/100.";
        lines.Add(verdict);
        return (sacked ? "You have been sacked" : "Season review", string.Join("\n", lines), sacked);
    }

    /// <summary>A club's standing when hiring: the better the squad and the bigger the spending, the higher the bar.</summary>
    public static double Prestige(League league, Team team) => 25 + (30 - ProjectedRank(league, team)) / 29.0 * 45;

    /// <summary>Chance a club hires you: your reputation against its prestige.</summary>
    public static double HireChance(Coach coach, League league, Team team) => 1 / (1 + Math.Exp(-(coach.Reputation - Prestige(league, team) + 8) / 7));

    /// <summary>
    /// The coaching carousel for computer-run teams at the end of a season: owners who expected more may fire their
    /// coach (impatient owners more readily). Returns the teams that changed coach.
    /// </summary>
    public static List<Team> AiSeasonChanges(League league, Standings standings, Rng rng, int? humanTeamId)
    {
        var fired = new List<Team>();
        foreach (var t in league.Teams.Where(t => t.Id != humanTeamId))
        {
            var e = SetExpectations(league, t, league.Season);
            int wins = standings[t].Wins;
            double shortfall = Math.Max(0, e.TargetWins - wins);
            double p = Math.Clamp(0.04 + shortfall * 0.025 * (1.3 - t.Owner.Patience / 40.0), 0, 0.7);
            if (rng.Chance(p)) fired.Add(t);
        }
        return fired;
    }
}
