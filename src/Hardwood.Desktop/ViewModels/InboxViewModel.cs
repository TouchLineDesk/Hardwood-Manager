using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Hardwood.Core.Season;

namespace Hardwood.Desktop.ViewModels;

public sealed partial class InboxItem : ObservableObject
{
    public required InboxMessage Message { get; init; }
    public string Date => Message.Date.ToString("MMM d");
    public string Title => Message.Title;
    public string Category => Message.Category;
    public string Body => Message.Body;
    [ObservableProperty] private bool _isRead;
    public string Weight => IsRead ? "Normal" : "SemiBold";
}

public sealed partial class InboxViewModel : PageViewModel
{
    public override string Title => "Inbox";
    public ObservableCollection<InboxItem> Items { get; } = new();
    [ObservableProperty] private InboxItem? _selected;
    [ObservableProperty] private string _summary = "";
    public Avalonia.Media.IBrush TeamBrush => new Avalonia.Media.LinearGradientBrush
    {
        StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative), EndPoint = new Avalonia.RelativePoint(1, 0, Avalonia.RelativeUnit.Relative),
        GradientStops =
        {
            new Avalonia.Media.GradientStop(Avalonia.Media.Color.Parse(Session.HumanTeam.PrimaryColor), 0),
            new Avalonia.Media.GradientStop(Avalonia.Media.Color.Parse("#161B22"), 1),
        },
    };

    public InboxViewModel(MainViewModel shell) : base(shell) { }

    partial void OnSelectedChanged(InboxItem? value)
    {
        if (value == null || value.IsRead) return;
        value.Message.Read = true;
        value.IsRead = true;
        Shell.RefreshHeader();
    }

    public override void Refresh()
    {
        var selectedId = Selected?.Message.Id;
        Items.Clear();
        foreach (var m in Session.Inbox) Items.Add(new InboxItem { Message = m, IsRead = m.Read });
        Selected = Items.FirstOrDefault(i => i.Message.Id == selectedId) ?? Items.FirstOrDefault();
        var s = Session;
        var rec = s.Standings[s.HumanTeam];
        int rank = s.Standings.Conference(s.HumanTeam.Conference).FindIndex(r => r.Team == s.HumanTeam) + 1;
        var hurt = s.HumanTeam.Roster.Where(p => !p.IsAvailable(s.Today)).ToList();
        Summary = $"{s.HumanTeam.FullName} · {rec.Wins}-{rec.Losses}" + (rec.Games > 0 ? $" · {Ordinal(rank)} in the {s.HumanTeam.Conference}" : "")
                  + $" · Payroll ${ContractMarket.Payroll(s.HumanTeam) / 1e6:F1}M of ${s.League.Finance.HardCap / 1e6:F0}M"
                  + (hurt.Count > 0 ? $" · Injured: {string.Join(", ", hurt.Select(p => $"{p.LastName} ({p.Injury!.DaysOut(s.Today)}d)"))}" : "");
    }

    private static string Ordinal(int n) => n % 100 is 11 or 12 or 13 ? $"{n}th" : (n % 10) switch { 1 => $"{n}st", 2 => $"{n}nd", 3 => $"{n}rd", _ => $"{n}th" };
}
