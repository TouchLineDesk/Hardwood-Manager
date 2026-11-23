using Hardwood.Core.College;
using Hardwood.Core.Season;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 166, step (4) of Danny's college order (calendar moments, folded into CS10): Midnight Madness, the early signing
/// period, poll Monday, Championship Week, Selection Sunday's watch party, court storming and a One Shining Moment recap,
/// each on its day in a college career's inbox, and none of them in a pro career's.
/// </summary>
public class Run166CalendarMomentsTests
{
    private readonly ITestOutputHelper _out;
    public Run166CalendarMomentsTests(ITestOutputHelper o) => _out = o;

    private static GameSession Career(string name, ulong seed)
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(seed, 2026, GameSession.DefaultFullSim);
        int id = world.College!.Programs.First(p => p.FullName.StartsWith(name)).Id;
        return GameSession.NewGame(seed, id, null);
    }

    [Fact]
    public void Midnight_madness_falls_on_a_friday_in_mid_october()
    {
        foreach (int y in new[] { 2026, 2027, 2028, 2031 })
        {
            var d = GameSession.MidnightMadnessDay(y);
            Assert.Equal(DayOfWeek.Friday, d.DayOfWeek);
            Assert.Equal(10, d.Month);
            Assert.InRange(d.Day, 10, 16);
        }
    }

    [Theory]
    [InlineData("Duke", 2026UL)]
    [InlineData("Ball State", 31UL)]
    public void A_college_season_has_its_moments_on_their_days(string program, ulong seed)
    {
        var s = Career(program, seed);
        var c = s.World.College!;
        int year = c.Season.Year;
        _out.WriteLine($"{s.HumanTeam.FullName}: start {s.Today} ({s.Phase}), first game {c.FirstGameDay}");
        var all = new List<InboxMessage>();
        var storms = new List<CollegeGame>();
        var seen = new HashSet<int>();
        int guard = 0;
        while (s.Phase != SessionPhase.Offseason && guard++ < 260 && s.Today < new DateOnly(year + 1, 4, 30))
        {
            s.AdvanceDay();
            foreach (var m in s.Inbox.Where(m => seen.Add(m.Id))) all.Add(m);
            storms.AddRange(c.Season.Games.Where(g => g.Date == s.Today.AddDays(-0) && g.Played && g.Event == null && g.HomeRank == null
                && g.AwayRank is <= GameSession.StormRank && g.WinnerId == g.HomeId && (g.HomeId == s.HumanTeam.Id || g.AwayId == s.HumanTeam.Id)));
            // "Delete all read" never brings a moment back: clear the inbox now and then.
            if (s.Today.Day == 1) s.Inbox.Clear();
        }
        var moments = all.Where(m => m.Title.StartsWith("Midnight Madness") || m.Title.StartsWith("Early signing") || m.Title.StartsWith("Poll Monday")
            || m.Title.StartsWith("Championship Week") || m.Title.StartsWith("Selection Sunday") || m.Title.Contains("stormed") || m.Title.StartsWith("Court storm")
            || m.Title.StartsWith("One Shining Moment")).ToList();
        foreach (var m in moments) _out.WriteLine($"{m.Date:yyyy-MM-dd ddd}: {m.Title}\n  {m.Body.Replace("\n", "\n  ")}");
        _out.WriteLine($"inbox messages this season: {all.Count}; moments {moments.Count}");

        var mm = Assert.Single(moments, m => m.Title.StartsWith("Midnight Madness"));
        Assert.Equal(GameSession.MidnightMadnessDay(year), mm.Date);
        Assert.Contains("dunk contest", mm.Body);

        var es = Assert.Single(moments, m => m.Title.StartsWith("Early signing"));
        Assert.Equal(CollegeWorld.EarlySigningStart(year + 1), es.Date);
        Assert.Contains("signing day", es.Body);

        var cw = Assert.Single(moments, m => m.Title.StartsWith("Championship Week"));
        Assert.Equal(GameSession.ConferenceTournamentDay(year).AddDays(-1), cw.Date);
        Assert.Contains(s.MyConference!.Short, cw.Title);

        var sel = Assert.Single(moments, m => m.Title.StartsWith("Selection Sunday"));
        if (sel.Title.Contains("seed") || sel.Body.Contains("watched")) Assert.Contains("watched the show", sel.Body);

        var osm = Assert.Single(moments, m => m.Title.StartsWith("One Shining Moment"));
        var final = c.Season.Games.Single(g => g.Event == CollegeWorld.Madness && g.Round == 6);
        Assert.Equal(final.Date.AddDays(1), osm.Date);
        Assert.Contains(c.Program(c.Season.ChampionId!.Value)!.FullName, osm.Title);
        Assert.Contains("The road to the final:", osm.Body);

        var polls = moments.Where(m => m.Title.StartsWith("Poll Monday")).ToList();
        Assert.All(polls, m => Assert.Equal(DayOfWeek.Monday, m.Date.DayOfWeek));
        Assert.True(polls.Count <= 12, $"{polls.Count} poll messages");

        // Every court storm involving us became a message on its day.
        foreach (var g in storms)
            Assert.Contains(moments, m => m.Date == g.Date && (m.Title.Contains("stormed") || m.Title.StartsWith("Court storm")));
        Assert.True(moments.Count <= 25, $"{moments.Count} moments in a season");
    }

    [Fact]
    public void A_pro_career_gets_none_of_them()
    {
        var (league, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026, 2026, GameSession.DefaultFullSim);
        var s = GameSession.NewGame(2026, league.Teams[0].Id, null);
        Assert.False(s.InCollege);
        for (int i = 0; i < 60; i++) s.AdvanceDay();
        Assert.DoesNotContain(s.Inbox, m => m.Title.StartsWith("Midnight Madness") || m.Title.StartsWith("Poll Monday") || m.Title.StartsWith("Early signing"));
    }
}
