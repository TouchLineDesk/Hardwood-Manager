using System.Text.Json;
using Hardwood.Core;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.People;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>Run 230, the last of Danny's three bug-only runs (`danny-bug-sweep-nov27`): K46 for clubs outside the NBA (the
/// pyramid and abroad), the relegated NBA club's hold asking the G League's minimum, and Danny's columnist renames
/// (`danny-columnist-names-nov27`).</summary>
public class Run230BugSweepTests
{
    private readonly ITestOutputHelper _out;
    public Run230BugSweepTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void The_columnists_have_plain_american_names_and_old_saves_show_them()
    {
        Assert.Equal("Ryan Holt", MediaCast.Numbers.Name);
        Assert.Equal("Frank Mahoney", MediaCast.OldSchool.Name);
        Assert.Equal("Mike Delaney", MediaCast.CollegeWriter.Name);
        Assert.Equal("The Possession Log", MediaCast.Numbers.Outlet);   // outlets unchanged
        Assert.Equal("the Courtside Sentinel", MediaCast.OldSchool.Outlet);
        Assert.Equal("Campus Hoops Weekly", MediaCast.CollegeWriter.Outlet);

        // A save made before the rename: the stored names read as today's.
        Assert.Equal("Ryan Holt", MediaCast.CurrentName("Priya Venkataraman"));
        Assert.Equal("Frank Mahoney", MediaCast.CurrentName("Walt Brennecke"));
        Assert.Equal("Mike Delaney", MediaCast.CurrentName("Dana Whitcombe"));
        Assert.Equal("Sarah Morrison", MediaCast.CurrentName("Elena Lindqvist"));
        Assert.Equal("Megan Porter", MediaCast.CurrentName("Noor Whitlock"));
        Assert.Equal("Megan Porter", MediaCast.CurrentName("Megan Porter"));   // today's names are left alone
        Assert.Equal("Someone Else", MediaCast.CurrentName("Someone Else"));
        var old = JsonSerializer.Deserialize<ColumnPicks>("{\"Key\":\"numbers\",\"Name\":\"Priya Venkataraman\",\"Outlet\":\"The Possession Log\"}")!;
        Assert.Equal("Ryan Holt", old.Name);

        // Every beat writer a team can get is a plain invented name, and none is a well-known basketball writer.
        string[] real = { "Brian Windhorst", "Chris Haynes", "Marc Stein", "Zach Lowe", "Jeff Goodman", "Andy Katz", "Dan Wetzel",
            "Jason Reid", "Brian Mahoney", "Chris Mannix", "Matt Norlander", "Steve Kerr", "Chris Broussard", "Jake Fischer" };
        var all = MediaCast.AllBeatNames.ToList();
        Assert.Equal(144, all.Distinct().Count());
        Assert.Empty(all.Intersect(real));
        Assert.DoesNotContain(all, n => n.EndsWith("Lindqvist") || n.EndsWith("Okafor") || n.EndsWith("Novak") || n.EndsWith("Haddad"));
    }

    private static int ClubIn(string key, ulong seed)
    {
        var league = new WorldGenerator(new Rng(seed)).Generate("North American Basketball League", 2026, new DateOnly(2026, 10, 1));
        var world = GameWorld.Generate(league, seed, GameSession.DefaultFullSim, new DateOnly(2026, 10, 1));
        return world[key].Clubs[3].Id;
    }

    private static void ToFreeAgency(GameSession s)
    {
        int guard = 0;
        while (s.Phase != SessionPhase.Offseason && guard++ < 700) s.AdvanceDay();
        Assert.Equal(SessionPhase.Offseason, s.Phase);
        guard = 0;
        while (s.Offseason!.Phase != OffseasonPhase.FreeAgency && s.Phase == SessionPhase.Offseason && guard++ < 40) s.AdvanceOffseason();
        Assert.Equal(OffseasonPhase.FreeAgency, s.Offseason.Phase);
    }

    private static void Cut(GameSession s, int keep)
    {
        var waiting = s.MyExpiring.ToHashSet();
        foreach (var p in s.HumanTeam.Roster.Where(p => !waiting.Contains(p)).OrderBy(p => p.CurrentAbility).Skip(keep).ToList())
        {
            s.HumanTeam.Roster.Remove(p); p.TeamId = null; p.Contract = null; s.League.FreeAgents.Add(p);
        }
    }

    /// <summary>K46 abroad: with the signings yours, a short squad holds the season and the world's summer signs nobody for
    /// you; a caller that skips the hold gets only the reported one-year fill to the league minimum.</summary>
    [Fact]
    public void K46_abroad_the_season_holds_and_nobody_is_signed_quietly()
    {
        var s = GameSession.NewGame(7, ClubIn(Competitions.Spain, 7), null);
        s.Job.Current!.TargetWins = 0;
        Assert.True(s.Abroad);
        ToFreeAgency(s);
        Assert.False(s.Delegated.Signings);
        Cut(s, 7);
        Assert.Equal(GameWorld.RosterMinimum, s.NextSeasonRosterMin);
        Assert.Equal(s.NextSeasonRosterMin, s.MyRosterMin);
        var hold = s.SeasonStartHold();
        _out.WriteLine(hold);
        Assert.NotNull(hold);
        Assert.Contains("Liga ACB", hold);
        Assert.Contains($"need at least {GameWorld.RosterMinimum}", hold);
        s.Delegated.Signings = true;
        Assert.Null(s.SeasonStartHold());
        s.Delegated.Signings = false;

        var waiting = s.MyExpiring.ToHashSet();
        var before = s.HumanTeam.Roster.Where(p => !waiting.Contains(p)).Select(p => p.Id).ToHashSet();
        int guard = 0;
        while (s.Phase == SessionPhase.Offseason && guard++ < 20) s.AdvanceOffseason();   // skips the hold (the desktop never does)
        var joined = s.HumanTeam.Roster.Where(p => !before.Contains(p.Id)).ToList();
        foreach (var p in joined) _out.WriteLine($"joined {p.Name} {p.Contract!.Salary} x{p.Contract.Years}");
        Assert.True(s.HumanTeam.Roster.Count >= GameWorld.RosterMinimum);
        Assert.All(joined, p => Assert.Equal(1, p.Contract!.Years));   // only the one-year fill, never the like-for-like refill
        var msg = s.Inbox.FirstOrDefault(m => m.Title.Contains("make up the numbers"));
        Assert.NotNull(msg);
        Assert.Contains("the league filled it", msg!.Body);
        Assert.Contains("Staff page", msg.Body);
    }

    /// <summary>K46 abroad, delegated: the sporting director refills like for like and the inbox names every signing.</summary>
    [Fact]
    public void K46_abroad_delegated_signings_are_named()
    {
        var s = GameSession.NewGame(7, ClubIn(Competitions.Spain, 7), null);
        s.Job.Current!.TargetWins = 0;
        ToFreeAgency(s);
        s.Delegated.Signings = true;
        Cut(s, 7);
        int guard = 0;
        while (s.Phase == SessionPhase.Offseason && guard++ < 20) s.AdvanceOffseason();
        Assert.True(s.HumanTeam.Roster.Count >= GameWorld.RosterMinimum);
        var msg = s.Inbox.FirstOrDefault(m => m.Title.Contains("your sporting director's summer signings"));
        _out.WriteLine(msg?.Body);
        Assert.NotNull(msg);
        Assert.Contains("× ", msg!.Body);
    }

    /// <summary>The relegated NBA club's hold asks for the G League's 11, and the summer's end doesn't top it up to 13.</summary>
    [Fact]
    public void A_relegated_nba_club_needs_the_g_leagues_minimum()
    {
        var (probe, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(31);
        var teamId = probe.Teams.OrderBy(t => QuickSim.Strength(t)).First().Id;
        var s = GameSession.NewGame(31, teamId, null);
        s.Job.Current!.TargetWins = 0;
        ToFreeAgency(s);
        _out.WriteLine($"{s.HumanTeam.FullName}: going down {s.GoingDown()}");
        Assert.True(s.GoingDown(), "seed 31's weakest club goes down (run 229's measure)");
        Assert.Equal(GameWorld.RosterMinimum, s.NextSeasonRosterMin);
        Assert.Equal(s.NextSeasonRosterMin, s.MyRosterMin);   // review: releases, minimum deals and pages count to the hold's number
        Cut(s, GameWorld.RosterMinimum);
        if (s.HumanTeam.Roster.Count(p => !s.MyExpiring.Contains(p)) == GameWorld.RosterMinimum) Assert.Null(s.SeasonStartHold());   // 11 is enough: not 13
        Cut(s, 9);
        var hold = s.SeasonStartHold();
        _out.WriteLine(hold);
        Assert.NotNull(hold);
        Assert.Contains($"need at least {GameWorld.RosterMinimum}", hold);
        Assert.Contains("G League", hold);
    }
}
