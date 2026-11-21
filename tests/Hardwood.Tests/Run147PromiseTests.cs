using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Hardwood.Core.College;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 147, K15b (critic review #18 item 7; K15): promises and decommitments. A starting-role promise sways a prospect,
/// is limited to two a class, goes with him to college and is judged in mid-January; a broken one makes him unhappy, puts
/// a line in the inbox and costs you with the next class. A verbal commitment can flip until he signs, to you or away.
/// With the run 146 review's open answer: the assistant leaves a deliberate "No pitch" alone.
/// </summary>
[Collection(SharedUiState.Name)]
public class Run147PromiseTests
{
    private readonly ITestOutputHelper _out;
    public Run147PromiseTests(ITestOutputHelper o) => _out = o;

    private static GameSession Career(string name, ulong seed = 77, string? path = null)
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(seed, 2026, GameSession.DefaultFullSim);
        int id = world.College!.Programs.First(p => p.FullName.StartsWith(name)).Id;
        return GameSession.NewGame(seed, id, path);
    }

    private static List<(Player p, Recruit r)> Open(CollegeWorld c, Team me, DateOnly today, int stars) =>
        c.ClassOf(c.RecruitingClass(today)).Select(x => (x, c.Recruits[x.Id]))
         .Where(x => x.Item2.Stars == stars && x.Item2.CommittedTo == null && x.Item2.CourtedBy != me.Id).ToList();

    private static void FreshHours(GameSession s) { var c = s.TrailWorld!; c.TrailWeek = CollegeWorld.MondayOf(s.Today); c.TrailHours = CollegeWorld.WeeklyHours; }

    [Fact]
    public void A_promise_adds_appeal_two_a_class_and_cannot_be_taken_back()
    {
        var s = Career("Providence");
        var c = s.TrailWorld!; var me = s.HumanTeam;
        var three = Open(c, me, s.Today, 3).Take(3).ToList();
        FreshHours(s);
        var (p, r) = three[0];
        var board = c.BoardFor(r.ClassOf);
        double before = c.Appeal(me, p, r, board);
        var reply = s.PromiseProspect(p);
        _out.WriteLine(reply);
        Assert.StartsWith($"You promised {p.Name} a starting role", reply);
        Assert.True(r.Promised);
        Assert.Equal(before + CollegeWorld.PromiseAppeal, c.Appeal(me, p, r, board), 6);
        Assert.Contains(c.AppealBreakdown(me, p, r, board), x => x.Factor == "promise" && x.Value == CollegeWorld.PromiseAppeal);
        Assert.Equal(c.Appeal(me, p, r, board), c.AppealBreakdown(me, p, r, board).Sum(x => x.Value), 6);
        Assert.Contains("can't be taken back", s.PromiseProspect(p));
        Assert.StartsWith("You promised", s.PromiseProspect(three[1].p));
        Assert.Contains("only five places", s.PromiseProspect(three[2].p));
        Assert.False(three[2].r.Promised);
        Assert.Equal(2, c.PromisesMade(me, r.ClassOf));
        // Another program's courting wipes it (a new job starts from nothing).
        CollegeWorld.Courting(r, c.Programs.First(t => t != me));
        Assert.False(r.Promised);
    }

    [Fact]
    public void A_commitment_is_verbal_until_he_signs()
    {
        var r = new Recruit { ClassOf = 2027, CommittedTo = 1, CommittedOn = new DateOnly(2026, 10, 1) };
        Assert.False(CollegeWorld.Signed(r, new DateOnly(2026, 11, 7)));
        Assert.True(CollegeWorld.Signed(r, new DateOnly(2026, 11, 8)));
        r.CommittedOn = new DateOnly(2026, 11, 15); // in the early period: binding at once
        Assert.True(CollegeWorld.Signed(r, new DateOnly(2026, 11, 15)));
        r.CommittedOn = new DateOnly(2026, 12, 7); // after it: verbal until signing day
        Assert.False(CollegeWorld.Signed(r, new DateOnly(2027, 3, 1)));
        Assert.True(CollegeWorld.Signed(r, CollegeWorld.SigningDay(2027)));
        // The roll is deterministic and roughly uniform.
        var rolls = Enumerable.Range(1, 4000).Select(i => CollegeWorld.FlipRoll(i, new DateOnly(2026, 12, 7))).ToList();
        Assert.Equal(rolls, Enumerable.Range(1, 4000).Select(i => CollegeWorld.FlipRoll(i, new DateOnly(2026, 12, 7))).ToList());
        double share = rolls.Count(x => x < CollegeWorld.FlipChance) / 4000.0;
        Assert.InRange(share, CollegeWorld.FlipChance - 0.03, CollegeWorld.FlipChance + 0.03);
    }

    /// <summary>A Monday after the early period on which this player's roll comes up.</summary>
    private static DateOnly FlipMonday(Player p, int cls)
    {
        var d = CollegeWorld.MondayOf(CollegeWorld.EarlySigningEnd(cls)).AddDays(7);
        while (CollegeWorld.FlipRoll(p.Id, d) >= CollegeWorld.FlipChance) d = d.AddDays(7);
        Assert.True(d < CollegeWorld.SigningDay(cls));
        return d;
    }

    [Fact]
    public void A_rivals_verbal_commit_flips_to_you_and_yours_can_flip_away()
    {
        var s = Career("Duke");
        var c = s.TrailWorld!; var me = s.HumanTeam;
        int cls = c.RecruitingClass(s.Today);
        var ranks = c.PrestigeRanks();
        var weak = c.Programs.OrderBy(t => c.Prestige[t.Id]).First(t => c.BoardFor(cls).Open[t.Id] > 1);
        // A 3-star verbally committed to the weakest program, offered and courted hard by Duke: he flips to you.
        var (p, r) = Open(c, me, s.Today, 3).First();
        var day = FlipMonday(p, cls);
        r.CommittedTo = weak.Id; r.CommittedOn = CollegeWorld.EarlySigningEnd(cls).AddDays(1);
        CollegeWorld.Courting(r, me); r.Offered = true; r.Interest = 90;
        c.CoachNews.Clear();
        var board = c.BoardFor(cls);
        Assert.True(c.Reconsider(cls, day, board, ranks) >= 1);
        Assert.Equal(me.Id, r.CommittedTo);
        Assert.Contains(c.CoachNews, x => x.Title == $"{p.Name} flips to you" && x.Body.Contains(weak.FullName));
        // The other way: a prospect committed to you whom a rival now leads clearly (you never called, no offer kept).
        var (q, rq) = Open(c, me, s.Today, 4).First(x => x.p.Id != p.Id);
        var day2 = FlipMonday(q, cls);
        CollegeWorld.Courting(rq, me); rq.Offered = true; rq.Interest = 0;
        rq.CommittedTo = me.Id; rq.CommittedOn = CollegeWorld.EarlySigningEnd(cls).AddDays(1);
        c.Prestige[me.Id] -= 60; // a scandal: Duke is no longer Duke
        c.CoachNews.Clear();
        int flips = c.Reconsider(cls, day2, c.BoardFor(cls), c.PrestigeRanks());
        Assert.True(flips >= 1);
        Assert.NotEqual(me.Id, rq.CommittedTo);
        Assert.NotNull(rq.CommittedTo);
        Assert.Contains(c.CoachNews, x => x.Title == $"{q.Name} decommits");
        // A signed commitment never flips.
        rq.CommittedTo = weak.Id; rq.CommittedOn = CollegeWorld.EarlySigningStart(cls).AddDays(-10);
        Assert.True(CollegeWorld.Signed(rq, day2));
        c.Reconsider(cls, day2, c.BoardFor(cls), c.PrestigeRanks());
        Assert.Equal(weak.Id, rq.CommittedTo);
    }

    [Fact]
    public void A_verbal_commit_elsewhere_can_be_courted_but_a_signed_one_cannot()
    {
        var s = Career("Providence");
        var c = s.TrailWorld!; var me = s.HumanTeam;
        var (p, r) = Open(c, me, s.Today, 3).First();
        var rival = c.Programs.First(t => t != me);
        r.CommittedTo = rival.Id; r.CommittedOn = s.Today;
        FreshHours(s);
        Assert.False(CollegeWorld.Signed(r, s.Today));
        Assert.StartsWith("You called", s.CallProspect(p));
        int before = c.OffersOut(me, r.ClassOf), promised = c.PromisesMade(me, r.ClassOf);
        Assert.Contains("only verbally", s.OfferProspect(p));
        // Review: an offer and a promise to a verbal commit elsewhere still count against the limits.
        Assert.Equal(before + 1, c.OffersOut(me, r.ClassOf));
        s.PromiseProspect(p);
        Assert.Equal(promised + 1, c.PromisesMade(me, r.ClassOf));
        Assert.Contains("verbally", c.CommitmentText(r, s.Today, me));
        r.CommittedOn = CollegeWorld.EarlySigningStart(r.ClassOf).AddDays(-30);
        var signedDay = CollegeWorld.EarlySigningStart(r.ClassOf).AddDays(1);
        Assert.Equal($"{p.Name} has signed with {rival.FullName}.", c.Call(me, p, r, signedDay));
        Assert.Equal($"Signed with {rival.FullName}.", c.CommitmentText(r, signedDay, me));
    }

    [Fact]
    public void Flips_happen_but_rarely_over_a_season()
    {
        var s = Career("Providence");
        var c = s.TrailWorld!;
        int cls = c.RecruitingClass(s.Today);
        var end = CollegeWorld.SigningDay(cls).AddDays(1);
        while (s.Today < end) s.AdvanceDay();
        int top = c.ClassOf(cls).Count(p => c.Recruits[p.Id] is { CommittedTo: not null, Rank: <= CollegeWorld.ReconsiderTop });
        _out.WriteLine($"Class of {cls}: {c.Flips} flips; {top} of the top {CollegeWorld.ReconsiderTop} signed");
        Assert.InRange(c.Flips, top / 50, top / 6);
    }

    [Fact]
    public void A_broken_promise_makes_him_unhappy_tells_you_and_costs_you_with_the_next_class()
    {
        var s = Career("Providence");
        var c = s.TrailWorld!; var me = s.HumanTeam;
        // Promises to the whole roster, made known a week before they're judged: the starters keep theirs, the bench doesn't.
        var week = new DateOnly(c.Season.Year + 1, 1, 12);
        while (s.Today < week) s.AdvanceDay();
        foreach (var x in me.Roster) c.Pledges.Add(new RecruitPledge { PlayerId = x.Id, ProgramId = me.Id, Season = c.Season.Year });
        var jan = new DateOnly(c.Season.Year + 1, 1, 19);
        while (s.Today < jan) s.AdvanceDay();
        foreach (var x in me.Roster)
            _out.WriteLine($"{x.Name}: {c.Pledges.First(y => y.PlayerId == x.Id).Status}, {s.MyStats.GetValueOrDefault(x.Id)?.Starts} starts in {s.MyStats.GetValueOrDefault(x.Id)?.Games}, {s.MyStats.GetValueOrDefault(x.Id)?.Mpg:F1} mpg");
        var fr = me.Roster.First(x => c.Pledges.First(y => y.PlayerId == x.Id).Status == "broken");
        var kept = me.Roster.First(x => c.Pledges.First(y => y.PlayerId == x.Id).Status == "kept");
        var bench = c.Pledges.First(x => x.PlayerId == fr.Id);
        var star = c.Pledges.First(x => x.PlayerId == kept.Id);
        Assert.InRange(c.Pledges.Count(x => x.Status == "kept"), 4, 8);
        Assert.Equal("broken", bench.Status);
        Assert.Equal("kept", star.Status);
        Assert.Contains(s.Inbox, m => m.Title == $"Promise broken: {fr.Name}" && m.Body.Contains("feels lied to"));
        Assert.Contains(s.Inbox, m => m.Title == $"Promise kept: {kept.Name}");
        Assert.NotNull(fr.DistrustUntil);
        Assert.True(c.WordBroken);
        // The trail has heard: every prospect sees you 4 lower, and a new promise is refused.
        var (p, r) = Open(c, me, s.Today, 3).First();
        var board = c.BoardFor(r.ClassOf);
        Assert.Contains(c.AppealBreakdown(me, p, r, board), x => x.Factor == "your word" && x.Value == -CollegeWorld.BrokenWordPenalty);
        Assert.Contains("broke a promise", s.PromiseProspect(p));
        Assert.False(r.Promised);
    }

    [Fact]
    public void A_promise_goes_to_college_with_him_and_everything_is_saved()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run147-{Guid.NewGuid():N}.save");
        try
        {
            var s = Career("Providence", path: path);
            var c = s.TrailWorld!; var me = s.HumanTeam;
            var (p, r) = Open(c, me, s.Today, 3).First();
            FreshHours(s);
            s.PromiseProspect(p);
            s.PitchProspect(p, null);
            Assert.True(r.PitchChosen);
            c.BrokenWordUntil = new DateOnly(2020, 1, 1);
            c.Pledges.Add(new RecruitPledge { PlayerId = 42, ProgramId = me.Id, Season = c.Season.Year, Status = "kept" });
            s.SaveNow();
            var loaded = GameSession.Load(path);
            var c2 = loaded.TrailWorld!;
            Assert.True(c2.Recruits[p.Id].Promised);
            Assert.True(c2.Recruits[p.Id].PitchChosen);
            Assert.Equal(new DateOnly(2020, 1, 1), c2.BrokenWordUntil);
            Assert.Contains(c2.Pledges, x => x.PlayerId == 42 && x.Status == "kept");
            // Enrollment: committed to you with your promise, he carries it to college.
            r = c2.Recruits[p.Id];
            r.CommittedTo = loaded.HumanTeam.Id; r.CommittedOn = loaded.Today;
            var summer = new DateOnly(r.ClassOf, 7, 2);
            while (loaded.Today < summer) { loaded.AdvanceDay(); if (loaded.Phase == SessionPhase.Offseason) loaded.AdvanceOffseason(); }
            Assert.Contains(c2.Pledges, x => x.PlayerId == p.Id && x.ProgramId == loaded.HumanTeam.Id && x.Season == r.ClassOf && x.Status == "pending");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void The_assistant_leaves_a_deliberate_no_pitch_alone()
    {
        var s = Career("Providence");
        var c = s.TrailWorld!; var me = s.HumanTeam;
        int cls = c.RecruitingClass(s.Today);
        var monday = CollegeWorld.MondayOf(s.Today);
        var targets = c.Fits(me, cls, null, 6, -4);
        var (p, r) = targets[0];
        FreshHours(s);
        s.ScoutProspect(p);
        s.PitchProspect(p, null);
        c.TrailWeek = monday; c.TrailHours = CollegeWorld.WeeklyHours;
        c.AutoRecruit(me, monday);
        Assert.Null(r.Pitch);
        Assert.Contains(targets.Skip(1), x => x.r.Pitch != null);
    }

    [AvaloniaFact]
    public void The_recruiting_panel_offers_the_promise_and_says_a_commitment_is_verbal()
    {
        var oldSettings = Hardwood.Desktop.AppSettings.Current; var oldPath = Hardwood.Desktop.AppSettings.FilePath;
        Hardwood.Desktop.AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        Hardwood.Desktop.AppSettings.Use(new Hardwood.Desktop.AppSettings { AutoPortraits = false });
        Hardwood.Desktop.Views.MainWindow? window = null;
        try
        {
            var s = Career("Providence");
            var vm = new Hardwood.Desktop.ViewModels.MainViewModel();
            window = new Hardwood.Desktop.Views.MainWindow { DataContext = vm, Width = 1480, Height = 900 };
            window.Show();
            vm.AttachSession(s);
            vm.Navigate("College");
            var col = Assert.IsType<Hardwood.Desktop.ViewModels.CollegeViewModel>(vm.CurrentPage);
            void Settle() { for (int i = 0; i < 3; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window!.UpdateLayout(); } }
            col.Tab = 5; col.TrailView = "All uncommitted"; col.BoardView = "Cards"; Settle();
            var row = col.TrailCards.Last();
            col.PickTrail(row); Settle();
            col.ScoutTrailCommand.Execute(null);
            col.PromiseTrailCommand.Execute(null); Settle();
            Assert.StartsWith("You promised", col.TrailReply);
            Assert.Contains(col.ProspectChips, ch => ch.Label == "Promise" && ch.Value == "Starting role" && ch.Tone == "good");
            Assert.Contains(window.GetVisualDescendants().OfType<Avalonia.Controls.Button>(), b => b.Content is "Promise a starting role (1 h)");
            // A rival's verbal commit: the panel says he can still flip.
            var c = s.TrailWorld!;
            var p = col.SelectedTrail!.Player;
            c.Recruits[p.Id].CommittedTo = c.Programs.First(t => t != s.HumanTeam).Id; c.Recruits[p.Id].CommittedOn = s.Today;
            col.TrailView = "My board"; Settle();
            col.PickTrail(col.TrailRows.First(x => x.Player == p)); Settle();
            Assert.Contains("verbally", col.TrailConsidering);
            Assert.Empty(LayoutCheck.Scan(window));
            var dir = Environment.GetEnvironmentVariable("HW_SCREENSHOT_DIR");
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
                using var frame = window.CaptureRenderedFrame();
                frame?.Save(Path.Combine(dir, "run147-recruiting-promise.png"));
            }
        }
        finally
        {
            window?.Close();
            Hardwood.Desktop.AppSettings.Use(oldSettings); Hardwood.Desktop.AppSettings.FilePath = oldPath;
        }
    }
}
