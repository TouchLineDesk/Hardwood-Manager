using Hardwood.Core.College;
using Hardwood.Core.Model;

namespace Hardwood.Core.Season;

/// <summary>
/// Slice 11 of the revised launch plan (#1164, run 202): player stories over four years. Each day of your college season, your
/// players' lines are read for a story (<see cref="PlayerStories"/>): the freshman phenom, the hometown hero, the walk-on who
/// earned his minutes (and, the summer after, a scholarship), the senior's breakout, and the overlooked recruit on awards night.
/// Each is told once per player, in the inbox, and kept on his player page. No random draw.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>The stories told about a player (any program you coached him at), oldest first.</summary>
    public IReadOnlyList<PlayerStory> StoriesOf(int playerId) => World.College?.Stories.Of(playerId).ToList() ?? new List<PlayerStory>();

    private void PlayerStoriesCalendar(CollegeWorld c)
    {
        var book = c.Stories;
        // A summer story (the walk-on's scholarship) is told on the first day of the next season.
        foreach (var s in book.Told.Where(s => !s.Posted && s.TeamId == HumanTeam.Id).ToList())
        {
            s.Posted = true;
            Post(s.Title, s.Text + StoryFooter, "Roster");
        }
        if (!CoachingThisCollegeSeason || c.Season.Year != CollegeWorld.SchoolYear(Today)) return;
        var t = HumanTeam;
        var lines = t.Roster.Select(p => (p, st: c.Season.Stats.GetValueOrDefault(p.Id)))
                            .Where(x => x.st != null && x.st.TeamId == t.Id).ToList();
        if (lines.Count == 0) return;
        int topScorer = lines.OrderByDescending(x => x.st!.Ppg).ThenBy(x => x.p.Id).First().p.Id;
        var legacy = c.LegacyOf(t.Id);
        foreach (var (p, st) in lines.OrderBy(x => x.p.Id))
        {
            if (c.ClassYearOf(p, Today) is not { } cy) continue;
            var prior = legacy.Careers.FirstOrDefault(x => x.PlayerId == p.Id && !x.Done && x.Last < c.Season.Year);
            foreach (var (key, title, body) in PlayerStories.InSeason(p, t, st!, cy, c.OriginOf(p.Id), c.WalkOnIds.Contains(p.Id),
                         p.Id == topScorer, prior?.G ?? 0, prior?.Pts ?? 0).ToList())
                Tell(c, p, key, title, body);
        }
        // Awards night: the season's honours are voted with the title decided.
        if (c.Awards.FirstOrDefault(a => a.Year == c.Season.Year) is { } awards)
            foreach (var (p, _) in lines.OrderBy(x => x.p.Id))
                if (PlayerStories.Overlooked(p, t, c.OriginOf(p.Id), c.WalkOnIds.Contains(p.Id),
                        PlayerStories.HonoursIn(awards, p.Id, CollegeWorld.ConferenceShortName)) is { } o)
                    Tell(c, p, o.Key, o.Title, o.Body);
    }

    private const string StoryFooter = "\n\nHis story is kept on his player page.";

    private void Tell(CollegeWorld c, Player p, string key, string title, string body)
    {
        if (c.Stories.Has(p.Id, key)) return;
        c.Stories.Told.Add(new PlayerStory
        {
            PlayerId = p.Id, TeamId = HumanTeam.Id, Key = key, Year = c.Season.Year, Date = Today, Title = title, Text = body, Posted = true,
        });
        Post(title, body + StoryFooter, "Roster");
    }
}
