using Hardwood.Core;
using Hardwood.Core.College;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// C4 part 1 (#190-#192): bracketology projects the field the way Selection Sunday picks it, the bubble is the next
/// teams by rating, and a college coach hears his seed (or that he's out) on Selection Sunday.
/// </summary>
public class BracketologyTests
{
    private static (CollegeWorld World, GameSimulator Sim, Rng Rng) NewWorld(ulong seed)
    {
        var rng = new Rng(seed);
        var gen = new PlayerGenerator(new Rng(seed ^ 0x51), 2_000_000);
        return (CollegeWorld.Generate(gen, rng, new DateOnly(2026, 10, 1)), new GameSimulator(), rng);
    }

    [Fact]
    public void A_midseason_projection_has_68_teams_on_sixteen_lines_with_one_automatic_bid_per_conference()
    {
        var (c, sim, rng) = NewWorld(41);
        c.PlayThrough(new DateOnly(2027, 2, 10), sim, rng);
        var p = c.Bracketology();
        Assert.False(p.Final);
        Assert.Equal(CollegeWorld.FieldSize, p.Field.Count);
        Assert.Equal(p.Field.Count, p.Field.Distinct().Count());
        for (int seed = 1; seed <= 15; seed++) Assert.Equal(4, p.Seeds.Values.Count(x => x == seed));
        Assert.Equal(8, p.Seeds.Values.Count(x => x == 16));
        // Each conference's leader holds its automatic bid, and every one of them is in.
        foreach (var conf in c.Active)
        {
            int leader = c.ConferenceTable(conf.Key)[0].TeamId;
            Assert.True(p.In(leader), $"{conf.Short} leader missing");
        }
        Assert.Equal(c.Active.Count(), p.AutoBids.Count);
        Assert.Equal(0, p.AutoBidsDecided);
        // The bubble: four in, eight out, none of the eight in the field, and the "out" teams rated below the last ones in.
        Assert.Equal(4, p.LastFourIn.Count);
        Assert.Equal(4, p.FirstFourOut.Count);
        Assert.Equal(4, p.NextFourOut.Count);
        Assert.All(p.LastFourIn, id => Assert.True(p.In(id) && !p.AutoBids.ContainsKey(id)));
        Assert.All(p.FirstFourOut.Concat(p.NextFourOut), id => Assert.False(p.In(id)));
        double Elo(int id) => c.Season.Elo[id];
        Assert.True(p.LastFourIn.Min(Elo) >= p.FirstFourOut.Max(Elo));
        Assert.True(p.FirstFourOut.Min(Elo) >= p.NextFourOut.Max(Elo));
        Assert.StartsWith("Last four in", p.StatusOf(p.LastFourIn[0]));
        Assert.Equal("First four out", p.StatusOf(p.FirstFourOut[0]));
        Assert.Equal("Next four out", p.StatusOf(p.NextFourOut[0]));
        Assert.StartsWith("No. 1 seed", p.StatusOf(p.Field[0]));
    }

    [Fact]
    public void Selection_Sunday_picks_exactly_the_last_projection_and_tells_the_coach_his_seed()
    {
        var (c, sim, rng) = NewWorld(42);
        c.PlayThrough(new DateOnly(2027, 3, 1), sim, rng);
        // Coach a program on the bubble's edge of the field, so the message is a real one either way.
        var before = c.Bracketology();
        int me = before.Field[30];
        c.ManagedProgramId = me;
        var day = new DateOnly(2027, 3, 2);
        while (!c.Season.Selected && day < new DateOnly(2027, 3, 16)) { c.PlayThrough(day, sim, rng); day = day.AddDays(1); }
        Assert.True(c.Season.Selected);
        var s = c.Season;
        Assert.DoesNotContain(s.Games, g => g.Event == CollegeWorld.Madness && g.Played);
        // Picked on the day the conference tournaments ended, from the same ratings: the projection is the bracket.
        var now = c.ProjectNow();
        Assert.Equal(c.Active.Count(), now.AutoBidsDecided);
        Assert.Equal(s.Seeds.OrderBy(kv => kv.Key), now.Seeds.OrderBy(kv => kv.Key));
        Assert.Equal(s.LastFourIn, now.LastFourIn);
        Assert.Equal(s.FirstFourOut, now.FirstFourOut);
        var final = c.Bracketology();
        Assert.True(final.Final);
        Assert.Equal(s.SelectionOrder, final.Field);
        Assert.Equal(CollegeWorld.FieldSize, final.Field.Count);
        // Every team is in a region; the coach got one Selection Sunday message.
        Assert.All(s.Seeds.Keys, id => Assert.InRange(c.RegionOf(id), 0, 3));
        var news = c.CoachNews.Where(n => n.Title.StartsWith("Selection Sunday")).ToList();
        Assert.Single(news);
        if (s.Seeds.TryGetValue(me, out var seed))
        {
            Assert.Equal($"Selection Sunday: we're a No. {seed} seed", news[0].Title);
            Assert.Contains($"{MadnessBracket.Regions[c.RegionOf(me)]} region", news[0].Body);
            Assert.Contains(seed == 16 ? "First Four" : "Round of 64", news[0].Body);
        }
        else Assert.Equal("Selection Sunday: we're left out", news[0].Title);
        // The public news names the bubble.
        Assert.Contains(c.News, n => n.Item1 == "March Madness: the field is set" && n.Item2.Contains("First four out"));
    }

    [Fact]
    public void A_program_left_out_hears_where_it_stood()
    {
        var (c, sim, rng) = NewWorld(43);
        c.PlayThrough(new DateOnly(2027, 3, 1), sim, rng);
        var p = c.Bracketology();
        // The weakest program in the country: no conference lead, no chance.
        int me = c.Programs.Select(t => t.Id).Where(id => !p.In(id)).OrderBy(id => c.Season.Elo[id]).First();
        c.ManagedProgramId = me;
        c.PlayThrough(new DateOnly(2027, 3, 15), sim, rng);
        Assert.True(c.Season.Selected);
        if (c.Season.Seeds.ContainsKey(me)) return; // a conference tournament miracle: nothing to check
        var news = Assert.Single(c.CoachNews, n => n.Title.StartsWith("Selection Sunday"));
        Assert.Equal("Selection Sunday: we're left out", news.Title);
        Assert.Contains("weren't near the bubble", news.Body);
        Assert.Equal("Out", c.Bracketology().StatusOf(me));
    }
}
