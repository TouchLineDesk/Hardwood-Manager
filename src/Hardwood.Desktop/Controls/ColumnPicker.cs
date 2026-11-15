using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Hardwood.Desktop.Controls;

/// <summary>
/// Custom views on the big tables (Danny, #465; Phase 3, #653). A "View" button above a table opens a panel with
/// ready-made views (chips) and a tick box per column. Ticking or unticking a column makes it "My view", which the game
/// remembers per table in settings.json (<see cref="AppSettings.TableViews"/>), not in the save.
/// The first column (the player's name) always stays, and so do columns with no header (status marks).
/// "All columns" is the default, so a table you never touch looks exactly as before.
/// </summary>
public sealed class ColumnPicker : StackPanel
{
    public const string AllName = "All columns";
    public const string MineName = "My view";

    /// <summary>Which table this is (the settings key), e.g. "roster", "market".</summary>
    public static readonly StyledProperty<string> KeyProperty = AvaloniaProperty.Register<ColumnPicker, string>(nameof(Key), "");
    /// <summary>The table it controls.</summary>
    public static readonly StyledProperty<DataGrid?> TargetProperty = AvaloniaProperty.Register<ColumnPicker, DataGrid?>(nameof(Target));
    /// <summary>Ready-made views: "Name=Col,Col,Col|Name=Col,…" by column header (the first column is implied).</summary>
    public static readonly StyledProperty<string> PresetsProperty = AvaloniaProperty.Register<ColumnPicker, string>(nameof(Presets), "");

    public string Key { get => GetValue(KeyProperty); set => SetValue(KeyProperty, value); }
    public DataGrid? Target { get => GetValue(TargetProperty); set => SetValue(TargetProperty, value); }
    public string Presets { get => GetValue(PresetsProperty); set => SetValue(PresetsProperty, value); }

    private readonly ToggleButton _toggle = new() { HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Border _panel = new() { Classes = { "panel" }, IsVisible = false, Margin = new Thickness(0, 6, 0, 6) };
    private readonly WrapPanel _chips = new();
    private readonly WrapPanel _boxes = new();
    private readonly TextBlock _note = new() { Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, FontSize = 11.5 };
    private bool _building;

    public ColumnPicker()
    {
        Spacing = 0;
        Margin = new Thickness(0, 0, 0, 6);
        _toggle.IsCheckedChanged += (_, _) => { _panel.IsVisible = _toggle.IsChecked == true; Rebuild(); };
        ToolTip.SetTip(_toggle, "Choose which columns this table shows. Your choice is remembered.");
        var showAll = new Button { Content = "Show all columns", Margin = new Thickness(0, 6, 8, 0) };
        showAll.Click += (_, _) => Choose(AllName);
        var close = new Button { Content = "Done", Margin = new Thickness(0, 6, 0, 0) };
        close.Click += (_, _) => _toggle.IsChecked = false;
        _panel.Child = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = "VIEWS", Classes = { "muted" }, FontSize = 11, FontWeight = FontWeight.SemiBold },
                _chips,
                new TextBlock { Text = "COLUMNS", Classes = { "muted" }, FontSize = 11, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 4, 0, 0) },
                _boxes,
                _note,
                new StackPanel { Orientation = Orientation.Horizontal, Children = { showAll, close } },
            },
        };
        Children.Add(_toggle);
        Children.Add(_panel);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TargetProperty || change.Property == KeyProperty || change.Property == PresetsProperty)
            Apply();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Apply();
    }

    // --- The model (public for tests) ---

    /// <summary>The columns you can switch: every column but the first, "Player" and the unnamed ones.</summary>
    public IReadOnlyList<DataGridColumn> Choosable =>
        Target == null ? [] : Target.Columns.Skip(1).Where(c => HeaderOf(c) is not ("" or "Player")).ToList();

    public static string HeaderOf(DataGridColumn c) => c.Header as string ?? "";

    /// <summary>The ready-made views in order: "All columns", the table's own, then "My view" once you have one.</summary>
    public IReadOnlyList<(string Name, HashSet<string> Columns)> Views
    {
        get
        {
            var all = new HashSet<string>(Choosable.Select(HeaderOf));
            var list = new List<(string, HashSet<string>)> { (AllName, all) };
            foreach (var part in (Presets ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var eq = part.IndexOf('=');
                if (eq <= 0) continue;
                var cols = part[(eq + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                list.Add((part[..eq].Trim(), new HashSet<string>(cols.Where(all.Contains))));
            }
            var mine = Saved;
            // An empty "My view" (every box unticked) is still a view: only the always-shown columns (code review, PR #115).
            if (mine != null && (mine.Shown.Count > 0 || mine.View == MineName)) list.Add((MineName, new HashSet<string>(mine.Shown.Where(all.Contains))));
            return list;
        }
    }

    private TableViewChoice? Saved =>
        string.IsNullOrEmpty(Key) ? null : AppSettings.Current.TableViews.GetValueOrDefault(Key);

    /// <summary>The view in use ("All columns" until you pick another).</summary>
    public string Current
    {
        get
        {
            var name = Saved?.View;
            return name != null && Views.Any(v => v.Name == name) ? name : AllName;
        }
    }

    /// <summary>Use a view by name.</summary>
    public void Choose(string name)
    {
        if (string.IsNullOrEmpty(Key)) return;
        var choice = AppSettings.Current.TableViews.GetValueOrDefault(Key) ?? new TableViewChoice();
        choice.View = name;
        AppSettings.Current.TableViews[Key] = choice;
        AppSettings.Current.Save();
        Apply();
    }

    /// <summary>Show or hide one column; the table's view becomes "My view".</summary>
    public void SetShown(string header, bool shown)
    {
        if (string.IsNullOrEmpty(Key) || Target == null) return;
        var now = new HashSet<string>(Choosable.Where(c => c.IsVisible).Select(HeaderOf));
        if (shown) now.Add(header); else now.Remove(header);
        var choice = AppSettings.Current.TableViews.GetValueOrDefault(Key) ?? new TableViewChoice();
        choice.View = MineName;
        // Kept in the table's own column order, so the saved list reads naturally.
        choice.Shown = Choosable.Select(HeaderOf).Where(now.Contains).ToList();
        AppSettings.Current.TableViews[Key] = choice;
        AppSettings.Current.Save();
        Apply();
    }

    /// <summary>Show the current view's columns on the table and refresh the button and the panel.</summary>
    public void Apply()
    {
        if (Target == null) return;
        var current = Current;
        var cols = Views.First(v => v.Name == current).Columns;
        foreach (var c in Choosable) c.IsVisible = cols.Contains(HeaderOf(c));
        var total = Target.Columns.Count;
        var shown = Target.Columns.Count(c => c.IsVisible);
        _toggle.Content = current == AllName ? "View: all columns ▾" : $"View: {current} ({shown} of {total} columns) ▾";
        Rebuild();
    }

    private void Rebuild()
    {
        if (_building || !_panel.IsVisible || Target == null) return;
        _building = true;
        try
        {
            var current = Current;
            _chips.Children.Clear();
            foreach (var (name, _) in Views)
            {
                var chip = new Button { Content = name, Classes = { "filter" }, Margin = new Thickness(0, 0, 6, 6) };
                if (name == current) chip.Classes.Add("on");
                chip.Click += (_, _) => Choose(name);
                _chips.Children.Add(chip);
            }
            _boxes.Children.Clear();
            var always = Target.Columns.Select(HeaderOf).Where(h => h != "" && !Choosable.Any(c => HeaderOf(c) == h)).ToList();
            foreach (var c in Choosable)
            {
                var header = HeaderOf(c);
                var box = new CheckBox { Content = header, IsChecked = c.IsVisible, MinWidth = 92, Margin = new Thickness(0, 0, 6, 0) };
                box.IsCheckedChanged += (_, _) => { if (!_building) SetShown(header, box.IsChecked == true); };
                _boxes.Children.Add(box);
            }
            _note.Text = $"{(always.Count == 0 ? "The first column" : string.Join(" and ", always))} always show{(always.Count == 1 ? "s" : "")}. Ticking or unticking a column saves it as \"{MineName}\" for this table.";
        }
        finally { _building = false; }
    }
}

/// <summary>One table's remembered view (#465).</summary>
public sealed class TableViewChoice
{
    /// <summary>The view's name: "All columns", one of the table's own, or "My view".</summary>
    public string View { get; set; } = ColumnPicker.AllName;
    /// <summary>"My view": the columns you ticked, by header.</summary>
    public List<string> Shown { get; set; } = [];
}
