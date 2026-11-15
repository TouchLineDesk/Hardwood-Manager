using CommunityToolkit.Mvvm.Input;
using Hardwood.Core.Model;

namespace Hardwood.Desktop.ViewModels;

/// <summary>One name on a leader card (run 64, review #13 rank 3): rank, the player, his club and the number.</summary>
public sealed class LeaderEntry
{
    public int Rank { get; init; }
    public Player? Player { get; init; }
    public string Name { get; init; } = "";
    public Team? Team { get; init; }
    public string Club => Team?.Abbreviation ?? "";
    public string Value { get; init; } = "";
    /// <summary>One of yours: shown in bold.</summary>
    public bool Ours { get; init; }
    public Avalonia.Media.FontWeight Weight => Ours ? Avalonia.Media.FontWeight.Bold : Avalonia.Media.FontWeight.Normal;
}

/// <summary>A stat's leader card: the leader big (face, club colour strip, the number), then 2nd to 5th.
/// Flat properties for the leader so an empty card never binds through a null.</summary>
public sealed class LeaderCard
{
    public string Category { get; init; } = "";
    public string Title { get; init; } = "";
    public string Unit { get; init; } = "";
    public IReadOnlyList<LeaderEntry> Entries { get; init; } = Array.Empty<LeaderEntry>();
    public IRelayCommand? ShowAll { get; init; }

    public bool HasAny => Entries.Count > 0;
    public bool IsEmpty => !HasAny;
    private LeaderEntry? Top => Entries.Count > 0 ? Entries[0] : null;
    public Player? TopPlayer => Top?.Player;
    public string TopName => Top?.Name ?? "";
    public Team? TopTeam => Top?.Team;
    /// <summary>The nickname ("Lakers"): short enough to sit beside the number; the full name is on the club page.</summary>
    public string TopClub => Top?.Team?.Name ?? "";
    public string TopValue => Top?.Value ?? "";
    public IReadOnlyList<LeaderEntry> Rest => Entries.Skip(1).ToList();
    public string EmptyText { get; init; } = "Fills in after the first games.";
}

/// <summary>The stats that get a card, in reading order, with the unit under the big number.</summary>
public static class LeaderCards
{
    public static readonly (string Category, string Title, string Unit)[] Stats =
    {
        ("Points", "Points", "points a game"),
        ("Rebounds", "Rebounds", "rebounds a game"),
        ("Assists", "Assists", "assists a game"),
        ("Steals", "Steals", "steals a game"),
        ("Blocks", "Blocks", "blocks a game"),
        ("FG%", "Field goal %", "of shots made"),
        ("3P%", "Three-point %", "of threes made"),
        ("Minutes", "Minutes", "minutes a game"),
    };

    public const int PerCard = 5;

    /// <summary>A shooting value for a card: 0.523 → "52.3%".</summary>
    public static string Pct(double v) => $"{v * 100:0.0}%";
    public static string PerGame(double v) => v.ToString("0.0");
    public static bool IsPct(string category) => category is "FG%" or "3P%";
}
