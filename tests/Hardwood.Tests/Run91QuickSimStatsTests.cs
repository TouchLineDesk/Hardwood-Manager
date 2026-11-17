using Hardwood.Core;
using Hardwood.Core.Model;
using Hardwood.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// H63 (run 91): quick-simmed games record steals, blocks, threes and skill-based shooting, so League Leaders and
/// Leagues → Stats rank every club's players, not only yours. The scores come from the same random stream as before.
/// </summary>
public class Run91QuickSimStatsTests
{
    private readonly ITestOutputHelper _out;
    public Run91QuickSimStatsTests(ITestOutputHelper output) => _out = output;

    private static List<Team> Teams()
    {
        var (league, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026, 2026);
        return league.Teams.OrderBy(t => t.Id).ToList();
    }

    private static (Dictionary<int, StatLine> Stats, List<(int, int)> Scores, int Games) Season(List<Team> teams, Rules rules, ulong seed, int rounds = 6)
    {
        var rng = new Rng(seed);
        var stats = new Dictionary<int, StatLine>();
        var scores = new List<(int, int)>();
        var day = new DateOnly(2026, 11, 3);
        int games = 0;
        for (int r = 0; r < rounds; r++)
        for (int i = 0; i + 1 < teams.Count; i += 2)
        {
            var (h, a) = (teams[(i + r) % teams.Count], teams[(i + 1 + 3 * r) % teams.Count]);
            if (h.Id == a.Id) continue;
            scores.Add(QuickSim.Play(h, a, day.AddDays(r * 2 + i % 2), rng, stats, rules));
            games++;
        }
        return (stats, scores, games);
    }

    [Fact]
    public void Quick_games_record_steals_blocks_and_threes_at_believable_rates()
    {
        var teams = Teams();
        foreach (var (rules, stlLo, stlHi, blkLo, blkHi, tpaLo, tpaHi) in new[]
                 {
                     (Rules.Nba, 6.5, 8.7, 4.0, 5.8, 31.0, 42.0),
                     (Rules.Fiba, 5.5, 7.7, 2.5, 4.1, 22.0, 31.0),
                 })
        {
            var (stats, _, games) = Season(teams, rules, 91);
            double sides = games * 2.0;
            double stl = stats.Values.Sum(x => x.Stl) / sides, blk = stats.Values.Sum(x => x.Blk) / sides;
            double tpa = stats.Values.Sum(x => x.Tpa) / sides;
            double tp = stats.Values.Sum(x => x.Tpm) / (double)stats.Values.Sum(x => x.Tpa);
            double fg = stats.Values.Sum(x => x.Fgm) / (double)stats.Values.Sum(x => x.Fga);
            _out.WriteLine($"{rules.Name}: {games} games; a side: stl {stl:F1}, blk {blk:F1}, 3PA {tpa:F1}; 3P% {tp:F3}, FG% {fg:F3}");
            Assert.InRange(stl, stlLo, stlHi);
            Assert.InRange(blk, blkLo, blkHi);
            Assert.InRange(tpa, tpaLo, tpaHi);
            Assert.InRange(tp, 0.32, 0.40);
            Assert.InRange(fg, 0.40, 0.50);
            foreach (var l in stats.Values)
            {
                Assert.True(l.Tpm <= l.Tpa && l.Tpa <= l.Fga && l.Fgm <= l.Fga && l.Tpm <= l.Fgm, $"impossible line {l.Fgm}/{l.Fga}, {l.Tpm}/{l.Tpa}");
                Assert.True(2 * l.Fgm + l.Tpm <= l.Pts, $"makes worth more than the points: {l.Fgm} FGM, {l.Tpm} 3PM, {l.Pts} pts");
            }
            // The boards have many clubs in them: the top ten in steals and blocks come from at least five clubs.
            Assert.True(stats.Values.OrderByDescending(x => x.Stl).Take(10).Select(x => x.TeamId).Distinct().Count() >= 5);
            Assert.True(stats.Values.OrderByDescending(x => x.Blk).Take(10).Select(x => x.TeamId).Distinct().Count() >= 5);
        }
    }

    [Fact]
    public void Blocks_go_to_big_men_and_threes_to_shooters()
    {
        var teams = Teams();
        var (stats, _, _) = Season(teams, Rules.Nba, 7, rounds: 10);
        var byId = teams.SelectMany(t => t.Roster).ToDictionary(p => p.Id);
        double Per(Func<StatLine, int> f, Func<Player, bool> who)
        {
            var ls = stats.Where(kv => byId.TryGetValue(kv.Key, out var p) && who(p)).Select(kv => kv.Value).ToList();
            return ls.Sum(f) / Math.Max(1.0, ls.Sum(x => x.Min)) * 36;
        }
        double bigBlk = Per(x => x.Blk, p => p.PrimaryPosition is Position.C or Position.PF);
        double guardBlk = Per(x => x.Blk, p => p.PrimaryPosition is Position.PG or Position.SG);
        double shooters = Per(x => x.Tpa, p => p.Attributes[Attr.ThreePoint] >= 14);
        double nonShooters = Per(x => x.Tpa, p => p.Attributes[Attr.ThreePoint] <= 7);
        _out.WriteLine($"blocks per 36: bigs {bigBlk:F2}, guards {guardBlk:F2}; 3PA per 36: shooters {shooters:F1}, non-shooters {nonShooters:F1}");
        Assert.True(bigBlk > 2.5 * guardBlk);
        Assert.True(shooters > 2 * nonShooters);
    }

    [Fact]
    public void The_new_lines_leave_the_scores_and_the_main_stream_alone_and_repeat_exactly()
    {
        var teams = Teams();
        var a = Season(teams, Rules.Nba, 2026);
        var b = Season(teams, Rules.Nba, 2026);
        Assert.Equal(a.Scores, b.Scores);
        Assert.Equal(a.Stats.Values.Sum(x => x.Stl * 7 + x.Blk * 5 + x.Tpm * 3 + x.Tpa), b.Stats.Values.Sum(x => x.Stl * 7 + x.Blk * 5 + x.Tpm * 3 + x.Tpa));

        // The main stream: after a game it is where the old quick sim left it (no extra draws). The old one drew one
        // margin, one total, an optional tie-break, then per side three minutes-and-weight draws per player and two
        // team totals; replaying that by hand must land on the same next number.
        var home = teams[0]; var away = teams[1];
        var day = new DateOnly(2026, 11, 3);
        var rng = new Rng(555);
        QuickSim.Play(home, away, day, rng, new Dictionary<int, StatLine>(), Rules.Nba);
        var replay = new Rng(555);
        replay.NextGaussian(); replay.NextGaussian();
        foreach (var t in new[] { home, away })
        {
            int n = Math.Min(9, t.Available(day).Count());
            for (int i = 0; i < n * 2 + 2; i++) replay.NextGaussian();
        }
        // A tie needs a coin and a number: allow for it by checking the next value is one of the two.
        ulong next = rng.NextULong();
        var alt = new Rng(555);
        alt.NextGaussian(); alt.NextGaussian(); alt.NextDouble(); alt.NextDouble();
        foreach (var t in new[] { home, away })
        {
            int n = Math.Min(9, t.Available(day).Count());
            for (int i = 0; i < n * 2 + 2; i++) alt.NextGaussian();
        }
        Assert.True(next == replay.NextULong() || next == alt.NextULong(), "the quick sim drew more from the main stream than before");
    }

    [Fact]
    public void A_shooters_makes_never_exceed_his_points()
    {
        var p = Teams()[0].Roster.First();
        var side = new Rng(1);
        for (int pts = 0; pts < 40; pts++)
        {
            int fga = (int)Math.Round(pts / 1.25);
            var (fgm, tpa, tpm) = QuickSim.Shooting(p, pts, fga, fga, side);
            Assert.True(tpa <= fga && tpm <= tpa && fgm >= tpm && 2 * fgm + tpm <= pts, $"{pts} pts: {fgm}/{fga}, {tpm}/{tpa}");
        }
    }
}
