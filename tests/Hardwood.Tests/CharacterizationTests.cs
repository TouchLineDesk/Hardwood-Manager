using Hardwood.Core;
using Hardwood.Core.Engine;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Xunit;
using Xunit.Abstractions;
using static Hardwood.Tests.Characterization;

namespace Hardwood.Tests;

/// <summary>
/// Phase 1, part 1 of Danny's roadmap (#596, run 57): characterization tests. They pin what the game does today
/// (a seeded game's box score and play-by-play, the bench's game and season fingerprints from `claude/baseline.md`,
/// a managed career's days) and check the save: a career saved and loaded is the same career, and two loads of
/// one save play the same future. A refactor or a speed-up must leave every one of these unchanged; a change that
/// is meant to move results updates the pins in the same PR and says why.
/// </summary>
public class CharacterizationTests
{
    private readonly ITestOutputHelper _out;
    public CharacterizationTests(ITestOutputHelper output) => _out = output;

    private static readonly DateOnly GameDay = new(2026, 11, 3);

    private static GameResult OneGame(bool keepLog, ulong worldSeed = 2026, ulong gameSeed = 12345)
    {
        var (league, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(worldSeed, 2026);
        var t = league.Teams.OrderBy(x => x.Id).ToList();
        return new GameSimulator().Simulate(t[0], t[1], league.Rules, gameSeed, keepLog, GameDay);
    }

    private void Pin(string what, string expected, IReadOnlyList<string> lines)
    {
        string actual = Hash(lines);
        _out.WriteLine($"{what}: {actual} ({lines.Count} lines)");
        if (actual != expected)
            _out.WriteLine(string.Join("\n", lines.Take(40)));
        Assert.True(actual == expected, $"{what} moved: expected {expected}, got {actual}. First lines:\n{string.Join("\n", lines.Take(6))}");
    }

    // ---------------------------------------------------------------------------------------------------------------
    // One game

    [Fact]
    public void A_seeded_game_gives_the_pinned_box_score()
    {
        var r = OneGame(keepLog: false);
        Pin("box score, seed 12345", "57B9CC42DA383737", Box(r));
    }

    [Fact]
    public void The_play_log_changes_nothing_in_the_result()
    {
        var off = Box(OneGame(keepLog: false));
        var on = Box(OneGame(keepLog: true));
        Assert.True(FirstDifference(off, on) == null, FirstDifference(off, on));
    }

    [Fact]
    public void A_seeded_game_gives_the_pinned_play_by_play()
    {
        var r = OneGame(keepLog: true);
        Assert.NotEmpty(r.PlayLog);
        Pin("play-by-play, seed 12345", "68DDC00764BE741A", r.PlayLog);
        // The game flow (the chart's points) is part of what the live screen shows.
        Pin("game flow, seed 12345", "FA2877F94D5E4596", r.Flow.Select(f => $"{f.Elapsed:F1} {f.Period} {f.Home}-{f.Away} {f.Scorer?.Id}").ToList());
    }

    [Fact]
    public void The_same_game_on_a_fresh_world_is_the_same_game()
    {
        var a = OneGame(keepLog: true); var b = OneGame(keepLog: true);
        Assert.True(FirstDifference(Box(a), Box(b)) == null, FirstDifference(Box(a), Box(b)));
        Assert.Equal(a.PlayLog, b.PlayLog);
    }

    [Fact]
    public void A_live_game_stepped_to_the_end_matches_the_quick_game()
    {
        // The live screen steps a game possession by possession (`StartLive`); with nobody changing anything it plays
        // the same game as `Simulate` with the play log on (computer clubs, whose tactics are on auto).
        var quick = OneGame(keepLog: true);
        var (league, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026, 2026);
        var t = league.Teams.OrderBy(x => x.Id).ToList();
        var live = new GameSimulator().StartLive(t[0], t[1], league.Rules, 12345, GameDay);
        int steps = 0;
        while (live.Step()) steps++;
        Assert.True(steps > 150, $"a game is about 200 possessions ({steps})");
        Assert.True(FirstDifference(Box(quick), Box(live.Result)) == null, FirstDifference(Box(quick), Box(live.Result)));
        Assert.Equal(quick.PlayLog, live.Result.PlayLog);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // The baseline's fingerprints (claude/baseline.md, run 56): `hardwood bench` must keep printing these.

    [Theory]
    // Runs 112-113 (H105): stars take more shots (role ties go to the shooting role; a star bonus above CA 155; sharpness 1.65 → 1.4),
    // so every engine pin moved (was 22839796 / 22262109; seasons 296382 / 296763; game 74122B34…, log DE8EF515…, career FA6B5666…).
    // Run 118 (H110): the SuperSonics and Aces are stocked by an expansion draft of real players (every real club loses one
    // bench player), so the league's teams changed (was 22605263 / 22482269; seasons 294868 / 295503; game 5AB24971…,
    // log 24A8AB7F…, career 23333733…).
    [InlineData(2026UL, 22742473L)]
    [InlineData(77UL, 22553468L)]
    public void The_bench_games_match_the_baseline(ulong seed, long expected)
    {
        // The same loop as `Bench.Run`'s game table: a warm-up game, then 200 pairs rotating through the league.
        var (league, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(seed, 2026);
        var sim = new GameSimulator();
        var t = league.Teams;
        sim.Simulate(t[0], t[1], league.Rules, seed ^ 0xBEEF, keepLog: false);
        long points = 0;
        for (int i = 0; i < 200; i++)
        {
            var home = t[(i * 7 + 2) % t.Count]; var away = t[(i * 11 + 5) % t.Count];
            if (home == away) away = t[(i * 11 + 6) % t.Count];
            var res = sim.Simulate(home, away, league.Rules, seed + (ulong)i, keepLog: false);
            points += res.Home.Points * 1000 + res.Away.Points;
        }
        Assert.Equal(expected, points);
    }

    [Theory]
    // Run 101 (H78): the schedule's dates changed (was 296945 / 296650, OKC both); scoring moved under 0.2%, and seed 77's
    // title now goes to Detroit (a different playoff draw, not a strength change).
    // run 113 (H105): stars take more shots (scoring -0.3%); the title goes to Philadelphia (was 295611 / 295512).
    // Run 116: the AI doubles and shadows the go-to man the engine feeds (not simply the best-rated player): points -0.25% / -0.003%; seed 2026's title goes to Detroit (a different playoff draw).
    // Run 118 (H110): the expansion draft (see above): points +0.15% / -0.4%; the titles go to OKC / Golden State (different playoff draws).
    [InlineData(2026UL, "OKC", 295295L)]
    [InlineData(77UL, "GSW", 294326L)]
    public void The_bench_season_matches_the_baseline(ulong seed, string champion, long expected)
    {
        var (league, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(seed, 2026);
        var rng = new Rng(seed ^ 0xABCDEF);
        var schedule = ScheduleBuilder.Build(league, new DateOnly(2026, 10, 21), rng);
        var outcome = new SeasonSimulator(rng).Run(league, schedule);
        Assert.Equal(expected, outcome.RegularSeason.Sum(g => (long)g.Home.Points + g.Away.Points));
        Assert.Equal(champion, outcome.Champion!.Abbreviation);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // A managed career

    private static int BenchClub(ulong seed)
    {
        var (probe, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(seed, 2026);
        return probe.Teams.OrderBy(t => QuickSim.Strength(t)).ElementAt(14).Id; // the bench's club: the 15th strongest
    }

    private static void Days(GameSession s, int n) { for (int i = 0; i < n; i++) s.AdvanceDay(); }

    private static string TempSave() => Path.Combine(Path.GetTempPath(), $"hw-char-{Guid.NewGuid():N}.hwsave");

    private static string CopyOf(string path)
    {
        var copy = TempSave();
        File.Copy(path, copy);
        return copy;
    }

    private static void Clean(params string[] paths)
    {
        foreach (var p in paths)
            foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(p) + "*"))
                try { File.Delete(f); } catch (IOException) { }
    }

    [Fact]
    public void A_career_without_saves_plays_the_pinned_first_month()
    {
        var s = GameSession.NewGame(2026, BenchClub(2026), savePath: null);
        Days(s, 30);
        Assert.True(s.Results.Count > 100, $"a month of NBA games ({s.Results.Count})");
        // Run 95 (C5 part 2, #848): re-pinned (was CF89DF441AA947D3 since run 57). The college world plays on the session's
        // random stream, and scheduled rivals and arena home courts change college scores, so a few quick-sim tie-breaks
        // draw differently and the NBA's days after them move. The NBA's own odds are unchanged (audit on both seeds).
        // Run 101 (H78): re-pinned (was F5CA4EEE5084B908 since run 95). The schedule's dates changed (no third straight day,
        // ~14 back-to-backs a team, the season runs to Apr 12), so games fall on different days; no random draw changed.
        Pin("career seed 2026, 30 days, no saves", "4AA117BC5C5D7490", Session(s));
    }

    [Fact]
    public void Watching_your_game_live_plays_the_same_day_as_Continue_without_the_halftime_read()
    {
        // The live screen (`BeginLiveDay` / `FinishLiveDay`) draws your game's seed where Continue would, so watching
        // a game without touching anything gives the same day as pressing Continue, with one designed difference:
        // the halftime read (#268) is applied for you on Continue, and left to you when you watch (`Interactive`).
        // With the read switched off the two are the same day, game for game.
        var id = BenchClub(2026);
        var quick = GameSession.NewGame(2026, id, savePath: null);
        var watched = GameSession.NewGame(2026, id, savePath: null);
        quick.HumanTeam.Tactic.HalftimeAdjust = false; watched.HumanTeam.Tactic.HalftimeAdjust = false;
        int games = 0;
        for (int d = 0; d < 35; d++)
        {
            bool mine = watched.HumanPlaysToday;
            quick.AdvanceDay();
            if (mine)
            {
                var (live, played) = watched.BeginLiveDay();
                Assert.NotNull(live);
                while (live!.Step()) { }
                watched.FinishLiveDay(live, played);
                games++;
            }
            else watched.AdvanceDay();
        }
        Assert.True(games >= 6, $"some games watched ({games})");
        var a = Session(quick).Where(l => !l.StartsWith("inbox")).ToList();
        var b = Session(watched).Where(l => !l.StartsWith("inbox")).ToList();
        Assert.True(FirstDifference(a, b) == null, FirstDifference(a, b));
        Assert.Equal(quick.Inbox.Count, watched.Inbox.Count);
    }

    [Fact]
    public void A_saved_career_loads_as_the_same_career_and_two_loads_play_the_same_future()
    {
        var path = TempSave(); string? a = null, b = null, c = null;
        try
        {
            var s = GameSession.NewGame(2026, BenchClub(2026), path);
            Days(s, 6); // the day loop saves as in play (every 7 days and at phase changes since D74 B)
            s.SaveNow();
            var before = Session(s);
            a = CopyOf(path); b = CopyOf(path);
            var la = GameSession.Load(a); var lb = GameSession.Load(b);
            Assert.True(FirstDifference(before, Session(la)) == null, "load A: " + FirstDifference(before, Session(la)));
            Assert.True(FirstDifference(before, Session(lb)) == null, "load B: " + FirstDifference(before, Session(lb)));

            // Two loads of one save play the same future (a bug report's save replays).
            Days(la, 8); Days(lb, 8);
            Assert.True(FirstDifference(Session(la), Session(lb)) == null, "two loads' futures: " + FirstDifference(Session(la), Session(lb)));

            // A save of a loaded career loads back the same again.
            la.SaveNow();
            c = CopyOf(a);
            Assert.True(FirstDifference(Session(la), Session(GameSession.Load(c))) == null, "save of a load: " + FirstDifference(Session(la), Session(GameSession.Load(c))));
        }
        finally { Clean(path, a ?? "x-none", b ?? "x-none", c ?? "x-none"); }
    }

    [Fact]
    public void Saving_draws_from_the_random_streams()
    {
        // An invariant for Phase 2 (D74): each save takes one number from the session's and the world's random streams
        // (the seeds a reload starts from), so a career that saves plays a different future from one that doesn't, and
        // carrying on after a save differs from loading that save. A faster save must keep drawing them, or results move.
        var id = BenchClub(77);
        var path = TempSave();
        try
        {
            var plain = GameSession.NewGame(77, id, savePath: null);
            var saving = GameSession.NewGame(77, id, path);
            var first = Session(plain);
            Assert.True(FirstDifference(first.Skip(1).ToList(), Session(saving).Skip(1).ToList()) == null, "the same career on day one");
            Days(plain, 10); Days(saving, 10);
            Assert.NotEqual(Hash(Session(plain)), Hash(Session(saving)));

            // Loading a save and carrying on in memory: different futures from the same day. (The day loop saves every
            // 7 days since D74 B, #635, so save today first.)
            saving.SaveNow();
            var copy = CopyOf(path);
            try
            {
                var loaded = GameSession.Load(copy);
                Assert.True(FirstDifference(Session(saving), Session(loaded)) == null, FirstDifference(Session(saving), Session(loaded)));
                Days(saving, 5); Days(loaded, 5);
                Assert.NotEqual(Hash(Session(saving)), Hash(Session(loaded)));
            }
            finally { Clean(copy); }
        }
        finally { Clean(path); }
    }

    [Fact]
    public void A_career_saved_in_the_playoffs_and_the_summer_loads_the_same()
    {
        // Played without daily saves (quick), then saved at the two points where the most state lives outside the
        // tables: mid-playoffs (series and their games) and the summer (the offseason's phase, draft and contracts).
        var path = TempSave(); var copies = new List<string>();
        try
        {
            var s = GameSession.NewGame(2026, BenchClub(2026), savePath: null);
            int guard = 0;
            while (s.Phase != SessionPhase.Postseason && guard++ < 400) s.AdvanceDay();
            Assert.Equal(SessionPhase.Postseason, s.Phase);
            Days(s, 6);
            Check("mid-playoffs", 4);

            while (s.Phase != SessionPhase.Offseason && guard++ < 600) s.AdvanceDay();
            Assert.Equal(SessionPhase.Offseason, s.Phase);
            Check("the summer", 0);
            for (int i = 0; i < 3 && s.Phase == SessionPhase.Offseason; i++) s.AdvanceOffseason();
            Check($"the summer, {s.Offseason?.Phase}", 0);

            void Check(string when, int days)
            {
                s.Save = new Hardwood.Core.Persistence.SaveFile(path);
                s.SaveNow();
                var before = Session(s);
                var a = CopyOf(path); var b = CopyOf(path); copies.Add(a); copies.Add(b);
                var la = GameSession.Load(a); var lb = GameSession.Load(b);
                Assert.True(FirstDifference(before, Session(la)) == null, $"{when}: " + FirstDifference(before, Session(la)));
                Assert.Equal(s.Offseason?.Phase, la.Offseason?.Phase);
                if (days > 0) { Days(la, days); Days(lb, days); }
                else if (la.Phase == SessionPhase.Offseason) { la.AdvanceOffseason(); lb.AdvanceOffseason(); }
                Assert.True(FirstDifference(Session(la), Session(lb)) == null, $"{when}, two loads' futures: " + FirstDifference(Session(la), Session(lb)));
                s.Save = null;
                _out.WriteLine($"{when}: {before.Count} lines, {Hash(before)}");
            }
        }
        finally { Clean(copies.Prepend(path).ToArray()); }
    }
}
