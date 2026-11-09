namespace Hardwood.Core.Model;

/// <summary>2K-style card tiers mapped onto Current Ability bands. Colours are gradient stops as #RRGGBB.</summary>
public sealed record Tier(string Name, int MinCa, string Dark, string Light, string Accent)
{
    public static readonly Tier[] All =
    {
        new("Bronze",       0,   "#4A2C12", "#B87333", "#E8B98A"),
        new("Silver",       85,  "#4B5563", "#B8C0CC", "#F1F5F9"),
        new("Gold",         100, "#7A5A00", "#E6B422", "#FFE999"),
        new("Emerald",      115, "#064E3B", "#10B981", "#A7F3D0"),
        new("Sapphire",     125, "#1E3A8A", "#3B82F6", "#BFDBFE"),
        new("Ruby",         135, "#7F1D1D", "#DC2626", "#FECACA"),
        new("Amethyst",     145, "#4C1D95", "#8B5CF6", "#DDD6FE"),
        new("Diamond",      155, "#0C4A6E", "#38BDF8", "#E0F2FE"),
        new("Pink Diamond", 165, "#831843", "#EC4899", "#FBCFE8"),
        new("Galaxy Opal",  180, "#1E1B4B", "#A78BFA", "#F0ABFC"),
    };

    public static Tier ForCa(int ca)
    {
        var t = All[0];
        foreach (var x in All) if (ca >= x.MinCa) t = x;
        return t;
    }

    public Tier? Next => Array.IndexOf(All, this) is var i && i < All.Length - 1 ? All[i + 1] : null;
}
