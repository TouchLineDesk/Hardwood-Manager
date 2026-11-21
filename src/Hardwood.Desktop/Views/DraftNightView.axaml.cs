using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Hardwood.Desktop.Controls;
using Hardwood.Desktop.Graphics;
using Hardwood.Desktop.ViewModels;

namespace Hardwood.Desktop.Views;

/// <summary>Draft night's stage (B3, run 142): places the screen, the side board, the crest cards and the bubble on the rendered set.</summary>
public partial class DraftNightView : UserControl
{
    private readonly BroadcastSet _set = BroadcastArt.Draft;
    /// <summary>The part of the 1920 × 1080 frame on the stage: the screen, the podium and the four lecterns.</summary>
    public static readonly Rect Crop = new(170, 90, 1600, 900);
    private static Point At(Point p) => new(p.X - Crop.X, p.Y - Crop.Y);
    private readonly List<(Border card, TeamLogo logo, TextBlock label)> _cards = new();
    private OffseasonViewModel? _vm;

    public DraftNightView()
    {
        InitializeComponent();
        Backdrop.Source = _set.Frame;
        Canvas.SetLeft(Backdrop, -Crop.X); Canvas.SetTop(Backdrop, -Crop.Y);
        var r = _set.Screen.Translate(new Vector(-Crop.X, -Crop.Y));
        Canvas.SetLeft(ScreenPanel, r.X); Canvas.SetTop(ScreenPanel, r.Y);
        ScreenPanel.Width = r.Width; ScreenPanel.Height = r.Height;
        // The side board sits over the stage-right sign, from the screen's top down.
        Canvas.SetLeft(SideBoard, Crop.Width - 16 - SideBoard.Width); Canvas.SetTop(SideBoard, 12);
        SideBoard.MaxHeight = 470;
        Canvas.SetTop(LowerThird, Crop.Height - 150);
        // Without the frame's JSON (a stripped build), four cards along the stage.
        var cards = _set.Cards.Length > 0 ? _set.Cards : Enumerable.Range(0, 4).Select(i => new Rect(860 + i * 255, 800, 130, 92)).ToArray();
        foreach (var cr in cards)
        {
            var rr = cr.Translate(new Vector(-Crop.X, -Crop.Y));
            var logo = new TeamLogo { ShowRank = false, Width = rr.Height - 10, Height = rr.Height - 10, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            var label = new TextBlock { FontSize = 30, FontWeight = FontWeight.Black, Foreground = new SolidColorBrush(Color.Parse("#7C5CFF")), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            var border = new Border { Width = rr.Width, Height = rr.Height, Background = new SolidColorBrush(Color.Parse("#F4F6FA")), CornerRadius = new CornerRadius(3), Child = new Panel { Children = { logo, label } } };
            Canvas.SetLeft(border, rr.X); Canvas.SetTop(border, rr.Y);
            Cards.Children.Add(border);
            _cards.Add((border, logo, label));
        }
        DataContextChanged += (_, _) => Hook();
        Bubble.SizeChanged += (_, _) => PlaceBubble();
        EnvelopeGrid.LayoutUpdated += (_, _) => SetColumns();
        BoardList.LayoutUpdated += (_, _) => SetColumns();
    }

    private void Hook()
    {
        if (_vm != null) _vm.PropertyChanged -= OnVm;
        _vm = DataContext as OffseasonViewModel;
        if (_vm != null) _vm.PropertyChanged += OnVm;
        FillCards(); PlaceBubble(); SetColumns();
    }

    private void OnVm(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OffseasonViewModel.RepCards)) FillCards();
        else if (e.PropertyName is nameof(OffseasonViewModel.NightLine) or nameof(OffseasonViewModel.NightAtPodium)) PlaceBubble();
        else if (e.PropertyName is nameof(OffseasonViewModel.EnvelopeColumns) or nameof(OffseasonViewModel.BoardColumns)) SetColumns();
    }

    private void SetColumns()
    {
        if (_vm != null && EnvelopeGrid.ItemsPanelRoot is UniformGrid g && g.Columns != _vm.EnvelopeColumns) g.Columns = _vm.EnvelopeColumns;
        if (_vm != null && BoardList.ItemsPanelRoot is UniformGrid b && b.Columns != _vm.BoardColumns) b.Columns = _vm.BoardColumns;
    }

    private void FillCards()
    {
        var reps = _vm?.RepCards ?? new List<RepCard>();
        for (int i = 0; i < _cards.Count; i++)
        {
            var rep = i < reps.Count ? reps[i] : null;
            var (_, logo, label) = _cards[i];
            logo.Team = rep?.Team;
            logo.IsVisible = rep?.Team != null;
            label.Text = rep == null ? "" : rep.Team == null ? "?" : "";
        }
    }

    /// <summary>The official's bubble sits below and right of his head, with its tail pointing at it.</summary>
    private void PlaceBubble()
    {
        var head = At(_set.HostHead);
        double w = Bubble.Width, h = Math.Max(Bubble.Bounds.Height, 110);
        double x = Math.Clamp(head.X + 70, 16, Crop.Width - 16 - w);
        double y = Math.Clamp(head.Y + 40, 16, Crop.Height - 16 - h);
        Canvas.SetLeft(Bubble, x); Canvas.SetTop(Bubble, y);
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var tip = new Point(head.X + 26, head.Y + 20);
        BubbleTail.Data = Geometry.Parse(string.Format(ci, "M {0},{1} L {2},{3} L {0},{4} Z", x + 2, y + 24, tip.X, tip.Y, y + 64));
    }
}
