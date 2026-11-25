using Hardwood.Core.College;
using Hardwood.Core.Persistence;
using Hardwood.Core.Season;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>Run 199 in a career: three college seasons at a blue blood. The books fill, the Hall grows each spring, the coached
/// program's class reaches the inbox, and a save keeps every book.</summary>
public class Run199LegacySessionTests
{
    private readonly ITestOutputHelper _out;
    public Run199LegacySessionTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void Three_seasons_build_a_history_and_a_reload_keeps_it()
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(31, 2026, GameSession.DefaultFullSim);
        var me = world.College!.Programs.First(t => t.FullName.StartsWith("Duke"));
        var dir = Path.Combine(Path.GetTempPath(), $"hw-run199-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "career.hwsave");
        try
        {
            var s = GameSession.NewGame(31, me.Id, path);
            var end = new DateOnly(2029, 4, 20);
            int seen = 0;
            while (s.Today < end && s.Employed)
            {
                if (s.Phase == SessionPhase.Offseason) s.AdvanceOffseason(); else s.AdvanceDay();
                var c = s.World.College!;
                int classes = c.Legacy.SelectMany(b => b.Hall).Select(h => h.Class).Distinct().Count();
                if (classes > seen)
                {
                    seen = classes;
                    int cls = c.Legacy.SelectMany(b => b.Hall).Max(h => h.Class);
                    var now = c.Legacy.SelectMany(b => b.Hall).Where(h => h.Class == cls).ToList();
                    var mine = c.LegacyOf(s.HumanTeam.Id);
                    _out.WriteLine($"class of {cls}: {now.Count} inducted, {now.Count(h => h.Jersey != null)} jerseys; {s.HumanTeam.FullName}: {mine.Hall.Count(h => h.Class == cls)} (book: {mine.Careers.Count} careers, {mine.Seasons.Count} seasons)");
                    foreach (var h in now.OrderByDescending(h => h.Score).Take(3)) _out.WriteLine($"   {h.Score,5:0.0} {h.Name}: {string.Join("; ", h.Why.Take(3))}");
                    if (mine.Hall.Any(h => h.Class == cls))
                        Assert.Contains(s.Inbox, m => m.Date == s.Today && (m.Title.Contains("Hall of Fame") || m.Title.StartsWith("Into the rafters")));
                }
            }
            Assert.True(seen >= 2, $"classes {seen}");
            var col = s.World.College!;
            var book = col.LegacyOf(s.HumanTeam.Id);
            Assert.Equal(2026, book.FirstYear);
            Assert.True(book.Careers.Any(c => c.Done && c.First == 2026 && c.Last == 2027 || c.Last > c.First), "a career spans seasons");
            var hall = col.Legacy.SelectMany(b => b.Hall).ToList();
            _out.WriteLine($"after {seen} springs: {hall.Count} Hall of Famers, {hall.Count(h => h.Jersey != null)} jerseys retired; Duke's leader {book.Leaders(c => c.Pts, 1).FirstOrDefault()?.Name} {book.Leaders(c => c.Pts, 1).FirstOrDefault()?.Pts}");

            s.SaveNow();
            string before = System.Text.Json.JsonSerializer.Serialize(col.Legacy);
            _out.WriteLine($"books {before.Length / 1024} KB; save {new FileInfo(path).Length / 1024} KB");
            var back = GameSession.Load(path);
            Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(back.World.College!.Legacy));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
