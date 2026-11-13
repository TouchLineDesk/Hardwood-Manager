using Hardwood.Core.Engine;
using Hardwood.Core.Model;
using Hardwood.Core.World;

namespace Hardwood.Core.Season;

public sealed record BookTableRow(Team Team, int W, int L, int PointsFor, int PointsAgainst, string Group, string Zone)
{
    public int G => W + L;
    public double Pct => G == 0 ? 0 : (double)W / G;
    public double Diff => G == 0 ? 0 : (double)(PointsFor - PointsAgainst) / G;
}

public sealed record BookGame(DateOnly Date, Team Home, Team Away, int? HomePts, int? AwayPts, string Stage, GameResult? Box)
{
    public bool Played => HomePts != null;
    public bool Involves(Team t) => Home == t || Away == t;
}

/// <summary>A player's season in one competition, in one shape for the NABL, the world and college.</summary>
public sealed record BookStat(int PlayerId, int TeamId, int G, double Min, int Pts, int Reb, int Ast, int Stl, int Blk, int Fgm, int Fga, int Tpm, int Tpa)
{
    public double Mpg => G == 0 ? 0 : Min / G;
    public double Ppg => G == 0 ? 0 : (double)Pts / G;
    public double Rpg => G == 0 ? 0 : (double)Reb / G;
    public double Apg => G == 0 ? 0 : (double)Ast / G;
    public double Spg => G == 0 ? 0 : (double)Stl / G;
    public double Bpg => G == 0 ? 0 : (double)Blk / G;
    public double FgPct => Fga == 0 ? 0 : (double)Fgm / Fga;
    public double TpPct => Tpa == 0 ? 0 : (double)Tpm / Tpa;
}

/// <summary>
/// One league seen the same way whatever runs it (the NABL session, a world competition, college): table, games,
/// player stats, rules and format, and champions. Used by the league pages and the team page.
/// </summary>
public sealed class LeagueBook
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required string Region { get; init; }
    public required string Season { get; init; }
    public required string Status { get; init; }
    public required IReadOnlyList<Team> Teams { get; init; }
    public required Func<List<BookTableRow>> Table { get; init; }
    public required Func<List<BookGame>> Games { get; init; }
    public required Func<List<BookStat>> Stats { get; init; }
    public required IReadOnlyList<(string Label, string Value)> Rules { get; init; }
    public required IReadOnlyList<string> Champions { get; init; }
    /// <summary>Playoff series so far, one line each (empty before the playoffs).</summary>
    public required Func<List<string>> Series { get; init; }

    public const string NablKey = "nabl", CollegeKey = "college";

    // ------------------------------------------------------------------------------------------

    /// <summary>Every league in the save: the NABL, every world competition that plays, and college.</summary>
    public static IEnumerable<LeagueBook> All(GameSession s)
    {
        yield return Nabl(s);
        foreach (var c in s.World.Playing) yield return World(s, c);
        if (s.World.College != null) yield return College(s);
    }

    public static LeagueBook? Get(GameSession s, string key) => key == NablKey ? Nabl(s) : key == CollegeKey ? (s.World.College != null ? College(s) : null)
        : s.World.Competitions.FirstOrDefault(c => c.Key == key && c.Plays) is { } c ? World(s, c) : null;

    /// <summary>The league a team plays its domestic games in (NABL, world league, or college).</summary>
    public static LeagueBook? HomeOf(GameSession s, Team t)
    {
        if (s.League.Teams.Contains(t)) return Nabl(s);
        if (s.World.College?.Programs.Contains(t) == true) return College(s);
        var c = s.World.LeagueOf(t) ?? s.World.Competitions.FirstOrDefault(x => x.Clubs.Contains(t));
        return c != null && c.Plays ? World(s, c) : null;
    }

    /// <summary>Every competition a team is in this season (its league plus any cup).</summary>
    public static IEnumerable<LeagueBook> Of(GameSession s, Team t)
    {
        if (HomeOf(s, t) is { } home) yield return home;
        foreach (var c in s.World.Playing.Where(c => c.Profile.Kind == CompetitionKind.Continental && c.Season.Participants.Contains(t.Id)))
            yield return World(s, c);
    }

    // ---- the NABL

    private static LeagueBook Nabl(GameSession s)
    {
        var lg = s.League;
        var post = s.Postseason;
        var fin = lg.Finance;
        string Group(Team t) => $"{t.Conference} · {t.Division}";
        return new LeagueBook
        {
            Key = NablKey, Name = Hardwood.Core.Names.Brand.Top, Region = "USA", Season = $"{lg.Season}-{(lg.Season + 1) % 100:00}",
            Status = post?.Champion is { } ch ? $"Champions: {ch.FullName}" : post != null ? post.Stage : s.Phase == SessionPhase.Offseason ? "Offseason" : "Regular season",
            Teams = lg.Teams,
            Table = () =>
            {
                // By division (Danny, #318): playoff places still come from the conference table; the last in each
                // division drops to tier 2 when the world has a pyramid (#319).
                bool pyramid = s.World.Competitions.Any(c => c.Key == UsaPyramid.Tier2);
                bool divisional = Postseason.UsesDivisions(lg);
                var rows = new List<BookTableRow>();
                foreach (var conf in Enum.GetValues<Conference>())
                {
                    var confTable = s.Standings.Conference(conf);
                    var confRank = confTable.Select((r, i) => (r.Team, i)).ToDictionary(x => x.Team, x => x.i + 1);
                    foreach (var div in confTable.GroupBy(r => r.Team.Division).OrderBy(g => g.Key))
                    {
                        var list = div.ToList();
                        for (int i = 0; i < list.Count; i++)
                        {
                            var r = list[i]; int rank = confRank[r.Team];
                            bool started = r.Wins + r.Losses > 0;
                            string zone = pyramid && i == list.Count - 1 && list.Count > 1 && started ? "Relegation"
                                : divisional ? DivisionZone(i + 1)
                                : rank <= 6 ? "Playoffs" : rank <= 10 ? "Play-in" : "";
                            rows.Add(new(r.Team, r.Wins, r.Losses, r.PointsFor, r.PointsAgainst, Group(r.Team), zone));
                        }
                    }
                }
                return rows;
            },
            Games = () =>
            {
                var games = s.Schedule.Select(g => s.Results.TryGetValue(g.Index, out var r)
                    ? new BookGame(g.Date, g.Home, g.Away, r.Home.Points, r.Away.Points, "Regular season", r)
                    : new BookGame(g.Date, g.Home, g.Away, null, null, "Regular season", null)).ToList();
                if (post != null)
                    foreach (var se in post.Series)
                        foreach (var r in se.Games)
                            games.Add(new(r.Date ?? DateOnly.MinValue, r.Home.Team, r.Away.Team, r.Home.Points, r.Away.Points, se.Round, r));
                return games.OrderBy(g => g.Date).ToList();
            },
            Stats = () => s.Stats.Values.Select(p => new BookStat(p.PlayerId, p.TeamId, p.Games, p.Minutes, p.Points, p.OReb + p.DReb, p.Assists, p.Steals, p.Blocks,
                p.FGM, p.FGA, p.ThreePM, p.ThreePA)).ToList(),
            Series = () => post == null ? new() : post.Series.Select(x => $"{x.Round}: {x.Higher.Abbreviation} {x.HigherWins}-{x.LowerWins} {x.Lower.Abbreviation}"
                + (x.Complete ? $" ({x.Winner!.Abbreviation} win)" : "")).ToList(),
            Rules = new List<(string, string)>
            {
                ("Level", lg.Teams.Count == UsaPyramid.ProSize
                    ? $"The top league in the USA: two conferences of four regional divisions of four. The 4th-placed team in each division drops into the same division of {The(Hardwood.Core.World.Competitions.Get(Hardwood.Core.World.UsaPyramid.Tier2).Name)}, and that division's winner takes its place. Divisions keep their clubs from season to season."
                    : $"The top league in the USA, in six divisions. The last team in each division drops to {Hardwood.Core.World.Competitions.Get(Hardwood.Core.World.UsaPyramid.Tier2).Name}; its division winners come up."),
                ("Game", RulesText(lg.Rules)),
                ("Season", $"{lg.Teams.Count} teams in two conferences of {lg.Teams.Select(t => t.Division).Distinct().Count() / 2} divisions; 82 games from late October to mid-April."),
                ("Playoffs", Postseason.UsesDivisions(lg)
                    ? $"The top two in each division (16 teams), seeded 1-8 by record within their conference. Four rounds, best of seven. The eight third-placed teams play the {Postseason.ThirdPlaceName} (best of three) for a trophy and {Franchise.Money(Postseason.ThirdPlacePrizeFor(true))} in prize money."
                    : "Top 6 in each conference go straight in; 7th-10th play the play-in for the last two places. Four rounds, best of seven."),
                ("Rosters", $"{fin.RosterMin}-{fin.RosterMax} players. Americans turn pro at 23 (college before that)."),
                ("Money", fin.OpenMarket
                    ? (Hardwood.Core.RealWorld.RealWorldData.IsReal(lg) ? "Open market: no salary cap. Each club spends within a wage budget set by its owner and revenue; transfers in the summer and January. A one-round college draft each summer, before free agency: the top three in each division and the clubs coming up, in an equal-odds lottery." : "Open market: no draft and no salary cap. Each club spends within a wage budget set by its owner and revenue; transfers in the summer and January.")
                    : $"Salary cap ${fin.HardCap / 1e6:F0}M."),
            },
            Champions = lg.History.AsEnumerable().Reverse().Select(h =>
                $"{h.Season}-{(h.Season + 1) % 100:00}  {s.World.TeamById(h.ChampionTeamId)?.FullName ?? lg.Teams.FirstOrDefault(t => t.Id == h.ChampionTeamId)?.FullName ?? "?"}"
                + ((s.World.TeamById(h.RunnerUpTeamId) ?? lg.Teams.FirstOrDefault(t => t.Id == h.RunnerUpTeamId)) is { } ru ? $" (beat {ru.FullName})" : "")
                + (h.Awards?.ThirdPlaceWinnerId is { } tp && (s.World.TeamById(tp) ?? lg.Teams.FirstOrDefault(t => t.Id == tp)) is { } tpt ? $" · third-place tournament: {tpt.FullName}" : "")).ToList(),
        };
    }

    // ---- a world competition

    private static LeagueBook World(GameSession s, WorldCompetition c)
    {
        var w = s.World;
        var p = c.Profile;
        var se = c.Season;
        Team T(int id) => w.TeamById(id)!;
        var teams = se.Participants.Select(id => w.TeamById(id)).OfType<Team>().ToList();
        int playoffTeams = p.Playoffs.Length > 0 ? p.Playoffs[0].Teams : 0;
        bool pyramid = UsaPyramid.IsPyramid(p.Key);
        return new LeagueBook
        {
            Key = p.Key, Name = p.Name, Region = Competitions.RegionOf(p), Season = $"{se.Year}-{(se.Year + 1) % 100:00}",
            Status = se.IsComplete && se.ChampionId is { } ch ? $"Champions: {T(ch).FullName}" : se.Stage,
            Teams = teams,
            Table = () =>
            {
                int rank = 0;
                var table = se.Table();
                if (!pyramid)
                    return table.Select(r =>
                    {
                        rank++;
                        return new BookTableRow(T(r.TeamId), r.Wins, r.Losses, r.PointsFor, r.PointsAgainst, "", playoffTeams > 0 && rank <= playoffTeams ? "Playoffs" : "");
                    }).ToList();
                // The pyramid by division (Danny, #318-#319), each in table order, divisions in the order they're listed.
                var clubs = table.Select(r => T(r.TeamId)).ToList();
                var order = table.Select((r, i) => (r.TeamId, i)).ToDictionary(x => x.TeamId, x => x.i);
                var rows = new List<BookTableRow>();
                foreach (var (name, members) in UsaPyramid.Divisions(p.Key, clubs))
                {
                    var div = table.Where(r => members.Any(m => m.Id == r.TeamId)).ToList();
                    int k = 0;
                    foreach (var r in div)
                    {
                        k++;
                        rows.Add(new BookTableRow(T(r.TeamId), r.Wins, r.Losses, r.PointsFor, r.PointsAgainst, name,
                            PyramidZone(p.Key, order[r.TeamId] + 1, k, div.Count) ?? ""));
                    }
                }
                return rows;
            },
            Games = () => se.Fixtures.OrderBy(f => f.Date).ThenBy(f => f.Index).Select(f =>
            {
                s.AbroadResults.TryGetValue($"{c.Key}:{f.Index}", out var box);
                return new BookGame(f.Date, T(f.HomeId), T(f.AwayId), f.HomePts, f.AwayPts, f.Stage, box);
            }).ToList(),
            Stats = () => se.Stats.Select(kv => new BookStat(kv.Key, kv.Value.TeamId, kv.Value.G, kv.Value.Min, kv.Value.Pts, kv.Value.Reb, kv.Value.Ast,
                kv.Value.Stl, kv.Value.Blk, kv.Value.Fgm, kv.Value.Fga, kv.Value.Tpm, kv.Value.Tpa)).ToList(),
            Series = () => se.Series.Select(x => $"{x.Round}: {T(x.HighId).Abbreviation} {x.HighWins}-{x.LowWins} {T(x.LowId).Abbreviation}"
                + (x.Complete ? $" ({T(x.WinnerId!.Value).Abbreviation} win)" : "")).ToList(),
            Rules = WorldRules(c, teams.Count),
            Champions = c.History.AsEnumerable().Reverse().Select(h => $"{h.Season}-{(h.Season + 1) % 100:00}  {h.ChampionName} (beat {h.RunnerUpName})"
                + (h.ThirdPlaceName is { } tp ? $" · third-place tournament: {tp}" : "")).ToList(),
        };
    }

    /// <summary>A zone by place in a division in the division format (D36): the top two make the playoffs, third plays the
    /// third-place tournament (4th, relegated, is marked by the caller where there's a league below).</summary>
    public static string DivisionZone(int placeInDivision) => placeInDivision <= 2 ? "Playoffs" : placeInDivision == 3 ? "Third place" : "";

    /// <summary>A pyramid club's zone (#319): in tiers 2-5 each division winner goes up and the last in each division goes down
    /// (not from tier 5, the bottom); 2nd makes the playoffs and 3rd plays the third-place tournament (D36, D39 A). An older
    /// career's regional and local leagues (until its next summer) send their champion up and the last in each division of a
    /// regional league down.</summary>
    private static string? PyramidZone(string key, int overallRank, int rankInDivision, int divisionSize)
    {
        if (UsaPyramid.IsNational(key))
        {
            if (rankInDivision == 1) return "Promotion";
            if (key != UsaPyramid.Tier5 && divisionSize > 1 && rankInDivision == divisionSize) return "Relegation";
            return divisionSize == 4 && DivisionZone(rankInDivision) is { Length: > 0 } z ? z : null;
        }
        int level = UsaPyramid.Level(key);
        if (overallRank == 1) return "Promotion";
        if (level == 4 && divisionSize > 1 && rankInDivision == divisionSize) return "Relegation";
        return null;
    }

    /// <summary>"the NBA G League", but "The Championship" as it is.</summary>
    private static string The(string name) => name.StartsWith("The ") ? name : "the " + name;

    private static string TierName(string key) => Hardwood.Core.World.Competitions.All.First(c => c.Key == key).Name;

    private static List<(string, string)> WorldRules(WorldCompetition c, int clubs)
    {
        var p = c.Profile;
        var list = new List<(string, string)>();
        if (p.Kind == CompetitionKind.Continental)
        {
            list.Add(("Format", $"A club cup of {clubs} clubs from: {string.Join(", ", p.Entrants.Select(e => $"{Competitions.All.FirstOrDefault(x => x.Key == e.LeagueKey)?.Name ?? e.LeagueKey} ({e.Clubs})"))}. Clubs qualify by last season's table."));
        }
        else
        {
            string level = UsaPyramid.IsPyramid(p.Key) ? UsaPyramid.Level(p.Key) switch
            {
                2 => $"Tier 2 of the USA pyramid: an East and a West conference, each with four regional divisions. Each division winner goes up to the same division of the {Hardwood.Core.Names.Brand.Top}; the 4th-placed team in each division drops to the same division of {The(TierName(UsaPyramid.Tier3))}. Divisions keep their clubs from season to season.",
                3 => $"Tier 3: an East and a West conference, each with four regional divisions. Each division winner goes up to the same division of {The(TierName(UsaPyramid.Tier2))}; the 4th-placed team in each division drops to the same division of {The(TierName(UsaPyramid.Tier4))}.",
                4 when UsaPyramid.IsNational(p.Key) => $"Tier 4: an East and a West conference, each with four regional divisions. Each division winner goes up to the same division of {The(TierName(UsaPyramid.Tier3))}; the 4th-placed team in each division drops to the same division of {The(TierName(UsaPyramid.Tier5))}.",
                5 when UsaPyramid.IsNational(p.Key) => $"Tier 5, the bottom of the pyramid: an East and a West conference, each with four regional divisions. Each division winner goes up to the same division of {The(TierName(UsaPyramid.Tier4))}; nobody goes down.",
                4 => "Regional (tier 4, semi-pro), one division per local area. This league becomes part of the 32-club tier 4 at the end of the season.",
                _ => "Local (tier 5, near-amateur). This league becomes part of the 32-club tier 5 at the end of the season.",
            } : "Top flight. One division, no promotion or relegation.";
            list.Add(("Level", level));
            if (p.ForeignLimit < 99)
                list.Add(("Imports", $"Up to {p.ForeignLimit} players per club from outside {(p.LocalNationalities.Length > 0 ? string.Join("/", p.LocalNationalities) : p.Country)}" + (p.ImportsAreStars ? ", usually the team's stars." : ".")));
            if (p.MinAge > 0) list.Add(("Pro age", $"Players must be {p.MinAge} or older."));
            list.Add(("Wages", $"About {p.SalaryScale * 100:0.#}% of what the {Hardwood.Core.Names.Brand.Top} pays the same player."));
        }
        list.Add(("Game", RulesText(p.Rules)));
        int perOpp = p.RoundRobins;
        list.Add(("Season", $"{(perOpp == 1 ? "Everyone plays everyone once" : perOpp == 2 ? "Home and away against everyone" : $"Everyone plays everyone {perOpp} times")}"
                           + $" ({(clubs - 1) * perOpp} games), from {new DateOnly(2000, p.Start.Month, p.Start.Day):MMMM d}."));
        list.Add(("Playoffs", p.DivisionPlayoffs && clubs == UsaPyramid.ProSize
            ? $"The top two in each division (16 teams), seeded 1-8 by record within their conference: first round and conference semi-finals best of five, conference finals and the final best of seven. The eight third-placed teams play the {Postseason.ThirdPlaceName} (best of three) for a trophy and prize money."
            : p.DivisionPlayoffs ? "None this season: the table decides the title (the division playoffs start once the league has four divisions of four in each conference)."
            : p.Playoffs.Length == 0 ? "None: the table decides the title."
            : $"Top {p.Playoffs[0].Teams}: " + string.Join(", ", p.Playoffs.Select(r => r.BestOf == 1 ? $"{r.Name} (one game)" : $"{r.Name} (best of {r.BestOf})"))
              + (p.FinalFour ? ". The last four meet at a Final Four in late May." : ".")));
        list.Add(("Simulation", c.FullSim ? "Full match engine (box scores for every game)." : "Quick results (scores and player lines, no box scores)."));
        return list;
    }

    private static string RulesText(Rules r) =>
        $"{r.Name} rules: {r.Periods} × {r.MinutesPerPeriod} minutes, {r.ShotClockSeconds}-second shot clock, foul out on {r.FoulsToFoulOut}, three-point line {r.ThreePointDistanceM:0.00} m.";

    // ---- college

    private static LeagueBook College(GameSession s)
    {
        var col = s.World.College!;
        var se = col.Season;
        Team T(int id) => col.Program(id)!;
        return new LeagueBook
        {
            Key = CollegeKey, Name = "College basketball", Region = "USA", Season = $"{se.Year}-{(se.Year + 1) % 100:00}",
            Status = col.StageText, Teams = col.Programs,
            Table = () => Hardwood.Core.College.CollegeWorld.Conferences.SelectMany(conf => col.ConferenceTable(conf.Key).Select(r =>
            {
                var (w, l) = col.Record(r.TeamId);
                return new BookTableRow(T(r.TeamId), w, l, 0, 0, conf.Short, $"{r.Wins}-{r.Losses} in conf");
            })).ToList(),
            Games = () => se.Games.OrderBy(g => g.Date).Select(g => new BookGame(g.Date, T(g.HomeId), T(g.AwayId), g.HomePts, g.AwayPts, g.Event ?? g.Stage, null)).ToList(),
            Stats = () => se.Stats.Select(kv => new BookStat(kv.Key, kv.Value.TeamId, kv.Value.G, kv.Value.Min, kv.Value.Pts, kv.Value.Reb, kv.Value.Ast,
                kv.Value.Stl, kv.Value.Blk, kv.Value.Fgm, kv.Value.Fga, kv.Value.Tpm, kv.Value.Tpa)).ToList(),
            Series = () => new(),
            Rules = new List<(string, string)>
            {
                ("Format", col.Recruiting
                    ? $"{col.Programs.Count} programs in {col.Active.Count()} conferences, each with {Hardwood.Core.College.CollegeWorld.Scholarships} scholarships and {Hardwood.Core.College.CollegeWorld.WalkOns} walk-ons. Americans play high school from 14 to 17, sign with a college as seniors, enroll at 18 and turn pro at 23."
                    : $"{col.Programs.Count} programs in {col.Active.Count()} conferences. Every American from 14 to 22 plays here; at 23 they turn pro."),
                ("Game", RulesText(Model.Rules.College)),
                ("Season", "Non-conference games from November, Thanksgiving invitationals, December showcases, then a conference round robin from January and conference tournaments in March."),
                ("March Madness", "68 teams: the 8 conference tournament winners plus 60 at-large. First Four, then six rounds to the national title. Played on the full match engine."),
                ("Recruiting", "Programs recruit 14-year-olds by prestige; winning raises prestige."),
            },
            Champions = col.History.AsEnumerable().Reverse().Select(h => $"{h.Year}-{(h.Year + 1) % 100:00}  {h.Champion} (beat {h.RunnerUp})").ToList(),
        };
    }
}
