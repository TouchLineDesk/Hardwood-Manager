using Hardwood.Core.Model;
using Hardwood.Core.World;

namespace Hardwood.Core.Jobs;

/// <summary>
/// What the board needs to know about the league a club plays in (stage 3: any club in the world):
/// its clubs, how many games a season has, how many make the playoffs, whether there's a luxury tax,
/// and how prestigious the league's jobs are compared with the NABL.
/// </summary>
public sealed record LeagueContext(string Name, IReadOnlyList<Team> Teams, int SeasonGames, int PlayoffTeams, bool IsNabl,
                                   Finance? Finance, double PrestigeOffset, int PyramidLevel = 0, bool IsCollege = false)
{
    /// <summary>The semi-pro and amateur levels of the USA pyramid (4 regional, 5 local): owners there are patient and modest.</summary>
    public bool Grassroots => PyramidLevel >= 4;

    /// <summary>Danny's division format (D36): the top two in each division make the playoffs and the third-placed teams play
    /// their own tournament, so the board's "play-in" goal reads as a top-three finish in the division.</summary>
    public bool DivisionFormat => !IsCollege
        && ((IsNabl && Season.Postseason.UsesDivisions(Teams.ToList())) || (PyramidLevel is 2 or 3 && Teams.Count == UsaPyramid.ProSize));

    public static LeagueContext Nabl(League league) =>
        new(league.Name, league.Teams, 82, 16, true, league.Finance, 0);

    /// <summary>A domestic league outside the NABL: games from its round robin, playoffs from its bracket.</summary>
    public static LeagueContext Of(WorldCompetition comp)
    {
        var p = comp.Profile;
        int n = comp.Clubs.Count;
        int games = (n - 1) * p.RoundRobins;
        int playoff = p.Playoffs.Length > 0 ? p.Playoffs[0].Teams : 1;
        return new(p.Name, comp.Clubs, games, playoff, false, null, p.JobPrestige, UsaPyramid.Level(comp.Key));
    }

    /// <summary>
    /// College (slice C2, #168): the athletic director ranks a program against every program in the country, judges the
    /// season by March Madness, and expects about a regular season's worth of games (non-conference, conference, a tournament).
    /// </summary>
    public static LeagueContext College(College.CollegeWorld cw, Team program)
    {
        int confSize = cw.ConferenceOf.TryGetValue(program.Id, out var key) ? cw.InConference(key).Count() : 14;
        return new("College basketball", cw.Programs, 10 + confSize - 1 + 3, 68, false, null, -10, 0, true);
    }

    /// <summary>Where a team sits on a 0 (best) to 1 (worst) scale given its projected rank.</summary>
    public double Fraction(int rank) => Teams.Count <= 1 ? 0 : (rank - 1) / (double)(Teams.Count - 1);
}
