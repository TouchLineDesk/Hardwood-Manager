using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hardwood.Core.Jobs;
using Hardwood.Core.Model;
using Hardwood.Desktop.Controls;

namespace Hardwood.Desktop.ViewModels;

public sealed class DemandRow
{
    public string Text { get; init; } = "";
    public bool Met { get; init; }
    public string Icon => Met ? "Smile" : "TriangleAlert";
    public IBrush Brush => new SolidColorBrush(Color.Parse(Met ? "#3FB950" : "#D29922"));
}

public sealed class CoachAttrView
{
    public string Label { get; init; } = "";
    public int Value { get; init; }
}

public sealed class VacancyRow
{
    public required Team Team { get; init; }
    public string Name => Team.FullName;
    public string Detail { get; init; } = "";
    public string Status { get; init; } = "";
    public bool CanApply { get; init; }
    public bool HasOffer { get; init; }
    public required IRelayCommand Apply { get; init; }
    public required IRelayCommand Accept { get; init; }
    public required IRelayCommand Decline { get; init; }
}

/// <summary>Item M (#281): one of your club's facilities.</summary>
public sealed class FacilityRow
{
    public string Name { get; init; } = "";
    public string Pitch { get; init; } = "";
    public int Level { get; init; }
    public string LevelText { get; init; } = "";
    public string Effect { get; init; } = "";
    public string Status { get; init; } = "";
    public IBrush StatusBrush { get; init; } = Brushes.Gray;
}

/// <summary>CS7 part 2c (run 175): one of your players' grades this term.</summary>
public sealed class AcademicRow
{
    public string Name { get; init; } = "";
    public string Detail { get; init; } = "";
    public string Standing { get; init; } = "";
    public IBrush StandingBrush { get; init; } = Brushes.Gray;
    public string ButtonText { get; init; } = "";
    public required IRelayCommand Toggle { get; init; }
}

/// <summary>CS9 (run 191): one change on the college coaching carousel.</summary>
public sealed class CarouselRow
{
    public required Team Team { get; init; }
    public string Name => Team.FullName;
    public string Detail { get; init; } = "";
    public string Tag { get; init; } = "";
    public bool HasTag => Tag.Length > 0;
}

/// <summary>Item M (#281): a project the front office proposes.</summary>
public sealed class ProposalRow
{
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
    public required IRelayCommand Approve { get; init; }
    public required IRelayCommand Decline { get; init; }
}

/// <summary>The Board page (decisions #70-74): owner, expectations, board confidence, your coach profile, and jobs when out of work.</summary>
public sealed partial class BoardViewModel : PageViewModel
{
    /// <summary>The club's money (decision #88).</summary>
    [ObservableProperty] private string _finances = "";
    /// <summary>Roadmap 1: club finances as stat chips instead of one long sentence.</summary>
    [ObservableProperty] private List<Hardwood.Desktop.Controls.Chip> _financeChips = new();
    public Avalonia.Media.Color Accent => ThemeService.AccentFor(Session.HumanTeam);
    public override string Title => "Board";
    public BoardViewModel(MainViewModel shell) : base(shell) { }

    [ObservableProperty] private bool _employed;
    [ObservableProperty] private Team? _team;
    [ObservableProperty] private string _ownerName = "";
    /// <summary>"The owner", or "Athletic director" at a college program (C2).</summary>
    [ObservableProperty] private string _ownerTitle = "The owner";
    [ObservableProperty] private string _financeTitle = "Club finances";
    [ObservableProperty] private string _confidenceTitle = "Board confidence";
    [ObservableProperty] private string _financeNote = "Revenue comes from the league, the arena, the fans and results. The owner sets the wage budget from it each summer: ambitious owners spend more, careful ones keep a margin.";
    [ObservableProperty] private string _ownerStyle = "";
    /// <summary>CS2 (run 126): the money icon for a club, a graduation cap for a college program (no money lines in college).</summary>
    [ObservableProperty] private string _financeIcon = "DollarSign";
    /// <summary>CS2 (run 126): how confidence moves, in the boss's words (the athletic director and March in college).</summary>
    [ObservableProperty] private string _confidenceNote = ProConfidenceNote;
    public const string ProConfidenceNote = "Confidence follows your pace against the target each week (faster with impatient owners), and the owner's demands. After the playoffs the board reviews the season against the goal.";
    public const string CollegeConfidenceNote = "Confidence follows your regular-season pace against the target each week (faster with an impatient athletic director). After March Madness the AD reviews the season against the goal and your seed: a run past your seed counts for a lot; conference tournament and March games don't count toward the win target.";
    [ObservableProperty] private int _patience;
    [ObservableProperty] private int _ambition;
    [ObservableProperty] private string _patienceText = "";
    [ObservableProperty] private string _ambitionText = "";
    [ObservableProperty] private string _goal = "";
    [ObservableProperty] private string _pace = "";
    public ObservableCollection<DemandRow> Demands { get; } = new();
    [ObservableProperty] private double _confidence;
    [ObservableProperty] private string _confidenceText = "";
    [ObservableProperty] private IBrush _confidenceBrush = Brushes.Gray;
    [ObservableProperty] private IReadOnlyList<LineSeries>? _confidenceTrend;
    [ObservableProperty] private bool _hasTrend;
    /// <summary>Roadmap 4, the assistant explains why: what's pushing confidence up or down this week, and where it's heading.</summary>
    [ObservableProperty] private List<Hardwood.Desktop.Controls.Chip> _confidenceDrivers = new();
    [ObservableProperty] private string _confidenceHeading = "";
    [ObservableProperty] private string _confidenceRecent = "";

    [ObservableProperty] private string _coachName = "";
    [ObservableProperty] private string _coachBio = "";
    [ObservableProperty] private double _reputation;
    public ObservableCollection<CoachAttrView> CoachAttrs { get; } = new();
    [ObservableProperty] private string _jobHistory = "";

    public ObservableCollection<VacancyRow> Vacancies { get; } = new();

    // CS9 (run 191): the college coaching carousel, the newest spring.
    public ObservableCollection<CarouselRow> CarouselRows { get; } = new();
    [ObservableProperty] private bool _hasCarousel;
    [ObservableProperty] private string _carouselTitle = "";
    [ObservableProperty] private string _carouselNote = "";
    [ObservableProperty] private bool _hasCarouselMore;
    [ObservableProperty] private bool _carouselAll;
    [ObservableProperty] private string _carouselToggle = "";
    /// <summary>The carousel panel shows 10 changes until you ask for all.</summary>
    public const int CarouselShown = 10;
    /// <summary>Slice 9 (run 200): the head coaches you face, your rivals' first, then your conference's.</summary>
    public ObservableCollection<CarouselRow> RivalCoachRows { get; } = new();
    [ObservableProperty] private bool _hasRivalCoaches;
    [ObservableProperty] private string _rivalCoachesNote = "";
    /// <summary>Slice 10 (run 201): your college coaching career: the record, your seasons, milestones and coaching tree.</summary>
    [ObservableProperty] private bool _hasMyCareer;
    [ObservableProperty] private string _myCareerHeadline = "";
    [ObservableProperty] private string _myCareerNote = "";
    public ObservableCollection<string> MyCareerSeasons { get; } = new();
    public ObservableCollection<string> MyCareerMilestones { get; } = new();
    public ObservableCollection<string> MyCareerTree { get; } = new();
    [ObservableProperty] private bool _hasMyMilestones;
    [ObservableProperty] private bool _hasMyTree;
    [CommunityToolkit.Mvvm.Input.RelayCommand] private void ToggleCarousel() { CarouselAll = !CarouselAll; RefreshCarousel(); }

    // Item M (#281): facilities.
    [ObservableProperty] private bool _hasFacilities;
    [ObservableProperty] private List<Hardwood.Desktop.Controls.Chip> _facilityChips = new();
    public ObservableCollection<FacilityRow> Facilities { get; } = new();
    public ObservableCollection<ProposalRow> Proposals { get; } = new();
    [ObservableProperty] private bool _hasProposals;
    [ObservableProperty] private string _proposalNote = "";
    [ObservableProperty] private string _facilityStatus = "";
    [ObservableProperty] private string _building = "";
    [ObservableProperty] private bool _hasBuilding;
    private bool _refreshing;
    [ObservableProperty] private bool _gmDecides;
    partial void OnGmDecidesChanged(bool value)
    {
        if (_refreshing) return;
        Session.DelegateFacilities(value);
        FacilityStatus = value ? "From next summer your general manager approves the projects the owner will fund." : "From next summer the proposals come to you.";
        Session.SaveNow();
    }
    [ObservableProperty] private string _status = "";

    // The career path (run 61, #651): jobs and offers while in work, and resigning.
    [ObservableProperty] private bool _hasVacancies;
    [ObservableProperty] private string _jobsTitle = "Out of work: open jobs";
    [ObservableProperty] private string _jobsNote = "";
    /// <summary>The jobs panel shows when you're out of work, or when there's an opening or offer for you.</summary>
    [ObservableProperty] private bool _showJobs;
    /// <summary>Resign asks once before it acts.</summary>
    [ObservableProperty] private bool _confirmingResign;
    [ObservableProperty] private string _resignNote = "";

    [RelayCommand]
    private void Resign()
    {
        if (!Session.Employed) return;
        if (!ConfirmingResign) { ConfirmingResign = true; return; }
        ConfirmingResign = false;
        Status = Session.Resign() ?? "You have resigned. Open jobs are listed below.";
        ThemeService.Apply(Session.HumanTeam);
        Shell.AfterTick();
        Refresh();
    }

    [RelayCommand]
    private void CancelResign() => ConfirmingResign = false;

    public static (string label, string colour) Mood(double c) => Board.Mood(c, false);

    /// <summary>H70 (run 123): "Critical" needs a losing record; before a ball is bounced, last season's record counts.</summary>
    public static bool WinningForMood(Hardwood.Core.Season.GameSession s) =>
        s.MyRecord is { Games: > 0 } r ? r.Wins >= r.Losses : s.Job.LastSeasonWinning ?? false;

    /// <summary>CS2: the college regular season is over once the conference tournaments have begun.</summary>
    private static bool RegularSeasonOver(Hardwood.Core.Season.GameSession s) =>
        s.World.College!.Season.Games.Any(g => !Hardwood.Core.College.CollegeWorld.IsRegularSeason(g));

    private static string LowerFirst(string x) => x.Length == 0 ? x : char.ToLowerInvariant(x[0]) + x[1..];

    /// <summary>
    /// H23: the season's line. In season, the pace over the regular season; once the regular season is over (or the record
    /// passes its length, as college tournament games do) the finished record against the target.
    /// </summary>
    public static string PaceLine(int wins, int losses, int seasonGames, int? target, bool seasonOver)
    {
        int games = wins + losses;
        if (games == 0) return seasonOver ? "No games played this season." : "The season hasn't started.";
        if (seasonOver || games >= seasonGames)
            return target is { } tw
                ? $"Finished {wins}-{losses} (target: around {tw} wins): {(wins >= tw ? "met" : $"missed by {tw - wins}")}"
                : $"Finished {wins}-{losses}";
        return $"{wins}-{losses}: on pace for {(double)wins / games * seasonGames:0} wins of {seasonGames}";
    }

    /// <summary>Hired this season after your club's regular season ended: its record isn't yours to be judged on.</summary>
    private static bool HiredAfterSeason(Hardwood.Core.Season.GameSession s)
    {
        var job = s.Job; var t = s.HumanTeam;
        if (job.HiredOn == default) return false;
        if (job.HiredSeason > s.League.Season) return true;   // H80 (run 105): a summer hire is hired for next season
        if (job.HiredSeason != s.League.Season) return false;
        // H180 (run 172): a college program's games are on the college season, not the pro schedule (the fallback below kept a
        // summer hire's own season "before you came" for a whole year).
        var mine = s.InCollege
            ? s.World.College!.GamesOf(t.Id).Where(g => g.Played).Select(g => g.Date).ToList()
            : s.Schedule.Where(g => g.Home == t || g.Away == t).Select(g => g.Date).ToList();
        // Review of run 172: a mid-season college hire has regular-season games still to play, so the season is his.
        if (s.InCollege && s.World.College!.GamesOf(t.Id).Any(g => !g.Played && Hardwood.Core.College.CollegeWorld.IsRegularSeason(g))) return false;
        if (mine.Count > 0) return job.HiredOn > mine.Max();
        if (s.InCollege) return false;   // no game played yet this season: it is all yours
        return job.HiredOn.Month is >= 5 and <= 9;   // a league whose games aren't on your schedule: after the spring
    }

    public override void Refresh()
    {
        var s = Session; var job = s.Job; var coach = s.Coach;
        Employed = s.Employed;
        Team = s.Employed ? s.HumanTeam : null;
        if (s.Employed)
        {
            var t = s.HumanTeam; var o = t.Owner; var rec = s.MyBoardRecord; var ctx = s.ContextOf(t);
            OwnerName = o.Name; OwnerStyle = o.Style; Patience = o.Patience; Ambition = o.Ambition;
            // H169 (run 172): the bars say what they're out of, and each has the word its style uses.
            PatienceText = $"{o.Patience} of 20 · {o.PatienceWord}"; AmbitionText = $"{o.Ambition} of 20 · {o.AmbitionWord}";
            var e = job.Current;
            Goal = e == null ? "" : $"{(e.Survive ? (e.SurviveNote is { } note ? $"Stay up: {note}" : "Stay up (newly promoted)") : Board.Describe(e.Goal, ctx))} · around {e.TargetWins} wins";
            bool over = s.Phase is Hardwood.Core.Season.SessionPhase.Postseason or Hardwood.Core.Season.SessionPhase.Offseason;
            bool hiredAfter = HiredAfterSeason(s) && (over || rec.Games >= ctx.SeasonGames);
            Pace = hiredAfter
                ? $"Last season, before you came: {rec.Wins}-{rec.Losses}"
                : PaceLine(rec.Wins, rec.Losses, ctx.SeasonGames, e?.TargetWins, over || (s.InCollege && RegularSeasonOver(s)));
            // CS2 (run 126): the target covers the regular season; say so in college.
            if (s.InCollege && rec.Games > 0 && !hiredAfter) Pace = "Regular season " + LowerFirst(Pace);
            Demands.Clear();
            if (e != null)
                foreach (var d in e.Demands)
                    Demands.Add(new DemandRow { Text = Board.Describe(d), Met = Board.Meeting(d, ctx, t, s.MyStats, s.Today, rec.Wins, rec.Games) });
            Confidence = job.Confidence;
            var (label, colour) = Board.Mood(job.Confidence, WinningForMood(s));
            ConfidenceText = $"{label} ({job.Confidence:0}/100)";
            ConfidenceBrush = new SolidColorBrush(Color.Parse(colour));
            var m = t.Money;
            static string M(long v) => Hardwood.Core.Season.Budgets.Money(v);   // H98 (run 109): one money format everywhere
            // Bug hunt H45 (run 84): in the summer the deals that are up (and leave unless renewed) aren't spent, the way
            // Transactions, Offseason and the inbox count them (GameSession.MyBudgetLine); they're named under the chip.
            var expiring = s.MyExpiring;
            long upWages = expiring.Sum(p => p.Contract?.Salary ?? 0);
            long wages = expiring.Count > 0 ? s.MyCommittedWages : Hardwood.Core.Season.ContractMarket.Payroll(t);
            string wagesNote = expiring.Count > 0
                ? $"committed of a {M(m.WageBudget)} budget (not counting {M(upWages)} of {expiring.Count} expiring)"
                : $"of a {M(m.WageBudget)} budget";
            Finances = $"Revenue {M(m.Revenue)} a season" + (m.Parachute > 0 ? $" + parachute {M(m.Parachute)}" : "") + $" · wage budget {M(m.WageBudget)} · wages {M(wages)} · "
                       + $"transfer budget {M(m.TransferBudget)} · bank balance {M(m.Balance)} · fan base {m.FanBase:0}/100";
            FinanceChips = new List<Hardwood.Desktop.Controls.Chip>
            {
                // D81 (run 169): revenue includes what your prices add or cost.
                new("Revenue", M(m.Revenue + Hardwood.Core.Season.Business.Change(m)), Hardwood.Core.Season.Business.Change(m) is var bc && bc != 0 ? $"a season · {(bc > 0 ? "+" : "-")}{M(Math.Abs(bc))} from your prices" : "a season", "team"),
                // H144 (run 229): a relegated club's parachute is its own line, so the bank's swing is explained.
                new("Parachute", M(m.Parachute), ParachuteNote(m), "team"),
                new("Wages", M(wages), wagesNote, wages > m.WageBudget ? "bad" : wages > m.WageBudget * 0.95 ? "warn" : "good"),
                new("Wage room", M(Math.Max(0, m.WageBudget - wages)), wages > m.WageBudget ? $"{M(wages - m.WageBudget)} over" : "to spend on wages", wages > m.WageBudget ? "bad" : ""),
                new("Transfer budget", M(m.TransferBudget), "for transfer fees"),
                new("Bank", M(m.Balance), m.Balance < 0 ? "in debt" : "balance", m.Balance < 0 ? "bad" : ""),
                new("Fan base", $"{m.FanBase:0}", "out of 100", m.FanBase >= 70 ? "good" : m.FanBase < 35 ? "warn" : ""),
            };
            if (m.Parachute <= 0) FinanceChips = FinanceChips.Where(c => c.Label != "Parachute").ToList();
            OwnerTitle = s.InCollege ? "Athletic director" : "The owner";
            FinanceTitle = s.InCollege ? "The program" : "Club finances";
            FinanceIcon = s.InCollege ? "GraduationCap" : "DollarSign";
            ConfidenceTitle = s.InCollege ? "AD confidence" : "Board confidence";
            ConfidenceNote = s.InCollege ? CollegeConfidenceNote : ProConfidenceNote;
            FinanceNote = s.InCollege
                ? "College players are paid nothing (the classic era): the program's pull is its prestige, its conference and its winning. The athletic director judges the season by March Madness."
                : "Revenue comes from the league, the arena, the fans and results. The owner sets the wage budget from it each summer: ambitious owners spend more, careful ones keep a margin.";
            if (s.InCollege) FinanceChips = ProgramChips(s);
            // H180 (run 172): the record of a season you were hired after isn't yours, so nothing is judged on it.
            var outlook = HiredAfterSeason(s) ? null : Board.Outlook(job, ctx, t, rec.Wins, rec.Losses, s.MyStats, s.Today);
            ConfidenceDrivers = outlook == null ? new() : DriverChips(outlook);
            ConfidenceHeading = outlook == null
                ? $"The {(s.InCollege ? "athletic director" : "board")} starts judging the season after five games."
                : $"Heading toward {outlook.Level:0} (now {job.Confidence:0}): each week it closes {outlook.Rate * 100:0}% of the gap. "
                  + (o.Impatient ? $"An impatient {s.BossTitle}: it moves fast." : o.IsPatient ? $"A patient {s.BossTitle}: it moves slowly." : "");
            ConfidenceRecent = Recent(job);
            RefreshFacilities();
            RefreshBusiness();
            RefreshProgram();
            RefreshAcademics();
            HasTrend = job.History.Count >= 2;
            ConfidenceTrend = HasTrend ? new[] { new LineSeries { Name = s.InCollege ? "AD confidence" : "Board confidence", Values = job.History.Select(x => x.Confidence).ToArray(), Color = ThemeService.Accent, Emphasis = true } } : null;
        }

        else { HasFacilities = false; HasBusiness = false; HasProgram = false; }
        CoachName = coach.Name;
        // K14 (run 133): a coaching licence is a pro thing; a college head coach's line leaves it out.
        CoachBio = $"Age {coach.Age(s.Today)} · {coach.Nationality} · {Bio(coach.Background)}" + (s.InCollege ? "" : $" · licence {coach.Licence}");
        Reputation = coach.Reputation;
        CoachAttrs.Clear();
        foreach (var a in Enum.GetValues<CoachAttr>()) CoachAttrs.Add(new CoachAttrView { Label = Coach.Label(a), Value = coach[a] });
        JobHistory = coach.History.Count == 0 ? "No previous jobs." :
            string.Join("\n", coach.History.Select(Hardwood.Core.Jobs.JobRecords.Line)); // H19 (run 93): season labels

        Vacancies.Clear();
        bool summer = s.Phase == Hardwood.Core.Season.SessionPhase.Offseason;
        // H167 (run 144): out of work, the jobs most likely to answer come first (offers, applications, then by hire chance);
        // the list opened on NBA long shots while the programs that would consider a sacked college coach sat below them.
        var listed = s.LiveVacancies.Where(v => !s.Employed || v.WhileEmployed);
        if (!s.Employed)
            listed = listed.OrderBy(v => v.Status switch { "offered" => 0, "applied" => 1, _ => 2 })
                .ThenByDescending(v => s.FindTeam(v.TeamId) is { } vt ? s.HireChanceFor(vt) : 0).ToList();
        foreach (var v in listed)
        {
            var t = s.FindTeam(v.TeamId)!;
            var vctx = s.ContextOf(t);
            double chance = s.HireChanceFor(t); // H167; H194 (run 182): in work too, as the club decides on it
            var vrec = s.League.Teams.Contains(t) ? s.Standings[t] : s.World.LeagueOf(t) is { } lc ? s.TableOf(lc)[t]
                : s.IsCollegeProgram(t) ? s.CollegeRecord(t) : new Hardwood.Core.Season.TeamRecord { Team = t };
            int id = v.TeamId;
            Vacancies.Add(new VacancyRow
            {
                Team = t,
                // H183 (run 176): a college opening's boss is its athletic director, not an owner.
                Detail = $"{vctx.Name} · {(s.IsCollegeProgram(t) ? "AD" : "owner")}: {t.Owner.Name} ({t.Owner.Style.ToLower()}) · {vrec.Wins}-{vrec.Losses} · " + (chance >= 0.6 ? "good chance" : chance >= 0.3 ? "worth a try" : "long shot"),
                Status = v.Status switch
                {
                    "applied" => summer ? "Applied · they answer at the next step of the summer" : $"Applied · decision by {v.DecideOn:MMM d}",
                    "offered" => (v.Approach ? "They came to you! " : "Offer! ") + (summer ? "Answer before the season starts" : $"Answer by {v.DecideOn:MMM d}"),
                    _ => $"Open since {v.Opened:MMM d}",
                },
                CanApply = v.Status == "open", HasOffer = v.Status == "offered",
                Apply = new RelayCommand(() => { Status = Session.Apply(id) ?? "Application sent."; Refresh(); Shell.AfterTick(); }),
                Accept = new RelayCommand(() => { Status = Session.AcceptOffer(id) ?? "You have a new job."; ThemeService.Apply(Session.HumanTeam); Shell.AfterTick(); Refresh(); }),
                Decline = new RelayCommand(() => { Session.DeclineOffer(id); Status = "Offer declined."; Refresh(); }),
            });
        }
        HasVacancies = Vacancies.Count > 0;
        RefreshCarousel();
        RefreshRivalCoaches();
        RefreshMyCareer();
        RefreshPlan();
        ShowJobs = !s.Employed || HasVacancies;
        JobsTitle = s.Employed ? "Your career: bigger jobs" : "Out of work: open jobs";
        JobsNote = s.Employed
            ? $"These clubs changed coach and would be a step up from {s.HumanTeam.TheFullName}. Apply without leaving your job; if an offer comes and you accept it, you move at once and your history goes with you. Openings close when the new season starts."
              + (s.InCollege && s.Coach.Reputation < Hardwood.Core.Season.GameSession.CollegeToProReputation
                  ? " Pro clubs hire college coaches with a record: titles and deep March runs build the reputation they look for." : "")
            // H220 (run 212): an empty list says so, rather than showing nothing under the title.
            : (HasVacancies ? "" : "No openings right now. Clubs post them when they change coach, and most come in the spring. ")
              + "Clubs that change coach post openings here. Apply, wait for a decision, then accept or decline an offer. Keep pressing Continue while you wait: the computer runs every team."
              + (s.World.College == null ? ""
                  : summer ? " College programs posted theirs when the season ended; more open as their coaches move."
                  : " College programs post theirs at the end of the season.")
              + (s.RecentlySackedFromCollege && s.Coach.Reputation < Hardwood.Core.Season.GameSession.SackedCollegeCoachProReputation
                  ? " Pro clubs rarely hire a college coach straight after a sacking: a college program is the likelier way back." : "");
        ResignNote = !s.Employed ? "" : ConfirmingResign
            ? $"Resign as head coach of {s.HumanTeam.TheFullName}? You'll be out of work (reputation −2) and can apply for open jobs."
            : "Bigger clubs and programs come for coaches who beat expectations. You can also resign and look for work.";
    }

    partial void OnConfirmingResignChanged(bool value) { if (Session.Employed) ResignNote = value
        ? $"Resign as head coach of {Session.HumanTeam.TheFullName}? You'll be out of work (reputation −2) and can apply for open jobs."
        : "Bigger clubs and programs come for coaches who beat expectations. You can also resign and look for work."; }

    // CS7 (run 170, K19): the college program's boosters and facilities.
    [ObservableProperty] private bool _hasProgram;
    [ObservableProperty] private List<Hardwood.Desktop.Controls.Chip> _programBuildChips = new();
    [ObservableProperty] private string _programAppeal = "";
    /// <summary>CS7 part 2b (run 173): how prestige's five-season memory moves it.</summary>
    [ObservableProperty] private string _programPrestige = "";
    [ObservableProperty] private string _programStatus = "";
    [ObservableProperty] private string _programLastSummer = "";
    [ObservableProperty] private string _buildGymText = "";
    [ObservableProperty] private string _buildArenaText = "";
    [ObservableProperty] private string _buildGymTip = "";
    [ObservableProperty] private string _buildArenaTip = "";
    [ObservableProperty] private bool _canBuildGym;
    [ObservableProperty] private bool _canBuildArena;

    [RelayCommand] private void BuildGym() { ProgramStatus = Session.BuildProgramFacility(Hardwood.Core.Model.FacilityKind.Practice); Refresh(); }
    [RelayCommand] private void BuildArena() { ProgramStatus = Session.BuildProgramFacility(Hardwood.Core.Model.FacilityKind.Arena); Refresh(); }

    private static string Cap(string w) => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w[1..];

    // Run 192 (slice 2 of the revised launch plan): a college coach plans next season's non-conference schedule with the AD (the Board page scrolls; the Schedule page's side column didn't have the room).
    public const string MixDraw = "The conference office's draw";
    public const string MixBuy = "Buy games: 5 at home";
    public const string MixTough = "Home-and-homes: 3 series";
    public const string HolidayAny = "Take an invitation if one comes";
    public const string HolidayNone = "No holiday tournament";
    public string[] PlanMixes { get; } = { MixDraw, MixBuy, MixTough };
    public ObservableCollection<string> PlanHolidays { get; } = new();
    [ObservableProperty] private bool _hasPlan;
    [ObservableProperty] private string _planTitle = "";
    [ObservableProperty] private string? _planMix;
    [ObservableProperty] private string? _planHoliday;
    [ObservableProperty] private bool _planShowcase;
    [ObservableProperty] private string _planEffect = "";
    [ObservableProperty] private string _planHolidayNote = "";
    [ObservableProperty] private string _planNow = "";
    [ObservableProperty] private string _planSeries = "";
    private bool _planLoading;

    partial void OnPlanMixChanged(string? value) => SavePlan();
    partial void OnPlanHolidayChanged(string? value) => SavePlan();
    partial void OnPlanShowcaseChanged(bool value) => SavePlan();

    private static string HolidayLabel(string name) =>
        Hardwood.Core.College.CollegeWorld.IsMajorHoliday(name)
            ? $"{name} ({Hardwood.Core.College.CollegeWorld.PrestigeLetter(Hardwood.Core.College.CollegeWorld.MajorHolidayPrestige)} or better)" : name;

    private void SavePlan()
    {
        if (_planLoading || Session is not { } s || s.World.College is not { } c || !HasPlan) return;
        var books = s.HumanTeam.Money.Program;
        var plan = books.Schedule ??= new Hardwood.Core.College.NonConferencePlan();
        plan.Mix = PlanMix switch { MixBuy => Hardwood.Core.College.ScheduleMix.BuyGames, MixTough => Hardwood.Core.College.ScheduleMix.Tough, _ => Hardwood.Core.College.ScheduleMix.Balanced };
        plan.Showcase = PlanShowcase;
        string? pick = PlanHoliday switch { null or HolidayAny => null, HolidayNone => "", var h => Hardwood.Core.College.CollegeWorld.HolidayEvents.FirstOrDefault(e => h.StartsWith(e)) };
        if (pick is { Length: > 0 } ev && c.HolidayBlocked(s.HumanTeam.Id, ev) is { } why)
        {
            PlanHolidayNote = why + " The choice stays as it was.";
            _planLoading = true; PlanHoliday = plan.Holiday == null ? HolidayAny : plan.Holiday.Length == 0 ? HolidayNone : HolidayLabel(plan.Holiday); _planLoading = false;
        }
        else { plan.Holiday = pick; PlanHolidayNote = ""; }
        PlanEffect = EffectText(plan.Mix);
    }

    private static string EffectText(Hardwood.Core.College.ScheduleMix mix) => mix switch
    {
        Hardwood.Core.College.ScheduleMix.BuyGames => "Five low-majors (prestige under C-) are paid to come to your gym: near-certain wins. The AD expects one more win, and they barely move your rating, so your strength of schedule falls.",
        Hardwood.Core.College.ScheduleMix.Tough => "Three power programs (prestige B or better), one game this season and the return next season. The AD allows one win for them; a win moves your rating most, and your strength of schedule rises.",
        _ => "The conference office fills the dates, leaning to home games against smaller names. The AD's target is set as usual.",
    };

    private void RefreshPlan()
    {
        var s = Session;
        HasPlan = s != null && s.Employed && s.InCollege && s.World.College != null;
        if (!HasPlan) return;
        var c = s!.World.College!;
        int me = s.HumanTeam.Id, next = c.Season.Year + 1;
        var plan = s.HumanTeam.Money.Program.Schedule;
        _planLoading = true;
        PlanTitle = $"Your non-conference schedule for {next}-{(next + 1) % 100:00}";
        PlanHolidays.Clear();
        PlanHolidays.Add(HolidayAny); PlanHolidays.Add(HolidayNone);
        foreach (var h in Hardwood.Core.College.CollegeWorld.HolidayEvents) PlanHolidays.Add(HolidayLabel(h));
        PlanMix = plan?.Mix switch { Hardwood.Core.College.ScheduleMix.BuyGames => MixBuy, Hardwood.Core.College.ScheduleMix.Tough => MixTough, _ => MixDraw };
        PlanHoliday = plan?.Holiday switch { null => HolidayAny, "" => HolidayNone, var h => HolidayLabel(h) };
        PlanShowcase = plan?.Showcase == true;
        _planLoading = false;
        PlanEffect = EffectText(plan?.Mix ?? Hardwood.Core.College.ScheduleMix.Balanced);
        PlanHolidayNote = "";
        var st = c.ScheduleStrength(me);
        string slate = c.NonConferenceSummary(me);
        PlanNow = slate.Length == 0 ? "" : $"This season: {slate}" + (st is { } x ? $" Strength of schedule {Hardwood.Core.Presentation.GameCalls.Ordinal(x.Rank)} of {x.Of} (non-conference {Hardwood.Core.Presentation.GameCalls.Ordinal(x.NonConfRank)})." : "");
        var returns = plan?.Series.Where(x => x.FirstYear == c.Season.Year).ToList() ?? new();
        PlanSeries = returns.Count == 0 ? "" : "Returns next season: " + string.Join(", ", returns.Select(x => $"{(x.HomeFirst ? "at" : "vs")} {c.Program(x.OpponentId)?.FullName}")) + ".";
    }

    /// <summary>CS9 (run 191): the newest spring's coaching changes. In college your conference and your rivals come first,
    /// then the biggest jobs; each row says who left, why, and who came in.</summary>
    private void RefreshCarousel()
    {
        var s = Session;
        CarouselRows.Clear();
        var spring = s.LatestCarousel;
        HasCarousel = s.World.College != null && spring.Count > 0 && (s.InCollege || !s.Employed);   // H223 (run 229): not on a pro club's Board
        if (!HasCarousel) return;
        var cw = s.World.College!;
        bool mine = s.Employed && s.InCollege;
        string? conf = mine ? cw.ConferenceOf[s.HumanTeam.Id] : null;
        var rivals = mine ? cw.RivalsOf(s.HumanTeam.Id).Select(r => r.RivalId).ToHashSet() : new HashSet<int>();
        bool Ours(Hardwood.Core.Season.CarouselMove m) => rivals.Contains(m.ProgramId) || (conf != null && cw.ConferenceOf.GetValueOrDefault(m.ProgramId) == conf);
        var ordered = spring.Where(m => s.FindTeam(m.ProgramId) != null)
            .OrderByDescending(m => m.Why == "you" || (mine && m.ProgramId == s.HumanTeam.Id))
            .ThenByDescending(Ours)
            .ThenByDescending(m => s.CarouselSize(s.FindTeam(m.ProgramId)!)).ToList();
        foreach (var m in CarouselAll ? ordered : ordered.Take(CarouselShown))
        {
            var t = s.FindTeam(m.ProgramId)!;
            string line = s.CarouselLine(m);
            string detail = line.StartsWith(t.FullName + ": ") ? line[(t.FullName.Length + 2)..] : line;
            CarouselRows.Add(new CarouselRow
            {
                Team = t, Detail = UpperFirst(detail),
                Tag = rivals.Contains(m.ProgramId) ? "Rival" : Ours(m) ? Hardwood.Core.College.CollegeWorld.Conferences.First(c => c.Key == conf).Short : "",
            });
        }
        int fired = spring.Count(m => m.Why is "fired" or "you"), retired = spring.Count(m => m.Why == "retired"), left = spring.Count(m => m.Why == "left");
        CarouselTitle = $"The coaching carousel, spring {spring[0].Year}";
        CarouselNote = $"{spring.Count} programs changed head coach: {fired} let go, {retired} retired, {left} hired away by bigger jobs."
            + (mine ? " Your conference and your rivals first, then the biggest jobs." : " The biggest jobs first.");
        HasCarouselMore = ordered.Count > CarouselShown;
        CarouselToggle = CarouselAll ? "Show fewer" : $"Show all {ordered.Count}";
    }

    /// <summary>Slice 9 (run 200): the other benches in your world: each coach's style, his record at the program and in his
    /// career, his reputation, and your series with his program.</summary>
    private void RefreshRivalCoaches()
    {
        var s = Session;
        RivalCoachRows.Clear();
        HasRivalCoaches = s.Employed && s.InCollege && s.World.College != null;
        if (!HasRivalCoaches) return;
        var cw = s.World.College!;
        int me = s.HumanTeam.Id;
        string conf = cw.ConferenceOf[me];
        string confShort = Hardwood.Core.College.CollegeWorld.Conferences.First(c => c.Key == conf).Short;
        var rivals = cw.RivalsOf(me).Select(r => r.RivalId).ToList();
        var mates = cw.Programs.Where(p => p.Id != me && cw.ConferenceOf.GetValueOrDefault(p.Id) == conf && !rivals.Contains(p.Id))
            .Select(p => (p, b: cw.BenchOf(p.Id))).Where(x => x.b != null)
            .OrderByDescending(x => cw.ReputationOf(x.b!)).Select(x => x.p.Id);
        // The season-end review books the season into every bench (GameSession.SeasonEndCoaching → CollegeCoachingChanges, the
        // day the summer begins); until then it's added here. The review of PR #281: not judged by the carousel's moves, since a
        // spring with no changes posts none.
        bool booked = cw.Season.ChampionId != null && s.Phase == Hardwood.Core.Season.SessionPhase.Offseason;
        foreach (int id in rivals.Concat(mates).Distinct())
        {
            if (cw.Program(id) is not { } t || cw.BenchOf(id) is not { } c) continue;
            int rep = cw.ReputationOf(c);
            // H213 (run 212): the book counts completed seasons only (it adds a season each spring), so the games played this
            // season are added here: Virginia's coach read "in his first season: 0-0 here" after a 32-5 season.
            var (nw, nl) = booked ? (0, 0) : cw.RegularSeasonRecord(id); // the book counts the regular season
            string here = $"{c.Wins + nw}-{c.Losses + nl}";
            string career = c.CareerWins + c.CareerLosses > c.Wins + c.Losses ? $"; {c.CareerWins + nw}-{c.CareerLosses + nl} in his career" : "";
            string honours = (c.Bids > 0 ? $", {c.Bids} NCAA bid{(c.Bids == 1 ? "" : "s")}" : "") + (c.Titles > 0 ? $", {c.Titles} title{(c.Titles == 1 ? "" : "s")}" : "");
            string series = cw.SeriesLine(me, id) is { } line ? $" {UpperFirst(line)}." : "";
            RivalCoachRows.Add(new CarouselRow
            {
                Team = t,
                Detail = $"{c.Name}, {Hardwood.Core.College.RivalCoaches.StyleText(c.Style)}, {c.SeasonText}: {here} here{career}{honours}. "
                    + $"Reputation {rep} ({Hardwood.Core.College.RivalCoaches.ReputationWord(rep)}).{series}",
                Tag = rivals.Contains(id) ? "Rival" : confShort,
            });
        }
        HasRivalCoaches = RivalCoachRows.Count > 0;
        RivalCoachesNote = "The men on the other benches: your rivals first, then your conference by reputation. A coach's style is how he wins "
            + "and how he recruits; his quotes follow your rivalry games, and he's named when he beats you to a recruit.";
    }

    /// <summary>Slice 10 (run 201): your college career as the coaching book keeps it (read-only: nothing here writes the book).</summary>
    private void RefreshMyCareer()
    {
        var s = Session;
        MyCareerSeasons.Clear(); MyCareerMilestones.Clear(); MyCareerTree.Clear();
        var cw = s.World.College;
        var book = s.MyCoachBook;
        bool live = s.CoachingThisCollegeSeason && cw != null;
        HasMyCareer = cw != null && (s.Employed && s.InCollege || book.Seasons.Count > 0);
        HasMyMilestones = HasMyTree = false;
        if (!HasMyCareer) return;
        int w = book.Wins, l = book.Losses, n = book.Seasons.Count;
        string Plural(int k, string one, string many) => k == 1 ? $"1 {one}" : $"{k} {many}";
        MyCareerHeadline = n == 0 ? (book.Partial ? $"{s.Coach.Name}: your seasons count from this one (the book is newer than this save)."
                : live && cw!.Season.ChampionId != null ? $"{s.Coach.Name}: your first college season is over; it goes in the book at the athletic director's review."
                : live ? $"{s.Coach.Name}: your first college season is under way." : $"{s.Coach.Name}: your first college season starts in the fall.")
            : $"{s.Coach.Name}: {w}-{l} ({(w / (double)Math.Max(1, w + l)):.000}) in {Plural(n, "season", "seasons")}"
              + $" · {Plural(book.Bids, "NCAA bid", "NCAA bids")}" + (book.FinalFours > 0 ? $" · {Plural(book.FinalFours, "Final Four", "Final Fours")}" : "")
              + (book.Titles > 0 ? $" · {Plural(book.Titles, "national title", "national titles")}" : "");
        if (live && !book.Seasons.Any(x => x.Year == s.League.Season))
        {
            var rec = s.MyRecord;
            MyCareerSeasons.Add($"{Hardwood.Core.College.CollegeAwards.SeasonText(s.League.Season)} · {s.HumanTeam.FullName} · {rec.Wins}-{rec.Losses}{(cw!.Season.ChampionId != null ? " (season over; counted at the review)" : " so far")}");
        }
        foreach (var x in book.Seasons.OrderByDescending(x => x.Year))
            MyCareerSeasons.Add($"{Hardwood.Core.College.CollegeAwards.SeasonText(x.Year)} · {x.Program} · {x.Wins}-{x.Losses}"
                + (x.Seed > 0 ? $" · {x.Seed} seed, {Hardwood.Core.College.CollegeAwardsText.Reached(x.Reached)}" : ""));
        foreach (var m in book.Milestones.OrderByDescending(m => m.Date).Take(8))
            MyCareerMilestones.Add($"{m.Date:MMM d, yyyy} · {m.Text}");
        foreach (var b in book.Tree)
        {
            var now = cw!.BenchNamed(b.Name);
            MyCareerTree.Add(now != null && cw.Program(now.ProgramId) is { } t
                ? $"{b.Name}: head coach at {t.FullName}, {now.SeasonText} ({now.Record}; reputation {cw.ReputationOf(now)}). From your staff, spring {b.Year}."
                : $"{b.Name}: left your staff for {b.Program} in spring {b.Year}; no longer a head coach.");
        }
        HasMyMilestones = MyCareerMilestones.Count > 0;
        HasMyTree = MyCareerTree.Count > 0;
        MyCareerNote = "Your college seasons count the day the athletic director reviews them. Milestones are told once, in your inbox and the news. "
            + $"Once you've had {Hardwood.Core.College.CoachingLegacy.TreeSeasons} seasons at your program, an assistant of yours (coaching {Hardwood.Core.College.CoachingLegacy.TreeCoaching}+) may take a head job in the spring. "
            + $"A coach who leaves a program after {Hardwood.Core.College.CoachingLegacy.HallSeasons}+ winning seasons with {Hardwood.Core.College.CoachingLegacy.HallBids}+ NCAA bids, "
            + $"or {Hardwood.Core.College.CoachingLegacy.TitleSeasons}+ seasons with a national title, joins its Hall of Fame. You too.";
    }

    private static string UpperFirst(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private void RefreshProgram()
    {
        var s = Session;
        HasProgram = s.HasProgramBuilding;
        if (!HasProgram) return;
        var t = s.HumanTeam; var b = s.MyProgramBooks; var f = t.Money.Facilities;
        string M(long v) => Hardwood.Core.College.ProgramBuilding.Money(v);
        string Built(Hardwood.Core.Model.FacilityKind k) => Hardwood.Core.College.ProgramBuilding.Built(t, k) is var n && n > 0 ? $"+{n} built" : "as you found it";
        string Pending(Hardwood.Core.Model.FacilityKind k) => b.Pending == k ? " · next level opens next summer" : "";
        ProgramBuildChips = new List<Hardwood.Desktop.Controls.Chip>
        {
            new("Booster fund", M(b.Fund), b.LastGift > 0 ? $"to spend · {M(b.LastGift)} last summer" : "to spend", "team"),
            new("Boosters", Cap(Hardwood.Core.College.ProgramBuilding.Mood(b.Boosters)), // H199 (run 183): "Content", like "Level 3"
                $"{b.Boosters:0}/100 · winning and rivalry wins please them", b.Boosters >= 60 ? "good" : b.Boosters < 30 ? "warn" : ""),
            new("Practice gym", $"Level {f.Practice.Level}", Built(Hardwood.Core.Model.FacilityKind.Practice) + Pending(Hardwood.Core.Model.FacilityKind.Practice), f.Practice.Level >= 4 ? "good" : f.Practice.Level <= 2 ? "warn" : ""),
            new("Arena", $"Level {f.Arena.Level}", Built(Hardwood.Core.Model.FacilityKind.Arena) + Pending(Hardwood.Core.Model.FacilityKind.Arena), f.Arena.Level >= 4 ? "good" : f.Arena.Level <= 2 ? "warn" : ""),
        };
        double appeal = Hardwood.Core.College.ProgramBuilding.Appeal(t);
        int gymBuilt = Hardwood.Core.College.ProgramBuilding.Built(t, Hardwood.Core.Model.FacilityKind.Practice);
        ProgramAppeal = appeal > 0
            ? $"What you've built adds +{appeal:0} to your appeal with every recruit (two neighbouring programs are about 4 apart)"
              + (gymBuilt > 0 ? $", and the practice gym lifts training quality by +{gymBuilt * Hardwood.Core.College.ProgramBuilding.GymEdge * 0.4:0.0}." : ".")
            : $"Nothing built yet. Each arena level adds +{Hardwood.Core.College.ProgramBuilding.ArenaAppeal:0} appeal with recruits; each practice gym level +{Hardwood.Core.College.ProgramBuilding.GymAppeal:0}, and faster growth for your players.";
        ProgramPrestige = PrestigeMemoryLine(s.World.College!, t.Id);
        (BuildGymText, CanBuildGym, BuildGymTip) = BuildButton(t, Hardwood.Core.Model.FacilityKind.Practice);
        (BuildArenaText, CanBuildArena, BuildArenaTip) = BuildButton(t, Hardwood.Core.Model.FacilityKind.Arena);
        // H191 (run 183): say what building does now, not "a project paid for", beside "Nothing built yet".
        ProgramLastSummer = b.LastLines.Count > 0 ? "Last summer: " + string.Join(" ", b.LastLines)
            : s.ProgramBuildsNow ? "Build now and it's ready for the season." : "Build during the season and it opens next summer.";
        // H199 (run 183): the fund may pay for one build but not both; say so beside the two buttons.
        if (CanBuildGym && CanBuildArena && b.Pending == null)
        {
            long gym = Hardwood.Core.College.ProgramBuilding.Cost(Hardwood.Core.Model.FacilityKind.Practice, f.Practice.Level);
            long arena = Hardwood.Core.College.ProgramBuilding.Cost(Hardwood.Core.Model.FacilityKind.Arena, f.Arena.Level);
            if (b.Fund < gym + arena) ProgramLastSummer += $" The {M(b.Fund)} fund pays for one of these, not both.";
        }
    }

    /// <summary>CS7 part 2b (run 173): prestige's long memory in words: the five-season form and the remembered marks, newest
    /// first. K27 (run 180): the newest season counts most.</summary>
    public static string PrestigeMemoryLine(Hardwood.Core.College.CollegeWorld cw, int programId)
    {
        const string how = "the newest season counts most (40%), and each season keeps counting for five summers";
        if (!cw.Form.TryGetValue(programId, out var marks) || marks.Count == 0)
            return $"Prestige remembers {Hardwood.Core.College.CollegeWorld.PrestigeMemory} seasons: each summer their marks (the record, the conference record, March wins and titles) move it; {how}.";
        static string Signed(double v) => Hardwood.Core.College.CollegeWorld.Signed(v);
        double form = cw.FormOf(programId);
        string list = string.Join(", ", Enumerable.Reverse(marks).Select(Signed));
        return $"Prestige remembers {Hardwood.Core.College.CollegeWorld.PrestigeMemory} seasons: five-season form {Signed(form)} (marks {list}, newest first); {how}.";
    }

    private static (string, bool, string) BuildButton(Hardwood.Core.Model.Team t, Hardwood.Core.Model.FacilityKind k)
    {
        var lvl = t.Money.Facilities.Get(k).Level;
        string name = Hardwood.Core.College.ProgramBuilding.Name(k);
        if (lvl >= 5) return ($"{name}: top level", false, $"The {name.ToLowerInvariant()} is already the best in the country.");
        var why = Hardwood.Core.College.ProgramBuilding.Blocked(t, k);
        return ($"Build {name.ToLowerInvariant()} level {lvl + 1} · {Hardwood.Core.College.ProgramBuilding.Money(Hardwood.Core.College.ProgramBuilding.Cost(k, lvl))}", why == null,
                why ?? (k == Hardwood.Core.Model.FacilityKind.Arena ? "Recruits notice a better arena." : "Recruits notice it, and your players grow faster."));
    }

    // CS7 part 2c (run 175): academics.
    [ObservableProperty] private bool _hasAcademics;
    [ObservableProperty] private List<Hardwood.Desktop.Controls.Chip> _academicChips = new();
    [ObservableProperty] private List<AcademicRow> _academicRows = new();
    [ObservableProperty] private string _academicSummary = "";
    [ObservableProperty] private string _academicStatus = "";
    [ObservableProperty] private string _buildCenterText = "";
    [ObservableProperty] private string _buildCenterTip = "";
    [ObservableProperty] private bool _canBuildCenter;

    [RelayCommand] private void BuildCenter() { AcademicStatus = Session.BuildAcademicCenter(); Refresh(); }

    /// <summary>How many of the term's rows the panel lists: everyone at risk, then the lowest projections up to this.</summary>
    public const int AcademicRowsShown = 5;

    private void RefreshAcademics()
    {
        var s = Session;
        HasAcademics = s.HasAcademics;
        if (!HasAcademics) return;
        var b = s.HumanTeam.Money.Program;
        var term = s.AcademicTerm;
        var roster = s.AcademicRoster();
        int risk = roster.Count(x => x.Standing != Hardwood.Core.College.Academics.Standing.Fine);
        var ineligible = s.HumanTeam.Roster.Where(p => p.IsIneligible(s.Today)).ToList();
        int hall = roster.Count(x => x.StudyHall);
        AcademicChips = new List<Hardwood.Desktop.Controls.Chip>
        {
            new("Term", term is { } t ? char.ToUpperInvariant(Hardwood.Core.College.Academics.TermName(t)[0]) + Hardwood.Core.College.Academics.TermName(t)[1..] : "Summer", term is { } t2 ? $"grades {(t2.Fall ? Hardwood.Core.College.Academics.FallGrades(t2.Year) : Hardwood.Core.College.Academics.SpringGrades(t2.Year)):MMM d}" : "classes start in the fall", "team"),
            new("At risk", risk.ToString(), term == null ? "no classes" : risk == 0 ? "everyone on track" : $"under {Hardwood.Core.College.Academics.PassMark:0.0} possible", risk > 0 ? "warn" : "good"),
            new("Ineligible", ineligible.Count.ToString(), ineligible.Count == 0 ? "nobody sitting out" : string.Join(", ", ineligible.Select(p => p.LastName)), ineligible.Count > 0 ? "bad" : ""),
            new("Study hall", hall.ToString(), hall == 0 ? "nobody in it" : $"costs {hall * Hardwood.Core.College.Academics.StudyHallHours} recruiting hours a week", ""), // H199 (run 183)
            new("Academic center", $"Level {b.AcademicCenter}", b.AcademicCenter > 0 ? $"+{Hardwood.Core.College.Academics.CenterLift * b.AcademicCenter:0.00} on grades" : "not built", b.AcademicCenter >= 2 ? "good" : ""),
        };
        var good = (IBrush)new SolidColorBrush(Color.Parse("#3FB37F")); var warn = (IBrush)new SolidColorBrush(Color.Parse("#E0A33A")); var bad = (IBrush)new SolidColorBrush(Color.Parse("#E05A4F"));
        AcademicRows = roster.Where((x, i) => x.Standing != Hardwood.Core.College.Academics.Standing.Fine || x.StudyHall || i < 3).Take(Math.Max(AcademicRowsShown, risk))
            .Select(x =>
            {
                var player = x.Player;
                return new AcademicRow
                {
                    Name = $"{x.Player.Name} ({x.Player.PrimaryPosition})",
                    Detail = $"Projected {x.Projected:0.00}" + (x.StudyHall ? " · in study hall" : "") + (x.Player.IsIneligible(s.Today) ? $" · ineligible to {x.Player.IneligibleUntil:MMM d}" : ""),
                    Standing = Hardwood.Core.College.Academics.Word(x.Standing),
                    StandingBrush = x.Standing == Hardwood.Core.College.Academics.Standing.Failing ? bad : x.Standing == Hardwood.Core.College.Academics.Standing.AtRisk ? warn : good,
                    ButtonText = x.StudyHall ? "Out of study hall" : "Study hall",
                    Toggle = new RelayCommand(() => { AcademicStatus = Session.ToggleStudyHall(player); Refresh(); }),
                };
            }).ToList();
        AcademicSummary = term == null
            ? (b.LastTerm != "" ? $"Last term ({b.LastTerm}): team average {(b.LastGrades.Count > 0 ? b.LastGrades.Values.Average() : 0):0.00}. " : "") + "Classes start again in the fall; the midterm report comes out on Nov 20."
            : risk == 0 ? $"Everyone is on track to stay eligible this term (the mark is {Hardwood.Core.College.Academics.PassMark:0.0}). The lowest projections are listed."
            : $"{risk} player{(risk == 1 ? " is" : "s are")} at risk: under {Hardwood.Core.College.Academics.PassMark:0.0} at the grades, a player sits out " + (term.Value.Fall ? "until mid-February." : "until December.")
              + $" Study hall (+{Hardwood.Core.College.Academics.StudyHallLift:0.0}) gets anyone at risk through; a failing player may need the academic center too.";
        if (Hardwood.Core.College.Academics.CenterBlocked(b) is { } why && b.AcademicCenter >= Hardwood.Core.College.Academics.MaxCenter)
            (BuildCenterText, CanBuildCenter, BuildCenterTip) = ("Academic center: top level", false, why);
        else
            (BuildCenterText, CanBuildCenter, BuildCenterTip) = ($"Build academic center level {b.AcademicCenter + 1} · {Hardwood.Core.College.ProgramBuilding.Money(Hardwood.Core.College.Academics.CenterCost(b.AcademicCenter))}",
                Hardwood.Core.College.Academics.CenterBlocked(b) == null, Hardwood.Core.College.Academics.CenterBlocked(b) ?? $"Tutors and advisers: +{Hardwood.Core.College.Academics.CenterLift:0.00} on every player's grades, from this term.");
    }

    // D81 (run 169): the revenue levers.
    [ObservableProperty] private bool _hasBusiness;
    [ObservableProperty] private List<Hardwood.Desktop.Controls.Chip> _businessChips = new();
    public IReadOnlyList<string> TicketChoices => Hardwood.Core.Season.Business.TicketNames;
    public IReadOnlyList<string> FoodChoices => Hardwood.Core.Season.Business.FoodNames;
    public IReadOnlyList<string> ParkingChoices => Hardwood.Core.Season.Business.ParkingNames;
    public IReadOnlyList<string> FanDayChoices { get; } = new[] { "None", "1 a season", "2 a season", "3 a season", "4 a season" };
    [ObservableProperty] private string _ticket = "Standard";
    [ObservableProperty] private string _food = "Standard";
    [ObservableProperty] private string _parking = "Standard";
    [ObservableProperty] private string _fanDays = "None";
    [ObservableProperty] private string _businessPreview = "";
    [ObservableProperty] private string _businessWhen = "";
    [ObservableProperty] private string _businessStatus = "";
    // A blank selection (a template swap) is never taken for the cheapest level.
    partial void OnTicketChanged(string value) { if (value != null) ApplyBusiness(); }
    partial void OnFoodChanged(string value) { if (value != null) ApplyBusiness(); }
    partial void OnParkingChanged(string value) { if (value != null) ApplyBusiness(); }
    partial void OnFanDaysChanged(string value) { if (value != null) ApplyBusiness(); }

    private Hardwood.Core.Model.BusinessPlan PickedPlan() => new()
    {
        Tickets = Level(Hardwood.Core.Season.Business.TicketNames, Ticket),
        Food = Level(Hardwood.Core.Season.Business.FoodNames, Food),
        Parking = Level(Hardwood.Core.Season.Business.ParkingNames, Parking),
        FanDays = Math.Max(0, Array.IndexOf((string[])FanDayChoices, FanDays)),
    };

    /// <summary>A choice's level (-1 first); anything not in the list is standard.</summary>
    private static int Level(string[] names, string? v) => Array.IndexOf(names, v) is var i && i >= 0 ? i - 1 : 0;

    private void ApplyBusiness()
    {
        if (_refreshing || !HasBusiness) return;
        BusinessStatus = Session.SetBusiness(PickedPlan());
        Refresh();
    }

    private void RefreshBusiness()
    {
        var s = Session;
        HasBusiness = s.HasBusiness;
        if (!HasBusiness) return;
        var m = s.HumanTeam.Money;
        string M(long v) => Hardwood.Core.Season.Budgets.Money(v);
        string Signed(long v) => (v >= 0 ? "+" : "-") + M(Math.Abs(v));
        var now = s.MyMatchDay();
        var next = s.MyNextPlan;
        BusinessChips = new List<Hardwood.Desktop.Controls.Chip>
        {
            new("Match day", M(now.Total), now.Change == 0 ? "this season · standard prices" : $"this season · {Signed(now.Change)} on standard", now.Change > 0 ? "good" : now.Change < 0 ? "warn" : "team"),
            new("Arena", $"{now.Fill * 100:0}%", $"full · {(int)Math.Round(now.Fill * s.HumanTeam.ArenaCapacity):N0} of {s.HumanTeam.ArenaCapacity:N0}", now.Fill >= 0.95 ? "good" : now.Fill < 0.8 ? "warn" : ""),
            new("Fans", Hardwood.Core.Season.Business.FanDelta(s.MyPlan) is var fd && Math.Abs(fd) < 0.05 ? "±0" : $"{fd:+0.0;-0.0}", "fan base next summer, from prices", fd > 0.05 ? "good" : fd < -0.05 ? "warn" : ""),
        };
        _refreshing = true;
        Ticket = Hardwood.Core.Season.Business.TicketName(next.Tickets);
        Food = Hardwood.Core.Season.Business.FoodName(next.Food);
        Parking = Hardwood.Core.Season.Business.ParkingName(next.Parking);
        FanDays = FanDayChoices[Math.Clamp(next.FanDays, 0, 4)];
        _refreshing = false;
        var md = s.MyMatchDay(next);
        BusinessPreview = $"At these prices: tickets {M(md.Tickets)}, food and drink {M(md.Food)}, parking {M(md.Parking)}"
            + (md.FanDayCost > 0 ? $", fan days cost {M(md.FanDayCost)}" : "")
            + $". Match day {M(md.Total)} ({(md.Change == 0 ? "standard" : Signed(md.Change) + " on standard")}), the arena {md.Fill * 100:0}% full.";
        BusinessWhen = s.BusinessChangesNow ? "Changes take effect this season."
            : next.Same(s.MyPlan) ? "Season tickets are sold: a change now starts next season."
            : $"Next season's prices are set. This season stays at tickets {Hardwood.Core.Season.Business.TicketName(s.MyPlan.Tickets).ToLower()}, food {Hardwood.Core.Season.Business.FoodName(s.MyPlan.Food).ToLower()}, parking {Hardwood.Core.Season.Business.ParkingName(s.MyPlan.Parking).ToLower()}, {s.MyPlan.FanDays} fan day{(s.MyPlan.FanDays == 1 ? "" : "s")}.";
    }

    private void RefreshFacilities()
    {
        var s = Session;
        HasFacilities = s.HasFacilities;
        if (!HasFacilities) return;
        var t = s.HumanTeam; var f = s.MyFacilities; var m = t.Money;
        string M(long v) => Hardwood.Core.Season.Franchise.Money(v);
        long reserve = Hardwood.Core.Season.Franchise.Reserve(t), budget = s.FacilityBudget;
        FacilityChips = new List<Hardwood.Desktop.Controls.Chip>
        {
            // H67 (run 105): the reserve is a floor he won't spend below, not money he holds ("keeps $72M in the bank" beside "Bank $34M").
            new("Owner will fund", M(budget), budget > 0 ? $"above a {M(reserve)} reserve" : $"bank under his {M(reserve)} reserve", budget > 0 ? "good" : ""),
            new("Running costs", M(Hardwood.Core.Season.Franchise.Upkeep(f, s.MyLeagueRevenue)), "a season", ""),
            // H99 (run 109): money spent this summer reads "this summer" until the season starts.
            new("Invested", M(f.LastInvested), s.Phase is Hardwood.Core.Season.SessionPhase.Offseason or Hardwood.Core.Season.SessionPhase.Preseason ? "this summer" : "last summer", ""),
            new("Arena", $"{t.ArenaCapacity:N0}", $"seats · {f.ArenaYears} years old", f.ArenaYears >= Hardwood.Core.Season.Franchise.ArenaLife ? "warn" : "team"),
        };
        Facilities.Clear();
        foreach (var k in Hardwood.Core.Model.ClubFacilities.Kinds)
        {
            var fac = f.Get(k); var info = Hardwood.Core.Season.Franchise.Info(k);
            var build = f.Building.FirstOrDefault(b => b.Kind == k || (k == Hardwood.Core.Model.FacilityKind.Arena && b.Kind == Hardwood.Core.Model.FacilityKind.NewArena));
            int left = Hardwood.Core.Season.Franchise.Lifespan(k, fac.Level) - fac.Age;
            string status = build != null ? (build.Kind == Hardwood.Core.Model.FacilityKind.NewArena ? $"New arena: opens in {build.SeasonsLeft} summer{(build.SeasonsLeft == 1 ? "" : "s")}" : build.Renewal ? "Being renewed: done next summer" : $"Building level {build.ToLevel}: opens next summer")
                : left <= 1 ? "Dated: drops a level next summer unless it's renewed" : left <= 3 ? $"Ageing: dates in {left} seasons" : $"Modern: good for {left} more seasons";
            string tone = build != null ? "#58A6FF" : left <= 1 ? "#F85149" : left <= 3 ? "#D29922" : "#3FB950";
            Facilities.Add(new FacilityRow
            {
                Name = info.Name, Pitch = info.Pitch, Level = fac.Level,
                LevelText = $"{fac.Level}/5 · {(fac.Edge >= 0.05 ? "+" : "")}{fac.Edge:0.0} on the league",
                Effect = $"{Hardwood.Core.Season.Franchise.ClubEffect(k, fac.Edge)}. {info.Effect}", Status = status, StatusBrush = new SolidColorBrush(Color.Parse(tone)), // H102 (run 112)
            });
        }
        HasBuilding = f.Building.Count > 0;
        Building = HasBuilding ? "Under way: " + string.Join("; ", f.Building.Select(Hardwood.Core.Season.Franchise.Describe)) : "";
        Proposals.Clear();
        for (int i = 0; i < f.Proposals.Count; i++)
        {
            int idx = i; var p = f.Proposals[i]; var info = Hardwood.Core.Season.Franchise.Info(p.Kind);
            string title = p.Kind == Hardwood.Core.Model.FacilityKind.NewArena ? $"A new arena: {p.Capacity:N0} seats" : p.Renewal ? $"Renew the {info.Name.ToLower()} (level {p.ToLevel})" : $"{info.Name} to level {p.ToLevel}";
            string cost = p.Instalments > 1 ? $"{M(p.Cost)} in {p.Instalments} payments of {M(p.Instalment)}" : M(p.Cost);
            string when = p.SeasonsLeft <= 1 ? "opens next summer" : $"opens in {p.SeasonsLeft} summers";
            Proposals.Add(new ProposalRow
            {
                Title = title,
                Detail = $"{cost} · {when} · {info.Effect}",
                Approve = new RelayCommand(() => { FacilityStatus = Session.ApproveFacility(idx) ?? $"Approved: {title}."; Refresh(); }),
                Decline = new RelayCommand(() => { Session.DeclineFacility(idx); FacilityStatus = $"Declined: {title}."; Refresh(); }),
            });
        }
        HasProposals = Proposals.Count > 0;
        ProposalNote = !s.Delegated.Facilities
            ? (HasProposals ? "The front office's proposals. The owner will fund any of these from the bank; each summer brings new ones." : "No proposals right now. They come each summer, when the owner has money above his reserve.")
            : "Your general manager decides each summer, within what the owner will fund.";
        ProposalNote += $" Your owner {Hardwood.Core.Season.Franchise.Stance(t.Owner)}.";
        _refreshing = true; GmDecides = s.Delegated.Facilities; _refreshing = false;
    }

    /// <summary>Each thing the board weighs as a chip: the points it adds to or takes off where confidence settles.</summary>
    /// <summary>A college program at a glance: prestige, national rank, records, and next summer's graduates.</summary>
    public static List<Hardwood.Desktop.Controls.Chip> ProgramChips(Hardwood.Core.Season.GameSession s)
    {
        var cw = s.World.College!; var t = s.HumanTeam;
        double prestige = cw.Prestige.GetValueOrDefault(t.Id, 50);
        int rank = cw.PollPosition(t.Id); // H153 (run 136): the poll's rank, as the Top 25 shows it (was the rating alone)
        var rec = s.MyRecord; var conf = s.MyStandings[t];
        // H181 (run 172): after graduation day the chip counts next season's seniors, as Home's chart does (K22); it read
        // "Graduating 0" all summer while Home showed four seniors.
        var (summer, nextSeason) = HomeViewModel.ClassSummer(s);
        int seniors = t.Roster.Count(p => p.Age(summer) >= Hardwood.Core.College.CollegeWorld.GraduateAge);
        return new List<Hardwood.Desktop.Controls.Chip>
        {
            // CS2 (run 126, #166): prestige as a letter, the number beside it.
            new("Prestige", Hardwood.Core.College.CollegeWorld.PrestigeLetter(prestige), $"{prestige:0.0}/100, " + PrestigeWords(prestige, cw.PrestigeRanks().GetValueOrDefault(t.Id, cw.Programs.Count)), prestige >= 75 ? "good" : prestige < 40 ? "warn" : "team"),
            new("National rank", rank <= Hardwood.Core.College.CollegeWorld.PollSize ? $"No. {rank}" : "Unranked", rank <= Hardwood.Core.College.CollegeWorld.PollSize ? "in the Top 25 poll" : $"No. {rank} of {cw.Programs.Count} in the ratings", rank <= Hardwood.Core.College.CollegeWorld.PollSize ? "good" : ""),
            new("Overall", $"{rec.Wins}-{rec.Losses}", "this season", rec.Games > 0 && rec.WinPct >= 0.6 ? "good" : rec.Games > 0 && rec.WinPct < 0.4 ? "bad" : ""),
            // H160 (run 155): before conference play the chip said "0-0" with 14 games played; it says when play starts.
            conf.Games == 0 && FirstConferenceGame(cw, t) is { } first
                ? new("Conference", "Not started", $"{s.MyConference!.Short} play starts {first:MMM d}", "")
                : new("Conference", $"{conf.Wins}-{conf.Losses}", s.MyConference!.Short, conf.Games > 0 && conf.WinPct >= 0.6 ? "good" : ""),
            new("Graduating", $"{seniors}", nextSeason ? "next season's seniors" : "turn pro next summer", "info"),
        };
    }

    /// <summary>H136 (run 132): "a blue blood" is the top handful by prestige rank, not every program at 75+ (the No. 50
    /// program read "a blue blood"); the rest of 75+ are power programs.</summary>
    public const int BlueBloods = 8;
    /// <summary>H160: the date of a program's first unplayed conference game, or null.</summary>
    private static DateOnly? FirstConferenceGame(Hardwood.Core.College.CollegeWorld cw, Hardwood.Core.Model.Team t) =>
        cw.GamesOf(t.Id).Where(g => !g.Played && g.Stage == "Conference").Select(g => (DateOnly?)g.Date).Min();

    public static string PrestigeWords(double prestige, int prestigeRank) =>
        prestige >= 75 && prestigeRank <= BlueBloods ? "a blue blood" : prestige >= 75 ? "a power program" : prestige >= 55 ? "a known name" : "building a name";

    public static List<Hardwood.Desktop.Controls.Chip> DriverChips(BoardOutlook outlook)
    {
        var chips = new List<Hardwood.Desktop.Controls.Chip> { new("Starting point", $"{Board.BaseLevel:0}", "on target, demands met", "info") };
        foreach (var d in outlook.Drivers)
            chips.Add(new(d.Label, d.Points >= 0 ? $"+{d.Points:0}" : $"{d.Points:0}", d.Text, d.Points >= 1 ? "good" : d.Points <= -1 ? "bad" : ""));
        chips.Add(new("Heading toward", $"{outlook.Level:0}", "where confidence settles", outlook.Level >= 50 ? "good" : outlook.Level >= 35 ? "warn" : "bad"));
        return chips;
    }

    /// <summary>How confidence moved over the last four weekly reviews.</summary>
    public static string Recent(JobState job)
    {
        var h = job.History;
        if (h.Count < 2) return "";
        int back = Math.Min(4, h.Count - 1);
        double change = h[^1].Confidence - h[h.Count - 1 - back].Confidence;
        string weeks = back == 1 ? "last week" : $"the last {back} weeks";
        return Math.Abs(change) < 0.5 ? $"Steady over {weeks}." : $"{(change > 0 ? "Up" : "Down")} {Math.Abs(change):0} over {weeks}.";
    }

    private static string Bio(CoachBackground b) => b switch
    {
        CoachBackground.ExAllStar => "former All-Star",
        CoachBackground.ExPro => "former pro",
        CoachBackground.CollegeStar => "former college star",
        _ => "no playing career",
    };

    /// <summary>H144 (run 229): "a season · year 1 of 3 after going down" for the parachute chip.</summary>
    public static string ParachuteNote(Hardwood.Core.Model.ClubFinance m) =>
        m.ParachuteYears > 0 ? $"a season · year {Math.Clamp(m.ParachuteYear, 1, m.ParachuteYears)} of {m.ParachuteYears} after going down" : "a season after going down";
}
