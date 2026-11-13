using Hardwood.Core.Model;

namespace Hardwood.Core.Portraits;

/// <summary>Team crest drawn bottom-left on a card (the game's own TeamLogo design).</summary>
/// <remarks><see cref="LogoFile"/> is an image from the logo pack that replaces the drawn crest.</remarks>
public sealed record CrestSpec(string Abbreviation, string PrimaryColor, string SecondaryColor, int LogoStyle, string? LogoFile = null)
{
    public static CrestSpec Of(Team t, Func<Team, string?>? logoOf = null) => new(t.Abbreviation, t.PrimaryColor, t.SecondaryColor, t.LogoStyle, logoOf?.Invoke(t));
}

/// <summary>The club jersey a player wears on his card (decision #351): colours from the club, a number that stays his.</summary>
public sealed record JerseySpec(string PrimaryColor, string SecondaryColor, int Number)
{
    /// <summary>Common basketball numbers; each player keeps the same one (picked from his id).</summary>
    private static readonly int[] Numbers = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 20, 21, 22, 23, 24, 25, 30, 31, 32, 33, 34, 35, 40, 41, 42, 44, 45, 50, 55 };

    public static int NumberFor(int playerId) => Numbers[(int)((uint)(playerId * 2654435761u) % (uint)Numbers.Length)];

    public static JerseySpec Of(Team t, int playerId) => new(t.PrimaryColor, t.SecondaryColor, NumberFor(playerId));
}

/// <summary>Everything that decides how one player's portrait card looks.</summary>
/// <param name="Photo">A real photo (Danny's graphics pack) used instead of the generated face; it already shows the jersey.</param>
public sealed record PortraitCard(int PlayerId, int CurrentAbility, int PotentialAbility, CrestSpec? Crest, DraftRecord? Draft, string FaceFile,
    JerseySpec? Jersey = null, string? Photo = null)
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
        var kit = Jersey is { } j ? $"{j.PrimaryColor}/{j.SecondaryColor}/{j.Number}" : "-";
        var photo = Photo != null ? LogoStamp(Photo) : "-";
        return $"v{rendererVersion}|{body}|{badge}|{PotentialAbility}|{crest}|{draft}|{FaceFile}|{kit}|{photo}";
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
    /// <param name="jerseys">Dress rostered players in their club's jersey (#351).</param>
    /// <param name="photoOf">A real photo for a player, if any (#350).</param>
    public static List<PortraitCard> For(League league, IEnumerable<Player>? prospects, IEnumerable<Team>? worldClubs = null, Func<Team, string?>? logoOf = null,
        bool jerseys = false, Func<Player, string?>? photoOf = null)
    {
        var cards = new List<PortraitCard>();
        PortraitCard Card(Player p, CrestSpec? crest, Team? t) =>
            new(p.Id, p.CurrentAbility, p.PotentialAbility, crest, p.Draft, p.FaceFile!, jerseys && t != null ? JerseySpec.Of(t, p.Id) : null, photoOf?.Invoke(p));
        foreach (var t in league.Teams)
        {
            var crest = CrestSpec.Of(t, logoOf);
            foreach (var p in t.Roster)
                if (p.FaceFile != null) cards.Add(Card(p, crest, t));
        }
        foreach (var p in league.FreeAgents.Concat(prospects ?? Enumerable.Empty<Player>()))
            if (p.FaceFile != null) cards.Add(Card(p, null, null));
        // Clubs outside the NABL come last, so your own league's cards are drawn first.
        foreach (var t in worldClubs ?? Enumerable.Empty<Team>())
        {
            var crest = CrestSpec.Of(t, logoOf);
            foreach (var p in t.Roster)
                if (p.FaceFile != null) cards.Add(Card(p, crest, t));
        }
        return cards;
    }
}
