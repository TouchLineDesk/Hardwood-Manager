using Hardwood.Core;
using Hardwood.Core.Engine;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Hardwood.Desktop.Controls;
using Hardwood.Desktop.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 109, the fourth bug sweep (Danny, #883). H94: any of the five can block a shot (the help, mostly bigs), not only the
/// man on the ball, so the block leaders are rim protectors; the same draw decides whether a miss is blocked, so no score moves.
/// H95: a pyramid club re-signing reads the contract text. H59: a club outside the NBA is told why it has no pick. H96: opening
/// night isn't "Preseason · opens". H98: money under $100M keeps a decimal. H100: a series under way says which game is next.
/// </summary>
[Collection(SharedUiState.Name)] // touches app settings or the portrait service (run 131: they raced PortraitServiceTests)
public class Run109BugSweepTests
{
    private readonly ITestOutputHelper _out;
    public Run109BugSweepTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void A_tall_shot_blocking_centre_weighs_far_more_than_a_guard()
    {
        Player Make(Position pos, int cm, int block)
        {
            var p = new Player { Id = 1, FirstName = "A", LastName = "B", HeightCm = cm, PrimaryPosition = pos };
            p.Attributes[Attr.Block] = block;
            return p;
        }
        double big = GameSimulator.BlockWeight(Make(Position.C, 224, 20));
        double centre = GameSimulator.BlockWeight(Make(Position.C, 211, 15));
        double guard = GameSimulator.BlockWeight(Make(Position.PG, 188, 12));
        _out.WriteLine($"big {big:F2}, centre {centre:F2}, guard {guard:F2}");
        Assert.True(big > 2 * centre, "a 224 cm, 20-block centre stands out among centres");
        Assert.True(centre > 6 * guard, "a centre is far likelier than a guard to be the blocker");
    }

    [Fact]
    public void The_block_leaders_over_a_stretch_of_games_are_bigs()
    {
        var (league, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026, 2026);
        var teams = league.Teams.OrderBy(t => t.Id).ToList();
        var sim = new GameSimulator();
        var blk = new Dictionary<Player, (int g, int b)>();
        int teamGames = 0, teamBlocks = 0;
        var rng = new Rng(99);
        for (int i = 0; i < 300; i++)
        {
            var h = teams[i % teams.Count]; var w = teams[(i * 7 + 3) % teams.Count];
            if (h == w) continue;
            var r = sim.Simulate(h, w, league.Rules, rng.NextULong(), false, new DateOnly(2026, 11, 3));
            foreach (var box in new[] { r.Home, r.Away })
            {
                teamGames++; teamBlocks += box.Blocks;
                foreach (var l in box.Lines.Where(l => l.Seconds > 0))
                {
                    var t = blk.GetValueOrDefault(l.Player);
                    blk[l.Player] = (t.g + 1, t.b + l.Blocks);
                }
            }
        }
        var top = blk.Where(kv => kv.Value.g >= 10).OrderByDescending(kv => (double)kv.Value.b / kv.Value.g).Take(5).ToList();
        foreach (var (p, t) in top) _out.WriteLine($"{p.Name} {p.PrimaryPosition} {p.HeightCm} blk {p.Attributes[Attr.Block]}: {(double)t.b / t.g:F2}");
        double perTeam = (double)teamBlocks / teamGames;
        _out.WriteLine($"{perTeam:F2} blocks a team a game");
        Assert.InRange(perTeam, 4.0, 6.5);   // the NBA: about 5
        Assert.All(top, kv => Assert.True(kv.Key.PrimaryPosition is Position.C or Position.PF, $"{kv.Key.Name} ({kv.Key.PrimaryPosition}) in the top 5"));
        Assert.True((double)top[0].Value.b / top[0].Value.g >= 2.8, "the leader blocks about 3 a game or more");
    }

    [Fact]
    public void Money_keeps_a_decimal_under_100_million()
    {
        Assert.Equal("$12.6M", Budgets.Money(12_600_000));
        Assert.Equal("$34M", Budgets.Money(34_000_000));
        Assert.Equal("$8.2M", Budgets.Money(8_200_000));
        Assert.Equal("$4.0M", Budgets.Money(4_000_000));
        Assert.Equal("$146M", Budgets.Money(146_000_000));
        Assert.Equal(Budgets.Money(12_600_000), Franchise.Money(12_600_000));
        // H98's case: $8.2M of a $12.6M budget leaves $4.4M, and the three now add up.
        Assert.Equal("$4.4M", Budgets.Money(12_600_000 - 8_200_000));
    }

    [Fact]
    public void A_series_under_way_says_which_game_is_next()
    {
        var (league, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026, 2026);
        Team a = league.Teams[0], b = league.Teams[1];
        Assert.Equal("Game 7 next", BracketView.NextGameLabel(new BookSeries("Final", a, b, 3, 3, null, false)));
        Assert.Equal("Game 2 next", BracketView.NextGameLabel(new BookSeries("Final", a, b, 1, 0, null, false)));
        Assert.Null(BracketView.NextGameLabel(new BookSeries("Final", a, b, 0, 0, null, false)));
        Assert.Null(BracketView.NextGameLabel(new BookSeries("Final", a, b, 4, 3, a, false)));
        Assert.Null(BracketView.NextGameLabel(null));
    }

    [Fact]
    public void A_club_outside_the_NBA_is_told_why_it_has_no_pick()
    {
        Assert.Contains("only NBA clubs draft", GameSession.NoPickReasonOutsideNba);
        Assert.StartsWith("You have no pick this year:", GameSession.NoPickReasonOutsideNba);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void A_pyramid_club_re_signing_reads_the_contract_text_and_its_draft_reason()
    {
        // The gleague career (seed 31), to the summer's re-signing step.
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(31);
        var teamId = world[UsaPyramid.Tier2].Clubs.OrderBy(t => QuickSim.Strength(t)).ElementAt(15).Id;
        var s = GameSession.NewGame(31, teamId, null);
        int guard = 0;
        while (s.Phase != SessionPhase.Offseason && guard++ < 700) s.AdvanceDay();
        Assert.Equal(SessionPhase.Offseason, s.Phase);
        _out.WriteLine($"{s.HumanTeam.FullName} ({s.HumanLeagueName}), {s.Offseason!.Phase}, in the NBA {s.InNabl}");
        if (s.InNabl || s.Offseason.Phase != OffseasonPhase.ReSigning) return;   // promoted before the summer: not this case
        var shell = new MainViewModel();
        shell.AttachSession(s);
        var page = new OffseasonViewModel(shell);
        page.Refresh();
        _out.WriteLine(page.Instructions);
        Assert.StartsWith("Contract decisions and free agency", page.Instructions);   // H95
        if (page.ShowDraft && !page.DraftText.StartsWith("You pick")) Assert.Contains("only NBA clubs draft", page.DraftText);   // H59
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void Opening_night_is_not_still_to_open()
    {
        var (probe, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026);
        var s = GameSession.NewGame(2026, probe.Teams.First(t => t.Abbreviation == "BOS").Id, null);
        Assert.Equal($"Preseason · opens {s.OpeningNight:MMM d}", PhaseLine(s));
        int guard = 0;
        while (s.Today < s.OpeningNight && guard++ < 30) s.AdvanceDay();
        _out.WriteLine($"{s.Today}: {s.Phase}, {PhaseLine(s)}");
        if (s.Phase == SessionPhase.Preseason) Assert.Equal($"Opening night · {s.OpeningNight:MMM d}", PhaseLine(s));
        else Assert.DoesNotContain("opens", PhaseLine(s));
    }

    private static string PhaseLine(GameSession s)
    {
        var vm = new MainViewModel();
        vm.AttachSession(s);
        return vm.PhaseText;
    }
}
