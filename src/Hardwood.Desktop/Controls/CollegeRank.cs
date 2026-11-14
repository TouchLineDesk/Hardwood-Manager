using Hardwood.Core.Model;

namespace Hardwood.Desktop.Controls;

/// <summary>
/// The college Top 25 rank that travels with a team (Danny, #528): team logos of 36 px and up carry it in a corner
/// badge and team links put "#5" before the name. The shell points <see cref="Of"/> at the session's poll and raises
/// <see cref="Changed"/> after every day of games.
/// </summary>
public static class CollegeRank
{
    public static Func<Team, int?>? Of { get; set; }
    public static event Action? Changed;

    public static int? For(Team? team) => team == null ? null : Of?.Invoke(team);

    /// <summary>"#5 Duke" for a ranked program, else the name.</summary>
    public static string Prefix(Team? team, string name) => For(team) is { } r ? $"#{r} {name}" : name;

    public static void RaiseChanged() => Changed?.Invoke();
}
