using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hardwood.Core.Model;
using Hardwood.Core.People;

namespace Hardwood.Desktop.ViewModels;

public sealed partial class MoodRow : ObservableObject
{
    public required Player Player { get; init; }
    public Team? Team { get; init; }
    public DateOnly Today { get; init; }
    public string Name => Player.Name;
    public string Pos => Player.PrimaryPosition.ToString();
    public string Mood => PeopleEngine.Mood(Morale);   // the shown number, so a 49.6 shown as 50 reads "Content" (code review)
    public int Morale => (int)Math.Round(Player.Morale);
    /// <summary>Run 74: the face and colour follow the mood word's bands (PeopleEngine.Mood and the chips: Happy 68+,
    /// Content 50+, Unhappy below 50); before, the face turned green at 60, so a "Content" 62 looked happy.</summary>
    public static int Band(double morale) => Math.Round(morale) >= 68 ? 2 : Math.Round(morale) >= 50 ? 1 : 0;
    public static string BandColor(double morale) => Band(morale) switch { 2 => "#3FB950", 1 => "#D29922", _ => "#F85149" };
    public string MoodIcon => Band(Player.Morale) switch { 2 => "Smile", 1 => "Meh", _ => "Frown" };
    public Avalonia.Media.IBrush MoodBrush => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(BandColor(Player.Morale)));
    public string MainReason { get; init; } = "";
    public string Status { get; init; } = "";
    public bool HasStatus => Status.Length > 0;
    public bool StatusBad => Player.TradeRequested;
    public bool HasWorry => MainReason.Length > 0;
    public string ShortName => Player.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries) is { Length: > 1 } n ? $"{n[0][0]}. {string.Join(' ', n.Skip(1))}" : Player.Name;
    public string CardLine => $"{Pos} · {Player.Age(Today)} · {Mood}";
    public string WorryLine => HasWorry ? MainReason : "Nothing on his mind";
    public string CardTip => $"{Player.Name}: {Mood.ToLower()} ({Morale}/100)" + (HasWorry ? $". Biggest worry: {MainReason}" : "") + (HasStatus ? $". {Status}" : "") + ". Click to talk to him.";
    [ObservableProperty] private bool _picked;
}

/// <summary>Run 74: one lane of the dressing room (unsettled, content or happy), worst first.</summary>
public sealed class MoodLane
{
    public required string Title { get; init; }
    public required string Note { get; init; }
    public required string Color { get; init; }
    public Avalonia.Media.IBrush Brush => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(Color));
    public List<MoodRow> Rows { get; init; } = new();
}

public sealed class TalkButton
{
    public required string Label { get; init; }
    public required IRelayCommand Command { get; init; }
    /// <summary>Why the topic can't be raised now (greyed out with this as its tooltip, review #14 bug #7), or what it does.</summary>
    public string Tip { get; init; } = "";
}

public sealed class FactorRow
{
    public required string Reason { get; init; }
    public required double Effect { get; init; }
    public string EffectText => Effect >= 0 ? $"+{Effect:0}" : $"{Effect:0}";
    public bool Positive => Effect >= 0;
    /// <summary>Run 74: the bar's length, against the biggest factor in the list (0–<see cref="SquadViewModel.FactorBarMax"/> px).</summary>
    public double BarWidth { get; init; }
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
    public const double FactorBarMax = 90;
    /// <summary>Run 74 (review #13 rank 11): the page opens on the dressing room as cards; the table is one switch away.</summary>
    public string[] Views { get; } = { "Cards", "Full table" };
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowCards), nameof(ShowTable))] private string _view = "Cards";
    public bool ShowCards => View == "Cards";
    public bool ShowTable => !ShowCards;
    public ObservableCollection<MoodLane> Lanes { get; } = new();
    [ObservableProperty] private double _chemistry;
    [ObservableProperty] private string _chemistryWord = "";
    [ObservableProperty] private string _chemistryNumber = "";
    [ObservableProperty] private Avalonia.Media.Color _chemistryColor;
    [ObservableProperty] private string _moraleSummary = "";
    /// <summary>Roadmap 1: the dressing room at a glance (chemistry, moods, trade requests, promises).</summary>
    [ObservableProperty] private List<Hardwood.Desktop.Controls.Chip> _chips = new();
    public Avalonia.Media.Color Accent => ThemeService.AccentFor(Session.HumanTeam);
    public ObservableCollection<FactorRow> ChemistryFactors { get; } = new();
    public ObservableCollection<MoodRow> Players { get; } = new();
    [ObservableProperty] private MoodRow? _selected;
    partial void OnSelectedChanged(MoodRow? value)
    {
        foreach (var r in Players) r.Picked = r == value;
        ShowPlayer();
    }
    /// <summary>A card was clicked: he's the one in the side panel (talk to him there).</summary>
    public void Pick(MoodRow row) => Selected = row;
    public ObservableCollection<FactorRow> PlayerFactors { get; } = new();
    public ObservableCollection<TalkButton> Topics { get; } = new();
    [ObservableProperty] private string _playerHeadline = "";
    [ObservableProperty] private string _playerScore = "";
    [ObservableProperty] private string _reply = "";
    [ObservableProperty] private string _talkNote = "";
    [ObservableProperty] private string _promisesText = "";
    [ObservableProperty] private IReadOnlyList<Hardwood.Desktop.Controls.LineSeries>? _trend;
    [ObservableProperty] private string[]? _trendLabels;
    [ObservableProperty] private bool _hasTrend;

    private double WinPct => Session.MyRecord.Games > 0 ? Session.MyRecord.WinPct : 0.5;

    public List<Hardwood.Desktop.Controls.Chip> BuildChips(Hardwood.Core.Model.Team team, string chemistryLabel)
    {
        var roster = team.Roster;
        // The same bands as the Mood column (PeopleEngine.Mood): Happy 68+, Unhappy below 50.
        int happy = roster.Count(p => MoodRow.Band(p.Morale) == 2), unhappy = roster.Count(p => MoodRow.Band(p.Morale) == 0 && !p.TradeRequested),
            requests = roster.Count(p => p.TradeRequested), promises = Session.Promises.Count(pr => roster.Any(p => p.Id == pr.PlayerId));
        var lowest = roster.OrderBy(p => p.Morale).FirstOrDefault();
        var chips = new List<Hardwood.Desktop.Controls.Chip>
        {
            new("Chemistry", $"{team.Chemistry:0}", chemistryLabel + " · out of 100", team.Chemistry >= 62 ? "good" : team.Chemistry >= 48 ? "" : "bad"),
            new("Happy", happy.ToString(), $"of {roster.Count} players", happy >= roster.Count / 2 ? "good" : ""),
            new("Unhappy", unhappy.ToString(), unhappy == 0 ? "nobody unsettled" : lowest != null ? $"lowest: {lowest.Name.Split(' ').Last()} ({lowest.Morale:0})" : "",
                roster.Any(p => p.Morale < 35 && !p.TradeRequested) ? "bad" : unhappy > 0 ? "warn" : "good"),
        };
        // Danny, Nov 22: no college transfers, so a college program has no "wants out" count.
        if (!Session.InCollege) chips.Add(new("Trade requests", requests.ToString(), requests == 0 ? "nobody wants out" : "want out", requests > 0 ? "bad" : "good"));
        chips.Add(new("Promises", promises.ToString(), promises == 0 ? "none outstanding" : "you must keep them", promises > 0 ? "info" : ""));
        return chips;
    }

    public override void Refresh()
    {
        var team = Session.HumanTeam;
        string label = team.Chemistry >= 75 ? "excellent" : team.Chemistry >= 62 ? "good" : team.Chemistry >= 48 ? "average" : "poor";
        ChemistryHeadline = $"Team chemistry: {label} ({team.Chemistry:0}/100)";
        Chemistry = team.Chemistry;
        ChemistryWord = char.ToUpper(label[0]) + label[1..];
        ChemistryNumber = $"{team.Chemistry:0} / 100";
        ChemistryColor = Avalonia.Media.Color.Parse(team.Chemistry >= 62 ? "#3FB950" : team.Chemistry >= 48 ? "#D29922" : "#F85149");
        ChemistryFactors.Clear();
        var factors = PeopleEngine.ChemistryFactors(team, Session.InCollege).Concat(Session.SpiritFactors()).OrderByDescending(x => Math.Abs(x.effect)).ToList();
        double big = factors.Count == 0 ? 1 : Math.Max(1, factors.Max(x => Math.Abs(x.effect)));
        foreach (var (r, e) in factors)
            ChemistryFactors.Add(new FactorRow { Reason = r, Effect = e, BarWidth = Math.Max(3, FactorBarMax * Math.Abs(e) / big) });

        var keep = Selected?.Player;
        Players.Clear();
        foreach (var p in team.Roster.OrderBy(p => p.Morale))
        {
            Session.MyStats.TryGetValue(p.Id, out var st);
            var worst = PeopleEngine.Factors(p, team, WinPct, st, Session.League.Finance, Session.Today, Session.HumanLeague?.Profile.SalaryScale ?? 1, Session.InCollege).OrderBy(x => x.effect).FirstOrDefault();
            var promise = Session.Promises.FirstOrDefault(x => x.PlayerId == p.Id);
            Players.Add(new MoodRow
            {
                Player = p,
                Team = team,
                Today = Session.Today,
                MainReason = worst.reason != null && worst.effect < 0 ? worst.reason : "",
                Status = p.TradeRequested && !Session.InCollege ? "Wants a trade" : promise != null ? (promise.Kind == "trade" ? "Promised a trade" : $"Promised {promise.TargetMpg:0} min") : "",
            });
        }
        Lanes.Clear();
        foreach (var (band, title, note) in new[]
        {
            (0, "Unsettled", "below 50: talk to them before it spreads"),
            (1, "Content", "50 to 67"),
            (2, "Happy", "68 and up"),
        })
        {
            var rows = Players.Where(r => MoodRow.Band(r.Player.Morale) == band).ToList();
            if (rows.Count > 0)
                Lanes.Add(new MoodLane { Title = $"{title} ({rows.Count})", Note = note, Color = MoodRow.BandColor(band switch { 2 => 100, 1 => 50, _ => 0 }), Rows = rows });
        }
        MoraleSummary = team.Roster.Count == 0 ? "" : $"Average morale {team.Roster.Average(p => p.Morale):0} · " +
            string.Join(" · ", Lanes.Select(l => l.Title.ToLower()));
        Selected = Players.FirstOrDefault(r => r.Player == keep) ?? Players.FirstOrDefault();
        PromisesText = Session.Promises.Count == 0 ? "No promises outstanding." : string.Join("\n", Session.Promises.Select(pr =>
        {
            var p = team.Roster.FirstOrDefault(x => x.Id == pr.PlayerId);
            return p == null ? "" : pr.Kind == "trade" ? $"{p.Name}: trade him by {pr.Made.AddDays(30):MMM d}" : $"{p.Name}: about {pr.TargetMpg:0} minutes a game over his next {pr.Games} games";
        }).Where(x => x.Length > 0));
        Chips = BuildChips(team, label);
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
        if (p == null) { PlayerHeadline = ""; PlayerScore = ""; return; }
        Session.MyStats.TryGetValue(p.Id, out var st);
        // Bug list #14: the score is its own unwrapped text, so "(61/100)" never breaks mid-number in the narrow panel.
        PlayerHeadline = $"{p.Name}: {PeopleEngine.Mood(p.Morale).ToLower()}" + (p.TradeRequested && !Session.InCollege ? ", wants a trade" : "");
        PlayerScore = $"{p.Morale:0}/100";
        foreach (var (r, e) in PeopleEngine.Factors(p, Session.HumanTeam, WinPct, st, Session.League.Finance, Session.Today, Session.HumanLeague?.Profile.SalaryScale ?? 1, Session.InCollege).OrderBy(x => x.effect))
            PlayerFactors.Add(new FactorRow { Reason = r, Effect = e });
        var blocked = Talks.Unavailable(p, Session.Today);
        foreach (var topic in Talks.Available(p, Session.InCollege))
        {
            var t = topic;
            Topics.Add(new TalkButton { Label = Talks.Label(t), Command = new RelayCommand(() => Talk(p, t), () => blocked == null), Tip = blocked ?? $"Talk to {p.Name.Split(' ').Last()}" });
        }
        TalkNote = blocked ?? "Personality matters: professionals take criticism well, hot-heads don't, and a broken promise costs his trust for three months.";
    }

    private void Talk(Player p, TalkTopic topic)
    {
        var r = Session.Talk(p, topic);
        Reply = r.Reply;
        Refresh();
    }
}
