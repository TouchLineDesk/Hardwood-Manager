using Hardwood.Core.Engine;
using Hardwood.Core.People;
using Hardwood.Core.Model;
using Hardwood.Core.World;

namespace Hardwood.Core.Season;

/// <summary>Which studio the pre-game show comes from (Danny, #446): the set's look and title change with the night.</summary>
public enum StudioSet { Regular, OpeningNight, Thanksgiving, Christmas, NewYear, MlkDay, Playoffs, Finals, ThirdPlace, International, College }

/// <summary>A voice on the panel. Invented people on our own ESPN-style network, never real broadcasters (#446).</summary>
public sealed record StudioAnalyst(string Key, string Name, string Role, string Bio, string Colour)
{
    public string Initials
    {
        get
        {
            var words = Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => !w.StartsWith('\'')).ToList();
            return words.Count == 0 ? "" : words.Count == 1 ? words[0][..1] : $"{words[0][0]}{words[^1][0]}";
        }
    }
}

public sealed record StudioLine(StudioAnalyst Speaker, string Text);

/// <summary>One segment of the show: a kicker (the lower-third label), a title and the panel's lines.</summary>
public sealed record StudioSegment(string Kicker, string Title, List<StudioLine> Lines);

/// <summary>A tale-of-the-tape row: the away value, the home value, and which side has the edge (-1 away, 1 home, 0 neither).</summary>
public sealed record TapeRow(string Label, string Away, string Home, int Edge);

/// <summary>A row of a division table as it stands before tip-off.</summary>
public sealed record ImpactRow(int Place, Team Team, string Record, string Back, string Zone, bool Tonight);

public sealed record ImpactTable(string Title, List<ImpactRow> Rows);

/// <summary>A line of an award race (the MVP or rookie ladder) as the panel sees it tonight.</summary>
public sealed record RaceRow(int Rank, string Name, string Team, string Line, bool Tonight);

/// <summary>An analyst's pick for tonight and his record on your games this season.</summary>
public sealed record StudioPickView(StudioAnalyst Analyst, Team Pick, int Margin, string Why, int Wins, int Losses)
{
    public string Record => $"{Wins}-{Losses}";
}

/// <summary>
/// The pre-game studio show (Danny's design, #446, D53 B; part 1, run 45): a host and three analysts on HSN, our
/// ESPN-style network, before your games. The open, the tale of the tape, what to watch, the off-court stories, what the
/// night means in the standings, the award races and the panel's tracked picks. Everything is read from the save: records,
/// form, injuries, stats, the board, the digest. The set changes for opening night, the holidays the NBA plays on, the
/// playoffs, the Finals, the third-place tournament, games abroad and college nights. The NBA Cup's set waits for the
/// in-season tournament itself (roadmap 9).
/// </summary>
public sealed class StudioShow
{
    public const string Network = "HSN", NetworkName = "Hardwood Sports Network";

    public required StudioSet Set { get; init; }
    public required string ShowName { get; init; }
    public required string SetLine { get; init; }
    public required string Accent { get; init; }
    public required string Second { get; init; }
    public required Team Home { get; init; }
    public required Team Away { get; init; }
    public required DateOnly Date { get; init; }
    public string Venue { get; init; } = "";
    public string TipTime { get; init; } = "7:30 PM ET";
    public string AwayRecord { get; init; } = "";
    public string HomeRecord { get; init; } = "";
    public string Stakes { get; init; } = "";
    public required StudioAnalyst Host { get; init; }
    public required List<StudioAnalyst> Panel { get; init; }
    public List<StudioSegment> Segments { get; } = new();
    public List<TapeRow> Tape { get; } = new();
    public List<ImpactTable> Impact { get; } = new();
    public List<StudioLine> ImpactLines { get; } = new();
    public string RaceTitle { get; set; } = "";
    public List<RaceRow> Race { get; } = new();
    public List<StudioLine> RaceLines { get; } = new();
    public List<StudioPickView> Picks { get; } = new();
    /// <summary>The home side's chance of winning before tip-off (the panel's numbers desk).</summary>
    public double HomeChance { get; init; }
    public string Key => GameKey(Date, Home, Away);

    public static string GameKey(DateOnly date, Team home, Team away) => $"{date:yyyyMMdd}-{home.Id}-{away.Id}";

    // The cast (#446). Invented names; the tests check none is a real player's.
    public static readonly StudioAnalyst HostDesk = new("host", "Dale Whitmore", "Host", "Twenty years in the chair. Keeps the panel honest and the show on time.", "#E6EDF3");
    public static readonly StudioAnalyst BigMan = new("big", "Reggie Banfield", "Analyst · former center", "Fourteen seasons in the paint. Trusts size, defense and the home floor.", "#F0A33A");
    public static readonly StudioAnalyst Numbers = new("numbers", "Tasha Kincaid", "Analyst · numbers desk", "Ran a front office's analytics group. Picks what the numbers say, every time.", "#58A6FF");
    public static readonly StudioAnalyst Guard = new("guard", "Marcus 'Showtime' Lyle", "Analyst · former guard", "Two-time All-Star. Loves a hot hand, a star and an upset.", "#F85149");
    public static readonly StudioAnalyst Coach = new("coach", "Vern Haskell", "Guest · former head coach", "Thirty years on the sideline. Joins the desk for the big nights.", "#3FB950");
    public static readonly StudioAnalyst[] Cast = { HostDesk, BigMan, Numbers, Guard, Coach };
    public static StudioAnalyst? ByKey(string key) => Cast.FirstOrDefault(a => a.Key == key);

    public static (StudioSet set, string name, string line, string accent, string second) SetFor(DateOnly date, bool college, bool abroad, Series? series, bool firstGame)
    {
        string night = $"{date.DayOfWeek} night in {date:MMMM}";
        if (college) return (StudioSet.College, "HSN College Tip-Off", $"College basketball: {night}", "#7C5CFF", "#F0A33A");
        if (series != null)
        {
            string game = $"Game {series.GameNumber}";
            if (Postseason.IsThirdPlace(series.Round))
                return (StudioSet.ThirdPlace, "HSN Third-Place Tip-Off", $"{series.Round}, {game}: prize money and a trophy on the line", "#C0C7D0", "#8B96A5");
            if (series.Round == "Finals")
                return (StudioSet.Finals, "HSN Finals Tip-Off", $"The Finals, {game}", "#E3B341", "#101318");
            return (StudioSet.Playoffs, "HSN Playoff Tip-Off", $"{series.Round}, {game}", "#F85149", "#E6EDF3");
        }
        if (abroad) return (StudioSet.International, "HSN World Feed", $"International basketball: {night}", "#2EA8A0", "#E6EDF3");
        if (date.Month == 12 && date.Day == 25) return (StudioSet.Christmas, "HSN Christmas Tip-Off", "Christmas Day basketball", "#D73A49", "#3FB950");
        if (date.Month == 11 && date.DayOfWeek == DayOfWeek.Thursday && date.Day >= 22 && date.Day <= 28)
            return (StudioSet.Thanksgiving, "HSN Thanksgiving Tip-Off", "Thanksgiving night basketball", "#D9822B", "#8B5A2B");
        if ((date.Month == 12 && date.Day == 31) || (date.Month == 1 && date.Day == 1))
            return (StudioSet.NewYear, "HSN New Year's Tip-Off", date.Month == 12 ? "New Year's Eve basketball" : "New Year's Day basketball", "#E3B341", "#7C5CFF");
        if (date.Month == 1 && date.DayOfWeek == DayOfWeek.Monday && date.Day >= 15 && date.Day <= 21)
            return (StudioSet.MlkDay, "HSN MLK Day Tip-Off", "Martin Luther King Jr. Day basketball", "#58A6FF", "#E3B341");
        if (firstGame) return (StudioSet.OpeningNight, "HSN Opening Night", "Opening night: a new season tips off", "#E3B341", "#F85149");
        return (StudioSet.Regular, "HSN Tip-Off", night, "#58A6FF", "#F0A33A");
    }
}

public sealed partial class GameSession
{
    /// <summary>The pre-game show for your next game against <paramref name="opponent"/> (or tonight's live game).</summary>
    public StudioShow BuildStudioShow(Team home, Team away, DateOnly? on = null)
    {
        var date = on ?? Today;
        var us = HumanTeam;
        var them = home == us ? away : home;
        var series = Phase == SessionPhase.Postseason && InNabl ? HumanSeries is { Complete: false } cur && (cur.Higher == them || cur.Lower == them) ? cur : null : null;
        var mine = MyGames();
        bool firstGame = !mine.Any(g => g.Played && g.Stage == "Regular season");
        var (set, name, line, accent, second) = StudioShow.SetFor(date, InCollege, Abroad && MyLevel == 0 && !InCollege, series, firstGame && series == null);
        var rng = new Rng((ulong)(date.DayNumber * 7919L + home.Id * 131L + away.Id * 17L + 1));
        var table = MyStandings;
        string Rec(Team t) { var r = RecordOf(t); return r.Games > 0 || table.Has(t) ? $"{r.Wins}-{r.Losses}" : ""; }

        double hs = QuickSim.Strength(home, date), as_ = QuickSim.Strength(away, date);
        bool college = InCollege;
        double perPoint = college ? 0.52 : 0.55 * 1.2625, sd = college ? 9.0 : 11 * 1.2625;
        double margin = (hs - as_) * perPoint + 2.5;
        double homeChance = Phi(margin / sd);

        var panel = new List<StudioAnalyst> { StudioShow.BigMan, StudioShow.Numbers, StudioShow.Guard };
        if (set is StudioSet.Playoffs or StudioSet.Finals or StudioSet.ThirdPlace) panel.Add(StudioShow.Coach);
        string venue = RealWorld.RealWorldData.IsReal(League) ? RealWorld.RealWorldData.Teams.FirstOrDefault(r => r.Abbr == home.Abbreviation)?.Arena ?? "" : "";

        var show = new StudioShow
        {
            Set = set, ShowName = name, SetLine = line, Accent = accent, Second = second, Home = home, Away = away, Date = date,
            Venue = venue, AwayRecord = Rec(away), HomeRecord = Rec(home), Host = StudioShow.HostDesk, Panel = panel, HomeChance = homeChance,
            TipTime = set == StudioSet.Christmas ? "12:00 PM ET" : date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? "6:00 PM ET" : "7:30 PM ET",
            Stakes = series != null ? SeriesLine(series, us) : "",
        };
        var host = show.Host;
        var big = StudioShow.BigMan; var num = StudioShow.Numbers; var guard = StudioShow.Guard; var coach = StudioShow.Coach;
        string Pick(params string[] options) => options[rng.Next(options.Length)];

        // --- Form, stars and injuries for both sides ---
        var form = new Dictionary<Team, (int w, int l, int streak)> { [home] = Form(home), [away] = Form(away) };
        var stats = InNabl ? Stats : MyStats;
        (Player p, PlayerSeasonStats? s)? Star(Team t)
        {
            var avail = t.Roster.Where(p => p.IsAvailable(date)).ToList();
            var withStats = avail.Select(p => (p, s: stats.GetValueOrDefault(p.Id))).Where(x => x.s is { Games: >= 3 } && x.s.TeamId == t.Id).OrderByDescending(x => x.s!.Ppg).FirstOrDefault();
            if (withStats.p != null) return withStats;
            var best = avail.OrderByDescending(p => p.CurrentAbility).FirstOrDefault();
            return best == null ? null : (best, null);
        }
        string StatLine(PlayerSeasonStats? s) => s == null ? "" : $"{s.Ppg:F1} points, {s.Rpg:F1} rebounds and {s.Apg:F1} assists a night";
        var homeStar = Star(home); var awayStar = Star(away);
        var hurt = new Dictionary<Team, List<Player>>
        {
            [home] = home.Roster.Where(p => !p.IsAvailable(date)).OrderByDescending(p => p.CurrentAbility).ToList(),
            [away] = away.Roster.Where(p => !p.IsAvailable(date)).OrderByDescending(p => p.CurrentAbility).ToList(),
        };

        // --- The open ---
        var open = new List<StudioLine>();
        string greeting = set switch
        {
            StudioSet.Christmas => "Merry Christmas, everybody, and welcome to the HSN studio on the biggest regular-season day of the year.",
            StudioSet.Thanksgiving => "Happy Thanksgiving. Plates are full, the panel's full, and we've got basketball.",
            StudioSet.NewYear => date.Month == 12 ? "Last game of the year for these two, and the panel's dressed for midnight." : "Happy New Year, and welcome to the first HSN Tip-Off of the year.",
            StudioSet.MlkDay => "On Martin Luther King Jr. Day, welcome to a full holiday slate on HSN.",
            StudioSet.OpeningNight => "It's opening night. Every record is 0-0, every fan base believes, and the panel has had all summer to get its takes ready.",
            StudioSet.Playoffs => "Welcome to the playoffs. Every possession matters now.",
            StudioSet.Finals => "Two teams left. Welcome to the Finals.",
            StudioSet.ThirdPlace => "Not the bracket anybody wanted, but there's a trophy and real money on the table tonight.",
            StudioSet.International => "Good evening from the HSN World Feed.",
            StudioSet.College => "Welcome to college basketball night on HSN.",
            _ => Pick("Good evening and welcome to HSN Tip-Off.", "Welcome in. Another night, another big one.", "Evening, everybody. We've got a good one for you."),
        };
        open.Add(new(host, $"{greeting} {away.FullName}{(show.AwayRecord != "" ? $" ({show.AwayRecord})" : "")} visit {home.FullName}{(show.HomeRecord != "" ? $" ({show.HomeRecord})" : "")}" +
                           (venue != "" ? $" at {venue}." : ".") + (show.Stakes != "" ? $" {show.Stakes}." : "")));
        var fav = margin >= 0 ? home : away;
        double favChance = Math.Max(homeChance, 1 - homeChance);
        open.Add(new(num, favChance >= 0.75
            ? $"Our numbers make {fav.Name} a clear favourite, {favChance * 100:0}% to win. Anything else is an upset."
            : favChance >= 0.6 ? $"{fav.Name} are favoured, {favChance * 100:0}%, but that's a game, not a lock."
            : $"Close to a coin flip on paper: {fav.Name} by a hair, {favChance * 100:0}%."));
        var hotTeam = form[home].streak >= form[away].streak ? home : away;
        if (Math.Abs(form[hotTeam].streak) >= 3 && form[hotTeam].streak > 0)
            open.Add(new(guard, $"{hotTeam.Name} have won {form[hotTeam].streak} straight. You can feel that in the building. {Pick("Momentum is real, I don't care what the spreadsheet says.", "Confidence is a skill in this league.")}"));
        var coldTeam = form[home].streak <= form[away].streak ? home : away;
        if (form[coldTeam].streak <= -3)
            open.Add(new(big, $"{coldTeam.Name} have dropped {-form[coldTeam].streak} in a row. {Pick("Somebody in that locker room has to stand up tonight.", "That's when you find out who your leaders are.", "Losing becomes a habit if you let it.")}"));
        show.Segments.Add(new("THE OPEN", set == StudioSet.Regular ? "Tonight on HSN" : show.ShowName, open));

        // --- Tale of the tape ---
        static string Per(int total, int games) => games == 0 ? "–" : $"{(double)total / games:F1}";
        var oh = RecordOf(home); var oa = RecordOf(away);
        if (show.AwayRecord != "" && show.HomeRecord != "")
            show.Tape.Add(new("Record", show.AwayRecord, show.HomeRecord, Math.Sign(oh.WinPct.CompareTo(oa.WinPct))));
        if (table.Has(home) && table.Has(away))
        {
            var rh = table[home]; var ra = table[away];
            if (rh.Games > 0 && ra.Games > 0)
            {
                show.Tape.Add(new("Points scored", Per(ra.PointsFor, ra.Games), Per(rh.PointsFor, rh.Games), Math.Sign(((double)rh.PointsFor / rh.Games).CompareTo((double)ra.PointsFor / ra.Games))));
                show.Tape.Add(new("Points allowed", Per(ra.PointsAgainst, ra.Games), Per(rh.PointsAgainst, rh.Games), -Math.Sign(((double)rh.PointsAgainst / rh.Games).CompareTo((double)ra.PointsAgainst / ra.Games))));
                show.Tape.Add(new("Point differential", $"{ra.PointDiff:+0.0;-0.0;0.0}", $"{rh.PointDiff:+0.0;-0.0;0.0}", Math.Sign(rh.PointDiff.CompareTo(ra.PointDiff))));
            }
        }
        if (form[home].w + form[home].l > 0 && form[away].w + form[away].l > 0)
        {
            show.Tape.Add(new("Last 10", $"{form[away].w}-{form[away].l}", $"{form[home].w}-{form[home].l}", Math.Sign(form[home].w.CompareTo(form[away].w))));
            show.Tape.Add(new("Streak", Streak(form[away].streak), Streak(form[home].streak), Math.Sign(form[home].streak.CompareTo(form[away].streak))));
        }
        show.Tape.Add(new("Top-8 strength", $"{as_:F0}", $"{hs:F0}", Math.Sign(hs.CompareTo(as_))));
        show.Tape.Add(new("Out injured", hurt[away].Count.ToString(), hurt[home].Count.ToString(), -Math.Sign(hurt[home].Count.CompareTo(hurt[away].Count))));
        var h2h = mine.Where(g => g.Played && g.Opponent == them && g.Date.Year >= date.Year - 1 && g.Date < date && g.Date > date.AddDays(-250)).ToList();
        if (h2h.Count > 0)
        {
            int w = h2h.Count(g => g.Won);
            bool homeIsUs = home == us;
            show.Tape.Add(new("Season series", homeIsUs ? $"{h2h.Count - w}-{w}" : $"{w}-{h2h.Count - w}", homeIsUs ? $"{w}-{h2h.Count - w}" : $"{h2h.Count - w}-{w}", 0));
        }

        // --- What to watch ---
        var watch = new List<StudioLine>();
        if (homeStar is { } hsr && awayStar is { } asr)
        {
            watch.Add(new(host, $"The marquee matchup: {asr.p.Name} against {hsr.p.Name}."));
            var bigger = (hsr.s?.Ppg ?? 0) >= (asr.s?.Ppg ?? 0) ? hsr : asr;
            var other = bigger.p == hsr.p ? asr : hsr;
            watch.Add(new(guard, bigger.s != null
                ? $"{bigger.p.Name} is putting up {StatLine(bigger.s)}. {Pick("Give him the ball and get out of the way.", "That's a problem nobody has solved yet.", "He's the best player on the floor tonight and he knows it.")}"
                : $"{bigger.p.Name} is the best player on the floor tonight. {Pick("Everything runs through him.", "Stop him and you've got a chance.")}"));
            if (other.s != null)
                watch.Add(new(big, $"And don't sleep on {other.p.Name}: {StatLine(other.s)}. {Pick("He'll want this one.", "Head-to-head, that's personal.", "Watch who guards who early.")}"));
        }
        int depthEdge = (int)Math.Round(hs - as_);
        if (Math.Abs(depthEdge) >= 3)
        {
            var deeper = depthEdge > 0 ? home : away;
            watch.Add(new(num, $"Top eight against top eight, {deeper.Name} are {Math.Abs(depthEdge)} points better on our scale. The longer this stays a full-rotation game, the more that tells."));
        }
        else watch.Add(new(num, "These two rotations grade out almost identically. It comes down to the stars and the last five minutes."));
        string style = StyleClash(home, away);
        if (style != "") watch.Add(new(big, style));
        foreach (var t in new[] { away, home })
            if (hurt[t].FirstOrDefault() is { } out1)
                watch.Add(new(host, $"{t.Name} are without {out1.Name}{(out1.Injury != null ? $" ({out1.Injury.Name.ToLower()}, {Days(out1.Injury.DaysOut(date))})" : "")}" +
                                    (hurt[t].Count > 1 ? $" and {hurt[t].Count - 1} more." : ".")));
        show.Segments.Add(new("KEYS TO THE GAME", "What to watch", watch));

        // --- Off the court ---
        var off = OffCourt(show, us, them, date, rng);
        if (off.Count > 0) show.Segments.Add(new("OFF THE COURT", "Around the teams", off));

        // --- Standings impact ---
        StandingsImpact(show, table, us, them, series, rng);

        // --- Award races (the top league only: the panel covers the NBA's MVP and rookie ladders) ---
        if (InNabl && Stats.Count > 0) AwardRace(show, rng);

        // --- Predictions, tracked ---
        var desk = Media.Studio;
        foreach (var a in panel)
        {
            var (pick, by, why) = PanelPick(a, home, away, margin, sd, form, hurt, rng);
            var locked = desk.Picks.FirstOrDefault(x => x.Key == show.Key && x.Analyst == a.Key);
            if (locked != null && locked.PickId == (pick == home ? home.Id : away.Id)) by = locked.Margin;
            else if (locked != null) { pick = locked.PickId == home.Id ? home : away; by = locked.Margin; }
            var (w, l) = desk.Record(a.Key, League.Season);
            show.Picks.Add(new(a, pick, by, why, w, l));
        }
        return show;
    }

    /// <summary>Lock the panel's picks for a game once the show airs (once per game), so they can be graded at the final.</summary>
    public void LockStudioPicks(StudioShow show)
    {
        var desk = Media.Studio;
        foreach (var p in show.Picks)
        {
            if (desk.Picks.Any(x => x.Key == show.Key && x.Analyst == p.Analyst.Key)) continue;
            desk.Picks.Add(new StudioPickRecord
            {
                Key = show.Key, Date = show.Date, Season = League.Season, Analyst = p.Analyst.Key, HomeId = show.Home.Id, AwayId = show.Away.Id,
                PickId = p.Pick.Id, Margin = p.Margin,
            });
        }
        // Two seasons of picks is plenty for the records.
        desk.Picks.RemoveAll(x => x.Season < League.Season - 1);
    }

    /// <summary>Grade any locked picks for games just played.</summary>
    internal void GradeStudioPicks(List<GameResult> played)
    {
        var open = Media.Studio.Picks.Where(p => p.Correct == null).ToList();
        if (open.Count == 0) return;
        foreach (var r in played)
        {
            if (r.Date is not { } d) continue;
            string key = StudioShow.GameKey(d, r.Home.Team, r.Away.Team);
            int winner = r.Home.Points > r.Away.Points ? r.Home.Team.Id : r.Away.Team.Id;
            foreach (var p in open.Where(p => p.Key == key))
            {
                p.Correct = p.PickId == winner;
                p.Final = $"{r.Away.Team.Abbreviation} {r.Away.Points}, {r.Home.Team.Abbreviation} {r.Home.Points}";
            }
        }
        // A pick for a game that was never played that day (a postponed or replayed day) is dropped after a week.
        Media.Studio.Picks.RemoveAll(p => p.Correct == null && p.Date < Today.AddDays(-7));
    }

    /// <summary>The panel's last graded picks on your games, newest first, for the show's "how we did" line.</summary>
    public List<StudioPickRecord> StudioLastNight() =>
        Media.Studio.Picks.Where(p => p.Correct != null).GroupBy(p => p.Key).OrderByDescending(g => g.First().Date).FirstOrDefault()?.ToList() ?? new();

    // ------------------------------------------------------------------------------------------

    private static string SeriesLine(Series s, Team us)
    {
        var them = s.Higher == us ? s.Lower : s.Higher;
        int ours = s.Higher == us ? s.HigherWins : s.LowerWins, theirs = s.Higher == us ? s.LowerWins : s.HigherWins;
        string best = $"best of {s.WinsNeeded * 2 - 1}";
        if (ours == 0 && theirs == 0) return $"Game 1 of a {best}";
        if (ours == theirs) return $"The series is tied {ours}-{theirs}";
        if (theirs == s.WinsNeeded - 1 && ours == s.WinsNeeded - 1) return $"Tied {ours}-{theirs}: winner takes the series";
        if (theirs == s.WinsNeeded - 1) return $"{them.Name} lead {theirs}-{ours}: {us.Name} face elimination";
        string lead = ours > theirs ? $"{us.Name} lead {ours}-{theirs}" : $"{them.Name} lead {theirs}-{ours}";
        return ours == s.WinsNeeded - 1 ? $"{lead}: one win from moving on" : lead;
    }

    private static string SeriesStakes(Series s, Team us)
    {
        int ours = s.Higher == us ? s.HigherWins : s.LowerWins, theirs = s.Higher == us ? s.LowerWins : s.HigherWins;
        string win = ours + 1 >= s.WinsNeeded ? $"Win tonight and {us.Name} are through" : $"Win tonight and it's {ours + 1}-{theirs}";
        string lose = theirs + 1 >= s.WinsNeeded ? "lose and the season's over" : $"lose and it's {ours}-{theirs + 1}";
        return $"{win}; {lose}.";
    }

    private static string Streak(int s) => s == 0 ? "–" : s > 0 ? $"W{s}" : $"L{-s}";
    private static string Days(int d) => d <= 1 ? "day to day" : d < 14 ? $"{d} days" : $"about {Math.Round(d / 7.0)} weeks";

    /// <summary>Last ten games (wins, losses) and the current streak for any club, from this season's results.</summary>
    private (int w, int l, int streak) Form(Team t)
    {
        var games = new List<(DateOnly date, bool won)>();
        if (t == HumanTeam) games.AddRange(MyGames().Where(g => g.Played).Select(g => (g.Date, g.Won)));
        else if (InNabl && League.Teams.Contains(t))
        {
            foreach (var (idx, r) in Results)
                if (r.Home.Team == t || r.Away.Team == t)
                    games.Add((Schedule[idx].Date, (r.Home.Team == t ? r.Home.Points - r.Away.Points : r.Away.Points - r.Home.Points) > 0));
            if (Postseason != null)
                foreach (var se in Postseason.Series.Where(x => x.Higher == t || x.Lower == t))
                foreach (var r in se.Games)
                    games.Add((r.Date ?? Today, (r.Home.Team == t ? r.Home.Points - r.Away.Points : r.Away.Points - r.Home.Points) > 0));
        }
        var recent = games.OrderByDescending(g => g.date).ToList();
        int streak = 0;
        foreach (var g in recent)
        {
            int sign = g.won ? 1 : -1;
            if (streak != 0 && Math.Sign(streak) != sign) break;
            streak += sign;
        }
        var ten = recent.Take(10).ToList();
        return (ten.Count(g => g.won), ten.Count(g => !g.won), streak);
    }

    private static string StyleClash(Team home, Team away)
    {
        var th = home.Tactic; var ta = away.Tactic;
        if (Math.Abs(th.Pace - ta.Pace) >= 1.5)
        {
            var fast = th.Pace > ta.Pace ? home : away; var slow = fast == home ? away : home;
            return $"Tempo is the fight. {fast.Name} want to run; {slow.Name} want to grind it out in the half court. Whoever sets the pace early wins this.";
        }
        if (Math.Abs(th.ThreePointEmphasis - ta.ThreePointEmphasis) >= 2)
        {
            var shoot = th.ThreePointEmphasis > ta.ThreePointEmphasis ? home : away;
            return $"{shoot.Name} will let it fly from three. Make them put it on the floor and they look ordinary. Leave them open and it's a long night.";
        }
        if (th.System != ta.System)
            return $"{home.Name} run {Systems.Name(th.System).ToLower()}, {away.Name} run {Systems.Name(ta.System).ToLower()}. The coaching chess match is worth watching.";
        return "";
    }

    private List<StudioLine> OffCourt(StudioShow show, Team us, Team them, DateOnly date, Rng rng)
    {
        var host = show.Host; var big = StudioShow.BigMan; var num = StudioShow.Numbers; var guard = StudioShow.Guard;
        var lines = new List<StudioLine>();
        // Your seat: how the board sees you.
        if (Employed)
        {
            string label = Job.Confidence switch { >= 70 => "very happy", >= 55 => "happy", >= 40 => "patient", >= 25 => "restless", _ => "out of patience" };
            if (Job.Confidence < 40)
                lines.Add(new(big, $"Let's talk about {Coach.Name}. The word is the {(InCollege ? "AD" : "front office")} is {label}. {(Job.Confidence < 25 ? "Lose a couple more and this could be his last week." : "He needs a run, and he needs it soon.")}"));
            else if (Job.Confidence < 70 || MyRecord.Games < 10)
            {
                if (rng.Chance(0.3))
                    lines.Add(new(host, $"{Coach.Name}'s {(InCollege ? "AD" : "front office")} is {label} for now. {(MyRecord.Games < 10 ? "Early days: they want to see a direction." : "Nothing's on fire, but nobody's handing out extensions either.")}"));
            }
            else if (rng.Chance(0.3))
                lines.Add(new(num, $"{Coach.Name} has the {(InCollege ? "AD" : "owner")} {label} right now. That buys a coach the freedom to experiment."));
        }
        // Unhappy players on your side.
        var unhappy = us.Roster.Where(p => p.Morale < 35).OrderBy(p => p.Morale).FirstOrDefault();
        if (unhappy != null)
            lines.Add(new(guard, $"I'm hearing {unhappy.Name} isn't happy in {us.City}. {People.PeopleEngine.Mood(unhappy.Morale)}, and it's starting to show. Watch his body language tonight."));
        // A player facing his old club.
        foreach (var (side, other) in new[] { (us, them), (them, us) })
            foreach (var p in side.Roster.Where(p => p.IsAvailable(date)).OrderByDescending(p => p.CurrentAbility).Take(8))
                if (League.Careers.GetValueOrDefault(p.Id) is { } c && c.GamesByTeam.TryGetValue(other.Id, out int gp) && gp >= 82)
                {
                    lines.Add(new(host, $"{p.Name} faces {other.Name}, his old club: {gp} games in their colours. {(side == us ? "He'll get a reception." : "Expect a tribute video and a few boos.")}"));
                    goto doneReturn;
                }
        doneReturn:
        // Contract years, comebacks and young risers on either side.
        var stats = InNabl ? Stats : MyStats;
        foreach (var t in new[] { them, us })
        {
            var core = t.Roster.Where(p => p.IsAvailable(date)).OrderByDescending(p => p.CurrentAbility).Take(6).ToList();
            if (core.FirstOrDefault(p => p.Contract is { } c && c.StartSeason + c.Years - 1 == League.Season && p.Age(date) <= 34) is { } walk && rng.Chance(0.6))
            {
                lines.Add(new(guard, $"{walk.Name} is in the last year of his deal. {Pick(rng, "Every night is an audition, and he plays like it.", "Contract years bring out the best in people.", "Somebody's going to pay him this summer.")}"));
                break;
            }
        }
        foreach (var t in new[] { us, them })
            if (t.Roster.Where(p => p.IsAvailable(date) && p.InjuryHistory.LastOrDefault() is { } inj && inj.ReturnDate > date.AddDays(-8) && inj.ReturnDate.DayNumber - inj.Occurred.DayNumber >= 14)
                    .OrderByDescending(p => p.CurrentAbility).FirstOrDefault() is { } back)
            {
                var inj = back.InjuryHistory[^1];
                lines.Add(new(big, $"{back.Name} is back after missing {Math.Round((inj.ReturnDate.DayNumber - inj.Occurred.DayNumber) / 7.0)} weeks with a {inj.Name.ToLower()}. {Pick(rng, "Watch his minutes; they'll ease him in.", "Conditioning is the question, not talent.")}"));
                break;
            }
        foreach (var t in new[] { them, us })
            if (t.Roster.Where(p => p.Age(date) <= 22 && stats.GetValueOrDefault(p.Id) is { Games: >= 5, TeamId: var tid } st && tid == t.Id && st.Ppg >= 12)
                    .OrderByDescending(p => stats[p.Id].Ppg).FirstOrDefault() is { } kid)
            {
                lines.Add(new(num, $"Keep an eye on {kid.Name}: {kid.Age(date)} years old and {stats[kid.Id].Ppg:F1} points a game. {Pick(rng, "That's not normal for his age.", "The kid is ahead of schedule.")}"));
                break;
            }
        // The coaches.
        string theirCoach = them.Coach?.Name ?? them.CoachName;
        if (theirCoach is { Length: > 0 } && theirCoach != "Interim coach" && rng.Chance(0.5))
            lines.Add(new(big, $"On the other bench, {theirCoach}. {Pick(rng, "Watch his first timeout: it tells you whether he likes what he sees.", "His teams always come out with a plan.", "Smart coach. He'll make adjustments at the half.")}"));
        else if (theirCoach == "Interim coach")
            lines.Add(new(guard, $"{them.Name} are playing for an interim coach. Players are auditioning for whoever gets the job."));
        // The world's digest: the last ten days' stories about either club.
        var stories = WorldDigest.Where(d => d.Date >= date.AddDays(-10) && (Mentions(d, them) || Mentions(d, us))).OrderByDescending(d => d.Date).Take(2).ToList();
        foreach (var d in stories) lines.Add(new(host, $"In the news: {d.Title}. {FirstLine(d.Line)}"));
        // Your own recent headlines (records, milestones) from the inbox.
        var mineNews = Inbox.Where(m => m.Date >= date.AddDays(-7) && (m.Title.StartsWith("Milestone") || m.Title.StartsWith("Record") || m.Title.StartsWith("New league record"))).Take(1).ToList();
        foreach (var m in mineNews) lines.Add(new(num, $"{m.Title}. {FirstLine(m.Body)}"));
        // Last night's picks, if the panel has a record to defend.
        var last = StudioLastNight();
        if (last.Count > 0)
        {
            int right = last.Count(p => p.Correct == true);
            lines.Add(new(host, right == last.Count ? $"Quick note: the whole panel got last time right ({last[0].Final}). Don't let it go to their heads."
                : right == 0 ? $"For the record, nobody on this desk got the last one right ({last[0].Final})."
                : $"Last time out ({last[0].Final}), {right} of {last.Count} on the panel had it right."));
        }
        return lines;
    }

    private static bool Mentions(DigestItem d, Team t) => d.Title.Contains(t.Name) || d.Line.Contains(t.Name) || d.Title.Contains(t.City);

    private static string FirstLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var first = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
        return first.Length > 180 ? first[..first.LastIndexOf(' ', 177)] + "…" : first;
    }

    private static string Pick(Rng rng, params string[] options) => options[rng.Next(options.Length)];

    private void StandingsImpact(StudioShow show, Standings table, Team us, Team them, Series? series, Rng rng)
    {
        var host = show.Host; var num = StudioShow.Numbers; var big = StudioShow.BigMan;
        if (series != null)
        {
            show.ImpactLines.Add(new(num, series.Round == "Finals"
                ? $"Win and {us.Name} are {((series.Higher == us ? series.HigherWins : series.LowerWins) + 1 >= series.WinsNeeded ? "champions" : "one step closer to the title")}."
                : Postseason.IsThirdPlace(series.Round) ? $"The third-place winner takes {Postseason.ThirdPlacePrizeFor(true) / 1_000_000.0:F1} million dollars and a trophy; the runner-up half the money."
                : SeriesStakes(series, us)));
            return;
        }
        if (!table.Has(us)) return;
        int level = InNabl ? 1 : MyLevel;
        var all = table.All().ToList();
        List<TeamRecord> Division(Team t) => all.Where(r => r.Team.Conference == t.Conference && r.Team.Division == t.Division)
            .OrderByDescending(r => r.WinPct).ThenByDescending(r => r.PointDiff).ToList();
        var ourDiv = Division(us);
        bool divisional = level >= 1 && ourDiv.Count == 4;
        if (divisional)
        {
            void AddTable(Team t)
            {
                var div = Division(t);
                var lead = div[0];
                var rows = div.Select((r, i) =>
                {
                    double gb = ((lead.Wins - r.Wins) + (r.Losses - lead.Losses)) / 2.0;
                    return new ImpactRow(i + 1, r.Team, $"{r.Wins}-{r.Losses}", i == 0 ? "–" : gb.ToString("0.#"), Zone(level, i + 1), r.Team == us || r.Team == them);
                }).ToList();
                show.Impact.Add(new($"{t.Division} · {t.Conference}", rows));
            }
            AddTable(us);
            if (them.Division != us.Division || them.Conference != us.Conference) { if (Division(them).Count == 4) AddTable(them); }
            // Where a win or a loss tonight leaves you in your division.
            int now = ourDiv.FindIndex(r => r.Team == us) + 1;
            int ifWin = PlaceAfter(ourDiv, us, them, win: true), ifLose = PlaceAfter(ourDiv, us, them, win: false);
            string zNow = Zone(level, now), zWin = Zone(level, ifWin), zLose = Zone(level, ifLose);
            show.ImpactLines.Add(new(num, ifWin == ifLose
                ? $"{us.Name} stay {AwardsEngine.Ordinal(now)} in the {us.Division} whatever happens tonight{(zNow != "" ? $", in {ZonePhrase(zNow)}" : "")}."
                : $"A win puts {us.Name} {AwardsEngine.Ordinal(ifWin)} in the {us.Division}{(zWin != "" ? $", {ZonePhrase(zWin)}" : "")}; a loss leaves them {AwardsEngine.Ordinal(ifLose)}{(zLose != "" ? $", {ZonePhrase(zLose)}" : "")}."));
            bool sameDivision = them.Division == us.Division && them.Conference == us.Conference;
            if (sameDivision) show.ImpactLines.Add(new(big, "A division game: it counts twice. You win, you take one from the team you're chasing."));
            int gamesLeft = Schedule.Count(g => (g.Home == us || g.Away == us) && !Results.ContainsKey(g.Index));
            if (InNabl && zNow == "Relegation" && gamesLeft > 0)
                show.ImpactLines.Add(new(host, $"Fourth in the division means the drop to the G League. {gamesLeft} games left to climb out."));
            else if (zNow == "Promotion")
                show.ImpactLines.Add(new(host, "First place in the division goes up a tier at the end of the season. Every game matters."));
        }
        else
        {
            var conf = InNabl ? table.Conference(us.Conference) : all;
            int rank = conf.FindIndex(r => r.Team == us) + 1;
            if (rank > 0) show.ImpactLines.Add(new(num, $"{us.Name} sit {AwardsEngine.Ordinal(rank)} in the {(InNabl ? us.Conference.ToString() : MyTableName)} table."));
        }
        if (MyRecord.Games < 5) show.ImpactLines.Add(new(big, Pick(rng, "It's early. Nobody's season is decided in the first two weeks.", "Too early for the standings to mean much, but you don't want to dig a hole.")));
    }

    private static string ZonePhrase(string zone) => zone switch
    {
        "Playoffs" => "a playoff spot",
        "Third place" => "the third-place tournament spot",
        "Relegation" => "the relegation spot",
        "Promotion" => "the promotion spot",
        _ => zone.ToLower(),
    };

    private static int PlaceAfter(List<TeamRecord> div, Team us, Team them, bool win)
    {
        var sim = div.Select(r =>
        {
            int w = r.Wins, l = r.Losses;
            if (r.Team == us) { if (win) w++; else l++; }
            else if (r.Team == them) { if (win) l++; else w++; }
            return (r.Team, pct: w + l == 0 ? 0 : (double)w / (w + l), r.PointDiff);
        }).OrderByDescending(x => x.pct).ThenByDescending(x => x.PointDiff).ToList();
        return sim.FindIndex(x => x.Team == us) + 1;
    }

    /// <summary>The zone a place in a four-team division leads to (D33): the NBA's top two make the playoffs, third the
    /// third-place tournament, fourth goes down; below the NBA the winner goes up (and nobody goes down from tier 5).</summary>
    public static string Zone(int level, int place) => place switch
    {
        1 when level > 1 => "Promotion",
        1 or 2 => "Playoffs",
        3 => "Third place",
        4 when level < 5 => "Relegation",
        _ => "",
    };

    private void AwardRace(StudioShow show, Rng rng)
    {
        var table = Standings;
        var players = League.AllActivePlayers.ToDictionary(p => p.Id);
        double TeamGames(int id) => League.Teams.FirstOrDefault(t => t.Id == id) is { } t && table.Has(t) ? Math.Max(1, table[t].Games) : 1;
        double MvpScore(PlayerSeasonStats s)
        {
            var t = League.Teams.FirstOrDefault(x => x.Id == s.TeamId);
            double pct = t != null && table.Has(t) ? table[t].WinPct : 0.5;
            double availability = Math.Min(1.0, s.Games / Math.Max(1.0, TeamGames(s.TeamId) * 0.8));
            return (AwardsEngine.Production(s) + (pct - 0.5) * 82 * 0.45 + s.PlusMinus / (double)Math.Max(1, s.Games) * 0.6) * availability;
        }
        int maxGames = Stats.Values.Select(s => s.Games).DefaultIfEmpty(0).Max();
        if (maxGames < 3) return;
        var field = Stats.Values.Where(s => s.Games >= Math.Max(2, maxGames / 2) && players.ContainsKey(s.PlayerId)).ToList();
        bool rookies = rng.Chance(0.25) && field.Any(s => players[s.PlayerId].RookieSeason == League.Season);
        var pool = rookies ? field.Where(s => players[s.PlayerId].RookieSeason == League.Season).ToList() : field;
        Func<PlayerSeasonStats, double> score = rookies ? s => AwardsEngine.Production(s) : MvpScore;
        var ladder = pool.OrderByDescending(score).Take(5).ToList();
        if (ladder.Count == 0) return;
        show.RaceTitle = rookies ? "Rookie of the Year ladder" : "MVP ladder";
        var tonight = new HashSet<int> { show.Home.Id, show.Away.Id };
        for (int i = 0; i < ladder.Count; i++)
        {
            var s = ladder[i]; var p = players[s.PlayerId];
            var team = League.Teams.FirstOrDefault(t => t.Id == s.TeamId);
            show.Race.Add(new(i + 1, p.Name, team?.Abbreviation ?? "", $"{s.Ppg:F1} pts · {s.Rpg:F1} reb · {s.Apg:F1} ast", tonight.Contains(s.TeamId)));
        }
        var num = StudioShow.Numbers; var guard = StudioShow.Guard; var big = StudioShow.BigMan;
        var here = show.Race.FirstOrDefault(r => r.Tonight);
        if (here != null)
            show.RaceLines.Add(new(guard, here.Rank == 1
                ? $"{here.Name} is the front-runner, and a big night on HSN is how you keep it. Voters watch these games."
                : $"{here.Name} is {AwardsEngine.Ordinal(here.Rank)} on the ladder. A statement game tonight and he's right in the conversation."));
        else show.RaceLines.Add(new(big, $"Nobody on the floor tonight is in the top five. {show.Race[0].Name} is the name to catch."));
        if (!rookies && show.Race.Count >= 2)
            show.RaceLines.Add(new(num, $"{show.Race[0].Name} leads on production and his team's record. {show.Race[1].Name} is closest; it's a long season."));
    }

    private static (Team pick, int margin, string why) PanelPick(StudioAnalyst a, Team home, Team away, double margin, double sd,
        Dictionary<Team, (int w, int l, int streak)> form, Dictionary<Team, List<Player>> hurt, Rng rng)
    {
        // Each voice leans its own way, with a little noise so the desk doesn't agree every night.
        double lean = a.Key switch
        {
            "numbers" => 0,
            "big" => 1.5 + rng.NextGaussian(0, 2.5),                                       // trusts the home floor
            "guard" => (form[home].streak - form[away].streak) * 0.8 + rng.NextGaussian(0, 4), // the hot hand, and upsets
            _ => (hurt[away].Count - hurt[home].Count) * 1.0 + rng.NextGaussian(0, 2),      // the coach: who's healthy
        };
        double m = margin + lean;
        var pick = m >= 0 ? home : away;
        int by = Math.Clamp((int)Math.Round(Math.Abs(m)), 1, 25);
        if (a.Key != "numbers") by = Math.Clamp(by + rng.Next(-1, 3), 1, 25);
        var other = pick == home ? away : home;
        string why = a.Key switch
        {
            "numbers" => $"The model has {pick.Name} by {by}. I don't argue with it.",
            "big" => pick == home ? $"Home floor, {pick.Name} control the paint. {pick.Abbreviation} by {by}." : $"{pick.Name} are just bigger and tougher. They win on the road by {by}.",
            "guard" => form[pick].streak >= 3 ? $"{pick.Name} are rolling. Ride the hot team: {pick.Abbreviation} by {by}."
                     : pick == (margin >= 0 ? away : home) ? $"I'm calling the upset. {pick.Name} by {by}, and you heard it here first." : $"{pick.Name}'s stars are better. {pick.Abbreviation} by {by}.",
            _ => hurt[other].Count > hurt[pick].Count ? $"{other.Name} are short-handed. Healthy teams win: {pick.Abbreviation} by {by}." : $"{pick.Name} execute better late. {pick.Abbreviation} by {by}.",
        };
        return (pick, by, why);
    }

    private static double Phi(double x)
    {
        // Abramowitz-Stegun approximation of the normal CDF.
        double t = 1 / (1 + 0.2316419 * Math.Abs(x));
        double d = 0.3989423 * Math.Exp(-x * x / 2);
        double p = d * t * (0.3193815 + t * (-0.3565638 + t * (1.781478 + t * (-1.821256 + t * 1.330274))));
        return x >= 0 ? 1 - p : p;
    }
}
