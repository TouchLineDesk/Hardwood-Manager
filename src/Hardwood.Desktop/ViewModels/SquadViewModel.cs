using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hardwood.Core.Model;
using Hardwood.Core.People;

namespace Hardwood.Desktop.ViewModels;

public sealed class MoodRow
{
    public required Player Player { get; init; }
    public string Name => Player.Name;
    public string Pos => Player.PrimaryPosition.ToString();
    public string Mood => PeopleEngine.Mood(Player.Morale);
    public int Morale => (int)Math.Round(Player.Morale);
    public string MoodIcon => Player.Morale >= 60 ? "Smile" : Player.Morale >= 45 ? "Meh" : "Frown";
    public Avalonia.Media.IBrush MoodBrush => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(Player.Morale >= 60 ? "#3FB950" : Player.Morale >= 45 ? "#D29922" : "#F85149"));
    public string MainReason { get; init; } = "";
    public string Status { get; init; } = "";
}

public sealed class TalkButton
{
    public required string Label { get; init; }
    public required IRelayCommand Command { get; init; }
}

public sealed class FactorRow
{
    public required string Reason { get; init; }
    public required double Effect { get; init; }
    public string EffectText => Effect >= 0 ? $"+{Effect:0}" : $"{Effect:0}";
    public bool Positive => Effect >= 0;
}

/// <summary>
/// Squad dynamics (decision #64): team chemistry and what drives it, every player's mood and why, and one-on-one
/// talks: praise, criticism, promises of minutes, roles, and handling trade requests.
/// </summary>
public sealed partial class SquadViewModel : PageViewModel
{
    public override string Title => "Squad";
    public SquadViewModel(MainViewModel shell) : base(shell) { }

    [ObservableProperty] private string _chemistryHeadline = "";
    public ObservableCollection<FactorRow> ChemistryFactors { get; } = new();
    public ObservableCollection<MoodRow> Players { get; } = new();
    [ObservableProperty] private MoodRow? _selected;
    partial void OnSelectedChanged(MoodRow? value) => ShowPlayer();
    public ObservableCollection<FactorRow> PlayerFactors { get; } = new();
    public ObservableCollection<TalkButton> Topics { get; } = new();
    [ObservableProperty] private string _playerHeadline = "";
    [ObservableProperty] private string _reply = "";
    [ObservableProperty] private string _talkNote = "";
    [ObservableProperty] private string _promisesText = "";
    [ObservableProperty] private IReadOnlyList<Hardwood.Desktop.Controls.LineSeries>? _trend;
    [ObservableProperty] private string[]? _trendLabels;
    [ObservableProperty] private bool _hasTrend;

    private double WinPct => Session.MyRecord.Games > 0 ? Session.MyRecord.WinPct : 0.5;

    public override void Refresh()
    {
        var team = Session.HumanTeam;
        string label = team.Chemistry >= 75 ? "excellent" : team.Chemistry >= 62 ? "good" : team.Chemistry >= 48 ? "average" : "poor";
        ChemistryHeadline = $"Team chemistry: {label} ({team.Chemistry:0}/100)";
        ChemistryFactors.Clear();
        foreach (var (r, e) in PeopleEngine.ChemistryFactors(team).OrderByDescending(x => Math.Abs(x.effect)))
            ChemistryFactors.Add(new FactorRow { Reason = r, Effect = e });

        var keep = Selected?.Player;
        Players.Clear();
        foreach (var p in team.Roster.OrderBy(p => p.Morale))
        {
            Session.MyStats.TryGetValue(p.Id, out var st);
            var worst = PeopleEngine.Factors(p, team, WinPct, st, Session.League.Finance, Session.Today, Session.HumanLeague?.Profile.SalaryScale ?? 1).OrderBy(x => x.effect).FirstOrDefault();
            var promise = Session.Promises.FirstOrDefault(x => x.PlayerId == p.Id);
            Players.Add(new MoodRow
            {
                Player = p,
                MainReason = worst.reason != null && worst.effect < 0 ? worst.reason : "",
                Status = p.TradeRequested ? "Wants a trade" : promise != null ? (promise.Kind == "trade" ? "Promised a trade" : $"Promised {promise.TargetMpg:0} min") : "",
            });
        }
        Selected = Players.FirstOrDefault(r => r.Player == keep) ?? Players.FirstOrDefault();
        PromisesText = Session.Promises.Count == 0 ? "No promises outstanding." : string.Join("\n", Session.Promises.Select(pr =>
        {
            var p = team.Roster.FirstOrDefault(x => x.Id == pr.PlayerId);
            return p == null ? "" : pr.Kind == "trade" ? $"{p.Name}: trade him by {pr.Made.AddDays(30):MMM d}" : $"{p.Name}: about {pr.TargetMpg:0} minutes a game over his next {pr.Games} games";
        }).Where(x => x.Length > 0));
        var h = Session.MoodHistory;
        HasTrend = h.Count >= 2;
        Trend = HasTrend ? new[]
        {
            new Hardwood.Desktop.Controls.LineSeries { Name = "Chemistry", Values = h.Select(x => x.Chemistry).ToArray(), Color = ThemeService.Accent, Emphasis = true },
            new Hardwood.Desktop.Controls.LineSeries { Name = "Average morale", Values = h.Select(x => x.Morale).ToArray(), Color = Avalonia.Media.Color.Parse("#58A6FF") },
        } : null;
        TrendLabels = HasTrend ? h.Select((x, i) => i % 4 == 0 ? x.Date.ToString("MMM d") : "").ToArray() : null;
        ShowPlayer();
    }

    private void ShowPlayer()
    {
        PlayerFactors.Clear(); Topics.Clear();
        // The grid can clear its selection when it first binds: show the unhappiest player rather than nothing.
        var p = Selected?.Player ?? Players.FirstOrDefault()?.Player;
        if (p == null) { PlayerHeadline = ""; return; }
        Session.MyStats.TryGetValue(p.Id, out var st);
        PlayerHeadline = $"{p.Name}: {PeopleEngine.Mood(p.Morale).ToLower()} ({p.Morale:0}/100)" + (p.TradeRequested ? ", wants a trade" : "");
        foreach (var (r, e) in PeopleEngine.Factors(p, Session.HumanTeam, WinPct, st, Session.League.Finance, Session.Today, Session.HumanLeague?.Profile.SalaryScale ?? 1).OrderBy(x => x.effect))
            PlayerFactors.Add(new FactorRow { Reason = r, Effect = e });
        foreach (var topic in Talks.Available(p))
        {
            var t = topic;
            Topics.Add(new TalkButton { Label = Talks.Label(t), Command = new RelayCommand(() => Talk(p, t)) });
        }
        TalkNote = Talks.Unavailable(p, Session.Today) ?? "Personality matters: professionals take criticism well, hot-heads don't, and a broken promise costs his trust for three months.";
    }

    private void Talk(Player p, TalkTopic topic)
    {
        var r = Session.Talk(p, topic);
        Reply = r.Reply;
        Refresh();
    }
}
