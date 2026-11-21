using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hardwood.Core.Model;
using Hardwood.Core.Season;

namespace Hardwood.Desktop.ViewModels;

/// <summary>A row of the studio's big screen, ready to draw.</summary>
public sealed record DeskRowView(string Label, string Away, string Home, IBrush AwayBrush, IBrush HomeBrush, FontWeight AwayWeight, FontWeight HomeWeight, IBrush Fill, double Size = 22)
{
    public double LabelSize => Size - 3;
    public bool HasLabel => Label != "";
    /// <summary>A line of its own (around college): it takes the screen's whole width.</summary>
    public int Span => Label == "" && Home == "" ? 3 : 1;
}

/// <summary>
/// A show on the HSN studio set (B4a, run 143): the pre-game show and the halftime desk play their beats one at a time on
/// the big screen, with the speaker's line in a bubble. Next, Autorun and Skip to the end, as on the Selection Show.
/// </summary>
public sealed partial class DeskReelViewModel : ObservableObject
{
    public IReadOnlyList<DeskBeat> Beats { get; }
    public Team Away { get; }
    public Team Home { get; }
    /// <summary>The words on the desk's front panel ("SELECTION SHOW" in the render; painted over by the game).</summary>
    public string Plate { get; }
    public IBrush Accent { get; }
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3.5) };

    public DeskReelViewModel(IReadOnlyList<DeskBeat> beats, Team away, Team home, string plate, string accent = "#7C5CFF")
    {
        Beats = beats.Count > 0 ? beats : new[] { new DeskBeat("HSN", plate, Array.Empty<DeskRow>(), null) };
        Away = away; Home = home; Plate = plate.ToUpperInvariant(); Accent = B(accent);
        _timer.Tick += (_, _) => { if (CanNext) Next(); else StopAutorun(); };
    }

    private static IBrush B(string hex) => new SolidColorBrush(Color.Parse(hex));
    private static readonly IBrush Plain = B("#E6EDF3"), Good = B("#3FB950"), None = B("#00000000"), KeyFill = B("#33F0A33A");

    [ObservableProperty, NotifyPropertyChangedFor(nameof(Current), nameof(Kicker), nameof(Title), nameof(Rows), nameof(HasLine), nameof(Speaker),
        nameof(SpeakerBrush), nameof(Text), nameof(BubbleLeft), nameof(Progress), nameof(CanNext), nameof(Note), nameof(HasNote), nameof(RowSize), nameof(LabelSize))]
    private int _index;

    public DeskBeat Current => Beats[Index];
    public string Kicker => Current.Kicker;
    public string Title => Current.Title;
    public string Note => Current.Note;
    public bool HasNote => Current.Note != "";
    public IReadOnlyList<DeskRowView> Rows => Current.Rows.Select(r => new DeskRowView(r.Label, r.Away, r.Home,
        r.Edge < 0 ? Good : Plain, r.Edge > 0 ? Good : Plain,
        r.Edge < 0 ? FontWeight.Bold : FontWeight.Normal, r.Edge > 0 ? FontWeight.Bold : FontWeight.Normal,
        r.Key ? KeyFill : None, RowSize)).ToList();
    /// <summary>Bigger type when the screen has room (a short beat); the nine-row tale of the tape keeps 22.</summary>
    public double RowSize => Current.Rows.Count <= 6 ? 28 : 22;
    public double LabelSize => RowSize - 3;
    public bool HasLine => Current.Line != null;
    public string Speaker => Current.Line?.Speaker.Name ?? "";
    public IBrush SpeakerBrush => Current.Line is { } l ? B(l.Speaker.Colour == "#E6EDF3" ? "#7C5CFF" : l.Speaker.Colour) : Accent;
    public string Text => Current.Line?.Text ?? "";
    /// <summary>The host stands on the left of the set; every analyst speaks from the right.</summary>
    public bool BubbleLeft => Current.Line?.Speaker.Key is null or "host";
    public string Progress => $"{Index + 1} of {Beats.Count} · {Kicker.ToLowerInvariant()}";
    public bool CanNext => Index < Beats.Count - 1;

    [ObservableProperty] private bool _autorunning;
    public string AutorunText => Autorunning ? "Stop" : "Autorun";
    partial void OnAutorunningChanged(bool value) => OnPropertyChanged(nameof(AutorunText));

    [RelayCommand] public void Next() { if (CanNext) Index++; }
    [RelayCommand] public void Back() { if (Index > 0) Index--; }
    [RelayCommand] public void SkipToEnd() { StopAutorun(); Index = Beats.Count - 1; }
    [RelayCommand]
    public void Autorun()
    {
        if (Autorunning) { StopAutorun(); return; }
        if (!CanNext) Index = 0;
        Autorunning = true; _timer.Start();
    }
    public void StopAutorun() { _timer.Stop(); Autorunning = false; }
}
