using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hardwood.Core.College;
using Hardwood.Core.Model;
using Hardwood.Desktop.Controls;

namespace Hardwood.Desktop.ViewModels;

/// <summary>A high-school prospect on a college coach's recruiting board (C3, #173).</summary>
public sealed partial class TrailRow : ObservableObject
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
    // Run 68 (review #13 rank 7): the prospect as a star card on your board.
    public int StarCount { get; init; }
    public Team? LeaningTeam { get; init; }
    public string HighSchool { get; init; } = "";
    [ObservableProperty] private bool _picked;
    public string RankText => $"#{Rank}";
    public string StarsOn => CollegeCards.On(StarCount);
    public string StarsOff => CollegeCards.Off(StarCount);
    public Avalonia.Media.IBrush StarBrush => CollegeCards.StarEdge(StarCount);
    public string CardLine => $"{Pos} · {PlayerRow.FormatHeight(Player.HeightCm)} · {CollegeWorld.StateName(State)}";
    public string StarTip => CollegeCards.StarTip(StarCount, Rank);
    public bool HasLeaning => LeaningTeam != null;
    public string LeaningText => LeaningTeam == null ? "No leader yet" : $"Leans {Leaning}";
    public Avalonia.Media.IBrush StatusBrush => CollegeCards.StatusBrush(Status);
    public Avalonia.Media.IBrush InterestBrush => CollegeCards.InterestBrush(Interest);
}

/// <summary>The recruiting trail on the College page, for a college coach (slice C3, decisions #173-#178).</summary>
public sealed partial class CollegeViewModel
{
    /// <summary>You coach a college program: the Recruiting tab is your high-school trail, not the pros' senior board.</summary>
    public bool IsCollegeCoach => Session.TrailOpen;
    public bool IsProRecruiting => !Session.InCollege && Session.SeniorRecruiting; // D48 B: real careers draft instead
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
    /// <summary>Bug list #13: bound instead of SelectedTrail.Name.</summary>
    public string TrailName => SelectedTrail?.Name ?? "";
    private bool _trailLoading;

    partial void OnTrailViewChanged(string value) => FillTrail();
    partial void OnTrailClassChanged(string value) => FillTrail();
    partial void OnSelectedTrailChanged(TrailRow? value)
    {
        foreach (var r in TrailRows) r.Picked = r == value;
        OnPropertyChanged(nameof(HasTrail)); OnPropertyChanged(nameof(TrailName)); TrailReply = ""; FillTrailPanel();
    }
    /// <summary>Run 68: your board as star cards (the best 30 of the view; the picked prospect is kept on it).</summary>
    public ObservableCollection<TrailRow> TrailCards { get; } = new();
    [ObservableProperty] private string _trailCardNote = "";
    /// <summary>Why the board is empty (it used to be a blank table).</summary>
    [ObservableProperty] private string _trailEmpty = "";
    public bool HasTrailEmpty => TrailEmpty.Length > 0;
    partial void OnTrailEmptyChanged(string value) => OnPropertyChanged(nameof(HasTrailEmpty));
    /// <summary>A card press picks him, as a row click in the table does.</summary>
    public void PickTrail(TrailRow row) => SelectedTrail = row;
    partial void OnAssistantRecruitsChanged(bool value) { if (_trailLoading) return; Session.Delegated.Recruiting = value; Session.SaveNow(); FillTrail(); }

    private int TrailYear => int.TryParse(TrailClass.Replace("Class of ", "").Split(' ')[0], out var y) ? y : 0;

    private void FillTrail()
    {
        var keep = SelectedTrail?.Player;
        TrailRows.Clear(); TrailCards.Clear(); TrailEmpty = ""; TrailCardNote = "";
        OnPropertyChanged(nameof(IsCollegeCoach)); OnPropertyChanged(nameof(IsProRecruiting));
        if (!Session.TrailOpen || Session.TrailWorld is not { } c) return;
        _trailLoading = true; AssistantRecruits = Session.Delegated.Recruiting; _trailLoading = false;
        if (!Classes.Contains(TrailClass))
        {
            // Seniors only (#320): start on the class being recruited (Classes lists it first).
            if (Classes.Count > 0) TrailClass = Classes[0];
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
                StarCount = r.Stars, LeaningTeam = lean, HighSchool = r.HighSchool,
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
            OffersChip(c.OffersOut(me, year), c.OfferLimit(me, year)),
            new("Committed to you", c.Commits(me, year).ToString(), year == senior ? $"signing day {CollegeWorld.SigningDay(year):MMM d}" : "early pledges", c.Commits(me, year) > 0 ? "good" : ""),
            new("Class rank", ourRank >= 0 ? $"No. {ourRank + 1}" : "-", ourRank >= 0 ? $"{ranking[ourRank].Points} pts" : "no commitments yet", ourRank is >= 0 and < 25 ? "good" : ""),
        };
        bool closed = !c.Recruitable(new Recruit { ClassOf = year }, Session.Today);
        string full = closed ? $"Signing day has passed for the Class of {year}: the rising seniors are the class to recruit now. "
            : open == 0 ? $"No scholarships left in the Class of {year}: next year's seniors can be recruited after signing day ({CollegeWorld.SigningDay(year):MMM d}). "
            // CS1 (run 125): the calendar for the class you're chasing.
            : $"{open} scholarship{(open == 1 ? "" : "s")} open in the Class of {year}: seniors commit through the season, many in the November early period, and the rest sign on signing day ({CollegeWorld.SigningDay(year):MMM d}). ";
        TrailNote = full + OverOffersNote(c.OffersOut(me, year), c.OfferLimit(me, year), open, year) + (Session.Delegated.Recruiting
            ? "You recruit high-school seniors only. Your assistant spends the hours you leave unused each week on the best fits. A prospect can only commit to you once you've offered."
            : "You recruit high-school seniors only, and you have the trail to yourself: hours you don't use are lost. A prospect can only commit to you once you've offered.");
        foreach (var r in TrailRows.Take(CollegeCards.CardCount)) TrailCards.Add(r);
        if (keep != null && !TrailCards.Any(r => r.Player == keep) && TrailRows.FirstOrDefault(r => r.Player == keep) is { } further) TrailCards.Add(further);
        TrailEmpty = TrailRows.Count > 0 ? ""
            : TrailView == "Best fits" && open == 0 ? $"No best fits: every scholarship in the Class of {year} is taken. \"All uncommitted\", \"5-star\" and \"4-star\" still list the class."
            : TrailView == "Best fits" && closed ? $"No best fits: signing day has passed for the Class of {year}. Pick the rising seniors' class."
            : TrailView switch
            {
                "My board" => $"Your board for the Class of {year} is empty. Pick a prospect in \"Best fits\" and press Board +/- or Offer.",
                "Committed to you" => $"No one in the Class of {year} has committed to you yet. A prospect can only commit once you've offered.",
                _ => $"No prospects in \"{TrailView}\" for the Class of {year}.",
            };
        TrailCardNote = TrailRows.Count <= CollegeCards.CardCount ? $"{TrailRows.Count} prospects in \"{TrailView}\". Pick a card to recruit him."
            : $"The first {CollegeCards.CardCount} of {TrailRows.Count} in \"{TrailView}\". Pick a card to recruit him; the full table lists them all.";
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
            new("Your staff project", ours && r.Scouted ? $"{c.StaffProjection(p, r)}" : "?", ours && r.Scouted ? "ability at 22" : $"services: {r.Projection}", ""),
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

    /// <summary>
    /// H22 (bug hunt, Nov 16; run 79): the offer limit is open scholarships + 3 (0 once none is open), so when places
    /// fill after offers went out (above all the last one: the limit drops from 4 to 0) the offers out can outnumber the
    /// limit ("8 of 5", "3 of 0"). They stay out (nobody's offer is pulled); the chip and the trail note say so.
    /// </summary>
    public static Chip OffersChip(int offersOut, int limit)
    {
        if (offersOut <= limit) return new("Offers out", $"{offersOut} of {limit}", "uncommitted prospects", "");
        // H83 (run 106): "3 of 0" read as nonsense; with no scholarship open it's "3 out · no scholarships open".
        if (limit == 0) return new("Offers out", $"{offersOut} out", "no scholarships open", "warn");
        return new("Offers out", $"{offersOut} of {limit}", "over: no new offers yet", "warn");
    }

    /// <summary>H22: why the offers out can pass the limit, in the trail's note.</summary>
    public static string OverOffersNote(int offersOut, int limit, int open, int year) => offersOut <= limit ? ""
        : limit == 0
            ? $"Your {offersOut} offers out to the Class of {year} stay out, but none of them can commit to you unless a scholarship opens up. "
            : $"You have {offersOut} offers out to the Class of {year} but can now have only {limit} ({open} scholarship{(open == 1 ? "" : "s")} open + 3): "
              + "places filled after those offers went out. They stay out, and any of them can still commit while a place is open; "
              + $"you can make a new offer once fewer than {limit} are out. ";
}
