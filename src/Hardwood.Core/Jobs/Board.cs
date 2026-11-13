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
    /// <summary>What the club actually achieved (set at the season review), so next season's goal can follow it.</summary>
    public SeasonGoal? Achieved { get; set; }
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
    /// <summary>
    /// A season in which the board won't sack you mid-season, or at the end while you're above <see cref="Board.ProtectedFloor"/>
    /// of games won (critic review #5): the season after you beat the win target by 5+, and the first season after relegation.
    /// 0 = none. Older saves load without it.
    /// </summary>
    public int ProtectedSeason { get; set; }
    /// <summary>Why <see cref="ProtectedSeason"/> was granted: "credit" (beat the target) or "rebuild" (relegated).</summary>
    public string ProtectedReason { get; set; } = "";
}

public sealed record ConfidencePoint(DateOnly Date, double Confidence);

/// <summary>One thing the board is weighing, and how many points it moves the level confidence heads toward.</summary>
public sealed record BoardDriver(string Label, string Text, double Points);

/// <summary>The level confidence is drifting toward, the share of the gap it closes each week, and why (see <see cref="Board.Outlook"/>).</summary>
public sealed record BoardOutlook(double Level, double Rate, IReadOnlyList<BoardDriver> Drivers);

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
    public static string Describe(SeasonGoal g, LeagueContext ctx) => ctx.IsCollege ? DescribeCollege(g)
        : ctx.DivisionFormat && g == SeasonGoal.PlayIn ? "Finish in your division's top three"
        : ctx.IsNabl || ctx.PlayoffTeams > 1 ? Describe(g) : g switch
    {
        SeasonGoal.Title => "Win the league",
        SeasonGoal.Finals => "Finish in the top two",
        SeasonGoal.ConferenceFinals or SeasonGoal.SecondRound => "Finish in the top three",
        SeasonGoal.Playoffs or SeasonGoal.PlayIn => "Finish in the top half",
        _ => "Stay clear of the bottom and show progress",
    };

    /// <summary>College goals are about March Madness (#160).</summary>
    public static string DescribeCollege(SeasonGoal g) => g switch
    {
        SeasonGoal.Title => "Win the national championship",
        SeasonGoal.Finals => "Reach the Final Four",
        SeasonGoal.ConferenceFinals => "Reach the Elite Eight",
        SeasonGoal.SecondRound => "Reach the Sweet 16",
        SeasonGoal.Playoffs or SeasonGoal.PlayIn => "Make March Madness",
        _ => "Build the program and show progress",
    };

    /// <summary>
    /// A goal's place on the college ladder: 0 build, 1 in the field, 2 Sweet 16, 2.5 Elite Eight, 3 Final Four, 4 champion.
    /// The Elite Eight is a half step (C4 part 2, #304), so a Sweet 16 goal reached with an Elite Eight run counts as more
    /// than met without making a 1-seed's early exit any harsher than before.
    /// </summary>
    public static double CollegeStep(SeasonGoal g) => g switch
    {
        SeasonGoal.Title => 4,
        SeasonGoal.Finals => 3,
        SeasonGoal.ConferenceFinals => 2.5,
        SeasonGoal.SecondRound => 2,
        SeasonGoal.Playoffs or SeasonGoal.PlayIn => 1,
        _ => 0,
    };

    /// <summary>How far a program got: champion, Final Four, Elite Eight, Sweet 16, in the field, or not.</summary>
    public static SeasonGoal Achieved(College.CollegeWorld cw, int teamId) => cw.MadnessReached(teamId) switch
    {
        7 => SeasonGoal.Title,
        >= 5 => SeasonGoal.Finals,
        4 => SeasonGoal.ConferenceFinals,
        >= 3 => SeasonGoal.SecondRound,
        >= 1 => SeasonGoal.Playoffs,
        _ => SeasonGoal.Develop,
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
    /// goal from the squad moves at most one step from last season's, up or down, and the win target at most 8 (of 82) either way.
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
        // Relegated: a rebuild year. The squad may look strong for the tier below, but it just won a fraction of its games
        // (critic review #5: an 11-71 club was set "reach the Finals" in tier 2 and sacked at 17-29).
        bool relegated = previous.League != leagueKey && level > 0 && previousLevel > 0 && level > previousLevel;
        if (relegated)
        {
            if ((int)e.Goal > (int)SeasonGoal.Playoffs) e.Goal = SeasonGoal.Playoffs;
            e.TargetWins = Math.Min(e.TargetWins, (int)Math.Round(ctx.SeasonGames * 0.5));
            return e;
        }
        if (previous.League != leagueKey) return e;
        var steps = ctx.IsNabl ? Enum.GetValues<SeasonGoal>().ToList()
            : new List<SeasonGoal> { SeasonGoal.Develop, SeasonGoal.Playoffs, SeasonGoal.SecondRound, SeasonGoal.Finals, SeasonGoal.Title };
        int prevIdx = steps.IndexOf(previous.Survive ? SeasonGoal.Develop : previous.Goal), idx = steps.IndexOf(e.Goal);
        if (prevIdx >= 0 && idx > prevIdx + 1) e.Goal = steps[prevIdx + 1];
        // A goal that was missed isn't raised (critic review #4: Japan's goal climbed to "reach the Finals" after a 5th-place
        // finish). The board asks for the same again, or one step past what the club really managed.
        if (previous.Achieved is { } got && !previous.Survive && (int)got < (int)previous.Goal)
        {
            int cap = Math.Max(steps.IndexOf(got) + 1, 0);
            var capGoal = steps[Math.Min(Math.Min(cap, prevIdx < 0 ? cap : prevIdx), steps.Count - 1)];
            if ((int)e.Goal > (int)capGoal) e.Goal = capGoal;
            if (previous.TargetWins > 0) e.TargetWins = Math.Min(e.TargetWins, previous.TargetWins);
        }
        // ...and a goal that was met isn't cut far below what the club just did (a 64-18 team was set "make the play-in").
        else if (previous.Achieved is { } done && !previous.Survive && steps.IndexOf(done) > 0)
        {
            var floor = steps[Math.Max(0, steps.IndexOf(done) - 1)];
            if ((int)e.Goal < (int)floor) e.Goal = floor;
        }
        // Symmetry (critic review #6, decision #152): a goal falls at most one step a season, as it rises at most one.
        // Milwaukee went 48-34 against "make the playoffs, about 49 wins" and was then set "develop, about 31 wins".
        int nowIdx = steps.IndexOf(e.Goal);
        if (prevIdx > 0 && nowIdx >= 0 && nowIdx < prevIdx - 1) e.Goal = steps[prevIdx - 1];
        int maxStep = (int)Math.Round(8 * ctx.SeasonGames / 82.0);
        if (previous.TargetWins > 0 && e.TargetWins > previous.TargetWins + maxStep) e.TargetWins = previous.TargetWins + maxStep;
        if (previous.TargetWins > 0 && e.TargetWins < previous.TargetWins - maxStep) e.TargetWins = previous.TargetWins - maxStep;
        return e;
    }

    public static Expectations SetExpectations(LeagueContext ctx, Team team, int season)
    {
        int r = ProjectedRank(ctx, team);
        SeasonGoal goal;
        int target;
        if (ctx.IsCollege)
        {
            // About 148 programs: the favourite is asked for the title, the top four the Final Four, the top 16 the Sweet 16,
            // roughly the top 45 a tournament bid (the at-large line sits around 45th once the automatic bids are in).
            goal = r == 1 ? SeasonGoal.Title : r <= 4 ? SeasonGoal.Finals : r <= 8 ? SeasonGoal.ConferenceFinals : r <= 16 ? SeasonGoal.SecondRound
                 : r <= 45 ? SeasonGoal.Playoffs : SeasonGoal.Develop;
            target = (int)Math.Round(ctx.SeasonGames * (0.80 - ctx.Fraction(r) * 0.52));
        }
        else if (ctx.IsNabl)
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
                 : r <= topHalf ? SeasonGoal.Playoffs : ctx.DivisionFormat && r <= 20 ? SeasonGoal.PlayIn : SeasonGoal.Develop;
            target = (int)Math.Round(ctx.SeasonGames * (0.73 - ctx.Fraction(r) * 0.46));
            // Local and regional owners are modest (critic review #4: a 17-13 first season at tier 5 left the board at 20
            // with a "win the title" goal): favourites aim for the top three, the rest for the top half.
            if (ctx.Grassroots && !ctx.DivisionFormat) // tiers 4-5 in the division format (D39 A) use the goals above
            {
                goal = r <= 3 ? SeasonGoal.SecondRound : r <= topHalf ? SeasonGoal.Playoffs : SeasonGoal.Develop;
                target = (int)Math.Round(ctx.SeasonGames * (0.62 - ctx.Fraction(r) * 0.36));
            }
        }
        var o = team.Owner;
        var e = new Expectations { Season = season, Goal = goal, TargetWins = target };
        if (ctx.IsCollege)
        {
            // No wages and no veterans in college: an ambitious AD simply wants the tournament (#168).
            if (o.Ambition >= 15 && goal < SeasonGoal.Playoffs)
            {
                e.Goal = SeasonGoal.Playoffs;
                e.TargetWins = Math.Max(e.TargetWins, (int)Math.Round(ctx.SeasonGames * 0.6));
            }
            return e;
        }
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
    /// <summary>In a protected season (<see cref="JobState.ProtectedSeason"/>) the board keeps you while you win at least this share.</summary>
    public const double ProtectedFloor = 0.40;
    /// <summary>Wins above the target (per 82 games) that earn the next season's protection on their own; meeting the goal
    /// earns it too unless the record fell this far short of the target.</summary>
    public const double CreditWins = 5;

    /// <summary>
    /// What the board is weighing each week (the assistant explains "why", roadmap 4): the level confidence drifts toward,
    /// how fast it gets there, and each thing pushing it up or down. <see cref="Weekly(JobState, LeagueContext, Team, int, int, IReadOnlyDictionary{int, PlayerSeasonStats}, DateOnly, int)"/>
    /// moves confidence with exactly these numbers. Null before the board starts judging (game five).
    /// </summary>
    public static BoardOutlook? Outlook(JobState job, LeagueContext ctx, Team team, int wins, int losses,
        IReadOnlyDictionary<int, PlayerSeasonStats> stats, DateOnly today)
    {
        var e = job.Current;
        int games = wins + losses;
        if (e == null || games < 5) return null;
        double impatience = (1.3 - team.Owner.Patience / 40.0) * (ctx.Grassroots ? 0.6 : 1);
        double full = Math.Max(1, ctx.SeasonGames);
        double projected = wins / (double)games * full;
        var drivers = new List<BoardDriver>();
        // Confidence drifts toward a level set by the pace: on target ≈ 62, 10 wins short (of 82) ≈ 42, 20 short ≈ 22.
        // Early in the season the record says little, so the board moves slowly; impatient owners move faster.
        double pace = (projected - e.TargetWins) * 2 * (82 / full);
        drivers.Add(new BoardDriver("Pace",
            $"On pace for {projected:0} wins against a target of {e.TargetWins}" + (Math.Abs(projected - e.TargetWins) < 0.5 ? "" : projected > e.TargetWins ? $" ({projected - e.TargetWins:0} ahead)" : $" ({e.TargetWins - projected:0} short)"),
            pace));
        foreach (var d in e.Demands)
        {
            bool met = Meeting(d, ctx, team, stats, today, wins, games);
            drivers.Add(new BoardDriver("Owner's demand", $"{Describe(d)}: {(met ? "being done" : "not being done")}", met ? 0 : -5));
        }
        double level = Math.Clamp(BaseLevel + drivers.Sum(x => x.Points), 0, 100);
        double rate = Math.Clamp(games / full, 0.1, 1) * 0.18 * impatience;
        return new BoardOutlook(level, rate, drivers);
    }

    /// <summary>Where confidence settles for a team exactly on its target with every demand met.</summary>
    public const double BaseLevel = 62;

    public static (string? title, string? body, bool sacked) Weekly(JobState job, League league, Team team, int wins, int losses,
        IReadOnlyDictionary<int, PlayerSeasonStats> stats, DateOnly today) =>
        Weekly(job, LeagueContext.Nabl(league), team, wins, losses, stats, today, league.Season);

    public static (string? title, string? body, bool sacked) Weekly(JobState job, LeagueContext ctx, Team team, int wins, int losses,
        IReadOnlyDictionary<int, PlayerSeasonStats> stats, DateOnly today, int season)
    {
        var e = job.Current;
        int games = wins + losses;
        var outlook = Outlook(job, ctx, team, wins, losses, stats, today);
        if (e == null || outlook == null) return (null, null, false);
        double full = Math.Max(1, ctx.SeasonGames);
        job.Confidence = Math.Clamp(job.Confidence + (outlook.Level - job.Confidence) * outlook.Rate, 0, 100);
        job.History.Add(new ConfidencePoint(today, Math.Round(job.Confidence, 1)));
        if (job.History.Count > 200) job.History.RemoveAt(0);

        bool honeymoon = today.DayNumber - job.HiredOn.DayNumber < 60;
        // A new coach is only sacked mid-way through his first season if it's a catastrophe.
        double line = job.HiredSeason == season && job.HiredSeason != 0 ? 3 : 12 + (10 - team.Owner.Patience) * 0.8;
        // A winning team isn't sacked mid-season, whatever the target (critic review #3).
        bool winning = wins / (double)games >= WinningRecord;
        // The season after a big overshoot, and a relegated club's first season down, are never mid-season sackings.
        bool shielded = job.ProtectedSeason == season && season != 0;
        if (!honeymoon && !winning && !shielded && games >= full * 30 / 82 && job.Confidence < line)
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
        // The bracket names its rounds by conference ("East First Round", "West Semifinals", "East Finals"); only the
        // NABL Finals is plain "Finals". Until v0.48.1 this matched the plain names only, so every exit before the
        // Finals was judged "reach the play-in" (review #7: a 64-18 second seed's season review).
        return RoundReached(last.Round) ?? (post.Series.Any(s => RoundReached(s.Round) == SeasonGoal.Playoffs && (s.Higher == team || s.Lower == team))
            ? SeasonGoal.Playoffs : SeasonGoal.PlayIn);
    }

    /// <summary>The goal a playoff round stands for (null for the play-in).</summary>
    public static SeasonGoal? RoundReached(string round)
    {
        if (round == "Finals") return SeasonGoal.Finals;
        // The third-place tournament (D36) is the division format's step below the playoffs, where the play-in was.
        if (Season.Postseason.IsThirdPlace(round)) return SeasonGoal.PlayIn;
        if (round.Contains("Play-In")) return null;
        if (round.EndsWith("First Round")) return SeasonGoal.Playoffs;
        if (round.EndsWith("Semifinals")) return SeasonGoal.SecondRound;
        if (round.EndsWith("Finals")) return SeasonGoal.ConferenceFinals;
        return null;
    }

    /// <summary>How far a club got in an overseas league's playoffs (title, final, won a round, made them, or not).</summary>
    public static SeasonGoal Achieved(int teamId, World.CompetitionSeason season, World.CompetitionProfile profile)
    {
        if (profile.Playoffs.Length == 0 || (profile.DivisionPlayoffs && season.Series.Count == 0))
        {
            // Judged on the final table (also a pyramid tier's last season before it reaches four divisions of four).
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
        // The third-place tournament (D36) stands where the play-in did.
        var main = mine.Where(x => !Season.Postseason.IsThirdPlace(x.Round)).ToList();
        if (main.Count == 0) return SeasonGoal.PlayIn;
        int top = main.Max(x => x.RoundIndex);
        return top >= 2 && profile.Playoffs.Length >= 4 ? SeasonGoal.ConferenceFinals : top >= 1 ? SeasonGoal.SecondRound : SeasonGoal.Playoffs;
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
        e.Achieved = achieved;
        double impatience = (1.3 - team.Owner.Patience / 40.0) * (ctx.Grassroots ? 0.6 : 1);
        // College goals sit on a shorter ladder (in the field, Sweet 16, Final Four, title): a 1-seed set "win it all" who
        // goes out in the round of 32 is three steps short, not four (the college-strong critic career, C2).
        double diff = ctx.IsCollege ? CollegeStep(achieved) - CollegeStep(e.Goal) : (int)achieved - (int)e.Goal;
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
        // Protected seasons (critic review #5: 59-23 then 41-41 was sacked; so was a relegated club 46 games into life
        // in the tier below): the board keeps you unless you fell below the floor.
        bool shielded = job.ProtectedSeason == season && season != 0
            && wins / Math.Max(1.0, wins + losses) >= ProtectedFloor;
        bool sacked = !firstSeason && !winning && !shielded && job.Confidence < 25 - (team.Owner.Patience - 10);
        if (shielded && job.Confidence < 25 - (team.Owner.Patience - 10)) job.Confidence = Math.Max(job.Confidence, 20);
        // Meet the goal, or beat the win target clearly, and next season is protected (critic review #5: a coach who won
        // the title at 59-23 was sacked the next May at 41-41).
        bool earned = winsVsTarget >= CreditWins || (diff >= 0 && winsVsTarget >= -CreditWins);
        if (!sacked && earned) { job.ProtectedSeason = season + 1; job.ProtectedReason = "credit"; }
        if (firstSeason && job.Confidence < 25 - (team.Owner.Patience - 10)) job.Confidence = Math.Max(job.Confidence, 20);
        string verdict = sacked ? $"{team.Owner.Name} has decided to make a change. You have been sacked."
            : firstSeason && diff < 0 && job.Confidence <= 25 ? $"{team.Owner.Name} is giving you another season, but expects much better. Consider this a final warning."
            : shielded && diff < 0 && job.Confidence <= 25 ? (job.ProtectedReason == "rebuild"
                ? $"{team.Owner.Name} accepts that this was a rebuilding year after relegation, but expects a promotion push next season."
                : $"{team.Owner.Name} hasn't forgotten last season, and that bought you time. It won't happen twice.")
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
