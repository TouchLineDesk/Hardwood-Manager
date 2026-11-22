using Hardwood.Core.Jobs;
using Hardwood.Core.Model;
using Hardwood.Core.World;

namespace Hardwood.Core.Season;

/// <summary>
/// The career path (Danny, chat, Nov 15, #651; run 61): resign; jobs and offers while you're in work (bigger clubs and
/// programs approach a coach who beats expectations, by reputation); applying for a step up without leaving; and college
/// and pro jobs both ways (a college coach can go to the NBA or the G League, a pro coach to a college program).
///
/// Everything here draws from its own random stream (<see cref="CareerRng"/>), never the session's, so a career that
/// never moves plays exactly as before (Danny's roadmap principle: seeded results stay reproducible, #595).
/// </summary>
public sealed partial class GameSession
{
    /// <summary>How much more prestigious a job must be than yours for it to count as a step up.</summary>
    public const double StepUpMargin = 3;
    /// <summary>The most offers one summer brings.</summary>
    public const int MaxApproaches = 2;
    /// <summary>A college opening reaches you only if the program would seriously consider you (else 30+ a summer).</summary>
    public const double CollegeInterestFloor = 0.1;
    /// <summary>The most step-up openings shown to a coach in work at once (a summer's carousel opened 80 for a weak NBA coach).</summary>
    public const int MaxOpenings = 8;
    /// <summary>Of those, the most from college programs.</summary>
    public const int MaxCollegeOpenings = 4;

    /// <summary>The career path's own random numbers: a stream per day (and salt), independent of the session's.</summary>
    private Rng CareerRng(int salt = 0) =>
        new(Seed ^ 0xC0AC4E5A11D3B7F1UL ^ ((ulong)(uint)Today.DayNumber * 0xBF58476D1CE4E5B9UL) ^ ((ulong)(uint)salt * 0x9E3779B97F4A7C15UL));

    /// <summary>A club's standing as a job (the board's hiring bar), in its own league's terms.</summary>
    public double JobPrestige(Team t) => Board.Prestige(ContextOf(t), t);

    /// <summary>H167 (run 144, #933): what a recently sacked college coach's chance at a pro club is scaled by while his
    /// reputation is low. Pro clubs look for coaches who have won; a college coach sacked this season is a long shot until
    /// he has rebuilt his name (Providence, sacked at 12-19 with reputation 24, coached Riga the same week).</summary>
    public const double SackedCollegeCoachProFactor = 0.2;

    /// <summary>H167: below this reputation a recently sacked college coach is a long shot for a pro head job.</summary>
    public const double SackedCollegeCoachProReputation = 50;

    /// <summary>
    /// The chance a club hires you (<see cref="Board.HireChance(Coach, LeagueContext, Team)"/>), with H167's rule (#933): out of
    /// work after a sacking at a college program this season, a pro club takes you only rarely unless your reputation is high.
    /// College programs (lower conferences first) are the way back.
    /// </summary>
    public double HireChanceFor(Team t)
    {
        double chance = Board.HireChance(Coach, ContextOf(t), t);
        return RecentlySackedFromCollege && !IsCollegeProgram(t) && Coach.Reputation < SackedCollegeCoachProReputation
            ? chance * SackedCollegeCoachProFactor : chance;
    }

    /// <summary>H167: out of work, and the last job was a college program that sacked you this season (or this summer).</summary>
    public bool RecentlySackedFromCollege =>
        !Employed && Coach.History.LastOrDefault() is { Ended: "Sacked" } last && last.ToSeason >= League.Season
        && World.College?.Programs.Any(p => p.Id == last.TeamId) == true;

    /// <summary>A college program (not a pro club).</summary>
    public bool IsCollegeProgram(Team t) => World.College?.Programs.Contains(t) == true;

    /// <summary>The USA pyramid level of a pro club (1 the NBA, 2 the G League…), 0 abroad or in college.</summary>
    public int PyramidLevelOf(Team t) => League.Teams.Contains(t) ? 1 : World.LeagueOf(t) is { } c ? UsaPyramid.Level(c.Key) : 0;

    /// <summary>
    /// Jobs you can be hired for from where you are (#651): a college coach can go to another program or to the NBA and the
    /// G League (the top two tiers); a pro coach to any pro club or a college program.
    /// </summary>
    public bool EligibleFor(Team t) => !InCollege || !Employed || IsCollegeProgram(t) || PyramidLevelOf(t) is 1 or 2;

    /// <summary>A job worth leaving yours for: eligible, and clearly more prestigious than your club.</summary>
    public bool IsStepUp(Team t) =>
        Employed && t != HumanTeam && EligibleFor(t) && NotALowerLeague(t) && JobPrestige(t) >= JobPrestige(HumanTeam) + StepUpMargin;

    /// <summary>
    /// A move up never means a lower league: in the USA pyramid a job in your tier or above; a club abroad only for a coach
    /// abroad or in tiers 3-5 (overseas top flights sit about there); college programs for anyone.
    /// </summary>
    private bool NotALowerLeague(Team t)
    {
        if (IsCollegeProgram(t) || InCollege) return true;
        int mine = MyLevel, theirs = PyramidLevelOf(t);
        if (theirs > 0) return mine == 0 || theirs <= mine;
        return mine == 0 || mine >= 3;
    }

    /// <summary>Openings you can act on right now: open, applied for, or offered.</summary>
    public IEnumerable<Vacancy> LiveVacancies => Job.Vacancies.Where(v => v.Status is "open" or "applied" or "offered");

    // ------------------------------------------------------------------------------------------
    // Resign

    /// <summary>Resign from your job. Null on success, else why not. You keep your history and look for work.</summary>
    public string? Resign()
    {
        if (!Employed) return "You don't have a job to resign from.";
        var club = HumanTeam;
        Coach.History.Add(JobRecordFor(club, "Resigned"));
        // Walking out costs a little standing (a sacking costs 5).
        Coach.Reputation = Math.Max(0, Coach.Reputation - 2);
        // The jobs you'd opened while in work stay open to you; offers stand until they lapse.
        foreach (var v in LiveVacancies) v.WhileEmployed = false;
        string successor = Board.NewCoachName(CareerRng(1));
        ReleaseClub(successor);
        Post("You resigned", $"You have resigned as head coach of {club.TheFullName}; {successor} takes over. "
            + "Clubs that change coach post their openings on the Board page, where you can apply. "
            + $"Your reputation: {Coach.Reputation:0}/100.", "Board");
        SaveNow();
        return null;
    }

    /// <summary>Hand your club back to the computer (a sacking, a resignation or a move).</summary>
    private void ReleaseClub(string successor)
    {
        var club = HumanTeam;
        club.Coach = null;
        club.Tactic.Auto = true;
        club.DepthChart = null;
        club.CoachName = successor;
        // Mid-summer, the club you leave still re-signs its own players (it was left out of the computer's pass for you).
        if (Phase == SessionPhase.Offseason && Offseason is { Phase: OffseasonPhase.ReSigning } o && League.Teams.Contains(club)) o.AiReSign(club);
        Job.Employed = false;
        Job.Current = null;
        Promises.Clear();
        // H109 (run 117): the old club's unposted results and knocks go with the job (the sacking message closes that chapter).
        WeekResults.Clear();
        WeekKnocks.Clear();
        WeekRecruiting.Clear();
        _resultsSince = null;
        SyncManagedIds();
    }

    /// <summary>The world and the summer know which club is yours (none while you're out of work).</summary>
    private void SyncManagedIds()
    {
        World.ManagedClubId = Employed && Abroad && !InCollege ? HumanTeam.Id : null;
        if (World.College is { } cw) cw.ManagedProgramId = Employed && InCollege ? HumanTeam.Id : null;
        if (Offseason != null) Offseason.HumanTeamId = ControlledTeamId;
        RefreshProPitch(); // CP3: a new program has its own pro record (none if you left college)
    }

    // ------------------------------------------------------------------------------------------
    // Openings and offers while in work

    /// <summary>
    /// A club changes coach while you're in work: a step up opens to you (it shows on the Board page, where you can
    /// apply). Returns false when it isn't one, so the club just hires.
    /// </summary>
    private bool OpenWhileEmployed(Team t, string successor)
    {
        if (!IsStepUp(t) || LiveVacancies.Any(v => v.TeamId == t.Id)) return false;
        var mine = LiveVacancies.Where(v => v.WhileEmployed).ToList();
        if (mine.Count >= MaxOpenings) return false;
        if (IsCollegeProgram(t) && (Board.HireChance(Coach, ContextOf(t), t) < CollegeInterestFloor
            || mine.Count(v => FindTeam(v.TeamId) is { } x && IsCollegeProgram(x)) >= MaxCollegeOpenings)) return false;
        Job.Vacancies.Add(new Vacancy { TeamId = t.Id, Opened = Today, WhileEmployed = true, NextCoach = successor });
        t.CoachName = "Interim coach";
        return true;
    }

    /// <summary>
    /// College programs change coaches at the end of the season too (#651), so college jobs open: athletic directors who
    /// expected more sometimes make a change, as owners do.
    /// </summary>
    private void CollegeCoachingChanges()
    {
        if (World.College is not { } cw || cw.Programs.Count == 0) return;
        var rng = CareerRng(2);
        var ctx = LeagueContext.College(cw, cw.Programs[0]);
        // CS2 (run 126): the regular season, as the target covers (review of PR #190).
        var wins = cw.Programs.ToDictionary(p => p.Id, p => cw.RegularSeasonRecord(p.Id).wins);
        foreach (var t in Board.AiSeasonChanges(ctx, t => wins[t.Id], rng, ControlledTeamId, League.Season))
        {
            string old = t.CoachName;
            string successor = Board.NewCoachName(rng);
            if (Employed ? OpenWhileEmployed(t, successor) : OpenWhileOut(t, successor, old)) continue;
            t.CoachName = successor;
        }
    }

    /// <summary>Out of work: a college opening joins the list when the program would consider you.</summary>
    private bool OpenWhileOut(Team t, string successor, string old)
    {
        if (LiveVacancies.Any(v => v.TeamId == t.Id) || Board.HireChance(Coach, ContextOf(t), t) < CollegeInterestFloor) return false;
        Job.Vacancies.Add(new Vacancy { TeamId = t.Id, Opened = Today, NextCoach = successor });
        t.CoachName = "Interim coach";
        JobOpenings.Add($"• {t.FullName} ({ContextOf(t).Name}) have parted ways with {old}.");
        return true;
    }

    /// <summary>
    /// After the season review: clubs with a step-up opening may come to you. A coach who beat his goal or his win target
    /// is wanted; the chance is the club's hiring chance (your reputation against its standing), much lower after an
    /// ordinary season. At most two offers a summer; all of them in one message, and on the Board page.
    /// </summary>
    private void CareerApproaches(bool overachieved)
    {
        if (!Employed) return;
        var rng = CareerRng(3);
        var openings = LiveVacancies.Where(v => v.WhileEmployed && v.Status == "open")
            .Select(v => (v, team: FindTeam(v.TeamId))).Where(x => x.team != null)
            .OrderByDescending(x => JobPrestige(x.team!)).ToList();
        var offers = new List<Team>();
        foreach (var (v, t) in openings)
        {
            if (offers.Count >= MaxApproaches) break;
            double chance = Board.HireChance(Coach, ContextOf(t!), t!) * (overachieved ? 0.6 : 0.08);
            if (!rng.Chance(chance)) continue;
            v.Status = "offered"; v.Approach = true; v.DecideOn = Today.AddDays(7);
            offers.Add(t!);
        }
        var rest = openings.Where(x => x.v.Status == "open").Select(x => x.team!).ToList();
        if (offers.Count > 0)
            Post(offers.Count == 1 ? $"Job offer: {offers[0].FullName}" : $"{offers.Count} job offers",
                string.Join("\n", offers.Select(t => $"• {t.Owner.Name} wants you as head coach of {t.TheFullName} ({ContextOf(t).Name}).")) + "\n\n"
                + "Your season has been noticed. Accept or decline on the Board page before the new season starts; if you accept, you leave "
                + $"{HumanTeam.TheFullName} straight away." + (rest.Count > 0 ? $" {Count(rest.Count, "other job")} that would be a step up {(rest.Count == 1 ? "is" : "are")} open: apply there too." : ""), "Board");
        else if (rest.Count > 0)
            Post(rest.Count == 1 ? "A bigger job is open" : $"{rest.Count} bigger jobs are open",
                string.Join("\n", rest.Take(6).Select(t => $"• {t.FullName} ({ContextOf(t).Name})")) + (rest.Count > 6 ? $"\n• and {rest.Count - 6} more" : "") + "\n\n"
                + $"These clubs changed coach this summer and would be a step up from {HumanTeam.TheFullName}. Apply on the Board page without leaving your job; they answer during the summer.", "Board");
    }

    /// <summary>
    /// The career market while you're in work: applications are answered (a few days in the season, at the next step of
    /// the summer) and unanswered offers lapse. Openings that opened while you were in work close when the season starts.
    /// </summary>
    private void CareerMarketStep()
    {
        if (!Employed) return;
        bool summer = Phase == SessionPhase.Offseason;
        // The date stands still all summer, so each summer step gets its own salt (run 97, independent review of PR #158).
        var rng = CareerRng(summer ? 4 + 16 * (1 + (int)(Offseason?.Phase ?? 0)) : 4);
        foreach (var v in LiveVacancies.ToList())
        {
            if (FindTeam(v.TeamId) is not { } t) { v.Status = "filled"; continue; }
            if (v.Status == "applied" && (summer || v.DecideOn <= Today))
            {
                if (rng.Chance(Board.HireChance(Coach, ContextOf(t), t)))
                {
                    v.Status = "offered"; v.DecideOn = Today.AddDays(7);
                    Post($"Job offer: {t.FullName}", $"{t.Owner.Name} wants you as head coach of {t.TheFullName}. Accept or decline on the Board page"
                        + (summer ? " before the new season starts." : " within a week.") + $" If you accept, you leave {HumanTeam.TheFullName} straight away.", "Board");
                }
                else
                {
                    v.Status = "rejected";
                    t.CoachName = Successor(v, rng);
                    Post($"{t.Abbreviation} went another way", $"{t.FullName} have hired {t.CoachName} instead. You stay with {HumanTeam.TheFullName}.", "Board");
                }
            }
            else if (v.Status == "offered" && !summer && v.DecideOn <= Today) DeclineOffer(v.TeamId);
        }
    }

    /// <summary>
    /// H75 (bug hunt Nov 17, run 97): out of work in the summer. The daily market runs only on game days, and the summer
    /// has none, so applications sent in May waited until October. Now they're answered at each step of the summer, as
    /// they are in work, on the career path's own random numbers (the session's stream is untouched). Offers stand
    /// until the new season starts (<see cref="CarrySummerSearch"/>).
    /// </summary>
    private void SummerJobMarket()
    {
        // The date stands still all summer, so each step gets its own salt (independent review of PR #158).
        var rng = CareerRng(8 + 16 * (1 + (int)(Offseason?.Phase ?? 0)));
        foreach (var v in Job.Vacancies.Where(v => v.Status == "applied").ToList())
        {
            if (FindTeam(v.TeamId) is not { } t) { v.Status = "filled"; continue; }
            if (rng.Chance(HireChanceFor(t)))
            {
                v.Status = "offered"; v.DecideOn = Today.AddDays(7);
                Post($"Job offer: {t.FullName}", $"{t.Owner.Name} wants you as head coach of {t.TheFullName}. Accept or decline on the Board page before the new season starts.", "Board");
            }
            else
            {
                v.Status = "rejected";
                t.CoachName = Successor(v, rng);
                Post($"{t.Abbreviation} went another way", $"{t.FullName} have hired {t.CoachName} instead.", "Board");
            }
        }
    }

    /// <summary>
    /// Out of work when the new season starts: the date jumps from the end of the summer to the preseason, so the
    /// summer's offers get a week from today to answer and the jobs still open stay open a week more (they'd otherwise
    /// all close on the first day, dated from May).
    /// </summary>
    private void CarrySummerSearch()
    {
        foreach (var v in LiveVacancies)
        {
            if (v.Status == "offered") v.DecideOn = Today.AddDays(7);
            else if (v.Status == "open" && Today.DayNumber - v.Opened.DayNumber > 14) v.Opened = Today.AddDays(-14);
        }
    }

    /// <summary>The new season starts: openings from the summer that you didn't take are filled.</summary>
    private void CloseSummerOpenings()
    {
        var rng = CareerRng(5);
        foreach (var v in LiveVacancies.Where(v => v.WhileEmployed).ToList())
        {
            v.Status = "filled";
            if (FindTeam(v.TeamId) is { } t && t != HumanTeam) t.CoachName = Successor(v, rng);
        }
    }

    private static string Successor(Vacancy v, Rng rng) => v.NextCoach is { Length: > 0 } n ? n : Board.NewCoachName(rng);

    /// <summary>Whether the last season beat the board's expectations (the goal, or the win target by four or more over 82 games).</summary>
    private bool Overachieved(Expectations e, SeasonGoal achieved, int wins, int losses, LeagueContext ctx, bool relegated = false, bool goingUp = false)
    {
        // H70 (run 114): the board's own judgement of the season (Board.Judge), so an offer never follows a review that read
        // "expected more" for the same reasons, and going up counts.
        // CS2 (run 126): the seed counts in college, as in the review.
        var j = InCollege ? Board.Judge(e, ctx, achieved, wins, losses, relegated, goingUp, World.College!.SeedOf(HumanTeam.Id), World.College!.MadnessReached(HumanTeam.Id))
            : Board.Judge(e, ctx, achieved, wins, losses, relegated, goingUp);
        return j.Steps >= 1 || j.WinsVsTarget >= 4 || (j.GoalMet && j.WinsVsTarget >= 0);
    }

    /// <summary>Leave your club for a new one (an accepted offer while in work).</summary>
    private void MoveTo(Team team)
    {
        var club = HumanTeam;
        Coach.History.Add(JobRecordFor(club, $"Left for {team.FullName}"));
        string successor = Board.NewCoachName(CareerRng(6));
        ReleaseClub(successor);
        PostWorld($"{club.Abbreviation} change coach", $"{Coach.Name} has left {club.TheFullName} for {team.TheFullName}; {successor} takes over.", college: IsCollegeProgram(club));
    }
}
