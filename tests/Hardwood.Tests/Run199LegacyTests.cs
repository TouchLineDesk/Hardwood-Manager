using System.Text.Json;
using Hardwood.Core;
using Hardwood.Core.College;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Season;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>Slice 8 of the revised launch plan (run 199): program legacy: the record book, the Hall of Fame and the rafters.</summary>
public class Run199LegacyTests
{
    private readonly ITestOutputHelper _out;
    public Run199LegacyTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void The_night_of_the_national_final_every_program_counts_its_season_and_its_best_graduates_are_honoured()
    {
        var rng = new Rng(41);
        var gen = new PlayerGenerator(new Rng(41 ^ 0x51), 2_000_000);
        var c = CollegeWorld.Generate(gen, rng, new DateOnly(2026, 10, 1));
        var sim = new GameSimulator();
        c.PlayThrough(new DateOnly(2027, 3, 20), sim, rng);
        Assert.Null(c.Season.ChampionId);
        Assert.Empty(c.Legacy);
        c.PlayThrough(new DateOnly(2027, 4, 20), sim, rng);
        Assert.NotNull(c.Season.ChampionId);

        Assert.Equal(c.Programs.Count, c.Legacy.Count);
        int year = c.Season.Year;
        foreach (var book in c.Legacy)
        {
            Assert.Equal(year, book.LastYear);
            Assert.Equal(year, book.FirstYear);
            var t = c.Program(book.TeamId)!;
            // A current player's totals are his season's line.
            foreach (var cr in book.Careers.Where(x => !x.Done))
            {
                var st = c.Season.Stats[cr.PlayerId];
                Assert.Equal((st.G, st.Pts, st.Reb, st.Ast), (cr.G, cr.Pts, cr.Reb, cr.Ast));
            }
            // The seniors' careers are closed; nobody else's is.
            var seniors = c.FinalYear(t).Select(p => p.Id).ToHashSet();
            Assert.All(book.Careers, cr => Assert.Equal(seniors.Contains(cr.PlayerId) || !t.Roster.Any(p => p.Id == cr.PlayerId), cr.Done));
            // Every Hall member was a senior here, earned it, and only the very best are in the rafters, each number once.
            Assert.All(book.Hall, h =>
            {
                Assert.Contains(h.PlayerId, seniors);
                Assert.True(h.Score >= ProgramLegacy.HallScore);
                Assert.Equal(h.Score >= ProgramLegacy.JerseyScore, h.Jersey != null);
                Assert.NotEmpty(h.Why);
                Assert.Equal(year + 1, h.Class);
            });
            // A retired number is the one on his card.
            Assert.All(book.Hall.Where(h => h.Jersey != null), h => Assert.Equal(Hardwood.Core.Portraits.JerseySpec.NumberFor(h.PlayerId), h.Jersey));
            // The book keeps the top of each column.
            Assert.True(book.Seasons.Count <= 3 * ProgramLegacy.Keep);
        }
        var hall = c.Legacy.SelectMany(b => b.Hall).ToList();
        int seniorsAll = c.Programs.Sum(t => c.FinalYear(t).Count);
        _out.WriteLine($"{seniorsAll} seniors; {hall.Count} in a Hall of Fame ({hall.Count(h => h.Jersey != null)} jerseys retired) at {hall.Select(h => h.PlayerId).Distinct().Count()} players");
        foreach (var h in hall.OrderByDescending(h => h.Score).Take(5))
            _out.WriteLine($"  {h.Score,5:0.0} {h.Name} ({c.Program(c.Legacy.First(b => b.Hall.Contains(h)).TeamId)!.FullName}) No.{h.Jersey?.ToString() ?? "-"}: {string.Join("; ", h.Why)}");
        Assert.InRange(hall.Count, 1, seniorsAll / 5); // an honour, not a graduation present
        var top = hall.OrderByDescending(h => h.Score).First();
        var topProgram = c.Program(c.Legacy.First(b => b.Hall.Contains(top)).TeamId)!;
        var honours = c.HonoursOf(top.PlayerId);
        Assert.Contains($"{topProgram.FullName} Hall of Fame, class of {year + 1}", honours);
        if (top.Jersey != null) Assert.Contains($"No. {top.Jersey} retired by {topProgram.FullName}", honours);
        Assert.DoesNotContain("Hall of Fame", honours[0]); // his awards first (the farewell shows four)
        if (hall.Any(h => h.Jersey != null)) Assert.Contains(c.News, n => n.Title.StartsWith("Into the rafters"));

        // Once a night: playing on changes nothing, and the books round-trip through JSON (the save).
        var before = JsonSerializer.Serialize(c.Legacy);
        c.PlayThrough(new DateOnly(2027, 4, 30), sim, rng);
        Assert.Equal(before, JsonSerializer.Serialize(c.Legacy));
        var back = JsonSerializer.Deserialize<List<ProgramLegacy>>(before)!;
        Assert.Equal(before, JsonSerializer.Serialize(back));
        _out.WriteLine($"books: {before.Length / 1024} KB of JSON for {c.Legacy.Count} programs");
    }

    [Fact]
    public void A_retired_number_is_the_one_he_wore_and_spans_read_as_seasons()
    {
        Assert.Equal(Hardwood.Core.Portraits.JerseySpec.NumberFor(12345), ProgramLegacy.JerseyFor(12345));
        Assert.Equal("2026-27", ProgramLegacy.Span(2026, 2026));
        Assert.Equal("2026-30", ProgramLegacy.Span(2026, 2029));
    }
}
