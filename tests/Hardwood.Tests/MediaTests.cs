using Hardwood.Core;
using Hardwood.Core.Generation;
using Hardwood.Core.People;
using Hardwood.Core.Season;
using Xunit;

namespace Hardwood.Tests;

/// <summary>People part 2: press conferences and locker-room moments, with answers whose effects you can read first.</summary>
public class MediaTests
{
    private static GameSession Session(ulong seed, int strengthRank, string? path = null)
    {
        var probe = new WorldGenerator(new Rng(seed)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));
        var id = probe.Teams.OrderByDescending(t => Hardwood.Core.World.QuickSim.Strength(t)).ElementAt(strengthRank).Id;
        return GameSession.NewGame(seed, id, path);
    }

    private static PendingDecision? PlayUntilMoment(GameSession s, DateOnly until)
    {
        while (s.Today < until)
        {
            s.AdvanceDay();
            if (s.OpenDecisions.FirstOrDefault() is { } d) return d;
        }
        return null;
    }

    [Fact]
    public void A_moment_arrives_in_the_inbox_with_answers_and_the_answer_does_what_it_says()
    {
        var s = Session(91, 28);
        var d = PlayUntilMoment(s, new DateOnly(2027, 1, 15));
        Assert.NotNull(d);
        Assert.InRange(d!.Options.Count, 2, 3);
        Assert.All(d.Options, o => Assert.False(string.IsNullOrWhiteSpace(o.Hint)));
        var msg = s.Inbox.Single(m => m.Id == d.MessageId);
        Assert.Contains(d.Text, msg.Body);
        Assert.Contains(d.Options[d.SafeOption].Label, msg.Body);
        // The assistant points at it.
        var advice = Hardwood.Core.Guide.Assistant.Advise(s, "Home", Hardwood.Core.Guide.GuideLevel.Auto, new Hardwood.Core.Guide.GuideProgress());
        Assert.Contains(advice.Tasks, t => t.Key == $"decision-{d.Id}" && t.Page == "Inbox");

        double before = s.HumanTeam.Roster.Average(p => p.Morale);
        double chem = s.HumanTeam.Chemistry;
        var reply = s.Answer(d.Id, d.SafeOption);
        Assert.StartsWith("You answered", reply);
        Assert.False(d.Open);
        Assert.Equal(reply, s.Answer(d.Id, 0)); // a second answer changes nothing
        Assert.DoesNotContain("heard that one", reply);
        Assert.Empty(s.OpenDecisions.Where(x => x.Id == d.Id));
        // Effects are bounded: no answer swings the squad by more than a few points or chemistry at all on the spot.
        Assert.InRange(s.HumanTeam.Roster.Average(p => p.Morale) - before, -6, 6);
        Assert.Equal(chem, s.HumanTeam.Chemistry);
    }

    [Fact]
    public void Unanswered_moments_get_the_safe_answer_and_a_season_has_a_handful()
    {
        var s = Session(92, 15);
        while (s.Phase != SessionPhase.Postseason && s.Phase != SessionPhase.Offseason) s.AdvanceDay();
        var mine = s.Media.Decisions.Where(d => d.TeamId == s.HumanTeam.Id).ToList();
        Assert.InRange(mine.Count(d => d.Kind == "press"), 2, 16);
        Assert.InRange(mine.Count(d => d.Kind == "locker"), 1, 9);
        // Nobody answered, so the assistant did, a week after each one.
        foreach (var d in mine.Where(d => !d.Open))
        {
            Assert.True(d.Auto);
            Assert.Equal(d.SafeOption, d.Chosen);
            Assert.StartsWith("Your assistant answered", d.Outcome);
        }
        Assert.True(s.Media.AutoAnswered >= mine.Count - 2);
        // Moments ride on the weekly results message; they add a message of their own only in a week without results.
        Assert.True(mine.Count(d => s.Inbox.Any(m => m.Id == d.MessageId && m.Title.StartsWith("This week"))) >= mine.Count - 2);
    }

    [Fact]
    public void Team_spirit_from_an_answer_lifts_chemistry_while_it_lasts()
    {
        var s = Session(93, 10);
        PendingDecision? d;
        // The first moment with an answer that leaves spirit behind (others get the safe answer).
        while ((d = PlayUntilMoment(s, new DateOnly(2027, 3, 1))) != null && !d.Options.Any(o => o.Hint.Contains("pirit up")))
            s.Answer(d.Id, d.SafeOption);
        Assert.NotNull(d);
        s.Media.Spirit.Clear();
        int pick = Enumerable.Range(0, d!.Options.Count).First(i => d.Options[i].Hint.Contains("pirit up"));
        s.Answer(d.Id, pick);
        var spirit = s.SpiritFactors();
        Assert.Single(spirit);
        Assert.True(spirit[0].effect > 0);
        var effect = s.Media.Spirit.Single();
        Assert.True(effect.Until > s.Today.AddDays(13));
    }

    [Fact]
    public void The_desk_survives_a_save()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-media-{Guid.NewGuid():N}.hwsave");
        try
        {
            var s = Session(94, 28, path);
            var d = PlayUntilMoment(s, new DateOnly(2027, 1, 15));
            Assert.NotNull(d);
            s.SaveNow();
            var loaded = GameSession.Load(path);
            var back = loaded.Media.Find(d!.Id);
            Assert.NotNull(back);
            Assert.True(back!.Open);
            Assert.Equal(d.Options.Select(o => o.Label), back.Options.Select(o => o.Label));
            Assert.Equal(d.MessageId, back.MessageId);
            Assert.StartsWith("You answered", loaded.Answer(back.Id, 0));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
