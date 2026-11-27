using Hardwood.Core.College;
using Hardwood.Core.Model;
using Hardwood.Core.People;

namespace Hardwood.Core.Season;

/// <summary>One part of an almanac edition: "The year's stories", "The honours", "Your season" ...</summary>
public sealed record AlmanacSection(string Title, IReadOnlyList<string> Lines);

/// <summary>
/// W2 (run 225, `claude/world-stories-plan.md`): one season's almanac. A short magazine page built only from what the save
/// kept: the champions, the honours, your season, the columnists' best and worst calls (W1), records and milestones, the
/// Hall of Fame class (pros) or the coaching carousel (college). <see cref="Season"/> is the season's first year.
/// </summary>
public sealed record AlmanacEdition(int Season, string Label, string Cover, int? ChampionId, IReadOnlyList<AlmanacSection> Sections)
{
    public string Title => $"The {Label} Almanac";
    public AlmanacSection? Section(string title) => Sections.FirstOrDefault(x => x.Title == title);
}

/// <summary>
/// W2 (run 225): the yearly almanac. Every edition is built on demand from the saved history (the league's season summaries,
/// the college champions and honours, the record book, the Hall of Fame, the columnists' judged picks and the carousel), so
/// every past edition stays browsable, an older save gets its editions back, and nothing new is saved. Nothing here draws a
/// random number or writes to the session.
/// </summary>
public static class Almanac
{
    public const string Stories = "The year's stories", Honours = "The honours", Yours = "Your season", Calls = "The columnists' calls",
        Records = "Records and milestones", Hall = "The Hall of Fame", Carousel = "The coaching carousel", Abroad = "Around the world";

    /// <summary>The career's editions, newest first: the college game's in a college career, the league's otherwise.</summary>
    public static List<AlmanacEdition> Editions(GameSession s) =>
        s.CollegeWords && s.World.College is { } cw
            ? cw.History.OrderByDescending(h => h.Year).Select(h => College(s, h)).ToList()
            : s.League.History.OrderByDescending(h => h.Season).Select(h => Pro(s, h)).ToList();

    public static string SeasonLabel(int season) => $"{season}-{(season + 1) % 100:00}";
    private static string Ord(int n) => AwardsEngine.Ordinal(n);
    private static string Plural(int n, string one) => $"{n} {one}{(n == 1 ? "" : "s")}";

    // ------------------------------------------------------------------------------------------
    // The pros

    public static AlmanacEdition Pro(GameSession s, SeasonSummary h)
    {
        var players = s.League.AllActivePlayers.Concat(s.League.FreeAgents).Concat(s.League.Retired).Concat(s.World?.Players ?? Enumerable.Empty<Player>())
            .GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First());
        string Who(int? id) => id is { } i && players.TryGetValue(i, out var p) ? p.Name : "";
        string WhoWith(int? id)
        {
            if (id is not { } i || !players.TryGetValue(i, out var p)) return "";
            var club = p.TeamId is { } t ? s.FindTeam(t) : null;
            return club != null ? $"{p.Name} ({club.Abbreviation})" : p.Name;
        }
        var champ = s.FindTeam(h.ChampionTeamId);
        var runner = s.FindTeam(h.RunnerUpTeamId);
        string top = Names.Brand.TopShort;
        var sections = new List<AlmanacSection>();

        // The year's stories.
        var stories = new List<string>();
        if (champ != null)
            stories.Add($"{champ.TheFullNameCap} won the {top} title" + (runner != null ? $", beating {runner.TheFullName} in the Finals." : ".")
                + (Who(h.FinalsMvpPlayerId) is { Length: > 0 } fm ? $" Finals MVP: {fm}." : ""));
        if (h.Records.Count > 0)
        {
            var best = h.Records.OrderByDescending(r => r.wins).ThenBy(r => r.losses).First();
            if (s.FindTeam(best.teamId) is { } bt)
                stories.Add($"Best regular season: {bt.TheFullName}, {best.wins}-{best.losses}" + (best.teamId == h.ChampionTeamId ? ", and they finished the job." : "."));
            var worst = h.Records.OrderBy(r => r.wins).ThenByDescending(r => r.losses).First();
            if (s.FindTeam(worst.teamId) is { } wt && worst.teamId != best.teamId)
                stories.Add($"At the other end, {wt.TheFullName} won {worst.wins} of {worst.wins + worst.losses}.");
        }
        if (s.League.Cup?.Winners.FirstOrDefault(w => w.Season == h.Season) is { } cup && s.FindTeam(cup.WinnerId) is { } cw1)
            stories.Add($"{cw1.TheFullNameCap} won the {NbaCup.Name}" + (s.FindTeam(cup.RunnerUpId) is { } cr ? $", beating {cr.TheFullName} {cup.Score} in {NbaCup.Venue}" : "")
                + "." + (cup.Mvp is { Length: > 0 } cm ? $" Cup MVP: {cm}." : ""));
        if (h.Awards?.ThirdPlaceWinnerId is { } third && s.FindTeam(third) is { } tt)
            stories.Add($"{tt.TheFullNameCap} won the third-place tournament.");
        if (stories.Count > 0) sections.Add(new(Stories, stories));

        // The honours.
        var honours = new List<string>();
        void Honour(string label, int? id) { if (WhoWith(id) is { Length: > 0 } n) honours.Add($"{label}: {n}"); }
        var a = h.Awards;
        Honour("MVP", h.MvpPlayerId ?? a?.Mvp);
        Honour("Rookie of the Year", a?.Roy);
        Honour("Defensive Player of the Year", a?.Dpoy);
        Honour("Sixth Man of the Year", a?.SixthMan);
        if (a?.CoachOfYearTeamId is { } ct && s.FindTeam(ct) is { } coachTeam)
            honours.Add($"Coach of the Year: {(a.CoachOfYearName.Length > 0 ? $"{a.CoachOfYearName} ({coachTeam.Abbreviation})" : coachTeam.FullName)}");
        Honour("Scoring leader", h.ScoringLeaderPlayerId);
        if (a?.AllLeague.FirstOrDefault() is { Count: > 0 } first)
            honours.Add($"All-League first team: {string.Join(", ", first.Select(id => Who(id)).Where(n => n.Length > 0))}");
        if (honours.Count > 0) sections.Add(new(Honours, honours));

        // Your season (the team you coached that season: the one the columnists picked, or today's if you were there).
        int? yours = TeamThatSeason(s, h.Season, h.Records.Any(r => r.teamId == s.HumanTeam.Id));
        if (yours is { } y && h.Records.FindIndex(r => r.teamId == y) is var idx and >= 0 && s.FindTeam(y) is { } yt)
        {
            var rec = h.Records[idx];
            int rank = h.Records.Count(r => r.wins > rec.wins) + 1;
            string end = y == h.ChampionTeamId ? "Champions." : y == h.RunnerUpTeamId ? "Beaten in the Finals." : "";
            sections.Add(new(Yours, new[] { $"{yt.TheFullNameCap} went {rec.wins}-{rec.losses}, {Ord(rank)} of {h.Records.Count} by record. {end}".TrimEnd() }));
        }

        if (CallLines(s, h.Season) is { Count: > 0 } calls) sections.Add(new(Calls, calls));

        // Records and milestones set that season (a game record still standing, the biggest milestones).
        var marks = new List<string>();
        var book = s.League.Records;
        foreach (var (key, label) in RecordBook.PlayerGame.Select(c => (c.Key, c.Label)).Concat(RecordBook.TeamGame.Select(c => (c.Key, c.Label))))
            if (book.Top(key).FirstOrDefault() is { } e && e.Season == h.Season)
                marks.Add($"{label}: {e.Value:0}, {(e.PlayerId != null ? $"{e.PlayerName} ({e.Team})" : e.Team)} vs {e.Opponent}"
                    + (e.Date is { } d ? $" on {d:MMM d}" : "") + ". Still the league record.");
        if (marks.Count > 5) marks = marks.Take(5).ToList();
        marks.AddRange(book.Milestones.Where(m => m.Season == h.Season).OrderByDescending(m => m.Threshold).ThenBy(m => m.Date).Take(3).Select(m => m.Text + "."));
        if (marks.Count > 0) sections.Add(new(Records, marks));

        // The Hall of Fame class of the summer after.
        var hall = s.League.HallOfFame.Where(x => x.Year == h.Season + 1).OrderByDescending(x => x.Score).Select(x =>
        {
            string name = Who(x.PlayerId);
            if (name.Length == 0) return "";
            return s.League.Careers.TryGetValue(x.PlayerId, out var c)
                ? $"{name}: {Plural(c.Seasons, "season")}, {c.Points:N0} points" + (c.Titles > 0 ? $", {Plural(c.Titles, "title")}" : "") + (c.Mvps > 0 ? $", {Plural(c.Mvps, "MVP")}" : "") + "."
                : $"{name}.";
        }).Where(l => l.Length > 0).ToList();
        if (hall.Count > 0) sections.Add(new(Hall, hall));

        // Around the world: the cups first, then the leagues abroad (the pyramid's own tiers are left out).
        var world = new List<string>();
        foreach (var comp in (s.World?.Competitions ?? new()).Where(c => c.Plays && !Hardwood.Core.World.UsaPyramid.IsPyramid(c.Key))
                     .OrderBy(c => c.Profile.Kind == Hardwood.Core.World.CompetitionKind.Continental ? 0 : 1))
            if (comp.History.FirstOrDefault(r => r.Season == h.Season) is { } r)
                world.Add($"{comp.Profile.Name}: {r.ChampionName}, over {r.RunnerUpName}.");
        if (world.Count > 0) sections.Add(new(Abroad, world.Take(8).ToList()));

        string cover = champ != null ? $"{champ.TheFullNameCap} are champions" : "The season in review";
        return new AlmanacEdition(h.Season, SeasonLabel(h.Season), cover, champ?.Id, sections);
    }

    // ------------------------------------------------------------------------------------------
    // College

    public static AlmanacEdition College(GameSession s, CollegeChampion h)
    {
        var cw = s.World.College!;
        var a = cw.Awards.FirstOrDefault(x => x.Year == h.Year);
        string Abbr(int id) => cw.Program(id)?.Abbreviation ?? "-";
        string Named(int? id) => id is { } i && a != null && a.Names.ContainsKey(i) ? $"{a.NameOf(i)} ({Abbr(a.ProgramOf.GetValueOrDefault(i))})" : "";
        var sections = new List<AlmanacSection>();

        var stories = new List<string> { $"{h.Champion} won the national championship, beating {h.RunnerUp} in the final." };
        if (a != null)
        {
            var four = a.Records.Where(kv => kv.Value.Length >= 3 && kv.Value[2] >= 5).OrderByDescending(kv => kv.Value[2]).ThenBy(kv => kv.Key)
                .Select(kv => cw.Program(kv.Key)?.FullName).OfType<string>().ToList();
            if (four.Count >= 3) stories.Add($"The Final Four: {string.Join(", ", four)}.");
            if (a.OutstandingId is { } mop && Named(mop) is { Length: > 0 } m) stories.Add($"Most Outstanding Player: {m}.");
            var best = a.Records.Where(kv => kv.Value.Length >= 2).OrderByDescending(kv => kv.Value[0]).ThenBy(kv => kv.Value[1]).ThenBy(kv => kv.Key).FirstOrDefault();
            if (best.Value != null && cw.Program(best.Key) is { } bt)
                stories.Add($"Best record: {bt.FullName}, {best.Value[0]}-{best.Value[1]}" + (best.Key == h.ChampionId ? ", and the title to go with it." : "."));
        }
        sections.Add(new(Stories, stories));

        if (a != null)
        {
            var honours = new List<string>();
            void Honour(string label, int? id) { if (Named(id) is { Length: > 0 } n) honours.Add($"{label}: {n}"); }
            Honour("National Player of the Year", a.PlayerOfYearId);
            Honour("Defensive Player of the Year", a.DefensiveId);
            Honour("Freshman of the Year", a.FreshmanId);
            if (a.CoachName.Length > 0) honours.Add($"Coach of the Year: {a.CoachName}" + (a.CoachProgramId is { } cp ? $" ({Abbr(cp)})" : ""));
            if (a.AllAmerica.FirstOrDefault() is { Count: > 0 } first)
                honours.Add($"First-team All-Americans: {string.Join(", ", first.Select(id => Named(id)).Where(n => n.Length > 0))}");
            if (honours.Count > 0) sections.Add(new(Honours, honours));

            int? yours = TeamThatSeason(s, h.Year, a.Records.ContainsKey(s.HumanTeam.Id) && cw.History.Max(x => x.Year) == h.Year);
            if (yours is { } y && a.Records.TryGetValue(y, out var r) && r.Length >= 3 && cw.Program(y) is { } yt)
            {
                string end = r[2] switch
                {
                    7 => "national champions",
                    6 => "beaten in the national final",
                    5 => "a Final Four",
                    4 => "out in the Elite Eight",
                    3 => "out in the Sweet 16",
                    2 => "out in the Round of 32",
                    1 => "out in the first round",
                    0 => "out in the First Four",
                    _ => "no tournament bid",
                };
                string line = $"{yt.FullName} went {r[0]}-{r[1]}: {end}.";
                sections.Add(new(Yours, new[] { line }));
            }
        }

        if (CallLines(s, h.Year) is { Count: > 0 } calls) sections.Add(new(Calls, calls));

        // The carousel of the spring that followed (kept for four springs).
        var moves = s.Carousel.Where(m => m.Year == h.Year + 1).Take(6).Select(m => m.Why switch
        {
            "fired" => $"{m.Program} let {m.Out} go" + (m.Record.Length > 0 ? $" after {m.Record}" : "") + $"; {m.In} takes over.",
            "retired" => $"{m.Out} retired at {m.Program}; {m.In} takes over.",
            "left" => $"{m.Out} left {m.Program} for a bigger job; {m.In} takes over.",
            _ => $"{m.Program}: {m.Out} out, {m.In} in.",
        }).ToList();
        if (moves.Count > 0) sections.Add(new(Carousel, moves));

        return new AlmanacEdition(h.Year, SeasonLabel(h.Year), $"{h.Champion}, national champions", h.ChampionId, sections);
    }

    // ------------------------------------------------------------------------------------------
    // Shared

    /// <summary>The team you coached in a season: the one the columnists picked for you then, else today's team when
    /// <paramref name="currentFits"/> (you're employed and it played that season).</summary>
    private static int? TeamThatSeason(GameSession s, int season, bool currentFits)
    {
        var picked = s.Media.Columns.Picks.FirstOrDefault(p => p.Season == season);
        if (picked != null) return picked.TeamId;
        return s.Employed && currentFits ? s.HumanTeam.Id : null;
    }

    /// <summary>The columnists' judged picks for a season: each writer's call on your team, then the season's best call and
    /// worst miss across the table (W1's saved picks).</summary>
    public static List<string> CallLines(GameSession s, int season)
    {
        var picks = s.Media.Columns.Picks.Where(p => p.Season == season && p.Judged != null && p.Finished.Count > 0).ToList();
        if (picks.Count == 0) return new();
        int team = picks[0].TeamId;
        picks = picks.Where(p => p.TeamId == team).ToList();
        string Name(int id) => s.World.College?.Program(id)?.FullName ?? s.FindTeam(id)?.TheFullName ?? "?";
        var lines = new List<string>();
        foreach (var p in picks)
            if (p.PickOf(team) is > 0 and var pick && p.FinishOf(team) is > 0 and var fin)
                lines.Add($"{p.Name} ({p.Outlet}) picked {Name(team)} {Ord(pick)} in the {p.Table}; they finished {Ord(fin)}.");
        // The season's best call: the writer whose order was closest overall; the worst: the single biggest miss.
        var best = picks.Select(p => (p, err: p.Order.Sum(id => Math.Abs(p.PickOf(id) - Math.Max(1, p.FinishOf(id))))))
            .OrderBy(x => x.err).ThenBy(x => x.p.Key, StringComparer.Ordinal).First();
        var exact = best.p.Order.Where(id => best.p.PickOf(id) == best.p.FinishOf(id)).Select(Name).Take(3).ToList();
        lines.Add($"Best call: {best.p.Name}, the closest to the final {best.p.Table} table" + (exact.Count > 0 ? $" ({string.Join(", ", exact)} exactly right)." : "."));
        var miss = picks.SelectMany(p => p.Order.Where(id => p.FinishOf(id) > 0).Select(id => (p, id, d: Math.Abs(p.PickOf(id) - p.FinishOf(id)))))
            .OrderByDescending(x => x.d).ThenBy(x => x.p.Key, StringComparer.Ordinal).ThenBy(x => x.id).FirstOrDefault();
        if (miss.p != null && miss.d >= 3)
            lines.Add($"Worst call: {miss.p.Name} had {Name(miss.id)} {Ord(miss.p.PickOf(miss.id))}; they finished {Ord(miss.p.FinishOf(miss.id))}.");
        return lines;
    }

    /// <summary>The summer message's body: the cover, the first stories and your season, and where to read the rest.</summary>
    public static string MessageBody(AlmanacEdition e)
    {
        var lines = new List<string> { $"{e.Cover}." };
        if (e.Section(Stories) is { } st) lines.AddRange(st.Lines.Take(3));
        if (e.Section(Yours) is { } y) lines.Add(y.Lines[0]);
        if (e.Section(Calls) is { Lines.Count: > 0 } c) lines.Add(c.Lines.FirstOrDefault(l => l.StartsWith("Best call", StringComparison.Ordinal)) ?? c.Lines[0]);
        string inside = string.Join(" · ", e.Sections.Skip(1).Select(x => x.Title));
        return string.Join("\n", lines) + $"\n\nInside: {inside}. Every edition is kept on the History page's Almanac tab.";
    }
}

public sealed partial class GameSession
{
    /// <summary>The title of a season's almanac message ("The 2026-27 Almanac is out").</summary>
    public static string AlmanacTitle(AlmanacEdition e) => $"{e.Title} is out";

    /// <summary>W2 (run 225): at the start of the pros' summer, the season just finished gets its almanac, one message (a
    /// college career's is the college season whose champion was crowned this spring). Every edition stays on History.</summary>
    private void PostAlmanac()
    {
        AlmanacEdition? e = null;
        if (CollegeWords && World.College is { } cw)
        {
            if (cw.History.Count > 0 && cw.History.Max(h => h.Year) is var y && y + 1 == Today.Year)
                e = Almanac.College(this, cw.History.First(h => h.Year == y));
        }
        else if (Offseason?.Summary is { } sum) e = Almanac.Pro(this, sum);
        if (e == null || Inbox.Any(m => m.Title == AlmanacTitle(e))) return;
        Post(AlmanacTitle(e), Almanac.MessageBody(e), "League");
    }
}
