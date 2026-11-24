using Hardwood.Core.Model;

namespace Hardwood.Core.Season;

/// <summary>
/// What a front office can see of players it doesn't employ. Knowledge is a 0–1 figure from the team's head scout;
/// every attribute of an outsider is shown as a range whose width shrinks with knowledge, centred on a stable,
/// per-player misjudgement so the number doesn't jump around between screens.
/// </summary>
public static class Scouting
{
    public readonly record struct Range(int Low, int High)
    {
        public bool Exact => Low == High;
        public override string ToString() => Exact ? Low.ToString() : $"{Low}-{High}";
        public double Mid => (Low + High) / 2.0;
    }

    /// <summary>Knowledge a team has of a player: own players are known exactly.</summary>
    public static double Knowledge(Team viewer, Player p, bool prospect = false)
    {
        if (p.TeamId == viewer.Id) return 1.0;
        double k = StaffEffects.ScoutingKnowledge(viewer);
        return prospect ? k * 0.85 : k;
    }

    /// <summary>Deterministic pseudo-random in [-1, 1) for (player, attribute) so the fog is stable.</summary>
    internal static double Bias(int playerId, int salt)
    {
        unchecked
        {
            uint h = (uint)playerId * 2654435761u ^ (uint)(salt + 1) * 2246822519u;
            h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; h *= 3266489917u; h ^= h >> 16;
            return (h / 4294967296.0) * 2 - 1;
        }
    }

    public static Range Attribute(Player p, Attr a, double knowledge)
    {
        if (knowledge >= 0.999) return new Range(p.Attributes[a], p.Attributes[a]);
        int width = (int)Math.Round((1 - knowledge) * 10);           // 0.3 → 7, 0.95 → 0-1
        if (width <= 0) return new Range(p.Attributes[a], p.Attributes[a]);
        int centre = p.Attributes[a] + (int)Math.Round(Bias(p.Id, (int)a) * width / 2.0);
        int low = Math.Clamp(centre - width / 2, 1, 20), high = Math.Clamp(centre + (width + 1) / 2, 1, 20);
        if (low > p.Attributes[a]) low = p.Attributes[a];
        if (high < p.Attributes[a]) high = p.Attributes[a];
        return new Range(low, high);
    }

    // CA and PA are shown exactly for every player (Danny, Nov 10: ranges like "165-180" were confusing, and a PA range
    // could read below the CA range). Scouting still blurs individual attributes.
    public static Range Ability(Player p, double knowledge) => new(p.CurrentAbility, p.CurrentAbility);
    public static Range Potential(Player p, double knowledge) => new(p.PotentialAbility, p.PotentialAbility);

    /// <summary>
    /// CP3 (run 132, `claude/college-pipeline-design.md`): a college player's or recruit's potential as a scouting range
    /// ("150-168"), never an exact number, since nobody knows what a teenager will become. It narrows with what the viewer
    /// knows (<see cref="CollegeKnowledge"/>): about 20 points wide for a recruit you haven't scouted, 6 for your own
    /// senior. Decision #119's two complaints are kept out: the range never reads below his current ability, and it is
    /// stable from screen to screen (a fixed per-player misjudgement, like the attributes' fog). Pros keep exact numbers.
    /// </summary>
    public static Range CollegePotential(Player p, double knowledge)
    {
        int width = Math.Max(MinCollegeWidth, (int)Math.Round((1 - Math.Clamp(knowledge, 0, 1)) * 40));
        int pa = p.PotentialAbility;
        int centre = pa + (int)Math.Round(Bias(p.Id, 97) * width / 3.0);
        int low = centre - width / 2, high = low + width;
        if (low > pa) { low = pa; high = low + width; }
        if (high < pa) { high = pa; low = high - width; }
        low = Math.Min(Math.Max(low, p.CurrentAbility), 200 - MinCollegeWidth / 2);
        high = Math.Clamp(high, low + MinCollegeWidth / 2, 200);
        return new Range(low, high);
    }

    /// <summary>
    /// H119 (run 176): a college player's current ability as a scouting range ("118-130"), half as wide as his potential's
    /// for the same knowledge, stable per player, always containing the truth, and never reaching above the potential range.
    /// </summary>
    public static Range CollegeAbility(Player p, double knowledge)
    {
        int width = Math.Max(MinCollegeWidth, (int)Math.Round((1 - Math.Clamp(knowledge, 0, 1)) * 20));
        int ca = p.CurrentAbility;
        int centre = ca + (int)Math.Round(Bias(p.Id, 98) * width / 3.0);
        int low = centre - width / 2, high = low + width;
        if (low > ca) { low = ca; high = low + width; }
        if (high < ca) { high = ca; low = high - width; }
        int cap = CollegePotential(p, knowledge).High;
        if (high > cap) { high = Math.Max(cap, ca); low = Math.Min(low, high - width); }
        low = Math.Clamp(low, 1, ca); high = Math.Clamp(high, ca, 200);
        return new Range(low, high);
    }

    /// <summary>The narrowest a college potential range gets (your own senior, scouted every day for four years).</summary>
    public const int MinCollegeWidth = 4;

    /// <summary>
    /// What a coach knows of a college player's or recruit's potential: his own players from 0.55 as freshmen to 0.85 as
    /// seniors (time on your roster); anyone else from the head scout's knowledge, a little less than a pro's, plus 0.1 for
    /// a recruit your staff has scouted.
    /// </summary>
    public static double CollegeKnowledge(Team viewer, Player p, int yearsOnRoster, bool scouted = false)
    {
        if (p.TeamId == viewer.Id) return 0.55 + 0.1 * Math.Clamp(yearsOnRoster, 0, 3);
        return Math.Min(0.8, StaffEffects.ScoutingKnowledge(viewer) * 0.85 + (scouted ? 0.1 : 0));
    }

    public static string GroupAverage(Player p, AttrGroup g, double knowledge)
    {
        var attrs = AttrInfo.All.Where(a => AttrInfo.GroupOf(a) == g).ToList();
        if (knowledge >= 0.999) return p.Attributes.Average(g).ToString("0.0");
        double low = attrs.Average(a => Attribute(p, a, knowledge).Low), high = attrs.Average(a => Attribute(p, a, knowledge).High);
        return $"{low:0.0}-{high:0.0}";
    }
}
