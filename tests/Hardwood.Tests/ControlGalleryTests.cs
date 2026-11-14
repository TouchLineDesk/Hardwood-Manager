using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Hardwood.Core;
using Hardwood.Core.Generation;
using Hardwood.Desktop.Controls;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Renders the custom controls into a gallery PNG when HW_SCREENSHOT_DIR is set; always asserts they render without throwing.</summary>
public class ControlGalleryTests
{
    [AvaloniaFact]
    public void Controls_render()
    {
        var league = new WorldGenerator(new Rng(2026)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));
        var panel = new StackPanel { Background = Brush.Parse("#0F1216"), Spacing = 12, Margin = new Thickness(16) };

        var logos = new WrapPanel();
        foreach (var t in league.Teams) logos.Children.Add(new TeamLogo { Team = t, Width = 64, Height = 64, Margin = new Thickness(4) });
        panel.Children.Add(logos);

        var cards = new WrapPanel();
        var showcase = league.Teams.SelectMany(t => t.Roster.Select(p => (p, t))).OrderByDescending(x => x.p.CurrentAbility).ToList();
        var picked = new List<(Core.Model.Player p, Core.Model.Team t)>();
        foreach (var tier in Core.Model.Tier.All)
        {
            var hit = showcase.FirstOrDefault(x => Core.Model.Tier.ForCa(x.p.CurrentAbility) == tier);
            if (hit.p != null) picked.Add(hit);
        }
        var galaxy = showcase[0];
        galaxy.p.CurrentAbility = 185;
        picked.Add(galaxy);
        foreach (var (p, t) in picked)
            cards.Children.Add(new PlayerCard { Player = p, Team = t, Margin = new Thickness(4), Footer = $"{p.Age(new DateOnly(2026, 10, 1))} yrs · ability {p.CurrentAbility}" });
        foreach (var (p, t) in picked.Take(3)) { var c = new PlayerCard { Player = p, Team = t, Margin = new Thickness(4) }; PlayerCard.Animate = false; c.Flip(); PlayerCard.Animate = true; cards.Children.Add(c); }
        panel.Children.Add(cards);

        var compact = new WrapPanel();
        foreach (var (p, t) in picked.Take(6)) compact.Children.Add(new PlayerCard { Player = p, Team = t, Compact = true, Width = 130, Height = 180, Margin = new Thickness(4), Knowledge = 0.5 });
        panel.Children.Add(compact);

        var window = new Window { Content = new ScrollViewer { Content = panel }, Width = 1480, Height = 1100, Background = Brush.Parse("#0F1216") };
        window.Show();
        window.UpdateLayout();
        var dir = Environment.GetEnvironmentVariable("HW_SCREENSHOT_DIR");
        if (!string.IsNullOrEmpty(dir))
        {
            using var frame = window.CaptureRenderedFrame();
            frame?.Save(Path.Combine(dir, "gallery.png"));
        }
        Assert.True(true);
    }
}
