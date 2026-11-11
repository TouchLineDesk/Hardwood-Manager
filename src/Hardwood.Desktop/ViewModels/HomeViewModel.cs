using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hardwood.Core.Engine;
using Hardwood.Core.Model;
using Hardwood.Core.People;
using Hardwood.Core.Season;
using Hardwood.Desktop.Controls;

namespace Hardwood.Desktop.ViewModels;

public sealed class HomeRow
{
    public string Icon { get; init; } = "";
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
    public bool Highlight { get; init; }
    public IBrush IconBrush { get; init; } = Brushes.Gray;
    /// <summary>Roadmap 1: a coloured category badge (BOARD, MEDICAL, GAME…) so the Latest column scans like a news ticker.</summary>
    public string Badge { get; init; } = "";
    public IBrush BadgeBrush { get; init; } = Brushes.Gray;

    public static IBrush CategoryBrush(string category) => new SolidColorBrush(Color.Parse(category switch
    {
        "Board" => "#D29922", "Medical" => "#F85149", "Squad" => "#A371F7", "Transactions" or "Roster" => "#58A6FF",
        "League" => "#3FB950", "Awards" => "#E3B341", "World" => "#6E7A8A", "Draft" => "#58A6FF", _ => "#3A4654",
    }));
}

public sealed class TableRow
{
    public int Rank { get; init; }
    public Team? Team { get; init; }
    /// <summary>A shorter name where space is tight (a college program goes by its school, C2).</summary>
    public string? Label { get; init; }
    public string Name => Label ?? Team?.FullName ?? "";
    public string Record { get; init; } = "";
    public string Diff { get; init; } = "";
    public bool Ours { get; init; }
}

/// <summary>
/// Home: the start-of-day dashboard (decision #69): next game and the scouting line, a standings snapshot, squad
/// pulse (chemistry, unhappy players), injuries, the latest news, and charts for form, team vs league, payroll and mood.
/// </summary>
public sealed partial class HomeViewModel : PageViewModel
{
    public override string Title => "Home";
    public HomeViewModel(MainViewModel shell) : base(shell) { }

    public Team Team => Session.HumanTeam;
    [ObservableProperty] private string _headline = "";
    [ObservableProperty] private string _subline = "";
    [ObservableProperty] private Color _glow;
    [ObservableProperty] private Team? _opponent;
    [ObservableProperty] private string _nextGame = "";
    [ObservableProperty] private string _nextDetail = "";
    [ObservableProperty] private string _scouting = "";
    [ObservableProperty] private string _chemistryText = "";
    public ObservableCollection<TableRow> Table { get; } = new();
    public ObservableCollection<HomeRow> Pulse { get; } = new();
    public ObservableCollection<HomeRow> Injuries { get; } = new();
    public ObservableCollection<HomeRow> News { get; } = new();
    /// <summary>"On this day" (roadmap 4): records and milestones from earlier seasons around today's date.</summary>
    [ObservableProperty] private List<string> _onThisDay = new();
    public bool HasOnThisDay => OnThisDay.Count > 0;
    partial void OnOnThisDayChanged(List<string> value) => OnPropertyChanged(nameof(HasOnThisDay));
    [ObservableProperty] private IReadOnlyList<LineSeries>? _form;
    [ObservableProperty] private string[]? _formLabels;
    [ObservableProperty] private IReadOnlyList<Bar>? _comparison;
    [ObservableProperty] private IReadOnlyList<Bar>? _payroll;
    [ObservableProperty] private IReadOnlyList<RefLine>? _payrollLines;
    /// <summary>"Payroll committed", or "Roster by class" at a college program (C2).</summary>
    [ObservableProperty] private string _payrollTitle = "Payroll committed";
    [ObservableProperty] private string _payrollIcon = "DollarSign";
    [ObservableProperty] private IReadOnlyList<LineSeries>? _mood;
    [ObservableProperty] private string[]? _moodLabels;
    [ObservableProperty] private bool _hasMood;
    [ObservableProperty] private string _tableTitle = "Conference";

    private static readonly IBrush Good = new SolidColorBrush(Color.Parse("#3FB950"));
    private static readonly IBrush Bad = new SolidColorBrush(Color.Parse("#F85149"));
    private static readonly IBrush Warn = new SolidColorBrush(Color.Parse("#D29922"));
    private static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#8B96A5"));

    public override void Refresh()
    {
        var s = Session; var team = Team; var rec = s.MyRecord;
        Glow = ThemeService.Accent;
        var conf = s.MyTable;
        int seed = s.MyRank;
        TableTitle = s.InNabl ? $"{team.Conference} Conference" : s.HumanLeagueName;
        Headline = team.FullName;
        if (s.InCollege && rec.Games > 0)
        {
            // College: the overall record, then the conference race once conference play has begun.
            var cr = s.MyStandings[team];
            Subline = $"{rec.Wins}-{rec.Losses} · " + (cr.Games > 0 ? $"{Ordinal(seed)} in the {s.MyConference!.Short} ({cr.Wins}-{cr.Losses})" : "conference play starts in January")
                + $" · {(rec.PointDiff >= 0 ? "+" : "")}{rec.PointDiff:0.0} per game";
        }
        else Subline = rec.Games > 0 ? $"{rec.Wins}-{rec.Losses} · {Ordinal(seed)} in the {s.MyTableName} · {(rec.PointDiff >= 0 ? "+" : "")}{rec.PointDiff:0.0} per game"
            : $"{s.HumanLeagueName} · season {s.League.Season}-{(s.League.Season + 1) % 100:00}";

        var next = s.NextMyGame;
        if (next != null)
        {
            var opp = next.Opponent;
            Opponent = opp;
            var orec = s.RecordOf(opp);
            NextGame = $"{(next.Home ? "vs" : "at")} {opp.FullName}";
            NextDetail = $"{next.Date:ddd MMM d}{(next.Date == s.Today ? " (today)" : "")}" + (next.Competition != s.HumanLeagueName ? $" · {next.Competition}" : "")
                + (orec.Games > 0 ? $" · they're {orec.Wins}-{orec.Losses}" : "");
            var report = CoachAi.ScoutingReport(team, opp, next.Date);
            Scouting = report.Split(". ").FirstOrDefault() is { } first ? first.TrimEnd('.') + "." : report;
        }
        else { Opponent = null; NextGame = "No game scheduled"; NextDetail = s.Phase == SessionPhase.Offseason ? "It's the offseason." : ""; Scouting = ""; }

        Table.Clear();
        int i = 0;
        foreach (var r in conf)
        {
            i++;
            if (i > 8 && r.Team != team) continue;
            Table.Add(new TableRow { Rank = i, Team = r.Team, Label = s.InCollege ? r.Team.City : null, Record = $"{r.Wins}-{r.Losses}", Diff = $"{(r.PointDiff >= 0 ? "+" : "")}{r.PointDiff:0.0}", Ours = r.Team == team });
        }

        string chem = team.Chemistry >= 75 ? "excellent" : team.Chemistry >= 62 ? "good" : team.Chemistry >= 48 ? "average" : "poor";
        ChemistryText = $"Chemistry {chem} ({team.Chemistry:0})";
        Pulse.Clear();
        foreach (var p in team.Roster.Where(p => p.TradeRequested || p.Morale < 45).OrderBy(p => p.Morale).Take(5))
            Pulse.Add(new HomeRow { Icon = p.Morale < 38 ? "Frown" : "Meh", IconBrush = p.Morale < 38 ? Bad : Warn, Title = p.Name,
                Detail = p.TradeRequested ? "Wants a trade" : PeopleEngine.Mood(p.Morale) });
        if (Pulse.Count == 0) Pulse.Add(new HomeRow { Icon = "Smile", IconBrush = Good, Title = "Everyone is content", Detail = "" });

        Injuries.Clear();
        foreach (var p in team.Roster.Where(p => !p.IsAvailable(s.Today)).OrderBy(p => p.Injury!.ReturnDate))
            Injuries.Add(new HomeRow { Icon = "Bandage", IconBrush = Bad, Title = p.Name, Detail = $"{p.Injury!.Name}, back {p.Injury.ReturnDate:MMM d}" });
        if (Injuries.Count == 0) Injuries.Add(new HomeRow { Icon = "Activity", IconBrush = Good, Title = "No injuries", Detail = "" });

        News.Clear();
        foreach (var m in s.Inbox.Take(6))
            News.Add(new HomeRow
            {
                Icon = m.Category switch { "Squad" => "HeartHandshake", "World" => "Globe", "Draft" => "ClipboardPen", "Roster" => "Users", "League" => "Trophy", "Board" => "Shield", _ => "Newspaper" },
                IconBrush = m.Read ? Muted : new SolidColorBrush(ThemeService.Accent), Title = m.Title, Detail = m.Date.ToString("MMM d"), Highlight = !m.Read,
                Badge = m.Category.ToUpperInvariant(), BadgeBrush = HomeRow.CategoryBrush(m.Category),
            });

        OnThisDay = s.League.Records.OnThisDay(s.Today).Take(3)
            .Select(m => $"{(m.YearsAgo <= 0 ? "Last year" : m.YearsAgo == 1 ? "1 year ago" : $"{m.YearsAgo} years ago")}, {m.Date:MMM d}: {m.Text}").ToList();

        // Form: running point differential over the season's games.
        double run = 0; var vals = new List<double>();
        foreach (var g in s.MyGames().Where(g => g.Played))
        {
            run += g.Us!.Value - g.Them!.Value; vals.Add(run);
        }
        Form = vals.Count > 1 ? new[] { new LineSeries { Name = "Point differential", Values = vals.ToArray(), Color = ThemeService.Accent, Emphasis = true } } : null;
        FormLabels = vals.Count > 1 ? Enumerable.Range(1, vals.Count).Select(x => x % 10 == 0 ? x.ToString() : "").ToArray() : null;

        // Team vs league: points scored and allowed per game.
        var all = s.MyStandings.All().Where(r => r.Games > 0).ToList();
        var played = s.InCollege ? s.World.College!.Season.Games.Where(g => g.Played).ToList() : null;
        if (rec.Games > 0 && (all.Count > 0 || played is { Count: > 0 }))
        {
            double lg = played != null ? played.Average(g => (g.HomePts!.Value + g.AwayPts!.Value) / 2.0) : all.Average(r => (double)r.PointsFor / r.Games);
            var accent = ThemeService.Accent; var grey = Color.Parse("#4A5563");
            Comparison = new[]
            {
                new Bar { Label = "We score", Value = (double)rec.PointsFor / rec.Games, Color = accent },
                new Bar { Label = played != null ? "College avg" : "League avg", Value = lg, Color = grey },
                new Bar { Label = "We allow", Value = (double)rec.PointsAgainst / rec.Games, Color = Color.Parse("#F85149") },
            };
        }
        else Comparison = null;

        if (s.InCollege)
        {
            // No wages in college: the roster by class instead, so the coach sees who graduates when (C2).
            var summer = new DateOnly(s.World.College!.Season.Year + 1, 7, 1);
            // By age next summer: 19 = freshmen ... 22 = seniors; 23 and over graduate into the pros then (#162).
            string[] names = { "Fr", "So", "Jr", "Sr", "Final yr" };
            Payroll = names.Select((n, k) =>
            {
                int count = team.Roster.Count(p => Math.Clamp(p.Age(summer), 19, 23) == 19 + k);
                return new Bar { Label = n, Value = count, ValueText = $"{count}", Color = k == 4 ? ThemeService.Accent : Color.Parse("#4A5563") };
            }).ToList();
            PayrollLines = null;
            PayrollTitle = "Roster by class"; PayrollIcon = "GraduationCap";
        }
        else
        {
        PayrollTitle = "Payroll committed"; PayrollIcon = "DollarSign";
        // Payroll committed for this and the next three seasons.
        var fin = s.League.Finance;
        var bars = new List<Bar>();
        for (int k = 0; k < 4; k++)
        {
            // A contract with N years left covers this season and the next N-1.
            long sum = team.Roster.Where(p => p.Contract is { } c && c.Years > k).Sum(p => p.Contract!.Salary);
            bars.Add(new Bar { Label = $"{s.League.Season + k}-{(s.League.Season + k + 1) % 100:00}", Value = sum / 1e6,
                ValueText = sum >= 10_000_000 ? $"${sum / 1e6:0}M" : $"${sum / 1e6:0.0}M", Color = k == 0 ? ThemeService.Accent : Color.Parse("#4A5563") });
        }
        Payroll = bars;
        // Cap and tax lines are NABL rules; clubs abroad just see their wage bill.
        PayrollLines = fin.OpenMarket || s.Abroad
            ? new[] { new RefLine { Label = "Budget", Value = team.Money.WageBudget / 1e6, Color = Color.Parse("#58A6FF") } }
            : new[]
            {
                new RefLine { Label = "Cap", Value = fin.SalaryCap / 1e6, Color = Color.Parse("#58A6FF") },
                new RefLine { Label = "Tax", Value = fin.TaxLine / 1e6, Color = Color.Parse("#F85149") },
            };
        }

        HasMood = s.MoodHistory.Count >= 2;
        Mood = HasMood ? new[]
        {
            new LineSeries { Name = "Chemistry", Values = s.MoodHistory.Select(x => x.Chemistry).ToArray(), Color = ThemeService.Accent, Emphasis = true },
            new LineSeries { Name = "Average morale", Values = s.MoodHistory.Select(x => x.Morale).ToArray(), Color = Color.Parse("#58A6FF") },
        } : null;
        MoodLabels = HasMood ? s.MoodHistory.Select((x, idx) => idx % 4 == 0 ? x.Date.ToString("MMM d") : "").ToArray() : null;
    }

    private static string Ordinal(int n) => n + (n % 100 is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });

    [RelayCommand] private void Go(string page) => Shell.Navigate(page);
}

public static class HomeConverters
{
    public static readonly Avalonia.Data.Converters.IValueConverter Highlight =
        new Avalonia.Data.Converters.FuncValueConverter<bool, IBrush>(b => b ? new SolidColorBrush(Color.FromArgb(40, ThemeService.Accent.R, ThemeService.Accent.G, ThemeService.Accent.B)) : Brushes.Transparent);
    public static readonly Avalonia.Data.Converters.IValueConverter Bold =
        new Avalonia.Data.Converters.FuncValueConverter<bool, FontWeight>(b => b ? FontWeight.SemiBold : FontWeight.Normal);
}
