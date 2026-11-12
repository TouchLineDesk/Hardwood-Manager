using Hardwood.Core.Model;

namespace Hardwood.Core.Season;

/// <summary>A staff candidate's answer in an interview: will he come, and on what terms.</summary>
public sealed record StaffAnswer(bool Willing, long Wage, int Years, string Text);

/// <summary>What you hand to your staff (roadmap 12, delegation). Saved with the league; older saves delegate nothing.</summary>
public sealed class Delegation
{
    /// <summary>The development coach sets each player's training focus and the intensity every week.</summary>
    public bool Training { get; set; }
    /// <summary>The head scout spends the recruiting points courting the best seniors who'd play for you.</summary>
    public bool Recruiting { get; set; }
    /// <summary>The assistant coach fronts the press and handles the locker room (staff part 2), in his own way.</summary>
    public bool Media { get; set; }
    /// <summary>The general manager decides the summer's facility projects within the owner's budget (item M, #281; D10).</summary>
    public bool Facilities { get; set; }
    /// <summary>What the staff did last (shown on the Staff page).</summary>
    public string LastTraining { get; set; } = "";
    public string LastRecruiting { get; set; } = "";
    public string LastMedia { get; set; } = "";
}

/// <summary>Staff depth (roadmap 12): interviews, staff styles in action, development projections and delegation.</summary>
public sealed partial class GameSession
{
    /// <summary>How strong a name he is (23-80), against the club's pull (prestige plus your reputation).</summary>
    private static double Standing(Staff st) => st.Reputation * 3 + 20;
    private double ClubPull => Jobs.Board.Prestige(ContextOf(HumanTeam), HumanTeam) + Coach.Reputation * 0.2;

    /// <summary>
    /// Interview a candidate. Big names want a big club: if the club's pull is well short of his standing he turns you
    /// down; otherwise he names his terms (more than his usual wage when he's stepping down to join you).
    /// </summary>
    public StaffAnswer Interview(Staff st)
    {
        double gap = Standing(st) - ClubPull;
        long baseWage = StaffPayFor(st);
        int years = st.Age(Today) < 50 ? 3 : 2;
        if (gap > 12)
            return new StaffAnswer(false, baseWage, years,
                $"{st.Name} thanks you for your time, but he's after a bigger club (his standing {Standing(st):0}, your club's pull {ClubPull:0}). Win more, or grow your reputation, and ask again.");
        long wage = (long)(baseWage * (1 + Math.Max(0, gap) / 60.0) / 1000) * 1000;
        string style = $"He's a {st.Style.ToLower()}: {st.StyleText}";
        return new StaffAnswer(true, wage, years,
            $"{st.Name} would join as {Staff.RoleName(st.Role).ToLower()} for ${wage / 1e6:F2}M a year over {years} years. {style}");
    }

    /// <summary>Hire on the terms he gave in the interview.</summary>
    public string HireOnTerms(Staff st)
    {
        var a = Interview(st);
        if (!a.Willing) return a.Text;
        if (!HireStaff(st)) return $"{st.Name} is no longer available.";
        st.Salary = a.Wage; st.ContractYears = a.Years;
        SaveNow();
        return $"{st.Name} has joined: ${a.Wage / 1e6:F2}M a year for {a.Years} years.";
    }

    /// <summary>
    /// Extending a staff member whose contract ends this summer (staff part 2). He asks his going rate, a little more if he's
    /// outgrown the club; a big name at a small club with a poor season may want to move on.
    /// </summary>
    public StaffAnswer StaffRenewal(Staff st)
    {
        if (st.TeamId != HumanTeam.Id) return new StaffAnswer(false, 0, 0, $"{st.Name} doesn't work for you.");
        if (st.ContractYears > 1) return new StaffAnswer(false, st.Salary, st.ContractYears, $"{st.Name} has {st.ContractYears} years left; talk again in his final year.");
        double gap = Standing(st) - ClubPull;
        double winPct = MyRecord.Games > 0 ? MyRecord.WinPct : 0.5;
        if (gap > 18 && winPct < 0.45)
            return new StaffAnswer(false, st.Salary, 0, $"{st.Name} wants to see out his contract and move to a bigger club this summer.");
        long wage = (long)(Math.Max(st.Salary, StaffPayFor(st)) * (1 + Math.Max(0, gap) / 80.0) / 1000) * 1000;
        int years = st.Age(Today) >= 60 ? 1 : st.Age(Today) >= 52 ? 2 : 3;
        return new StaffAnswer(true, wage, years, $"{st.Name} would sign on for {years} more year{(years == 1 ? "" : "s")} at ${wage / 1e6:F2}M a year.");
    }

    /// <summary>Extend on the terms he asked for.</summary>
    public string ExtendStaff(Staff st)
    {
        var a = StaffRenewal(st);
        if (!a.Willing) return a.Text;
        st.Salary = a.Wage;
        st.ContractYears += a.Years;
        Post($"{st.Name} extends", $"{st.Name} ({Staff.RoleName(st.Role).ToLower()}) has signed on until {League.Season + st.ContractYears}: ${a.Wage / 1e6:F2}M a year.", "Staff");
        SaveNow();
        return $"Done: {st.Name} stays {a.Years} more year{(a.Years == 1 ? "" : "s")} at ${a.Wage / 1e6:F2}M.";
    }

    /// <summary>Your staff whose contracts end this summer (they leave unless extended).</summary>
    public List<Staff> StaffExpiring => Employed ? HumanTeam.Staff.Where(x => x.ContractYears <= 1).ToList() : new();

    /// <summary>
    /// The assistant coach's answer to a press question or locker-room moment, by his style: a Mentor protects the squad's
    /// spirit, a Film junkie keeps it short and safe.
    /// </summary>
    internal int DelegatedAnswer(People.PendingDecision d)
    {
        var asst = HumanTeam.Staff.FirstOrDefault(x => x.Role == StaffRole.AssistantCoach);
        if (asst?.Is("Mentor") == true)
        {
            int spirit = d.Options.FindIndex(o => o.Hint.Contains("pirit up"));
            if (spirit >= 0) return spirit;
        }
        return d.SafeOption;
    }

    /// <summary>
    /// A season on the job teaches you something (staff part 2): one point in what you did most, up to two a season, never
    /// past 20. Fronting the press yourself builds Motivation, one-on-one talks Man management, a winning season Tactics,
    /// planning your own training with young players Development.
    /// </summary>
    internal void CoachGrowth()
    {
        if (!Employed) { Coach.SeasonTalks = 0; Coach.SeasonAnswers = 0; return; }
        var rec = MyRecord;
        var young = HumanTeam.Roster.Count(p => p.Age(Today) <= 23);
        var reasons = new List<(Jobs.CoachAttr attr, int weight, string why)>();
        if (Coach.SeasonAnswers >= 5) reasons.Add((Jobs.CoachAttr.Motivation, Coach.SeasonAnswers, $"you fronted {Coach.SeasonAnswers} press conferences and locker-room moments yourself"));
        if (Coach.SeasonTalks >= 6) reasons.Add((Jobs.CoachAttr.ManManagement, Coach.SeasonTalks, $"you held {Coach.SeasonTalks} one-on-one talks"));
        if (rec.Games >= 20 && rec.WinPct >= 0.6) reasons.Add((Jobs.CoachAttr.Tactics, (int)(rec.WinPct * 20), $"a {rec.Wins}-{rec.Losses} season"));
        if (!Delegated.Training && young >= 3) reasons.Add((Jobs.CoachAttr.Development, young * 2, $"you planned training yourself for {young} young players"));
        var grown = new List<string>();
        foreach (var (attr, _, why) in reasons.OrderByDescending(r => r.weight))
        {
            if (grown.Count >= 2) break;
            int now = Coach[attr];
            if (now >= Jobs.Coach.MaxAttr) continue;
            Coach.Attributes[attr] = now + 1;
            grown.Add($"{Jobs.Coach.Label(attr)} {now} → {now + 1} ({why})");
        }
        Coach.SeasonTalks = 0; Coach.SeasonAnswers = 0;
        if (grown.Count > 0)
            Post("You've grown as a coach", "A season's work shows:\n" + string.Join("\n", grown) + "\n\nYour ratings are on the Staff & Training page.", "Staff");
    }

    /// <summary>
    /// A player's development heading into the summer, before luck: the expected CA change at his current minutes, and how much
    /// of it your staff add (against average training, quality 10).
    /// </summary>
    public (double Change, double FromStaff) Projection(Player p)
    {
        var summer = new DateOnly(League.Season + 1, 7, 1);
        int age = p.Age(summer);
        double mpg = MyStats.TryGetValue(p.Id, out var s) ? s.Mpg : 0;
        double q = StaffEffects.TrainingQualityFor(HumanTeam, p, summer);
        var (g, d) = DevelopmentEngine.Expected(p, age, mpg, q);
        var (g0, d0) = DevelopmentEngine.Expected(p, age, mpg, 10);
        double change = Math.Min(g - d, p.PotentialAbility - p.CurrentAbility);
        return (change, (g - d) - (g0 - d0));
    }

    public Delegation Delegated => League.Delegation;

    /// <summary>The development coach's weekly plan: focus each young player on his weakest area for his position, ease off when injuries pile up.</summary>
    internal void DelegatedTraining()
    {
        if (!Delegated.Training || !Employed) return;
        var t = HumanTeam;
        int hurt = t.Roster.Count(p => !p.IsAvailable(Today));
        t.Training.Intensity = hurt >= 3 ? 35 : hurt == 2 ? 45 : 60;
        int changed = 0;
        foreach (var p in t.Roster)
        {
            bool developing = p.Age(Today) <= 25 && p.PotentialAbility - p.CurrentAbility >= 5;
            AttrGroup? want = developing ? Weakest(p) : null;
            t.Training.Focus.TryGetValue(p.Id, out var have);
            bool has = t.Training.Focus.ContainsKey(p.Id);
            if (want is { } w) { if (!has || have != w) { t.Training.Focus[p.Id] = w; changed++; } }
            else if (has) { t.Training.Focus.Remove(p.Id); changed++; }
        }
        var dev = t.Staff.FirstOrDefault(s => s.Role == StaffRole.DevelopmentCoach);
        Delegated.LastTraining = $"{Today:MMM d}: {(dev?.Name ?? "Your staff")} set intensity to {t.Training.Intensity} ({hurt} injured)" + (changed > 0 ? $" and changed {changed} player focus{(changed == 1 ? "" : "es")}." : ".");
    }

    /// <summary>Hand training over now (the Staff page switch) rather than waiting for Monday.</summary>
    public void DelegateTrainingNow() => DelegatedTraining();

    /// <summary>The area a player most needs for his position: the lowest of the groups that matter for it.</summary>
    public static AttrGroup Weakest(Player p)
    {
        var groups = p.PrimaryPosition switch
        {
            Position.PG => new[] { AttrGroup.Playmaking, AttrGroup.Shooting, AttrGroup.Defense, AttrGroup.Mental },
            Position.SG => new[] { AttrGroup.Shooting, AttrGroup.Defense, AttrGroup.Physical, AttrGroup.Mental },
            Position.SF => new[] { AttrGroup.Shooting, AttrGroup.Defense, AttrGroup.Physical, AttrGroup.Mental },
            _ => new[] { AttrGroup.Defense, AttrGroup.Physical, AttrGroup.Mental, AttrGroup.Shooting },
        };
        return groups.OrderBy(g => GroupAverage(p, g)).First();
    }

    private static double GroupAverage(Player p, AttrGroup g) =>
        AttrInfo.All.Where(a => AttrInfo.GroupOf(a) == g).Select(a => (double)p.Attributes[a]).DefaultIfEmpty(10).Average();

    /// <summary>The head scout's weekly round: court the three best seniors who'd get minutes here, visits first, then calls.</summary>
    internal void DelegatedRecruiting()
    {
        if (!Delegated.Recruiting || InCollege || !RecruitingOpen || Recruiting is not { } board) return;
        TopUpRecruitingPoints();
        var targets = Seniors().Take(80)
            .Where(p => board.Find(p.Id)?.CommittedTeamId == null && InterestIn(p) is { Blocked: null, Minutes: >= 70 })
            .Take(3).ToList();
        var names = new List<string>();
        foreach (var p in targets)
        {
            var st = board.Find(p.Id);
            string r = st == null || st.Visits < Hardwood.Core.College.RecruitingBoard.MaxVisits ? VisitRecruit(p) : CallRecruit(p);
            if (r.StartsWith("You ")) names.Add(p.Name);
        }
        var scout = HumanTeam.Staff.FirstOrDefault(s => s.Role == StaffRole.Scout);
        if (names.Count > 0)
            Delegated.LastRecruiting = $"{Today:MMM d}: {(scout?.Name ?? "Your scout")} courted {string.Join(", ", names)}. Offers are yours to make.";
    }
}
