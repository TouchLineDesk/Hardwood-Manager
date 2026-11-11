namespace Hardwood.Core.College;

/// <summary>Where one senior stands with the pro clubs: your effort and offer, and whom he has committed to.</summary>
public sealed class RecruitState
{
    /// <summary>The club whose effort this is (your club when you started; reset if you change jobs).</summary>
    public int TeamId { get; set; }
    public bool Shortlisted { get; set; }
    /// <summary>Goodwill from calls and visits; it fades a little every week.</summary>
    public double Effort { get; set; }
    public int Calls { get; set; }
    public int Visits { get; set; }
    public long OfferWage { get; set; }
    public int OfferYears { get; set; }
    public int? CommittedTeamId { get; set; }
    public DateOnly? CommittedOn { get; set; }
}

/// <summary>
/// Senior recruiting (decision #157): with no draft (#80), a college senior can agree in advance which pro club he'll
/// sign for when he graduates. You court him with calls and visits from a monthly budget of recruiting points, then
/// offer terms; rival NABL clubs land commitments of their own as the season goes on.
/// </summary>
public sealed class RecruitingBoard
{
    public Dictionary<int, RecruitState> Prospects { get; set; } = new();
    public int Points { get; set; }
    /// <summary>The month (year × 12 + month) the points were last topped up.</summary>
    public int PointsMonth { get; set; }

    public const int CallCost = 1, VisitCost = 3, MaxVisits = 2, PointsCap = 20;

    public RecruitState For(int playerId) => Prospects.TryGetValue(playerId, out var s) ? s : Prospects[playerId] = new RecruitState();
    public RecruitState? Find(int playerId) => Prospects.GetValueOrDefault(playerId);
}
