namespace Hardwood.Core.Model;

/// <summary>
/// Appearance group used to pick a portrait face. The values match the folder names of the
/// FMNEWGAN face pack (see <see cref="EthnicityInfo.FolderName"/>), so a player's face can be
/// chosen from the right folder.
/// </summary>
public enum Ethnicity
{
    African,
    Asian,
    Caucasian,
    CentralEuropean,
    EECA,
    Italmed,
    MENA,
    MESA,
    SAMed,
    Scandinavian,
    Seasian,
    SouthAmerican,
    /// <summary>Spanish / Iberian Mediterranean (pack folder "SpanMed").</summary>
    SpanMed,
    /// <summary>Former Yugoslavia and Greece (pack folder "YugoGreek").</summary>
    YugoGreek,
}

public static class EthnicityInfo
{
    /// <summary>Face pack folder for an ethnicity, e.g. CentralEuropean → "Central European".</summary>
    public static string FolderName(Ethnicity e) => e switch
    {
        Ethnicity.CentralEuropean => "Central European",
        Ethnicity.SouthAmerican => "South American",
        _ => e.ToString(),
    };

    public static Ethnicity? FromFolderName(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var key = s.Replace(" ", "");
        return Enum.TryParse<Ethnicity>(key, ignoreCase: true, out var e) ? e : null;
    }
}
