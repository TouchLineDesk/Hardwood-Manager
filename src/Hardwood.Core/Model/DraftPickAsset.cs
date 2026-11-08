namespace Hardwood.Core.Model;

/// <summary>
/// A future draft pick as a tradable asset. The pick belongs to its original team's slot in the order, but is
/// used by whoever owns it. Top-N protection: if the slot lands inside the protected range, the original team keeps
/// it and the owner receives the original team's same-round pick the following year instead (rolls once).
/// </summary>
public sealed class DraftPickAsset
{
    public int Id { get; init; }
    public int Year { get; init; }
    public int Round { get; init; }
    public int OriginalTeamId { get; init; }
    public int OwnerTeamId { get; set; }
    /// <summary>Protected if the slot is within the top N (1-based). 0 = unprotected.</summary>
    public int ProtectedTop { get; set; }

    public bool IsOwnPick => OriginalTeamId == OwnerTeamId;

    public string Describe(Func<int, string> abbr) =>
        $"{Year} {(Round == 1 ? "1st" : "2nd")}" + (IsOwnPick ? "" : $" ({abbr(OriginalTeamId)})") + (ProtectedTop > 0 ? $" top-{ProtectedTop} protected" : "");
}
