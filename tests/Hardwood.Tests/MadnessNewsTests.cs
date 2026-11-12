using Hardwood.Core;
using Hardwood.Core.College;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Jobs;
using Hardwood.Core.Model;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// C4 part 2, the clean-up (#303-#304): March Madness news that matches what happened, round by round; the Elite Eight on the athletic director's ladder.
/// </summary>
public class MadnessNewsTests
{
    private static (CollegeWorld World, GameSimulator Sim, Rng Rng) Played(ulong seed, bool real)
    {
        var rng = new Rng(seed);
        var gen = new PlayerGenerator(new Rng(seed ^ 0x51), 2_000_000);
        var c = CollegeWorld.Generate(gen, rng, new DateOnly(2026, 10, 1), real: real);
        var sim = new GameSimulator();
        c.PlayThrough(new DateOnly(2027, 4, 30), sim, rng);
        return (c, sim, rng);
    }

    [Theory]
    [InlineData(25UL, true)]
    [InlineData(7UL, false)]
    public void Every_round_has_news_that_matches_the_bracket(ulong seed, bool real)
    {
        var (c, _, _) = Played(seed, real);
        var s = c.Season;
        Assert.NotNull(s.ChampionId);
        var titles = c.News.Select(n => n.Title).ToList();
        int Index(string start) => titles.FindIndex(t => t.StartsWith(start));

        // One story per round, in order: First Four, round of 64, Sweet 16, Elite Eight, Final Four, the final, the champion.
        int[] order =
        {
            Index("March Madness: the field is set"), Index("March Madness: the First Four"),
            titles.FindIndex(t => t.StartsWith("March Madness:") && t.EndsWith("in the round of 64")),
            Index("March Madness: the Sweet 16 is set"), Index("March Madness: the Elite Eight is set"),
            Index("The Final Four: "), Index("The final: "), titles.FindIndex(t => t.Contains("win March Madness")),
        };
        Assert.All(order, i => Assert.True(i >= 0, string.Join(" | ", titles.Where(t => t.Contains("Madness") || t.StartsWith("The ")))));
        Assert.Equal(order.OrderBy(i => i), order);
        Assert.Single(titles, t => t.Contains("win March Madness"));

        var games = s.Games.Where(g => g.Event == CollegeWorld.Madness).ToList();
        // The Sweet 16 story lists the 16 still in, four a region, with their seeds.
        var sweet = c.News[Index("March Madness: the Sweet 16 is set")].Body;
        var sixteen = games.Where(g => g.Round == 3).SelectMany(g => new[] { g.HomeId, g.AwayId }).ToList();
        Assert.Equal(16, sixteen.Count);
        foreach (var id in sixteen) Assert.Contains(c.WithSeed(id), sweet);
        foreach (var region in MadnessBracket.Regions) Assert.Contains(region + ": ", sweet);

        // Every upset of the round of 64 is named with its score, and nothing else is called one.
        var r64 = c.News[order[2]].Body;
        var upsets = games.Where(g => g.Round == 1 && c.IsUpset(g)).ToList();
        foreach (var g in upsets) Assert.Contains($"{c.WithSeed(g.WinnerId!.Value)} beat ", r64);
        Assert.Equal(upsets.Count == 0, !r64.Contains("Upsets:"));

        // A Cinderella story for each double-digit seed in the Sweet 16, and only for them.
        int cinderellas = sixteen.Count(id => s.Seeds[id] >= 10);
        Assert.Equal(cinderellas, titles.Count(t => t.StartsWith("Cinderella:") && t.EndsWith("reach the Sweet 16")));

        // The Final Four and the champion's story agree with the games.
        var four = games.Where(g => g.Round == 5).SelectMany(g => new[] { g.HomeId, g.AwayId }).ToList();
        foreach (var id in four) Assert.Contains(c.Program(id)!.FullName, titles[order[5]]);
        var champ = c.News[order[7]];
        Assert.Contains(c.Program(s.ChampionId!.Value)!.FullName, champ.Title);
        Assert.Contains($"as {CollegeWorld.Article(s.Seeds[s.ChampionId.Value])}-seed", champ.Body);
        Assert.Contains("The road: ", champ.Body);
    }

    [Fact]
    public void An_elite_eight_run_is_its_own_step_on_the_ladder()
    {
        var (c, _, _) = Played(25, real: false);
        var games = c.Season.Games.Where(g => g.Event == CollegeWorld.Madness).ToList();
        var eliteLoser = games.Where(g => g.Round == 4).Select(g => g.HomeId == g.WinnerId ? g.AwayId : g.HomeId).First();
        var sweetLoser = games.Where(g => g.Round == 3).Select(g => g.HomeId == g.WinnerId ? g.AwayId : g.HomeId).First();
        Assert.Equal(SeasonGoal.ConferenceFinals, Board.Achieved(c, eliteLoser));
        Assert.Equal(SeasonGoal.SecondRound, Board.Achieved(c, sweetLoser));
        Assert.Equal(SeasonGoal.Title, Board.Achieved(c, c.Season.ChampionId!.Value));
        Assert.Equal(2.5, Board.CollegeStep(SeasonGoal.ConferenceFinals));
    }

    [Fact]
    public void The_real_first_four_has_the_last_four_in_on_the_11_line_and_the_weakest_champions_on_the_16_line()
    {
        var (c, _, _) = Played(25, real: true);
        var s = c.Season;
        var ff = s.Games.Where(g => g.Event == CollegeWorld.Madness && g.Round == 0).ToList();
        Assert.Equal(4, ff.Count);
        var elevens = ff.Where(g => s.Seeds[g.HomeId] == 11).SelectMany(g => new[] { g.HomeId, g.AwayId }).ToList();
        Assert.Equal(s.LastFourIn.OrderBy(i => i), elevens.OrderBy(i => i));
        var sixteens = ff.Where(g => s.Seeds[g.HomeId] == 16).SelectMany(g => new[] { g.HomeId, g.AwayId }).ToList();
        Assert.Equal(4, sixteens.Count);
        Assert.All(sixteens, id => Assert.Contains(id, s.ConferenceChampions.Values));
        Assert.All(ff, g => Assert.Equal(s.Seeds[g.HomeId], s.Seeds[g.AwayId]));
        // Lines 11 and 16 hold six teams, every other line four; each First Four winner meets the line's opponent.
        for (int line = 1; line <= 16; line++) Assert.Equal(line is 11 or 16 ? 6 : 4, s.Seeds.Values.Count(v => v == line));
        Assert.All(ff, g => Assert.Equal(17 - s.Seeds[g.HomeId], s.Seeds[s.Bracket[g.Slot ^ 1]]));
        Assert.DoesNotContain(0, s.Bracket);
        // The projection on the day agrees with the bracket (bracketology shows six teams on those lines).
        Assert.Equal(s.Seeds.OrderBy(kv => kv.Key), c.Bracketology().Seeds.OrderBy(kv => kv.Key));
    }
}
