using Hardwood.Core.College;
using Hardwood.Core.Model;

namespace Hardwood.Core.Season;

/// <summary>CS7 part 1 (run 170, K19): the college program's boosters and the facilities they pay for.</summary>
public sealed partial class GameSession
{
    /// <summary>A college head coach builds his program (a pro club has Facilities and the match-day business).</summary>
    public bool HasProgramBuilding => Employed && InCollege && World.College != null;

    /// <summary>Your program's books, seeded from its prestige the first time they're used.</summary>
    public ProgramBooks MyProgramBooks => ProgramBuilding.Ensure(HumanTeam, World.College!.Prestige.GetValueOrDefault(HumanTeam.Id, 50));

    /// <summary>A project opens at once when no game of the season is being played: before the first game, or after the
    /// national final. Mid-season it's built through the year and opens next summer.</summary>
    public bool ProgramBuildsNow => World.College is { } c && (!c.Season.Games.Any(g => g.Played) || c.Season.ChampionId != null);

    /// <summary>Pay for the next level of a facility from the booster fund. Returns a line for the page.</summary>
    public string BuildProgramFacility(FacilityKind k)
    {
        if (!HasProgramBuilding) return "Only a college program builds with booster money.";
        _ = MyProgramBooks;
        string line = ProgramBuilding.Build(HumanTeam, k, ProgramBuildsNow);
        SaveNow();
        return line;
    }

    /// <summary>The summer's booster message (posted with graduation and enrollment).</summary>
    private void BoostersSummerNote()
    {
        if (!HasProgramBuilding) return;
        var b = MyProgramBooks;
        if (b.LastLines.Count == 0) return;
        Post($"Boosters: {ProgramBuilding.Mood(b.Boosters)}, {ProgramBuilding.Money(b.LastGift)} for the program",
            string.Join(" ", b.LastLines)
            + "\n\nSpend the booster fund on the Board page: a better arena and practice gym make the program more appealing to recruits, and the gym helps your players grow.", "Board");
    }
}
