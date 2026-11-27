using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Hardwood.Desktop.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>Run 229, the second of Danny's three bug-only runs (`danny-bug-sweep-nov27`): K46's "now" half (the game signs
/// nobody for you unless you switched it on; `danny-delegation-nov27`) and H129/K47 (the relegation summer's room to spend is
/// the league you're going to).</summary>
public class Run229BugSweepTests
{
    private readonly ITestOutputHelper _out;
    public Run229BugSweepTests(ITestOutputHelper output) => _out = output;

    /// <summary>The weakest NBA club on a seed, played to its summer's free agency.</summary>
    private static GameSession ToFreeAgency(ulong seed, string? path = null)
    {
        var (probe, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(seed);
        var teamId = probe.Teams.OrderBy(t => QuickSim.Strength(t)).First().Id;
        var s = GameSession.NewGame(seed, teamId, path);
        s.Job.Current!.TargetWins = 0;   // keep the job through the season
        int guard = 0;
        while (s.Phase != SessionPhase.Offseason && guard++ < 700) s.AdvanceDay();
        Assert.Equal(SessionPhase.Offseason, s.Phase);
        guard = 0;
        while (s.Offseason!.Phase != OffseasonPhase.FreeAgency && s.Phase == SessionPhase.Offseason && guard++ < 40) s.AdvanceOffseason();
        Assert.Equal(OffseasonPhase.FreeAgency, s.Offseason.Phase);
        return s;
    }

    private static void CutTo(GameSession s, int keep)
    {
        foreach (var p in s.HumanTeam.Roster.OrderBy(p => p.CurrentAbility).Skip(keep).ToList())
        {
            s.HumanTeam.Roster.Remove(p); p.TeamId = null; p.Contract = null; s.League.FreeAgents.Add(p);
        }
    }

    /// <summary>K46: with the signings yours, the season is held while the roster is short, and a caller that skips the hold gets
    /// only one-year minimum deals for your club, named in one message; no staff are hired for you. H129/K47 in the same summer:
    /// the room to spend comes from next season's budget when you're going down.</summary>
    [Fact]
    public void K46_the_game_signs_nobody_for_you_unless_you_switch_it_on()
    {
        var s = ToFreeAgency(31);
        Assert.False(s.Delegated.Signings);
        Assert.False(s.Delegated.StaffHiring);

        // H129/K47: the summer's budget never exceeds this league's, and going down it is the room the next one leaves.
        var down = s.RelegationOutlook();
        _out.WriteLine($"{s.HumanTeam.FullName}: going down {s.GoingDown()}, outlook {down}, budget {s.HumanTeam.Money.WageBudget}, summer {s.MySummerBudget}, committed {s.MyCommittedWages}");
        Assert.True(s.MySummerBudget <= s.HumanTeam.Money.WageBudget);
        if (down is { } d)
        {
            long room = (long)Math.Round(Math.Max(0, d.BudgetAfter - d.PayrollAfter) / d.Keep / 1000) * 1000;
            Assert.Equal(Math.Min(s.HumanTeam.Money.WageBudget, s.MyCommittedWages + room), s.MySummerBudget);
            Assert.True(d.BudgetAfter < s.HumanTeam.Money.WageBudget, "next season's budget is the smaller league's");
        }

        CutTo(s, 8);
        var hold = s.SeasonStartHold();
        Assert.NotNull(hold);
        Assert.Contains("need at least", hold);
        Assert.Contains("Staff page", hold);
        var before = s.HumanTeam.Roster.Select(p => p.Id).ToHashSet();
        int staffBefore = s.HumanTeam.Staff.Count;
        int vacant = Enum.GetValues<StaffRole>().Count(r => s.HumanTeam.Staff.All(x => x.Role != r));
        _out.WriteLine($"staff {staffBefore}, vacant roles {vacant}");

        s.AdvanceOffseason();   // a caller that skips the hold (the desktop never does)
        Assert.NotEqual(SessionPhase.Offseason, s.Phase);
        var joined = s.HumanTeam.Roster.Where(p => !before.Contains(p.Id)).ToList();
        foreach (var p in joined) _out.WriteLine($"joined {p.Name} {p.Contract!.Salary} x{p.Contract.Years}");
        Assert.NotEmpty(joined);
        // Only the minimum deals that make up the numbers (a relegated club's are cut with the rest, never below half).
        long min = s.League.Finance.MinSalary;
        Assert.All(joined, p => { Assert.Equal(1, p.Contract!.Years); Assert.True(p.Contract.Salary <= min, $"{p.Name} {p.Contract.Salary}"); });
        Assert.Equal(staffBefore, s.HumanTeam.Staff.Count);   // nobody hired for you
        var msg = Assert.Single(s.Inbox, m => m.Title.StartsWith("Summer signings"));
        Assert.Contains("one-year minimum deals", msg.Body);
        Assert.All(joined, p => Assert.Contains(p.Name, msg.Body));
    }

    /// <summary>K46 on screen: Continue says why it can't start the season and stays; with the signings delegated it goes on, and
    /// one message lists every signing with wage and years. The switches survive a save and reload.</summary>
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void K46_continue_holds_a_short_roster_and_the_delegated_signings_are_reported()
    {
        string path = Path.Combine(Path.GetTempPath(), $"run229-{Guid.NewGuid():N}.hwsave");
        try
        {
            var s = ToFreeAgency(77, path);
            CutTo(s, 8);
            var vm = new MainViewModel();
            vm.AttachSession(s);
            vm.ContinueCommand.Execute(null);
            Assert.Equal(SessionPhase.Offseason, s.Phase);
            var hold = Assert.Single(s.Inbox, m => m.Title.StartsWith("Sign ") && m.Title.EndsWith("before the season"));
            Assert.Contains("Free agents", hold.Body);
            Assert.Equal(s.SeasonStartHold(), ((OffseasonViewModel)vm.Page("Offseason")).Status);
            vm.ContinueCommand.Execute(null);   // pressing again doesn't post it twice
            Assert.Single(s.Inbox, m => m.Title == hold.Title);

            // The Staff page's switches, saved and loaded.
            var staff = (StaffViewModel)vm.Page("Staff & Training");
            staff.Refresh();
            Assert.True(staff.ShowSummerDuties);
            staff.DelegateSignings = true;
            staff.DelegateStaffHiring = true;
            var back = GameSession.Load(path);
            Assert.True(back.Delegated.Signings);
            Assert.True(back.Delegated.StaffHiring);

            Assert.Null(s.SeasonStartHold());
            var before = s.HumanTeam.Roster.Select(p => p.Id).ToHashSet();
            vm.ContinueCommand.Execute(null);
            Assert.NotEqual(SessionPhase.Offseason, s.Phase);
            var joined = s.HumanTeam.Roster.Where(p => !before.Contains(p.Id)).ToList();
            _out.WriteLine($"{s.HumanTeam.FullName} in {s.HumanLeagueName}: roster {s.HumanTeam.Roster.Count}, wages {s.MyCommittedWages}, budget {s.HumanTeam.Money.WageBudget}");
            Assert.True(s.HumanTeam.Roster.Count >= Hardwood.Core.World.GameWorld.ManagedMinimum, $"{s.HumanTeam.Roster.Count}");
            // H129/K47: the delegated signings answered to the summer's budget, so the club opens within next season's.
            Assert.True(s.MyCommittedWages <= s.HumanTeam.Money.WageBudget * 1.02, $"wages {s.MyCommittedWages} on {s.HumanTeam.Money.WageBudget}");
            var msg = Assert.Single(s.Inbox, m => m.Title.StartsWith("Summer signings"));
            _out.WriteLine(msg.Title + "\n" + msg.Body);
            Assert.Contains("sporting director", msg.Body);
            foreach (var p in joined) _out.WriteLine($"joined {p.Name} {p.Contract?.Salary} x{p.Contract?.Years} rookie {p.RookieSeason}");
            // Everyone the summer's end signed is named (the G League's own "make up the numbers" fill after the relegation cut
            // has its own message, from the world).
            var named = joined.Where(p => msg.Body.Contains(p.Name)).ToList();
            Assert.True(named.Count >= joined.Count - 2, $"{named.Count} of {joined.Count} named");
            Assert.Contains(" × ", msg.Body);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    /// <summary>An older save's delegation (no summer duties in its JSON) loads with them yours.</summary>
    [Fact]
    public void K46_an_older_saves_delegation_keeps_the_summer_duties_yours()
    {
        var d = System.Text.Json.JsonSerializer.Deserialize<Delegation>("{\"Training\":true,\"Media\":true}")!;
        Assert.True(d.Training);
        Assert.False(d.Signings);
        Assert.False(d.StaffHiring);
    }
}
