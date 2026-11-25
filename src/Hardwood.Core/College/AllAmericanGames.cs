using Hardwood.Core.Engine;
using Hardwood.Core.Model;

namespace Hardwood.Core.College;

/// <summary>One player's line in an All-American game.</summary>
public sealed record AllAmericanLine(int PlayerId, bool East, int Pts, int Reb, int Ast, int Min);

/// <summary>
/// CS11 (slice 5 of the revised launch plan, #1164, run 195): one of the season's two All-American games, East against West.
/// Names and schools are kept with the ids, so a player who has since turned pro (or left the game) is still named.
/// </summary>
public sealed class AllAmericanGame
{
    /// <summary>The college season (the year it began) this game closes.</summary>
    public int Year { get; init; }
    /// <summary><see cref="CollegeWorld.SeniorGame"/> or <see cref="CollegeWorld.HighSchoolGame"/>.</summary>
    public string Kind { get; init; } = "";
    public DateOnly Date { get; init; }
    public List<int> East { get; init; } = new();
    public List<int> West { get; init; } = new();
    /// <summary>Every player's name then (id → name).</summary>
    public Dictionary<int, string> Names { get; init; } = new();
    /// <summary>Where each player comes from: his program (seniors) or the program he has committed to (recruits; missing when uncommitted).</summary>
    public Dictionary<int, int> ProgramOf { get; init; } = new();
    /// <summary>Recruits only: "Oak Hill (VA)", his high school and state.</summary>
    public Dictionary<int, string> SchoolOf { get; init; } = new();
    public int? EastPts { get; set; }
    public int? WestPts { get; set; }
    public int? MvpId { get; set; }
    public List<AllAmericanLine> Box { get; set; } = new();

    public bool Played => EastPts != null && WestPts != null;
    public bool Includes(int playerId) => East.Contains(playerId) || West.Contains(playerId);
    public string NameOf(int id) => Names.TryGetValue(id, out var n) ? n : "-";
    public string Title => CollegeWorld.AllAmericanTitle(Kind);
    /// <summary>"East 102, West 97" (the winner first), or "" before the game.</summary>
    public string ScoreLine => !Played ? "" : EastPts >= WestPts ? $"East {EastPts}, West {WestPts}" : $"West {WestPts}, East {EastPts}";
    /// <summary>"24 pts, 7 reb, 5 ast" for a player in the box (or "").</summary>
    public string LineOf(int id) => Box.FirstOrDefault(b => b.PlayerId == id) is { } b ? $"{b.Pts} pts, {b.Reb} reb, {b.Ast} ast" : "";
}

/// <summary>
/// CS11 (run 195): the All-American games, two showcase games each spring, East against West on a neutral floor.
/// <list type="bullet">
/// <item><b>The College All-American Game</b> (<see cref="SeniorGame"/>): the 20 best seniors whose programs aren't in the
/// Final Four or the NIT's last four, picked the Monday after the Elite Eight and played on the Friday before the semi-finals.</item>
/// <item><b>The Hardwood All-American Classic</b> (<see cref="HighSchoolGame"/>, a fictional name): the 24 best high-school
/// seniors of the class that enrolls next summer, picked on February 1 and played on March 31 in Chicago.</item>
/// </list>
/// East and West follow home (the program's state for seniors, the recruit's for the Classic), ten or twelve a side, with any
/// overflow sent to the short side. Each game plays the full engine on its own random stream (a hash of the season and the
/// game) and with no date, so it rolls no injuries, records no stats and moves nobody else's dice; it changes no rating.
/// What it adds is honour and story: the rosters and results in the news, your own players' picks and the MVP in your inbox,
/// a line in each player's honours, and the two MVPs on the Awards page.
/// </summary>
public sealed partial class CollegeWorld
{
    public const string SeniorGame = "Seniors", HighSchoolGame = "HighSchool";
    public const int SeniorsASide = 10, RecruitsASide = 12;
    public const string ClassicCity = "Chicago";

    /// <summary>Every All-American game, oldest first (saved with the college world; none before v0.183.0).</summary>
    public List<AllAmericanGame> AllAmerican { get; } = new();

    public static string AllAmericanTitle(string kind) => kind == HighSchoolGame ? "Hardwood All-American Classic" : "College All-American Game";

    /// <summary>The day each game's rosters are named and the day it is played, for the season that began in <paramref name="year"/>.</summary>
    public static (DateOnly Named, DateOnly Played) AllAmericanDates(string kind, int year) => kind == HighSchoolGame
        ? (new DateOnly(year + 1, 2, 1), new DateOnly(year + 1, 3, 31))
        : (new DateOnly(year + 1, 3, 30), new DateOnly(year + 1, 4, 3));

    /// <summary>This season's game of a kind, once its rosters are named.</summary>
    public AllAmericanGame? AllAmericanOf(string kind, int? year = null) =>
        AllAmerican.FirstOrDefault(g => g.Kind == kind && g.Year == (year ?? Season.Year));

    /// <summary>East or West for a state: the Northeast and Southeast play East; the Midwest, Southwest and West play West.</summary>
    public static bool IsEast(string state) => RegionOf(state) is "Northeast" or "Southeast";

    /// <summary>Name the rosters and play the games whose day has come (called as the college calendar moves to <paramref name="date"/>).</summary>
    public void AllAmericanThrough(DateOnly date)
    {
        int year = Season.Year;
        foreach (var kind in new[] { HighSchoolGame, SeniorGame })
        {
            var (named, played) = AllAmericanDates(kind, year);
            var game = AllAmericanOf(kind);
            if (game == null && date >= named && date <= played.AddDays(7))
            {
                game = kind == HighSchoolGame ? PickClassic(year, played) : PickSeniors(year, played);
                if (game == null) continue;
                AllAmerican.Add(game);
                RostersNews(game);
            }
            if (game is { Played: false } && date >= game.Date) PlayAllAmerican(game);
        }
    }

    /// <summary>The 20 best seniors (by ability, then the season's scoring) on programs that aren't in the Final Four, healthy on game day.</summary>
    private AllAmericanGame? PickSeniors(int year, DateOnly day)
    {
        // Still playing: the Final Four, and the NIT's semi-finalists (its final is the same night).
        var fourth = Season.Games.Where(g => (g.Event == Madness && g.Round >= 5) || (g.Event == Nit && g.Round >= 3))
            .SelectMany(g => new[] { g.HomeId, g.AwayId }).ToHashSet();
        var pool = Programs.Where(t => !fourth.Contains(t.Id))
            .SelectMany(t => t.Roster.Where(p => p.Age(SummerAfter(year)) >= GraduateAge && p.IsAvailable(day)).Select(p => (P: p, T: t)))
            .Select(x => (x.P, x.T, Score: x.P.CurrentAbility + (Season.Stats.TryGetValue(x.P.Id, out var st) && st.G > 0 ? st.Ppg * 0.25 : 0)))
            .OrderByDescending(x => x.Score).ThenBy(x => x.P.Id).Take(2 * SeniorsASide).ToList();
        if (pool.Count < 2 * SeniorsASide) return null;
        var (east, west) = Split(pool.Select(x => (x.P.Id, IsEast(HomeState(x.T)))), SeniorsASide);
        return new AllAmericanGame
        {
            Year = year, Kind = SeniorGame, Date = day, East = east, West = west,
            Names = pool.ToDictionary(x => x.P.Id, x => x.P.Name),
            ProgramOf = pool.ToDictionary(x => x.P.Id, x => x.T.Id),
        };
    }

    /// <summary>The 24 best high-school seniors of the class that enrolls next summer (by the services' rank), healthy on game day.</summary>
    private AllAmericanGame? PickClassic(int year, DateOnly day)
    {
        int classOf = year + 1;
        var pool = Prospects.Where(p => Recruits.TryGetValue(p.Id, out var r) && r.ClassOf == classOf && r.Rank > 0 && p.IsAvailable(day))
            .Select(p => (P: p, R: Recruits[p.Id])).OrderBy(x => x.R.Rank).ThenBy(x => x.P.Id).Take(2 * RecruitsASide).ToList();
        if (pool.Count < 2 * RecruitsASide) return null;
        var (east, west) = Split(pool.Select(x => (x.P.Id, IsEast(x.R.State))), RecruitsASide);
        return new AllAmericanGame
        {
            Year = year, Kind = HighSchoolGame, Date = day, East = east, West = west,
            Names = pool.ToDictionary(x => x.P.Id, x => x.P.Name),
            ProgramOf = pool.Where(x => x.R.CommittedTo != null).ToDictionary(x => x.P.Id, x => x.R.CommittedTo!.Value),
            SchoolOf = pool.ToDictionary(x => x.P.Id, x => $"{x.R.HighSchool} ({x.R.State})"),
        };
    }

    /// <summary>East and West by home, best first; once a side is full the rest go to the other.</summary>
    public static (List<int> East, List<int> West) Split(IEnumerable<(int Id, bool East)> best, int perSide)
    {
        var east = new List<int>(); var west = new List<int>();
        foreach (var (id, e) in best)
        {
            if ((e && east.Count < perSide) || west.Count >= perSide) east.Add(id); else west.Add(id);
        }
        return (east, west);
    }

    /// <summary>An All-American game's own random stream: a hash of the season and the game.</summary>
    private static ulong AllAmericanSeed(AllAmericanGame g)
    {
        ulong x = 0xA11A3E1CA5UL;
        foreach (int v in new[] { g.Year, g.Kind == SeniorGame ? 1 : 2 }.Concat(g.East).Concat(g.West)) x = (x ^ (ulong)(uint)v) * 0x100000001B3UL;
        return x;
    }

    private void PlayAllAmerican(AllAmericanGame g)
    {
        var all = Players.GroupBy(p => p.Id).ToDictionary(x => x.Key, x => x.First());
        Team Side(bool east, List<int> ids)
        {
            var t = new Team { Id = east ? -195_001 : -195_002, City = east ? "East" : "West", Name = "All-Americans", Abbreviation = east ? "EAST" : "WEST", Division = "" };
            foreach (var id in ids) if (all.TryGetValue(id, out var p)) t.Roster.Add(p);
            return t;
        }
        var east = Side(true, g.East); var west = Side(false, g.West);
        if (east.Roster.Count < 5 || west.Roster.Count < 5) { g.EastPts = g.WestPts = 0; return; } // a class that left the game (never in practice)
        // No date: the engine rolls injuries only for a dated game, and this one counts for nothing but pride.
        var r = new GameSimulator().Simulate(east, west, Rules.CollegeNeutral, AllAmericanSeed(g), false, null);
        g.EastPts = r.Home.Points; g.WestPts = r.Away.Points;
        g.Box = r.Home.Lines.Select(l => (l, true)).Concat(r.Away.Lines.Select(l => (l, false)))
            .Select(x => new AllAmericanLine(x.l.Player.Id, x.Item2, x.l.Points, x.l.Rebounds, x.l.Assists, (int)Math.Round(x.l.Minutes))).ToList();
        bool eastWon = g.EastPts > g.WestPts;
        g.MvpId = g.Box.Where(b => b.East == eastWon).OrderByDescending(b => b.Pts + 0.6 * b.Reb + 0.8 * b.Ast).ThenBy(b => b.PlayerId).FirstOrDefault()?.PlayerId;
        ResultNews(g);
    }

    /// <summary>"Jalen Brooks (Duke)" for a senior; "Jalen Brooks (Oak Hill, VA; Duke signee)" for a recruit.</summary>
    private string Who(AllAmericanGame g, int id)
    {
        string? prog = g.ProgramOf.TryGetValue(id, out var t) ? Program(t)?.FullName : null;
        if (g.Kind == SeniorGame) return $"{g.NameOf(id)} ({prog ?? "-"})";
        string school = g.SchoolOf.GetValueOrDefault(id, "");
        return $"{g.NameOf(id)} ({school}{(prog != null ? $"; {prog} commit" : "")})";
    }

    private void RostersNews(AllAmericanGame g)
    {
        string where = g.Kind == SeniorGame ? "at the Final Four" : $"in {ClassicCity}";
        string what = g.Kind == SeniorGame
            ? $"The best seniors outside the Final Four play one last college game, East against West, {where} on {g.Date:MMMM d}."
            : $"The top {2 * RecruitsASide} high-school seniors in the country, East against West, {where} on {g.Date:MMMM d}.";
        News.Add(($"The {g.Title} rosters are named", what + $"\nEast: {string.Join(", ", g.East.Select(id => Who(g, id)))}.\nWest: {string.Join(", ", g.West.Select(id => Who(g, id)))}."));
        if (ManagedProgramId is not { } me || Program(me) is not { } us) return;
        var ours = g.East.Concat(g.West).Where(id => g.ProgramOf.GetValueOrDefault(id) == me).ToList();
        if (g.Kind == SeniorGame)
        {
            if (ours.Count == 0) return;
            string names = string.Join(" and ", ours.Select(g.NameOf));
            CoachNews.Add((ours.Count == 1 ? $"{names} is a College All-American Game pick" : $"{ours.Count} of our seniors are College All-American Game picks",
                $"{names} {(ours.Count == 1 ? "has" : "have")} been picked for the {g.Title} {where} on {g.Date:MMMM d}: one more game in {us.FullName}'s colours, in front of every pro scout in the building."));
        }
        else
        {
            var courted = g.East.Concat(g.West).Where(id => !ours.Contains(id) && Recruits.TryGetValue(id, out var r) && r.CourtedBy == me && (r.OnBoard || r.Offered)).ToList();
            if (ours.Count == 0 && courted.Count == 0) return;
            var lines = new List<string>();
            if (ours.Count > 0) lines.Add($"Committed to us: {string.Join(", ", ours.Select(id => $"{g.NameOf(id)} ({g.SchoolOf.GetValueOrDefault(id, "")})"))}.");
            if (courted.Count > 0) lines.Add($"On our board: {string.Join(", ", courted.Select(id => Who(g, id)))}.");
            CoachNews.Add((ours.Count > 0 ? $"{(ours.Count == 1 ? "One of our signees is" : $"{ours.Count} of our signees are")} Hardwood All-Americans" : "Recruits on our board are Hardwood All-Americans",
                $"The {g.Title} rosters are out ({g.Date:MMMM d}, {ClassicCity}).\n" + string.Join("\n", lines)));
        }
    }

    private void ResultNews(AllAmericanGame g)
    {
        if (!g.Played || g.MvpId is not { } mvp) return;
        bool eastWon = g.EastPts > g.WestPts;
        string winner = eastWon ? "East" : "West", loser = eastWon ? "West" : "East";
        int wp = Math.Max(g.EastPts!.Value, g.WestPts!.Value), lp = Math.Min(g.EastPts!.Value, g.WestPts!.Value);
        var top = g.Box.Where(b => b.PlayerId != mvp).OrderByDescending(b => b.Pts).ThenBy(b => b.PlayerId).Take(2).Select(b => $"{g.NameOf(b.PlayerId)} {b.Pts}");
        News.Add(($"{winner} win the {g.Title}, {wp}-{lp}", $"MVP: {Who(g, mvp)}, {g.LineOf(mvp)}. Also: {string.Join(", ", top)}."));
        if (ManagedProgramId is not { } me || Program(me) is not { } us) return;
        var ours = g.Box.Where(b => g.ProgramOf.GetValueOrDefault(b.PlayerId) == me).OrderByDescending(b => b.Pts).ToList();
        if (ours.Count == 0) return;
        string body = string.Join("\n", ours.Select(b => $"{g.NameOf(b.PlayerId)} ({(b.East ? "East" : "West")}): {b.Pts} pts, {b.Reb} reb, {b.Ast} ast in {b.Min} min."));
        string title = ours.Any(b => b.PlayerId == mvp)
            ? (g.Kind == SeniorGame ? $"{g.NameOf(mvp)} is the College All-American Game MVP" : $"Our signee {g.NameOf(mvp)} is the Classic's MVP")
            : $"{g.Title}: {winner} {wp}, {loser} {lp}";
        CoachNews.Add((title, body + (g.Kind == HighSchoolGame ? $"\nThey join {us.FullName} this summer." : "")));
    }

    /// <summary>A player's All-American game honours, newest first ("College All-American Game MVP 2026-27").</summary>
    public IEnumerable<string> AllAmericanHonoursOf(int playerId) =>
        AllAmerican.Where(g => g.Includes(playerId)).OrderByDescending(g => g.Year).ThenBy(g => g.Kind == SeniorGame ? 0 : 1)
            .Select(g => $"{g.Title}{(g.MvpId == playerId ? " MVP" : "")} {CollegeAwards.SeasonText(g.Year)}");
}
