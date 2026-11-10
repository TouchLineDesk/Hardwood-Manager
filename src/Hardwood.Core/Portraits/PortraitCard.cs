using Hardwood.Core.Model;

namespace Hardwood.Core.Portraits;

/// <summary>Team crest drawn bottom-left on a card (the game's own TeamLogo design).</summary>
/// <remarks><see cref="LogoFile"/> is an image from the logo pack that replaces the drawn crest.</remarks>
public sealed record CrestSpec(string Abbreviation, string PrimaryColor, string SecondaryColor, int LogoStyle, string? LogoFile = null)
{
    public static CrestSpec Of(Team t, Func<Team, string?>? logoOf = null) => new(t.Abbreviation, t.PrimaryColor, t.SecondaryColor, t.LogoStyle, logoOf?.Invoke(t));
}

/// <summary>Everything that decides how one player's portrait card looks.</summary>
public sealed record PortraitCard(int PlayerId, int CurrentAbility, int PotentialAbility, CrestSpec? Crest, DraftRecord? Draft, string FaceFile)
{
    /// <summary>
    /// Changes whenever the rendered image would change: body/badge tier, the POT number, crest,
    /// draft stamp, face, or the renderer's design version. A card is re-rendered when its key differs.
    /// </summary>
    public string Key(int rendererVersion)
    {
        var body = PortraitTiers.Body(CurrentAbility).Name;
        var badge = PortraitTiers.Badge(PotentialAbility).Name;
        var crest = Crest is { } c ? $"{c.Abbreviation}/{c.PrimaryColor}/{c.SecondaryColor}/{c.LogoStyle}{LogoStamp(c.LogoFile)}" : "-";
        var draft = Draft is { } d ? $"{d.Year}/{d.Round}/{d.Pick}" : "-";
        return $"v{rendererVersion}|{body}|{badge}|{PotentialAbility}|{crest}|{draft}|{FaceFile}";
    }

    /// <summary>A logo file's name and last write time, so replacing a logo redraws the cards.</summary>
    private static string LogoStamp(string? file)
    {
        if (file == null) return "";
        try { return $"/{Path.GetFileName(file)}@{File.GetLastWriteTimeUtc(file).Ticks}"; }
        catch (Exception) { return "/" + Path.GetFileName(file); }
    }

    /// <summary>
    /// Cards for every player who shows a portrait: rostered players (with their team's crest),
    /// free agents and draft prospects (no crest), and players of clubs outside the NABL (with their club's crest). Retired players keep their last card.
    /// Players without a face are skipped (they keep the procedural avatar).
    /// </summary>
    public static List<PortraitCard> For(League league, IEnumerable<Player>? prospects, IEnumerable<Team>? worldClubs = null, Func<Team, string?>? logoOf = null)
    {
        var cards = new List<PortraitCard>();
        foreach (var t in league.Teams)
        {
            var crest = CrestSpec.Of(t, logoOf);
            foreach (var p in t.Roster)
                if (p.FaceFile != null) cards.Add(new(p.Id, p.CurrentAbility, p.PotentialAbility, crest, p.Draft, p.FaceFile));
        }
        foreach (var p in league.FreeAgents.Concat(prospects ?? Enumerable.Empty<Player>()))
            if (p.FaceFile != null) cards.Add(new(p.Id, p.CurrentAbility, p.PotentialAbility, null, p.Draft, p.FaceFile));
        // Clubs outside the NABL come last, so your own league's cards are drawn first.
        foreach (var t in worldClubs ?? Enumerable.Empty<Team>())
        {
            var crest = CrestSpec.Of(t, logoOf);
            foreach (var p in t.Roster)
                if (p.FaceFile != null) cards.Add(new(p.Id, p.CurrentAbility, p.PotentialAbility, crest, p.Draft, p.FaceFile));
        }
        return cards;
    }
}
