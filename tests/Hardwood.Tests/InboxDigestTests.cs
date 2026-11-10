using Hardwood.Core.Season;
using Xunit;

namespace Hardwood.Tests;

/// <summary>The weekly world digest (critic review, Nov 10): world news no longer floods the inbox.</summary>
public class InboxDigestTests
{
    [Fact]
    public void World_news_arrives_once_a_week_and_survives_a_reload()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-digest-{Guid.NewGuid():N}.hwsave");
        try
        {
            var s = GameSession.NewGame(2026, 5, path);
            var start = s.Today;
            while (s.Today < start.AddDays(60)) s.AdvanceDay();
            Assert.InRange(s.Inbox.Count(m => m.Category == "World"), 0, 12);
            s.PostWorld("First headline", "A club far away won something. Details follow.");
            s.PostWorld($"{s.HumanTeam.FullName} linked with a star", "Rumours.");   // about your club: straight in
            Assert.Contains(s.Inbox, m => m.Title.Contains("linked with a star"));
            var mark = s.Today;
            while (s.Today < mark.AddDays(8)) s.AdvanceDay();
            var digest = s.Inbox.First(m => m.Title.StartsWith("Around the world"));
            Assert.Contains("• First headline: A club far away won something.", digest.Body);
            Assert.Empty(s.WorldDigest);
            s.PostWorld("Test headline", "Something happened somewhere. More detail.");
            Assert.Contains(s.WorldDigest, d => d.Title == "Test headline" && d.Line == "Something happened somewhere.");
            s.SaveNow();
            var loaded = GameSession.Load(path);
            Assert.Contains(loaded.WorldDigest, d => d.Title == "Test headline");
            loaded.MarkAllRead();
            Assert.All(loaded.Inbox, m => Assert.True(m.Read));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
