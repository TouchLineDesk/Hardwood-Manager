namespace Hardwood.Core.People;

/// <summary>One answer to a press question or a locker-room situation, with a plain hint of what it will do.</summary>
public sealed class DecisionOption
{
    public string Label { get; set; } = "";
    public string Hint { get; set; } = "";
}

/// <summary>
/// A moment that needs the coach (roadmap: people part 2): a press conference question or a locker-room situation.
/// It waits a week; if you don't answer, your assistant gives the safe answer for you.
/// </summary>
public sealed class PendingDecision
{
    public int Id { get; set; }
    /// <summary>"press" or "locker".</summary>
    public string Kind { get; set; } = "press";
    /// <summary>Which situation (losing-streak, feud, late, ...): decides the effects of each answer.</summary>
    public string Topic { get; set; } = "";
    public int TeamId { get; set; }
    public DateOnly Created { get; set; }
    public DateOnly Expires { get; set; }
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public List<DecisionOption> Options { get; set; } = new();
    /// <summary>The answer your assistant gives if the week runs out.</summary>
    public int SafeOption { get; set; }
    public List<int> PlayerIds { get; set; } = new();
    public int? Chosen { get; set; }
    public bool Auto { get; set; }
    public string? Outcome { get; set; }
    /// <summary>The inbox message that brought it (the inbox shows the answers under it).</summary>
    public int MessageId { get; set; }
    public bool Open => Chosen == null;
}

/// <summary>A lasting lift (or dent) to a team's spirit from something the coach did: it shifts chemistry's target while it lasts.</summary>
public sealed class SpiritEffect
{
    public int TeamId { get; set; }
    public double Amount { get; set; }
    public DateOnly Until { get; set; }
    public string Reason { get; set; } = "";
}

/// <summary>The press room and the locker room: open moments, recent answers and the spirit they left. Saved as JSON in meta.</summary>
public sealed class MediaDesk
{
    public List<PendingDecision> Decisions { get; set; } = new();
    public List<SpiritEffect> Spirit { get; set; } = new();
    public int NextId { get; set; } = 1;
    public DateOnly LastPress { get; set; }
    public DateOnly LastLocker { get; set; }
    /// <summary>Players the press has already asked about, and when (so the same unhappy player isn't a story every week).</summary>
    public Dictionary<int, DateOnly> AskedAbout { get; set; } = new();
    /// <summary>Answers given in the last four months (a repeated line counts for less).</summary>
    public List<SaidLine> Said { get; set; } = new();
    /// <summary>When each situation last came up (the same locker-room story doesn't repeat for months).</summary>
    public Dictionary<string, DateOnly> LastTopic { get; set; } = new();
    /// <summary>Counts by kind and answer, for the critic bot and the tests.</summary>
    public int Answered { get; set; }
    public int AutoAnswered { get; set; }

    public IEnumerable<PendingDecision> OpenFor(int teamId) => Decisions.Where(d => d.Open && d.TeamId == teamId);
    public PendingDecision? Find(int id) => Decisions.FirstOrDefault(d => d.Id == id);
    public PendingDecision? ForMessage(int messageId) => Decisions.FirstOrDefault(d => d.MessageId == messageId);
}

/// <summary>An answer given, and when.</summary>
public sealed class SaidLine
{
    public string Key { get; set; } = "";
    public DateOnly Date { get; set; }
}
