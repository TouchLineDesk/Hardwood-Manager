using Hardwood.Core.International;

namespace Hardwood.Core.Season;

public sealed partial class GameSession
{
    /// <summary>
    /// International plan I2 (run 209): a World Cup that ended over the summer is played at the summer's jump to the new season
    /// (the date holds in May and jumps to October, run 197's lesson), once, from its own stream (<see cref="WorldCup.Play(GameSession, int, Engine.GameSimulator?)"/>).
    /// Nothing the shared streams, players or clubs see changes: the inbox gets the result.
    /// </summary>
    /// <remarks>Public for the tests (run 211); the session calls it once at the summer's jump.</remarks>
    public void PlayInternationalOverSummer(DateOnly summer, DateOnly newYear)
    {
        _settled.Clear();
        _jumpDay = newYear;
        for (int y = summer.Year; y <= newYear.Year; y++)
        {
            PlayOlympicsOverSummer(y, summer, newYear);
            PlayContinentalsOverSummer(y, summer, newYear);
            PlayU23OverSummer(y, summer, newYear);
            if (WorldCup.EventIn(y) is not { } ev) continue;
            // Review (run 209): a jump that lands mid-tournament (a league that opens before Sep 15) still plays it, whole.
            if (ev.Start >= newYear || ev.End < summer.AddMonths(-6)) continue;
            if (World.International.WorldCups.Any(w => w.Year == y)) continue;
            if (WorldCup.Play(this, y, _sim) is not { } rec) continue;
            Settle(rec);
            World.International.WorldCups.Add(rec);
            RankWorldCup(rec);
            Post($"World Cup {y}: {rec.NameOf(rec.Gold)} win gold", WorldCupStory(rec), "World");
        }
        RefreshInternationalMoods();
        if (_settled.Count > 0) ReviewTournaments(_settled); // I6b (run 215): the federation's verdict on your nation's summer
        if (_settled.Count > 0) PostCallups(_settled);
        _settled.Clear();
    }

    /// <summary>The records played by the current call of <see cref="PlayInternationalOverSummer"/> (for the one call-ups message).</summary>
    private readonly List<WorldCupRecord> _settled = new();
    /// <summary>The day the summer jumps to (today, once the session has moved on): what an injury is carried against.</summary>
    private DateOnly _jumpDay;

    /// <summary>
    /// I5 (run 213): once, as a tournament is played: who was left out (from the nation's previous twelve) and the injuries carried
    /// to the clubs. An injury still running on the jump day replaces a shorter one; every one goes in the player's history.
    /// </summary>
    private void Settle(WorldCupRecord rec)
    {
        if (rec.Games.Count == 0) return;
        var first = DateOnly.ParseExact($"{rec.Games[0].Day} {rec.Year}", "MMM d yyyy", System.Globalization.CultureInfo.InvariantCulture);
        var pools = NationalTeams.For(this, first);
        rec.LeftOut = Callups.LeftOut(World.International, rec, pools, first);
        if (rec.Injuries.Count > 0)
        {
            var people = InternationalPeople();
            foreach (var inj in rec.Injuries)
            {
                // Review (run 213): a jump that lands mid-tournament plays it whole; an injury dated after today isn't carried.
                if (inj.Occurred > _jumpDay || !people.TryGetValue(inj.PlayerId, out var p)) continue;
                var injury = new Hardwood.Core.Model.Injury { Name = inj.Injury, Severity = inj.Severity, Occurred = inj.Occurred, ReturnDate = inj.Return };
                p.InjuryHistory.Add(injury);
                if (inj.Return > _jumpDay && (p.Injury == null || p.Injury.ReturnDate < inj.Return)) p.Injury = injury;
            }
        }
        _settled.Add(rec);
    }

    /// <summary>Every player a national team can pick (pros, clubs abroad, college, free agents), by id.</summary>
    private Dictionary<int, Hardwood.Core.Model.Player> InternationalPeople()
    {
        var d = new Dictionary<int, Hardwood.Core.Model.Player>();
        foreach (var t in League.Teams) foreach (var p in t.Roster) d.TryAdd(p.Id, p);
        foreach (var t in World.Clubs) foreach (var p in t.Roster) d.TryAdd(p.Id, p);
        if (World.College?.Programs is { } programs) foreach (var t in programs) foreach (var p in t.Roster) d.TryAdd(p.Id, p);
        foreach (var p in League.FreeAgents) d.TryAdd(p.Id, p);
        return d;
    }

    /// <summary>I5: every player's line from the latest summer's tournaments, re-derived from the saved records (at load and after a
    /// summer). Pure over the history: no draw.</summary>
    internal void RefreshInternationalMoods()
    {
        var h = World.International;
        bool any = h.WorldCups.Count + h.Olympics.Count + h.Continentals.Count + h.U23Cups.Count > 0;
        foreach (var p in InternationalPeople().Values) p.International = any ? Callups.MoodFor(h, p) : null;
    }

    /// <summary>I5: the U23 Nations Cup (2030, 2034, …), played once at the summer's jump on its own stream. No ranking move.</summary>
    private void PlayU23OverSummer(int y, DateOnly summer, DateOnly newYear)
    {
        if (U23Cup.EventIn(y) is not { } ev) return;
        if (ev.Start >= newYear || ev.End < summer.AddMonths(-6)) return;
        var h = World.International;
        if (h.U23Cups.Any(c => c.Year == y)) return;
        if (U23Cup.Play(this, y, _sim) is not { Final: not null } rec) return;
        Settle(rec);
        h.U23Cups.Add(rec);
        var f = rec.Final!;
        string story = $"{rec.NameOf(rec.Gold)} beat {rec.NameOf(rec.Silver)} {Math.Max(f.HomePts, f.AwayPts)}-{Math.Min(f.HomePts, f.AwayPts)} in the final ({rec.Dates}); "
                       + $"{rec.NameOf(rec.Bronze)} took bronze. Sixteen under-23 twelves played; it's a game event (FIBA has no men's U23 world championship) and doesn't move the ranking."
                       + (rec.Mvp is { } m ? $"\n\nMVP: {m.Name} ({m.Code}), {m.Ppg:0.0} points a game." : "")
                       + (Ours(rec) is { Length: > 0 } ours ? $"\n\nOurs at the U23 Nations Cup: {ours}." : "");
        Post($"U23 Nations Cup {y}: {rec.NameOf(rec.Gold)} win gold", story, "World");
    }

    /// <summary>I5: one message for your own players' summer: who played (and where they finished), who was left out, and who
    /// comes back hurt. Nothing when none of yours was involved.</summary>
    private void PostCallups(IReadOnlyList<WorldCupRecord> played)
    {
        if (!Employed) return;
        var mine = HumanTeam.Roster.ToDictionary(p => p.Id);
        var lines = new List<string>();
        int picked = 0;
        foreach (var rec in played)
        {
            var ours = rec.Teams.SelectMany(t => t.Squad.Where(mine.ContainsKey).Select(id => $"{mine[id].Name} ({t.Name}, {Placing(rec, t.Code)})")).ToList();
            picked += ours.Count;
            if (ours.Count > 0) lines.Add($"{rec.Short}: {string.Join(", ", ours)}.");
            var left = rec.LeftOut.Where(kv => mine.ContainsKey(kv.Key)).Select(kv => $"{mine[kv.Key].Name} ({rec.NameOf(kv.Value)})").ToList();
            if (left.Count > 0) lines.Add($"Left out for {rec.Short}: {string.Join(", ", left)}. He was in the twelve last time.");
            foreach (var inj in rec.Injuries.Where(i => mine.ContainsKey(i.PlayerId) && i.Occurred <= _jumpDay))
                lines.Add(inj.Return > _jumpDay
                    ? $"{inj.Name} was hurt at {rec.Short} ({inj.Injury}, {inj.Occurred:MMM d}) and is out until {inj.Return:MMM d}."
                    : $"{inj.Name} was hurt at {rec.Short} ({inj.Injury}) but is fit again.");
        }
        if (lines.Count == 0) return;
        lines.Add("A medal or a call-up lifts a player's mood for the first weeks of the season; being left out stings. Both show among his mood factors.");
        string title = picked switch
        {
            0 => "International duty: news about our players",
            1 => "International duty: one of ours played for his country",
            _ => $"International duty: {picked} of ours played for their countries",
        };
        Post(title, string.Join("\n\n", lines), "World");
    }

    /// <summary>I3 (run 210): every World Cup game moves the FIBA ranking.</summary>
    private void RankWorldCup(WorldCupRecord rec) => RankTournament(rec, FibaRanking.WorldCupK);

    /// <summary>I4 (run 211): a tournament's games move the FIBA ranking by <paramref name="k"/>.</summary>
    private void RankTournament(WorldCupRecord rec, double k, bool keepLast = false)
    {
        var h = World.International;
        var teams = NationalTeams.For(this, Today);
        var points = FibaRanking.Current(h, teams);
        var before = FibaRanking.Apply(teams, points).Where(t => t.Rank > 0).ToDictionary(t => t.Nation.Code, t => t.Rank);
        foreach (var g in rec.Games) FibaRanking.Update(points, g, k);
        h.Ranking = points;
        if (!keepLast) h.LastRanking = before;
    }

    /// <summary>I4 (run 211): the Olympic qualifying tournaments and the Olympics, played once at the summer's jump.</summary>
    private void PlayOlympicsOverSummer(int y, DateOnly summer, DateOnly newYear)
    {
        if (Olympics.EventIn(y) is not { } ev || Olympics.QualifyingIn(y) is not { } oqt) return;
        if (oqt.Start >= newYear || ev.End < summer.AddMonths(-6)) return;
        var h = World.International;
        if (h.Olympics.Any(o => o.Year == y)) return;
        if (Olympics.Play(this, y, _sim) is not { } result) return;
        Settle(result.Olympics);
        h.OlympicQualifiers.AddRange(result.Qualifiers);
        h.Olympics.Add(result.Olympics);
        // The movement arrows read against the ranking before the qualifiers; both events move the points.
        var before = CurrentRanks();
        foreach (var q in result.Qualifiers) RankTournament(q, Olympics.QualifierK, keepLast: true);
        RankTournament(result.Olympics, Olympics.OlympicK, keepLast: true);
        h.LastRanking = before;
        Post($"Olympics {y}: {result.Olympics.NameOf(result.Olympics.Gold)} win gold", OlympicStory(result), "World");
    }

    /// <summary>The world ranks as they stand (code to rank), for the movement arrows.</summary>
    private Dictionary<string, int> CurrentRanks()
    {
        var teams = NationalTeams.For(this, Today);
        return FibaRanking.Apply(teams, FibaRanking.Current(World.International, teams)).Where(t => t.Rank > 0).ToDictionary(t => t.Nation.Code, t => t.Rank);
    }

    /// <summary>I4 (run 211): the four continental championships, played once at the summer's jump, each moving the ranking.</summary>
    private void PlayContinentalsOverSummer(int y, DateOnly summer, DateOnly newYear)
    {
        if (!FibaCalendar.IsContinentalYear(y)) return;
        var events = FibaCalendar.InYear(y).Where(e => e.Kind == FibaEventKind.Continental).ToList();
        if (events.Min(e => e.Start) >= newYear || events.Max(e => e.End) < summer.AddMonths(-6)) return;
        var h = World.International;
        if (h.Continentals.Any(c => c.Year == y)) return;
        var before = CurrentRanks();
        var played = Continentals.Play(this, y, _sim, rec => RankTournament(rec, Continentals.ContinentalK, keepLast: true));
        if (played.Count == 0) return;
        foreach (var rec in played) Settle(rec);
        h.Continentals.AddRange(played);
        h.LastRanking = before;
        Post($"Continental championships {y}: " + string.Join(", ", played.Select(c => $"{c.NameOf(c.Gold)} ({c.Short.Replace($" {y}", "")})")),
            ContinentalStory(played), "World");
    }

    /// <summary>The Olympic story: medals, the final, the MVP, the four qualifiers and your own players who were there.</summary>
    internal string OlympicStory(Olympics.Result result)
    {
        var rec = result.Olympics;
        var f = rec.Final!;
        var lines = new List<string>
        {
            $"{rec.NameOf(rec.Gold)} beat {rec.NameOf(rec.Silver)} {Math.Max(f.HomePts, f.AwayPts)}-{Math.Min(f.HomePts, f.AwayPts)} in the Olympic final in {rec.Host} ({rec.Dates}). {rec.NameOf(rec.Bronze)} took bronze over {rec.NameOf(rec.Fourth)}.",
        };
        if (result.Qualifiers.Count > 0)
            lines.Add("Through the qualifying tournaments: " + string.Join(", ", result.Qualifiers.Select(q => $"{q.NameOf(q.Gold)} (in {q.Host})")) + ".");
        if (rec.Mvp is { } mvp) lines.Add($"MVP: {mvp.Name} ({mvp.Code}), {mvp.Ppg:0.0} points, {mvp.Rpg:0.0} rebounds and {mvp.Apg:0.0} assists a game.");
        if (Ours(rec) is { Length: > 0 } ours) lines.Add("Ours at the Olympics: " + ours + ".");
        lines.Add("The groups, the knockouts and the medal table are on the International page.");
        return string.Join("\n\n", lines);
    }

    /// <summary>The continental story: each champion and final, and your own players who were there.</summary>
    internal string ContinentalStory(IReadOnlyList<WorldCupRecord> played)
    {
        var lines = new List<string>();
        foreach (var c in played)
        {
            var f = c.Final!;
            string line = $"{c.Title} ({c.Dates}): {c.NameOf(c.Gold)} beat {c.NameOf(c.Silver)} {Math.Max(f.HomePts, f.AwayPts)}-{Math.Min(f.HomePts, f.AwayPts)} in the final"
                          + (c.Bronze != "" ? $"; {c.NameOf(c.Bronze)} took bronze" : "") + $" ({c.Teams.Count} teams)."
                          + (c.Mvp is { } m ? $" MVP: {m.Name} ({m.Code})." : "");
            lines.Add(line);
            if (Ours(c) is { Length: > 0 } ours) lines.Add($"Ours at {c.Short}: {ours}.");
        }
        lines.Add("The fields came from the continental qualifying tables. Every result is on the International page.");
        return string.Join("\n\n", lines);
    }

    /// <summary>"Name (Nation, gold), …" for your own players in a tournament's twelves, or "".</summary>
    private string Ours(WorldCupRecord rec)
    {
        if (!Employed) return "";
        var mine = HumanTeam.Roster.ToDictionary(p => p.Id);
        var ours = rec.Teams.SelectMany(t => t.Squad.Where(mine.ContainsKey).Select(id => $"{mine[id].Name} ({t.Name}, {Placing(rec, t.Code)})")).ToList();
        return string.Join(", ", ours);
    }

    /// <summary>
    /// I3 (run 210): a FIBA window that ended today (or within <see cref="Qualifying.CatchUpDays"/>, after a jump) is played
    /// once, by quick result on its own stream, from today's rosters (players fit on the window's first day). No player, club or
    /// shared stream is touched. The inbox gets one message.
    /// </summary>
    private void PlayFibaWindows()
    {
        foreach (var w in Qualifying.DueWindows(Today))
        {
            var h = World.International;
            string key = Qualifying.KeyOf(w);
            if (h.Windows.Any(x => x.Key == key)) continue;
            var teams = NationalTeams.For(this, w.Start);
            if (teams.Count(t => t.Rank > 0) < 2) continue;
            var before = new Dictionary<string, double>(FibaRanking.Current(h, teams));
            var rec = Qualifying.Play(h, w, teams, Seed, NationalJobState.Games, NationalTeams.WindowStrengths(this, w.Start));
            ReviewWindow(rec, before);
            Post($"FIBA window: qualifiers for the {rec.Target}", WindowStory(rec), "World");
        }
    }

    /// <summary>The window's inbox story: the top of the ranking, the biggest results, and your own players' nations.</summary>
    internal string WindowStory(WindowRecord rec)
    {
        var h = World.International;
        var teams = NationalTeams.For(this, Today);
        var ranked = FibaRanking.Apply(teams, FibaRanking.Current(h, teams)).Where(t => t.Rank > 0).ToList();
        string Name(string code) => ranked.FirstOrDefault(t => t.Nation.Code == code)?.Name ?? code;
        var lines = new List<string>
        {
            $"{rec.Games.Count} qualifiers were played ({rec.Dates}), window {rec.Index + 1} of the {rec.Target} cycle. Club seasons carried on.",
            "FIBA world ranking: " + string.Join(", ", ranked.Take(5).Select(t => $"{t.Rank}. {t.Name}" + Move(h, t))) + ".",
        };
        var upsets = rec.Games.Select(g => (g, gap: 0))
            .Where(x => (h.LastRanking.GetValueOrDefault(x.g.Winner, 999)) > (h.LastRanking.GetValueOrDefault(x.g.Loser, 999)) + 8)
            .OrderByDescending(x => h.LastRanking.GetValueOrDefault(x.g.Winner, 999) - h.LastRanking.GetValueOrDefault(x.g.Loser, 999)).Take(2).ToList();
        if (upsets.Count > 0)
            lines.Add("Upsets: " + string.Join("; ", upsets.Select(x => $"{Name(x.g.Winner)} beat {Name(x.g.Loser)} {Math.Max(x.g.HomePts, x.g.AwayPts)}-{Math.Min(x.g.HomePts, x.g.AwayPts)}")) + ".");
        if (Employed)
        {
            var mine = HumanTeam.Roster.Select(p => p.Id).ToHashSet();
            var nations = ranked.Where(t => t.Squad.Any(x => mine.Contains(x.Player.Id))).ToList();
            foreach (var t in nations)
            {
                var gs = rec.Games.Where(g => g.Home == t.Nation.Code || g.Away == t.Nation.Code).ToList();
                if (gs.Count == 0) continue;
                lines.Add($"{t.Name} (with " + string.Join(", ", t.Squad.Where(x => mine.Contains(x.Player.Id)).Select(x => x.Player.Name)) + "): "
                    + string.Join(", ", gs.Select(g => (g.Winner == t.Nation.Code ? "beat " : "lost to ") + Name(g.Home == t.Nation.Code ? g.Away : g.Home)
                        + $" {Math.Max(g.HomePts, g.AwayPts)}-{Math.Min(g.HomePts, g.AwayPts)}")) + $"; now No. {t.Rank}.");
            }
        }
        lines.Add("The groups and the ranking are on the International page. Players aren't called up from clubs yet (a later build).");
        return string.Join("\n\n", lines);
    }

    /// <summary>" (up 2)", " (down 1)" or "" against the rank before the latest window or tournament.</summary>
    public static string Move(InternationalHistory h, NationalTeam t)
    {
        if (!h.LastRanking.TryGetValue(t.Nation.Code, out var was) || was == t.Rank) return "";
        return was > t.Rank ? $" (up {was - t.Rank})" : $" (down {t.Rank - was})";
    }

    /// <summary>The inbox story: medals, the final, the MVP, the top scorer and your own players who were there.</summary>
    internal string WorldCupStory(WorldCupRecord rec)
    {
        var f = rec.Final!;
        var lines = new List<string>
        {
            $"{rec.NameOf(rec.Gold)} beat {rec.NameOf(rec.Silver)} {Math.Max(f.HomePts, f.AwayPts)}-{Math.Min(f.HomePts, f.AwayPts)} in the final in {rec.Host} ({rec.Dates}). {rec.NameOf(rec.Bronze)} took bronze over {rec.NameOf(rec.Fourth)}.",
        };
        if (rec.Mvp is { } mvp) lines.Add($"MVP: {mvp.Name} ({mvp.Code}), {mvp.Ppg:0.0} points, {mvp.Rpg:0.0} rebounds and {mvp.Apg:0.0} assists a game.");
        if (rec.TopScorers.FirstOrDefault() is { } top) lines.Add($"Top scorer: {top.Name} ({top.Code}), {top.Ppg:0.0} a game.");
        if (Employed)
        {
            var mine = HumanTeam.Roster.Select(p => p.Id).ToHashSet();
            var ours = rec.Teams.SelectMany(t => t.Squad.Where(mine.Contains).Select(id => (t, p: HumanTeam.Roster.First(p => p.Id == id)))).ToList();
            if (ours.Count > 0) lines.Add("Ours at the World Cup: " + string.Join(", ", ours.Select(x => $"{x.p.Name} ({x.t.Name}, {Placing(rec, x.t.Code)})")) + ".");
        }
        lines.Add("The groups, the knockouts and the all-star five are on the International page.");
        return string.Join("\n\n", lines);
    }

    /// <summary>"gold", "silver", "bronze", "fourth", "out in the quarter-finals", …</summary>
    public static string Placing(WorldCupRecord rec, string code)
    {
        if (code == rec.Gold) return "gold";
        if (code == rec.Silver) return "silver";
        if (code == rec.Bronze) return "bronze";
        if (code == rec.Fourth) return "fourth";
        if (rec.Games.Any(g => g.Round == WorldCup.SemiRound && (g.Home == code || g.Away == code))) return "out in the semi-finals"; // I4: no bronze game
        if (rec.Games.Any(g => g.Round == WorldCup.QuarterRound && (g.Home == code || g.Away == code))) return "out in the quarter-finals";
        if (rec.Games.Any(g => g.Round == Tournament.RoundOf16 && (g.Home == code || g.Away == code))) return "out in the round of 16";
        if (rec.Games.Any(g => g.Round == WorldCup.SecondRound && (g.Home == code || g.Away == code))) return "out in the second round";
        return "out in the group";
    }
}
