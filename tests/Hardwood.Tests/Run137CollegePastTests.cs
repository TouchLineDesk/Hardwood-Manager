using Avalonia.Headless;
using Hardwood.Core;
using Hardwood.Core.College;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.RealWorld;
using Hardwood.Core.Season;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 137, K12b (critic review #18 item 7): a real college career has a past.
/// - the NCAA's champions and Final Fours since 1985 (Data/ncaa_history.json), agreeing with each program's totals;
/// - program banners: real totals plus this career's own tournaments (Kentucky: 8 titles, 1996, 1998 and 2012 since 1985);
/// - real NBA alumni in Your pros from day one (Miami (FL): Kyshawn George, Jordan Miller), seeded once and saved;
/// - History's Seasons shows the last ten real champions, and a College records tab has the records book.
/// </summary>
public class Run137CollegePastTests
{
    private readonly ITestOutputHelper _out;
    public Run137CollegePastTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void The_real_tournaments_agree_with_the_program_totals()
    {
        var ts = RealCollegeHistory.Tournaments;
        Assert.Equal(1985, RealCollegeHistory.FirstYear);
        Assert.Equal(2026, RealCollegeHistory.LastYear);
        Assert.Equal(41, ts.Count); // 1985-2026 without 2020 (cancelled)
        Assert.DoesNotContain(ts, t => t.Year == 2020);
        var schools = RealColleges.Programs.ToDictionary(p => p.School);
        foreach (var t in ts)
        {
            Assert.Equal(4, t.FinalFour.Distinct().Count());
            Assert.All(t.FinalFour, s => Assert.True(schools.ContainsKey(s), $"{t.Year}: {s} is not a Division I program"));
            Assert.All(t.Vacated, s => Assert.Contains(s, t.FinalFour));
        }
        // No program has more titles or Final Fours since 1985 than its all-time totals, except Florida's Final Fours: the
        // programs pack says 5, the list has 6 (the banners take the larger; the pack sets prestige and stays as it is).
        foreach (var p in RealColleges.Programs)
        {
            Assert.True(RealCollegeHistory.TitleYears(p.School).Count <= p.Titles, $"{p.School} titles");
            if (p.School != "Florida") Assert.True(RealCollegeHistory.FinalFourYears(p.School).Count <= p.FinalFours, $"{p.School} Final Fours");
        }
        Assert.Equal(new[] { 1996, 1998, 2012 }, RealCollegeHistory.TitleYears("Kentucky"));
        Assert.Equal(new[] { 1999, 2004, 2011, 2014, 2023, 2024 }, RealCollegeHistory.TitleYears("UConn"));
        Assert.Equal("2025-26", ts.Single(t => t.Year == 2026).Season);
        // Every school with listed alumni is a real program.
        Assert.All(RealCollegeHistory.Alumni.Keys, s => Assert.True(schools.ContainsKey(s), s));
    }

    [Fact]
    public void Banners_add_this_careers_tournaments_to_the_real_ones()
    {
        var rng = new Rng(137);
        var gen = new PlayerGenerator(new Rng(137 ^ 0x51), 2_000_000);
        var c = CollegeWorld.Generate(gen, rng, new DateOnly(2026, 10, 1), real: true);
        var uk = c.Programs.First(p => p.City == "Kentucky");
        var past = c.PastOf(uk);
        Assert.Equal(8, past.Titles);
        Assert.Equal(new[] { 1996, 1998, 2012 }, past.TitleYears);
        Assert.Equal(5, past.TitlesEarlier);
        Assert.Equal(("8 titles", "17 Final Fours · 64 NCAA tournaments"), past.Chip());
        string line = past.Line(c.FirstListedYear);
        _out.WriteLine(line);
        Assert.StartsWith("National champions 1996, 1998, 2012 (and 5 times before 1985)", line);
        var app = c.Programs.First(p => p.City == "Appalachian State");
        Assert.Equal(("3 NCAA tournaments", "no Final Four yet"), c.PastOf(app).Chip());
        var before = c.Pasts().ToDictionary(p => p.Program.Id);

        // One season: the champion hangs a banner for 2027, and the Final Four teams add one each.
        c.PlayThrough(new DateOnly(2027, 4, 30), new GameSimulator(), rng);
        var champ = c.Program(c.Season.ChampionId!.Value)!;
        var after = c.PastOf(champ);
        _out.WriteLine($"{champ.FullName}: {after.Line(1985)}");
        Assert.Equal(before[champ.Id].Titles + 1, after.Titles);
        Assert.Equal(2027, after.LastTitle);
        Assert.Equal(1, after.SaveTitles);
        var fours = c.Awards.Single().Records.Where(kv => kv.Value[2] >= 5).Select(kv => kv.Key).ToList();
        Assert.Equal(4, fours.Count);
        Assert.All(fours, id => Assert.Equal(before[id].FinalFours + 1, c.PastOf(c.Program(id)!).FinalFours));
        int bids = c.Awards.Single().Records.Count(kv => kv.Value[2] >= 0);
        Assert.Equal(c.Pasts().Sum(p => p.Appearances) - before.Values.Sum(p => p.Appearances), bids);
        Assert.Equal(5, c.BestSeasons(5).Count);
    }

    [Fact]
    public void A_real_career_starts_with_its_nba_alumni_once()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hw-run137-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "career.hwsave");
        try
        {
            var (_, world) = RealWorldData.NewWorld(2026, 2026, GameSession.DefaultFullSim);
            int id = world.College!.Programs.First(p => p.FullName.StartsWith("Miami (FL)")).Id;
            var s = GameSession.NewGame(2026, id, path);
            var c = s.World.College!;
            Assert.True(c.RealAlumniSeeded);
            var rec = s.ProRecordOf(s.HumanTeam);
            _out.WriteLine($"{c.Alumni.Count} real alumni; Miami: {string.Join(", ", rec.Lines.Select(l => $"{l.Player.Name} ({l.Club})"))}");
            Assert.Contains(rec.Lines, l => l.Player.Name == "Kyshawn George");
            Assert.Contains(rec.Lines, l => l.Player.Name == "Jordan Miller");
            Assert.All(rec.Lines, l => Assert.Equal(0, l.Class));
            Assert.True(rec.TopLeague >= 1);
            Assert.True(c.ProPitch > 0, "Miami's NBA alumni count in the pitch on day one");
            Assert.True(c.Alumni.Count >= 80, $"only {c.Alumni.Count} real alumni matched");
            // Nobody is claimed twice, and no college player or recruit is anyone's alumnus.
            var college = c.Players.Select(p => p.Id).ToHashSet();
            Assert.DoesNotContain(c.Alumni.Keys, college.Contains);
            Assert.Equal(0, s.SeedRealAlumni()); // once per career

            // Saved and loaded: the same alumni, not seeded again.
            s.SaveNow();
            var back = GameSession.Load(path);
            Assert.True(back.World.College!.RealAlumniSeeded);
            Assert.Equal(c.Alumni.Count, back.World.College.Alumni.Count);
            Assert.Equal(rec.Lines.Count, back.ProRecordOf(back.HumanTeam).Lines.Count);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void A_fictional_world_has_no_real_past()
    {
        var rng = new Rng(9);
        var gen = new PlayerGenerator(new Rng(9 ^ 0x51), 2_000_000);
        var c = CollegeWorld.Generate(gen, rng, new DateOnly(2026, 10, 1));
        Assert.False(c.IsReal);
        Assert.All(c.Pasts(), p => Assert.Equal(0, p.Titles + p.FinalFours + p.Appearances));
        Assert.Equal("", c.PastOf(c.Programs[0]).Line(c.FirstListedYear));
    }
}

/// <summary>Run 137 on screen: Kentucky's banners, History's real past and records book, and Miami's real alumni.</summary>
[Collection(SharedUiState.Name)]   // it swaps AppSettings, app-wide state
public class Run137CollegePastUiTests
{
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void A_college_career_opens_on_a_real_past()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run137-{Guid.NewGuid():N}.hwsave");
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { AutoPortraits = false });
        MainWindow? window = null;
        try
        {
            var (_, world) = RealWorldData.NewWorld(2026, 2026, GameSession.DefaultFullSim);
            int id = world.College!.Programs.First(p => p.FullName.StartsWith("Miami (FL)")).Id;
            var s = GameSession.NewGame(2026, id, path);
            var cw = s.World.College!;
            var vm = new MainViewModel();
            window = new MainWindow { DataContext = vm, Width = 1480, Height = 900 };
            window.Show();
            vm.AttachSession(s);
            var w = window;
            var dir = Environment.GetEnvironmentVariable("HW_SCREENSHOT_DIR");
            void Settle() { for (int i = 0; i < 6; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); } }
            void Shot(string file)
            {
                Settle();
                if (string.IsNullOrEmpty(dir)) return;
                Directory.CreateDirectory(dir);
                using var frame = w.CaptureRenderedFrame();
                frame?.Save(Path.Combine(dir, file));
            }

            // Kentucky's Team page: 8 titles on the chip, the years under the header.
            vm.ShowTeam(cw.Programs.First(p => p.City == "Kentucky")); Settle();
            var team = (TeamViewModel)vm.CurrentPage!;
            var chip = team.Chips.Single(c => c.Label == "Banners");
            Assert.Equal("8 titles", chip.Value);
            Assert.True(team.HasBanners);
            Assert.StartsWith("National champions 1996, 1998, 2012 (and 5 times before 1985)", team.Banners);
            Shot("run137-college-team-banners.png");
            // A pro club has no banners line.
            vm.ShowTeam(s.League.Teams[0]); Settle();
            Assert.False(((TeamViewModel)vm.CurrentPage!).HasBanners);

            // History before any season: the last ten real champions, newest first, and the records book.
            vm.Navigate("History"); Settle();
            var hist = (HistoryViewModel)vm.CurrentPage!;
            Assert.True(hist.IsCollege);
            Assert.False(hist.HasSeasonCards);
            Assert.Equal(10, hist.RealPastCards.Count);
            var last = hist.RealPastCards[0];
            Assert.Equal("2025-26", last.Season);
            Assert.StartsWith("Michigan", last.ChampionName);
            Assert.Equal("beat UConn 69–63 in the final", last.Beat);
            Assert.Equal("Arizona · Illinois", last.FinalsMvp);
            Assert.Equal("Michigan's 2nd national title", last.Mvp);
            Assert.Contains("real college game's recent past", hist.SeasonsNote);
            Assert.Contains("College records tab", hist.RealPastFootnote);
            Shot("run137-college-history-past.png");

            hist.Tab = 2; Settle();
            Assert.Equal(41, hist.CollegeChampions.Count);
            Assert.Contains(hist.CollegeChampions, r => r.Season == "2012-13" && r.Note.Contains("Louisville"));
            var titles = hist.CollegeRecords.First();
            Assert.Equal("National titles", titles.Title);
            Assert.Equal("11", titles.Top!.Value);
            Assert.StartsWith("UCLA", titles.Top.Who);
            Assert.Contains(hist.CollegeRecords, t => t.Title == "Final Fours");
            Assert.Contains("Miami (FL)", hist.CollegeRecordsNote);
            Shot("run137-college-records.png");
            // The tab survives a refresh (only the hidden pro tabs send it back to Seasons).
            hist.Refresh(); Settle();
            Assert.Equal(2, hist.Tab);

            // Your pros: Miami's real NBA alumni, marked Earlier.
            vm.Navigate("College"); Settle();
            var col = (CollegeViewModel)vm.CurrentPage!;
            col.Tab = 8; Settle();
            Assert.Contains(col.Alumni, r => r.Name == "Kyshawn George" && r.Class == "Earlier" && r.Draft == "-");
            Assert.Contains("Rows marked Earlier", col.AlumniNote);
            Shot("run137-college-your-pros.png");
        }
        finally
        {
            window?.Close();
            AppSettings.FilePath = oldPath; AppSettings.Use(oldSettings);
            try { File.Delete(path); } catch { }
        }
    }
}
