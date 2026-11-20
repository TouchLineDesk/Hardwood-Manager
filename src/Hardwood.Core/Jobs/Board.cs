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
    /// <summary>Newly promoted, or (K7, run 115) projected last in a division that sends its last club down: the aim is to stay up.</summary>
    public bool Survive { get; set; }
    /// <summary>
    /// K7 (run 115): what "stay up" means for this club ("finish above last in the Mojave division, or win the promotion playoff
    /// to keep your place"). Null in older saves and for a promoted club that isn't projected last (<see cref="Board.SurviveText"/>).
    /// </summary>
    public string? SurviveNote { get; set; }
    /// <summary>What the club actually achieved (set at the season review), so next season's goal can follow it.</summary>
    public SeasonGoal? Achieved { get; set; }
    /// <summary>H70 (run 114): the season's wins, set at the review with <see cref="Achieved"/> (null in older saves).</summary>
    public int? Wins { get; set; }
}

/// <summary>A job you could apply for while out of work (decision #74).</summary>
public sealed class Vacancy
{
    public int TeamId { get; set; }
    public DateOnly Opened { get; set; }
    /// <summary>open · applied · offered · rejected · filled</summary>
    public string Status { get; set; } = "open";
    public DateOnly? DecideOn { get; set; }
    /// <summary>
    /// The career path (run 61, #651): true when the job opened while you were in work, as a step up from your club. Such
    /// openings close when the new season starts; older saves load without it (false).
    /// </summary>
    public bool WhileEmployed { get; set; }
    /// <summary>The coach the club hires if you don't take the job (drawn when it opened; empty in older saves).</summary>
    public string NextCoach { get; set; } = "";
    /// <summary>True when the club came to you (an approach after a strong season), not after an application.</summary>
    public bool Approach { get; set; }
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
    /// <summary>
    /// K7 (run 115): the protected rebuild after a relegation is granted once per job. A second relegation is not another free
    /// season. Reset on a new job; older saves load without it (false).
    /// </summary>
    public bool RebuildUsed { get; set; }
    /// <summary>
    /// H70 (run 123): board confidence when this season's judging began (<see cref="SeasonStartSeason"/>), the base for
    /// <see cref="Board.SeasonDropCap"/>. Null in older saves until the next weekly check.
    /// </summary>
    public double? SeasonStartConfidence { get; set; }
    public int SeasonStartSeason { get; set; }
    /// <summary>H70 (run 123): whether the last reviewed season was a winning one (for the mood label before a ball is bounced).</summary>
    public bool? LastSeasonWinning { get; set; }
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

    /// <summary>A goal inside a sentence: only its first letter lowered, so "Reach the Final Four" reads "reach the Final Four" (H65).</summary>
    public static string LowerFirst(string text) => text.Length == 0 ? text : char.ToLowerInvariant(text[0]) + text[1..];

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

    /// <summary>
    /// What a season came to, in the past tense (review #14 bug #16: the review printed the goal's wording for the result,
    /// so a club that missed the playoffs "resulted" in "develop the squad and show progress").
    /// </summary>
    public static string DescribeResult(SeasonGoal achieved, LeagueContext ctx, bool relegated = false, bool wonDivision = false, int madnessRound = -1, bool? goingUp = null)
    {
        if (relegated) return "relegated, last in your division";
        // H65: the round reached, when it's known (a team out in the Round of 32 won a game; one out in the First Four was in the field).
        if (ctx.IsCollege && madnessRound == 0) return "made the First Four, out before the Round of 64";
        if (ctx.IsCollege && achieved == SeasonGoal.Playoffs && madnessRound == 2) return "won a game in March Madness, out in the Round of 32";
        if (ctx.IsCollege && achieved == SeasonGoal.Playoffs && madnessRound == 1) return "made March Madness, out in the Round of 64";
        // CS2 (run 126): the runner-up reached the final, not just the Final Four.
        if (ctx.IsCollege && achieved == SeasonGoal.Finals && madnessRound == 6) return "reached the national final";
        if (ctx.IsCollege) return achieved switch
        {
            SeasonGoal.Title => "won the national championship",
            SeasonGoal.Finals => "reached the Final Four",
            SeasonGoal.ConferenceFinals => "reached the Elite Eight",
            SeasonGoal.SecondRound => "reached the Sweet 16",
            SeasonGoal.Playoffs or SeasonGoal.PlayIn => "made March Madness",
            _ => "missed March Madness",
        };
        // H70: a tier-2 division winner goes up only by winning the promotion playoff (run 102).
        string won = !wonDivision ? "" : goingUp ?? true ? "won your division and go up; " : "won your division but lost the promotion playoff; ";
        if (ctx.IsNabl || ctx.PlayoffTeams > 1) return won + achieved switch
        {
            SeasonGoal.Title => "won the championship",
            SeasonGoal.Finals => "reached the Finals",
            SeasonGoal.ConferenceFinals => "reached the conference finals",
            SeasonGoal.SecondRound => "won a playoff round",
            SeasonGoal.Playoffs => "made the playoffs",
            SeasonGoal.PlayIn => ctx.DivisionFormat ? "third in your division (the third-place tournament)" : "reached the play-in",
            _ => "missed the playoffs",
        };
        return won + achieved switch
        {
            SeasonGoal.Title => "won the league",
            SeasonGoal.Finals => "finished second",
            SeasonGoal.ConferenceFinals or SeasonGoal.SecondRound => "finished in the top three",
            SeasonGoal.Playoffs or SeasonGoal.PlayIn => "finished in the top half",
            _ => "finished in the bottom half",
        };
    }

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
    public static Expectations NextExpectations(LeagueContext ctx, Team team, int season, Expectations? previous, string leagueKey, int level, int previousLevel,
        bool promotionPlayoff = false, bool topLeagueDrops = true)
    {
        var e = NextExpectationsCore(ctx, team, season, previous, leagueKey, level, previousLevel);
        // K7 (run 115): a club the board expects to finish last in a division that sends its last club down is set to stay up,
        // and told what that takes. The Kings (12-70) were set "develop the squad, around 22 wins" and went down without the
        // stake ever being named.
        // The stake replaces the ladder's one-step rules on purpose: a club that sold its squad is told the truth this summer,
        // and next summer's goal starts again from "develop" (review of #179). A top league with no tier below drops nobody.
        if ((!ctx.IsNabl || topLeagueDrops) && AtRisk(ctx, team))
        {
            bool promoted = e.Survive;
            e.Survive = true;
            e.Goal = SeasonGoal.Develop;
            e.Demands.Remove(Demand.MakePlayoffs);
            // Review of #179: an ambitious owner's playoff-level target (43 of 82) doesn't belong to a survival goal.
            e.TargetWins = Math.Min(e.TargetWins, (int)Math.Round(ctx.SeasonGames * 0.4));
            string div = $"the {team.Division} division";
            e.SurviveNote = (promoted ? "newly promoted: " : "")
                + (ctx.IsNabl && promotionPlayoff ? $"finish above last in {div}, or win the promotion playoff to keep your place" : $"finish above last in {div}");
        }
        return e;
    }

    /// <summary>What "stay up" means for a club set to survive (K7): its own note, or the promoted club's general aim.</summary>
    public static string SurviveText(Expectations e) => e.SurviveNote ?? "newly promoted, avoid the relegation places";

    /// <summary>
    /// K7 (run 115): does this league send the last club in each division down? The top league in the division format (to tier 2,
    /// directly or through the promotion playoff) and the national tiers 2-4; nobody goes down from tier 5, college or abroad.
    /// </summary>
    public static bool Relegates(LeagueContext ctx) => ctx.DivisionFormat && (ctx.IsNabl || ctx.PyramidLevel is >= 2 and <= 4);

    /// <summary>
    /// K7 (run 115): projected last in its division (squad and payroll, <see cref="ProjectedRank(LeagueContext, Team)"/>) in a
    /// league that relegates the last club in each division.
    /// </summary>
    public static bool AtRisk(LeagueContext ctx, Team team)
    {
        if (!Relegates(ctx)) return false;
        var div = ctx.Teams.Where(t => t.Conference == team.Conference && t.Division == team.Division).ToList();
        if (div.Count < 2 || !div.Contains(team)) return false;
        return div.OrderBy(t => ProjectedRank(ctx, t)).Last() == team;
    }

    private static Expectations NextExpectationsCore(LeagueContext ctx, Team team, int season, Expectations? previous, string leagueKey, int level, int previousLevel)
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
        // H70 (run 114): the league's own ladder. College's Elite Eight and the division format's third place were off the old
        // list, so a goal on them skipped the one-step rules.
        var steps = Ladder(ctx);
        // A result off the ladder (an overseas conference final) counts as the step below it.
        int Idx(SeasonGoal g) { int i = steps.IndexOf(g); return i >= 0 ? i : steps.FindLastIndex(x => x < g); }
        int prevIdx = Idx(previous.Survive ? SeasonGoal.Develop : previous.Goal), idx = Idx(e.Goal);
        if (prevIdx >= 0 && idx > prevIdx + 1) e.Goal = steps[prevIdx + 1];
        // A goal that was missed isn't raised (critic review #4: Japan's goal climbed to "reach the Finals" after a 5th-place
        // finish). The board asks for the same again, or one step past what the club really managed.
        if (previous.Achieved is { } got && !previous.Survive && (int)got < (int)previous.Goal)
        {
            int cap = Math.Max(Idx(got) + 1, 0);
            var capGoal = steps[Math.Min(Math.Min(cap, prevIdx < 0 ? cap : prevIdx), steps.Count - 1)];
            if ((int)e.Goal > (int)capGoal) e.Goal = capGoal;
            if (previous.TargetWins > 0) e.TargetWins = Math.Min(e.TargetWins, previous.TargetWins);
        }
        // ...and a goal that was met isn't cut far below what the club just did (a 64-18 team was set "make the play-in").
        else if (previous.Achieved is { } done && !previous.Survive && Idx(done) > 0)
        {
            // A result off the ladder already maps to the step below it, which is the floor.
            var floor = steps[Math.Max(0, steps.Contains(done) ? Idx(done) - 1 : Idx(done))];
            if ((int)e.Goal < (int)floor) e.Goal = floor;
        }
        // Symmetry (critic review #6, decision #152): a goal falls at most one step a season, as it rises at most one.
        // Milwaukee went 48-34 against "make the playoffs, about 49 wins" and was then set "develop, about 31 wins".
        int nowIdx = Idx(e.Goal);
        if (prevIdx > 0 && nowIdx >= 0 && nowIdx < prevIdx - 1) e.Goal = steps[prevIdx - 1];
        int maxStep = (int)Math.Round(8 * ctx.SeasonGames / 82.0);
        if (previous.TargetWins > 0 && e.TargetWins > previous.TargetWins + maxStep) e.TargetWins = previous.TargetWins + maxStep;
        if (previous.TargetWins > 0 && e.TargetWins < previous.TargetWins - maxStep) e.TargetWins = previous.TargetWins - maxStep;
        // H70: after a goal met or a target beaten, the target falls by at most 3 (of 82). The Spurs' fell after a title, and
        // a patient owner's went 58 → 50 → 42 while the team won 64 and 61.
        int metDrop = (int)Math.Round(3 * ctx.SeasonGames / 82.0);
        bool goalMet = previous.Achieved is { } met && (ctx.IsCollege ? CollegeStep(met) >= CollegeStep(previous.Goal) : (int)met >= (int)previous.Goal);
        bool beatTarget = previous.Wins is { } won && won >= previous.TargetWins;
        if (previous.TargetWins > 0 && !previous.Survive && (goalMet || beatTarget)
            && e.TargetWins < previous.TargetWins - metDrop)
            e.TargetWins = previous.TargetWins - metDrop;
        // H70 (run 123): next season's target is set at the review, before the summer's moves, so the club still has the core
        // that won. It doesn't fall more than 3 (of 82) below the wins it just managed, nor rises past the old target this way
        // (a patient owner asked a 56-26 team for "around 49 wins").
        if (previous.Wins is { } lastWins && previous.TargetWins > 0 && !previous.Survive)
        {
            int floorWins = Math.Min(previous.TargetWins, lastWins - metDrop);
            if (e.TargetWins < floorWins) e.TargetWins = floorWins;
        }
        // H70 (run 117): a contender that keeps winning 60 (of 82) isn't asked for less as its squad ages. The Spurs won 69, 64
        // and 61, reached the conference finals each year, and were set Finals → conference finals → win a round.
        // A goal it met at that pace rises a step; the target never falls.
        if (previous.Wins is { } bigWins && !previous.Survive && bigWins >= ContenderShare * ctx.SeasonGames && previous.Goal > SeasonGoal.Develop)
        {
            int pi = Idx(previous.Goal);
            bool met60 = previous.Achieved is { } a60 && (ctx.IsCollege ? CollegeStep(a60) >= CollegeStep(previous.Goal) : (int)a60 >= (int)previous.Goal);
            var floorGoal = pi >= 0 && met60 ? steps[Math.Min(pi + 1, steps.Count - 1)] : previous.Goal;
            if ((int)e.Goal < (int)floorGoal) e.Goal = floorGoal;
            if (e.TargetWins < previous.TargetWins) e.TargetWins = previous.TargetWins;
        }
        return e;
    }

    /// <summary>K7 (run 115): the least a relegation costs in board confidence, and in a season the board had protected.</summary>
    public const double RelegationCost = 20, RelegationProtectedCost = 30;

    /// <summary>H70 (run 117): the win share (60 of 82) at which a club is a contender whose goal doesn't fall.</summary>
    public const double ContenderShare = 60 / 82.0;

    /// <summary>H70 (run 117): below this board confidence the verdict never says "delighted" or "satisfied".</summary>
    public const double ContentLine = 40;

    /// <summary>H137 (run 138): the most AD confidence a losing college regular season ends on (below "Secure").</summary>
    public const double LosingCap = 69;

    /// <summary>Owner patience at or above which the goal is set a step lower (H70).</summary>
    public const int PatientOwner = 15;

    /// <summary>The goals a league's season can end on, lowest first (the ladder goals move along).</summary>
    public static List<SeasonGoal> Ladder(LeagueContext ctx) =>
        ctx.IsNabl ? Enum.GetValues<SeasonGoal>().ToList()
        : ctx.IsCollege ? new List<SeasonGoal> { SeasonGoal.Develop, SeasonGoal.Playoffs, SeasonGoal.SecondRound, SeasonGoal.ConferenceFinals, SeasonGoal.Finals, SeasonGoal.Title }
        : ctx.DivisionFormat ? new List<SeasonGoal> { SeasonGoal.Develop, SeasonGoal.PlayIn, SeasonGoal.Playoffs, SeasonGoal.SecondRound, SeasonGoal.Finals, SeasonGoal.Title }
        : new List<SeasonGoal> { SeasonGoal.Develop, SeasonGoal.Playoffs, SeasonGoal.SecondRound, SeasonGoal.Finals, SeasonGoal.Title };

    /// <summary>One step down the league's ladder (Develop stays Develop).</summary>
    public static SeasonGoal StepDown(SeasonGoal g, LeagueContext ctx)
    {
        var steps = Ladder(ctx);
        int i = steps.IndexOf(g);
        if (i < 0) i = steps.FindLastIndex(x => x < g) + 1; // a goal not on this ladder sits just above the step below it
        return steps[Math.Max(0, i - 1)];
    }

    /// <summary>
    /// CS2 (run 126): where the athletic director ranks a program: this year's squad and the program's prestige (its standing over
    /// years) count equally, and the programs are ranked on the sum, so the country still has one favourite. The squad alone
    /// churns with every class, so Clemson's goal flipped between "build the program" and "make March Madness" each spring.
    /// Without prestige (older contexts) it is the squad's rank.
    /// </summary>
    public static int CollegeRank(LeagueContext ctx, Team team, int squadRank)
    {
        if (ctx.ProgramPrestige is not { } pr || !pr.ContainsKey(team.Id)) return squadRank;
        double Squad(Team t) => t.Roster.OrderByDescending(p => p.CurrentAbility).Take(9).Select(p => (double)p.CurrentAbility).DefaultIfEmpty(0).Average();
        var squadOrder = ctx.Teams.OrderByDescending(Squad).ThenBy(t => t.Id).Select((t, i) => (t.Id, i)).ToDictionary(x => x.Id, x => x.i + 1);
        var prestigeOrder = ctx.Teams.OrderByDescending(t => pr.GetValueOrDefault(t.Id, 0)).ThenBy(t => t.Id).Select((t, i) => (t.Id, i)).ToDictionary(x => x.Id, x => x.i + 1);
        int Sum(Team t) => squadOrder[t.Id] + prestigeOrder[t.Id];
        int mine = Sum(team);
        // Ties go to the better squad.
        return 1 + ctx.Teams.Count(t => t.Id != team.Id && (Sum(t) < mine || (Sum(t) == mine && squadOrder[t.Id] < squadOrder[team.Id])));
    }

    public static Expectations SetExpectations(LeagueContext ctx, Team team, int season)
    {
        int r = ProjectedRank(ctx, team);
        SeasonGoal goal;
        int target;
        if (ctx.IsCollege)
        {
            r = CollegeRank(ctx, team, r);
            // About 148 programs: the favourite is asked for the title, the top four the Final Four, the top 16 the Sweet 16,
            // roughly the top 45 a tournament bid (the at-large line sits around 45th once the automatic bids are in).
            goal = r == 1 ? SeasonGoal.Title : r <= 4 ? SeasonGoal.Finals : r <= 8 ? SeasonGoal.ConferenceFinals : r <= 16 ? SeasonGoal.SecondRound
                 : r <= 45 ? SeasonGoal.Playoffs : SeasonGoal.Develop;
            // CS2 (run 126): the target follows the rank on a curve, as college records do: the favourite about 88% of its
            // regular season (25 of 28), the 50th program about two in three, the middle of the country about half. The straight
            // line asked a title favourite and a "build the program" side for the same 22 wins.
            target = (int)Math.Round(ctx.SeasonGames * (0.88 - Math.Sqrt(ctx.Fraction(r)) * 0.6));
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
        // H70 (run 114): a patient owner asks one step less than the squad suggests, and a few wins fewer; an impatient one asks
        // for the squad's full worth (ambition, below, can still raise either). A patient tier-5 owner's first goal was "reach the
        // Finals" (Worcester).
        if (o.Patience >= PatientOwner && goal > SeasonGoal.Develop)
        {
            goal = StepDown(goal, ctx);
            target -= (int)Math.Round(3 * ctx.SeasonGames / 82.0);
        }
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
        // H70 (run 117): a team on or ahead of its target with every demand met keeps the board's goodwill. The weekly drift
        // pulled the Suns from 88 to about 70 while they went 53-29 against a target of 49.
        if (pace >= 0 && drivers.All(x => x.Points >= 0) && job.Confidence > level)
        {
            drivers.Add(new BoardDriver("On target", "On target with every demand met: the board's mood holds", job.Confidence - level));
            level = job.Confidence;
        }
        double rate = Math.Clamp(games / full, 0.1, 1) * 0.18 * impatience;
        return new BoardOutlook(level, rate, drivers);
    }

    /// <summary>
    /// H70 (run 123): wins (per 82) short of the target within which a playoff team's season costs at most
    /// <see cref="SeasonDropCap"/> of board confidence.
    /// </summary>
    public const double CloseWins = 7;

    /// <summary>
    /// H70 (run 123): extra wins of slack for the in-season pace, which swings more than a finished record (the Spurs were on
    /// pace for 49 in February and finished 52-30 against 57).
    /// </summary>
    public const double InSeasonSlack = 3;

    /// <summary>
    /// H70 (run 123): the most confidence a season can cost a team that made the playoffs within <see cref="CloseWins"/> of its
    /// target (and, in season, a team at .500 or better on that pace): about 19 for a patient owner, 23 for an average one and
    /// 27 for the most impatient. Hunt 13: a patient owner went 85 → 48 after 56-26 and a round won (target 57).
    /// </summary>
    public static double SeasonDropCap(Owner o) => 22 * (1.3 - o.Patience / 40.0);

    /// <summary>
    /// H70 (run 123): the board's mood label. "Critical" needs a losing record (or a relegation): a 54-28 team that went out in
    /// the first round read "Critical". A winning team below 35 reads "Under pressure".
    /// </summary>
    public static (string Label, string Colour) Mood(double c, bool winning) => c switch
    {
        >= 70 => ("Secure", "#3FB950"),
        >= 50 => ("Stable", "#A3D977"),
        >= 35 => ("Under pressure", "#D29922"),
        _ when winning => ("Under pressure", "#D29922"),
        _ => ("Critical", "#F85149"),
    };

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
        // H70 (run 123): the season's starting point for the drop cap, taken at the first weekly check of the season.
        if (job.SeasonStartSeason != season || job.SeasonStartConfidence == null)
        {
            job.SeasonStartConfidence = job.Confidence;
            job.SeasonStartSeason = season;
        }
        var outlook = Outlook(job, ctx, team, wins, losses, stats, today);
        if (e == null || outlook == null) return (null, null, false);
        double full = Math.Max(1, ctx.SeasonGames);
        double before = job.Confidence;
        job.Confidence = Math.Clamp(job.Confidence + (outlook.Level - job.Confidence) * outlook.Rate, 0, 100);
        // H70 (run 123): a team at .500 or better on pace within a few wins of its target doesn't lose more than a season's cap
        // from where the season began (a patient owner's 100 drifted to 56 while his champions went 52-30 against 57).
        double projected = wins / (double)games * full;
        if (job.SeasonStartConfidence is { } startConf && job.SeasonStartSeason == season
            && wins >= losses && (e.TargetWins - projected) * 82 / full <= CloseWins + InSeasonSlack)
            job.Confidence = Math.Max(job.Confidence, Math.Min(before, startConf - SeasonDropCap(team.Owner)));
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
        // A promotion playoff (run 102) isn't a round of the league's own playoffs: a division winner who played one is judged
        // on winning the division (the caller's credit), not as a playoff team.
        var mine = season.Series.Where(x => (x.HighId == teamId || x.LowId == teamId) && !World.UsaPyramid.IsPromotion(x.Round)).ToList();
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

    /// <summary>
    /// H70 (run 114): how a season measures up, the one judgement the review, its verdict, the confidence change and the
    /// career path's "overachieved" all read. <paramref name="Steps"/> is the result against the goal in ladder steps (college's
    /// shorter ladder; staying up for a promoted club; going up always meets the goal; going down never does);
    /// <paramref name="WinsVsTarget"/> is wins against the target, per 82 games and pro-rated to the games played.
    /// </summary>
    public readonly record struct SeasonJudgement(double Steps, double WinsVsTarget)
    {
        public bool GoalMet => Steps >= 0;
        /// <summary>The regular season clearly beat the target (the credit line for next season's protection).</summary>
        public bool BeatTarget => WinsVsTarget >= CreditWins;
    }

    /// <summary>
    /// CS2 (run 126): the round a seed is expected to reach in March Madness (<see cref="College.CollegeWorld.MadnessReached"/>'s
    /// scale: 1 = Round of 64, 2 = Round of 32, 3 = Sweet 16, 4 = Elite Eight), roughly what each seed line has done historically.
    /// An 8- or 9-seed is a coin flip in its first game (1.5).
    /// </summary>
    public static double SeedExpectedRound(int seed) => seed switch
    {
        <= 0 => 0,
        1 => 4,
        2 => 3.5,
        <= 4 => 3,
        <= 6 => 2,
        <= 10 => 1.5,
        _ => 1,
    };

    /// <summary>CS2: rounds past what the seed was expected to reach (0 when not in the field or not college).</summary>
    public static double SeedOverachievement(int seed, int madnessRound) =>
        seed <= 0 || madnessRound < 0 ? 0 : Math.Max(madnessRound, 1) - SeedExpectedRound(seed);

    /// <summary>H137 (run 133): confidence for each round short of the seed's expected March (at most two rounds).</summary>
    public const double MarchWeight = 6;

    /// <summary>CS2: for a "build the program" goal, the regular season (wins per 82 below the target, about 2 of a 28-game season) that still shows progress.</summary>
    public const double CollegeProgressSlack = 6;

    public static SeasonJudgement Judge(Expectations e, LeagueContext ctx, SeasonGoal achieved, int wins, int losses, bool relegated = false, bool goingUp = false,
        int seed = 0, int madnessRound = -1)
    {
        // College goals sit on a shorter ladder (in the field, Sweet 16, Final Four, title): a 1-seed set "win it all" who
        // goes out in the round of 32 is three steps short, not four (the college-strong critic career, C2).
        double diff = ctx.IsCollege ? CollegeStep(achieved) - CollegeStep(e.Goal) : (int)achieved - (int)e.Goal;
        double winsVsTargetNow = (wins - e.TargetWins * (wins + losses) / Math.Max(1.0, ctx.SeasonGames)) * 82 / Math.Max(1.0, ctx.SeasonGames);
        if (ctx.IsCollege && !e.Survive)
        {
            // CS2 (run 126): March is judged against the seed as well as the goal. An 8-seed in the Sweet 16 has overachieved
            // (Cincinnati, 16-15, read "the record fell short" at confidence 36); a 12-seed that wins a game has done well.
            double over = SeedOverachievement(seed, madnessRound);
            if (over >= 1) diff = Math.Max(diff, 1);
            else if (over >= 0.5) diff = Math.Max(diff, 0);
            // A "build the program" goal is met by progress: a regular season near the target. A program that missed the
            // tournament well short of it hasn't shown any (Clemson, 4-11 in the ACC, read "satisfied" and rose 39 → 45).
            if (e.Goal == SeasonGoal.Develop && achieved == SeasonGoal.Develop && winsVsTargetNow < -CollegeProgressSlack)
                diff = Math.Min(diff, -0.5);
        }
        // Review #14 bug #16: "stay up" is judged on staying up (met; more than met with a playoff place), and a relegation is
        // never a goal met (a 25-57 relegated season read "the owner is satisfied").
        if (e.Survive) diff = relegated ? -1 : achieved >= SeasonGoal.Playoffs ? 1 : 0;
        else if (relegated) diff = Math.Min(diff, -1);
        // H70: going up is the season a lower-tier owner wants most, so it beats any goal (the Kings went up at 50-12 and read
        // "expected more").
        if (goingUp && !relegated) diff = Math.Max(diff, 1);
        return new SeasonJudgement(diff, winsVsTargetNow);
    }

    public static (string title, string body, bool sacked) SeasonReview(JobState job, Coach coach, LeagueContext ctx, Team team, SeasonGoal achieved,
        int wins, int losses, IReadOnlyDictionary<int, PlayerSeasonStats> stats, DateOnly today, int season, bool relegated = false, bool wonDivision = false, int madnessRound = -1,
        bool? goingUp = null, int seed = 0, (int Wins, int Losses)? overall = null)
    {
        var e = job.Current ?? SetExpectations(ctx, team, season);
        e.Achieved = achieved;
        e.Wins = wins;
        // Older callers pass only wonDivision: a division winner went up before the promotion playoffs (run 102).
        bool up = goingUp ?? wonDivision;
        double impatience = (1.3 - team.Owner.Patience / 40.0) * (ctx.Grassroots ? 0.6 : 1);
        var j = Judge(e, ctx, achieved, wins, losses, relegated, up, seed, madnessRound);
        double diff = j.Steps, winsVsTarget = j.WinsVsTarget;
        // CS2 (run 126): rounds past the seed's expectation in March (college only; 0 otherwise).
        double seedOver = ctx.IsCollege ? SeedOverachievement(seed, madnessRound) : 0;
        double delta = diff >= 0 ? 10 + 5 * diff : -12 * -diff * impatience;
        // The regular season counts too (critic review: a 70-12 team that lost in the playoffs ended at confidence 20).
        delta += Math.Clamp(winsVsTarget * 0.6, -10, 12);
        // H137 (run 133): in college, March against the seed counts as much as the regular season. Duke's AD read "expected
        // more" (41) after an Elite Eight as a 1-seed, then "pleased with the regular season" (72) after a worse exit in the
        // Round of 32. An exit rounds before the seed's expectation costs up to 12; reaching it is no loss.
        bool earlyExit = ctx.IsCollege && seed > 0 && seedOver <= -1.5;
        bool onPar = ctx.IsCollege && seed > 0 && seedOver >= 0;
        if (ctx.IsCollege && seed > 0 && !e.Survive) delta += MarchWeight * Math.Clamp(seedOver, -2, 0);
        // A strong season with a playoff upset is a disappointment, not a crisis; a near miss on the win target is a warning.
        // (Replayed careers, Nov 10: a 54-28 team set "conference finals" was sacked after a first-round exit.)
        var lines = new List<string>();
        foreach (var d in e.Demands)
        {
            bool met = Meeting(d, ctx, team, stats, today, wins, wins + losses);
            delta += met ? 4 : -8 * impatience;
            lines.Add($"{Describe(d)}: {(met ? "done" : "not done")}.");
        }
        // H70: the floors come after the owner's demands, so a "delighted", "satisfied" or "pleased" verdict never comes with a
        // fall in confidence; a regular season that clearly beat the target never lowers it, whatever the playoffs did.
        bool protectedNow = job.ProtectedSeason == season && season != 0;
        bool rebuildYear = protectedNow && job.ProtectedReason == "rebuild";
        if (diff >= 0) delta = Math.Max(delta, 0);
        // K7 (run 115): going down is never softened by the record. It costs at least 20, and at least 30 in a season the board
        // had protected (a second relegation two years after the first left the Kings' owner at 71/100).
        else if (relegated) delta = Math.Min(delta, protectedNow ? -RelegationProtectedCost : -RelegationCost);
        else if (diff < 0 && j.BeatTarget && !earlyExit) delta = Math.Max(delta, 2);
        else if (diff < 0 && j.BeatTarget) delta = Math.Max(delta, -10); // H137: a great regular season softens an early March exit
        else if (diff < 0 && onPar) delta = Math.Max(delta, -6); // H137: March went as far as the seed promised
        else if (diff < 0 && winsVsTarget >= 0) delta = Math.Max(delta, -6);
        else if (diff < 0 && winsVsTarget >= -5) delta = Math.Max(delta, -14);
        // CS2 (run 126): a "build the program" season with no progress is a warning, not a second punishment on top of the
        // weekly drift that already followed the record (the review's share is at most 8).
        if (ctx.IsCollege && e.Goal == SeasonGoal.Develop && !e.Survive && diff < 0) delta = Math.Max(delta, -8);
        string goalText = e.Survive ? (e.SurviveNote is { } note ? $"stay up ({note})" : "stay up") : LowerFirst(Describe(e.Goal, ctx));
        string result = DescribeResult(achieved, ctx, relegated, wonDivision, madnessRound, up);
        if (e.Survive && !relegated) result = "stayed up, " + result;
        // CS2 (run 126): in college the target is the regular season's, and the line says so; the overall record (with the
        // conference tournament and March) sits beside it. The seed says what the March run was worth.
        if (ctx.IsCollege && seed > 0) result += $" as {SeedPhrase(seed)}";
        string record = ctx.IsCollege
            ? $"regular season {wins}-{losses}{(overall is { } all && all.Wins + all.Losses > wins + losses ? $", {all.Wins}-{all.Losses} overall" : "")}; target {e.TargetWins} wins"
            : $"{wins}-{losses}, target {e.TargetWins} wins";
        lines.Insert(0, $"Goal: {goalText}. Result: {result} ({record}).");
        double beforeReview = job.Confidence;
        job.Confidence = Math.Clamp(job.Confidence + delta, 0, 100);
        // H70 (run 123): a team that made the playoffs within a few wins of its target loses at most the season's cap from
        // where the season began, the weekly drift and the review together (85 → 48 after 56-26 and a round won, target 57).
        bool madePost = achieved >= SeasonGoal.Playoffs;
        if (!relegated && madePost && winsVsTarget >= -CloseWins)
        {
            double start = job.SeasonStartSeason == season && job.SeasonStartConfidence is { } sc ? sc : beforeReview;
            job.Confidence = Math.Max(job.Confidence, Math.Min(beforeReview, start - SeasonDropCap(team.Owner)));
        }
        // CS2 (run 126): a March run well past the seed (an 8-seed in the Sweet 16) leaves the AD content, whatever the regular
        // season's drift did (Cincinnati ended such a run at 36).
        if (seedOver >= 1) job.Confidence = Math.Max(job.Confidence, ContentLine + 5);
        // A .500 record isn't a losing one, the same rule as the mood label in season.
        job.LastSeasonWinning = !relegated && wins >= losses;
        // Success in a smaller league counts for less with clubs elsewhere.
        double weight = ctx.IsNabl ? 1 : Math.Clamp(1 + ctx.PrestigeOffset / 40, 0.4, 1);
        coach.Reputation = Math.Clamp(coach.Reputation + ((wins / Math.Max(1.0, wins + losses) - 0.5) * 10
            + achieved switch { SeasonGoal.Title => 10, SeasonGoal.Finals => 6, SeasonGoal.ConferenceFinals => 4, SeasonGoal.SecondRound => 2, SeasonGoal.Playoffs => 1, _ => 0 }) * weight, 0, 100);
        // A new coach gets his first season: a bad one earns a final warning, not the sack.
        bool firstSeason = job.HiredSeason == season;
        // A clearly winning season is never a sacking: the owner is disappointed, not done (critic review #3: 51-31 a year
        // after 72-10, and 24-22 the season after relegation, were both sacked).
        // K7 (run 115): ...but a relegation is, if the owner's patience has run out (the division rule, not the record, decides it).
        bool winning = !relegated && wins / Math.Max(1.0, wins + losses) >= WinningRecord;
        if (winning) job.Confidence = Math.Max(job.Confidence, 25);
        // Protected seasons (critic review #5: 59-23 then 41-41 was sacked; so was a relegated club 46 games into life
        // in the tier below): the board keeps you unless you fell below the floor.
        // K7 (run 115): the protection covers a poor record, not a relegation.
        bool shielded = protectedNow && !relegated
            && wins / Math.Max(1.0, wins + losses) >= ProtectedFloor;
        bool sacked = !firstSeason && !winning && !shielded && job.Confidence < 25 - (team.Owner.Patience - 10);
        if (shielded && job.Confidence < 25 - (team.Owner.Patience - 10)) job.Confidence = Math.Max(job.Confidence, 20);
        // Meet the goal, or beat the win target clearly, and next season is protected (critic review #5: a coach who won
        // the title at 59-23 was sacked the next May at 41-41).
        // K7 (run 115): a relegated season earns no protection, whatever the record.
        bool earned = !relegated && (j.BeatTarget || (diff >= 0 && winsVsTarget >= -CreditWins)
            // CS2 (run 126): in college, a regular season on target and a March run at least as far as the seed earns it too
            // (Kentucky went 25-2 and reached the Elite Eight as a 1-seed, then was sacked at 15-12 the next spring).
            || (ctx.IsCollege && seed > 0 && seedOver >= 0 && winsVsTarget >= 0));
        if (!sacked && earned) { job.ProtectedSeason = season + 1; job.ProtectedReason = "credit"; }
        if (firstSeason && job.Confidence < 25 - (team.Owner.Patience - 10)) job.Confidence = Math.Max(job.Confidence, 20);
        string owner = team.Owner.Name;
        // An exit needs a postseason the club was in (review of #178: a club that missed the playoffs read "playoff exit").
        bool reachedPost = ctx.IsCollege ? achieved >= SeasonGoal.Playoffs || madnessRound >= 0
            : (ctx.IsNabl || ctx.PlayoffTeams > 1) && achieved >= SeasonGoal.Playoffs;
        int ahead = (int)Math.Round(winsVsTarget * Math.Max(1.0, ctx.SeasonGames) / 82);
        // H137 (run 138): in college the words and the number agree with where the season began. A losing regular season is
        // never "Secure" (App State, 12-13, read Secure 95/100); a "delighted" season never ends below its start (Duke won the
        // title as a 1-seed and went 95 → 80); a "disappointed" one or "expected more" never above it (44 → 57).
        double seasonStart = job.SeasonStartSeason == season && job.SeasonStartConfidence is { } ss ? ss : beforeReview;
        if (ctx.IsCollege && !sacked)
        {
            if (wins < losses) job.Confidence = Math.Min(job.Confidence, LosingCap);
            string first = Verdict();
            if (first.Contains(" is delighted", StringComparison.Ordinal))
                job.Confidence = Math.Max(job.Confidence, wins < losses ? Math.Min(seasonStart, LosingCap) : seasonStart);
            else if (first.Contains(" is disappointed", StringComparison.Ordinal) || first.Contains(" expected more", StringComparison.Ordinal))
                job.Confidence = Math.Min(job.Confidence, Math.Max(seasonStart, winning ? 25 : 0));
        }
        string verdict = Verdict();
        lines.Add(verdict);
        return (sacked ? "You have been sacked" : "Season review", string.Join("\n", lines), sacked);

        string Verdict() => sacked ? $"{owner} has decided to make a change. You have been sacked."
            : firstSeason && diff < 0 && job.Confidence <= 25 ? $"{owner} is giving you another season, but expects much better. Consider this a final warning."
            : shielded && diff < 0 && job.Confidence <= 25 ? (job.ProtectedReason == "rebuild"
                ? $"{owner} accepts that this was a rebuilding year after relegation, but expects a promotion push next season."
                : $"{owner} hasn't forgotten last season, and that bought you time. It won't happen twice.")
            : relegated && rebuildYear ? $"{owner} gave you a season to rebuild after the last relegation, and the club has gone down again. Board confidence: {job.Confidence:0}/100."
            : relegated && e.Survive ? $"{owner} set one job, to stay up, and the club is going down. Board confidence: {job.Confidence:0}/100."
            : up && !relegated ? $"{owner} is delighted: the club goes up."
            : seedOver >= 1 ? $"{owner} is delighted with the March run: {SeedPhrase(seed)} {MarchPhrase(madnessRound)}. AD confidence: {job.Confidence:0}/100."
            // CS2: a "build the program" season that missed the tournament well short of the target showed no progress.
            : ctx.IsCollege && e.Goal == SeasonGoal.Develop && diff < 0 && !reachedPost
                ? $"{owner} wanted to see progress, and {wins}-{losses} is well short of the {e.TargetWins}-win target. AD confidence: {job.Confidence:0}/100."
            // H70 (run 117): the word follows the confidence. Cincinnati went 14-14 against a 21-win target and read "satisfied" at 32.
            : diff >= 0 && job.Confidence < ContentLine ? (winsVsTarget < 0
                ? $"{owner} accepts that the goal was met, but the record fell short of the {e.TargetWins}-win target. Board confidence: {job.Confidence:0}/100."
                : $"{owner} accepts the result, but there is ground to make up. Board confidence: {job.Confidence:0}/100.")
            : diff >= 1 ? $"{owner} is delighted."
            // H137 (run 138): Miami missed its 20-win target and March at 18-11 and read "satisfied" with nothing else.
            : diff == 0 && ctx.IsCollege && wins < e.TargetWins ? $"{owner} is satisfied with the progress, though {wins}-{losses} is short of the {e.TargetWins}-win target{(reachedPost ? "" : " and the team missed the tournament")}. AD confidence: {job.Confidence:0}/100."
            : diff == 0 ? $"{owner} is satisfied."
            // H70: the verdict follows the whole season, so a record well past the target isn't met with "expected more".
            : relegated && wins > losses ? $"{owner} is disappointed: a winning record wasn't enough to stay up, last in the division. Board confidence: {job.Confidence:0}/100."
            // H137: a college season is told by March as well. An early exit for the seed is a disappointment, however good
            // the regular season; a run as far as the seed promised is close to par.
            : earlyExit && !relegated ? $"{owner} is disappointed: {SeedPhrase(seed)} {ExitPhrase(madnessRound)} is well short of what the seed promised{(j.BeatTarget ? ", whatever the regular season did" : "")}. AD confidence: {job.Confidence:0}/100."
            : onPar && !relegated ? $"{owner} hoped for more, but {SeedPhrase(seed)} {ExitPhrase(madnessRound)} is about what the seed promised. AD confidence: {job.Confidence:0}/100."
            : !relegated && j.BeatTarget ? $"{owner} is pleased with the regular season ({ahead} {(ahead == 1 ? "win" : "wins")} past the target), but hoped to go further. Board confidence: {job.Confidence:0}/100."
            : !relegated && winsVsTarget >= 0 ? (reachedPost
                ? $"{owner} is disappointed with the {(ctx.IsCollege ? "tournament" : "playoff")} exit; the regular season was on target. Board confidence: {job.Confidence:0}/100."
                : $"{owner} is disappointed to fall short of the goal, though the record was on target. Board confidence: {job.Confidence:0}/100.")
            // H70 (run 123): a playoff team close to its target keeps the board's backing, and the verdict says so.
            : !relegated && madePost && winsVsTarget >= -CloseWins && job.Confidence >= 50
                ? $"{owner} hoped for more, but the team {(ctx.IsCollege ? "made the tournament" : reachedPost ? "made the playoffs" : "finished in the top half")} close to the {e.TargetWins}-win target and still has the {(ctx.IsCollege ? "AD's" : "board's")} backing. Board confidence: {job.Confidence:0}/100."
            : $"{owner} expected more. Board confidence: {job.Confidence:0}/100.";
    }

    /// <summary>CS2: "an 8-seed", "an 11-seed", "a 1-seed".</summary>
    public static string SeedPhrase(int seed) => (seed is 8 or 11 or 18 ? "an " : "a ") + $"{seed}-seed";

    /// <summary>H137 (run 133): where a March run ended, as an exit ("a 1-seed out in the Round of 32").</summary>
    public static string ExitPhrase(int madnessRound) => madnessRound switch
    {
        >= 7 => "winning the national championship",
        6 => "beaten in the national final",
        5 => "out in the Final Four",
        4 => "out in the Elite Eight",
        3 => "out in the Sweet 16",
        2 => "out in the Round of 32",
        0 => "out in the First Four",
        _ => "out in the Round of 64",
    };

    /// <summary>CS2: where a March run ended, after the seed ("an 8-seed in the Sweet 16").</summary>
    public static string MarchPhrase(int madnessRound) => madnessRound switch
    {
        7 => "winning the national championship",
        6 => "in the national final",
        5 => "in the Final Four",
        4 => "in the Elite Eight",
        3 => "in the Sweet 16",
        2 => "winning a game",
        _ => "in the field",
    };

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
