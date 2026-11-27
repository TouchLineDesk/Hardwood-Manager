using Hardwood.Core.Model;
using Hardwood.Core.People;

namespace Hardwood.Core.Season;

/// <summary>
/// W1 (run 221, `claude/world-stories-plan.md`): the media cast's preseason picks, judged at the end of the regular season,
/// and the stats writer's power rankings in pro careers. Picks are made once a season for the table you play in, kept in the
/// save (<see cref="ColumnBook"/>), and judged only for the team they were made about. No random draw (<see cref="MediaCast"/>).
/// </summary>
public sealed partial class GameSession
{
    /// <summary>Days between the power rankings in your results message (about every fourth week).</summary>
    public const int RankingsGapDays = 28;
    /// <summary>Games your team must have played before the first power rankings.</summary>
    public const int RankingsMinGames = 8;

    /// <summary>The season the columnists' picks belong to (a college season's starting year; the pros' season otherwise).</summary>
    public int ColumnYear => InCollege && World.College is { } c ? c.Season.Year : League.Season;

    /// <summary>Your table as the columnists name it ("East", "ACC", "Liga ACB").</summary>
    public string ColumnTable => InCollege ? MyConference?.Short ?? HumanLeagueName : MyTableName;

    /// <summary>The columnists who cover your team this career.</summary>
    public List<Columnist> MyColumnists => MediaCast.CastFor(HumanTeam, InCollege);

    /// <summary>This season's picks about your team (empty before they're made, or when you joined after them).</summary>
    public List<ColumnPicks> MyColumnPicks => Media.Columns.For(ColumnYear, HumanTeam.Id);

    /// <summary>Called at the end of each day, after the college calendar (whose preseason message the college picks ride in).</summary>
    internal void MediaCastDay()
    {
        if (!Employed || Phase == SessionPhase.Offseason) return;
        int year = ColumnYear;
        var mine = Media.Columns.For(year, HumanTeam.Id);
        if (mine.Count == 0)
        {
            if (PicksDue(out var preseason)) MakeColumnPicks(year, preseason);
        }
        else if (mine.All(p => p.Judged == null) && RegularSeasonOver()) JudgeColumnPicks(mine);
    }

    /// <summary>Before your table's first game: in college on the preseason message's day (the picks ride in it).</summary>
    private bool PicksDue(out InboxMessage? preseason)
    {
        preseason = null;
        var table = MyTable;
        if (table.Count < 3 || table.Any(r => r.Games > 0)) return false;
        if (InCollege)
        {
            preseason = Inbox.FirstOrDefault(m => m.Date == Today && m.Title.StartsWith("Preseason:"));
            return preseason != null;
        }
        return InNabl ? Phase == SessionPhase.Preseason : HumanLeague?.Season.Stage == Hardwood.Core.World.CompetitionSeason.RegularSeason;
    }

    private bool RegularSeasonOver() =>
        InCollege ? World.College!.Season.Selected
        : InNabl ? Phase == SessionPhase.Postseason
        : HumanLeague?.Season.Stage != Hardwood.Core.World.CompetitionSeason.RegularSeason && MyRecord.Games > 0;

    private void MakeColumnPicks(int year, InboxMessage? preseason)
    {
        var teams = MyTable.Select(r => r.Team).ToList();
        string table = ColumnTable;
        var c = World.College;
        Func<Team, double>? reputation = InCollege && c != null ? t => c.Season.Elo.GetValueOrDefault(t.Id, 1500) : null;
        var cast = MyColumnists;
        var lines = new List<string>();
        foreach (var w in cast)
        {
            var order = MediaCast.Pick(w, teams, HumanTeam, t => Hardwood.Core.World.QuickSim.Strength(t), reputation, Seed, year);
            Media.Columns.Picks.Add(new ColumnPicks
            {
                Season = year, TeamId = HumanTeam.Id, Key = w.Key, Name = w.Name, Outlet = w.Outlet, Table = table, Order = order, Made = Today,
            });
            int pick = order.IndexOf(HumanTeam.Id) + 1;
            string top = string.Join(", ", order.Take(3).Select((id, i) => $"{i + 1}. {NameOf(teams, id)}"));
            lines.Add($"• {MediaCast.PreseasonLine(w, pick, teams.Count, HumanTeam.FullName, table)}\n  {w.HeCap} picks {top}.");
        }
        Media.Columns.Picks.RemoveAll(p => p.Season < year - 12); // the almanac (W2) reads a dozen seasons back at most
        string footer = $"The picks are kept: at the end of the regular season they'll be held to them.";
        if (preseason != null)
        {
            // College: the picks ride in the preseason message (inbox volume stays low).
            Inbox[Inbox.IndexOf(preseason)] = new InboxMessage { Id = preseason.Id, Date = preseason.Date, Title = preseason.Title,
                Body = preseason.Body + "\n\nThe columnists' picks:\n" + string.Join("\n", lines) + "\n" + footer, Category = preseason.Category, Read = preseason.Read };
            return;
        }
        var beat = cast[0];
        int beatPick = Media.Columns.For(year, HumanTeam.Id).First(p => p.Key == beat.Key).PickOf(HumanTeam.Id);
        Post($"Preseason picks: {beat.Name} has you {Ordinal(beatPick)} in the {table}",
            $"Before the season, the columnists pick the {table}:\n\n" + string.Join("\n", lines) + "\n\n" + footer, "League");
    }

    private void JudgeColumnPicks(List<ColumnPicks> picks)
    {
        var finished = MyTable.Select(r => r.Team.Id).ToList();
        var teams = MyTable.Select(r => r.Team).ToList();
        foreach (var p in picks)
        {
            p.Finished = finished.Where(p.Order.Contains).ToList();
            p.Judged = Today;
        }
        int finish = picks[0].FinishOf(HumanTeam.Id);
        if (finish == 0) return;
        string table = picks[0].Table;
        var cast = MyColumnists.ToDictionary(w => w.Key);
        var lines = new List<string>();
        foreach (var p in picks)
        {
            var w = cast.GetValueOrDefault(p.Key) ?? new Columnist(p.Key, p.Name, p.Outlet, ColumnVoice.Numbers, false);
            lines.Add("• " + MediaCast.VerdictLine(w, p.PickOf(HumanTeam.Id), finish, HumanTeam.FullName, table));
        }
        // Around the table: each writer's best call and biggest miss (the almanac, W2, keeps them).
        var calls = new List<string>();
        foreach (var p in picks)
        {
            var miss = p.Order.Select(id => (id, d: Math.Abs(p.PickOf(id) - p.FinishOf(id)))).OrderByDescending(x => x.d).ThenBy(x => x.id).First();
            var best = p.Order.Where(id => p.PickOf(id) == p.FinishOf(id)).OrderBy(p.PickOf).FirstOrDefault(-1);
            string b = best >= 0 ? $"called {NameOf(teams, best)} {Ordinal(p.PickOf(best))} exactly" : "called nobody's place exactly";
            calls.Add($"{p.Name} {b}; biggest miss: {NameOf(teams, miss.id)} (picked {Ordinal(p.PickOf(miss.id))}, finished {Ordinal(p.FinishOf(miss.id))}).");
        }
        int beatPick = picks[0].PickOf(HumanTeam.Id);
        string title = finish == 1 ? $"Picked {Ordinal(beatPick)}, won the {table}: the columnists look back"
            : $"Picked {Ordinal(beatPick)}, finished {Ordinal(finish)}: the columnists look back";
        Post(title, $"The regular season is over, and the preseason picks are judged.\n\n{string.Join("\n", lines)}\n\nAround the {table}: {string.Join(" ", calls)}", "League");
    }

    /// <summary>Pros and abroad: the stats writer's power rankings ride in your weekly results message about every fourth week.</summary>
    internal void AttachPowerRankings()
    {
        if (!Employed || InCollege) return;
        bool regular = InNabl ? Phase == SessionPhase.RegularSeason : HumanLeague?.Season.Stage == Hardwood.Core.World.CompetitionSeason.RegularSeason;
        if (!regular || MyRecord.Games < RankingsMinGames) return;
        var book = Media.Columns;
        if (Today.DayNumber - book.LastRankings.DayNumber < RankingsGapDays) return;
        var week = Inbox.FirstOrDefault(m => m.Date == Today && m.Category == "Game" && IsResultsTitle(m.Title));
        if (week == null) return;
        var all = MyStandings.All().Where(r => r.Games > 0).OrderByDescending(MediaCast.PowerScore).ThenBy(r => r.Team.Id).ToList();
        int rank = all.FindIndex(r => r.Team == HumanTeam) + 1;
        if (rank == 0 || all.Count < 4) return;
        var w = MediaCast.Numbers;
        string top = string.Join(", ", all.Take(5).Select((r, i) => $"{i + 1}. {r.Team.FullName} ({r.Wins}-{r.Losses})"));
        string since = book.LastRank > 0 && book.LastRankings.DayNumber > 0 && Today.DayNumber - book.LastRankings.DayNumber <= 60
            ? rank < book.LastRank ? $", up {book.LastRank - rank} since the last rankings" : rank > book.LastRank ? $", down {rank - book.LastRank} since the last rankings" : ", the same as last time"
            : MyColumnPicks.FirstOrDefault(p => p.Key == w.Key) is { } pk ? $" ({w.He} picked you {Ordinal(pk.PickOf(HumanTeam.Id))} in the {pk.Table} before the season)" : "";
        string body = $"\n\nPOWER RANKINGS, by {w.Byline}: {top}. {HumanTeam.FullName}: {Ordinal(rank)} of {all.Count}{since}.";
        Inbox[Inbox.IndexOf(week)] = new InboxMessage { Id = week.Id, Date = week.Date, Title = week.Title, Body = week.Body + body, Category = week.Category, Read = week.Read };
        book.LastRankings = Today;
        book.LastRank = rank;
    }

    private static string NameOf(List<Team> teams, int id) => teams.FirstOrDefault(t => t.Id == id)?.FullName ?? "?";
}
