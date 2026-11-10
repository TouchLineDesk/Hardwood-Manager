using Hardwood.Core.Model;
using Hardwood.Core.World;

namespace Hardwood.Core.Jobs;

/// <summary>
/// What the board needs to know about the league a club plays in (stage 3: any club in the world):
/// its clubs, how many games a season has, how many make the playoffs, whether there's a luxury tax,
/// and how prestigious the league's jobs are compared with the NABL.
/// </summary>
public sealed record LeagueContext(string Name, IReadOnlyList<Team> Teams, int SeasonGames, int PlayoffTeams, bool IsNabl,
                                   Finance? Finance, double PrestigeOffset, int PyramidLevel = 0)
{
    /// <summary>The semi-pro and amateur levels of the USA pyramid (4 regional, 5 local): owners there are patient and modest.</summary>
    public bool Grassroots => PyramidLevel >= 4;

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

    /// <summary>Where a team sits on a 0 (best) to 1 (worst) scale given its projected rank.</summary>
    public double Fraction(int rank) => Teams.Count <= 1 ? 0 : (rank - 1) / (double)(Teams.Count - 1);
}
