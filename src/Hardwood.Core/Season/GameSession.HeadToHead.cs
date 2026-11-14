namespace Hardwood.Core.Season;

/// <summary>Danny's #529: your record against every opponent since the career began, kept in the save.</summary>
public sealed partial class GameSession
{
    public HeadToHeadBook HeadToHeadBook { get; set; } = new();

    /// <summary>Counts this season's results into the book (after every day, before a new season's schedule replaces them).</summary>
    public void SyncHeadToHead() => HeadToHeadBook.Sync(HumanTeam.Id, MyGames(), Today);

    /// <summary>Your club's record against <paramref name="opponent"/> since the book began.</summary>
    public H2HRecord HeadToHead(Model.Team opponent)
    {
        SyncHeadToHead();
        return HeadToHeadBook.Of(HumanTeam.Id, opponent.Id);
    }

    /// <summary>Your club's record against each opponent in <paramref name="games"/>, after one sync.</summary>
    public Dictionary<int, H2HRecord> HeadToHeads(IReadOnlyList<MyGame> games)
    {
        HeadToHeadBook.Sync(HumanTeam.Id, games, Today);
        return games.Select(g => g.Opponent.Id).Distinct().ToDictionary(id => id, id => HeadToHeadBook.Of(HumanTeam.Id, id));
    }

    /// <summary>"since your career began (Oct 21, 2026)" or, for an older save, "since Oct 21, 2026".</summary>
    public string HeadToHeadSince => HeadToHeadBook.Since is not { } d ? "since your career began"
        : HeadToHeadBook.FromCareerStart ? $"since your career began ({d:MMM d, yyyy})" : $"since {d:MMM d, yyyy} (this save kept no earlier meetings)";
}
