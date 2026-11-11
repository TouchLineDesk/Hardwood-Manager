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

    public static string GroupAverage(Player p, AttrGroup g, double knowledge)
    {
        var attrs = AttrInfo.All.Where(a => AttrInfo.GroupOf(a) == g).ToList();
        if (knowledge >= 0.999) return p.Attributes.Average(g).ToString("0.0");
        double low = attrs.Average(a => Attribute(p, a, knowledge).Low), high = attrs.Average(a => Attribute(p, a, knowledge).High);
        return $"{low:0.0}-{high:0.0}";
    }
}
