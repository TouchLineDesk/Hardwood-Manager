using Avalonia.Media;

namespace Hardwood.Desktop.ViewModels;

/// <summary>Run 68 (review #13 rank 7, Danny's #464): the college recruiting boards as star cards. The high-school
/// classes and a college coach's own board share these pieces: the stars (gold on, grey off), the card's edge by
/// star rating, the status chip and the interest bar's colour.</summary>
public static class CollegeCards
{
    /// <summary>How many prospects show as cards; the full table lists everyone.</summary>
    public const int CardCount = 30;

    public static readonly IBrush Gold = new SolidColorBrush(Color.Parse("#FFC83D"));
    private static readonly IBrush Silver = new SolidColorBrush(Color.Parse("#9DB4D0"));
    private static readonly IBrush Bronze = new SolidColorBrush(Color.Parse("#C0875A"));
    private static readonly IBrush Grey = new SolidColorBrush(Color.Parse("#5A6573"));
    private static readonly IBrush Blue = new SolidColorBrush(Color.Parse("#2F6FB3"));
    private static readonly IBrush Open = new SolidColorBrush(Color.Parse("#3A4452"));
    private static readonly IBrush Amber = new SolidColorBrush(Color.Parse("#B7832A"));

    public static string On(int stars) => new('★', Math.Clamp(stars, 0, 5));
    public static string Off(int stars) => new('★', 5 - Math.Clamp(stars, 0, 5));

    /// <summary>The card's left edge: gold for a five-star, silver for a four-star, bronze for a three-star.</summary>
    public static IBrush StarEdge(int stars) => stars switch { >= 5 => Gold, 4 => Silver, 3 => Bronze, _ => Grey };

    public static string StarTip(int stars, int rank) =>
        (stars > 0 ? $"A {stars}-star prospect" : "Unrated") + $", No. {rank} in his class by the recruiting services (an opinion, not a promise)";

    /// <summary>A trail status as a coloured chip: yours green, elsewhere grey, offered amber, on your board blue.</summary>
    public static IBrush StatusBrush(string status) => status switch
    {
        "Committed to you" => StandingRow.PlayoffBrush,
        "Offered" => Amber,
        "On your board" => Blue,
        _ when status.StartsWith("Committed", StringComparison.Ordinal) => Grey,
        _ => Open,
    };

    /// <summary>Interest in you: 70+ green, 40–69 amber, under 40 red.</summary>
    public static IBrush InterestBrush(double interest) =>
        interest >= 70 ? StandingRow.PlayoffBrush : interest >= 40 ? Amber : StandingRow.RelegationBrush;
}
