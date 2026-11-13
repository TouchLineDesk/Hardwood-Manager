using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hardwood.Core.College;
using Hardwood.Core.Model;
using Hardwood.Desktop.Controls;

namespace Hardwood.Desktop.ViewModels;

/// <summary>A high-school prospect on a college coach's recruiting board (C3, #173).</summary>
public sealed class TrailRow
{
    public required Player Player { get; init; }
    public int Rank { get; init; }
    public string Stars { get; init; } = "";
    public string Name => Player.Name;
    public string Pos => Player.PrimaryPosition.ToString();
    public string State { get; init; } = "";
    public double Interest { get; init; }
    public string InterestText { get; init; } = "";
    public string Leaning { get; init; } = "";
    public string Status { get; init; } = "";
}

/// <summary>The recruiting trail on the College page, for a college coach (slice C3, decisions #173-#178).</summary>
public sealed partial class CollegeViewModel
{
    /// <summary>You coach a college program: the Recruiting tab is your high-school trail, not the pros' senior board.</summary>
    public bool IsCollegeCoach => Session.TrailOpen;
    public bool IsProRecruiting => !Session.InCollege;
    public List<string> TrailViews { get; } = new() { "Best fits", "My board", "Committed to you", "All uncommitted", "5-star", "4-star" };
    [ObservableProperty] private string _trailView = "Best fits";
    [ObservableProperty] private string _trailClass = "";
    public ObservableCollection<TrailRow> TrailRows { get; } = new();
    [ObservableProperty] private TrailRow? _selectedTrail;
    [ObservableProperty] private List<Chip> _trailChips = new();
    [ObservableProperty] private List<Chip> _prospectChips = new();
    [ObservableProperty] private string _trailNote = "";
    [ObservableProperty] private string _trailHeader = "";
    [ObservableProperty] private string _trailConsidering = "";
    [ObservableProperty] private string _trailReply = "";
    [ObservableProperty] private bool _assistantRecruits;
    public bool HasTrail => SelectedTrail != null;
    private bool _trailLoading;

    partial void OnTrailViewChanged(string value) => FillTrail();
    partial void OnTrailClassChanged(string value) => FillTrail();
    partial void OnSelectedTrailChanged(TrailRow? value) { OnPropertyChanged(nameof(HasTrail)); TrailReply = ""; FillTrailPanel(); }
    partial void OnAssistantRecruitsChanged(bool value) { if (_trailLoading) return; Session.Delegated.Recruiting = value; Session.SaveNow(); FillTrail(); }

    private int TrailYear => int.TryParse(TrailClass.Replace("Class of ", "").Split(' ')[0], out var y) ? y : 0;

    private void FillTrail()
    {
        var keep = SelectedTrail?.Player;
        TrailRows.Clear();
        OnPropertyChanged(nameof(IsCollegeCoach)); OnPropertyChanged(nameof(IsProRecruiting));
        if (!Session.TrailOpen || Session.TrailWorld is not { } c) return;
        _trailLoading = true; AssistantRecruits = Session.Delegated.Recruiting; _trailLoading = false;
        if (!Classes.Contains(TrailClass))
        {
            // Start on the first class with a scholarship to give (the seniors are often spoken for by the time you look).
            var first = Classes.FirstOrDefault(x => int.TryParse(x.Replace("Class of ", "").Split(' ')[0], out var y) && c.OpenScholarships(Session.HumanTeam, y) > 0);
            if (Classes.Count > 0) TrailClass = first ?? Classes[0];
            return;
        }
        var me = Session.HumanTeam;
        int year = TrailYear;
        var board = c.BoardFor(year);
        IEnumerable<(Player p, Recruit r)> rows = TrailView switch
        {
            "Best fits" => c.Fits(me, year, board, 60),
            _ => c.ClassOf(year).Select(p => (p, r: c.Recruits[p.Id])).Where(x => TrailView switch
            {
                "My board" => x.r.CourtedBy == me.Id && (x.r.OnBoard || x.r.Offered) || x.r.CommittedTo == me.Id,
                "Committed to you" => x.r.CommittedTo == me.Id,
                "5-star" => x.r.Stars == 5,
                "4-star" => x.r.Stars == 4,
                _ => x.r.CommittedTo == null,
            }).OrderBy(x => x.r.Rank).Take(150),
        };
        foreach (var (p, r) in rows)
        {
            double interest = CollegeWorld.InterestIn(r, me);
            var lean = c.LeaningTo(p, r, board);
            TrailRows.Add(new TrailRow
            {
                Player = p, Rank = r.Rank, Stars = r.Stars > 0 ? new string('★', r.Stars) : "–", State = r.State,
                Interest = interest, InterestText = $"{interest:0}",
                Leaning = lean == null ? "-" : lean == me ? "You" : lean.City,
                Status = r.CommittedTo is { } to ? (to == me.Id ? "Committed to you" : $"Committed: {c.Program(to)!.City}")
                       : r.CourtedBy == me.Id && r.Offered ? "Offered" : r.CourtedBy == me.Id && r.OnBoard ? "On your board" : "Open",
            });
        }
        int senior = c.SeniorClass;
        var ranking = c.ClassRanking(year);
        int ourRank = ranking.FindIndex(x => x.Program == me);
        int open = c.OpenScholarships(me, year), hours = Session.TrailHoursLeft;
        TrailChips = new()
        {
            new("Coach hours", $"{hours} of {CollegeWorld.WeeklyHours}", "this week · refill Mondays", hours > 0 ? "team" : "warn"),
            new("Scholarships open", open.ToString(), $"Class of {year}", open == 0 ? "warn" : "info"),
            new("Offers out", $"{c.OffersOut(me, year)} of {c.OfferLimit(me, year)}", "uncommitted prospects", ""),
            new("Committed to you", c.Commits(me, year).ToString(), year == senior ? $"signing day {CollegeWorld.SigningDay(year):MMM d}" : "early pledges", c.Commits(me, year) > 0 ? "good" : ""),
            new("Class rank", ourRank >= 0 ? $"No. {ourRank + 1}" : "-", ourRank >= 0 ? $"{ranking[ourRank].Points} pts" : "no commitments yet", ourRank is >= 0 and < 25 ? "good" : ""),
        };
        string full = open == 0 ? $"No scholarships left in the Class of {year}: build for the next class. " : "";
        TrailNote = full + (Session.Delegated.Recruiting
            ? "Your assistant spends the hours you leave unused each week on the best fits. A prospect can only commit to you once you've offered."
            : "You have the trail to yourself: hours you don't use are lost. A prospect can only commit to you once you've offered.");
        SelectedTrail = keep == null ? null : TrailRows.FirstOrDefault(x => x.Player == keep);
        FillTrailPanel();
    }

    private void FillTrailPanel()
    {
        if (SelectedTrail?.Player is not { } p || Session.TrailWorld is not { } c || c.RecruitOf(p) is not { } r)
        { ProspectChips = new(); TrailHeader = ""; TrailConsidering = ""; return; }
        var me = Session.HumanTeam;
        var board = c.BoardFor(r.ClassOf);
        bool ours = r.CourtedBy == me.Id;
        double interest = CollegeWorld.InterestIn(r, me);
        TrailHeader = $"No. {r.Rank} · {CollegeWorld.StarText(r.Stars)} {p.PrimaryPosition} · {r.HighSchool}, {CollegeWorld.StateName(r.State)} · Class of {r.ClassOf}";
        var lean = c.LeaningTo(p, r, board);
        ProspectChips = new()
        {
            new("Your interest", $"{interest:0}", ours ? $"{r.Calls} call{(r.Calls == 1 ? "" : "s")}{(r.HomeVisit ? " · home visit" : "")}{(r.OfficialVisit ? " · official visit" : "")}" : "not recruited yet", interest >= 50 ? "good" : interest > 0 ? "" : "warn"),
            new("Leaning", lean == null ? "-" : lean == me ? "You" : lean.City, r.CommittedTo != null ? "committed" : "if he chose today", lean == me ? "good" : "warn"),
            new("Cares most about", ours && r.Scouted ? CollegeWorld.PriorityText(CollegeWorld.PriorityOf(p)) : "?", ours && r.Scouted ? "it weighs extra with him" : "scout him to find out", "info"),
            new("Your staff project", ours && r.Scouted ? $"{c.StaffProjection(p, r)}" : "?", ours && r.Scouted ? "ability at 23" : $"services: {r.Projection}", ""),
            new("Offer", ours && r.Offered ? "Made" : "None", ours && r.Offered ? "he can commit to you" : "needed before he commits", ours && r.Offered ? "good" : ""),
        };
        var list = c.Considering(p, r, board).Take(4).ToList();
        int open = c.OpenScholarships(me, r.ClassOf), standing = c.StandingOf(me, p, r, board);
        string why = open <= 0 ? "no scholarship left in his class" : !(ours && r.Offered) ? "no offer yet" : "";
        TrailConsidering = r.CommittedTo is { } to ? $"Committed to {c.Program(to)!.FullName}."
            : "Considering: " + string.Join(" · ", list.Select((x, i) => $"{i + 1}. {x.Program.City}{(x.Program == me ? " (you)" : "")}")) + "."
              + (why != "" ? $" You'd rank {standing} with him, but you can't land him: {why}." : list.All(x => x.Program != me) ? $" You rank {standing}." : "");
    }

    private void AfterTrail(string reply)
    {
        FillTrail();
        TrailReply = reply;
        Shell.Guide.Refresh();
    }

    [RelayCommand] private void ScoutTrail() { if (SelectedTrail?.Player is { } p) AfterTrail(Session.ScoutProspect(p)); }
    [RelayCommand] private void CallTrail() { if (SelectedTrail?.Player is { } p) AfterTrail(Session.CallProspect(p)); }
    [RelayCommand] private void HomeVisitTrail() { if (SelectedTrail?.Player is { } p) AfterTrail(Session.HomeVisitProspect(p)); }
    [RelayCommand] private void OfficialVisitTrail() { if (SelectedTrail?.Player is { } p) AfterTrail(Session.OfficialVisitProspect(p)); }
    [RelayCommand] private void OfferTrail() { if (SelectedTrail?.Player is { } p) AfterTrail(Session.OfferProspect(p)); }
    [RelayCommand] private void BoardTrail() { if (SelectedTrail?.Player is { } p) AfterTrail(Session.BoardProspect(p)); }
    [RelayCommand] private void OpenTrail() { if (SelectedTrail?.Player is { } p) Shell.ShowPlayer(p); }
}
