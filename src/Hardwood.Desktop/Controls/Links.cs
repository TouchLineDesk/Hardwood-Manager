using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Hardwood.Core.Model;
using Hardwood.Desktop.ViewModels;

namespace Hardwood.Desktop.Controls;

/// <summary>A team's name that opens its team page when clicked (anywhere a team is listed).</summary>
public sealed class TeamLink : Button
{
    protected override Type StyleKeyOverride => typeof(Button);

    public static readonly StyledProperty<Team?> TeamProperty = AvaloniaProperty.Register<TeamLink, Team?>(nameof(Team));
    /// <summary>Text to show instead of the full name (e.g. the abbreviation).</summary>
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<TeamLink, string?>(nameof(Text));

    public Team? Team { get => GetValue(TeamProperty); set => SetValue(TeamProperty, value); }
    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }

    public TeamLink()
    {
        Classes.Add("link");
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TeamProperty || change.Property == TextProperty)
        {
            Content = Text ?? Team?.FullName ?? "";
            ToolTip.SetTip(this, Team == null ? null : $"Open {Team.FullName}");
        }
    }

    protected override void OnClick()
    {
        base.OnClick();
        if (Team != null && TopLevel.GetTopLevel(this)?.DataContext is MainViewModel shell) shell.ShowTeam(Team);
    }
}

/// <summary>A player's name that opens his page when clicked.</summary>
public sealed class PlayerLink : Button
{
    protected override Type StyleKeyOverride => typeof(Button);

    public static readonly StyledProperty<Player?> PlayerProperty = AvaloniaProperty.Register<PlayerLink, Player?>(nameof(Player));
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<PlayerLink, string?>(nameof(Text));

    public Player? Player { get => GetValue(PlayerProperty); set => SetValue(PlayerProperty, value); }
    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }

    public PlayerLink()
    {
        Classes.Add("link");
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PlayerProperty || change.Property == TextProperty) Content = Text ?? Player?.Name ?? "";
    }

    protected override void OnClick()
    {
        base.OnClick();
        if (Player != null && TopLevel.GetTopLevel(this)?.DataContext is MainViewModel shell) shell.ShowPlayer(Player);
    }
}
