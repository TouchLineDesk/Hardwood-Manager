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
    /// <summary>What the staff did last (shown on the Staff page).</summary>
    public string LastTraining { get; set; } = "";
    public string LastRecruiting { get; set; } = "";
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
        if (!Delegated.Recruiting || !RecruitingOpen || Recruiting is not { } board) return;
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
