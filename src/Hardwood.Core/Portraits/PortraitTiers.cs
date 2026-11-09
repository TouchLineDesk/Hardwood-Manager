namespace Hardwood.Core.Portraits;

/// <summary>One colour band of the portrait card: name, CA/PA range and the four palette colours.</summary>
public sealed record PortraitTier(string Name, int Lo, int Hi, string Dark, string Base, string Light, string Ink);

/// <summary>
/// Portrait card bands (decision #31). FROZEN after the first real build: never rescale, so
/// cards stay comparable season to season. The card body shows the current-ability tier; the
/// POT badge shows the potential tier, or Diamond at PA 180+.
/// </summary>
public static class PortraitTiers
{
    public static readonly IReadOnlyList<PortraitTier> Bands = new[]
    {
        new PortraitTier("Pearl", 1, 84, "#8C96A3", "#D9DFE6", "#FFFFFF", "#1B2430"),
        new PortraitTier("Bronze", 85, 99, "#240F03", "#5C2C0E", "#A35A28", "#FFE9D6"),
        new PortraitTier("Silver", 100, 114, "#2C3746", "#8C9DB1", "#F4F8FC", "#0F1724"),
        new PortraitTier("Gold", 115, 134, "#8A5E00", "#F2BC00", "#FFF5A8", "#2A1C00"),
        new PortraitTier("Amethyst", 135, 154, "#2A0F52", "#6D34B3", "#C9A2FF", "#FFFFFF"),
        new PortraitTier("Ruby", 155, 200, "#420606", "#A5161A", "#FF7A6B", "#FFFFFF"),
    };

    public static readonly PortraitTier Diamond = new("Diamond", 180, 200, "#20364F", "#A9DDF2", "#FFFFFF", "#0A1E30");

    /// <summary>Band for a rating; values outside every band fall into the top one (as the kit does).</summary>
    public static PortraitTier For(int value)
    {
        foreach (var t in Bands)
            if (value >= t.Lo && value <= t.Hi) return t;
        return Bands[^1];
    }

    public static PortraitTier Body(int currentAbility) => For(currentAbility);

    public static PortraitTier Badge(int potentialAbility) => potentialAbility >= Diamond.Lo ? Diamond : For(potentialAbility);
}
