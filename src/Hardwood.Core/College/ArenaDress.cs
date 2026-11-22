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

    // M4 (run 151, match-engine-2d-design.md §D): the rest of the arena. All of it reads the game; nothing draws a number.
    /// <summary>How full the stands are, 0.35 (a thin house) to 1 (a sell-out).</summary>
    public double Crowd { get; init; } = 0.8;
    /// <summary>The away side's colour for its end of the stands at a big game (an event, the playoffs), or null.</summary>
    public string? AwayCrowdColour { get; init; }
    /// <summary>The panels of the ad and ribbon boards, in turn round the floor (the game's own marks only, no real logos).</summary>
    /// <remarks>Kept as one string so two dresses for the same game compare equal (a record compares lists by reference).</remarks>
    public string BoardLine { get; init; } = "";
    public IReadOnlyList<string> Boards => BoardLine.Length == 0 ? Array.Empty<string>() : BoardLine.Split(" | ");
    private static string Join(params string[] panels) => string.Join(" | ", panels.Where(p => p.Length > 0));
    /// <summary>The scorer's table's front, e.g. "HSN COLLEGE HOOPS".</summary>
    public string TableText { get; init; } = "HSN LIVE";

    public const string PlayoffTrim = "#F2F4F7", FinalsPaint = "#101318", FinalsTrim = "#D9B44A";

    /// <summary>A pro game: the home club's floor, with the playoffs' and the Finals' dressing, or a continental cup's
    /// event floor. <paramref name="league"/> is the competition's short name ("NABL", "ESP").</summary>
    public static ArenaDress ForPro(Team home, Team away, string league, Hardwood.Core.Season.Series? series = null, string? cup = null)
    {
        double fans = Math.Clamp(home.Money.FanBase, 0, 100);
        double crowd = Math.Round(Math.Clamp(0.5 + fans / 200, 0.5, 1.0), 2);
        string city = home.City.ToUpperInvariant(), name = home.Name.ToUpperInvariant();
        if (cup != null)
        {
            // A continental cup: its own floor, both clubs' fans.
            string c = cup.ToUpperInvariant();
            return new ArenaDress(EventPaint, EventTrim, EventTrim, c, c, $"{city} · {home.Name.ToUpperInvariant()}", home, "", false)
            {
                CrowdColour = home.PrimaryColor, AwayCrowdColour = away.PrimaryColor, Crowd = Math.Max(crowd, 0.85),
                BoardLine = Join(c, $"{home.Abbreviation} v {away.Abbreviation}", "HSN LIVE"), TableText = $"{c} · HSN",
            };
        }
        if (series != null)
        {
            bool finals = series.Round == "Finals";
            string round = series.Round.ToUpperInvariant(), number = series.WinsNeeded == 1 ? "" : $"GAME {series.GameNumber}";
            // "NABL FINALS · GAME 4"; "NABL PLAYOFFS · EAST SEMIFINALS · GAME 3".
            string game = finals ? (number.Length > 0 ? number : "THE FINALS") : number.Length > 0 ? $"{round} · {number}" : round;
            return new ArenaDress(home.PrimaryColor, finals ? FinalsTrim : home.SecondaryColor, "#F4F1EA", city, name,
                $"{league} {(finals ? "FINALS" : "PLAYOFFS")} · {game}", home, "", false)
            {
                Crowd = 1.0, AwayCrowdColour = away.PrimaryColor,
                BoardLine = Join(finals ? $"{league} FINALS" : $"{league} PLAYOFFS", game, home.Abbreviation, "HSN LIVE"),
                TableText = finals ? $"{league} FINALS" : $"{league} PLAYOFFS",
            };
        }
        return new ArenaDress(home.PrimaryColor, home.SecondaryColor, "#F4F1EA", city, name, "", home, "", false)
        {
            Crowd = crowd, BoardLine = Join($"{city} {name}", league, "HSN LIVE"), TableText = $"{league} · HSN LIVE",
        };
    }

    /// <summary>A college campus game's crowd: a long tradition and a big house fill it; a ranked visitor or a rival adds.</summary>
    public static double CampusCrowd(double tradition, int capacity, bool bigGame)
    {
        double t = Math.Clamp((tradition - 20) / 70, 0, 1), c = Math.Clamp((capacity - 4000) / 14000.0, 0, 1);
        return Math.Round(Math.Clamp(0.45 + 0.35 * t + 0.1 * c + (bigGame ? 0.2 : 0), 0.35, 1.0), 2);
    }

    public const string MadnessPaint = "#123A7A", MadnessTrim = "#E8EEF8";
    public const string FinalFourPaint = "#15171C", FinalFourTrim = "#D9B44A";
    public const string EventPaint = "#22324A", EventTrim = "#C9D1D9";

    public static ArenaDress For(Team home, CollegeGame? game) => For(home, game, null, null);

    /// <summary>A college game's floor. <paramref name="away"/> dresses the away end at an event; <paramref name="world"/>
    /// fills a campus house by the home program's tradition and arena (with neither, the crowd is a typical 0.8).</summary>
    public static ArenaDress For(Team home, CollegeGame? game, Team? away, CollegeWorld? world)
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
                        $"{round} · {city.ToUpperInvariant()} {game.Date.Year}", null, "FINAL FOUR", true)
                    {
                        CrowdColour = "#B8922F", Crowd = 1.0, AwayCrowdColour = away?.PrimaryColor,
                        BoardLine = Join("FINAL FOUR", $"{city.ToUpperInvariant()} {game.Date.Year}", round, "HSN COLLEGE HOOPS"), TableText = $"FINAL FOUR · {city.ToUpperInvariant()}",
                    };
                }
                string mround = CollegeWorld.RoundName(game.Round).ToUpperInvariant();
                return new ArenaDress(MadnessPaint, MadnessTrim, MadnessTrim, "MARCH MADNESS", "MARCH MADNESS",
                    mround, null, "MARCH MADNESS", true)
                {
                    CrowdColour = "#2F5DA8", Crowd = Math.Min(1.0, 0.78 + 0.04 * game.Round), AwayCrowdColour = away?.PrimaryColor,
                    BoardLine = Join("MARCH MADNESS", mround, "HSN COLLEGE HOOPS"), TableText = "MARCH MADNESS",
                };
            }
            string name = ev.ToUpperInvariant();
            bool confT = game.Stage == "Conference tournament";
            return new ArenaDress(EventPaint, EventTrim, EventTrim, name, name, game.Date.Year.ToString(), null, name, true)
            {
                CrowdColour = "#3F5A80", Crowd = confT ? 0.85 : 0.7, AwayCrowdColour = away?.PrimaryColor,
                BoardLine = Join(name, game.Date.Year.ToString(), "HSN COLLEGE HOOPS"), TableText = name,
            };
        }
        // A campus floor: the program's colours, its school name on one baseline and its nickname on the other.
        double crowd = 0.8;
        if (world != null)
        {
            double tradition = world.Tradition.TryGetValue(home.Id, out var tr) ? tr : world.Prestige.GetValueOrDefault(home.Id, 40);
            bool big = away != null && (world.RivalryOf(home.Id, away.Id) != null || world.Rank(away.Id) is <= 25);
            crowd = CampusCrowd(tradition, home.ArenaCapacity, big);
        }
        string school = home.City.ToUpperInvariant();
        return new ArenaDress(home.PrimaryColor, home.SecondaryColor, "#F4F1EA", school, home.Name.ToUpperInvariant(),
            "", home, "", false)
        {
            Crowd = crowd, BoardLine = Join($"{school} {home.Name.ToUpperInvariant()}", "HSN COLLEGE HOOPS", home.Abbreviation),
            TableText = $"{school} · HSN COLLEGE HOOPS",
        };
    }
}
