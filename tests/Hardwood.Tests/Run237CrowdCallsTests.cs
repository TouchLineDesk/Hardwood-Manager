using Hardwood.Core;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.Presentation;
using Hardwood.Desktop;
using Hardwood.Desktop.Audio;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>Run 237, K25's leftovers (`danny-broadcast-nov27` (c)): the commentary hears the crowd, from the same moments
/// and rules as the crowd's sounds; one final call; nothing after the final horn but the result; initials in the
/// play-by-play list. Presentation only: no engine, RNG or save change.</summary>
[Collection(SharedUiState.Name)]
public class Run237CrowdCallsTests
{
    private readonly ITestOutputHelper _out;
    public Run237CrowdCallsTests(ITestOutputHelper output) => _out = output;

    private static (Team home, Team away) Teams()
    {
        var l = new WorldGenerator(new Rng(17)).Generate("L", 2026, new DateOnly(2026, 10, 1));
        var h = l.Teams[0]; var a = l.Teams[1];
        h.Tactic = new Tactic(); a.Tactic = new Tactic();
        return (h, a);
    }

    private static List<Player> Five(Team t) => t.Roster.OrderByDescending(p => p.Overall).Take(5).OrderBy(p => (int)p.PrimaryPosition).ToList();

    private static PossessionInput Input(int index, List<PlayEvent> ev, List<Player> hf, List<Player> af, double start, double end, int sh, int sa) =>
        new(index, 2026, ev, start, end, hf, af, hf.Concat(af).Select((p, i) => (p, i)).ToDictionary(x => x.p, x => new Spot(47 + (x.i < 5 ? -4 : 4), 10 + (x.i % 5) * 7)),
            sh, sa, true, "Duke", "Kansas", DefensiveScheme.Man, DefensiveScheme.Man, PossessionStart.DeadBall, 2, false)
        { HomeSystem = OffensiveSystem.Motion };

    private static readonly string[] Crowdish = { "building", "crowd", "home fans", "this place", "roof", "pin drop", "the stands", "Duke fans", "officials hear it", "Boos" };
    private static bool IsCrowd(string text) => Crowdish.Any(w => text.Contains(w, StringComparison.Ordinal));

    [Fact]
    public void A_late_home_lead_change_erupts_in_the_words_and_the_sound()
    {
        var (h, a) = Teams();
        var hf = Five(h); var af = Five(a);
        // Down 1 with 40 s left, a home jumper puts Duke in front.
        var ev = new List<PlayEvent> { new(2, 40, PlayKind.Shot, true, hf[1], null, ShotZone.LongMid, true, 70, 69) };
        var c = PossessionChoreographer.Build(Input(5, ev, hf, af, 58, 40, 68, 69));
        var reads = CrowdCalls.Reads(c);
        Assert.Contains(reads, r => r.Read == CrowdRead.Erupt);
        var cues = Reactions.Plan(c);
        foreach (var (t, read) in reads.Where(r => r.Read == CrowdRead.Erupt))
            Assert.Contains(cues, q => q.Kind == Cue.Erupt && Math.Abs(q.T - t) < 1e-6);
        var crowd = c.Lines.Where(l => IsCrowd(l.Text)).ToList();
        Assert.Single(crowd);
        _out.WriteLine(string.Join(" | ", c.Lines.Select(l => $"{l.T:0.0} {l.Text}")));
        // Same input, same words (no random stream).
        var c2 = PossessionChoreographer.Build(Input(5, ev, hf, af, 58, 40, 68, 69));
        Assert.Equal(c.Lines.Select(l => l.Text), c2.Lines.Select(l => l.Text));
    }

    [Fact]
    public void A_home_foul_in_a_tight_finish_is_booed_and_a_visitors_go_ahead_three_silences_the_building()
    {
        var (h, a) = Teams();
        var hf = Five(h); var af = Five(a);
        var foul = new List<PlayEvent> { new(2, 30, PlayKind.Foul, true, hf[3], af[1], ShotZone.Rim, false, 70, 70) };
        var c = PossessionChoreographer.Build(Input(7, foul, hf, af, 45, 30, 70, 70));
        Assert.Contains(CrowdCalls.Reads(c), r => r.Read == CrowdRead.Boo);
        Assert.Contains(Reactions.Plan(c), q => q.Kind == Cue.Boo);
        Assert.Contains(c.Lines, l => IsCrowd(l.Text));
        // The visitors' three to lead late: the words go quiet as the sound's loudest groan plays.
        var three = new List<PlayEvent> { new(2, 20, PlayKind.Shot, false, af[1], null, ShotZone.AboveBreakThree, true, 70, 72) };
        var input = Input(8, three, hf, af, 40, 20, 70, 69) with { HomeBallIfNoEvents = false };
        var c3 = PossessionChoreographer.Build(input);
        Assert.Contains(CrowdCalls.Reads(c3), r => r.Read == CrowdRead.Silenced);
        Assert.Contains(Reactions.Plan(c3), q => q.Kind == Cue.Groan && q.Gain >= 1.0);
        // A home foul early in a game the crowd isn't booed every time in words (the sound always boos).
        var early = new List<PlayEvent> { new(1, 900, PlayKind.Foul, true, hf[3], af[1], ShotZone.Rim, false, 20, 18) };
        var ce = PossessionChoreographer.Build(Input(9, early, hf, af, 915, 900, 20, 18) with { Periods = 2 });
        Assert.DoesNotContain(CrowdCalls.Reads(ce), r => r.Read == CrowdRead.Boo);
        Assert.Contains(Reactions.Plan(ce), q => q.Kind == Cue.Boo);
    }

    [Fact]
    public void Nothing_is_said_after_the_horn_and_the_buzzer_beater_keeps_its_line()
    {
        var (h, a) = Teams();
        var hf = Five(h); var af = Five(a);
        var ev = new List<PlayEvent>
        {
            new(2, 0.8, PlayKind.Shot, true, hf[1], null, ShotZone.AboveBreakThree, true, 71, 69),
            new(2, 0, PlayKind.PeriodEnd, true, null, null, ShotZone.Rim, false, 71, 69),
        };
        var c = PossessionChoreographer.Build(Input(11, ev, hf, af, 8, 0, 68, 69));
        var end = c.Lines.Single(l => l.Text.StartsWith("That's the end of"));
        Assert.DoesNotContain(c.Lines, l => l.T > end.T);
        var result = c.Lines.Single(l => l.Text.Contains("AT THE BUZZER"));
        // The crowd is on the buzzer-beater's own line, not a line of its own between it and the horn.
        Assert.True(IsCrowd(result.Text), result.Text);
        Assert.True(end.T - result.T >= PossessionChoreographer.ResultHold - 1e-9);
        Assert.Equal(1, c.Lines.Count(l => IsCrowd(l.Text)));
    }

    [Fact]
    public void Blowout_reads_follow_the_sounds_rule()
    {
        var (h, a) = Teams();
        var hf = Five(h); var af = Five(a);
        // The visitors go 15 up: the crowd goes quiet. A home dunk down 20 is a rare cheer, not an eruption.
        var ev = new List<PlayEvent> { new(1, 500, PlayKind.Shot, false, af[0], null, ShotZone.ShortMid, true, 30, 45) };
        var c = PossessionChoreographer.Build(Input(13, ev, hf, af, 515, 500, 30, 43) with { HomeBallIfNoEvents = false });
        Assert.Contains(CrowdCalls.Reads(c), r => r.Read == CrowdRead.Hushed);
        // Once a game: a side that was already 16 down earlier gets no second "gone quiet" when the gap swings back.
        var again = PossessionChoreographer.Build(Input(13, ev, hf, af, 515, 500, 30, 43) with { HomeBallIfNoEvents = false, HomeDeepestDeficit = 16 });
        Assert.DoesNotContain(CrowdCalls.Reads(again), r => r.Read == CrowdRead.Hushed);
        Assert.Equal(15, Reactions.BlowoutDeficit);
        Assert.Equal("A rare cheer from the home crowd tonight", CrowdCalls.Line(CrowdRead.Erupt, 0, "Duke", beaten: true, late: false));
        Assert.Equal("The home fans start heading for the exits", CrowdCalls.Line(CrowdRead.Hushed, 0, "Duke", beaten: false, late: true));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void Whole_games_hear_the_crowd_and_end_on_one_result_call()
    {
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { AutoPortraits = false, StudioShow = false });
        var paths = new List<string>();
        MainWindow? window = null;
        try
        {
            foreach (bool college in new[] { true, false })
            {
                var path = Path.Combine(Path.GetTempPath(), $"hw-run237-{Guid.NewGuid():N}.hwsave"); paths.Add(path);
                var vm = new MainViewModel();
                window = new MainWindow { DataContext = vm, Width = 1280, Height = 800 };
                window.Show();
                var start = (StartViewModel)vm.CurrentPage!;
                start.SavePath = path; start.SeedText = "31";
                if (college) start.PlayLeague = StartViewModel.CollegeChoice;
                start.SelectedTeam = start.Teams[1];
                start.NewGame();
                var s = vm.Session!;
                for (int g = 0; g < 2; g++)
                {
                    while (!s.HumanPlaysToday) s.AdvanceDay();
                    var (game, played) = s.BeginLiveDay();
                    var live = new LiveGameViewModel(vm, game!, played);
                    live.PauseAtBreaks = false;
                    int mismatched = 0, readsSaid = 0;
                    live.ScriptPlannedForTest = (c, cues) =>
                    {
                        // Every crowd read the commentary can say has its sound at the same moment.
                        foreach (var (t, read) in CrowdCalls.Reads(c))
                        {
                            bool ok = read switch
                            {
                                CrowdRead.Erupt => cues.Any(q => q.Kind == Cue.Erupt && Math.Abs(q.T - t) < 1e-6),
                                CrowdRead.Boo => cues.Any(q => q.Kind == Cue.Boo && Math.Abs(q.T - t - 0.15) < 1e-6),
                                CrowdRead.Oh => cues.Any(q => q.Kind == Cue.Oh && Math.Abs(q.T - t) < 1e-6),
                                _ => cues.Any(q => q.Kind == Cue.Groan && Math.Abs(q.T - t) < 1e-6),
                            };
                            if (!ok) mismatched++;
                        }
                        readsSaid += c.Lines.Count(l => IsCrowd(l.Text));
                    };
                    while (!live.Complete) live.StepOne();
                    var said = live.SaidForTest.Select(l => l.Text).ToList();
                    int crowd = said.Count(IsCrowd);
                    int finals = said.Count(l => l.Contains("! Final: "));
                    int lastHorn = said.FindLastIndex(l => l.StartsWith("That's the end of"));
                    var afterHorn = said.Skip(lastHorn + 1).ToList();
                    var log = live.Log.ToList();
                    int twinLog = log.Count(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"\b([A-Z][\w'-]+) fouls \1\b"));
                    _out.WriteLine($"{(college ? "college" : "pro")} {live.AwayTeam.City} at {live.HomeTeam.City}: {said.Count} lines, {crowd} crowd lines ({100.0 * crowd / said.Count:0.0}%), finals {finals}, after the horn: {string.Join(" | ", afterHorn)}");
                    foreach (var l in said.Where(IsCrowd).GroupBy(x => x).OrderByDescending(x => x.Count()).Take(8)) _out.WriteLine($"   {l.Count()}× {l.Key}");
                    Assert.Equal(0, mismatched);
                    Assert.Equal(crowd, readsSaid);
                    // The "gone quiet" line is said at most once a game.
                    Assert.True(said.Count(l => l.Contains("gone quiet") || l.Contains("heading for the exits") || l.Contains("seen enough") || l.Contains("much for the home fans")) <= 1);
                    // No crowd line is said more than three times in a game.
                    Assert.True(said.Where(IsCrowd).GroupBy(x => x).All(x => x.Count() <= 3));
                    Assert.InRange(crowd, 3, 40);
                    Assert.Equal(1, finals);
                    // After the last horn, only the result call and what it meant (at most two lines).
                    Assert.True(afterHorn.Count >= 1 && afterHorn.Count <= 3, string.Join(" | ", afterHorn));
                    Assert.Contains("! Final: ", afterHorn[0]);
                    Assert.Equal(0, twinLog);
                    s.FinishLiveDay(game!, played);
                    s.AdvanceDay();
                }
                window.Close(); window = null;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            }
        }
        finally
        {
            window?.Close();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            if (File.Exists(AppSettings.FilePath)) File.Delete(AppSettings.FilePath);
            AppSettings.FilePath = oldPath; AppSettings.Use(oldSettings);
            foreach (var p in paths) if (File.Exists(p)) File.Delete(p);
        }
    }
}
