using Hardwood.Core;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.People;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>W3 (run 231, `claude/world-stories-plan.md`): years into a save, sons of retired generated players appear in the
/// new classes with the father's surname and appearance, a career line and a stated likeness; one inbox message a summer;
/// kept through a save. No random draw and no change to ability.</summary>
public class Run231SecondGenerationTests
{
    private readonly ITestOutputHelper _out;
    public Run231SecondGenerationTests(ITestOutputHelper output) => _out = output;

    private static readonly string[] Firsts = { "Thaddeus", "Corwin", "Lysander", "Ambrose", "Barnaby", "Evander", "Fitzgerald", "Osric", "Percival", "Quillon" };
    private static readonly string[] Lasts = { "Quillbrook", "Marrowfield", "Tenneby", "Ashgrove", "Wexlowe", "Hollingtree", "Brackwater", "Pellridge", "Stonecrop", "Varnham", "Oakhollow", "Thistlewood" };

    /// <summary>Retired generated fathers with a career begun in the save (rookie 2026 or later).</summary>
    private static List<Player> AddFathers(League league, int count, int birthYear, int rookie = 2026, int games = 700, string nat = "USA")
    {
        var list = new List<Player>();
        for (int i = 0; i < count; i++)
        {
            var f = new Player
            {
                Id = league.NextPlayerId++, FirstName = Firsts[i % Firsts.Length], LastName = Lasts[i % Lasts.Length] + (i >= Lasts.Length ? $"-{i}" : ""),
                BirthDate = new DateOnly(birthYear + i % 4, 3, 1 + i % 20), Nationality = nat, HeightCm = 200 + i % 12, WingspanCm = 210, WeightKg = 100,
                PrimaryPosition = (Position)(i % 5), CurrentAbility = 120, PotentialAbility = 130, RookieSeason = rookie, Ethnicity = Ethnicity.Caucasian,
            };
            league.Retired.Add(f);
            league.Careers[f.Id] = new CareerTotals { PlayerId = f.Id, Seasons = 11, Games = games, Points = games * 17, Titles = i % 3, AllStars = i % 4, Mvps = i % 7 == 0 ? 1 : 0, GamesByTeam = { [7] = games } };
            list.Add(f);
        }
        return list;
    }

    private static List<Player> Class(int count, int summerYear, int age, ulong seed)
    {
        var gen = new PlayerGenerator(new Rng(seed), 50_000);
        var us = Heritages.All.First(h => h.Key == "us");
        return Enumerable.Range(0, count).Select(i => gen.Generate((Position)(i % 5), 30 + i % 20, age, new DateOnly(summerYear, 7, 1), us, "USA")).ToList();
    }

    private static League NewLeague() { var l = new League { Name = "Test", Season = 2045 }; l.Records.FirstSeason = 2026; return l; }

    [Fact]
    public void Sons_take_the_fathers_name_and_look_and_nothing_else_moves()
    {
        var league = NewLeague();
        var fathers = AddFathers(league, 80, 2004);
        var book = new LineageBook();
        int found = 0;
        for (int year = 2040; year <= 2056; year++)
        {
            var cohort = Class(150, year, 14, (ulong)year);
            var before = cohort.ToDictionary(p => p.Id, p => (p.CurrentAbility, p.PotentialAbility, p.FirstName, p.HeightCm, p.PrimaryPosition));
            var sons = Lineage.Link(league, book, cohort, year, "high school", 31, id => id == 7 ? "Boston Shamrocks" : null);
            Assert.True(sons.Count <= Lineage.MaxPerSummer);
            foreach (var s in sons)
            {
                var son = cohort.Single(p => p.Id == s.SonId);
                var dad = fathers.Single(f => f.Id == s.FatherId);
                Assert.Equal(dad.LastName, son.LastName.Replace(" Jr.", ""));
                Assert.Equal(dad.Ethnicity, son.Ethnicity);
                Assert.InRange(son.BirthDate.Year - dad.BirthDate.Year, Lineage.MinGap, Lineage.MaxGap);
                var was = before[son.Id];
                Assert.Equal((was.CurrentAbility, was.PotentialAbility, was.FirstName, was.HeightCm, was.PrimaryPosition),
                             (son.CurrentAbility, son.PotentialAbility, son.FirstName, son.HeightCm, son.PrimaryPosition));
                Assert.Contains("mostly with the Boston Shamrocks", s.FatherCareer);
                Assert.Contains("his father", s.Likeness);
                _out.WriteLine($"{year}: {son.Name} ({son.PrimaryPosition}) — {Lineage.SonLine(s)}");
            }
            // Everyone else in the class keeps his name.
            Assert.All(cohort.Where(p => sons.All(s => s.SonId != p.Id)), p => Assert.Equal(before[p.Id].FirstName, p.FirstName));
            found += sons.Count;
        }
        Assert.InRange(found, 3, 40);
        Assert.Equal(book.Sons.Count, book.Sons.Select(s => s.FatherId).Distinct().Count()); // one son per father
        Assert.Equal(book.Sons.Count, book.Sons.Select(s => s.SonId).Distinct().Count());
        _out.WriteLine($"{found} sons over 17 summers from {fathers.Count} fathers");
    }

    [Fact]
    public void Only_generated_fathers_whose_careers_began_here_qualify_and_the_draw_is_stable()
    {
        var league = NewLeague();
        AddFathers(league, 40, 2004, rookie: 2026, games: 100);            // too short a career
        var old = AddFathers(league, 40, 2004, rookie: 2020);             // a career from before the save
        foreach (var p in old) p.RookieSeason = null;                     // and one generated already in the league
        AddFathers(league, 40, 1975);                                     // too old to have a 14-year-old
        var real = new Player { Id = league.NextPlayerId++, FirstName = "LeBron", LastName = "James", BirthDate = new DateOnly(2004, 12, 30), RookieSeason = 2026, Nationality = "USA" };
        league.Retired.Add(real);
        league.Careers[real.Id] = new CareerTotals { PlayerId = real.Id, Seasons = 20, Games = 1500, Mvps = 4, Titles = 4 };
        Assert.False(Lineage.CanBeFather(league, real, 2026));
        var book = new LineageBook();
        for (int year = 2040; year <= 2060; year++)
            Assert.Empty(Lineage.Link(league, book, Class(120, year, 14, (ulong)year), year, "high school", 9, _ => null));

        // Deterministic: the same save finds the same sons in the same recruits.
        string Run(ulong seed)
        {
            var l = NewLeague(); AddFathers(l, 80, 2004);
            var b = new LineageBook();
            var names = new List<string>();
            for (int y = 2040; y <= 2050; y++)
            {
                var c = Class(150, y, 14, (ulong)y);
                names.AddRange(Lineage.Link(l, b, c, y, "high school", seed, _ => null).Select(s => $"{y}:{s.FatherId}:{c.Single(p => p.Id == s.SonId).Name}"));
            }
            return string.Join("|", names);
        }
        Assert.Equal(Run(31), Run(31));
        Assert.NotEqual(Run(31), Run(77));
    }

    [Fact]
    public void A_new_name_keeps_the_generators_rules()
    {
        // The review: a son must not become a real player ("LeBron" + a father named James) or a living player's double.
        int Found(string a, string b)
        {
            var league = NewLeague();
            foreach (var d in AddFathers(league, 30, 2004)) d.LastName = "James";
            var living = new NameBook();
            living.Add("Marcus", "James");
            var book = new LineageBook();
            for (int year = 2040; year <= 2056; year++)
            {
                var cohort = Class(40, year, 14, (ulong)year);
                for (int i = 0; i < cohort.Count; i++)
                    cohort[i] = new Player { Id = cohort[i].Id, FirstName = i % 2 == 0 ? a : b, LastName = "Smith", BirthDate = cohort[i].BirthDate,
                                             Nationality = "USA", PrimaryPosition = (Position)(i % 5), HeightCm = 190, PotentialAbility = 100 - i };
                Lineage.Link(league, book, cohort, year, "high school", 31, _ => null, living);
            }
            return book.Sons.Count;
        }
        Assert.Equal(0, Found("LeBron", "Marcus"));
        Assert.True(Found("Darius", "Marcus") > 0); // the same summers do find sons when the name is free
        var jr = new Player { FirstName = "Tom", LastName = "Ashgrove Jr." };
        Assert.Equal("Ashgrove III", Lineage.SurnameFor(new Player { FirstName = "Tom", LastName = "X" }, jr));
        Assert.Equal("Ashgrove Jr.", Lineage.SurnameFor(new Player { FirstName = "Ned", LastName = "X" }, jr));
        Assert.Equal("Ashgrove Jr.", Lineage.SurnameFor(new Player { FirstName = "Tom", LastName = "X" }, new Player { FirstName = "Tom", LastName = "Ashgrove" }));
    }

    [Fact]
    public void A_careers_summer_finds_sons_posts_one_message_and_a_reload_keeps_them()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hw-run231-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "career.hwsave");
        try
        {
            var (probe, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(31, 2026);
            int club = probe.Teams.OrderBy(t => QuickSim.Strength(t)).ElementAt(14).Id;
            var s = GameSession.NewGame(31, club, savePath: null);
            s.Job.Current!.TargetWins = 0;
            // A save 18 years on, in miniature: retired players whose careers began here and whose sons are 14 next summer.
            var fathers = AddFathers(s.League, 90, 1982, rookie: s.League.Records.FirstSeason > 0 ? s.League.Records.FirstSeason : 2026);
            int guard = 0;
            while (s.Phase != SessionPhase.Offseason && guard++ < 600) s.AdvanceDay();
            Assert.Equal(SessionPhase.Offseason, s.Phase);

            var sons = s.Lineage.Sons;
            Assert.InRange(sons.Count, 1, Lineage.MaxPerSummer);
            var msg = Assert.Single(s.Inbox, m => m.Title == "Second generation");
            _out.WriteLine(msg.Body);
            foreach (var son in sons)
            {
                var p = Assert.IsType<Player>(s.AnyPlayer(son.SonId));
                Assert.Contains(s.World.College!.Prospects, x => x.Id == p.Id);
                Assert.Equal(fathers.Single(f => f.Id == son.FatherId).LastName, p.LastName.Replace(" Jr.", ""));
                Assert.Contains($"{p.Name} (", msg.Body);
                Assert.Contains("high-school class of", msg.Body);
                Assert.StartsWith("Son of ", s.LineageLine(p));
                Assert.StartsWith("Father of ", s.LineageLine(fathers.Single(f => f.Id == son.FatherId)));
            }

            s.Save = new Hardwood.Core.Persistence.SaveFile(path);
            s.SaveNow();
            var back = GameSession.Load(path);
            Assert.Equal(sons.Select(x => $"{x.SonId}:{x.FatherId}:{x.FatherCareer}:{x.Likeness}"), back.Lineage.Sons.Select(x => $"{x.SonId}:{x.FatherId}:{x.FatherCareer}:{x.Likeness}"));
            Assert.All(sons, x => Assert.Equal(s.AnyPlayer(x.SonId)!.Name, back.AnyPlayer(x.SonId)!.Name));
            Assert.Contains(s.League.Season + 1, back.Lineage.Summers);
            back.AdvanceOffseason();
            Assert.Single(back.Inbox, m => m.Title == "Second generation"); // the summer isn't searched twice
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
