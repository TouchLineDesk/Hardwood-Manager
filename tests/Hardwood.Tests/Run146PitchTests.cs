using Hardwood.Core.College;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 146, K15a (critic review #18 item 7): the recruiting pitch. You choose what your calls and visits sell a prospect;
/// a pitch that matches what he cares about beats one that doesn't, the cards show what he cares about once scouted, and
/// the panel says why a target trails, factor by factor. With H156: the offers leaning elsewhere as a chip and a view.
/// </summary>
[Collection(SharedUiState.Name)]
public class Run146PitchTests
{
    private readonly ITestOutputHelper _out;
    public Run146PitchTests(ITestOutputHelper o) => _out = o;

    private static GameSession Career(string name, ulong seed = 77, string? path = null)
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(seed, 2026, GameSession.DefaultFullSim);
        int id = world.College!.Programs.First(p => p.FullName.StartsWith(name)).Id;
        return GameSession.NewGame(seed, id, path);
    }

    private static (Player p, Recruit r) Target(CollegeWorld c, Team me, DateOnly today, int stars)
    {
        int cls = c.RecruitingClass(today);
        return c.ClassOf(cls).Select(x => (x, c.Recruits[x.Id]))
                .First(x => x.Item2.Stars == stars && x.Item2.CommittedTo == null && x.Item2.CourtedBy != me.Id);
    }

    /// <summary>The same week's courting (scout, call, home visit, offer) under one pitch, from a clean slate.</summary>
    private static (double Interest, double Appeal) CourtUnder(GameSession s, Player p, Recruit r, RecruitPriority? pitch)
    {
        var c = s.TrailWorld!; var me = s.HumanTeam;
        r.CourtedBy = null; CollegeWorld.Courting(r, me);
        c.TrailWeek = CollegeWorld.MondayOf(s.Today); c.TrailHours = CollegeWorld.WeeklyHours;
        Assert.StartsWith("Scouted", s.ScoutProspect(p));
        s.PitchProspect(p, pitch);
        Assert.Equal(pitch, r.Pitch);
        Assert.StartsWith("You called", s.CallProspect(p));
        Assert.StartsWith("You visited", s.HomeVisitProspect(p));
        Assert.StartsWith("You offered", s.OfferProspect(p));
        return (r.Interest, c.Appeal(me, p, r, c.BoardFor(r.ClassOf)));
    }

    [Fact]
    public void A_pitch_that_matches_beats_no_pitch_and_no_pitch_beats_a_miss()
    {
        var s = Career("Providence");
        var c = s.TrailWorld!;
        foreach (int stars in new[] { 3, 4, 5 })
        {
            var (p, r) = Target(c, s.HumanTeam, s.Today, stars);
            var cares = CollegeWorld.PriorityOf(p);
            var miss = CollegeWorld.Pitches.First(k => k != cares);
            var hit = CourtUnder(s, p, r, cares);
            var none = CourtUnder(s, p, r, null);
            var wrong = CourtUnder(s, p, r, miss);
            _out.WriteLine($"{stars}-star {p.Name} cares about {cares}: interest {hit.Interest:F1} / {none.Interest:F1} / {wrong.Interest:F1}, appeal {hit.Appeal:F1} / {none.Appeal:F1} / {wrong.Appeal:F1}");
            Assert.True(hit.Interest > none.Interest && none.Interest > wrong.Interest);
            Assert.True(hit.Appeal > none.Appeal && none.Appeal > wrong.Appeal);
            // The measurable margin: the right pitch over the wrong one is worth at least a neighbouring program's gap.
            Assert.True(hit.Appeal - wrong.Appeal >= 5, $"{stars}-star: only {hit.Appeal - wrong.Appeal:F1} appeal between a hit and a miss");
        }
    }

    [Fact]
    public void The_breakdown_adds_up_to_the_appeal_and_the_panel_says_who_leads_and_why()
    {
        var s = Career("Providence");
        var c = s.TrailWorld!; var me = s.HumanTeam;
        var (p, r) = Target(c, me, s.Today, 5);
        CollegeWorld.Courting(r, me); r.Offered = true; r.Scouted = true; r.Interest = 20;
        var board = c.BoardFor(r.ClassOf);
        var rival = c.Considering(p, r, board).Select(x => x.Program).First(x => x != me);
        foreach (var t in new[] { me, rival })
            Assert.Equal(c.Appeal(t, p, r, board), c.AppealBreakdown(t, p, r, board).Sum(x => x.Value), 6);
        var why = c.WhyTrails(me, p, r, board);
        _out.WriteLine(why);
        Assert.StartsWith($"{rival.City} leads you by", why);
        Assert.Contains("prestige", why);
        Assert.Contains($"He cares most about {CollegeWorld.PriorityText(CollegeWorld.PriorityOf(p))}", why);
        r.CommittedTo = rival.Id;
        Assert.Equal("", c.WhyTrails(me, p, r, board));
    }

    [Fact]
    public void The_pitch_is_saved_and_reset_when_another_program_courts_him()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run146-{Guid.NewGuid():N}.save");
        try
        {
            var s = Career("Providence", path: path);
            var (p, r) = Target(s.TrailWorld!, s.HumanTeam, s.Today, 3);
            s.ScoutProspect(p);
            var reply = s.PitchProspect(p, CollegeWorld.PriorityOf(p));
            Assert.Contains("That is what he cares about most", reply);
            s.SaveNow();
            var loaded = GameSession.Load(path);
            var r2 = loaded.TrailWorld!.Recruits[p.Id];
            Assert.Equal(CollegeWorld.PriorityOf(p), r2.Pitch);
            var other = loaded.TrailWorld!.Programs.First(t => t.Id != loaded.HumanTeam.Id);
            CollegeWorld.Courting(r2, other);
            Assert.Null(r2.Pitch);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void The_assistant_sells_a_scouted_target_what_he_cares_about()
    {
        var s = Career("Providence");
        var c = s.TrailWorld!; var me = s.HumanTeam;
        int cls = c.RecruitingClass(s.Today);
        var monday = CollegeWorld.MondayOf(s.Today);
        var targets = c.Fits(me, cls, null, 6, -4);
        Assert.NotEmpty(targets);
        // He scouts the targets he hasn't (1 hour each), then pitches what each one cares about.
        c.TrailWeek = monday; c.TrailHours = CollegeWorld.WeeklyHours;
        c.AutoRecruit(me, monday);
        var pitched = targets.Where(x => x.r.Pitch != null).ToList();
        Assert.NotEmpty(pitched);
        Assert.All(pitched, x => Assert.True(x.r.Scouted));
        Assert.All(pitched, x => Assert.Equal(CollegeWorld.PriorityOf(x.p), x.r.Pitch));
    }

    [AvaloniaFact]
    public void The_recruiting_tab_shows_the_pitch_what_he_cares_about_and_why_he_leans_elsewhere()
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
            // A 5-star he can't win yet: pick him, scout him, offer, and pitch the wrong thing.
            var row = col.TrailCards.First(x => x.StarCount == 5);
            col.PickTrail(row); Settle();
            col.ScoutTrailCommand.Execute(null);
            col.OfferTrailCommand.Execute(null);
            var p = col.SelectedTrail!.Player;
            var miss = CollegeWorld.Pitches.First(k => k != CollegeWorld.PriorityOf(p));
            col.TrailPitch = CollegeWorld.PitchName(miss); Settle();
            Assert.Contains("worse", col.TrailReply);
            Assert.Equal(miss, s.TrailWorld!.Recruits[p.Id].Pitch);
            Assert.Contains(col.ProspectChips, ch => ch.Label == "Your pitch" && ch.Value == CollegeWorld.PitchName(miss) && ch.Sub.StartsWith("misses"));
            Assert.Contains("leads you by", col.TrailWhy);
            Assert.True(col.SelectedTrail!.HasCares);
            Assert.Equal(CollegeWorld.PitchName(miss), col.TrailPitch);
            _out.WriteLine(col.TrailWhy);
            // H156: the offer leaning elsewhere is a chip and a view, not a paragraph.
            Assert.Contains(col.TrailChips, ch => ch.Label == "Offers out" && ch.Sub == "1 leaning elsewhere" && ch.Tone == "warn");
            Assert.DoesNotContain("leaning elsewhere", col.TrailNote);
            col.TrailView = Hardwood.Desktop.ViewModels.CollegeViewModel.LeaningElsewhere; Settle();
            Assert.Contains(col.TrailRows, x => x.Player == p);
            Assert.Contains(p.Name, col.TrailCardNote);
            // The right pitch turns the chip green.
            col.PickTrail(col.TrailRows.First(x => x.Player == p));
            col.TrailPitch = CollegeWorld.PitchName(CollegeWorld.PriorityOf(p)); Settle();
            Assert.Contains(col.ProspectChips, ch => ch.Label == "Your pitch" && ch.Sub.StartsWith("fits"));
            Assert.Empty(LayoutCheck.Scan(window));
            var dir = Environment.GetEnvironmentVariable("HW_SCREENSHOT_DIR");
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
                using var frame = window.CaptureRenderedFrame();
                frame?.Save(Path.Combine(dir, "run146-recruiting-pitch.png"));
            }
        }
        finally
        {
            window?.Close();
            Hardwood.Desktop.AppSettings.Use(oldSettings); Hardwood.Desktop.AppSettings.FilePath = oldPath;
        }
    }
}
