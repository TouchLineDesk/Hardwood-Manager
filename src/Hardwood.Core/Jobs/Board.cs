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
    /// <summary>The club and league the goal was set for (to tell a promotion from a normal summer).</summary>
    public int TeamId { get; set; }
    public string League { get; set; } = "";
    /// <summary>Newly promoted: the aim is to stay up.</summary>
    public bool Survive { get; set; }
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
    public static void EnsureOwnersAndCoaches(League league, ulong seed) => EnsureOwnersAndCoaches(league.Teams, seed);

    public static void EnsureOwnersAndCoaches(IEnumerable<Team> teams, ulong seed)
    {
        foreach (var t in teams)
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
    public static int ProjectedRank(League league, Team team) => ProjectedRank(LeagueContext.Nabl(league), team);

    public static int ProjectedRank(LeagueContext ctx, Team team)
    {
        double Score(Team t) =>
            t.Roster.OrderByDescending(p => p.CurrentAbility).Take(9).Select(p => (double)p.CurrentAbility).DefaultIfEmpty(0).Average() * 0.7
            + ContractMarket.Payroll(t) / 1e6 * 0.3 * 0.25 * (ctx.IsNabl ? 1 : 8); // overseas wages are ~1/8 of the NABL's
        return ctx.Teams.OrderByDescending(Score).ThenBy(t => t.Id).ToList().IndexOf(team) + 1;
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

    /// <summary>Goal wording for a league: leagues without playoffs are judged on the final table.</summary>
    public static string Describe(SeasonGoal g, LeagueContext ctx) => ctx.IsNabl || ctx.PlayoffTeams > 1 ? Describe(g) : g switch
    {
        SeasonGoal.Title => "Win the league",
        SeasonGoal.Finals => "Finish in the top two",
        SeasonGoal.ConferenceFinals or SeasonGoal.SecondRound => "Finish in the top three",
        SeasonGoal.Playoffs or SeasonGoal.PlayIn => "Finish in the top half",
        _ => "Stay clear of the bottom and show progress",
    };

    public static string Describe(Demand d) => d switch
    {
        Demand.StayUnderTax => "Keep wages within the budget",
        Demand.PlayYoungsters => "Give the young players real minutes",
        _ => "Make the playoffs, whatever it takes",
    };

    /// <summary>
    /// Season expectations (decision #72, "both"): a goal from the squad and the payroll, plus the owner's own demands.
    /// </summary>
    public static Expectations SetExpectations(League league, Team team, int season) => SetExpectations(LeagueContext.Nabl(league), team, season);

    /// <summary>
    /// Next season's goal, reasonably (critic review, Nov 10): a newly promoted club aims to stay up; otherwise the
    /// goal from the squad moves at most one step from last season's, and the win target at most 8 (of 82) up.
    /// A 3rd-place finish no longer turns "reach the final" into "win the title" overnight.
    /// </summary>
    public static Expectations NextExpectations(LeagueContext ctx, Team team, int season, Expectations? previous, string leagueKey, int level, int previousLevel)
    {
        var e = SetExpectations(ctx, team, season);
        e.TeamId = team.Id; e.League = leagueKey;
        if (previous == null || previous.TeamId != team.Id) return e;
        bool promoted = previous.League != leagueKey && level > 0 && previousLevel > 0 && level < previousLevel;
        if (promoted)
        {
            e.Survive = true;
            e.Goal = SeasonGoal.Develop;
            e.TargetWins = (int)Math.Round(ctx.SeasonGames * 0.4);
            e.Demands.Remove(Demand.MakePlayoffs);
            return e;
        }
        if (previous.League != leagueKey) return e;
        var steps = ctx.IsNabl ? Enum.GetValues<SeasonGoal>().ToList()
            : new List<SeasonGoal> { SeasonGoal.Develop, SeasonGoal.Playoffs, SeasonGoal.SecondRound, SeasonGoal.Finals, SeasonGoal.Title };
        int prevIdx = steps.IndexOf(previous.Survive ? SeasonGoal.Develop : previous.Goal), idx = steps.IndexOf(e.Goal);
        if (prevIdx >= 0 && idx > prevIdx + 1) e.Goal = steps[prevIdx + 1];
        int maxRise = (int)Math.Round(8 * ctx.SeasonGames / 82.0);
        if (previous.TargetWins > 0 && e.TargetWins > previous.TargetWins + maxRise) e.TargetWins = previous.TargetWins + maxRise;
        return e;
    }

    public static Expectations SetExpectations(LeagueContext ctx, Team team, int season)
    {
        int r = ProjectedRank(ctx, team);
        SeasonGoal goal;
        int target;
        if (ctx.IsNabl)
        {
            goal = r <= 2 ? SeasonGoal.Title : r <= 5 ? SeasonGoal.ConferenceFinals : r <= 10 ? SeasonGoal.SecondRound
                 : r <= 16 ? SeasonGoal.Playoffs : r <= 20 ? SeasonGoal.PlayIn : SeasonGoal.Develop;
            target = Math.Clamp((int)Math.Round(60 - (r - 1) * 1.25), 22, 62);
        }
        else
        {
            // Overseas leagues have one table and a short bracket: favourites chase the title, the top half the playoffs.
            // Without playoffs (the USA pyramid) the goal is a table position: top half instead of "make the playoffs".
            int topHalf = ctx.PlayoffTeams > 1 ? ctx.PlayoffTeams : Math.Max(3, ctx.Teams.Count / 2);
            goal = r == 1 ? SeasonGoal.Title : r <= 2 ? SeasonGoal.Finals : r <= Math.Max(3, ctx.PlayoffTeams / 2) ? SeasonGoal.SecondRound
                 : r <= topHalf ? SeasonGoal.Playoffs : SeasonGoal.Develop;
            target = (int)Math.Round(ctx.SeasonGames * (0.73 - ctx.Fraction(r) * 0.46));
        }
        var o = team.Owner;
        var e = new Expectations { Season = season, Goal = goal, TargetWins = target };
        // Careful owners want wages kept within the budget (the luxury tax, before the open market).
        if (o.Ambition <= 7 && (ctx.Finance is { SoftCap: true } || ctx.Finance is null or { OpenMarket: true })) e.Demands.Add(Demand.StayUnderTax);
        if (o.Ambition >= 15 && goal < SeasonGoal.Playoffs)
        {
            e.Demands.Add(Demand.MakePlayoffs); e.Goal = SeasonGoal.Playoffs;
            e.TargetWins = Math.Max(e.TargetWins, (int)Math.Round(ctx.SeasonGames * (ctx.IsNabl ? 43 / 82.0 : 0.52)));
        }
        var start = new DateOnly(season, 10, 1);
        if (o.Patience >= 12 && team.Roster.Count(p => p.Age(start) <= 23 && p.PotentialAbility >= 120) >= 2) e.Demands.Add(Demand.PlayYoungsters);
        return e;
    }

    /// <summary>How the board feels about each demand right now (true = being met).</summary>
    public static bool Meeting(Demand d, League league, Team team, IReadOnlyDictionary<int, PlayerSeasonStats> stats, DateOnly today, int wins, int games) =>
        Meeting(d, LeagueContext.Nabl(league), team, stats, today, wins, games);

    public static bool Meeting(Demand d, LeagueContext ctx, Team team, IReadOnlyDictionary<int, PlayerSeasonStats> stats, DateOnly today, int wins, int games) => d switch
    {
        Demand.StayUnderTax => ctx.Finance is { OpenMarket: false } f ? ContractMarket.Payroll(team) <= f.TaxLine
                               : !team.Money.Ready || ContractMarket.Payroll(team) <= team.Money.WageBudget * 1.01, // a rounding overage isn't defiance
        Demand.PlayYoungsters => team.Roster.Where(p => p.Age(today) <= 23).Sum(p => stats.TryGetValue(p.Id, out var s) && s.TeamId == team.Id ? s.Mpg : 0) >= 25,
        _ => games == 0 || wins / (double)games >= 0.5,
    };

    /// <summary>
    /// Weekly: confidence follows your pace against the target, faster with impatient owners; unmet demands chip away.
    /// Returns a message for the inbox when the board's mood crosses a line, and whether you're sacked mid-season.
    /// </summary>
    /// <summary>Win share at or above which the board won't sack you (it can still be disappointed).</summary>
    public const double WinningRecord = 0.58;

    public static (string? title, string? body, bool sacked) Weekly(JobState job, League league, Team team, int wins, int losses,
        IReadOnlyDictionary<int, PlayerSeasonStats> stats, DateOnly today) =>
        Weekly(job, LeagueContext.Nabl(league), team, wins, losses, stats, today, league.Season);

    public static (string? title, string? body, bool sacked) Weekly(JobState job, LeagueContext ctx, Team team, int wins, int losses,
        IReadOnlyDictionary<int, PlayerSeasonStats> stats, DateOnly today, int season)
    {
        var e = job.Current;
        int games = wins + losses;
        if (e == null || games < 5) return (null, null, false);
        double impatience = 1.3 - team.Owner.Patience / 40.0;
        double full = Math.Max(1, ctx.SeasonGames);
        double projected = wins / (double)games * full;
        // Confidence drifts toward a level set by the pace: on target ≈ 62, 10 wins short (of 82) ≈ 42, 20 short ≈ 22.
        // Early in the season the record says little, so the board moves slowly; impatient owners move faster.
        double paceLevel = 62 + (projected - e.TargetWins) * 2 * (82 / full);
        foreach (var d in e.Demands) if (!Meeting(d, ctx, team, stats, today, wins, games)) paceLevel -= 5;
        double rate = Math.Clamp(games / full, 0.1, 1) * 0.18 * impatience;
        job.Confidence = Math.Clamp(job.Confidence + (Math.Clamp(paceLevel, 0, 100) - job.Confidence) * rate, 0, 100);
        job.History.Add(new ConfidencePoint(today, Math.Round(job.Confidence, 1)));
        if (job.History.Count > 200) job.History.RemoveAt(0);

        bool honeymoon = today.DayNumber - job.HiredOn.DayNumber < 60;
        // A new coach is only sacked mid-way through his first season if it's a catastrophe.
        double line = job.HiredSeason == season && job.HiredSeason != 0 ? 3 : 12 + (10 - team.Owner.Patience) * 0.8;
        // A winning team isn't sacked mid-season, whatever the target (critic review #3).
        bool winning = wins / (double)games >= WinningRecord;
        if (!honeymoon && !winning && games >= full * 30 / 82 && job.Confidence < line)
            return ("You have been sacked", $"{team.Owner.Name} has run out of patience. With the team {wins}-{losses} against a target of {e.TargetWins} wins, you have been relieved of your duties.", true);
        int level = job.Confidence < 20 ? 2 : job.Confidence < 35 ? 1 : 0;
        if (level > job.WarningLevel)
        {
            job.WarningLevel = level;
            return level == 2
                ? ("Your job is under threat", $"{team.Owner.Name} wants to see results immediately. Board confidence: {job.Confidence:0}/100.", false)
                : ("The owner is concerned", $"{team.Owner.Name} is unhappy with how the season is going ({wins}-{losses}). Board confidence: {job.Confidence:0}/100.", false);
        }
        // Warnings ease only once confidence has clearly recovered, so a board hovering at a line doesn't warn every week
        // (critic review #3: 126-135 board messages in three seasons).
        int eased = job.Confidence < 28 ? 2 : job.Confidence < 43 ? 1 : 0;
        if (eased < job.WarningLevel) job.WarningLevel = eased;
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

    /// <summary>How far a club got in an overseas league's playoffs (title, final, won a round, made them, or not).</summary>
    public static SeasonGoal Achieved(int teamId, World.CompetitionSeason season, World.CompetitionProfile profile)
    {
        if (profile.Playoffs.Length == 0)
        {
            // Judged on the final table.
            var table = season.Table();
            int rank = table.FindIndex(r => r.TeamId == teamId) + 1;
            if (rank <= 0) return SeasonGoal.Develop;
            return rank == 1 ? SeasonGoal.Title : rank == 2 ? SeasonGoal.Finals : rank <= 3 ? SeasonGoal.SecondRound
                 : rank <= Math.Max(3, table.Count / 2) ? SeasonGoal.Playoffs : SeasonGoal.Develop;
        }
        if (season.ChampionId == teamId) return SeasonGoal.Title;
        if (season.RunnerUpId == teamId) return SeasonGoal.Finals;
        var mine = season.Series.Where(x => x.HighId == teamId || x.LowId == teamId).ToList();
        if (mine.Count == 0) return SeasonGoal.Develop;
        return mine.Max(x => x.RoundIndex) >= 1 ? SeasonGoal.SecondRound : SeasonGoal.Playoffs;
    }

    /// <summary>
    /// End of season: the board judges the result against the goal and the demands, then decides your future.
    /// Also moves your reputation.
    /// </summary>
    public static (string title, string body, bool sacked) SeasonReview(JobState job, Coach coach, League league, Team team, SeasonGoal achieved,
        int wins, int losses, IReadOnlyDictionary<int, PlayerSeasonStats> stats, DateOnly today) =>
        SeasonReview(job, coach, LeagueContext.Nabl(league), team, achieved, wins, losses, stats, today, league.Season);

    public static (string title, string body, bool sacked) SeasonReview(JobState job, Coach coach, LeagueContext ctx, Team team, SeasonGoal achieved,
        int wins, int losses, IReadOnlyDictionary<int, PlayerSeasonStats> stats, DateOnly today, int season)
    {
        var e = job.Current ?? SetExpectations(ctx, team, season);
        double impatience = 1.3 - team.Owner.Patience / 40.0;
        int diff = (int)achieved - (int)e.Goal;
        double delta = diff >= 0 ? 10 + 5 * diff : -12 * -diff * impatience;
        // The regular season counts too (critic review: a 70-12 team that lost in the playoffs ended at confidence 20).
        double winsVsTarget = (wins - e.TargetWins * (wins + losses) / Math.Max(1.0, ctx.SeasonGames)) * 82 / Math.Max(1.0, ctx.SeasonGames);
        delta += Math.Clamp(winsVsTarget * 0.6, -10, 12);
        // A strong season with a playoff upset is a disappointment, not a crisis; a near miss on the win target is a warning.
        // (Replayed careers, Nov 10: a 54-28 team set "conference finals" was sacked after a first-round exit.)
        if (diff < 0 && winsVsTarget >= 0) delta = Math.Max(delta, -6);
        else if (diff < 0 && winsVsTarget >= -5) delta = Math.Max(delta, -14);
        string goalText = e.Survive ? "stay up" : Describe(e.Goal, ctx).ToLower();
        var lines = new List<string> { $"Goal: {goalText}. Result: {Describe(achieved, ctx).ToLower()} ({wins}-{losses}, target {e.TargetWins} wins)." };
        foreach (var d in e.Demands)
        {
            bool met = Meeting(d, ctx, team, stats, today, wins, wins + losses);
            delta += met ? 4 : -8 * impatience;
            lines.Add($"{Describe(d)}: {(met ? "done" : "not done")}.");
        }
        job.Confidence = Math.Clamp(job.Confidence + delta, 0, 100);
        // Success in a smaller league counts for less with clubs elsewhere.
        double weight = ctx.IsNabl ? 1 : Math.Clamp(1 + ctx.PrestigeOffset / 40, 0.4, 1);
        coach.Reputation = Math.Clamp(coach.Reputation + ((wins / Math.Max(1.0, wins + losses) - 0.5) * 10
            + achieved switch { SeasonGoal.Title => 10, SeasonGoal.Finals => 6, SeasonGoal.ConferenceFinals => 4, SeasonGoal.SecondRound => 2, SeasonGoal.Playoffs => 1, _ => 0 }) * weight, 0, 100);
        // A new coach gets his first season: a bad one earns a final warning, not the sack.
        bool firstSeason = job.HiredSeason == season;
        // A clearly winning season is never a sacking: the owner is disappointed, not done (critic review #3: 51-31 a year
        // after 72-10, and 24-22 the season after relegation, were both sacked).
        bool winning = wins / Math.Max(1.0, wins + losses) >= WinningRecord;
        if (winning) job.Confidence = Math.Max(job.Confidence, 25);
        bool sacked = !firstSeason && !winning && job.Confidence < 25 - (team.Owner.Patience - 10);
        if (firstSeason && job.Confidence < 25 - (team.Owner.Patience - 10)) job.Confidence = Math.Max(job.Confidence, 20);
        string verdict = sacked ? $"{team.Owner.Name} has decided to make a change. You have been sacked."
            : firstSeason && diff < 0 && job.Confidence <= 25 ? $"{team.Owner.Name} is giving you another season, but expects much better. Consider this a final warning."
            : diff >= 1 ? $"{team.Owner.Name} is delighted." : diff == 0 ? $"{team.Owner.Name} is satisfied." : $"{team.Owner.Name} expected more. Board confidence: {job.Confidence:0}/100.";
        lines.Add(verdict);
        return (sacked ? "You have been sacked" : "Season review", string.Join("\n", lines), sacked);
    }

    /// <summary>A club's standing when hiring: the better the squad and the bigger the spending, the higher the bar; overseas leagues are easier to get into.</summary>
    public static double Prestige(League league, Team team) => Prestige(LeagueContext.Nabl(league), team);
    public static double Prestige(LeagueContext ctx, Team team) => 25 + (1 - ctx.Fraction(ProjectedRank(ctx, team))) * 45 + ctx.PrestigeOffset;

    /// <summary>Chance a club hires you: your reputation against its prestige.</summary>
    public static double HireChance(Coach coach, League league, Team team) => HireChance(coach, LeagueContext.Nabl(league), team);
    public static double HireChance(Coach coach, LeagueContext ctx, Team team) => 1 / (1 + Math.Exp(-(coach.Reputation - Prestige(ctx, team) + 8) / 7));

    /// <summary>
    /// The coaching carousel for computer-run teams at the end of a season: owners who expected more may fire their
    /// coach (impatient owners more readily). Returns the teams that changed coach.
    /// </summary>
    public static List<Team> AiSeasonChanges(League league, Standings standings, Rng rng, int? humanTeamId) =>
        AiSeasonChanges(LeagueContext.Nabl(league), t => standings[t].Wins, rng, humanTeamId, league.Season);

    public static List<Team> AiSeasonChanges(LeagueContext ctx, Func<Team, int> wins, Rng rng, int? humanTeamId, int season)
    {
        var fired = new List<Team>();
        foreach (var t in ctx.Teams.Where(t => t.Id != humanTeamId))
        {
            var e = SetExpectations(ctx, t, season);
            double shortfall = Math.Max(0, e.TargetWins - wins(t)) * 82.0 / Math.Max(1, ctx.SeasonGames);
            double p = Math.Clamp(0.04 + shortfall * 0.025 * (1.3 - t.Owner.Patience / 40.0), 0, 0.7);
            if (rng.Chance(p)) fired.Add(t);
        }
        return fired;
    }
}
