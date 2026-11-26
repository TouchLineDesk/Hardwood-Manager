using Hardwood.Core.Engine;
using Hardwood.Core.Model;

namespace Hardwood.Core.International;

/// <summary>I6b (run 215): one qualifying game of a window, before it is played: its window ("2026-11"), group and day.</summary>
public sealed record NationalFixture(string Key, string Group, string Home, string Away, DateOnly Day);

/// <summary>I6b (run 215): one of your nation's window games, played in the full engine on its day (#109): live, or by your
/// assistant when your club played the same day. The window's record takes this score for the pairing.</summary>
public sealed record CoachedGame(string Key, string Group, string Home, string Away, int HomePts, int AwayPts, DateOnly Day, bool Live)
{
    public bool Won(string code) => code == Home ? HomePts > AwayPts : AwayPts > HomePts;
}

/// <summary>
/// I6b (run 215, #108, #109): the national coach's games and the federation's judgement.
/// <list type="bullet">
/// <item>A window game of your nation is played in the full engine on <see cref="Rules.FibaNational"/> from your picks, on its own
/// stream (the seed, the day and the pairing), so no shared stream moves and the club's day is unchanged.</item>
/// <item>Reputation: each window moves it by how your results beat the ranking's expectation; a summer tournament by where you
/// finished against where you were seeded, plus a medal's lift.</item>
/// <item>The sack: a federation ends the job after a tournament that finished far below the seeding (a top-quarter seed out in
/// the group, or half the field below its seed).</item>
/// </list>
/// </summary>
public static class NationalGames
{
    /// <summary>A window's reputation swing: this much per win above the ranking's expectation, capped either way.</summary>
    public const double WindowRepPerWin = 1.5, WindowRepCap = 3.0;
    /// <summary>A tournament: this much per place above the seeding, capped either way; then the medal's lift.</summary>
    public const double TournamentRepPerPlace = 0.4, TournamentRepCap = 4.0;

    public static ulong GameSeed(ulong seed, NationalFixture f) =>
        seed ^ 0x4E41_5447_414D_4542UL ^ ((ulong)(uint)f.Day.DayNumber * 0x9E3779B97F4A7C15UL)
             ^ ((ulong)(uint)StringComparer.Ordinal.GetHashCode(f.Home) * 31) ^ (ulong)(uint)f.Away.Length;

    /// <summary>A temporary side for one national game: the twelve on the floor, the nation's code on the scoreboard.
    /// The away side wears a contrasting kit so the dots differ on the court.</summary>
    public static Team TeamOf(NationalTeam n, int id, bool home)
    {
        var t = new Team
        {
            Id = id, City = n.Name, Name = n.Name, Abbreviation = n.Nation.Code, Division = "FIBA",
            PrimaryColor = home ? "#1D3FA3" : "#C8102E", SecondaryColor = home ? "#FFFFFF" : "#F2F2F2",
        };
        foreach (var x in n.Squad) t.Roster.Add(x.Player);
        return t;
    }

    /// <summary>The reputation a window's games are worth: the wins above what the ranking expected, ±<see cref="WindowRepCap"/>.</summary>
    public static double WindowReputation(IEnumerable<CoachedGame> games, string code, IReadOnlyDictionary<string, double> pointsBefore)
    {
        double sum = 0;
        foreach (var g in games)
        {
            string opp = g.Home == code ? g.Away : g.Home;
            double us = pointsBefore.GetValueOrDefault(code, FibaRanking.Top / 2), them = pointsBefore.GetValueOrDefault(opp, FibaRanking.Top / 2);
            sum += (g.Won(code) ? 1 : 0) - FibaRanking.Expected(us, them);
        }
        return Math.Round(Math.Clamp(sum * WindowRepPerWin, -WindowRepCap, WindowRepCap), 1);
    }

    /// <summary>Where a team finished, as a place (1 gold … the field's size), from the rounds it reached.</summary>
    public static double FinishPlace(WorldCupRecord rec, string code)
    {
        if (code == rec.Gold) return 1;
        if (code == rec.Silver) return 2;
        if (code == rec.Bronze) return 3;
        if (code == rec.Fourth) return 4;
        bool Reached(string round) => rec.Games.Any(g => g.Round == round && (g.Home == code || g.Away == code));
        int n = rec.Teams.Count;
        if (Reached(WorldCup.SemiRound)) return 3.5;
        if (Reached(WorldCup.QuarterRound)) return 6.5;
        if (Reached(Tournament.RoundOf16) || Reached(WorldCup.SecondRound)) return 12.5;
        // Out in the group: the places below the knockouts' (the field's lower part).
        int knock = rec.Games.Any(g => g.Round == Tournament.RoundOf16 || g.Round == WorldCup.SecondRound) ? 16 : 8;
        return (Math.Min(knock, n) + 1 + n) / 2.0;
    }

    /// <summary>Where a team was seeded in the field: its place among the field by world rank (1 the best).</summary>
    public static int SeedPlace(WorldCupRecord rec, string code)
    {
        var order = rec.Teams.OrderBy(t => t.Rank <= 0 ? 999 : t.Rank).ThenBy(t => t.Code, StringComparer.Ordinal).Select(t => t.Code).ToList();
        int i = order.IndexOf(code);
        return i < 0 ? rec.Teams.Count : i + 1;
    }

    /// <summary>The federation's verdict on a tournament: the reputation it's worth and whether it ends the job.</summary>
    public static (double Reputation, bool Sacked, string Verdict) Review(WorldCupRecord rec, string code, bool major)
    {
        double finish = FinishPlace(rec, code);
        int seed = SeedPlace(rec, code), n = Math.Max(1, rec.Teams.Count);
        double rep = Math.Clamp((seed - finish) * TournamentRepPerPlace, -TournamentRepCap, TournamentRepCap);
        double medal = code == rec.Gold ? 6 : code == rec.Silver ? 4 : code == rec.Bronze ? 3 : 0;
        rep += major ? medal : medal * 0.6;
        string[] knockouts = { WorldCup.SecondRound, Tournament.RoundOf16, WorldCup.QuarterRound, WorldCup.SemiRound, WorldCup.BronzeRound, WorldCup.FinalRound };
        bool groupOut = !rec.Games.Any(g => (g.Home == code || g.Away == code) && knockouts.Contains(g.Round));
        bool sacked = (groupOut && seed <= Math.Max(2, n / 4)) || finish - seed >= n / 2.0;
        string verdict = medal > 0 ? "delighted" : finish <= seed - 2 ? "pleased" : finish <= seed + 2 ? "satisfied" : sacked ? "has seen enough" : "disappointed";
        return (Math.Round(rep, 1), sacked, verdict);
    }
}
