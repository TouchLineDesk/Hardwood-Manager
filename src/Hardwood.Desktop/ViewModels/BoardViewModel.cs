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
    [ObservableProperty] private int _patience;
    [ObservableProperty] private int _ambition;
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

    public static (string label, string colour) Mood(double c) => c switch
    {
        >= 70 => ("Secure", "#3FB950"),
        >= 50 => ("Stable", "#A3D977"),
        >= 35 => ("Under pressure", "#D29922"),
        _ => ("Critical", "#F85149"),
    };

    public override void Refresh()
    {
        var s = Session; var job = s.Job; var coach = s.Coach;
        Employed = s.Employed;
        Team = s.Employed ? s.HumanTeam : null;
        if (s.Employed)
        {
            var t = s.HumanTeam; var o = t.Owner; var rec = s.MyRecord; var ctx = s.ContextOf(t);
            OwnerName = o.Name; OwnerStyle = o.Style; Patience = o.Patience; Ambition = o.Ambition;
            var e = job.Current;
            Goal = e == null ? "" : $"{(e.Survive ? "Stay up (newly promoted)" : Board.Describe(e.Goal, ctx))} · around {e.TargetWins} wins";
            Pace = rec.Games > 0 ? $"{rec.Wins}-{rec.Losses}: on pace for {rec.WinPct * ctx.SeasonGames:0} wins of {ctx.SeasonGames}" : "The season hasn't started.";
            Demands.Clear();
            if (e != null)
                foreach (var d in e.Demands)
                    Demands.Add(new DemandRow { Text = Board.Describe(d), Met = Board.Meeting(d, ctx, t, s.MyStats, s.Today, rec.Wins, rec.Games) });
            Confidence = job.Confidence;
            var (label, colour) = Mood(job.Confidence);
            ConfidenceText = $"{label} ({job.Confidence:0}/100)";
            ConfidenceBrush = new SolidColorBrush(Color.Parse(colour));
            var m = t.Money;
            static string M(long v) => Math.Abs(v) >= 10_000_000 ? $"${v / 1e6:F0}M" : $"${v / 1e6:F1}M";
            long wages = Hardwood.Core.Season.ContractMarket.Payroll(t);
            Finances = $"Revenue {M(m.Revenue)} a season · wage budget {M(m.WageBudget)} · wages {M(wages)} · "
                       + $"transfer budget {M(m.TransferBudget)} · bank balance {M(m.Balance)} · fan base {m.FanBase:0}/100";
            FinanceChips = new List<Hardwood.Desktop.Controls.Chip>
            {
                new("Revenue", M(m.Revenue), "a season", "team"),
                new("Wages", M(wages), $"of a {M(m.WageBudget)} budget", wages > m.WageBudget ? "bad" : wages > m.WageBudget * 0.95 ? "warn" : "good"),
                new("Wage room", M(Math.Max(0, m.WageBudget - wages)), wages > m.WageBudget ? $"{M(wages - m.WageBudget)} over" : "to spend on wages", wages > m.WageBudget ? "bad" : ""),
                new("Transfer budget", M(m.TransferBudget), "for transfer fees"),
                new("Bank", M(m.Balance), m.Balance < 0 ? "in debt" : "balance", m.Balance < 0 ? "bad" : ""),
                new("Fan base", $"{m.FanBase:0}", "out of 100", m.FanBase >= 70 ? "good" : m.FanBase < 35 ? "warn" : ""),
            };
            OwnerTitle = s.InCollege ? "Athletic director" : "The owner";
            FinanceTitle = s.InCollege ? "The program" : "Club finances";
            ConfidenceTitle = s.InCollege ? "AD confidence" : "Board confidence";
            FinanceNote = s.InCollege
                ? "College players are paid nothing (the classic era): the program's pull is its prestige, its conference and its winning. The athletic director judges the season by March Madness."
                : "Revenue comes from the league, the arena, the fans and results. The owner sets the wage budget from it each summer: ambitious owners spend more, careful ones keep a margin.";
            if (s.InCollege) FinanceChips = ProgramChips(s);
            var outlook = Board.Outlook(job, ctx, t, rec.Wins, rec.Losses, s.MyStats, s.Today);
            ConfidenceDrivers = outlook == null ? new() : DriverChips(outlook);
            ConfidenceHeading = outlook == null
                ? "The board starts judging the season after five games."
                : $"Heading toward {outlook.Level:0} (now {job.Confidence:0}): each week it closes {outlook.Rate * 100:0}% of the gap. "
                  + (o.Patience <= 7 ? $"An impatient {s.BossTitle}: it moves fast." : o.Patience >= 14 ? $"A patient {s.BossTitle}: it moves slowly." : "");
            ConfidenceRecent = Recent(job);
            RefreshFacilities();
            HasTrend = job.History.Count >= 2;
            ConfidenceTrend = HasTrend ? new[] { new LineSeries { Name = "Board confidence", Values = job.History.Select(x => x.Confidence).ToArray(), Color = ThemeService.Accent, Emphasis = true } } : null;
        }

        else HasFacilities = false;
        CoachName = coach.Name;
        CoachBio = $"Age {coach.Age(s.Today)} · {coach.Nationality} · {Bio(coach.Background)} · licence {coach.Licence}";
        Reputation = coach.Reputation;
        CoachAttrs.Clear();
        foreach (var a in Enum.GetValues<CoachAttr>()) CoachAttrs.Add(new CoachAttrView { Label = Coach.Label(a), Value = coach[a] });
        JobHistory = coach.History.Count == 0 ? "No previous jobs." :
            string.Join("\n", coach.History.Select(h => $"{h.TeamName}, {h.FromSeason}-{h.ToSeason}: {h.Ended.ToLower()} ({h.Wins}-{h.Losses} in the last season)"));

        Vacancies.Clear();
        foreach (var v in job.Vacancies.Where(v => v.Status is "open" or "applied" or "offered"))
        {
            var t = s.FindTeam(v.TeamId)!;
            var vctx = s.ContextOf(t);
            double chance = Board.HireChance(coach, vctx, t);
            var vrec = s.League.Teams.Contains(t) ? s.Standings[t] : s.World.LeagueOf(t) is { } lc ? s.TableOf(lc)[t] : new Hardwood.Core.Season.TeamRecord { Team = t };
            int id = v.TeamId;
            Vacancies.Add(new VacancyRow
            {
                Team = t,
                Detail = $"{vctx.Name} · owner: {t.Owner.Name} ({t.Owner.Style.ToLower()}) · {vrec.Wins}-{vrec.Losses} · " + (chance >= 0.6 ? "good chance" : chance >= 0.3 ? "worth a try" : "long shot"),
                Status = v.Status switch { "applied" => $"Applied · decision by {v.DecideOn:MMM d}", "offered" => $"Offer! Answer by {v.DecideOn:MMM d}", _ => $"Open since {v.Opened:MMM d}" },
                CanApply = v.Status == "open", HasOffer = v.Status == "offered",
                Apply = new RelayCommand(() => { Status = Session.Apply(id) ?? "Application sent."; Refresh(); Shell.AfterTick(); }),
                Accept = new RelayCommand(() => { Status = Session.AcceptOffer(id) ?? "You have a new job."; ThemeService.Apply(Session.HumanTeam); Shell.AfterTick(); Refresh(); }),
                Decline = new RelayCommand(() => { Session.DeclineOffer(id); Status = "Offer declined."; Refresh(); }),
            });
        }
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
            new("Owner will fund", M(budget), $"keeps {M(reserve)} in the bank", budget > 0 ? "good" : ""),
            new("Running costs", M(Hardwood.Core.Season.Franchise.Upkeep(f, s.MyLeagueRevenue)), "a season", ""),
            new("Invested", M(f.LastInvested), "last summer", ""),
            new("Arena", $"{t.ArenaCapacity:N0}", $"seats · {f.ArenaYears} years old", f.ArenaYears >= Hardwood.Core.Season.Franchise.ArenaLife ? "warn" : "team"),
        };
        Facilities.Clear();
        foreach (var k in Hardwood.Core.Model.ClubFacilities.Kinds)
        {
            var fac = f.Get(k); var info = Hardwood.Core.Season.Franchise.Info(k);
            var build = f.Building.FirstOrDefault(b => b.Kind == k || (k == Hardwood.Core.Model.FacilityKind.Arena && b.Kind == Hardwood.Core.Model.FacilityKind.NewArena));
            int left = info.Lifespan - fac.Age;
            string status = build != null ? (build.Kind == Hardwood.Core.Model.FacilityKind.NewArena ? $"New arena: opens in {build.SeasonsLeft} summer{(build.SeasonsLeft == 1 ? "" : "s")}" : build.Renewal ? "Being renewed: done next summer" : $"Building level {build.ToLevel}: opens next summer")
                : left <= 1 ? "Dated: drops a level next summer unless it's renewed" : left <= 3 ? $"Ageing: dates in {left} seasons" : $"Modern: good for {left} more seasons";
            string tone = build != null ? "#58A6FF" : left <= 1 ? "#F85149" : left <= 3 ? "#D29922" : "#3FB950";
            Facilities.Add(new FacilityRow
            {
                Name = info.Name, Pitch = info.Pitch, Level = fac.Level,
                LevelText = $"{fac.Level}/5 · {(fac.Edge >= 0.05 ? "+" : "")}{fac.Edge:0.0} on the league",
                Effect = info.Effect, Status = status, StatusBrush = new SolidColorBrush(Color.Parse(tone)),
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
        _refreshing = true; GmDecides = s.Delegated.Facilities; _refreshing = false;
    }

    /// <summary>Each thing the board weighs as a chip: the points it adds to or takes off where confidence settles.</summary>
    /// <summary>A college program at a glance: prestige, national rank, records, and next summer's graduates.</summary>
    public static List<Hardwood.Desktop.Controls.Chip> ProgramChips(Hardwood.Core.Season.GameSession s)
    {
        var cw = s.World.College!; var t = s.HumanTeam;
        double prestige = cw.Prestige.GetValueOrDefault(t.Id, 50);
        int rank = cw.Programs.OrderByDescending(p => cw.Season.Elo.GetValueOrDefault(p.Id, 1500)).ToList().IndexOf(t) + 1;
        var rec = s.MyRecord; var conf = s.MyStandings[t];
        int seniors = t.Roster.Count(p => p.Age(new DateOnly(cw.Season.Year + 1, 7, 1)) >= Hardwood.Core.College.CollegeWorld.GraduateAge);
        return new List<Hardwood.Desktop.Controls.Chip>
        {
            new("Prestige", $"{prestige:0}", prestige >= 75 ? "a blue blood" : prestige >= 55 ? "a known name" : "building a name", prestige >= 75 ? "good" : prestige < 40 ? "warn" : "team"),
            new("National rank", $"#{rank}", $"of {cw.Programs.Count} programs", rank <= 25 ? "good" : ""),
            new("Overall", $"{rec.Wins}-{rec.Losses}", "this season", rec.Games > 0 && rec.WinPct >= 0.6 ? "good" : rec.Games > 0 && rec.WinPct < 0.4 ? "bad" : ""),
            new("Conference", $"{conf.Wins}-{conf.Losses}", s.MyConference!.Short, conf.Games > 0 && conf.WinPct >= 0.6 ? "good" : ""),
            new("Graduating", $"{seniors}", "turn pro next summer", "info"),
        };
    }

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
}
