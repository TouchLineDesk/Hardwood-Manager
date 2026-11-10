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

/// <summary>The Board page (decisions #70-74): owner, expectations, board confidence, your coach profile, and jobs when out of work.</summary>
public sealed partial class BoardViewModel : PageViewModel
{
    /// <summary>The club's money (decision #88).</summary>
    [ObservableProperty] private string _finances = "";
    public override string Title => "Board";
    public BoardViewModel(MainViewModel shell) : base(shell) { }

    [ObservableProperty] private bool _employed;
    [ObservableProperty] private Team? _team;
    [ObservableProperty] private string _ownerName = "";
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

    [ObservableProperty] private string _coachName = "";
    [ObservableProperty] private string _coachBio = "";
    [ObservableProperty] private double _reputation;
    public ObservableCollection<CoachAttrView> CoachAttrs { get; } = new();
    [ObservableProperty] private string _jobHistory = "";

    public ObservableCollection<VacancyRow> Vacancies { get; } = new();
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
            HasTrend = job.History.Count >= 2;
            ConfidenceTrend = HasTrend ? new[] { new LineSeries { Name = "Board confidence", Values = job.History.Select(x => x.Confidence).ToArray(), Color = ThemeService.Accent, Emphasis = true } } : null;
        }

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

    private static string Bio(CoachBackground b) => b switch
    {
        CoachBackground.ExAllStar => "former All-Star",
        CoachBackground.ExPro => "former pro",
        CoachBackground.CollegeStar => "former college star",
        _ => "no playing career",
    };
}
