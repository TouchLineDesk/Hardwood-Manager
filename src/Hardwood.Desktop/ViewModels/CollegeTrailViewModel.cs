using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hardwood.Core.College;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Desktop.Controls;
using System.Linq;

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
    /// <summary>K15a (run 146): what he cares about most, once your staff has scouted him ("" before).</summary>
    public string Cares { get; init; } = "";
    /// <summary>Run 164: before your staff scouts him, the recruiting services' word on what he cares about ("" once scouted).</summary>
    public string Word { get; init; } = "";
    public bool HasCares => Cares.Length > 0 || Word.Length > 0;
    public string CaresText => Cares.Length > 0 ? $"Cares about {Cares}" : Word.Length > 0 ? $"Word is: {Word}?" : "";
    public string CaresTip => Cares.Length > 0 ? "What he weighs most when he picks a school (your scouts found out)"
        : $"The recruiting services' word on what he weighs most. It's right about {CollegeWorld.WordAccuracy * 10:0} times in 10: scout him to be sure.";
    /// <summary>The table's "Cares about" column: confirmed, or the word with a question mark.</summary>
    public string CaresCell => Cares.Length > 0 ? Cares : Word.Length > 0 ? $"{Word}?" : "";
    /// <summary>Run 164 (critic review #21 item 5): why he'd say no to you today, on his card ("" when he wouldn't).</summary>
    public string NoReason { get; init; } = "";
    public bool HasNoReason => NoReason.Length > 0;
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

/// <summary>Run 197: one road event on the Recruiting tab (the spring and July evaluation periods, the camp, the visit weekend).</summary>
public sealed partial class RoadTile : ObservableObject
{
    private readonly Action<RoadEvent, string>? _choose;
    private bool _loading;
    public RoadTile(RoadRow row, Action<RoadEvent, string>? choose)
    {
        Row = row; _choose = choose;
        _loading = true; _choice = row.Choice; _loading = false;
    }
    public RoadRow Row { get; }
    public string Title => Row.Title;
    public string When => Row.When;
    public IReadOnlyList<string> Options => Row.Options;
    public bool Open => Row.Open;
    public bool Done => !Row.Open;
    public string Help => Row.Help;
    public string Result => Row.Result ?? "";
    public string State => Row.Open ? (Row.Choice == null ? "Not picked: your staff will decide" : "Your pick")
        : Row.Choice == null ? "Passed" : Row.ByStaff ? "Done · your staff's pick" : "Done · your pick";
    [ObservableProperty] private string? _choice;
    partial void OnChoiceChanged(string? value)
    {
        if (_loading || value == null) return;
        _choose?.Invoke(Row.Event, value);
    }
}

/// <summary>Run 164: one line of the first-week guide.</summary>
public sealed record GuideStepRow(string Mark, string Title, string How, bool Done)
{
    public double Faded => Done ? 0.6 : 1;
}

/// <summary>The recruiting trail on the College page, for a college coach (slice C3, decisions #173-#178).</summary>
public sealed partial class CollegeViewModel
{
    /// <summary>You coach a college program: the Recruiting tab is your high-school trail, not the pros' senior board.</summary>
    public bool IsCollegeCoach => Session.TrailOpen;
    public bool IsProRecruiting => !Session.InCollege && Session.SeniorRecruiting; // D48 B: real careers draft instead
    public List<string> TrailViews { get; } = new() { "Best fits", "My board", LeaningElsewhere, "Committed to you", "All uncommitted", "5-star", "4-star" };
    /// <summary>H156 (run 146): the offers out to prospects leaning to another school, as a view instead of a paragraph.</summary>
    public const string LeaningElsewhere = "Offers leaning elsewhere";
    /// <summary>K15a (run 146): the pitch picker's choices (<see cref="CollegeWorld.PitchName"/>), "No pitch" first.</summary>
    public List<string> TrailPitches { get; } = new[] { (RecruitPriority?)null }.Concat(CollegeWorld.Pitches.Select(k => (RecruitPriority?)k)).Select(CollegeWorld.PitchName).ToList();
    [ObservableProperty] private string _trailPitch = CollegeWorld.PitchName(null);
    [ObservableProperty] private string _trailWhy = "";
    [ObservableProperty] private string _trailPitchNote = "";
    public bool HasTrailWhy => TrailWhy.Length > 0;
    partial void OnTrailWhyChanged(string value) => OnPropertyChanged(nameof(HasTrailWhy));
    private bool _pitchLoading;
    partial void OnTrailPitchChanged(string value)
    {
        if (value is null || _pitchLoading || SelectedTrail?.Player is not { } p) return;
        var k = CollegeWorld.Pitches.Cast<RecruitPriority?>().FirstOrDefault(x => CollegeWorld.PitchName(x) == value);
        AfterTrail(Session.PitchProspect(p, k));
    }
    /// <summary>H156: the other class on the list (after signing day, the rising seniors), offered from the empty state.</summary>
    public string TrailOtherClass => Classes.FirstOrDefault(x => x != TrailClass) ?? "";
    public bool HasTrailOtherClass => HasTrailEmpty && _otherClassOpen && TrailOtherClass.Length > 0;
    /// <summary>Run 146 review: offer the other class only when the one on screen is closed (signed) and the other is open.</summary>
    private bool _otherClassOpen;
    public string TrailOtherClassText => TrailOtherClass.Length > 0 ? $"See the {TrailOtherClass.Split(" (")[0]}" : "";
    [RelayCommand] private void OtherTrailClass() { if (TrailOtherClass is { Length: > 0 } other) TrailClass = other; }
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
    partial void OnTrailEmptyChanged(string value)
    {
        OnPropertyChanged(nameof(HasTrailEmpty)); OnPropertyChanged(nameof(HasTrailOtherClass));
        OnPropertyChanged(nameof(TrailOtherClass)); OnPropertyChanged(nameof(TrailOtherClassText));
    }
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
                LeaningElsewhere => x.r.CourtedBy == me.Id && x.r.Offered && x.r.CommittedTo == null && c.LeaningTo(x.p, x.r, board) is { } l && l != me,
                "5-star" => x.r.Stars == 5,
                "4-star" => x.r.Stars == 4,
                _ => x.r.CommittedTo == null,
            }).OrderBy(x => x.r.Rank).Take(150),
        };
        var ranks = c.PrestigeRanks();
        int n = 0;
        foreach (var (p, r) in rows)
        {
            double interest = CollegeWorld.InterestIn(r, me);
            var lean = c.LeaningTo(p, r, board);
            TrailRows.Add(new TrailRow
            {
                Player = p, Rank = r.Rank, Stars = r.Stars > 0 ? new string('★', r.Stars) : "–", State = r.State,
                StarCount = r.Stars, LeaningTeam = lean, HighSchool = r.HighSchool,
                Cares = r.CourtedBy == me.Id && r.Scouted ? CollegeWorld.PriorityShort(CollegeWorld.PriorityOf(p)) : "",
                Word = r.CourtedBy == me.Id && r.Scouted ? "" : CollegeWorld.PriorityShort(CollegeWorld.WordOf(p)),
                NoReason = n++ < CollegeCards.CardCount || p == keep ? c.SaysNo(me, p, r, board, lean, ranks) : "",
                Interest = interest, InterestText = $"{interest:0}",
                Leaning = lean == null ? "-" : lean == me ? "You" : lean.City,
                Status = r.CommittedTo is { } to ? (CollegeWorld.Signed(r, Session.Today) ? (to == me.Id ? "Signed with you" : $"Signed: {c.Program(to)!.City}")
                                                     : to == me.Id ? "Committed to you" : $"Committed: {c.Program(to)!.City}")
                       : r.CourtedBy == me.Id && r.Offered ? "Offered" : r.CourtedBy == me.Id && r.OnBoard ? "On your board" : "Open",
            });
        }
        int senior = c.SeniorClass;
        var ranking = c.ClassRanking(year);
        int ourRank = ranking.FindIndex(x => x.Program == me);
        int open = c.OpenScholarships(me, year), hours = Session.TrailHoursLeft;
        var chips = new List<Chip>
        {
            new("Coach hours", $"{hours} of {CollegeWorld.WeeklyHours}", "this week · refill Mondays", hours > 0 ? "team" : "warn"),
            new("Scholarships open", open.ToString(), $"Class of {year}", open == 0 ? "warn" : "info"),
            OffersChip(c.OffersOut(me, year), c.OfferLimit(me, year)),
            new("Committed to you", c.Commits(me, year).ToString(), year == senior ? $"signing day {CollegeWorld.SigningDay(year):MMM d}" : "early pledges", c.Commits(me, year) > 0 ? "good" : ""),
            new("Class rank", ourRank >= 0 ? $"No. {ourRank + 1}" : "-", ourRank >= 0 ? $"{ranking[ourRank].Points} pts" : "no commitments yet", ourRank is >= 0 and < 25 ? "good" : ""),
        };
        // H156 (run 146): the offers leaning elsewhere are a chip (and a view), not a paragraph.
        var (leaningAway, _) = OffersLeaning(c, me, year, board);
        if (leaningAway > 0 && chips.FindIndex(x => x.Label == "Offers out") is var oi and >= 0)
            chips[oi] = chips[oi] with { Sub = chips[oi].Tone == "" ? $"{leaningAway} leaning elsewhere" : $"{chips[oi].Sub} · {leaningAway} lean away", Tone = "warn" };
        // K15b (run 147): a broken promise is known on the trail for a year.
        if (c.WordBroken && c.BrokenWordUntil is { } until)
            chips.Add(new("Your word", $"−{CollegeWorld.BrokenWordPenalty:0}", $"a broken promise · until {until:MMM d, yyyy}", "warn"));
        TrailChips = chips;
        bool closed = !c.Recruitable(new Recruit { ClassOf = year }, Session.Today);
        _otherClassOpen = closed && TrailOtherClass.Length > 0 && int.TryParse(TrailOtherClass.Replace("Class of ", "").Split(' ')[0], out var oy)
                          && c.Recruitable(new Recruit { ClassOf = oy }, Session.Today);
        string full = closed ? $"Signing day has passed for the Class of {year}: the rising seniors are the class to recruit now. "
            : open == 0 ? $"No scholarships left in the Class of {year}: next year's seniors can be recruited after signing day ({CollegeWorld.SigningDay(year):MMM d}). "
            // CS1 (run 125): the calendar for the class you're chasing.
            : $"{open} scholarship{(open == 1 ? "" : "s")} open in the Class of {year}: seniors commit through the season, many in the November early period, and the rest sign on signing day ({CollegeWorld.SigningDay(year):MMM d}). ";
        TrailNote = full + OverOffersNote(c.OffersOut(me, year), c.OfferLimit(me, year), open, year)
                    + PlacesNote(c.OffersOut(me, year), c.OfferLimit(me, year), open) + "A prospect can only commit to you once you've offered.";
        FillGuide(c, me, year);
        RoadReply = ""; FillRoad();
        foreach (var r in TrailRows.Take(CollegeCards.CardCount)) TrailCards.Add(r);
        if (keep != null && !TrailCards.Any(r => r.Player == keep) && TrailRows.FirstOrDefault(r => r.Player == keep) is { } further) TrailCards.Add(further);
        TrailEmpty = TrailRows.Count > 0 ? ""
            : TrailView == "Best fits" && open == 0 ? $"No best fits: every scholarship in the Class of {year} is taken. \"All uncommitted\", \"5-star\" and \"4-star\" still list the class."
            : TrailView == "Best fits" && closed ? $"No best fits: signing day has passed for the Class of {year}. Pick the rising seniors' class."
            : TrailView switch
            {
                "My board" => $"Your board for the Class of {year} is empty. Pick a prospect in \"Best fits\" and press Board +/- or Offer.",
                "Committed to you" => $"No one in the Class of {year} has committed to you yet. A prospect can only commit once you've offered.",
                LeaningElsewhere => $"None of your open offers in the Class of {year} leans to another school.",
                _ => $"No prospects in \"{TrailView}\" for the Class of {year}.",
            };
        TrailCardNote = TrailView == LeaningElsewhere ? OffersLeaningNote(c, me, year, board).Trim()
            : TrailRows.Count <= CollegeCards.CardCount ? $"{TrailRows.Count} prospects in \"{TrailView}\". Pick a card to recruit him."
            : $"The first {CollegeCards.CardCount} of {TrailRows.Count} in \"{TrailView}\". Pick a card to recruit him; the full table lists them all.";
        // H178 (run 164): the detail pane opens on the first card instead of an empty column.
        SelectedTrail = keep == null ? TrailCards.FirstOrDefault() : TrailRows.FirstOrDefault(x => x.Player == keep) ?? TrailCards.FirstOrDefault();
        FillTrailPanel();
    }

    /// <summary>Run 197: the recruiting class's road calendar (four events), with a pick for each still to come.</summary>
    public ObservableCollection<RoadTile> RoadTiles { get; } = new();
    [ObservableProperty] private string _roadHeader = "";
    [ObservableProperty] private string _roadReply = "";
    public bool HasRoad => RoadTiles.Count > 0;

    private void FillRoad()
    {
        RoadTiles.Clear();
        var rows = Session.RoadCalendar();
        foreach (var r in rows) RoadTiles.Add(new RoadTile(r, (e, choice) => { RoadReply = Session.ChooseRoad(e, choice); FillRoad(); }));
        int cls = Session.TrailWorld?.RecruitingClass(Session.Today) ?? 0;
        int open = rows.Count(r => r.Open);
        RoadHeader = rows.Count == 0 ? "" : $"On the road · the Class of {cls}" + (open > 0 ? $" · {open} still to come" : " · all done for this class");
        OnPropertyChanged(nameof(HasRoad));
    }

    /// <summary>Run 164: the first-week guide's steps (scout, offer, visit) for the class on screen; empty once all three are done.</summary>
    [ObservableProperty] private List<GuideStepRow> _trailGuide = new();
    [ObservableProperty] private string _trailGuideHeader = "";
    public bool HasTrailGuide => TrailGuide.Count > 0;
    partial void OnTrailGuideChanged(List<GuideStepRow> value) => OnPropertyChanged(nameof(HasTrailGuide));

    private void FillGuide(CollegeWorld c, Team me, int year)
    {
        var steps = c.FirstWeekGuide(me, year, Session.Today);
        if (steps.Count == 0 || steps.All(s => s.Done)) { TrailGuide = new(); TrailGuideHeader = ""; return; }
        int done = steps.Count(s => s.Done);
        var top = TrailView == "Best fits" ? TrailRows.FirstOrDefault() : null;
        TrailGuideHeader = $"Your first week on the trail · {done} of {steps.Count} done"
            + (top != null && done == 0 ? $" · start with No. {top.Rank} {top.Name}, your best fit" : "");
        TrailGuide = steps.Select((s, i) => new GuideStepRow(s.Done ? "✓" : $"{i + 1}", s.Title, s.How, s.Done)).ToList();
    }

    /// <summary>
    /// H152 (run 136): why the offers out aren't turning into commitments. "2 of your 5 offers are with prospects leaning
    /// elsewhere: No. 53 Ty Cole (Kentucky), No. 84 Ed Moss (UConn)." Empty when every offer leans your way.
    /// </summary>
    public static string OffersLeaningNote(CollegeWorld c, Team me, int year, CollegeWorld.ClassBoard board)
    {
        var offered = c.ClassOf(year).Select(p => (p, r: c.RecruitOf(p)!))
            .Where(x => x.r.CourtedBy == me.Id && x.r.Offered && x.r.CommittedTo == null).OrderBy(x => x.r.Rank).ToList();
        if (offered.Count == 0) return "";
        var leans = offered.Select(x => (x.p, x.r, lean: c.LeaningTo(x.p, x.r, board))).ToList();
        var away = leans.Where(x => x.lean != null && x.lean != me).ToList();
        if (leans.Any(x => x.lean == null)) return away.Count == 0 ? "" : $"{away.Count} of your {offered.Count} open offers are with prospects leaning elsewhere: "
            + string.Join(", ", away.Take(4).Select(x => $"No. {x.r.Rank} {x.p.Name} ({x.lean!.City})")) + (away.Count > 4 ? $" and {away.Count - 4} more" : "") + ". ";
        if (away.Count == 0) return offered.Count == 1 ? "Your open offer leans your way today. " : $"All {offered.Count} of your open offers lean your way today. ";
        return (offered.Count == 1 ? "Your open offer is with a prospect leaning elsewhere: " : $"{away.Count} of your {offered.Count} open offers are with prospects leaning elsewhere: ")
            + string.Join(", ", away.Take(4).Select(x => $"No. {x.r.Rank} {x.p.Name} ({x.lean!.City})")) + (away.Count > 4 ? $" and {away.Count - 4} more" : "")
            + ". More calls and visits close the gap; a prospect far behind usually signs elsewhere. ";
    }

    /// <summary>H156: how many of the open offers lean to another school today, of how many.</summary>
    public static (int Away, int Offered) OffersLeaning(CollegeWorld c, Team me, int year, CollegeWorld.ClassBoard board)
    {
        var offered = c.ClassOf(year).Select(p => (p, r: c.RecruitOf(p)!)).Where(x => x.r.CourtedBy == me.Id && x.r.Offered && x.r.CommittedTo == null).ToList();
        return (offered.Count(x => c.LeaningTo(x.p, x.r, board) is { } l && l != me), offered.Count);
    }

    /// <summary>The assistant checkbox's tip (the trail note's old last sentence, H156).</summary>
    public string AssistantTip => "You recruit high-school seniors only. With this on, your assistant spends the hours you leave unused each week on the best fits, sells each scouted prospect what he cares about, and from March withdraws offers that won't be accepted. With it off, hours you don't use are lost.";

    private void FillTrailPanel()
    {
        if (SelectedTrail?.Player is not { } p || Session.TrailWorld is not { } c || c.RecruitOf(p) is not { } r)
        { ProspectChips = new(); TrailHeader = ""; TrailConsidering = ""; TrailWhy = ""; TrailPitchNote = ""; return; }
        var me = Session.HumanTeam;
        var board = c.BoardFor(r.ClassOf);
        bool ours = r.CourtedBy == me.Id;
        double interest = CollegeWorld.InterestIn(r, me);
        string tag = CollegeWorld.ProspectTag(r);
        TrailHeader = $"No. {r.Rank} · {CollegeWorld.StarText(r.Stars)} {p.PrimaryPosition}{(tag != "" ? $" · {tag}" : "")} · {r.HighSchool}, {CollegeWorld.StateName(r.State)} · Class of {r.ClassOf}";
        var lean = c.LeaningTo(p, r, board);
        ProspectChips = new()
        {
            new("Your interest", $"{interest:0}", ours ? $"{r.Calls} call{(r.Calls == 1 ? "" : "s")}{(r.HomeVisit ? " · home visit" : "")}{(r.OfficialVisit ? " · official visit" : "")}" : "not recruited yet", interest >= 50 ? "good" : interest > 0 ? "" : "warn"),
            new("Leaning", lean == null ? "-" : lean == me ? "You" : lean.City, r.CommittedTo != null ? "committed" : "if he chose today", lean == me ? "good" : "warn"),
            new("Cares most about", ours && r.Scouted ? CollegeWorld.PriorityText(CollegeWorld.PriorityOf(p)) : $"{CollegeWorld.PriorityShort(CollegeWorld.WordOf(p))}?",
                ours && r.Scouted ? "it weighs extra with him" : $"the word (right {CollegeWorld.WordAccuracy * 10:0} in 10) · scout to be sure", "info"),
            new("Your staff project", ours && r.Scouted ? $"{c.StaffProjection(p, r)}" : "?", ours && r.Scouted ? "ability at 22" : $"services: {r.Projection}", ""),
            new("Your pitch", ours && r.Pitch is { } pk ? CollegeWorld.PitchName(pk) : "None",
                !(ours && r.Pitch != null) ? "pick one below" : !r.Scouted ? "scout him to know if it fits" : CollegeWorld.PitchMatches(p, r) == true ? $"fits · contacts land {(CollegeWorld.PitchMatch - 1) * 100:0}% better" : $"misses · contacts land {(1 - CollegeWorld.PitchMiss) * 100:0}% worse",
                !(ours && r.Pitch != null && r.Scouted) ? "" : CollegeWorld.PitchMatches(p, r) == true ? "good" : "warn"),
            new("Offer", ours && r.Offered ? "Made" : "None", ours && r.Offered ? (r.CommittedTo is { } ct && ct != me.Id ? "a verbal commit elsewhere" : "he can commit to you") : "needed before he commits", ours && r.Offered ? "good" : ""),
            // K15b (run 147): a starting-role promise (two a class), and whether the trail still believes you.
            new("Promise", ours && r.Promised ? "Starting role" : "None",
                ours && r.Promised ? (c.WordBroken ? "worth nothing: your word is broken" : $"+{CollegeWorld.PromiseAppeal:0} · judged in January")
                    : $"{c.PromisesMade(me, r.ClassOf)} of {CollegeWorld.PromisesPerClass} made this class",
                ours && r.Promised ? (c.WordBroken ? "warn" : "good") : ""),
            // CP3 (run 132): your graduates in the pros sway him a little (see the College page's Your pros tab).
            new("Your pros", c.PitchFor(r) > 0 ? $"+{c.PitchFor(r):0.#}" : "0", c.ProPitchText == "" ? "no graduates yet" : c.ProPitchText, c.PitchFor(r) > 0 ? "good" : ""),
        };
        var list = c.Considering(p, r, board).Take(4).ToList();
        int open = c.OpenScholarships(me, r.ClassOf), standing = c.StandingOf(me, p, r, board);
        string why = open <= 0 ? "no scholarship left in his class" : !(ours && r.Offered) ? "no offer yet" : "";
        TrailConsidering = r.CommittedTo is not null ? c.CommitmentText(r, Session.Today, me)
            : "Considering: " + string.Join(" · ", list.Select((x, i) => $"{i + 1}. {x.Program.City}{(x.Program == me ? " (you)" : "")}")) + "."
              + (why != "" ? $" You'd rank {standing} with him, but you can't land him: {why}." : list.All(x => x.Program != me) ? $" You rank {standing}." : "");
        // K15a (run 146): why you trail (or lead), factor by factor, and what your calls and visits sell him.
        TrailWhy = c.WhyTrails(me, p, r, board);
        _pitchLoading = true; TrailPitch = CollegeWorld.PitchName(ours ? r.Pitch : null); _pitchLoading = false;
        TrailPitchNote = "What your calls and visits sell him. " + (ours && r.Scouted
            ? $"He cares most about {CollegeWorld.PriorityText(CollegeWorld.PriorityOf(p))}: the right pitch lands {(CollegeWorld.PitchMatch - 1) * 100:0}% better, a wrong one {(1 - CollegeWorld.PitchMiss) * 100:0}% worse."
            : $"The word is he cares about {CollegeWorld.PriorityText(CollegeWorld.WordOf(p))}, and it's right about {CollegeWorld.WordAccuracy * 10:0} times in 10. Scout him to be sure: a wrong pitch lands worse than none.");
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
    [RelayCommand] private void PromiseTrail() { if (SelectedTrail?.Player is { } p) AfterTrail(Session.PromiseProspect(p)); }
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

    /// <summary>
    /// H178 (run 164): more offers out than places (but within the limit): "5 offers out for 2 places: the first 2 to
    /// commit take them, and the rest can't commit to you once the class is full."
    /// </summary>
    public static string PlacesNote(int offersOut, int limit, int open) => open <= 0 || offersOut <= open || offersOut > limit ? ""
        : $"{offersOut} offers out for {open} place{(open == 1 ? "" : "s")}: the first {(open == 1 ? "one" : open.ToString())} to commit take{(open == 1 ? "s it" : " them")}, and the rest can't commit to you once the class is full. ";

    /// <summary>H22: why the offers out can pass the limit, in the trail's note.</summary>
    public static string OverOffersNote(int offersOut, int limit, int open, int year) => offersOut <= limit ? ""
        : limit == 0
            ? $"Your {offersOut} offers out to the Class of {year} stay out, but none of them can commit to you unless a scholarship opens up. "
            : $"You have {offersOut} offers out to the Class of {year} but can now have only {limit} ({open} scholarship{(open == 1 ? "" : "s")} open + 3): "
              + "places filled after those offers went out. They stay out, and any of them can still commit while a place is open; "
              + $"you can make a new offer once fewer than {limit} are out. ";
}
