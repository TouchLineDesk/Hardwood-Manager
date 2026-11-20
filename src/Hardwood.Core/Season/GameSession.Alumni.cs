using Hardwood.Core.College;
using Hardwood.Core.Model;
using Hardwood.Core.World;

namespace Hardwood.Core.Season;

/// <summary>One of a program's graduates as the alumni tracker shows him (CP3, run 132).</summary>
public sealed record AlumnusLine(Player Player, int Class, string Club, string League, bool TopLeague, bool Retired,
                                 int AllStars, int Awards, DraftRecord? Draft);

/// <summary>A program's pro record: its graduates in the pros and what they've won.</summary>
public sealed record ProRecord(int Pros, int TopLeague, int AllStars, int Awards, IReadOnlyList<AlumnusLine> Lines, int Abroad = 0)
{
    /// <summary>"12 pros · 5 in the NBA · 4 abroad · 3 All-Stars" (or "No pros yet").</summary>
    public string Summary(string top) => Pros == 0 ? "No pros yet"
        : $"{Pros} pro{(Pros == 1 ? "" : "s")} · {TopLeague} in the {top}" + (Abroad > 0 ? $" · {Abroad} abroad" : "")
          + (AllStars > 0 ? $" · {AllStars} All-Star{(AllStars == 1 ? "" : "s")}" : "");
}

/// <summary>
/// CP3 (run 132, `claude/college-pipeline-design.md`): show the payoff of developing players. The college world keeps every
/// graduate's program (<see cref="CollegeWorld.Alumni"/>); this finds them in the pros for the alumni tracker, the recruiting
/// pitch and the potential ranges a college coach sees.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>A college player's or high-school recruit's potential as you see it (a range); null for anyone else,
    /// whose potential stays exact (#119).</summary>
    public Scouting.Range? CollegePotentialOf(Player p)
    {
        if (World.College is not { } c) return null;
        var cy = c.ClassYearOf(p, Today);
        var r = cy == null && p.TeamId == null && c.Recruits.ContainsKey(p.Id) && c.Prospects.Contains(p) ? c.RecruitOf(p) : null;
        // This summer's graduates in the pros' draft are still read as college players (the review of PR #196): the draft
        // board and his page agree (college careers only; a pro coach keeps exact numbers). Your own graduate is known as a senior.
        if (cy == null && r == null && InCollege && p.TeamId == null && Offseason?.Prospects.Contains(p) == true && c.Alumni.TryGetValue(p.Id, out var a))
            return Scouting.CollegePotential(p, a.ProgramId == HumanTeam.Id ? 0.85 : Scouting.CollegeKnowledge(HumanTeam, p, 0));
        if (cy == null && r == null) return null;
        bool scouted = r is { Scouted: true } && r.CourtedBy == HumanTeam.Id;
        return Scouting.CollegePotential(p, Scouting.CollegeKnowledge(HumanTeam, p, (cy?.Year ?? 1) - 1, scouted));
    }

    /// <summary>A program's graduates who turned pro, newest class first, then best first.</summary>
    public ProRecord ProRecordOf(Team program)
    {
        var lines = new List<AlumnusLine>();
        if (World.College is { } c)
        {
            var mine = c.Alumni.Where(kv => kv.Value.ProgramId == program.Id).ToDictionary(kv => kv.Key, kv => kv.Value);
            if (mine.Count > 0)
            {
                var retired = League.Retired.Where(p => mine.ContainsKey(p.Id)).ToDictionary(p => p.Id);
                var top = League.Teams.ToHashSet();
                var draft = Offseason?.Prospects ?? new List<Player>();
                foreach (var p in League.AllActivePlayers.Concat(League.FreeAgents).Concat(draft).Concat(World.Players).Concat(retired.Values).DistinctBy(p => p.Id))
                {
                    if (!mine.TryGetValue(p.Id, out var a)) continue;
                    var club = p.TeamId is { } id ? FindTeam(id) : null;
                    bool isTop = club != null && top.Contains(club);
                    string league = club == null ? "" : isTop ? Names.Brand.TopShort : World.LeagueOf(club)?.Profile.Short ?? "";
                    var career = League.Careers.GetValueOrDefault(p.Id);
                    int awards = career == null ? 0 : career.Mvps + career.Dpoys + career.Roys + career.SixthMen + career.FinalsMvps + career.AllLeague;
                    bool gone = retired.ContainsKey(p.Id);
                    // K13 (run 133): a young graduate who found no club has left pro basketball; he didn't "retire" at 22.
                    string left = p.Age(Today) < 28 ? "Out of pro basketball" : "Retired";
                    lines.Add(new AlumnusLine(p, a.Year, gone ? left : club?.FullName ?? (draft.Contains(p) ? "In the draft" : "Free agent"), league, isTop && !gone, gone,
                        career?.AllStars ?? 0, awards, p.Draft));
                }
            }
        }
        lines = lines.OrderByDescending(l => l.Class).ThenByDescending(l => l.Player.CurrentAbility).ToList();
        int pros = lines.Count(l => !l.Retired && l.Player.TeamId != null);
        int abroad = lines.Count(l => !l.Retired && l.Player.TeamId is { } tid && FindTeam(tid) is { } t && World.LeagueOf(t) is { } comp && !UsaPyramid.IsNational(comp.Key));
        return new ProRecord(pros, lines.Count(l => l.TopLeague), lines.Count(l => l.AllStars > 0), lines.Sum(l => l.Awards), lines, abroad);
    }

    /// <summary>
    /// The coached program's pitch to recruits (CP3): its alumni in the top league and its All-Stars, a few appeal points at
    /// most (<see cref="CollegeWorld.PitchFor"/>). Called when a career loads and after each summer and the awards.
    /// </summary>
    public void RefreshProPitch()
    {
        Staff.CollegeWords = Employed && InCollege; // K14: staff titles follow the job (called on load, hire and each summer)
        if (World.College is not { } c) return;
        if (!Employed || !InCollege || !IsCollegeProgram(HumanTeam)) { c.ProPitch = 0; c.ProPitchText = ""; return; }
        var rec = ProRecordOf(HumanTeam);
        c.ProPitch = CollegeWorld.PitchPoints(rec.TopLeague, rec.Lines.Count(l => l.TopLeague && l.AllStars > 0));
        int stars = rec.Lines.Count(l => l.TopLeague && l.AllStars > 0); // what the pitch counts (the review of PR #196)
        c.ProPitchText = rec.TopLeague == 0 ? $"none in the {Names.Brand.TopShort} yet"
            : $"{rec.TopLeague} in the {Names.Brand.TopShort}" + (stars > 0 ? $", {stars} All-Star{(stars == 1 ? "" : "s")}" : "");
    }

    /// <summary>
    /// The college world keeps alumni only while they matter (CP3): every graduate of the program you coach, and anyone
    /// else until he retires. Retired graduates of other programs are dropped each summer, so the save doesn't grow forever.
    /// </summary>
    internal void PruneAlumni()
    {
        if (World.College is not { } c || c.Alumni.Count == 0) return;
        int? mine = Employed && InCollege ? HumanTeam.Id : null;
        foreach (var p in League.Retired)
            if (c.Alumni.TryGetValue(p.Id, out var a) && a.ProgramId != mine) c.Alumni.Remove(p.Id);
    }
}
