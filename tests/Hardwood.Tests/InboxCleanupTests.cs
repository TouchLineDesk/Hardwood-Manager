using Hardwood.Core;
using Hardwood.Core.Generation;
using Hardwood.Core.People;
using Hardwood.Core.Season;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// The inbox clean-up (Danny, #243, D4): delete a message, all read, or a whole filter; an optional weekly auto-clear;
/// questions you haven't answered can't be deleted; ids are never reused, so "messages created" stays honest.
/// </summary>
public class InboxCleanupTests
{
    private static GameSession Started(ulong seed, string? path = null)
    {
        var probe = new WorldGenerator(new Rng(seed)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));
        var id = probe.Teams.OrderByDescending(t => Hardwood.Core.World.QuickSim.Strength(t)).ElementAt(12).Id;
        var s = GameSession.NewGame(seed, id, path);
        for (int i = 0; i < 45; i++) s.AdvanceDay();
        return s;
    }

    [Fact]
    public void Delete_one_all_read_and_a_filter_but_never_an_open_question()
    {
        var s = Started(41);
        Assert.True(s.Inbox.Count > 5);
        int before = s.Inbox.Count;

        var first = s.Inbox[^1];
        Assert.True(s.DeleteMessage(first.Id));
        Assert.DoesNotContain(s.Inbox, m => m.Id == first.Id);
        Assert.False(s.DeleteMessage(first.Id));

        // An open press question: its message stays through every kind of delete.
        var target = s.Inbox[0];
        target.Read = true;
        s.Media.Decisions.Add(new PendingDecision { Id = 9999, TeamId = s.HumanTeam.Id, Created = s.Today, Expires = s.Today.AddDays(7), MessageId = target.Id,
            Options = { new DecisionOption { Label = "A" } } });
        Assert.False(s.CanDelete(target));
        Assert.False(s.DeleteMessage(target.Id));

        foreach (var m in s.Inbox.Take(3)) m.Read = true;
        int read = s.Inbox.Count(m => m.Read && s.CanDelete(m));
        Assert.Equal(read, s.DeleteAllRead());
        Assert.Contains(s.Inbox, m => m.Id == target.Id);
        Assert.DoesNotContain(s.Inbox, m => m.Read && m.Id != target.Id);

        int games = s.Inbox.Count(m => m.Category == "Game" && s.CanDelete(m));
        Assert.Equal(games, s.DeleteWhere(m => m.Category == "Game"));
        Assert.DoesNotContain(s.Inbox, m => m.Category == "Game" && s.CanDelete(m));
        Assert.True(s.Inbox.Count < before);
    }

    [Fact]
    public void Ids_are_never_reused_and_settings_survive_a_save()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hw-inbox-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var s = Started(42, Path.Combine(dir, "career.hwsave"));
            int next = s.NextMessageId;
            var newest = s.Inbox[0];
            Assert.Equal(next - 1, newest.Id);
            Assert.True(s.DeleteMessage(newest.Id));
            s.InboxSettings.AutoClearWeeks = 4;
            s.InboxSettings.ResultsEveryOtherWeek = true;
            s.SaveNow();

            var l = GameSession.Load(Path.Combine(dir, "career.hwsave"));
            Assert.Equal(next, l.NextMessageId);
            Assert.DoesNotContain(l.Inbox, m => m.Id == newest.Id);
            Assert.Equal(4, l.InboxSettings.AutoClearWeeks);
            Assert.True(l.InboxSettings.ResultsEveryOtherWeek);
            l.Post("A new note", "Body");
            Assert.Equal(next, l.Inbox[0].Id);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Auto_clear_takes_old_read_messages_on_Mondays_and_leaves_unread_ones()
    {
        var s = Started(43);
        s.InboxSettings.AutoClearWeeks = 2;
        foreach (var m in s.Inbox) m.Read = true;
        var unread = s.Inbox[^1];
        unread.Read = false;
        for (int i = 0; i < 8; i++) s.AdvanceDay();

        var monday = s.Today;
        while (monday.DayOfWeek != DayOfWeek.Monday) monday = monday.AddDays(-1);
        int cutoff = monday.DayNumber - 2 * 7;
        Assert.Contains(s.Inbox, m => m.Date.DayNumber < cutoff); // the unread one, at least
        Assert.DoesNotContain(s.Inbox, m => m.Read && m.Date.DayNumber < cutoff && s.CanDelete(m));
        Assert.Contains(s.Inbox, m => m.Id == unread.Id);
    }
}

/// <summary>
/// The wage-budget line (run 22, item 4): in the summer, renewals and free agents can't take the room the budget keeps
/// for college seniors committed to you. Before, three signings filled the budget to $10k, then two committed seniors
/// signed on graduation day and `nba-real-mid` was $2.4M over its budget all season.
/// </summary>
public class ReservedWagesTests
{
    [Fact]
    public void Summer_offers_leave_room_for_committed_seniors()
    {
        var probe = new WorldGenerator(new Rng(57)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));
        var id = probe.Teams.OrderByDescending(t => Hardwood.Core.World.QuickSim.Strength(t)).ElementAt(14).Id;
        var s = GameSession.NewGame(57, id, null);
        Assert.Equal(0, s.MyReservedWages); // in season nothing is reserved
        int guard = 0;
        while (s.Phase != SessionPhase.Offseason && guard++ < 400) s.AdvanceDay();
        Assert.Equal(SessionPhase.Offseason, s.Phase);
        var board = s.Recruiting;
        Assert.NotNull(board);

        // Two seniors committed to us at $1.2M each.
        foreach (var pid in new[] { -901, -902 })
            board!.Prospects[pid] = new Hardwood.Core.College.RecruitState { TeamId = s.HumanTeam.Id, OfferWage = 1_200_000, OfferYears = 3, CommittedTeamId = s.HumanTeam.Id };
        Assert.Equal(2_400_000, s.MyReservedWages);
        Assert.Equal(s.HumanTeam.Money.WageBudget - s.MyCommittedWages - 2_400_000, s.MyWageRoom);

        // A renewal whose wage fits the budget only without the reserve is refused, and the refusal says why.
        var fa = s.MyExpiring.First(p => s.CannotTalk(p) == null);
        s.HumanTeam.Money.WageBudget = s.MyCommittedWages + 5_000_000;
        var reply = s.Offer(fa, 4_000_000, 2);
        Assert.False(reply.Accepted);
        Assert.Contains("committed college seniors", reply.Text);
        Assert.Equal(2_600_000, s.MyWageRoom);
    }
}
