using Hardwood.Core.Model;

namespace Hardwood.Core.College;

/// <summary>
/// How the floor of a college game is dressed (B2a, run 140; broadcast-presentation-design.md): the home program's own
/// floor for a campus game, and an event floor for a neutral site (an invitational or showcase, a conference tournament,
/// March Madness and the Final Four). Pure presentation: it reads the game and never draws a random number.
/// </summary>
/// <param name="Paint">The keys, the centre circle and the apron (hex).</param>
/// <param name="Trim">The thin line inside the apron and the lettering's outline (hex).</param>
/// <param name="Lettering">The baseline lettering's colour (hex).</param>
/// <param name="LeftBaseline">Painted along the left baseline (the away side's basket end).</param>
/// <param name="RightBaseline">Painted along the right baseline.</param>
/// <param name="Sideline">Painted along the far sideline's apron, or empty.</param>
/// <param name="CrestTeam">The program whose crest sits at centre court, or null for an event floor (the HSN mark and
/// <see cref="CentreText"/>).</param>
public sealed record ArenaDress(string Paint, string Trim, string Lettering, string LeftBaseline, string RightBaseline,
    string Sideline, Team? CrestTeam, string CentreText, bool Neutral)
{
    /// <summary>The ribbon boards and the crowd wear the home side's colours on campus, and the event's elsewhere.</summary>
    public string CrowdColour { get; init; } = Paint;

    public const string MadnessPaint = "#123A7A", MadnessTrim = "#E8EEF8";
    public const string FinalFourPaint = "#15171C", FinalFourTrim = "#D9B44A";
    public const string EventPaint = "#22324A", EventTrim = "#C9D1D9";

    public static ArenaDress For(Team home, CollegeGame? game)
    {
        if (game?.Event is { } ev)
        {
            if (ev == CollegeWorld.Madness)
            {
                if (game.Round >= 5)
                {
                    string city = MadnessBracket.FinalFourHost(game.Date.Year).Split(',')[0];
                    string round = game.Round >= 6 ? "NATIONAL CHAMPIONSHIP" : "NATIONAL SEMIFINAL";
                    return new ArenaDress(FinalFourPaint, FinalFourTrim, FinalFourTrim, "FINAL FOUR", "FINAL FOUR",
                        $"{round} · {city.ToUpperInvariant()} {game.Date.Year}", null, "FINAL FOUR", true) { CrowdColour = "#B8922F" };
                }
                return new ArenaDress(MadnessPaint, MadnessTrim, MadnessTrim, "MARCH MADNESS", "MARCH MADNESS",
                    CollegeWorld.RoundName(game.Round).ToUpperInvariant(), null, "MARCH MADNESS", true) { CrowdColour = "#2F5DA8" };
            }
            string name = ev.ToUpperInvariant();
            return new ArenaDress(EventPaint, EventTrim, EventTrim, name, name, game.Date.Year.ToString(), null, name, true) { CrowdColour = "#3F5A80" };
        }
        // A campus floor: the program's colours, its school name on one baseline and its nickname on the other.
        return new ArenaDress(home.PrimaryColor, home.SecondaryColor, "#F4F1EA", home.City.ToUpperInvariant(), home.Name.ToUpperInvariant(),
            "", home, "", false);
    }
}
