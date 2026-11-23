using Hardwood.Core.College;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Desktop.ViewModels;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 164 (critic review #21 item 5; Danny's college order, step 2): the recruiting trail's first week. A three-step
/// guide (scout, offer, visit) that ticks itself off; the word on what a prospect cares about before the first scout
/// (right about 7 in 10, fixed per prospect); the reason he'd say no, on his card; and H178's offers-for-places line and
/// a detail pane that opens on the first card.
/// </summary>
[Collection(SharedUiState.Name)]
public class Run164RecruitingGuideTests
{
    private readonly ITestOutputHelper _out;
    public Run164RecruitingGuideTests(ITestOutputHelper o) => _out = o;

    private static GameSession Career(string name, ulong seed = 77)
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(seed, 2026, GameSession.DefaultFullSim);
        int id = world.College!.Programs.First(p => p.FullName.StartsWith(name)).Id;
        return GameSession.NewGame(seed, id, null);
    }

    [Fact]
    public void The_word_is_right_about_seven_times_in_ten_and_never_changes()
    {
        var s = Career("Providence");
        var c = s.TrailWorld!;
        int cls = c.RecruitingClass(s.Today);
        var all = Enumerable.Range(cls, 4).SelectMany(c.ClassOf).ToList();
        Assert.True(all.Count >= 1000, $"{all.Count} prospects");
        double right = all.Count(p => CollegeWorld.WordOf(p) == CollegeWorld.PriorityOf(p)) / (double)all.Count;
        _out.WriteLine($"{all.Count} prospects, the word is right {right:P1}");
        Assert.InRange(right, 0.64, 0.76);
        Assert.All(all.Take(200), p => Assert.Equal(CollegeWorld.WordOf(p), CollegeWorld.WordOf(p)));
        // A wrong word is spread over the other three, not always the same neighbour.
        var wrong = all.Where(p => CollegeWorld.WordOf(p) != CollegeWorld.PriorityOf(p))
                       .Select(p => ((int)CollegeWorld.WordOf(p) - (int)CollegeWorld.PriorityOf(p) + 4) % 4).Distinct().Count();
        Assert.Equal(3, wrong);
    }

    [Fact]
    public void The_guide_ticks_off_scout_offer_and_visit_then_goes_away()
    {
        var s = Career("Providence");
        var c = s.TrailWorld!; var me = s.HumanTeam;
        int cls = c.RecruitingClass(s.Today);
        var steps = c.FirstWeekGuide(me, cls, s.Today);
        Assert.Equal(new[] { "Scout a best fit", "Offer him a scholarship", "Visit him" }, steps.Select(x => x.Title));
        Assert.All(steps, x => Assert.False(x.Done));
        var (p, _) = c.Fits(me, cls, c.BoardFor(cls), 10).First();
        Assert.StartsWith("Scouted", s.ScoutProspect(p));
        Assert.Equal(new[] { true, false, false }, c.FirstWeekGuide(me, cls, s.Today).Select(x => x.Done));
        Assert.StartsWith("You offered", s.OfferProspect(p));
        Assert.Equal(new[] { true, true, false }, c.FirstWeekGuide(me, cls, s.Today).Select(x => x.Done));
        Assert.StartsWith("You visited", s.HomeVisitProspect(p));
        Assert.All(c.FirstWeekGuide(me, cls, s.Today), x => Assert.True(x.Done));
        // Another class (not recruitable yet) has no guide.
        Assert.Empty(c.FirstWeekGuide(me, cls + 1, s.Today));
    }

    [Fact]
    public void A_prospect_says_why_he_would_turn_you_down()
    {
        var s = Career("Providence");
        var c = s.TrailWorld!; var me = s.HumanTeam;
        int cls = c.RecruitingClass(s.Today);
        var board = c.BoardFor(cls);
        var ranks = c.PrestigeRanks();
        var five = c.ClassOf(cls).Select(p => (p, r: c.Recruits[p.Id])).First(x => x.r.Stars == 5 && x.r.CommittedTo == null);
        var lean = c.LeaningTo(five.p, five.r, board);
        Assert.NotNull(lean); Assert.NotSame(me, lean);
        string no = c.SaysNo(me, five.p, five.r, board, lean, ranks);
        _out.WriteLine(no);
        Assert.StartsWith($"Says no: {lean!.City} leads by ", no);
        Assert.Matches(@"\(.+\)$", no);
        // Leaning your way without an offer: he can't commit yet.
        Assert.Equal("Leans you, but can't commit: no offer yet", c.SaysNo(me, five.p, five.r, board, me, ranks));
        // With an offer and leaning your way, nothing to say.
        CollegeWorld.Courting(five.r, me); five.r.Offered = true;
        Assert.Equal("", c.SaysNo(me, five.p, five.r, board, me, ranks));
    }

    [Fact]
    public void More_offers_than_places_says_who_takes_them()
    {
        Assert.Equal("5 offers out for 2 places: the first 2 to commit take them, and the rest can't commit to you once the class is full. ",
            CollegeViewModel.PlacesNote(5, 5, 2));
        Assert.Equal("3 offers out for 1 place: the first one to commit takes it, and the rest can't commit to you once the class is full. ",
            CollegeViewModel.PlacesNote(3, 4, 1));
        Assert.Equal("", CollegeViewModel.PlacesNote(2, 5, 2));
        Assert.Equal("", CollegeViewModel.PlacesNote(6, 5, 2)); // over the limit: OverOffersNote says it
        Assert.Equal("", CollegeViewModel.PlacesNote(3, 0, 0));
    }

    [AvaloniaFact]
    public void Day_one_on_the_recruiting_tab_has_the_guide_the_word_and_the_reasons()
    {
        var oldSettings = Hardwood.Desktop.AppSettings.Current; var oldPath = Hardwood.Desktop.AppSettings.FilePath;
        Hardwood.Desktop.AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        Hardwood.Desktop.AppSettings.Use(new Hardwood.Desktop.AppSettings { AutoPortraits = false });
        Hardwood.Desktop.Views.MainWindow? window = null;
        try
        {
            var s = Career("Providence");
            var vm = new MainViewModel();
            window = new Hardwood.Desktop.Views.MainWindow { DataContext = vm, Width = 1480, Height = 900 };
            window.Show();
            vm.AttachSession(s);
            vm.Navigate("College");
            var col = Assert.IsType<CollegeViewModel>(vm.CurrentPage);
            void Settle() { for (int i = 0; i < 3; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window!.UpdateLayout(); } }
            col.Tab = 5; col.TrailView = "Best fits"; col.BoardView = "Cards"; Settle();
            Assert.True(col.HasTrailGuide);
            Assert.Equal(3, col.TrailGuide.Count);
            Assert.Contains("0 of 3 done", col.TrailGuideHeader);
            Assert.Contains("start with No.", col.TrailGuideHeader);
            // H178: the detail pane opens on the first card.
            Assert.True(col.HasTrail);
            Assert.Same(col.TrailCards[0], col.SelectedTrail);
            // The word before the first scout, on every card.
            Assert.All(col.TrailCards, x => Assert.StartsWith("Word is: ", x.CaresText));
            Assert.Contains(col.ProspectChips, ch => ch.Label == "Cares most about" && ch.Value.EndsWith("?") && ch.Sub.StartsWith("the word"));
            Assert.Contains("The word is he cares about", col.TrailPitchNote);
            _out.WriteLine(string.Join("\n", col.TrailCards.Take(8).Select(x => $"{x.Name}: {x.CaresText} · {x.NoReason}")));
            Assert.Contains(col.TrailCards, x => x.HasNoReason);
            Assert.Empty(LayoutCheck.Scan(window));
            var dir = Environment.GetEnvironmentVariable("HW_SCREENSHOT_DIR");
            void Shot(string name)
            {
                if (string.IsNullOrEmpty(dir)) return;
                Directory.CreateDirectory(dir);
                using var frame = window!.CaptureRenderedFrame();
                frame?.Save(Path.Combine(dir, name));
            }
            Shot("run164-recruiting-day-one.png");
            // Scout the first fit: his card says "Cares about", and step 1 ticks off.
            col.ScoutTrailCommand.Execute(null); Settle();
            Assert.StartsWith("Cares about ", col.SelectedTrail!.CaresText);
            Assert.Contains("1 of 3 done", col.TrailGuideHeader);
            Assert.Equal("✓", col.TrailGuide[0].Mark);
            col.OfferTrailCommand.Execute(null);
            col.HomeVisitTrailCommand.Execute(null); Settle();
            Assert.False(col.HasTrailGuide);
            Assert.Empty(LayoutCheck.Scan(window));
            Shot("run164-recruiting-after-first-week.png");
        }
        finally
        {
            window?.Close();
            Hardwood.Desktop.AppSettings.Use(oldSettings); Hardwood.Desktop.AppSettings.FilePath = oldPath;
        }
    }
}
