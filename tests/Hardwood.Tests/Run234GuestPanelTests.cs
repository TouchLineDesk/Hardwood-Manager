using Hardwood.Core.Model;
using Hardwood.Core.RealWorld;
using Hardwood.Core.Season;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Run 234, studio show part 2c (`danny-broadcast-nov27`): the month's guest in the studio chair.</summary>
public class Run234GuestPanelTests
{
    private static GameSession RealCareerOnGameDay(int days)
    {
        var s = GameSession.NewGame(31, 5, null);
        for (int i = 0; i < days; i++) s.AdvanceDay();
        int guard = 0;
        while (!s.HumanPlaysToday && guard++ < 30) s.AdvanceDay();
        Assert.True(s.HumanPlaysToday);
        return s;
    }

    [Fact]
    public void The_invented_guests_are_invented_and_have_every_voice()
    {
        foreach (var g in GuestPanel.Invented)
        {
            Assert.False(RealNames.IsReal(g.Name), g.Name);
            Assert.False(RealNames.IsRealPlayer(g.Name), g.Name);
            Assert.True(g.IsGuest);
            Assert.StartsWith("Guest · former", g.Role);
        }
        Assert.Equal(4, GuestPanel.Invented.Select(g => g.Voice).Distinct().Count());
        Assert.All(StudioShow.Cast, a => Assert.False(a.IsGuest));
        Assert.Equal(4, GuestPanel.Invented.Select(g => g.Colour).Distinct().Count());
        Assert.DoesNotContain(GuestPanel.Invented, g => StudioShow.Cast.Any(c => c.Colour == g.Colour));
    }

    [Fact]
    public void The_chair_rotates_by_month_and_never_repeats_last_month()
    {
        var pool = GuestPanel.Invented.ToList();
        var seen = new HashSet<string>();
        var d = new DateOnly(2026, 10, 1);
        StudioAnalyst? last = null;
        for (int m = 0; m < 24; m++)
        {
            var g = GuestPanel.ForMonth(pool, 31, d.AddMonths(m));
            Assert.Equal(g, GuestPanel.ForMonth(pool, 31, d.AddMonths(m).AddDays(20))); // the same all month
            if (last != null) Assert.NotEqual(last, g);
            seen.Add(g.Key);
            last = g;
        }
        Assert.True(seen.Count >= 3, $"only {seen.Count} guests in two seasons");
    }

    [Fact]
    public void A_regular_night_has_the_guest_his_take_and_his_tracked_pick()
    {
        var s = RealCareerOnGameDay(40);
        var (live, _) = s.BeginLiveDay();
        Assert.NotNull(live);
        var show = s.BuildStudioShow(live!.Result.Home.Team, live.Result.Away.Team);
        Assert.Equal(StudioSet.Regular, show.Set);
        var guest = Assert.Single(show.Panel, a => a.IsGuest);
        Assert.Contains(guest, GuestPanel.Invented); // a new save has no long-career retirees yet
        Assert.Equal(guest, s.MonthGuest(show.Date));
        var chair = Assert.Single(show.Segments, x => x.Kicker == "THE GUEST CHAIR");
        Assert.Contains(chair.Lines, l => l.Speaker == guest);
        Assert.Contains(chair.Lines, l => l.Speaker == show.Host && l.Text.Contains(guest.Name));
        Assert.True(chair.Lines.Count >= 3);
        Assert.Contains(show.Picks, p => p.Analyst == guest);
        // The three regulars' picks are the same as without the guest (he picks last, from the show's own stream).
        Assert.Equal(new[] { "big", "numbers", "guard" }, show.Picks.Take(3).Select(p => p.Analyst.Key));

        // His pick is locked with the others and counts on his own record.
        s.LockStudioPicks(show);
        Assert.Contains(s.Media.Studio.Picks, p => p.Key == show.Key && p.Analyst == guest.Key);
        var again = s.BuildStudioShow(live.Result.Home.Team, live.Result.Away.Team);
        Assert.Equal(show.Picks.Single(p => p.Analyst == guest).Pick, again.Picks.Single(p => p.Analyst == guest).Pick);
    }

    [Fact]
    public void Career_retirees_take_the_chair_and_real_players_never_do()
    {
        var s = RealCareerOnGameDay(5);
        var league = s.League;
        var generated = league.Teams.SelectMany(t => t.Roster).Concat(s.World.Players)
            .Where(p => !RealNames.IsRealPlayer(p.Name) && !RealCareers.Players.ContainsKey(p.Name)).Take(5).ToList();
        var real = league.Teams.SelectMany(t => t.Roster).First(p => RealNames.IsRealPlayer(p.Name));
        Assert.Equal(5, generated.Count);
        int i = 0;
        foreach (var p in generated.Append(real))
        {
            league.Retired.Add(p);
            league.Careers[p.Id] = new CareerTotals { PlayerId = p.Id, Seasons = 8 + i, Games = 500 + 10 * i, Points = 9000 + 500 * i, Rebounds = 2000, Assists = 1500 + 600 * i, AllStars = i % 3 };
            i++;
        }
        // One too short to count.
        var shortCareer = generated[0];
        league.Careers[shortCareer.Id] = new CareerTotals { PlayerId = shortCareer.Id, Seasons = 3, Games = 200, Points = 2000 };

        var pool = GuestPanel.Pool(league, id => league.Teams.FirstOrDefault(t => t.Id == id)?.FullName);
        Assert.Equal(4, pool.Count);
        Assert.DoesNotContain(pool, g => g.Name == real.Name);
        Assert.DoesNotContain(pool, g => g.Name == shortCareer.Name);
        Assert.DoesNotContain(pool, g => GuestPanel.Invented.Contains(g));
        Assert.All(pool, g => Assert.StartsWith("guest-p", g.Key));
        Assert.All(pool, g => Assert.Contains("seasons", g.Role));
        // Best Hall score first.
        Assert.Equal(generated[4].Name, pool[0].Name);
        Assert.Contains(pool, g => g.Role.Contains("All-Star game"));
        Assert.Contains(s.MonthGuest(s.Today), pool);
    }

    [Fact]
    public void A_guest_s_voice_follows_his_career()
    {
        var p = new Player { FirstName = "Test", LastName = "Big", PrimaryPosition = Position.C };
        Assert.Equal(GuestPanel.OldSchool, GuestPanel.VoiceFor(p, new CareerTotals { Games = 100, Rebounds = 800, Points = 900 }));
        p.PrimaryPosition = Position.PG;
        Assert.Equal(GuestPanel.NumbersVoice, GuestPanel.VoiceFor(p, new CareerTotals { Games = 100, Assists = 700, Points = 1200 }));
        Assert.Equal(GuestPanel.Hype, GuestPanel.VoiceFor(p, new CareerTotals { Games = 100, Assists = 300, Points = 2000 }));
        Assert.Equal(GuestPanel.Contrarian, GuestPanel.VoiceFor(p, new CareerTotals { Games = 100, Assists = 200, Points = 900 }));
    }
}
