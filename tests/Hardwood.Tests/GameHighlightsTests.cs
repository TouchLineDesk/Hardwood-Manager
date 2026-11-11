using Hardwood.Core;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Roadmap 2, match day part 2: every highlight card and ticker line says something true about the game.</summary>
public class GameHighlightsTests
{
    private static readonly League World = new WorldGenerator(new Rng(31)).Generate("L", 2026, new DateOnly(2026, 10, 1));

    private static IEnumerable<GameResult> Games(int n)
    {
        var sim = new GameSimulator();
        for (int i = 0; i < n; i++)
            yield return sim.Simulate(World.Teams[i % 6], World.Teams[6 + i % 6], Rules.Nba, (ulong)(100 + i), keepLog: true, date: new DateOnly(2026, 11, 5));
    }

    private static TeamBox BoxOf(GameResult r, Team? t) => t == r.Home.Team ? r.Home : r.Away;

    [Fact]
    public void Every_card_matches_the_box_score_and_timeline()
    {
        var kinds = new HashSet<string>();
        foreach (var r in Games(40))
        {
            var cards = GameHighlights.For(r);
            Assert.InRange(cards.Count, 1, 6);
            Assert.Equal("star", cards[0].Kind);
            var all = r.Home.Lines.Concat(r.Away.Lines).Where(l => l.Seconds > 0).ToList();
            var best = all.Max(GameHighlights.GameScore);
            var star = all.First(l => l.Player == cards[0].Player);
            Assert.Equal(best, GameHighlights.GameScore(star), 6);
            Assert.Equal(GameHighlights.StatLine(star), cards[0].Value);

            foreach (var c in cards)
            {
                kinds.Add(c.Kind);
                // A named player really played for the team on the card.
                if (c.Player != null) Assert.Contains(BoxOf(r, c.Team).Lines, l => l.Player == c.Player);
                var winner = r.Home.Points > r.Away.Points ? r.Home : r.Away;
                bool home = winner == r.Home;
                switch (c.Kind)
                {
                    case "clutch":
                        Assert.Equal(winner.Team, c.Team);
                        // After the decisive basket the winner never trailed or was level again.
                        var lastLead = r.Flow.Select((p, i) => (p, i)).Last(x => Math.Sign(x.p.Home - x.p.Away) == (home ? 1 : -1)
                            && (x.i == 0 || Math.Sign(r.Flow[x.i - 1].Home - r.Flow[x.i - 1].Away) != (home ? 1 : -1)));
                        Assert.All(r.Flow.Skip(lastLead.i), p => Assert.True(home ? p.Home > p.Away : p.Away > p.Home));
                        Assert.True(GameFlow.SecondsLeft(r, lastLead.p.Elapsed, lastLead.p.Period) <= 120);
                        Assert.Equal(lastLead.p.Scorer, c.Player);
                        break;
                    case "comeback":
                        int deficit = r.Flow.Max(p => home ? p.Away - p.Home : p.Home - p.Away);
                        Assert.True(deficit >= 10);
                        Assert.Equal($"{deficit} down", c.Value);
                        break;
                    case "wire":
                        Assert.All(r.Flow, p => Assert.True(home ? p.Home >= p.Away : p.Away >= p.Home));
                        break;
                    case "threes":
                        Assert.Contains(all, l => l.Player == c.Player && l.ThreePM >= 5 && c.Value == $"{l.ThreePM}-{l.ThreePA} 3PT");
                        break;
                    case "triple":
                        Assert.Contains(all, l => l.Player == c.Player && GameHighlights.Doubles(l) >= 3);
                        break;
                    case "double":
                        Assert.Contains(all, l => l.Player == c.Player && GameHighlights.Doubles(l) == 2);
                        break;
                    case "fouls":
                        Assert.Contains(all, l => l.Player == c.Player && l.FouledOut);
                        break;
                }
            }
        }
        // Forty games should show more than just the player of the game.
        Assert.True(kinds.Count >= 4, string.Join(",", kinds));
    }

    [Fact]
    public void A_game_in_progress_never_claims_a_winner()
    {
        var live = new GameSimulator().StartLive(World.Teams[0], World.Teams[1], Rules.Nba, 9, new DateOnly(2026, 11, 5));
        for (int i = 0; i < 120; i++)
        {
            live.Step();
            var cards = GameHighlights.For(live.Result, final: false);
            Assert.DoesNotContain(cards, c => c.Kind is "clutch" or "wire");
            Assert.True(cards.Count == 0 || cards[0].Title == "Best so far");
        }
    }

    [Fact]
    public void Ticker_facts_quote_the_box_score()
    {
        foreach (var r in Games(5))
        {
            var facts = GameHighlights.Ticker(r);
            Assert.Contains($"Turnovers: {r.Away.Team.Abbreviation} {r.Away.Turnovers}, {r.Home.Team.Abbreviation} {r.Home.Turnovers}", facts);
            Assert.Contains(facts, f => f.StartsWith("Shooting:") && f.Contains($"({r.Away.ThreePM}-{r.Away.ThreePA} from three)"));
            var top = r.Home.Lines.OrderByDescending(l => l.Points).ThenByDescending(GameHighlights.GameScore).First();
            Assert.Contains($"{r.Home.Team.Abbreviation} leader: {top.Player.Name}, {GameHighlights.StatLine(top)}", facts);
            foreach (var f in facts.Where(f => f.StartsWith("Foul trouble")))
                Assert.Contains(r.Home.Lines.Concat(r.Away.Lines), l => f.Contains(l.Player.Name) && f.EndsWith($"has {l.Fouls} fouls") && l.Fouls >= 4);
        }
    }

    [Fact]
    public void Flow_points_name_a_scorer_from_the_side_that_scored()
    {
        foreach (var r in Games(5))
        {
            int h = 0, a = 0;
            foreach (var p in r.Flow)
            {
                Assert.NotNull(p.Scorer);
                var side = p.Home > h ? r.Home : r.Away;
                if (p.Home > h && p.Away > a) side = null!; // both scored across a period break: skip
                if (side != null) Assert.Contains(side.Lines, l => l.Player == p.Scorer);
                h = p.Home; a = p.Away;
            }
        }
    }
}
