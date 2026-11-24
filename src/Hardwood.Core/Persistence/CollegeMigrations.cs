using System.Text.Json.Nodes;

namespace Hardwood.Core.Persistence;

/// <summary>
/// K30 (run 178, ADR-1 option B in <c>claude/tech-debt-2026-11-23.md</c>): the college world is saved as one JSON record
/// (<c>world_meta['college']</c>), and that record now carries its own format number (<c>world_meta['college_format']</c>).
/// Loading runs every step from the save's format up to <see cref="Current"/>, in order, on the JSON before it is read.
/// <para>The rule (also in <c>tests/Hardwood.Tests/SaveFixtures/README.md</c>): a change to what the college record means
/// (a renamed, retyped or moved field) bumps <see cref="Current"/>, adds a step here that rewrites the old shape, and adds a
/// college fixture made by the previous build. A new field with a safe default (null, zero, empty) needs no step.</para>
/// </summary>
public static class CollegeMigrations
{
    /// <summary>The college record's format written by this build.</summary>
    public const int Current = 1;

    /// <summary>One upgrade: from format <see cref="From"/> to <c>From + 1</c>. <see cref="Apply"/> is null when the step only
    /// marks a format (the JSON is read as it is).</summary>
    public sealed record Step(int From, string What, Action<JsonObject>? Apply);

    /// <summary>The ordered steps. Step k takes format k to k + 1.</summary>
    public static readonly IReadOnlyList<Step> Steps = new[]
    {
        new Step(0, "v0.165.0 and older: no college_format row. The record is read as it is (new fields default to null or zero).", null),
    };

    /// <summary>The message shown when a save's college world is newer than this build.</summary>
    public static string NewerMessage(int format) =>
        $"This career's college world was saved by a newer version of Hardwood Manager (college format {format}; this version reads up to {Current}). "
        + "Update the game to keep playing it. The save was not changed.";

    /// <summary>The college JSON upgraded from <paramref name="format"/> to <see cref="Current"/>. Throws, without touching
    /// anything, when the save is newer than this build.</summary>
    public static string Upgrade(string json, int format)
    {
        if (format > Current) throw new InvalidOperationException(NewerMessage(format));
        var todo = Steps.Where(s => s.From >= format && s.From < Current).OrderBy(s => s.From).ToList();
        if (todo.All(s => s.Apply == null)) return json;
        var node = JsonNode.Parse(json)!.AsObject();
        foreach (var s in todo) s.Apply?.Invoke(node);
        return node.ToJsonString();
    }
}
