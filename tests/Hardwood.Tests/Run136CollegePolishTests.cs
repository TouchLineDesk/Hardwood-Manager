using Avalonia.Headless;
using Hardwood.Core;
using Hardwood.Core.College;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Guide;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Hardwood.Core.Model;
using Hardwood.Desktop.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 136, the college polish run:
/// - H152: the delegated assistant withdraws spring offers that won't be accepted and offers prospects who lean his way, so
///   a mid-major fills its class (it used to sign 1 of 19-27 offers some years);
/// - H154: the final poll counts March (the champion first);
/// - K10: the live-game assistant reads this game;
/// - neutral-site games read "vs X (neutral)".
/// </summary>
public class Run136CollegePolishTests
{
    private readonly ITestOutputHelper _out;
    public Run136CollegePolishTests(ITestOutputHelper o) => _out = o;

    private static (CollegeWorld c, Rng rng) World(ulong seed)
    {
        var rng = new Rng(seed);
        var gen = new PlayerGenerator(new Rng(seed ^ 0x51), 2_000_000);
        return (CollegeWorld.Generate(gen, rng, new DateOnly(2026, 10, 1), real: true), rng);
    }

    [Theory]
    [InlineData(31UL)]
    [InlineData(2026UL)]
    public void The_assistant_fills_a_mid_major_class(ulong seed)
    {
        var (c, rng) = World(seed);
        var sim = new GameSimulator();
        // The 50th strongest program, as CriticBot's college career picks it.
        var t = c.Programs.OrderByDescending(p => QuickSim.Strength(p)).ElementAt(49);
        c.ManagedProgramId = t.Id;
        int cls = c.RecruitingClass(new DateOnly(2026, 10, 1));
        c.HandBackCommitments(t, new DateOnly(2026, 10, 1));
        var notes = new List<string>();
        for (var day = CollegeWorld.MondayOf(new DateOnly(2026, 10, 5)); day <= CollegeWorld.SigningDay(cls).AddDays(1); day = day.AddDays(7))
        {
            c.PlayThrough(day, sim, rng);
            if (c.NewTrailWeek(t, day, delegated: true) is { } did) notes.Add(did);
        }
        int signed = c.Commits(t, cls), left = c.OpenScholarships(t, cls);
        _out.WriteLine($"{t.FullName}: {signed} signed, {left} places left; " + string.Join(" | ", notes.Where(n => n.Contains("withdrew")).Take(3)));
        Assert.True(signed >= 2, $"{t.FullName} signed {signed}");
        Assert.True(left <= 1, $"{t.FullName} left {left} places open");
    }

    [Fact]
    public void A_lost_cause_offer_is_withdrawn_and_a_likely_one_is_kept()
    {
        var (c, _) = World(31);
        var t = c.Programs.OrderByDescending(p => QuickSim.Strength(p)).ElementAt(150); // a mid-major
        c.ManagedProgramId = t.Id;
        var day = new DateOnly(2026, 10, 5);
        int cls = c.RecruitingClass(day);
        var board = c.BoardFor(cls);
        var pool = c.ClassOf(cls).Where(p => c.Recruits[p.Id].CommittedTo == null).OrderBy(p => c.Recruits[p.Id].Rank).ToList();
        var star = pool.First();                       // the No. 1 prospect left: the blue bloods' man
        var likely = pool.Last(); // deep in the class: no school with a place is chasing him
        foreach (var p in new[] { star, likely })
        {
            var r = CollegeWorld.Courting(c.Recruits[p.Id], t);
            r.Offered = true; r.Interest = 60;
        }
        var said = c.WithdrawLostCauses(t, cls, board);
        Assert.False(c.Recruits[star.Id].Offered);
        Assert.True(c.Recruits[likely.Id].Offered);
        Assert.Contains(said, x => x.StartsWith(star.Name + ", leaning "));
        Assert.Equal(60, c.Recruits[star.Id].Interest); // the interest stays
        // Withdrawn means not a target again (the same effort assumption as Fits).
        Assert.DoesNotContain(c.Fits(t, cls, board, 999, CollegeWorld.SpringEdge), x => x.p == star);
    }

    [Fact]
    public void The_final_poll_puts_the_champion_first()
    {
        var (c, rng) = World(25);
        c.PlayThrough(new DateOnly(2027, 4, 30), new GameSimulator(), rng);
        var s = c.Season;
        Assert.True(c.FinalPoll);
        var order = c.PollOrder();
        Assert.Equal(s.ChampionId, order[0]);
        Assert.True(c.PollPosition(s.RunnerUpId!.Value) <= 4, $"runner-up No. {c.PollPosition(s.RunnerUpId!.Value)}");
        // Every Final Four team is in the final Top 25.
        foreach (var id in c.Programs.Where(p => c.MadnessReached(p.Id) >= 5).Select(p => p.Id))
            Assert.True(c.PollPosition(id) <= CollegeWorld.PollSize, $"{c.Program(id)!.City} No. {c.PollPosition(id)}");
    }

    [Fact]
    public void The_live_read_is_about_this_game()
    {
        var (c, _) = World(7);
        var home = c.Programs[0]; var away = c.Programs[1];
        var live = new GameSimulator().StartLive(home, away, Rules.College, 99);
        var (h0, t0, w0) = Assistant.LiveRead(live.Result, home, live.PeriodText, live.ClockText, live.Complete);
        Assert.Contains("no score yet", h0);
        Assert.Empty(w0);
        for (int i = 0; i < 70; i++) live.Step();
        int us = live.HomeScore, them = live.AwayScore;
        var (h, title, why) = Assistant.LiveRead(live.Result, home, live.PeriodText, live.ClockText, live.Complete);
        _out.WriteLine($"{h} / {title}: {string.Join(" ", why)}");
        Assert.StartsWith($"Live, {live.PeriodText} ", h);
        Assert.Contains(us == them ? $"tied {us}-{us}" : us > them ? $"we lead {us}-{them}" : $"we trail {us}-{them}", h);
        Assert.Equal($"So far against {away.Abbreviation}", title);
        Assert.DoesNotContain("Last game", h + title);
        live.Finish();
        var (hf, tf, _) = Assistant.LiveRead(live.Result, home, live.PeriodText, live.ClockText, live.Complete);
        Assert.StartsWith("Final: we ", hf);
        Assert.StartsWith("This game", tf);
    }

    [Fact]
    public void Neutral_games_read_vs_neutral()
    {
        var (c, _) = World(7);
        var opp = c.Programs[3];
        var neutral = new MyGame(new DateOnly(2026, 11, 26), opp, false, "Island Classic", "Regular season", null, null, null) { Neutral = true };
        var away = neutral with { Neutral = false };
        Assert.Equal($"vs {opp.City} (neutral)", neutral.Versus(opp.City));
        Assert.Equal($"at {opp.City}", away.Versus(opp.City));
        Assert.Equal($"vs {opp.City}", (away with { Home = true }).Versus(opp.City));
    }

    [Fact]
    public void The_recruiting_tab_says_which_offers_lean_elsewhere()
    {
        var (c, _) = World(31);
        var t = c.Programs.OrderByDescending(p => QuickSim.Strength(p)).ElementAt(150);
        c.ManagedProgramId = t.Id;
        int cls = c.RecruitingClass(new DateOnly(2026, 10, 5));
        var board = c.BoardFor(cls);
        var star = c.ClassOf(cls).Where(p => c.Recruits[p.Id].CommittedTo == null).OrderBy(p => c.Recruits[p.Id].Rank).First();
        Assert.Equal("", CollegeViewModel.OffersLeaningNote(c, t, cls, board));
        var r = CollegeWorld.Courting(c.Recruits[star.Id], t); r.Offered = true;
        var note = CollegeViewModel.OffersLeaningNote(c, t, cls, board);
        _out.WriteLine(note);
        Assert.StartsWith("Your open offer is with a prospect leaning elsewhere: ", note);
        Assert.Contains($"No. {r.Rank} {star.Name} (", note);
    }
}

/// <summary>Run 136 on screen: the Board's national rank is the poll's, the April Top 25 is the final poll, and the schedule names neutral sites.</summary>
[Collection(SharedUiState.Name)]   // it swaps AppSettings, app-wide state
public class Run136CollegePolishUiTests
{
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void The_board_the_poll_and_the_schedule_agree()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run136-{Guid.NewGuid():N}.hwsave");
        var oldSettings = Hardwood.Desktop.AppSettings.Current; var oldPath = Hardwood.Desktop.AppSettings.FilePath;
        Hardwood.Desktop.AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        Hardwood.Desktop.AppSettings.Use(new Hardwood.Desktop.AppSettings { AutoPortraits = false });
        Hardwood.Desktop.Views.MainWindow? window = null;
        try
        {
            var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026, 2026, GameSession.DefaultFullSim);
            int id = world.College!.Programs.First(p => p.FullName.StartsWith("Miami (FL)")).Id;
            var s = GameSession.NewGame(2026, id, path);
            var cw = s.World.College!;
            var vm = new MainViewModel();
            window = new Hardwood.Desktop.Views.MainWindow { DataContext = vm, Width = 1480, Height = 900 };
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

            // H153: Christmas Eve, the Board's rank is the poll's.
            cw.PlayThrough(new DateOnly(2026, 12, 24), new GameSimulator(), new Rng(136));
            cw.InvalidatePoll();
            vm.Navigate("Board"); Settle();
            var board = (BoardViewModel)vm.CurrentPage!;
            board.Refresh(); Settle();
            var chip = board.FinanceChips.Single(c => c.Label == "National rank");
            int pos = cw.PollPosition(id);
            Assert.Equal(pos <= CollegeWorld.PollSize ? $"No. {pos}" : "Unranked", chip.Value);
            if (pos > CollegeWorld.PollSize) Assert.Contains($"No. {pos} of", chip.Sub);
            Shot("run136-college-board-rank.png");
            vm.Navigate("Standings"); Settle();
            var st = (StandingsViewModel)vm.CurrentPage!;
            Assert.Equal("Top 25 poll (rating and record)", st.RightTitle);

            // Neutral-site games on the schedule (the November invitational and any December showcase).
            // (Miami's November invitational or December showcase, if it was invited; every event game is on a neutral court.)
            Assert.All(s.MyGames().Where(g => g.Competition != s.HumanLeagueName), g => Assert.True(g.Neutral, g.Competition));
            bool invited = s.MyGames().Any(g => g.Neutral);
            vm.Navigate("Schedule & Results"); Settle();
            var sched = (ScheduleViewModel)vm.CurrentPage!;
            if (invited) Assert.Contains(sched.Games, g => g.Game.Neutral && g.TileWhen.EndsWith("· neutral") && g.Opponent.EndsWith("(neutral)"));
            Assert.DoesNotContain(sched.Games, g => g.Game.Neutral && (g.TileOpponent.StartsWith("at ") || g.TileWhen.EndsWith("away") || g.TileWhen.EndsWith("home")));
            Shot("run136-college-schedule-neutral.png");


            // H154: after the title game the Top 25 is the final poll, the champion first.
            cw.PlayThrough(new DateOnly(2027, 4, 30), new GameSimulator(), new Rng(137));
            vm.Navigate("Standings"); Settle();
            st = (StandingsViewModel)vm.CurrentPage!;
            st.Refresh(); Settle();
            Assert.Equal("Final Top 25 (after March Madness)", st.RightTitle);
            vm.Navigate("College"); Settle();
            var col = (CollegeViewModel)vm.CurrentPage!;
            col.Refresh(); Settle();
            Assert.Equal(cw.Season.ChampionId, col.Top25[0].Team.Id);
            Assert.Contains("Final poll", col.PollNote);
            Shot("run136-college-final-poll.png");
        }
        finally
        {
            window?.Close();
            Hardwood.Desktop.AppSettings.FilePath = oldPath; Hardwood.Desktop.AppSettings.Use(oldSettings);
            try { File.Delete(path); } catch { }
        }
    }
}
