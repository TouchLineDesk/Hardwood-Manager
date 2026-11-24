using Hardwood.Core.Engine;
using Hardwood.Core.Model;

namespace Hardwood.Core.Season;

/// <summary>K29 (run 181): how long the halftime and postgame desk runs. A short show for an ordinary game; the full show
/// (the turning point, around college) against a ranked opponent, in a rivalry or in the postseason.</summary>
public enum DeskShowLength { Short, Full }

/// <summary>
/// K29 (run 181): what the studio desk knows about tonight beyond the box score: how long the show runs, the occasion
/// ("Rivalry · the Crosstown Shootout", "March Madness"), and the matchup rows the big screen fills with (records, ranks,
/// conference records, recent form, the series). Rows are away, home. Pure data: the session builds it, the desks read it.
/// </summary>
public sealed record DeskContext(DeskShowLength Length, string Occasion, IReadOnlyList<DeskRow> Matchup,
    string? Rivalry = null, int? AwayRank = null, int? HomeRank = null)
{
    /// <summary>No session (tests, old callers): the full show with nothing extra on the screen.</summary>
    public static readonly DeskContext None = new(DeskShowLength.Full, "", Array.Empty<DeskRow>());
    public bool Full => Length == DeskShowLength.Full;
}

public sealed partial class GameSession
{
    /// <summary>
    /// K29 (run 181): the desk's context for tonight's live game. <paramref name="final"/>: the postgame, where tonight's result
    /// is counted into the records and form (it is recorded on Continue). Reads the save only; draws no random numbers.
    /// </summary>
    public DeskContext DeskContextFor(GameResult r, bool final)
    {
        var (a, h) = (r.Away.Team, r.Home.Team);
        bool counts = final && LiveGameCountsInRecord;
        var rows = new List<DeskRow>();
        string occasion = "";
        string? rivalryLabel = null;
        int? rankA = null, rankH = null;
        bool full;
        int? Won(Team t) => counts ? (r.Winner.Team == t ? 1 : 0) : null;

        string Rec(Team t)
        {
            var rec = RecordOf(t);
            int w = rec.Wins + (Won(t) ?? 0), l = rec.Losses + (Won(t) is 0 ? 1 : 0);
            return w + l == 0 ? "-" : $"{w}-{l}";
        }

        if (InCollege && World.College is { } cw)
        {
            var g = _liveCollege;
            int? rkA = cw.Rank(a.Id), rkH = cw.Rank(h.Id);
            var rivalry = cw.RivalryOf(a.Id, h.Id);
            (rivalryLabel, rankA, rankH) = (rivalry?.Label, rkA, rkH);
            bool postseason = g != null && !College.CollegeWorld.IsRegularSeason(g);
            // A ranked opponent makes it a big night; your own ranking alone doesn't (No. 1 at home to a low-major is ordinary).
            int? oppRank = HumanTeam == h ? rkA : HumanTeam == a ? rkH : rkA ?? rkH;
            full = oppRank != null || rivalry != null || postseason;
            occasion = g?.Stage == College.CollegeWorld.Madness ? "March Madness"
                : g?.Stage == "Conference tournament" ? (g.Event ?? "Conference tournament")
                : rivalry != null ? $"Rivalry · {Cap(rivalry.Name ?? (rivalry.InState ? "in-state rivals" : "rivals"))}"
                : rkA != null && rkH != null ? $"Top 25 · #{rkA} vs #{rkH}"
                : oppRank != null ? "Top 25"
                : g?.Stage is "Invitational" or "Showcase" ? (g.Event ?? g.Stage) : "";
            rows.Add(new("Record", Rec(a), Rec(h)));
            rows.Add(new("Top 25", rkA is { } x ? $"No. {x}" : "-", rkH is { } y ? $"No. {y}" : "-"));
            bool conf = g?.Stage == "Conference";
            string Conf(Team t)
            {
                var (w, l) = cw.ConferenceRecord(t.Id);
                if (conf && Won(t) is { } win) { w += win; l += 1 - win; }
                return w + l == 0 ? "-" : $"{w}-{l}";
            }
            rows.Add(new("Conference", Conf(a), Conf(h)));
            string Last5(Team t)
            {
                var played = cw.GamesOf(t.Id).Where(x => x.Played).OrderByDescending(x => x.Date).Select(x => x.WinnerId == t.Id).ToList();
                if (Won(t) is { } win) played.Insert(0, win == 1);
                var last = played.Take(5).ToList();
                return last.Count == 0 ? "-" : $"{last.Count(x => x)}-{last.Count(x => !x)}";
            }
            rows.Add(new("Last 5", Last5(a), Last5(h)));
            if (rivalry != null && cw.SeriesOf(a.Id, h.Id) is { Games: > 0 } s)
            {
                int aw = a.Id < h.Id ? s.LoWins : s.HiWins, hw = s.Games - aw;
                if (Won(a) is { } win) { aw += win; hw += 1 - win; }
                rows.Add(new("Series", aw.ToString(), hw.ToString(), Math.Sign(hw - aw)));
            }
        }
        else
        {
            full = Phase == SessionPhase.Postseason;
            occasion = full ? "Postseason" : "";
            rows.Add(new("Record", Rec(a), Rec(h)));
            string Form10(Team t)
            {
                var (w, l, _) = Form(t);
                return w + l == 0 ? "-" : $"{w}-{l}";
            }
            rows.Add(new("Last 10", Form10(a), Form10(h)));
        }
        return new DeskContext(full ? DeskShowLength.Full : DeskShowLength.Short, occasion, rows, rivalryLabel, rankA, rankH);
    }

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
