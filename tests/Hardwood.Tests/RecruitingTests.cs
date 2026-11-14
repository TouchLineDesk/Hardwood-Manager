using Hardwood.Core;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Senior recruiting (decision #157, college depth): court seniors, they commit, they sign on graduation day.</summary>
public class RecruitingTests
{
    private static GameSession Season(ulong seed, string? path = null)
    {
        var probe = Hardwood.Core.RealWorld.RealWorldData.NewWorld(seed).League;
        var id = probe.Teams.OrderByDescending(t => Hardwood.Core.World.QuickSim.Strength(t)).ElementAt(3).Id;
        var s = GameSession.NewGame(seed, id, path);
        while (s.Today < new DateOnly(2026, 12, 1)) s.AdvanceDay();
        return s;
    }

    [Fact]
    public void No_recruiting_note_reaches_a_pro_inbox()
    {
        // D45 C (Danny, #413): no senior-recruiting introduction at all; D48 B: real careers draft seniors instead.
        var first = Season(71);
        Assert.False(first.RecruitingOpen);
        Assert.DoesNotContain(first.Inbox, m => m.Title is "Recruit college seniors for your club" or "Senior recruiting is open");
    }

    [Fact]
    public void A_real_career_drafts_instead_of_recruiting_seniors()
    {
        // D48 B (Danny, #429): no senior recruiting in real careers; nobody commits, and the whole class goes to the draft.
        var probe = GameSession.NewGame(71, 1, null);
        int id = probe.League.Teams.OrderByDescending(t => Hardwood.Core.World.QuickSim.Strength(t)).ElementAt(3).Id;
        var s = GameSession.NewGame(71, id, null);
        while (s.Today < new DateOnly(2027, 2, 1)) s.AdvanceDay();
        Assert.False(s.SeniorRecruiting);
        Assert.False(s.RecruitingOpen);
        Assert.StartsWith("College seniors turn pro through the draft", s.CallRecruit(s.Seniors()[10]));
        Assert.DoesNotContain(s.Recruiting?.Prospects.Values ?? Enumerable.Empty<Hardwood.Core.College.RecruitState>(), st => st.CommittedTeamId != null);
    }

    // The senior-recruiting tests (courting, commitments, roster spots, the board) went with the made-up world in v0.74.0
    // (D61 B, #478): since D48 B a real career drafts college seniors, so only the made-up world recruited them.
}
