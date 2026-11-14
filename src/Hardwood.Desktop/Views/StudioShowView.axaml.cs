using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Hardwood.Desktop.Views;

/// <summary>The pre-game studio show (#446). Narrow windows (1280 px with the assistant, big text) put the buttons under
/// the show's title and stack the two columns, so nothing is squeezed.</summary>
public partial class StudioShowView : UserControl
{
    public const double TwoColumnWidth = 860, ButtonsBesideWidth = 900;
    private bool? _wide, _beside;

    public StudioShowView()
    {
        InitializeComponent();
        PropertyChanged += (_, e) => { if (e.Property == BoundsProperty) Arrange(Bounds.Width); };
    }

    private void Arrange(double width)
    {
        if (width <= 0) return;
        bool beside = width >= ButtonsBesideWidth;
        if (_beside != beside)
        {
            _beside = beside;
            Grid.SetRow(Buttons, beside ? 0 : 1);
            Grid.SetColumn(Buttons, beside ? 2 : 0);
            Grid.SetColumnSpan(Buttons, beside ? 1 : 3);
            Buttons.HorizontalAlignment = beside ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            Buttons.MaxWidth = beside ? 330 : double.PositiveInfinity;
            Buttons.Margin = beside ? new Thickness(0) : new Thickness(0, 8, 0, 0);
        }
        bool wide = width >= TwoColumnWidth;
        if (_wide == wide) return;
        _wide = wide;
        Body.ColumnDefinitions = wide ? new ColumnDefinitions("3*,2*") : new ColumnDefinitions("*");
        Grid.SetRow(BodyRight, wide ? 0 : 1);
        Grid.SetColumn(BodyRight, wide ? 1 : 0);
        BodyLeft.Margin = wide ? new Thickness(0, 0, 10, 0) : new Thickness(0);
    }
}
