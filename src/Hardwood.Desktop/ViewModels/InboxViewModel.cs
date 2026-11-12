using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hardwood.Core.Season;

namespace Hardwood.Desktop.ViewModels;

public sealed partial class InboxItem : ObservableObject
{
    public required InboxMessage Message { get; init; }
    public string Date => Message.Date.ToString("MMM d");
    public string Title => Message.Title;
    public string Category => Message.Category;
    public string Icon => Message.Category switch
    {
        "Squad" => "HeartHandshake", "World" => "Globe", "Draft" => "ClipboardPen", "Roster" => "Users", "League" => "Trophy",
        "Board" => "Shield", "Staff" => "Dumbbell", "Game" => "Activity", "Trade" => "ArrowLeftRight", "Injury" => "Bandage", _ => "Newspaper",
    };
    public Avalonia.Media.IBrush IconBrush => Message.Read ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#8B96A5")) : new Avalonia.Media.SolidColorBrush(ThemeService.Accent);
    public string Body => Message.Body;
    [ObservableProperty] private bool _isRead;
    public string Weight => IsRead ? "Normal" : "SemiBold";

    /// <summary>A press question or locker-room moment that came with this message (people part 2).</summary>
    public Hardwood.Core.People.PendingDecision? Decision { get; init; }
    public bool HasDecision => Decision != null;
    public bool DecisionOpen => Decision is { Open: true };
    public string DecisionHeading => Decision == null ? "" : Decision.Kind == "press" ? "Your answer to the press" : "How do you handle it?";
    public List<DecisionButton> Answers { get; init; } = new();
    [ObservableProperty] private string _outcome = "";
}

/// <summary>One answer under a press question or locker-room moment, with what it will do.</summary>
public sealed class DecisionButton
{
    public required string Label { get; init; }
    public required string Hint { get; init; }
    public required IRelayCommand Command { get; init; }
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

    /// <summary>Unread first, your club's business, or everything (critic review: action items were buried under news).</summary>
    public List<string> Filters { get; } = new() { "All messages", "Unread", "Your club (board, squad, staff, medical)", "Games", "League and world" };
    [ObservableProperty] private string _filter = "All messages";
    partial void OnFilterChanged(string value) => Refresh();

    [RelayCommand]
    private void MarkAllRead()
    {
        Session.MarkAllRead();
        Shell.RefreshHeader();
        Refresh();
    }

    // ---- The inbox clean-up (Danny, #243, D4) ----

    /// <summary>What the last clean-up did ("Deleted 34 messages."), shown beside the buttons.</summary>
    [ObservableProperty] private string _cleanupNote = "";
    [ObservableProperty] private string _deleteReadLabel = "Delete read";
    [ObservableProperty] private string _deleteShownLabel = "Delete these";
    [ObservableProperty] private bool _canDeleteSelected;
    [ObservableProperty] private bool _canDeleteRead;
    [ObservableProperty] private bool _canDeleteShown;
    /// <summary>"Delete these" asks once: the first click arms it, the second deletes.</summary>
    private bool _deleteShownArmed;

    [RelayCommand]
    private void DeleteSelected()
    {
        if (Selected == null) return;
        int index = Items.IndexOf(Selected);
        if (!Session.DeleteMessage(Selected.Message.Id)) { CleanupNote = "Answer the question in this message first."; return; }
        CleanupNote = "Message deleted.";
        AfterDelete(index);
    }

    [RelayCommand]
    private void DeleteRead()
    {
        int n = Session.DeleteAllRead();
        CleanupNote = n == 0 ? "No read messages to delete." : $"Deleted {Plural(n, "read message")}.";
        AfterDelete(0);
    }

    [RelayCommand]
    private void DeleteShown()
    {
        if (!_deleteShownArmed)
        {
            _deleteShownArmed = true;
            DeleteShownLabel = $"Click again to delete {Items.Count(i => Session.CanDelete(i.Message))}";
            return;
        }
        var ids = Items.Select(i => i.Message.Id).ToHashSet();
        int n = Session.DeleteWhere(m => ids.Contains(m.Id));
        CleanupNote = $"Deleted {Plural(n, "message")}" + (n < ids.Count ? $"; {Plural(ids.Count - n, "message")} with a question to answer stayed." : ".");
        AfterDelete(0);
    }

    private void AfterDelete(int index)
    {
        Session.SaveNow();
        Shell.RefreshHeader();
        Selected = null;
        Refresh();
        if (Items.Count > 0) Selected = Items[Math.Clamp(index, 0, Items.Count - 1)];
    }

    public List<string> AutoClearOptions { get; } = InboxSettings.AutoClearChoices
        .Select(w => w == 0 ? "Auto-clear: off" : $"Auto-clear read after {w} weeks").ToList();
    public string AutoClear
    {
        get => AutoClearOptions[Math.Max(0, Array.IndexOf(InboxSettings.AutoClearChoices, Session.InboxSettings.AutoClearWeeks))];
        set
        {
            int i = AutoClearOptions.IndexOf(value);
            if (i < 0 || InboxSettings.AutoClearChoices[i] == Session.InboxSettings.AutoClearWeeks) return;
            Session.InboxSettings.AutoClearWeeks = InboxSettings.AutoClearChoices[i];
            Session.SaveNow();
            OnPropertyChanged();
            CleanupNote = i == 0 ? "Auto-clear is off." : $"Every Monday, read messages older than {InboxSettings.AutoClearChoices[i]} weeks are cleared. Unread ones stay.";
        }
    }

    public List<string> ResultsOptions { get; } = new() { "Results every week", "Results every other week" };
    public string ResultsCadence
    {
        get => ResultsOptions[Session.InboxSettings.ResultsEveryOtherWeek ? 1 : 0];
        set
        {
            bool other = value == ResultsOptions[1];
            if (other == Session.InboxSettings.ResultsEveryOtherWeek) return;
            Session.InboxSettings.ResultsEveryOtherWeek = other;
            Session.SaveNow();
            OnPropertyChanged();
            CleanupNote = other ? "Your results arrive every other Monday." : "Your results arrive every Monday.";
        }
    }

    private static string Plural(int n, string word) => $"{n} {word}{(n == 1 ? "" : "s")}";

    private bool Shows(InboxMessage m) => Filter switch
    {
        "Unread" => !m.Read,
        "Your club (board, squad, staff, medical)" => m.Category is "Board" or "Squad" or "Staff" or "Medical" or "Injury" or "Roster" or "Transactions",
        "Games" => m.Category == "Game",
        "League and world" => m.Category is "League" or "World" or "Awards" or "Draft",
        _ => true,
    };

    partial void OnSelectedChanged(InboxItem? value)
    {
        CanDeleteSelected = value != null && Session.CanDelete(value.Message);
        if (value == null || value.IsRead) return;
        value.Message.Read = true;
        value.IsRead = true;
        Shell.RefreshHeader();
    }

    public override void Refresh()
    {
        var selectedId = Selected?.Message.Id;
        Items.Clear();
        foreach (var m in Session.Inbox.Where(Shows)) Items.Add(MakeItem(m));
        Selected = Items.FirstOrDefault(i => i.Message.Id == selectedId) ?? Items.FirstOrDefault();
        CanDeleteSelected = Selected != null && Session.CanDelete(Selected.Message);
        int read = Session.Inbox.Count(m => m.Read && Session.CanDelete(m));
        DeleteReadLabel = read > 0 ? $"Delete read ({read})" : "Delete read";
        CanDeleteRead = read > 0;
        _deleteShownArmed = false;
        int shown = Items.Count(i => Session.CanDelete(i.Message));
        DeleteShownLabel = Filter == "All messages" ? $"Delete all ({shown})" : $"Delete these ({shown})";
        CanDeleteShown = shown > 0;
        var s = Session;
        var rec = s.MyRecord;
        int rank = s.MyRank;
        var hurt = s.HumanTeam.Roster.Where(p => !p.IsAvailable(s.Today)).ToList();
        Summary = $"{s.HumanTeam.FullName} · {rec.Wins}-{rec.Losses}" + (rec.Games > 0 ? $" · {Ordinal(rank)} in the {s.MyTableName}" : "")
                  + $" · {Budgets.Describe(s.HumanTeam)}"
                  + (hurt.Count > 0 ? $" · Injured: {string.Join(", ", hurt.Select(p => $"{p.LastName} ({p.Injury!.DaysOut(s.Today)}d)"))}" : "");
    }

    private InboxItem MakeItem(InboxMessage m)
    {
        var d = Session.Media.ForMessage(m.Id);
        if (d == null || d.TeamId != Session.HumanTeam.Id) return new InboxItem { Message = m, IsRead = m.Read };
        InboxItem? item = null;
        var answers = d.Options.Select((o, i) => new DecisionButton
        {
            Label = o.Label, Hint = o.Hint,
            Command = new RelayCommand(() =>
            {
                item!.Outcome = Session.Answer(d.Id, i);
                Shell.RefreshHeader();
                Refresh();
            }, () => d.Open),
        }).ToList();
        item = new InboxItem { Message = m, IsRead = m.Read, Decision = d, Answers = answers, Outcome = d.Outcome ?? "" };
        return item;
    }

    private static string Ordinal(int n) => n % 100 is 11 or 12 or 13 ? $"{n}th" : (n % 10) switch { 1 => $"{n}st", 2 => $"{n}nd", 3 => $"{n}rd", _ => $"{n}th" };
}
