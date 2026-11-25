using Hardwood.Core.College;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>Slice 10 of the revised launch plan (#1164, run 201): your coaching legacy (the book of your seasons, milestones),
/// your coaching tree, and coaches in their programs' Halls of Fame.</summary>
public class Run201CoachingLegacyTests
{
    private readonly ITestOutputHelper _out;
    public Run201CoachingLegacyTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void The_coaches_hall_takes_a_long_winning_spell_or_a_title()
    {
        Assert.True(CoachingLegacy.HallWorthy(12, 250, 130, 6, 0));
        Assert.False(CoachingLegacy.HallWorthy(12, 200, 180, 6, 0));  // not winning enough
        Assert.False(CoachingLegacy.HallWorthy(9, 200, 80, 6, 0));    // not long enough
        Assert.False(CoachingLegacy.HallWorthy(12, 250, 130, 4, 0));  // too few bids
        Assert.True(CoachingLegacy.HallWorthy(5, 100, 60, 3, 1));     // a title
        Assert.False(CoachingLegacy.HallWorthy(4, 100, 30, 4, 1));
        Assert.Equal("312-140 in 14 seasons, 9 NCAA bids, 1 national title", CoachingLegacy.CoachLine(14, 312, 140, 9, 1));
    }

    [Fact]
    public void Milestones_are_counted_from_the_book_and_this_season()
    {
        var book = new CoachBook();
        Assert.Empty(CoachingLegacy.Reached(book, "Alex Morgan", "Duke", 0, 0, -1, 0));
        var first = CoachingLegacy.Reached(book, "Alex Morgan", "Duke", 1, 0, -1, 0).Select(x => x.Key).ToList();
        Assert.Equal(new[] { "win-1" }, first);
        book.Seasons.Add(new CoachSeason { Year = 2026, Wins = 70, Losses = 30, Seed = 4, Reached = 5 });
        book.Seasons.Add(new CoachSeason { Year = 2027, Wins = 30, Losses = 5, Seed = 1, Reached = 7 });
        var keys = CoachingLegacy.Reached(book, "Alex Morgan", "Duke", 100, 2, -1, book.Bids).Select(x => x.Key).ToList();
        Assert.Equal(new[] { "win-1", "win-100", "bid-1", "march-win", "final-four", "title-1" }, keys);
        // This season's March counts as it's played.
        var live = CoachingLegacy.Reached(new CoachBook(), "A", "B", 25, 0, 2, 0).Select(x => x.Key).ToList();
        Assert.Contains("march-win", live);
        Assert.DoesNotContain("final-four", live);
        Assert.Contains(Enumerable.Range(2026, 30), y => CoachingLegacy.AssistantReady(Asst(7, 20), y, 2));
        Assert.False(CoachingLegacy.AssistantReady(Asst(7, 20), 2030, 1));   // too soon together
        Assert.False(CoachingLegacy.AssistantReady(Asst(7, 10), 2030, 5));   // not good enough
    }

    private static Staff Asst(int id, int coaching) => new() { Id = id, FirstName = "Sam", LastName = "Reed", Role = StaffRole.AssistantCoach, Coaching = coaching };

    /// <summary>A real world (seed 9, George Mason), three springs: every season is in the book, the first win is told once,
    /// a ready assistant leaves for a head job (the tree), and the book survives a reload.</summary>
    [Fact]
    public void Three_springs_fill_the_book_grow_the_tree_and_survive_a_reload()
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(9, 2026, GameSession.DefaultFullSim);
        var me = world.College!.Programs.First(t => t.FullName.StartsWith("George Mason"));
        var dir = Path.Combine(Path.GetTempPath(), $"hw-run201-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "career.hwsave");
        try
        {
            var s = GameSession.NewGame(9, me.Id, path);
            s.Job.Current!.TargetWins = 0; // keep the job through three springs
            var cw = s.World.College!;
            Assert.Empty(cw.MyCareer.Seasons);
            // An assistant who is ready in spring 2028 (after two seasons together).
            int id = Enumerable.Range(900_000, 1000).First(i => CoachingLegacy.AssistantReady(Asst(i, 18), 2028, 2));
            s.HumanTeam.Staff.Add(new Staff { Id = id, FirstName = "Treeby", LastName = "Branchwell", Role = StaffRole.AssistantCoach, Coaching = 18, TeamId = s.HumanTeam.Id, BirthDate = new DateOnly(1985, 1, 1) });
            var end = new DateOnly(2029, 6, 1);
            while (s.Today < end && s.Employed)
            {
                if (s.Phase == SessionPhase.Offseason) s.AdvanceOffseason(); else s.AdvanceDay();
            }
            Assert.True(s.Employed);
            var book = cw.MyCareer;
            foreach (var x in book.Seasons) _out.WriteLine($"{x.Year}: {x.Program} {x.Wins}-{x.Losses}, seed {x.Seed}, reached {x.Reached}");
            foreach (var m in book.Milestones) _out.WriteLine($"{m.Date}: {m.Text}");
            foreach (var b in book.Tree) _out.WriteLine($"Tree: {b.Name} -> {b.Program} ({b.Year})");
            Assert.Equal(new[] { 2026, 2027, 2028 }, book.Seasons.Select(x => x.Year).ToArray());
            Assert.All(book.Seasons, x => Assert.Equal(me.Id, x.ProgramId));
            Assert.Equal(1, s.Inbox.Count(m => m.Title == "Your first college win"));
            Assert.Equal(book.Milestones.Count, book.Milestones.Select(m => m.Key).Distinct().Count());
            // The tree: the ready assistant left for a head job no bigger than yours, and is on that bench now.
            var branch = Assert.Single(book.Tree);
            Assert.Equal("Treeby Branchwell", branch.Name);
            Assert.Equal(2028, branch.Year);
            Assert.DoesNotContain(s.HumanTeam.Staff, x => x.Id == id);
            Assert.Equal(branch.Name, s.FindTeam(branch.ProgramId)!.CoachName);
            Assert.NotNull(cw.BenchNamed(branch.Name));
            Assert.Contains(s.Carousel, m => m.FromStaffOf != null && m.In == branch.Name);
            Assert.Contains(s.Inbox, m => m.Title.StartsWith("Your assistant Treeby Branchwell takes"));
            Assert.Contains(book.Milestones, m => m.Key == "tree-1");
            // Coaches who left after long, winning spells are in their programs' halls.
            var coaches = cw.Legacy.SelectMany(l => l.Hall.Where(h => h.IsCoach).Select(h => (cw.Program(l.TeamId)!.FullName, h))).ToList();
            foreach (var (p, h) in coaches) _out.WriteLine($"Hall: {p}: {h.Name} ({h.Years}) {h.Line}");
            Assert.All(coaches, x => Assert.DoesNotContain(x.h.Name, cw.LegacyHonoursOf(0)));

            s.SaveNow();
            string before = System.Text.Json.JsonSerializer.Serialize(book);
            var back = GameSession.Load(path);
            Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(back.World.College!.MyCareer));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    /// <summary>You leave a program you built: after a title there, you join its Hall of Fame (once).</summary>
    [Fact]
    public void A_coach_who_leaves_after_a_title_joins_the_hall_once()
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(9, 2026, GameSession.DefaultFullSim);
        var cw = world.College!;
        var p = cw.Programs[3];
        var who = cw.InductCoach(p.Id, "Pat Long", 2015, 2027, 13, 280, 130, 8, 1, 2028);
        Assert.NotNull(who);
        Assert.True(who!.IsCoach);
        Assert.Equal("280-130 in 13 seasons, 8 NCAA bids, 1 national title", who.Line);
        Assert.Equal("2015-28", who.Years);
        Assert.Null(cw.InductCoach(p.Id, "Pat Long", 2015, 2027, 13, 280, 130, 8, 1, 2028));
        Assert.Null(cw.InductCoach(p.Id, "Short Stay", 2025, 2027, 3, 70, 30, 2, 0, 2028));
    }
}
