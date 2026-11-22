using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hardwood.Core.Engine;
using Hardwood.Core.Model;
using Hardwood.Core.People;
using Hardwood.Core.Season;
using Hardwood.Desktop.Controls;

namespace Hardwood.Desktop.ViewModels;

public sealed class HomeRow
{
    public string Icon { get; init; } = "";
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
    public bool Highlight { get; init; }
    public IBrush IconBrush { get; init; } = Brushes.Gray;
    /// <summary>Roadmap 1: a coloured category badge (BOARD, MEDICAL, GAME…) so the Latest column scans like a news ticker.</summary>
    public string Badge { get; init; } = "";
    public IBrush BadgeBrush { get; init; } = Brushes.Gray;

    public static IBrush CategoryBrush(string category) => new SolidColorBrush(Color.Parse(category switch
    {
        "Board" => "#D29922", "Medical" => "#F85149", "Squad" => "#A371F7", "Transactions" or "Roster" => "#58A6FF",
        "League" => "#3FB950", "Awards" => "#E3B341", "World" => "#6E7A8A", "Draft" => "#58A6FF", _ => "#3A4654",
    }));
}

public sealed class TableRow
{
    public int Rank { get; init; }
    public Team? Team { get; init; }
    /// <summary>A shorter name where space is tight (a college program goes by its school, C2).</summary>
    public string? Label { get; init; }
    public string Name => Label ?? Team?.FullName ?? "";
    public string Record { get; init; } = "";
    public string Diff { get; init; } = "";
    public bool Ours { get; init; }
}

/// <summary>
/// Home: the start-of-day dashboard (decision #69): next game and the scouting line, a standings snapshot, squad
/// pulse (chemistry, unhappy players), injuries, the latest news, and charts for form, team vs league, payroll and mood.
/// </summary>
public sealed partial class HomeViewModel : PageViewModel
{
    public override string Title => "Home";
    public HomeViewModel(MainViewModel shell) : base(shell) { }

    public Team Team => Session.HumanTeam;
    [ObservableProperty] private string _headline = "";
    /// <summary>H52 (run 84): the Game plan and Rotation buttons show only while you coach a club.</summary>
    [ObservableProperty] private bool _coaching = true;
    [ObservableProperty] private string _subline = "";
    /// <summary>
    /// Bug hunt H15b (run 93): out of work, Home showed the club you'd left (its crest, standings row, squad pulse, payroll and
    /// next game). Now the hero says you're out of work, the card beside it counts the open jobs, the squad panels give way to
    /// those jobs, and a button opens them on the Board page.
    /// </summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HeroTeam))] private bool _outOfWork;
    /// <summary>The hero crest: your club, or none out of work.</summary>
    public Team? HeroTeam => OutOfWork ? null : Session.HumanTeam;
    /// <summary>"NEXT GAME", or "OPEN JOBS" out of work.</summary>
    [ObservableProperty] private string _cardTitle = "NEXT GAME";
    [ObservableProperty] private string _cardIcon = "CalendarDays";
    public ObservableCollection<HomeRow> Jobs { get; } = new();
    [ObservableProperty] private string _jobsNote = "";
    [ObservableProperty] private Color _glow;
    [ObservableProperty] private Team? _opponent;
    partial void OnOpponentChanged(Team? value) => OnPropertyChanged(nameof(HasOpponent));
    [ObservableProperty] private string _nextGame = "";
    [ObservableProperty] private string _nextDetail = "";
    [ObservableProperty] private string _scouting = "";
    [ObservableProperty] private string _chemistryText = "";
    public ObservableCollection<TableRow> Table { get; } = new();
    public ObservableCollection<HomeRow> Pulse { get; } = new();
    public ObservableCollection<HomeRow> Injuries { get; } = new();
    public ObservableCollection<HomeRow> News { get; } = new();
    /// <summary>"On this day" (roadmap 4): records and milestones from earlier seasons around today's date.</summary>
    [ObservableProperty] private List<string> _onThisDay = new();
    public bool HasOnThisDay => OnThisDay.Count > 0;
    partial void OnOnThisDayChanged(List<string> value) => OnPropertyChanged(nameof(HasOnThisDay));
    [ObservableProperty] private IReadOnlyList<LineSeries>? _form;
    [ObservableProperty] private string[]? _formLabels;
    [ObservableProperty] private IReadOnlyList<Bar>? _comparison;
    /// <summary>What the empty charts say before there's anything to draw (bug hunt #12).</summary>
    [ObservableProperty] private string _formNote = "";
    [ObservableProperty] private string _comparisonNote = "";
    [ObservableProperty] private IReadOnlyList<Bar>? _payroll;
    [ObservableProperty] private IReadOnlyList<RefLine>? _payrollLines;
    /// <summary>H25: true once this summer has counted the club's contracts down (the NBA's offseason from re-signing on; the world's summer for everyone else).</summary>
    public static bool SummerCounted(GameSession s, Hardwood.Core.Model.Team team) => s.ContractsCounted(team);

    /// <summary>"Payroll committed", or "Roster by class" at a college program (C2).</summary>
    [ObservableProperty] private string _payrollTitle = "Payroll committed";
    [ObservableProperty] private string _payrollIcon = "DollarSign";
    /// <summary>CS3 (H132): what the college class bars mean; empty for the pros.</summary>
    [ObservableProperty] private string _payrollCaption = "";
    [ObservableProperty] private IReadOnlyList<LineSeries>? _mood;
    [ObservableProperty] private string[]? _moodLabels;
    [ObservableProperty] private bool _hasMood;
    [ObservableProperty] private string _tableTitle = "Conference";
    /// <summary>H120 (run 124): a muted line under the standings title ("Overall records until conference play"), so a long
    /// conference name and the note don't share one line and get cut off.</summary>
    [ObservableProperty] private string _tableNote = "";
    /// <summary>C5 part 1 (run 94): "Crosstown Shootout · You lead the series 2-1 since 2026-27" or "Ranked matchup: #5 vs #9"; empty otherwise.</summary>
    [ObservableProperty] private string _gameTag = "";
    /// <summary>C5 part 2 (run 95): a college game's home court, e.g. "Cameron Indoor Stadium: one of the loudest in the country (+4.4 points to the home side)"; empty on a neutral court or in the pros.</summary>
    [ObservableProperty] private string _courtLine = "";
    /// <summary>C5 (run 96): a college conference game's style.</summary>
    [ObservableProperty] private string _styleLine = "";

    private static readonly IBrush Good = new SolidColorBrush(Color.Parse("#3FB950"));
    private static readonly IBrush Bad = new SolidColorBrush(Color.Parse("#F85149"));
    private static readonly IBrush Warn = new SolidColorBrush(Color.Parse("#D29922"));
    private static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#8B96A5"));

    // March Madness on a pro career's Home (C4 part 2, the last piece): from Selection Sunday to a few days after the final.
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasMadness))] private Hardwood.Core.College.MadnessWatch? _madness;
    public bool HasMadness => Madness != null;
    public bool HasMadnessProspects => Madness is { Prospects.Count: > 0 };
    // Flat, never-null values for the panel, so a career with no tournament on logs no binding errors (bug list #13).
    public string MadnessTitle => Madness?.Title ?? "";
    public string MadnessStage => Madness?.Stage ?? "";
    public string MadnessSummary => Madness?.Summary ?? "";
    public string MadnessResultsTitle => Madness?.ResultsTitle ?? "";
    public IReadOnlyList<Hardwood.Core.College.MadnessWatchRow> MadnessResults => Madness?.Results ?? Array.Empty<Hardwood.Core.College.MadnessWatchRow>();
    public string MadnessProspectsTitle => Madness?.ProspectsTitle ?? "";
    public IReadOnlyList<Hardwood.Core.College.MadnessWatchRow> MadnessProspects => Madness?.Prospects ?? Array.Empty<Hardwood.Core.College.MadnessWatchRow>();
    partial void OnMadnessChanged(Hardwood.Core.College.MadnessWatch? value)
    {
        foreach (var n in new[] { nameof(HasMadnessProspects), nameof(MadnessTitle), nameof(MadnessStage), nameof(MadnessSummary), nameof(MadnessResultsTitle),
                     nameof(MadnessResults), nameof(MadnessProspectsTitle), nameof(MadnessProspects) })
            OnPropertyChanged(n);
    }

    public override void Refresh()
    {
        var s = Session; var team = Team; var rec = s.MyRecord;
        // H73 (run 92): after a job change the hero crest kept the old club; Team is read from the session, so say it changed.
        OnPropertyChanged(nameof(Team));
        OnPropertyChanged(nameof(HeroTeam));
        Coaching = s.Employed;
        OutOfWork = !s.Employed;
        CardTitle = "NEXT GAME"; CardIcon = "CalendarDays";
        Madness = !s.InCollege && s.World.College is { } college ? college.Watch(s.Today) : null;
        Glow = ThemeService.Accent;
        var conf = s.MyTable;
        int seed = s.MyRank;
        TableTitle = s.InNabl ? $"{team.Conference} Conference" : s.HumanLeagueName;
        TableNote = "";
        Headline = team.FullName;
        if (s.InCollege && rec.Games > 0)
        {
            // College: the overall record, then the conference race once conference play has begun.
            var cr = s.MyStandings[team];
            Subline = $"{rec.Wins}-{rec.Losses} · " + (cr.Games > 0 ? $"{Ordinal(seed)} in the {s.MyConference!.Short} ({cr.Wins}-{cr.Losses})" : "conference play starts in January")
                + $" · {(rec.PointDiff >= 0 ? "+" : "")}{rec.PointDiff:0.0} per game";
        }
        else Subline = rec.Games > 0 ? $"{rec.Wins}-{rec.Losses} · {Ordinal(seed)} in the {s.MyTableName} · {(rec.PointDiff >= 0 ? "+" : "")}{rec.PointDiff:0.0} per game"
            : $"{s.HumanLeagueName} · season {s.League.Season}-{(s.League.Season + 1) % 100:00}";

        var next = s.NextMyGame;
        GameTag = ""; CourtLine = ""; StyleLine = "";
        if (next != null)
        {
            var opp = next.Opponent;
            Opponent = opp;
            var orec = s.RecordOf(opp);
            NextGame = next.Versus(Hardwood.Core.College.CollegeWorld.WithRank(next.OpponentRank, opp.FullName)); // #528; run 136: neutral
            NextDetail = $"{next.Date:ddd MMM d}{(next.Date == s.Today ? " (today)" : "")}" + (next.Competition != s.HumanLeagueName ? $" · {next.Competition}" : "")
                + (orec.Games > 0 ? $" · they're {orec.Wins}-{orec.Losses}" : "");
            GameTag = CollegeGameTag(s, team, next);
            CourtLine = CollegeCourtLine(s, team, next);
            StyleLine = s.InCollege && next.StyleConference is { } sk ? Hardwood.Core.College.CollegeWorld.StyleLine(sk) : "";
            Scouting = CoachAi.ScoutingHeadline(team, opp, next.Date); // H101 (run 112)
        }
        else if (MainViewModel.SeasonOverText(s) is { } over)
        {
            // H20 (run 72): out of the postseason, the card says why there's no game.
            Opponent = null; NextGame = $"{char.ToUpper(over[0])}{over[1..]}"; Scouting = "";
            NextDetail = "The rest of the playoffs go on without you. Continue or Sim week moves on to the summer.";
        }
        else if (MainViewModel.CollegeSpring(s))
        {
            // H77 (bug hunt Nov 17, run 97): a college coach's spring has no games; say why, and how to move on.
            // Run 107: one button for the whole wait (the top bar's "Skip to the summer").
            Opponent = null; NextGame = "Season over"; Scouting = "";
            NextDetail = $"Your college season is done. The summer starts when the pro playoffs end, in mid-May: {MainViewModel.SkipToSummerText} takes you there.";
        }
        else { Opponent = null; NextGame = "No game scheduled"; NextDetail = s.Phase == SessionPhase.Offseason ? "It's the offseason." : ""; Scouting = ""; }

        Table.Clear();
        int i = 0;
        // H81 (run 106): before conference play every conference record is 0-0, so the table read alphabetically. Until
        // then it's ordered (and shown) by overall record, and the title says so.
        if (s.InCollege && conf.Count > 0 && conf.All(r => r.Games == 0))
        {
            conf = conf.Select(r => s.CollegeRecord(r.Team)).OrderByDescending(r => r.WinPct).ThenByDescending(r => r.Wins).ThenByDescending(r => r.PointDiff).ToList();
            TableNote = "Overall records until conference play";
        }
        foreach (var r in conf)
        {
            i++;
            if (i > 8 && r.Team != team) continue;
            Table.Add(new TableRow { Rank = i, Team = r.Team, Label = s.InCollege ? r.Team.City : null, Record = $"{r.Wins}-{r.Losses}", Diff = $"{(r.PointDiff >= 0 ? "+" : "")}{r.PointDiff:0.0}", Ours = r.Team == team && s.Employed });
        }

        string chem = team.Chemistry >= 75 ? "excellent" : team.Chemistry >= 62 ? "good" : team.Chemistry >= 48 ? "average" : "poor";
        ChemistryText = $"Chemistry {chem} ({team.Chemistry:0})";
        Pulse.Clear();
        // H142 (run 133): each unhappy player's line says why (his biggest worry, as on the Squad page) and where to act.
        double winPct = rec.Games > 0 ? rec.WinPct : 0.5;
        foreach (var p in team.Roster.Where(p => p.TradeRequested || p.Morale < 45).OrderBy(p => p.Morale).Take(5))
        {
            Session.MyStats.TryGetValue(p.Id, out var pst);
            var worst = PeopleEngine.Factors(p, team, winPct, pst, Session.League.Finance, s.Today, Session.HumanLeague?.Profile.SalaryScale ?? 1).OrderBy(x => x.effect).FirstOrDefault();
            string why = worst.reason != null && worst.effect < 0 ? worst.reason : "";
            string mood = p.TradeRequested && !Session.InCollege ? "Wants a trade" : PeopleEngine.Mood(p.Morale); // no college transfers (Danny, Nov 22)
            Pulse.Add(new HomeRow { Icon = p.Morale < 38 ? "Frown" : "Meh", IconBrush = p.Morale < 38 ? Bad : Warn, Title = p.Name,
                Detail = mood + (why != "" ? $": {char.ToLowerInvariant(why[0])}{why[1..]}" : "") + " · talk to him on Squad" });
        }
        if (Pulse.Count == 0) Pulse.Add(new HomeRow { Icon = "Smile", IconBrush = Good, Title = "Everyone is content", Detail = "" });

        Injuries.Clear();
        foreach (var p in team.Roster.Where(p => !p.IsAvailable(s.Today)).OrderBy(p => p.Injury!.ReturnDate))
            Injuries.Add(new HomeRow { Icon = "Bandage", IconBrush = Bad, Title = p.Name, Detail = $"{p.Injury!.Name}, back {p.Injury.ReturnDate:MMM d}" });
        if (Injuries.Count == 0) Injuries.Add(new HomeRow { Icon = "Activity", IconBrush = Good, Title = "No injuries", Detail = "" });

        News.Clear();
        foreach (var m in s.Inbox.Take(6))
            News.Add(new HomeRow
            {
                Icon = m.Category switch { "Squad" => "HeartHandshake", "World" => "Globe", "Draft" => "ClipboardPen", "Roster" => "Users", "League" => "Trophy", "Board" => "Shield", _ => "Newspaper" },
                IconBrush = m.Read ? Muted : new SolidColorBrush(ThemeService.Accent), Title = m.Title, Detail = m.Date.ToString("MMM d"), Highlight = !m.Read,
                Badge = m.Category.ToUpperInvariant(), BadgeBrush = HomeRow.CategoryBrush(m.Category),
            });

        OnThisDay = s.League.Records.OnThisDay(s.Today).Take(3)
            .Select(m => $"{(m.YearsAgo <= 0 ? "Last year" : m.YearsAgo == 1 ? "1 year ago" : $"{m.YearsAgo} years ago")}, {m.Date:MMM d}: {m.Text}").ToList();

        // Form: running point differential over the season's games.
        double run = 0; var vals = new List<double>();
        foreach (var g in s.MyGames().Where(g => g.Played))
        {
            run += g.Us!.Value - g.Them!.Value; vals.Add(run);
        }
        Form = vals.Count > 1 ? new[] { new LineSeries { Name = "Point differential", Values = vals.ToArray(), Color = ThemeService.Accent, Emphasis = true } } : null;
        FormNote = vals.Count switch { 0 => "Fills in after your first game.", 1 => "Fills in after your second game.", _ => "" };
        FormLabels = vals.Count > 1 ? Enumerable.Range(1, vals.Count).Select(x => x % 10 == 0 ? x.ToString() : "").ToArray() : null;

        // Team vs league: points scored and allowed per game.
        var all = s.MyStandings.All().Where(r => r.Games > 0).ToList();
        var played = s.InCollege ? s.World.College!.Season.Games.Where(g => g.Played).ToList() : null;
        if (rec.Games > 0 && (all.Count > 0 || played is { Count: > 0 }))
        {
            double lg = played != null ? played.Average(g => (g.HomePts!.Value + g.AwayPts!.Value) / 2.0) : all.Average(r => (double)r.PointsFor / r.Games);
            var accent = ThemeService.Accent; var grey = Color.Parse("#4A5563");
            double allow = (double)rec.PointsAgainst / rec.Games;
            Comparison = new[]
            {
                new Bar { Label = "We score", Value = (double)rec.PointsFor / rec.Games, Color = accent },
                new Bar { Label = played != null ? "D-I avg" : "League avg", Value = lg, Color = grey },
                // H176 (run 155): allowing fewer than the average is good, so it's green then; red only above it.
                new Bar { Label = "We allow", Value = allow, Color = Color.Parse(allow <= lg ? "#3FB950" : "#F85149") },
            };
        }
        else Comparison = null;
        ComparisonNote = Comparison == null ? "Fills in after your first game." : "";

        if (s.InCollege)
        {
            // No wages in college: the roster by class instead, so the coach sees who graduates when (C2).
            // K22 (run 162): once the seniors have graduated and the new class has enrolled (the summer), the chart shows next
            // season's classes, as the "Graduation and enrollment" message does; it says so in its caption.
            int schoolYear = Hardwood.Core.College.CollegeWorld.SchoolYear(s.Today);
            bool nextSeason = s.Phase == SessionPhase.Offseason && s.Today.Month < 7;
            if (nextSeason) schoolYear++;
            var summer = Hardwood.Core.College.CollegeWorld.SummerAfter(schoolYear); // the same school year as the cards
            // By age next summer: 19 = freshmen ... 22 = seniors, who turn pro then (D80 A, run 128; CS3, H132). In a career from
            // before four-year college, last year's juniors are 23 next summer and graduate with the seniors: they count as seniors.
            string[] names = Hardwood.Core.College.CollegeWorld.ClassShort;
            int last = names.Length - 1;
            Payroll = names.Select((n, k) =>
            {
                int count = team.Roster.Count(p => Math.Clamp(p.Age(summer), 19, 19 + last) == 19 + k);
                return new Bar { Label = n, Value = count, ValueText = $"{count}", Color = k == last ? ThemeService.Accent : Color.Parse("#4A5563") };
            }).ToList();
            PayrollLines = null;
            PayrollTitle = "Roster by class"; PayrollIcon = "GraduationCap";
            PayrollCaption = nextSeason ? $"Next season ({schoolYear}-{(schoolYear + 1) % 100:00}). Seniors: final season." // K22
                : "Enroll at 18, turn pro at 22. Seniors: final season."; // H138 (run 133): one line, so it isn't cut at 1480×900
        }
        else
        {
        PayrollTitle = "Payroll committed"; PayrollIcon = "DollarSign"; PayrollCaption = "";
        // Payroll committed for this and the next three seasons.
        var fin = s.League.Finance;
        var bars = new List<Bar>();
        // H25: once the summer has counted contracts down, a deal's years left start from next season, so the bars do too
        // (before, the first bar was labelled with the season just played but left out its expiring deals).
        int from = s.League.Season + (SummerCounted(s, team) ? 1 : 0);
        for (int k = 0; k < 4; k++)
        {
            // A contract with N years left covers this season and the next N-1.
            long sum = team.Roster.Where(p => p.Contract is { } c && c.Years > k).Sum(p => p.Contract!.Salary);
            bars.Add(new Bar { Label = $"{from + k}-{(from + k + 1) % 100:00}", Value = sum / 1e6,
                ValueText = sum >= 10_000_000 ? $"${sum / 1e6:0}M" : $"${sum / 1e6:0.0}M", Color = k == 0 ? ThemeService.Accent : Color.Parse("#4A5563") });
        }
        Payroll = bars;
        // Cap and tax lines are NABL rules; clubs abroad just see their wage bill.
        PayrollLines = fin.OpenMarket || s.Abroad
            ? new[] { new RefLine { Label = "Budget", Value = team.Money.WageBudget / 1e6, Color = Color.Parse("#58A6FF") } }
            : new[]
            {
                new RefLine { Label = "Cap", Value = fin.SalaryCap / 1e6, Color = Color.Parse("#58A6FF") },
                new RefLine { Label = "Tax", Value = fin.TaxLine / 1e6, Color = Color.Parse("#F85149") },
            };
        }

        HasMood = s.MoodHistory.Count >= 2;
        Mood = HasMood ? new[]
        {
            new LineSeries { Name = "Chemistry", Values = s.MoodHistory.Select(x => x.Chemistry).ToArray(), Color = ThemeService.Accent, Emphasis = true },
            new LineSeries { Name = "Average morale", Values = s.MoodHistory.Select(x => x.Morale).ToArray(), Color = Color.Parse("#58A6FF") },
        } : null;
        MoodLabels = HasMood ? s.MoodHistory.Select((x, idx) => idx % 4 == 0 ? x.Date.ToString("MMM d") : "").ToArray() : null;
        RefreshJobs(s);
    }

    /// <summary>C5 part 1: a college game's rivalry (with the series so far) and a ranked matchup, as one line.</summary>
    public static string CollegeGameTag(GameSession s, Hardwood.Core.Model.Team us, MyGame g)
    {
        if (!s.InCollege || s.World.College is not { } cw) return "";
        var parts = new List<string>();
        if (g.Rivalry is { } rv)
            parts.Add(cw.SeriesLine(us.Id, g.Opponent.Id) is { } line ? $"{rv.Title} · {line}" : $"{rv.Title} · first meeting in this career");
        if (Hardwood.Core.College.CollegeWorld.RankedMatchup(g.OurRank, g.OpponentRank) is { } ranked) parts.Add($"Ranked matchup: {ranked}");
        return string.Join(" · ", parts);
    }

    /// <summary>C5 part 2: the home court a college game is played on (none for neutral-court events).</summary>
    public static string CollegeCourtLine(GameSession s, Hardwood.Core.Model.Team us, MyGame g)
    {
        if (!s.InCollege || s.World.College is not { } cw || g.Neutral) return "";
        return cw.HomeCourtLine(g.Home ? us.Id : g.Opponent.Id);
    }

    /// <summary>H15b (run 93): out of work, the hero and the middle column are about finding the next job.</summary>
    private void RefreshJobs(GameSession s)
    {
        Jobs.Clear();
        if (s.Employed) { JobsNote = ""; return; }
        var coach = s.Coach;
        Headline = "Out of work";
        var last = coach.History.LastOrDefault();
        Subline = (last != null ? $"Last job: {last.TeamName} ({last.Ended.ToLower()}, {Hardwood.Core.Jobs.JobRecords.Seasons(last)}) · " : "")
            + $"reputation {coach.Reputation:0}/100";
        ChemistryText = "";
        Opponent = null; Scouting = ""; GameTag = ""; CourtLine = ""; StyleLine = "";
        CardTitle = "OPEN JOBS"; CardIcon = "Shield";
        var live = s.LiveVacancies.Select(v => (v, t: s.FindTeam(v.TeamId))).Where(x => x.t != null).ToList();
        int offers = live.Count(x => x.v.Status == "offered"), applied = live.Count(x => x.v.Status == "applied");
        NextGame = offers > 0 ? $"{offers} offer{(offers == 1 ? "" : "s")} waiting"
            : live.Count == 0 ? "No open jobs yet" : $"{live.Count} open job{(live.Count == 1 ? "" : "s")}";
        NextDetail = offers > 0 ? "Accept or decline on the Board page."
            : applied > 0 ? $"{applied} application{(applied == 1 ? "" : "s")} waiting for an answer."
            : live.Count == 0 ? "Clubs post openings when they change coach." : "Apply on the Board page.";
        foreach (var (v, t) in live
                     .OrderBy(x => x.v.Status switch { "offered" => 0, "applied" => 1, _ => 2 })
                     .ThenByDescending(x => s.HireChanceFor(x.t!))
                     .Take(6))
        {
            double chance = s.HireChanceFor(t!); // H167
            Jobs.Add(new HomeRow
            {
                Icon = v.Status == "offered" ? "Handshake" : v.Status == "applied" ? "ClipboardPen" : "Shield",
                IconBrush = v.Status == "offered" ? Good : v.Status == "applied" ? Warn : Muted,
                Title = t!.FullName, Highlight = v.Status == "offered",
                // What to do first, then where: league names run long ("National Basketball Association").
                Detail = v.Status switch
                {
                    "offered" => "Offer! Answer on the Board page",
                    "applied" => s.Phase == Hardwood.Core.Season.SessionPhase.Offseason ? "Applied · they answer at the next step of the summer" : $"Applied · decision by {v.DecideOn:MMM d}",
                    _ => chance >= 0.6 ? "Good chance" : chance >= 0.3 ? "Worth a try" : "Long shot",
                } + $" · {s.ContextOf(t).Name}",
            });
        }
        JobsNote = live.Count > Jobs.Count ? $"and {live.Count - Jobs.Count} more on the Board page"
            : live.Count == 0 ? "None yet. Keep pressing Continue: clubs that sack their coach post the job, and college programs post theirs at the end of the season."
            : "Keep pressing Continue while you wait: the computer runs every team.";
    }

    private static string Ordinal(int n) => n + (n % 100 is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });

    [RelayCommand] private void Go(string page) => Shell.Navigate(page);
    [RelayCommand] private void Studio() => Shell.ShowStudio();
    /// <summary>College → March Madness (tab 2).</summary>
    [RelayCommand]
    private void OpenBracket()
    {
        Shell.Navigate("College");
        if (Shell.CurrentPage is CollegeViewModel college) college.Tab = 2;
    }
    public bool HasOpponent => Opponent != null;
}

public static class HomeConverters
{
    public static readonly Avalonia.Data.Converters.IValueConverter Highlight =
        new Avalonia.Data.Converters.FuncValueConverter<bool, IBrush>(b => b ? new SolidColorBrush(Color.FromArgb(40, ThemeService.Accent.R, ThemeService.Accent.G, ThemeService.Accent.B)) : Brushes.Transparent);
    public static readonly Avalonia.Data.Converters.IValueConverter Bold =
        new Avalonia.Data.Converters.FuncValueConverter<bool, FontWeight>(b => b ? FontWeight.SemiBold : FontWeight.Normal);
}
