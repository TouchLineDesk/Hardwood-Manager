using Hardwood.Core.College;
using Hardwood.Core.Model;

namespace Hardwood.Core.Season;

/// <summary>
/// Run 166, step (4) of Danny's college order (critic review #19 item 8, folded into CS10): the college calendar's
/// moments as events of your own: Midnight Madness in mid-October, the early signing period, poll Monday, Championship
/// Week, court-storming news after upsets and a "One Shining Moment" recap after the title game (Selection Sunday's
/// watch party is in <see cref="CollegeWorld"/>'s Selection Sunday message). Every moment is posted on one exact day
/// (or from games played today), so "Delete all read" never brings one back; none draws a random number or adds a
/// save field.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>Midnight Madness: the first Friday on or after this day of October (the first practice).</summary>
    public const int MidnightMadnessFrom = 10;

    /// <summary>Poll Monday posts only a move of at least this many places inside the Top 25 (or entering, leaving, No. 1).</summary>
    public const int PollMoveWorthNews = 4;

    /// <summary>Court storming: an unranked home side beating a visitor ranked this high or better.</summary>
    public const int StormRank = 15;

    /// <summary>Other programs' court storms reach your digest only when the visitor was ranked this high or better.</summary>
    public const int StormRankNational = 5;

    /// <summary>The day the conference tournaments begin (<see cref="CollegeWorld"/> schedules them March 10-12).</summary>
    public static DateOnly ConferenceTournamentDay(int seasonYear) => new(seasonYear + 1, 3, 10);

    /// <summary>The first Friday on or after October 10 of a season's year: Midnight Madness.</summary>
    public static DateOnly MidnightMadnessDay(int seasonYear)
    {
        var d = new DateOnly(seasonYear, 10, MidnightMadnessFrom);
        while (d.DayOfWeek != DayOfWeek.Friday) d = d.AddDays(1);
        return d;
    }

    private void CollegeMoments(CollegeWorld c)
    {
        MidnightMadness(c);
        EarlySigning(c);
        PollMonday(c);
        ChampionshipWeek(c);
        CourtStorms(c);
        OneShiningMoment(c);
        AcademicCalendar(c); // CS7 part 2c (run 175)
        RecruitingRoad(c); // run 197: evaluation periods, the summer camp, the Midnight Madness visit weekend
    }

    // ------------------------------------------------------------------------------------------
    // Midnight Madness

    private void MidnightMadness(CollegeWorld c)
    {
        if (Today != MidnightMadnessDay(c.Season.Year)) return;
        var t = HumanTeam;
        var arena = CollegeWorld.ArenaOf(t);
        double pts = c.HomeCourtPoints(t.Id);
        string house = arena ?? "the home arena";
        string crowd = pts >= 4.2 ? $"{house} is full at midnight, students packed in behind the baskets"
            : pts >= 3.6 ? $"A big crowd fills {house}, the student section loudest of all"
            : $"A few thousand fans turn out at {house}";
        var roster = t.Roster;
        var freshmen = roster.Where(p => c.ClassYearOf(p, Today) is { Year: 1 }).OrderByDescending(p => p.CurrentAbility).ToList();
        var best = roster.OrderByDescending(p => p.CurrentAbility).FirstOrDefault();
        var dunker = roster.OrderByDescending(p => p.Attributes[Attr.Dunk] + p.Attributes[Attr.Vertical]).ThenBy(p => p.Id).FirstOrDefault();
        var lines = new List<string> { $"{crowd} for Midnight Madness, the first practice of the {c.Season.Year}-{(c.Season.Year + 1) % 100:00} season." };
        if (freshmen.Count > 0)
            lines.Add($"The new freshmen are introduced one by one: {string.Join(", ", freshmen.Select(p => $"{p.Name} ({p.PrimaryPosition})"))}.");
        if (best != null) lines.Add($"The biggest cheer is for {best.Name}{(ClassYearOf(best) is { } cy ? $", the {cy.Long.ToLowerInvariant()}" : "")}.");
        if (dunker != null) lines.Add($"{dunker.Name} wins the dunk contest.");
        if (c.FirstGameDay is { } first) lines.Add($"The season tips off on {first:ddd, MMM d}; the preseason polls come out a week before.");
        Post("Midnight Madness: the season starts tonight", string.Join(" ", lines), "Roster");
    }

    // ------------------------------------------------------------------------------------------
    // The early signing period

    private void EarlySigning(CollegeWorld c)
    {
        if (!TrailOpen) return;
        int cls = c.RecruitingClass(Today);
        if (Today != CollegeWorld.EarlySigningStart(cls)) return;
        var mine = c.Recruits.Where(kv => kv.Value.ClassOf == cls && kv.Value.CommittedTo == HumanTeam.Id)
            .Select(kv => (Player: c.Prospects.FirstOrDefault(p => p.Id == kv.Key), Recruit: kv.Value))
            .Where(x => x.Player != null).OrderBy(x => x.Recruit.Rank).ToList();
        var rank = c.ClassRanking(cls);
        int place = rank.FindIndex(x => x.Program == HumanTeam) + 1;
        int open = c.OpenScholarships(HumanTeam, cls);
        string ends = $"{CollegeWorld.EarlySigningEnd(cls):MMM d}";
        var lines = new List<string>();
        if (mine.Count == 0)
            lines.Add($"The early signing period opens today and runs to {ends}. Nobody has committed to us yet, so nobody signs today.");
        else
            lines.Add($"The early signing period opens today: {(mine.Count == 1 ? "our one commitment signs" : $"our {mine.Count} commitments sign")} "
                + $"and can no longer flip: {string.Join(", ", mine.Select(x => $"{x.Player!.Name} ({x.Recruit.Stars}★ {x.Player.PrimaryPosition}, No. {x.Recruit.Rank})"))}.");
        lines.Add($"Any prospect who commits by {ends} signs at once. After that, a verbal commitment can flip until signing day, {CollegeWorld.SigningDay(cls):MMMM d}.");
        if (place > 0) lines.Add($"The Class of {cls} ranks No. {place} in the country so far" + (rank.Count > 0 && place != 1 ? $"; {rank[0].Program.FullName} lead." : "."));
        lines.Add(open > 0 ? $"{(open == 1 ? "One scholarship is" : $"{open} scholarships are")} still open." : "Every scholarship in the class is taken.");
        if (rank.Count > 0)
            lines.Add("The top classes: " + string.Join(" · ", rank.Take(3).Select((x, i) => $"{i + 1}. {x.Program.FullName} ({x.Commits} signed)")) + ".");
        Post(mine.Count == 0 ? $"Early signing period: nobody signs with us yet" : $"Early signing period: {mine.Count} sign with us",
            string.Join("\n", lines), "Roster");
    }

    // ------------------------------------------------------------------------------------------
    // Poll Monday

    private void PollMonday(CollegeWorld c)
    {
        if (Today.DayOfWeek != DayOfWeek.Monday || c.Season.Selected || c.FirstGameDay is not { } first || Today <= first) return;
        // K35 (run 193): the poll is released this morning (if no game today has released it already), and the news
        // compares it with last week's release, the same ranks the badges show all week.
        c.ReleasePoll(Today);
        if (c.Season.PollDay != Today || c.Season.PollBefore.Count == 0) return;
        int? before = c.Season.PollBefore.TryGetValue(HumanTeam.Id, out var was) ? was : null;
        int? now = c.Rank(HumanTeam.Id);
        if (before == now) return;
        string? title = (before, now) switch
        {
            (_, 1) => "Poll Monday: we're No. 1 in the country",
            (null, { } n) => $"Poll Monday: we're in the Top 25 at No. {n}",
            ({ } b, null) => $"Poll Monday: we drop out of the Top 25 (we were No. {b})",
            ({ } b, { } n) when b - n >= PollMoveWorthNews => $"Poll Monday: up {b - n} places to No. {n}",
            ({ } b, { } n) when n - b >= PollMoveWorthNews => $"Poll Monday: down {n - b} places to No. {n}",
            _ => null,
        };
        if (title == null) return;
        var (w, l) = c.Record(HumanTeam.Id);
        var top = c.Top25().Take(5).Select((t, i) => $"{i + 1}. {t.FullName}");
        string body = $"The new Top 25 is out. {HumanTeam.FullName} ({w}-{l}) "
            + (now is { } r ? $"are No. {r}" : "aren't ranked") + (before is { } b2 ? $", from No. {b2} a week ago." : ", from unranked a week ago.")
            + $"\nThe top five: {string.Join(", ", top)}.\nThe whole poll is on the College page (Top 25).";
        Post(title, body, "League");
    }

    // ------------------------------------------------------------------------------------------
    // Championship Week

    private void ChampionshipWeek(CollegeWorld c)
    {
        var start = ConferenceTournamentDay(c.Season.Year);
        if (Today != start.AddDays(-1)) return;
        var conf = MyConference;
        if (conf == null) return;
        string ev = $"{conf.Short} tournament";
        var games = c.Season.Games.Where(g => g.Event == ev).ToList();
        if (games.Count == 0) return; // the regular season isn't over (the tournaments are drawn when it is)
        var p = c.Bracketology();
        string status = p.StatusOf(HumanTeam.Id);
        bool lockIn = p.Seeds.TryGetValue(HumanTeam.Id, out var seed) && seed <= 8 && !p.AutoBids.ContainsKey(HumanTeam.Id);
        string stakes = lockIn || (seed > 0 && seed <= 8)
            ? $"Bracketology has us as a No. {seed} seed for March Madness already: these games are about the seed line and a trophy."
            : p.LastFourIn.Contains(HumanTeam.Id) ? "We're one of the last four in: a win or two this week should be enough; an early loss leaves it to the committee."
            : p.FirstFourOut.Contains(HumanTeam.Id) || p.NextFourOut.Contains(HumanTeam.Id) ? $"We're on the wrong side of the bubble ({status.ToLowerInvariant()}): a run to the final would change that, and the title takes the automatic bid."
            : p.AutoBids.ContainsKey(HumanTeam.Id) ? "We lead the conference: the tournament title is the automatic bid; lose and it's up to the committee."
            : "Our only way into March Madness is the automatic bid: win three games in three days.";
        var mine = games.FirstOrDefault(g => g.HomeId == HumanTeam.Id || g.AwayId == HumanTeam.Id);
        var table = c.ConferenceTable(conf.Key);
        int place = table.FindIndex(r => r.TeamId == HumanTeam.Id) + 1;
        string title, body;
        if (mine == null)
        {
            title = $"Championship Week: we're not in the {ev}";
            body = $"Only the top eight of the {conf.Name} play in its tournament, and {HumanTeam.FullName} finished {Ordinal(place)}. "
                + (p.In(HumanTeam.Id) ? "We'll wait for Selection Sunday: " + stakes : "March Madness is gone unless the committee surprises everyone; the NIT takes the 32 best teams left out.")
                + $"\nThe {ev} runs {start:MMM d}-{start.AddDays(2).Day}.";
        }
        else
        {
            int opp = mine.HomeId == HumanTeam.Id ? mine.AwayId : mine.HomeId;
            var leader = c.Program(table[0].TeamId)!;
            title = $"Championship Week: the {ev} starts tomorrow";
            body = $"{HumanTeam.FullName} are the No. {place} seed in the {ev}: first up, {CollegeWorld.WithRank(c.Rank(opp), c.Program(opp)!.FullName)} on {mine.Date:ddd, MMM d}. "
                + $"The final is on {start.AddDays(2):ddd, MMM d}; the champions take the {conf.Short}'s automatic bid to March Madness."
                + (leader != HumanTeam ? $" {leader.FullName} are the top seed." : "")
                + $"\n{stakes}\nAll week, every conference crowns a champion; Selection Sunday follows.";
        }
        Post(title, body, "League");
    }

    // ------------------------------------------------------------------------------------------
    // Court storming

    private void CourtStorms(CollegeWorld c)
    {
        foreach (var g in c.Season.Games.Where(g => g.Date == Today && g.Played && !g.IsNeutral() && g.HomeRank == null && g.AwayRank is <= StormRank && g.WinnerId == g.HomeId))
        {
            var home = c.Program(g.HomeId)!; var away = c.Program(g.AwayId)!;
            int r = g.AwayRank!.Value;
            string score = $"{home.FullName} {g.HomePts}, #{r} {away.FullName} {g.AwayPts}";
            string place = CollegeWorld.ArenaOf(home) ?? $"{home.City}";
            if (g.HomeId == HumanTeam.Id)
                Post($"They stormed the court: we beat No. {r} {away.FullName}",
                    $"{score}. At the final horn the students poured out of the stands at {place} and onto the floor, and the players were lost in the crowd. "
                    + "Upsets like this one are what a season is remembered for.", "League");
            else if (g.AwayId == HumanTeam.Id)
                Post($"Court stormed: {home.FullName} fans rush the floor after beating us",
                    $"{score}. The home fans stormed the court at {place} as our players walked off. "
                    + "A ranked team on the road wears a target: every unranked home crowd wants this night.", "League");
            else if (r <= StormRankNational)
                PostWorld($"Court storm: {home.FullName} beat No. {r} {away.FullName}", $"{score}, and the fans stormed the court at {place}.", college: true);
        }
    }

    // ------------------------------------------------------------------------------------------
    // One Shining Moment

    private static string RoundLabel(int round) => round == 6 ? "final" : CollegeWorld.RoundName(round);

    private void OneShiningMoment(CollegeWorld c)
    {
        var s = c.Season;
        if (s.ChampionId is not { } champ || !s.RoundDates.TryGetValue(CollegeWorld.Madness, out var dates) || dates.Count < 7) return;
        var final = s.Games.Where(g => g.Event == CollegeWorld.Madness && g.Round == 6 && g.Played).FirstOrDefault();
        if (final == null || Today != final.Date.AddDays(1)) return;
        var champion = c.Program(champ)!;
        int seed = s.Seeds.GetValueOrDefault(champ);
        var madness = s.Games.Where(g => g.Event == CollegeWorld.Madness && g.Played && g.Round >= 1).ToList();
        string Name(int id) => c.WithSeed(id);
        var lines = new List<string>();
        var star = final.HomeId == champ ? final.HomeStar : final.AwayStar;
        lines.Add($"{c.Program(champ)!.FullName}, {CollegeWorld.Article(seed)}-seed, are the {s.Year + 1} champions: {Math.Max(final.HomePts!.Value, final.AwayPts!.Value)}-{Math.Min(final.HomePts!.Value, final.AwayPts!.Value)} over {Name(final.HomeId == champ ? final.AwayId : final.HomeId)}"
            + (final.Overtimes > 0 ? $" in {(final.Overtimes == 1 ? "overtime" : $"{final.Overtimes} overtimes")}" : "") + "."
            + (star != null ? $" {star.Line} in the final." : ""));
        var road = madness.Where(g => g.WinnerId == champ && g.Round <= 5).OrderBy(g => g.Round).Select(g => $"{Name(g.HomeId == champ ? g.AwayId : g.HomeId)} ({Math.Max(g.HomePts!.Value, g.AwayPts!.Value)}-{Math.Min(g.HomePts!.Value, g.AwayPts!.Value)})").ToList();
        if (road.Count > 0) lines.Add($"The road to the final: {string.Join(", ", road)}.");
        // The tournament's biggest upset (the widest seed gap won) and its closest game.
        var upset = madness.Where(c.IsUpset).OrderByDescending(g => s.Seeds.GetValueOrDefault(g.WinnerId!.Value) - s.Seeds.GetValueOrDefault(g.HomeId == g.WinnerId ? g.AwayId : g.HomeId)).ThenBy(g => g.Round).FirstOrDefault();
        if (upset != null)
        {
            int w = upset.WinnerId!.Value, l = upset.HomeId == w ? upset.AwayId : upset.HomeId;
            lines.Add($"The upset of the tournament: {Name(w)} over {Name(l)} in the {RoundLabel(upset.Round)}.");
        }
        var closest = madness.OrderBy(g => Math.Abs(g.HomePts!.Value - g.AwayPts!.Value) - 3 * g.Overtimes).ThenByDescending(g => g.Round).FirstOrDefault();
        if (closest != null)
            lines.Add($"The closest finish: {Name(closest.WinnerId!.Value)} {Math.Max(closest.HomePts!.Value, closest.AwayPts!.Value)}, {Name(closest.HomeId == closest.WinnerId ? closest.AwayId : closest.HomeId)} {Math.Min(closest.HomePts!.Value, closest.AwayPts!.Value)}"
                + (closest.Overtimes > 0 ? " (overtime)" : "") + ".");
        // The deepest run by a double-digit seed (a Cinderella).
        var cinderella = s.Seeds.Where(kv => kv.Value >= 10).Select(kv => (Id: kv.Key, Seed: kv.Value, Reached: c.MadnessReached(kv.Key)))
            .Where(x => x.Reached >= 3).OrderByDescending(x => x.Reached).ThenByDescending(x => x.Seed).FirstOrDefault();
        if (cinderella.Id != 0)
            lines.Add($"Cinderella: {Name(cinderella.Id)} {(cinderella.Reached == 7 ? "won the whole thing" : "reached the " + (cinderella.Reached == 6 ? "final" : cinderella.Reached >= 5 ? "Final Four" : cinderella.Reached == 4 ? "Elite Eight" : "Sweet 16"))}.");
        // Your March.
        int mine = c.MadnessReached(HumanTeam.Id);
        string ours = mine switch
        {
            7 => $"And it's us: {HumanTeam.FullName} cut down the nets. One shining moment, and it's ours.",
            6 => $"{HumanTeam.FullName} made it all the way to the final.",
            -1 => $"{HumanTeam.FullName} weren't in the field this year.",
            _ when madness.Concat(s.Games.Where(g => g.Event == CollegeWorld.Madness && g.Round == 0)).LastOrDefault(g => g.Played && (g.HomeId == HumanTeam.Id || g.AwayId == HumanTeam.Id) && g.WinnerId != HumanTeam.Id) is { } out_
                => $"Our March ended in the {RoundLabel(out_.Round)}, against {Name(out_.WinnerId!.Value)}.",
            _ => "",
        };
        if (ours.Length > 0) lines.Add(ours);
        Post($"One Shining Moment: {champion.FullName} are the champions", string.Join("\n", lines), "League");
    }
}
