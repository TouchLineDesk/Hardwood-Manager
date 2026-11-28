namespace Hardwood.Core.Season;

/// <summary>Which Kokoro voice speaks for whom on the HSN studio shows (run 239, V1 of <c>claude/studio-voice-plan.md</c>,
/// #1254). Fixed for good, so a voice always means the same person: the host and the four regulars have their own; a
/// guest in the chair keeps one of four by a stable hash of his name (FNV-1a, never <c>string.GetHashCode</c>). Only
/// American voices, and no <c>am_santa</c>. Nothing here is saved, and nothing reads the RNG.</summary>
public static class StudioVoiceCast
{
    public const string Host = "am_michael";
    public const string OldSchool = "am_fenrir";
    public const string NumbersDesk = "af_heart";
    public const string Showtime = "am_puck";
    public const string Haskell = "am_onyx";

    /// <summary>The guest chair's four voices, in order.</summary>
    public static readonly IReadOnlyList<string> GuestVoices = new[] { "am_eric", "am_liam", "am_echo", "am_adam" };

    /// <summary>Every voice the shows use (the voice pack's check makes a line in each).</summary>
    public static IReadOnlyList<string> All { get; } = new[] { Host, OldSchool, NumbersDesk, Showtime, Haskell }.Concat(GuestVoices).ToArray();

    /// <summary>The voice for <paramref name="speaker"/>.</summary>
    public static string For(StudioAnalyst speaker) => speaker.Key switch
    {
        "host" => Host,
        "big" => OldSchool,
        "numbers" => NumbersDesk,
        "guard" => Showtime,
        "coach" => Haskell,
        _ => GuestVoice(speaker.Name),
    };

    /// <summary>One of the four guest voices, from the name alone (the same every month he sits in, every process).</summary>
    public static string GuestVoice(string name)
    {
        var h = 2166136261u;
        foreach (var c in name.Trim().ToLowerInvariant()) { h ^= c; h *= 16777619u; }
        h ^= h >> 15; h *= 0x2c1b3c6du; h ^= h >> 12;
        return GuestVoices[(int)(h % (uint)GuestVoices.Count)];
    }
}
