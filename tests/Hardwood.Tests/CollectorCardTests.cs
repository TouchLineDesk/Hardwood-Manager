using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Desktop.Controls;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// The collector card (Danny's D58 B, #466–#467): a metal frame by ability with no tier label, three plain-English skill
/// badges, a click to flip, and a back with his season, what he does well, his career and his status.
/// </summary>
public class CollectorCardTests
{
    private static Player Make(int id, int baseline, params (Attr a, int v)[] tops)
    {
        var p = new Player { Id = id, FirstName = "Test", LastName = "Player", BirthDate = new DateOnly(2000, 1, 1), CurrentAbility = 120, PotentialAbility = 130 };
        foreach (var a in AttrInfo.All) p.Attributes[a] = baseline;
        foreach (var (a, v) in tops) p.Attributes[a] = v;
        return p;
    }

    [Fact]
    public void Badges_are_his_best_skills_in_plain_english_without_overlaps()
    {
        var shooter = Make(1, 8, (Attr.ThreePoint, 19), (Attr.CatchAndShoot, 19), (Attr.ShotCreation, 16), (Attr.MidRange, 16), (Attr.BallHandling, 15),
            (Attr.Block, 15), (Attr.InteriorDefense, 15), (Attr.HelpDefense, 15));
        var b = CollectorCard.Badges(shooter, 1.0);
        Assert.Equal(3, b.Count);
        Assert.Equal("Three-point shooter", b[0]);
        Assert.Contains("Rim protector", b);
        // Shot creator and Mid-range scorer share two attributes: only one of them shows.
        Assert.False(b.Contains("Shot creator") && b.Contains("Mid-range scorer"));
        Assert.All(b, x => Assert.DoesNotMatch("[0-9]", x));
        Assert.All(b, x => Assert.Contains(x, CollectorCard.Skills.Select(s => s.Name)));

        // A player good at nothing still gets one badge, never a blank card.
        var weak = Make(2, 6);
        Assert.Single(CollectorCard.Badges(weak, 1.0));

        // Scouting fog is stable: the same badges every time for the same knowledge.
        Assert.Equal(CollectorCard.Badges(shooter, 0.4), CollectorCard.Badges(shooter, 0.4));
    }

    [Fact]
    public void Strengths_are_his_best_four_areas_best_first()
    {
        var big = Make(3, 9, (Attr.DefensiveRebounding, 18), (Attr.OffensiveRebounding, 18), (Attr.Block, 16), (Attr.InteriorDefense, 16), (Attr.HelpDefense, 16));
        var s = CollectorCard.Strengths(big, 1.0);
        Assert.Equal(4, s.Count);
        Assert.Equal("Rebounding", s[0].Name);
        Assert.Equal(18, s[0].Value);
        Assert.Equal("Interior D", s[1].Name);
        for (int i = 1; i < s.Count; i++) Assert.True(s[i - 1].Value >= s[i].Value);
        Assert.All(s, x => Assert.InRange(x.Value, 1, 20));
    }

    [Fact]
    public void The_frame_is_a_metal_by_ability_and_names_no_tier()
    {
        Assert.Equal("#7FD1FF", CollectorCard.FrameColor(170));
        Assert.Equal("#E3B341", CollectorCard.FrameColor(150));
        Assert.Equal("#C0C7D0", CollectorCard.FrameColor(120));
        Assert.Equal("#B87333", CollectorCard.FrameColor(90));
        var p = Make(4, 10); p.CurrentAbility = 150; p.PotentialAbility = 150;
        Assert.Equal("POT 150 · at his ceiling", CollectorCard.PotentialLine(p, 1.0));
        foreach (var t in Tier.All) Assert.DoesNotContain(t.Name, CollectorCard.PotentialLine(p, 1.0));
    }

    [Fact]
    public void The_back_carries_his_season_career_and_status()
    {
        var s = GameSession.NewGame(31, 5, null);
        for (int i = 0; i < 25; i++) s.AdvanceDay();
        var star = s.HumanTeam.Roster.Where(p => s.Stats.TryGetValue(p.Id, out var st) && st.Games > 0).OrderByDescending(p => p.CurrentAbility).First();
        var back = CollectorCard.BackOf(s, star);
        var st = s.Stats[star.Id];
        Assert.Equal(st.Ppg, back.Ppg);
        Assert.Contains($"{st.Games} games", back.StatsLabel);
        Assert.Equal(4, back.Strengths.Count);
        Assert.Contains(back.Chips, c => c.Text is "Healthy" || c.Text.StartsWith("Out "));
        Assert.Contains(back.Chips, c => c.Text.Contains('$') || c.Text == "Free agent");
        Assert.False(string.IsNullOrEmpty(back.Career));
        Assert.False(string.IsNullOrEmpty(back.Honours));

        // A player from another club is seen through your scouts: the back still fills in.
        var other = s.League.Teams.First(t => t != s.HumanTeam).Roster.OrderByDescending(p => p.CurrentAbility).First();
        Assert.Equal(4, CollectorCard.BackOf(s, other).Strengths.Count);
    }

    [AvaloniaFact]
    public void A_click_flips_it_and_both_faces_draw()
    {
        PlayerCard.Animate = false;
        try
        {
            var s = GameSession.NewGame(31, 5, null);
            PlayerCard.BackSource = p => CollectorCard.BackOf(s, p);
            PlayerCard.Today = () => s.Today;
            var team = s.HumanTeam;
            var star = team.Roster.OrderByDescending(p => p.CurrentAbility).First();
            var card = new PlayerCard { Player = star, Team = team, Width = 236, Height = 344 };
            var window = new Window { Content = card, Width = 260, Height = 360, Background = Brushes.Black };
            window.Show(); window.UpdateLayout();
            using (var front = window.CaptureRenderedFrame()) Assert.NotNull(front);
            Assert.False(card.IsFlipped);
            card.Flip();
            Assert.True(card.IsFlipped);
            window.UpdateLayout();
            using (var back = window.CaptureRenderedFrame()) Assert.NotNull(back);
            // A new player shows his front again.
            card.Player = team.Roster.First(p => p != star);
            Assert.False(card.IsFlipped);
            card.FlipOnClick = false;
        }
        finally { PlayerCard.Animate = true; PlayerCard.BackSource = null; PlayerCard.Today = null; }
    }

    [AvaloniaFact]
    public void The_longest_real_names_fit_on_the_smallest_card()
    {
        var p = new Player { Id = 9, FirstName = "Shai", LastName = "Gilgeous-Alexander", BirthDate = new DateOnly(1998, 7, 12) };
        foreach (var width in new[] { 170.0, 180, 236 })
        {
            var (text, size) = PlayerCard.FitName(p, width, compact: false);
            Assert.True(size >= 10.5, $"{width}: {size}");
            var ft = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Inter", FontStyle.Normal, FontWeight.Black), size, Brushes.White);
            Assert.True(ft.Width <= width - 20 + 0.5, $"{width}: {text} at {size} is {ft.Width:0.0} px");
        }
        var (t2, _) = PlayerCard.FitName(p, 236, compact: false);
        Assert.Contains("GILGEOUS-ALEXANDER", t2);
    }

    /// <summary>Screenshots for the run's visual check: a real roster's fronts and backs (HW_SCREENSHOT_DIR).</summary>
    [AvaloniaFact]
    public void Real_roster_cards_front_and_back()
    {
        PlayerCard.Animate = false;
        try
        {
            var s = GameSession.NewGame(31, 5, null);
            for (int i = 0; i < 20; i++) s.AdvanceDay();
            PlayerCard.BackSource = p => CollectorCard.BackOf(s, p);
            PlayerCard.Today = () => s.Today;
            var panel = new StackPanel { Spacing = 10, Margin = new Thickness(12) };
            foreach (var team in new[] { s.HumanTeam, s.League.Teams.First(t => t != s.HumanTeam) })
            {
                var row = new WrapPanel();
                var k = Scouting.Knowledge(s.HumanTeam, team.Roster[0]);
                foreach (var p in team.Roster.OrderByDescending(p => p.CurrentAbility).Take(4))
                    row.Children.Add(new PlayerCard { Player = p, Team = team, Knowledge = k, Width = 180, Height = 268, Margin = new Thickness(0, 0, 10, 0) });
                foreach (var p in team.Roster.OrderByDescending(p => p.CurrentAbility).Take(3))
                {
                    var c = new PlayerCard { Player = p, Team = team, Knowledge = k, Width = 180, Height = 268, Margin = new Thickness(0, 0, 10, 0) };
                    c.Flip();
                    row.Children.Add(c);
                }
                panel.Children.Add(row);
            }
            var big = new WrapPanel();
            var top = s.HumanTeam.Roster.OrderByDescending(p => p.CurrentAbility).First();
            big.Children.Add(new PlayerCard { Player = top, Team = s.HumanTeam, Width = 236, Height = 344, Margin = new Thickness(0, 0, 10, 0) });
            var bigBack = new PlayerCard { Player = top, Team = s.HumanTeam, Width = 236, Height = 344, Margin = new Thickness(0, 0, 10, 0) };
            bigBack.Flip(); big.Children.Add(bigBack);
            var fa = s.League.FreeAgents.OrderByDescending(p => p.CurrentAbility).FirstOrDefault();
            if (fa != null) big.Children.Add(new PlayerCard { Player = fa, Width = 170, Height = 246, Margin = new Thickness(0, 0, 10, 0) });
            foreach (var p in s.HumanTeam.Roster.Take(4)) big.Children.Add(new PlayerCard { Player = p, Team = s.HumanTeam, Compact = true, Width = 130, Height = 180, Margin = new Thickness(0, 0, 8, 0) });
            panel.Children.Add(big);
            var window = new Window { Content = panel, Width = 1480, Height = 960, Background = Brush.Parse("#0F1216") };
            window.Show(); window.UpdateLayout();
            using var frame = window.CaptureRenderedFrame()!;
            var dir = Environment.GetEnvironmentVariable("HW_SCREENSHOT_DIR");
            if (!string.IsNullOrEmpty(dir)) frame.Save(Path.Combine(dir, "collector-cards.png"));
            Assert.True(frame.PixelSize.Width > 0);
        }
        finally { PlayerCard.Animate = true; PlayerCard.BackSource = null; PlayerCard.Today = null; }
    }
}
