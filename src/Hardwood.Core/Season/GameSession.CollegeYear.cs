using Hardwood.Core.College;
using Hardwood.Core.Model;

namespace Hardwood.Core.Season;

/// <summary>CS3 (run 127, the college plan): the college year you can see — class years, the preseason polls and media
/// day, and Senior Night before the last home game.</summary>
public sealed partial class GameSession
{
    /// <summary>A rostered college player's class year today (null outside college).</summary>
    public ClassYear? ClassYearOf(Player p) => World.College?.ClassYearOf(p, Today);

    /// <summary>The preseason message goes out this many days before the first game.</summary>
    public const int PreseasonLeadDays = 7;

    /// <summary>Called at the end of each day: the college calendar's moments for your program.</summary>
    internal void CollegeCalendar()
    {
        if (!Employed || !InCollege || World.College is not { } c || Phase == SessionPhase.Offseason) return;
        PreseasonPolls(c);
        SeniorNight(c);
        CollegeMoments(c); // run 166: Midnight Madness, early signing, poll Monday, Championship Week, court storms, One Shining Moment
    }

    private bool PostedThisSchoolYear(string titleStart) =>
        Inbox.Any(m => m.Title.StartsWith(titleStart) && CollegeWorld.SchoolYear(m.Date) == CollegeWorld.SchoolYear(Today));

    private void PreseasonPolls(CollegeWorld c)
    {
        // One exact day (review of PR #191): a check on the inbox would post it again after "Delete all read".
        if (c.FirstGameDay is not { } first || Today != first.AddDays(-PreseasonLeadDays)) return;
        if (c.Season.Year != CollegeWorld.SchoolYear(Today) || PostedThisSchoolYear("Preseason")) return;
        Post(PreseasonTitle(c), PreseasonBody(c, first), "League");
    }

    internal string PreseasonTitle(CollegeWorld c)
    {
        var conf = MyConference!;
        int place = c.ConferencePoll(conf.Key).IndexOf(HumanTeam) + 1;
        string rank = c.Rank(HumanTeam.Id) is { } r ? $"#{r} in the Top 25" : "unranked";
        return $"Preseason: {rank}, picked {Ordinal(place)} in the {conf.Short}";
    }

    internal string PreseasonBody(CollegeWorld c, DateOnly first)
    {
        var conf = MyConference!;
        var poll = c.PollOrder().Select(id => c.Program(id)!).ToList();
        var confPoll = c.ConferencePoll(conf.Key);
        int place = confPoll.IndexOf(HumanTeam) + 1, of = confPoll.Count;
        int? rank = c.Rank(HumanTeam.Id);
        var lines = new List<string>
        {
            "The preseason Top 25: " + string.Join(", ", poll.Take(10).Select((t, i) => $"{i + 1}. {t.FullName}")) + " …"
                + (rank is { } r ? $" {HumanTeam.FullName} start at #{r}." : $" {HumanTeam.FullName} aren't ranked."),
            $"The {conf.Name}'s coaches pick {string.Join(", ", confPoll.Take(3).Select((t, i) => $"{i + 1}. {t.FullName}"))}; "
                + $"{HumanTeam.FullName} are picked {Ordinal(place)} of {of}.",
        };
        // Media day: what the press asks about — the best player, the seniors' last season and the new freshmen.
        var roster = HumanTeam.Roster;
        var best = roster.OrderByDescending(p => p.CurrentAbility).FirstOrDefault();
        var finals = c.FinalYear(HumanTeam);
        var freshmen = roster.Where(p => c.ClassYearOf(p, Today) is { Year: 1 }).OrderByDescending(p => p.CurrentAbility).ToList();
        string expectation = rank is <= 5 ? "Everyone asks whether this is a Final Four team."
            : rank != null ? "The questions are about March: how high a seed, and how far."
            : place <= Math.Max(2, of / 4) ? "The talk is of a conference title and a place in March Madness."
            : place <= of / 2 ? "The press sees a bubble team: the conference season will decide it."
            : "Few expect much, so every win in the conference will be noticed.";
        var media = new List<string> { $"Media day: {expectation}" };
        if (best != null) media.Add($"Most of the cameras are on {best.Name} ({best.PrimaryPosition}, {ClassYearOf(best)?.Long.ToLowerInvariant() ?? "age " + best.Age(Today)}).");
        if (finals.Count > 0) media.Add($"{(finals.Count == 1 ? "One player starts his" : $"{finals.Count} players start their")} final season: {string.Join(", ", finals.Select(p => p.Name))}.");
        if (freshmen.Count > 0) media.Add($"The new freshmen: {string.Join(", ", freshmen.Select(p => $"{p.Name} ({p.PrimaryPosition})"))}.");
        lines.Add(string.Join(" ", media));
        if (c.GamesOf(HumanTeam.Id).FirstOrDefault(g => !g.Played) is { } opener)
        {
            var opp = c.Program(opener.HomeId == HumanTeam.Id ? opener.AwayId : opener.HomeId)!;
            lines.Add($"The season opens on {opener.Date:ddd, MMM d}: {(opener.HomeId == HumanTeam.Id ? "at home to" : "away at")} {CollegeWorld.WithRank(c.Rank(opp.Id), opp.FullName)}.");
        }
        return string.Join("\n\n", lines);
    }

    private void SeniorNight(CollegeWorld c)
    {
        if (c.LastHomeGame(HumanTeam.Id) is not { Played: false } g || g.Date != Today.AddDays(1)) return;
        if (PostedThisSchoolYear("Senior Night")) return;
        var opp = c.Program(g.AwayId)!;
        var finals = c.FinalYear(HumanTeam);
        string body = finals.Count == 0
            ? $"Tomorrow's game against {CollegeWorld.WithRank(c.Rank(opp.Id), opp.FullName)} is the last home game of the regular season. Nobody on the roster is in his final season, so there's no one to honour this year."
            : $"Tomorrow's game against {CollegeWorld.WithRank(c.Rank(opp.Id), opp.FullName)} is the last home game of the regular season. Before tip-off the crowd honours "
              + $"{(finals.Count == 1 ? "the player" : $"the {finals.Count} players")} in {(finals.Count == 1 ? "his" : "their")} final season, who turn pro this summer: "
              + string.Join(", ", finals.Select(p => $"{p.Name} ({p.PrimaryPosition}{SeasonLine(p)})")) + ".";
        Post($"Senior Night tomorrow: {opp.FullName} visit for the last home game", body, "Roster");
    }

    /// <summary>"3 Fr, 4 So, 4 Jr, 4 Sr" for your program as the school year after the summer will see it.</summary>
    public string ClassCounts()
    {
        int year = World.College!.Season.Year + 1;
        var counts = HumanTeam.Roster.Select(p => CollegeWorld.ClassYearOf(p, year)).Where(c => c != null).GroupBy(c => c!.Year).ToDictionary(g => g.Key, g => g.Count());
        return string.Join(", ", CollegeWorld.ClassShort.Select((n, i) => $"{counts.GetValueOrDefault(i + 1)} {n}"));
    }

    private string SeasonLine(Player p) =>
        (MyStats.TryGetValue(p.Id, out var st) || Stats.TryGetValue(p.Id, out st)) && st is { Games: > 0 } ? $", {st.Ppg:F1} points a game" : "";
}
