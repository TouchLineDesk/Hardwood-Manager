using Hardwood.Core.College;
using Hardwood.Core.Model;

namespace Hardwood.Core.Season;

/// <summary>Why a senior is (or isn't) leaning your way: each part of his interest, and what it takes to commit.</summary>
public sealed record RecruitInterest(double Appeal, double Minutes, double Effort, double Total, double Need, string? Blocked)
{
    public bool Enough => Blocked == null && Total >= Need;
}

/// <summary>Senior recruiting (decision #157): court college seniors so they sign for you when they graduate.</summary>
public sealed partial class GameSession
{
    /// <summary>The date that decides who's a senior: players 23 by July 1 graduate this summer (they turn pro when the NABL season ends).</summary>
    public DateOnly GraduationDay => new(World.College?.Season.Year + 1 ?? Today.Year, 7, 1);

    /// <summary>Recruiting runs from the college season's start in November until the NABL season ends, while you have a club.</summary>
    public bool RecruitingOpen => World.College is { } c && Employed && Phase != SessionPhase.Offseason && Today >= new DateOnly(c.Season.Year, 11, 1) && Today < GraduationDay;

    public RecruitingBoard? Recruiting => World.College?.Season.Recruiting;

    /// <summary>This season's seniors, best first: the players who turn pro this summer.</summary>
    public List<Player> Seniors() =>
        World.College is not { } c ? new() : c.Players.Where(p => p.Age(GraduationDay) >= CollegeWorld.GraduateAge).OrderByDescending(p => p.CurrentAbility).ToList();

    /// <summary>His interest in your club, part by part (weights: club pull 45%, playing time 25%, plus your effort).</summary>
    public RecruitInterest InterestIn(Player p)
    {
        var board = Recruiting;
        var st = board?.Find(p.Id);
        double effort = st != null && st.TeamId == HumanTeam.Id ? st.Effort : 0;

        double winPct = MyRecord.Games > 0 ? MyRecord.WinPct : 0.5;
        double appeal = Math.Clamp((InNabl ? 60 : 40) + (winPct - 0.5) * 40 + (Coach.Reputation - 50) * 0.1, 5, 95);
        int ahead = HumanTeam.Roster.Count(x => x.PrimaryPosition == p.PrimaryPosition && x.CurrentAbility >= p.CurrentAbility);
        double minutes = Math.Clamp(100 - 30 * ahead, 10, 100);
        double total = Math.Clamp(appeal * 0.45 + minutes * 0.25 + effort, 0, 100);

        int rank = Seniors().IndexOf(p);
        double need = 55 + (rank is >= 0 and < 10 ? 20 : rank is >= 0 and < 30 ? 10 : 0);
        string? blocked = null;
        if (!InNabl && HumanLeague is { } lg && World.Levels.TryGetValue(lg.Key, out var level) && p.CurrentAbility - level > 22)
            blocked = $"He's aiming higher than the {lg.Profile.Name}.";
        if (st?.CommittedTeamId is { } other && other != HumanTeam.Id)
            blocked = $"He has committed to {FindTeam(other)?.FullName ?? "another club"}.";
        return new RecruitInterest(appeal, minutes, effort, total, need, blocked);
    }

    private RecruitState? Mine(Player p, out string? why)
    {
        why = null;
        if (!RecruitingOpen) { why = "Recruiting is closed: it runs from November until the season ends and the seniors turn pro."; return null; }
        if (!Seniors().Contains(p)) { why = $"{p.Name} isn't a senior this season."; return null; }
        var st = Recruiting!.For(p.Id);
        if (st.TeamId != HumanTeam.Id) { st.TeamId = HumanTeam.Id; st.Effort = 0; st.Calls = 0; st.Visits = 0; st.OfferWage = 0; st.OfferYears = 0; }
        if (st.CommittedTeamId is { } t) { why = t == HumanTeam.Id ? $"{p.Name} has already committed to you." : $"{p.Name} has committed to {FindTeam(t)?.FullName ?? "another club"}."; return null; }
        return st;
    }

    /// <summary>Top up the monthly recruiting points: 6 plus your scout's eye (up to +5), capped at 20.</summary>
    public void TopUpRecruitingPoints()
    {
        if (Recruiting is not { } b) return;
        int month = Today.Year * 12 + Today.Month;
        if (b.PointsMonth == month) return;
        b.PointsMonth = month;
        int scout = HumanTeam.Staff.FirstOrDefault(s => s.Role == StaffRole.Scout)?.Scouting ?? 5;
        b.Points = Math.Min(RecruitingBoard.PointsCap, b.Points + 6 + scout / 4);
    }

    public string Shortlist(Player p)
    {
        if (Mine(p, out var why) is not { } st) return why!;
        st.Shortlisted = !st.Shortlisted;
        return st.Shortlisted ? $"{p.Name} is on your recruiting list." : $"{p.Name} is off your recruiting list.";
    }

    /// <summary>A phone call: a little goodwill (less each time). Costs a point.</summary>
    public string CallRecruit(Player p)
    {
        if (Mine(p, out var why) is not { } st) return why!;
        TopUpRecruitingPoints();
        if (Recruiting!.Points < RecruitingBoard.CallCost) return "No recruiting points left this month.";
        Recruiting.Points -= RecruitingBoard.CallCost;
        double gain = 6 / (1 + st.Calls * 0.4);
        st.Calls++; st.Effort += gain; st.Shortlisted = true;
        return $"You spoke to {p.Name}. Interest +{gain:0}.";
    }

    /// <summary>A campus visit: a big step (twice at most). Costs three points.</summary>
    public string VisitRecruit(Player p)
    {
        if (Mine(p, out var why) is not { } st) return why!;
        if (st.Visits >= RecruitingBoard.MaxVisits) return $"You've visited {p.Name} twice already.";
        TopUpRecruitingPoints();
        if (Recruiting!.Points < RecruitingBoard.VisitCost) return $"A visit costs {RecruitingBoard.VisitCost} recruiting points; you have {Recruiting.Points}.";
        Recruiting.Points -= RecruitingBoard.VisitCost;
        st.Visits++; st.Effort += 15; st.Shortlisted = true;
        return $"You visited {p.Name} at {World.College!.Program(p.TeamId ?? 0)?.FullName ?? "college"}. Interest +15.";
    }

    /// <summary>What he'd ask of your club as a first contract.</summary>
    public ContractDemand RecruitDemand(Player p) =>
        ContractTalks.Demand(p, HumanTeam, ContractDate, MySalaryScale, League.Finance, renewal: false);

    /// <summary>
    /// Offer terms for when he graduates. He commits now if his interest is high enough and the money is right (his ask,
    /// or 90% of it when he's keen); otherwise the offer stands and he'll keep thinking it over each week.
    /// </summary>
    public string OfferRecruit(Player p, long wage, int years)
    {
        if (Mine(p, out var why) is not { } st) return why!;
        years = Math.Clamp(years, 1, 5);
        if (MyCommittedWages + wage > HumanTeam.Money.WageBudget)
            return $"The owner won't sanction it: that would take wages to ${(MyCommittedWages + wage) / 1e6:F1}M against a ${HumanTeam.Money.WageBudget / 1e6:F1}M budget.";
        st.OfferWage = wage; st.OfferYears = years; st.Shortlisted = true;
        return TryCommit(p, st) ?? $"{p.Name} has your offer (${wage / 1e6:F2}M for {years} year{(years == 1 ? "" : "s")}). {Lean(p)}";
    }

    /// <summary>The line the recruiting panel shows: what it would take.</summary>
    public string Lean(Player p)
    {
        var i = InterestIn(p);
        if (i.Blocked != null) return i.Blocked;
        var st = Recruiting?.Find(p.Id);
        if (st?.CommittedTeamId == HumanTeam.Id) return $"Committed to you: he signs for you when the season ends, before free agency opens.";
        if (i.Total < i.Need) return $"Interest {i.Total:0} of the {i.Need:0} he needs before he'll commit.";
        long ask = RecruitDemand(p).Wage;
        long floor = (long)(ask * (i.Total >= i.Need + 15 ? 0.9 : 1.0));
        return st is { OfferWage: > 0 } && st.OfferWage < floor
            ? $"Interested enough, but he wants at least ${floor / 1e6:F2}M a year."
            : $"Interested enough: offer at least ${floor / 1e6:F2}M a year and he'll commit.";
    }

    private string? TryCommit(Player p, RecruitState st)
    {
        var i = InterestIn(p);
        if (!i.Enough || st.OfferWage <= 0) return null;
        long ask = RecruitDemand(p).Wage;
        if (st.OfferWage < (long)(ask * (i.Total >= i.Need + 15 ? 0.9 : 1.0))) return null;
        st.CommittedTeamId = HumanTeam.Id; st.CommittedOn = Today;
        string text = $"{p.Name} ({p.PrimaryPosition}, {World.College!.Program(p.TeamId ?? 0)?.FullName}) commits to the {HumanTeam.FullName}. He signs for you when the season ends: ${st.OfferWage / 1e6:F2}M a year for {st.OfferYears} year{(st.OfferYears == 1 ? "" : "s")}.";
        Post($"{p.Name} commits to you", text, "Roster");
        return text;
    }

    /// <summary>
    /// Each Monday in the recruiting window: your effort fades a little, keen seniors with your offer commit, and rival
    /// NABL clubs land some of the best seniors (the better the senior, the sooner).
    /// </summary>
    internal void WeeklyRecruiting()
    {
        if (World.College is not { } c || !RecruitingOpen || Recruiting is not { } board) return;
        bool first = board.PointsMonth == 0;
        TopUpRecruitingPoints();
        if (first)
            Post("Senior recruiting is open",
                $"This season's {Seniors().Count} college seniors turn pro when the season ends. Court the ones you want on the College page (Recruiting): calls and visits build interest, and a senior who commits to you signs before free agency opens. Rival clubs are recruiting too; the best seniors go early.", "Roster");
        var seniors = Seniors();
        foreach (var p in seniors)
        {
            var st = board.Find(p.Id);
            if (st is { CommittedTeamId: null } && st.TeamId == HumanTeam.Id)
            {
                st.Effort *= 0.97;
                if (TryCommit(p, st) != null) continue;
            }
        }
        // Rivals: about two-thirds of the top 40 seniors are committed somewhere by graduation; fewer further down.
        var rivals = League.Teams.Where(t => t != HumanTeam).ToList();
        if (rivals.Count == 0) return;
        for (int rank = 0; rank < Math.Min(80, seniors.Count); rank++)
        {
            var p = seniors[rank];
            var st = board.Find(p.Id);
            if (st?.CommittedTeamId != null) continue;
            double weekly = rank < 10 ? 0.045 : rank < 40 ? 0.03 : 0.012;
            // Your courting slows rivals down: a senior who's nearly sold on you takes longer to say yes elsewhere.
            if (st != null && st.TeamId == HumanTeam.Id) weekly *= Math.Clamp(1 - InterestIn(p).Total / 120, 0.2, 1);
            if (!_rng.Chance(weekly)) continue;
            var team = rivals.OrderByDescending(t => (MyStandings.Has(t) ? MyStandings[t].WinPct : 0.5) + _rng.NextDouble() * 0.6).First();
            st ??= board.For(p.Id);
            st.CommittedTeamId = team.Id; st.CommittedOn = Today;
            if (st.Shortlisted && st.TeamId == HumanTeam.Id)
                Post($"{p.Name} commits to {team.Abbreviation}", $"{p.Name} ({p.PrimaryPosition}, {c.Program(p.TeamId ?? 0)?.FullName}), on your recruiting list, has committed to the {team.FullName}. He'll sign for them when he graduates.", "Roster");
        }
    }

    /// <summary>Graduation day: committed seniors sign. Yours sign on the agreed terms if the budget and roster allow; rivals' sign with them.</summary>
    internal void SignCommittedGraduates(RecruitingBoard? board)
    {
        if (board == null) return;
        foreach (var (pid, st) in board.Prospects.Where(kv => kv.Value.CommittedTeamId != null).ToList())
        {
            var p = League.FreeAgents.FirstOrDefault(x => x.Id == pid);
            if (p == null) continue;
            if (st.CommittedTeamId == HumanTeam.Id)
            {
                // Players whose contracts are ending don't count against the roster: you decide on them this summer.
                if (!Employed || HumanTeam.Roster.Count(x => !MyExpiring.Contains(x)) >= League.Finance.RosterMax || MyCommittedWages + st.OfferWage > HumanTeam.Money.WageBudget)
                {
                    Post($"{p.Name} is a free agent", $"{p.Name} committed to you, but the deal couldn't go through when he turned pro (roster full or over the wage budget). He's in the free-agent pool.", "Roster");
                    continue;
                }
                Sign(p, st.OfferWage, Math.Max(1, st.OfferYears));
            }
            else if (FindTeam(st.CommittedTeamId!.Value) is { } t && League.Teams.Contains(t) && Offseason != null)
            {
                // If his club can't fit him under the cap or its budget after all, another rival that can steps in; failing that he's a free agent.
                if (Offseason.AiSignIfAffordable(t, p)) continue;
                foreach (var other in League.Teams.Where(x => x != HumanTeam && x != t).OrderByDescending(x => MyStandings.Has(x) ? MyStandings[x].WinPct : 0.5))
                    if (Offseason.AiSignIfAffordable(other, p)) { st.CommittedTeamId = other.Id; break; }
            }
        }
    }
}
