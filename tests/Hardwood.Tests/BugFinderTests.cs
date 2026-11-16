using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hardwood.Core;
using Hardwood.Core.Engine;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Review #14's bug finder (Danny, #551). Bot careers of every kind open every page and tab, press every button,
/// play a live game through the studio show, take every offseason step, lose the job and find another, and check
/// the game's invariants every day and a save and reload along the way. It writes a ranked report rather than
/// failing on the first problem.
/// <para>Off by default (it takes a while): set HW_BUGFINDER to a folder for the report and screenshots;
/// HW_BUGFINDER_CAREERS picks careers (comma-separated: nba-contender, nba-rebuild, gleague, bottom, college, fired);
/// HW_BUGFINDER_SEASONS sets how many seasons each plays (default 1);
/// HW_BUGFINDER_TOUR saves a screenshot of every page and tab at some sweeps ("1": the first day and day 70).</para>
/// </summary>
[Collection(SharedUiState.Name)]
public class BugFinderTests
{
    private readonly ITestOutputHelper _out;
    public BugFinderTests(ITestOutputHelper output) => _out = output;

    [AvaloniaFact]
    public void Bug_finder_report()
    {
        var dir = Environment.GetEnvironmentVariable("HW_BUGFINDER");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        var careers = (Environment.GetEnvironmentVariable("HW_BUGFINDER_CAREERS") ?? "nba-contender,nba-rebuild,gleague,bottom,college,fired")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        int seasons = int.TryParse(Environment.GetEnvironmentVariable("HW_BUGFINDER_SEASONS"), out var n) ? n : 1;

        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { FaceFolder = oldSettings.FaceFolder, LogoFolder = oldSettings.LogoFolder, AutoPortraits = false });
        var sink = new BugFinder.BindingSink();
        var oldSink = Avalonia.Logging.Logger.Sink;
        Avalonia.Logging.Logger.Sink = sink;
        var vm = new MainViewModel();
        var window = new MainWindow { DataContext = vm, Width = 1480, Height = 900 };
        window.Show();
        var bf = new BugFinder(vm, window, Path.Combine(dir, "shots"));
        var log = new List<string>();
        void L(string x) { log.Add($"{DateTime.Now:HH:mm:ss} {x}"); _out.WriteLine(x); File.AppendAllText(Path.Combine(dir, "progress.txt"), x + "\n"); }
        bf.Log = L;
        // HW_BUGFINDER_TOUR: "1" for the first day and day 70, or a comma-separated list of sweep labels' starts ("first day,offseason").
        var tour = Environment.GetEnvironmentVariable("HW_BUGFINDER_TOUR");
        if (!string.IsNullOrWhiteSpace(tour))
            bf.TourAt = tour.Trim() == "1" ? new[] { "first day", "day 70" } : tour.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        try
        {
            foreach (var career in careers)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                bf.Career = career;
                int before = bf.Findings.Count;
                try { Play(career, seasons, vm, window, bf, L); }
                catch (Exception ex) { bf.Add("crash", "(career)", $"{ex.GetType().Name}: {ex.Message} [{ex.StackTrace?.Split('\n').FirstOrDefault(l => l.Contains("Hardwood."))?.Trim()}]", true); }
                L($"{career}: done in {sw.Elapsed.TotalMinutes:F1} min, {bf.Findings.Count - before} new findings; pages {bf.Pages}, tabs {bf.Tabs}, buttons {bf.Buttons}, days {bf.Days}, reloads {bf.Reloads}{(bf.TourShots > 0 ? $", tour shots {bf.TourShots}" : "")}");
                WriteReport(dir, bf, sink, log);
            }
        }
        finally
        {
            Avalonia.Logging.Logger.Sink = oldSink;
            window.Close();
            AppSettings.FilePath = oldPath; AppSettings.Use(oldSettings);
            WriteReport(dir, bf, sink, log);
        }
    }

    private static void Play(string career, int seasons, MainViewModel vm, MainWindow window, BugFinder bf, Action<string> L)
    {
        ulong seed = career switch { "nba-rebuild" => 77, "gleague" => 31, "bottom" => 9, "college" => 2026, "fired" => 55, _ => 2026 };
        var (probe, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(seed);
        int teamId = career switch
        {
            "nba-contender" => probe.Teams.OrderByDescending(t => QuickSim.Strength(t)).First().Id,
            "nba-rebuild" => probe.Teams.OrderBy(t => QuickSim.Strength(t)).First().Id,
            "fired" => probe.Teams.OrderBy(t => QuickSim.Strength(t)).ElementAt(10).Id,
            "gleague" => world[UsaPyramid.Tier2].Clubs.OrderBy(t => QuickSim.Strength(t)).ElementAt(15).Id,
            "bottom" => world[UsaPyramid.Tier5].Clubs.OrderBy(t => QuickSim.Strength(t)).First().Id,
            "college" => world.College!.Programs.OrderByDescending(t => QuickSim.Strength(t)).ElementAt(49).Id,
            _ => throw new ArgumentException(career),
        };
        var path = Path.Combine(Path.GetTempPath(), $"hw-bug-{career}-{Guid.NewGuid():N}.hwsave");
        var session = GameSession.NewGame(seed, teamId, path);
        vm.AttachSession(session);
        bf.Settle();
        L($"{career}: {session.HumanTeam.FullName} ({session.HumanLeagueName}), seed {seed}, {session.Today:MMM d yyyy}");
        bf.SweepAll("first day");
        bf.Invariants();

        int startYear = vm.Session!.League.Season;
        bool livePlayed = false, firedDone = career != "fired";
        var sweepAt = new HashSet<int> { 70 };
        int days = 0, inSeasonDays = 0, guard = 0;
        var club = vm.Session!.HumanTeam;
        while (vm.Session!.League.Season < startYear + seasons && guard++ < 1400)
        {
            var s = vm.Session!;
            // A change of club (a job taken) is worth a line in the log.
            if (s.HumanTeam.Id != club.Id) { L($"{career}: the club is now {s.HumanTeam.FullName} ({s.HumanLeagueName}, was {club.FullName}) on {s.Today:MMM d yyyy}"); }
            club = s.HumanTeam;
            if (s.Phase == SessionPhase.Offseason) { Offseason(vm, bf, L); continue; }
            // One live game a career, the way the player sees it: the studio show, the live game, the box score.
            if (!livePlayed && s.Phase == SessionPhase.RegularSeason && s.HumanPlaysToday && s.Employed)
            {
                livePlayed = true;
                LiveGame(vm, window, bf, L);
                bf.Invariants();
                continue;
            }
            var phase = s.Phase;
            s.AdvanceDay();
            days++;
            if (vm.Session!.Phase is SessionPhase.RegularSeason or SessionPhase.Postseason || s.InCollege) inSeasonDays++;
            if (days % 3 == 0 || vm.Session.Phase != phase) vm.AfterTick();
            bf.Invariants();
            if (vm.Session.Phase != phase) { L($"{career}: {phase} → {vm.Session.Phase} on {vm.Session.Today:MMM d yyyy} ({vm.Session.HumanTeam.FullName}, {vm.Session.HumanLeagueName})"); vm.AfterTick(); bf.SweepAll($"start of {vm.Session.Phase}"); bf.SaveAndReload($"start of {vm.Session.Phase}"); }
            if (sweepAt.Contains(inSeasonDays) && vm.Session.Phase != SessionPhase.Offseason)
            {
                sweepAt.Remove(inSeasonDays);
                vm.AfterTick();
                bf.SweepAll($"day {inSeasonDays} of the season");
                bf.SaveAndReload($"day {inSeasonDays}");
            }
            else if (days % 45 == 0) bf.SaveAndReload($"day {days}");
            // The fired career: sacked a third of the way in, then finds work through the Board page.
            if (!firedDone && inSeasonDays == 50)
            {
                firedDone = true;
                Fired(vm, bf, L);
            }
        }
        vm.AfterTick();
        bf.SweepAll("the end of the run");
        bf.SaveAndReload("the end of the run");
    }

    private static void Pump(MainViewModel vm, int ms = 30000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        do { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); } while (vm.IsBusy && sw.ElapsedMilliseconds < ms);
        Dispatcher.UIThread.RunJobs();
    }

    private static Avalonia.Controls.Button? Find(MainWindow w, string startsWith) =>
        w.GetVisualDescendants().OfType<Avalonia.Controls.Button>().FirstOrDefault(b => b.IsEffectivelyVisible && b.IsEffectivelyEnabled && BugFinder.Label(b).StartsWith(startsWith, StringComparison.OrdinalIgnoreCase));

    /// <summary>Selects the live page's tab with this header; returns its TabControl (null if there's no such tab).</summary>
    private static Avalonia.Controls.TabControl? OpenTab(MainWindow w, string header)
    {
        foreach (var tc in w.GetVisualDescendants().OfType<Avalonia.Controls.TabControl>().Where(t => t.IsEffectivelyVisible))
        {
            var items = tc.Items.OfType<Avalonia.Controls.TabItem>().ToList();
            int i = items.FindIndex(t => t.Header as string == header);
            if (i < 0) continue;
            tc.SelectedIndex = i;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            return tc;
        }
        return null;
    }

    private static void Click(Avalonia.Controls.Button b) =>
        typeof(Avalonia.Controls.Button).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(b, null);

    private static void LiveGame(MainViewModel vm, MainWindow window, BugFinder bf, Action<string> L)
    {
        vm.LiveGames = true;
        vm.ContinueCommand.Execute(null);
        Pump(vm);
        bf.Settle();
        if (vm.CurrentPage is StudioShowViewModel)
        {
            var labels = window.GetVisualDescendants().OfType<Avalonia.Controls.Button>().Where(b => b.IsEffectivelyVisible).Select(BugFinder.Label).ToList();
            L($"studio show buttons: {string.Join(", ", labels.Distinct())}");
            var go = Find(window, "Tip off") ?? Find(window, "Sim the game");
            if (go == null) { bf.Add("dead end", "Studio show", "no button to get from the studio show to the game", true); return; }
            Click(go); bf.Settle();
        }
        if (vm.CurrentPage is not LiveGameViewModel live)
        {
            bf.Add("dead end", "Live game", $"Tip off on the studio show opened {vm.CurrentPage?.GetType().Name}, not the live game", true);
            vm.InLiveGame = false; vm.Navigate("Home");
            return;
        }
        // The live page's controls, each pressed once mid-game.
        live.Refresh();
        foreach (var label in new[] { "Game flow", "On the floor", "Double their best scorer", "Stop at breaks", "Apply lineup" })
        {
            // Bug list H4: the double-team box lives on the live game's Tactics tab, so open that tab first and go
            // back to the first tab after.
            var tabs = label == "Double their best scorer" ? OpenTab(window, "Tactics") : null;
            var b = Find(window, label);
            if (tabs != null) { if (b != null) { try { Click(b); bf.Settle(); } catch (Exception ex) { bf.Add("crash", "Live game", $"\"{label}\": {(ex.InnerException ?? ex).Message}", true); } }
                else bf.Add("dead end", "Live game", $"no \"{label}\" box on the live game's Tactics tab");
                tabs.SelectedIndex = 0; bf.Settle(); continue; }
            if (b == null) { bf.Add("dead end", "Live game", $"no \"{label}\" button mid-game"); continue; }
            try { Click(b); bf.Settle(); } catch (Exception ex) { bf.Add("crash", "Live game", $"\"{label}\": {(ex.InnerException ?? ex).Message}", true); }
        }
        var sim = Find(window, "Sim to end");
        if (sim == null) { bf.Add("dead end", "Live game", "no Sim to end button", true); live.SimToEnd(); }
        else Click(sim);
        bf.Settle();
        if (!live.Complete) bf.Add("dead end", "Live game", "Sim to end didn't finish the game", true);
        var cont = Find(window, "Continue");
        if (cont == null) { bf.Add("dead end", "Live game", "a finished live game has no Continue", true); live.Continue(); }
        else Click(cont);
        Pump(vm);
        bf.Settle();
        if (vm.CurrentPage is not BoxScoreViewModel) bf.Add("dead end", "Live game", $"Continue after the game opened {vm.CurrentPage?.GetType().Name}, not the box score", true);
        else
        {
            var back = Find(window, "Back");
            if (back == null) bf.Add("dead end", "Box score", "the box score after a live game has no Back button", true);
        }
        if (vm.InLiveGame) { bf.Add("dead end", "Live game", "still in the live game after Continue (the sidebar stays locked)", true); vm.InLiveGame = false; }
        vm.Navigate("Home");
        L($"live game played: {vm.Session!.MyRecord.Wins}-{vm.Session.MyRecord.Losses}");
    }

    private static void Offseason(MainViewModel vm, BugFinder bf, Action<string> L)
    {
        var s = vm.Session!;
        int guard = 0;
        while (vm.Session!.Phase == SessionPhase.Offseason && guard++ < 12)
        {
            s = vm.Session!;
            var phase = s.Offseason?.Phase;
            L($"offseason: {phase} on {s.Today:MMM d yyyy} (roster {s.HumanTeam.Roster.Count})");
            vm.AfterTick();
            bf.SweepPage("Offseason", $"offseason, {phase}");
            bf.SweepPage("Home", $"offseason, {phase}", press: false);
            bf.SweepPage("Transactions", $"offseason, {phase}", press: false);
            bf.Invariants();
            bf.SaveAndReload($"offseason, {phase}");
            s = vm.Session!;
            // The top bar's Continue, as the player presses it; it must move on or say why not.
            int inbox = s.Inbox.Count;
            var off = vm.Page("Offseason") as OffseasonViewModel;
            string status = off?.Status ?? "";
            vm.ContinueCommand.Execute(null);
            Pump(vm);
            bf.Settle();
            var after = vm.Session!;
            if (after.Phase == SessionPhase.Offseason && after.Offseason?.Phase == phase)
            {
                bool told = after.Inbox.Count > inbox || (off?.Status ?? "") != status;
                if (!told) bf.Add("dead end", "Offseason", $"Continue in {phase} does nothing and says nothing (roster {after.HumanTeam.Roster.Count})", true);
                // Unblock as a player would: fill the roster from free agency, then force it.
                if (after.Employed && after.HumanTeam.Roster.Count < after.League.Finance.RosterMin)
                    foreach (var fa in after.League.FreeAgents.OrderBy(p => p.CurrentAbility).Take(after.League.Finance.RosterMin - after.HumanTeam.Roster.Count).ToList())
                        after.Offseason?.SignFreeAgent(after.HumanTeam, fa, 1, after.League.Finance.MinSalary);
                after.AdvanceOffseason();
                vm.AfterTick();
            }
        }
        if (vm.Session!.Phase == SessionPhase.Offseason) bf.Add("dead end", "Offseason", "the offseason didn't end after 12 presses of Continue", true);
        else { vm.AfterTick(); bf.SweepAll("the new season's first day"); }
    }

    private static void Fired(MainViewModel vm, BugFinder bf, Action<string> L)
    {
        var s = vm.Session!;
        s.Fire();
        vm.AfterTick();
        L($"fired on {s.Today:MMM d yyyy}");
        bf.SweepAll("out of work");
        // Look for work the way the player does: the Board page lists openings with an Apply button.
        int days = 0;
        while (!vm.Session!.Employed && days++ < 120)
        {
            s = vm.Session!;
            if (s.Phase == SessionPhase.Offseason) { s.AdvanceOffseason(); continue; }
            foreach (var v in s.Job.Vacancies.Where(x => x.Status == "open").Take(3).ToList()) s.Apply(v.TeamId);
            foreach (var v in s.Job.Vacancies.Where(x => x.Status == "offered").ToList())
            {
                var err = s.AcceptOffer(v.TeamId);
                if (err != null) bf.Add("state", "Board", $"accepting an offer: {err}");
                break;
            }
            if (s.Employed) break;
            s.AdvanceDay();
            bf.Invariants();
        }
        vm.AfterTick();
        if (!vm.Session.Employed) bf.Add("dead end", "Board", $"out of work for {days} days after applying to every opening (reputation {vm.Session.Coach.Reputation:0})", true);
        else { L($"re-hired by {vm.Session.HumanTeam.FullName} on {vm.Session.Today:MMM d yyyy} after {days} days"); bf.SweepAll("first day of the new job"); bf.SaveAndReload("re-hired"); }
    }

    private static readonly string[] Rank = { "crash", "save", "state", "mismatch", "dead end", "wrong text", "no feedback", "empty" };

    private static void WriteReport(string dir, BugFinder bf, BugFinder.BindingSink sink, List<string> log)
    {
        var lines = new List<string> { "# Bug finder report", "", $"Pages {bf.Pages}, tabs {bf.Tabs}, buttons pressed {bf.Buttons}, game days checked {bf.Days}, save-and-reloads {bf.Reloads}.", "" };
        foreach (var kind in Rank.Concat(bf.Findings.Select(f => f.Kind)).Distinct())
        {
            var group = bf.Findings.Where(f => f.Kind == kind).ToList();
            if (group.Count == 0) continue;
            lines.Add($"## {kind} ({group.Count})");
            foreach (var f in group) lines.Add($"- [{f.Career} · {f.Date}] **{f.Page}**: {f.What}{(f.Shot != null ? $" (shot {f.Shot})" : "")}");
            lines.Add("");
        }
        List<string> errs; lock (sink.Errors) errs = sink.Errors.ToList();
        var grouped = errs.GroupBy(e => System.Text.RegularExpressions.Regex.Replace(e, @"\d+", "#")).OrderByDescending(g => g.Count()).ToList();
        lines.Add($"## binding errors ({grouped.Count} distinct, {errs.Count} in all)");
        foreach (var g in grouped.Take(60)) lines.Add($"- ×{g.Count()} {g.First()}");
        lines.Add("");
        lines.Add("## progress");
        lines.AddRange(log.Select(l => "- " + l));
        File.WriteAllLines(Path.Combine(dir, "bug-report.md"), lines);
    }
}

/// <summary>Fixes for what review #14's bug finder found in run 53 (each with its own test).</summary>
public class BugFinderFixTests
{
    private static GameSession NewCareer(out string path)
    {
        var (probe, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026);
        path = Path.Combine(Path.GetTempPath(), $"hw-bugfix-{Guid.NewGuid():N}.hwsave");
        return GameSession.NewGame(2026, probe.Teams.First(t => t.Abbreviation == "BOS").Id, path);
    }

    [Fact]
    public void Delegated_tactics_survive_a_save_and_reload()
    {
        var s = NewCareer(out var path);
        try
        {
            for (int i = 0; i < 10; i++) s.AdvanceDay();
            // Staff & Training's "delegate tactics" sets Auto; a load used to run the new-career setup and switch it off.
            s.HumanTeam.Tactic.Auto = true;
            s.HumanTeam.Tactic.System = Hardwood.Core.Model.OffensiveSystem.Motion;
            s.SaveNow();
            var loaded = GameSession.Load(path);
            Assert.True(loaded.HumanTeam.Tactic.Auto);
            Assert.Equal(Hardwood.Core.Model.OffensiveSystem.Motion, loaded.HumanTeam.Tactic.System);
            // A hand-run tactic loads as it was saved too.
            s.HumanTeam.Tactic.Auto = false;
            s.HumanTeam.Tactic.System = Hardwood.Core.Model.OffensiveSystem.Isolation;
            s.SaveNow();
            loaded = GameSession.Load(path);
            Assert.False(loaded.HumanTeam.Tactic.Auto);
            Assert.Equal(Hardwood.Core.Model.OffensiveSystem.Isolation, loaded.HumanTeam.Tactic.System);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Talk_cooldown_reads_in_plain_words()
    {
        var p = new Hardwood.Core.Model.Player { FirstName = "A", LastName = "B", LastTalk = new DateOnly(2026, 11, 1) };
        Assert.Equal("You spoke to him today; give it a week.", Hardwood.Core.People.Talks.Unavailable(p, new DateOnly(2026, 11, 1)));
        Assert.Equal("You spoke to him yesterday; give it a week.", Hardwood.Core.People.Talks.Unavailable(p, new DateOnly(2026, 11, 2)));
        Assert.Equal("You spoke to him 3 days ago; give it a week.", Hardwood.Core.People.Talks.Unavailable(p, new DateOnly(2026, 11, 4)));
        Assert.Null(Hardwood.Core.People.Talks.Unavailable(p, new DateOnly(2026, 11, 8)));
    }

    [Fact]
    public void No_young_player_minutes_tip_before_the_games_start()
    {
        var s = NewCareer(out var path);
        try
        {
            // In preseason nobody has minutes yet: "could become a star but plays 0 minutes" was wrong for every starter.
            var notes = Hardwood.Core.Guide.Assistant.Strategy(s, Hardwood.Core.Guide.GuideStage.Strategy);
            Assert.DoesNotContain(notes, n => n.Contains("but plays 0 minutes"));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Sweep_leaves_roster_cuts_alone()
    {
        // Bug-hunt Nov 16: the sweep pressed Waive on Wembanyama on the nba-contender career's first day.
        Assert.True(BugFinder.IsCut("Waive"));
        Assert.True(BugFinder.IsCut("Release"));
        Assert.True(BugFinder.IsCut("Release selected"));
        Assert.False(BugFinder.IsCut("Make offer"));
        Assert.False(BugFinder.IsCut("Draft selected"));
    }
}
