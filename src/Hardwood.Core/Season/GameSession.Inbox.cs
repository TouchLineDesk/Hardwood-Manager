namespace Hardwood.Core.Season;

/// <summary>
/// How the inbox behaves for this career (Danny, #243, D4): results every week or every other week, and an optional
/// auto-clear of old read messages. Saved with the career (career_meta "inbox"); a save without it loads the defaults.
/// </summary>
public sealed class InboxSettings
{
    /// <summary>Clear read messages older than this many weeks every Monday. 0 = off (the default).</summary>
    public int AutoClearWeeks { get; set; }
    /// <summary>Post your results every other Monday instead of every Monday (the v0.52.1–v0.55.0 rhythm).</summary>
    public bool ResultsEveryOtherWeek { get; set; }

    public static readonly int[] AutoClearChoices = { 0, 2, 4, 8, 12 };
}

/// <summary>The inbox clean-up (#243): delete one message, all read, or a whole filter; and the weekly auto-clear.</summary>
public sealed partial class GameSession
{
    public InboxSettings InboxSettings { get; set; } = new();

    /// <summary>
    /// Every message ever created in this career counts here, deleted or not, so the inbox measure (messages created per
    /// season) can't be hidden by clearing. Message ids are never reused, even after the newest message is deleted.
    /// </summary>
    public int NextMessageId => _nextMessageId;

    /// <summary>A message carrying a press question or locker-room moment you haven't answered can't be deleted.</summary>
    public bool CanDelete(InboxMessage m) => !Media.Decisions.Any(d => d.Open && d.MessageId == m.Id);

    public bool DeleteMessage(int id)
    {
        var m = Inbox.FirstOrDefault(x => x.Id == id);
        if (m == null || !CanDelete(m)) return false;
        Inbox.Remove(m);
        return true;
    }

    /// <summary>Deletes every message matching <paramref name="which"/> that can be deleted; returns how many went.</summary>
    public int DeleteWhere(Func<InboxMessage, bool> which) => Inbox.RemoveAll(m => which(m) && CanDelete(m));

    public int DeleteAllRead() => DeleteWhere(m => m.Read);

    /// <summary>Monday: read messages older than the setting's weeks go (unread ones and open decisions stay).</summary>
    private void MaybeAutoClear()
    {
        int weeks = InboxSettings.AutoClearWeeks;
        if (weeks <= 0 || Today.DayOfWeek != DayOfWeek.Monday) return;
        int cutoff = Today.DayNumber - weeks * 7;
        DeleteWhere(m => m.Read && m.Date.DayNumber < cutoff);
    }

    /// <summary>The season's champions message is posted once, even if you delete it while the playoffs wind down.</summary>
    private int _championPostedSeason;
}
