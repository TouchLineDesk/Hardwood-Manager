using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hardwood.Core.Engine;
using Hardwood.Core.Model;
using Hardwood.Core.Season;

namespace Hardwood.Desktop.ViewModels;

public sealed class TeamPlayerRow
{
    public required Player Player { get; init; }
    public string Pos => Player.PrimaryPosition.ToString();
    public int Age { get; init; }
    public string CA { get; init; } = "";
    public string PA { get; init; } = "";
    public string Contract { get; init; } = "";
    public string Nat => Player.Nationality;
    public string Status { get; init; } = "";
    public int G { get; init; }
    public string Mpg { get; init; } = "";
    public string Ppg { get; init; } = "";
    public string Rpg { get; init; } = "";
    public string Apg { get; init; } = "";
    public string Spg { get; init; } = "";
    public string Bpg { get; init; } = "";
    public string Fg { get; init; } = "";
    public string Tp { get; init; } = "";
    /// <summary>Run 70 (review #13 rank 8): the collector card's inputs: the club (its colours and crest), what your
    /// scouts know of him, and his line this season in this club's league.</summary>
    public Team? Club { get; init; }
    public double Knowledge { get; init; } = 1.0;
    public string CardFooter { get; init; } = "";
    /// <summary>Set by the page so a card's double-click opens his page.</summary>
    public IRelayCommand? Open { get; init; }
}

public sealed class TeamGameRow
{
    public string Date { get; init; } = "";
    public string Competition { get; init; } = "";
    public string Where { get; init; } = "";
    public required Team Opponent { get; init; }
    public string Result { get; init; } = "";
    public GameResult? Box { get; init; }
    public bool HasBox => Box != null;
    public IRelayCommand? Open { get; init; }
}

/// <summary>Slice 8 (run 199): a retired jersey in a program's rafters.</summary>
public sealed record RaftersRow(string Number, string Name, string Years, string Why);
/// <summary>A program Hall of Fame member.</summary>
public sealed record HallOfFameRow(string Class, string Name, string Years, string Line, string Why, bool Jersey);
/// <summary>A program record-book column ("Career points"), top five.</summary>
public sealed record LegacyTable(string Title, IReadOnlyList<LegacyRecordRow> Rows);
public sealed record LegacyRecordRow(int Rank, string Value, string Name, string When, bool Current);

/// <summary>Any team's page (NABL, abroad, college): roster as your scouts see it, season stats, games and the club.</summary>
public sealed partial class TeamViewModel : PageViewModel
{
    public Team Team { get; }
    public override string Title => Team.FullName;
    [ObservableProperty] private string _league = "";
    [ObservableProperty] private string _record = "";
    [ObservableProperty] private string _club = "";
    [ObservableProperty] private string _money = "";
    /// <summary>Roadmap 1: the club at a glance as stat chips (record, coach, owner, arena, money).</summary>
    [ObservableProperty] private List<Hardwood.Desktop.Controls.Chip> _chips = new();
    /// <summary>K12b (run 137): a college program's banners in words ("National champions 1996, 1998, 2012 …"); empty otherwise.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasBanners))] private string _banners = "";
    public bool HasBanners => Banners.Length > 0;
    // Slice 8 (run 199): a college program's legacy tab (the rafters, the Hall of Fame and the record book).
    [ObservableProperty] private bool _hasLegacy;
    [ObservableProperty] private string _legacyNote = "";
    [ObservableProperty] private List<RaftersRow> _rafters = new();
    [ObservableProperty] private List<HallOfFameRow> _hall = new();
    [ObservableProperty] private List<LegacyTable> _legacyTables = new();
    public bool HasRafters => Rafters.Count > 0;
    public bool HasHall => Hall.Count > 0;
    partial void OnRaftersChanged(List<RaftersRow> value) => OnPropertyChanged(nameof(HasRafters));
    partial void OnHallChanged(List<HallOfFameRow> value) => OnPropertyChanged(nameof(HasHall));
    public bool HasLegacyTables => LegacyTables.Count > 0;
    partial void OnLegacyTablesChanged(List<LegacyTable> value) => OnPropertyChanged(nameof(HasLegacyTables));
    public Avalonia.Media.Color Accent => ThemeService.AccentFor(Team);

    /// <summary>From Danny's graphics pack (#350): the club's banner behind the header, and its jerseys (home first).</summary>
    public Avalonia.Media.Imaging.Bitmap? Banner => Graphics.GraphicsPackService.BannerFor(Team);
    public IReadOnlyList<Avalonia.Media.Imaging.Bitmap> Jerseys => Graphics.GraphicsPackService.JerseysFor(Team);
    public bool HasJerseys => Jerseys.Count > 0;
    public ObservableCollection<TeamPlayerRow> Players { get; } = new();
    public ObservableCollection<TeamGameRow> Games { get; } = new();
    [ObservableProperty] private TeamPlayerRow? _selectedPlayer;

    // Run 70 (review #13 rank 8): another club's roster opens on collector cards in the club's colours, as your own
    // Roster does; the table (and its column views) is one click away.
    public string[] RosterViews { get; } = { "Cards", "Full table" };
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowRosterCards), nameof(ShowRosterTable))] private string _rosterView = "Cards";
    public bool ShowRosterCards => RosterView == "Cards";
    public bool ShowRosterTable => !ShowRosterCards;
    [ObservableProperty] private string _rosterNote = "";
    public bool IsYours => Team == Session.HumanTeam;

    public TeamViewModel(MainViewModel shell, Team team) : base(shell) => Team = team;

    [RelayCommand] private void Back() => Shell.GoBack();
    [RelayCommand] private void OpenPlayer() { if (SelectedPlayer is { } r) Shell.ShowPlayer(r.Player); }

    public override void Refresh()
    {
        var s = Session;
        var t = Team;
        var home = LeagueBook.HomeOf(s, t);
        var books = LeagueBook.Of(s, t).ToList();
        League = home == null ? "" : $"{home.Name} · {home.Season} · {home.Status}";
        if (home != null)
        {
            var table = home.Table();
            var mine = table.FirstOrDefault(r => r.Team == t);
            var group = mine == null ? new List<BookTableRow>() : table.Where(r => r.Group == mine.Group).ToList();
            int rank = mine == null ? 0 : group.IndexOf(mine) + 1;
            Record = mine == null ? "" : $"{mine.W}-{mine.L} · {Ordinal(rank)} in {(mine.Group.Length > 0 ? mine.Group : home.Name)}" + (mine.Zone.Length > 0 && !mine.Zone.Contains("conf") ? $" · {mine.Zone}" : "");
        }
        var coach = t.Coach?.Name ?? (t.CoachName.Length > 0 ? t.CoachName : "-");
        // K14 (run 135, hunt 19): a college program answers to its athletic director, not an owner.
        bool program = s.IsCollegeProgram(t);
        string boss = program ? "Athletic director" : "Owner";
        Club = $"Coach: {coach} · {boss}: {(t.Owner.Name.Length > 0 ? t.Owner.Name + ", " : "")}{t.Owner.Style.ToLowerInvariant()} · Arena: {t.ArenaCapacity:N0}";
        Money = t.Money.Ready ? $"Revenue ${t.Money.Revenue / 1e6:F1}M · Wage budget ${t.Money.WageBudget / 1e6:F1}M · Payroll ${t.Roster.Sum(p => p.Contract?.Salary ?? 0) / 1e6:F1}M" : "";
        var chips = new List<Hardwood.Desktop.Controls.Chip>();
        if (home != null)
        {
            var mine = home.Table().FirstOrDefault(r => r.Team == t);
            if (mine != null)
            {
                int gp = mine.W + mine.L;
                chips.Add(new("Record", $"{mine.W}-{mine.L}", Record.Contains(" · ") ? Record[(Record.IndexOf(" · ") + 3)..] : "",
                    gp == 0 ? "team" : mine.W * 2 > gp ? "good" : mine.W * 2 < gp ? "bad" : "team"));
            }
        }
        chips.Add(new("Coach", coach, t.Coach is { } hc ? $"reputation {hc.Reputation:0}" : ""));
        chips.Add(new(boss, t.Owner.Name.Length > 0 ? t.Owner.Name : "-", t.Owner.Style.ToLowerInvariant()));
        chips.Add(new("Arena", t.ArenaCapacity.ToString("N0"), "seats"));
        // K12b (run 137): a college program's banners: its titles, Final Fours and NCAA tournaments (real history in a real
        // world, plus this career's), and the years under the header.
        Banners = "";
        if (program && s.World.College is { } cw)
        {
            var past = cw.PastOf(t);
            var (v, sub) = past.Chip();
            chips.Add(new("Banners", v, sub, past.Titles > 0 ? "good" : ""));
            Banners = past.Line(cw.FirstListedYear);
        }
        BuildLegacy(program ? s.World.College : null, t);
        if (t.Money.Ready)
        {
            long payroll = t.Roster.Sum(p => p.Contract?.Salary ?? 0);
            chips.Add(new("Revenue", PlayerViewModel.Money(t.Money.Revenue), "a season"));
            chips.Add(new("Payroll", PlayerViewModel.Money(payroll), $"budget {PlayerViewModel.Money(t.Money.WageBudget)}",
                payroll > t.Money.WageBudget ? "bad" : payroll > t.Money.WageBudget * 0.95 ? "warn" : "good"));
        }
        Chips = chips;

        // Roster: what your scouts know (everything about your own players).
        var stats = (home?.Stats() ?? new()).Where(x => x.TeamId == t.Id).ToDictionary(x => x.PlayerId);
        var today = s.Today;
        Players.Clear();
        bool prospect = s.World.College?.Programs.Contains(t) == true;
        foreach (var p in t.Roster.OrderByDescending(p => stats.TryGetValue(p.Id, out var st) ? st.Min : 0).ThenByDescending(p => p.CurrentAbility))
        {
            double k = Scouting.Knowledge(s.HumanTeam, p, prospect: prospect);
            stats.TryGetValue(p.Id, out var st);
            var player = p;
            Players.Add(new TeamPlayerRow
            {
                Club = t, Knowledge = k, CardFooter = CardLine(st, p, today), Open = new RelayCommand(() => Shell.ShowPlayer(player)),
                Player = p, Age = p.Age(today), CA = Scouting.Ability(p, k).ToString(), PA = (prospect ? s.CollegePotentialOf(p)?.ToString() : null) ?? Scouting.Potential(p, k).ToString(), // CP3: a range in college
                Contract = p.Contract is { } c ? $"${c.Salary / 1e6:F2}M × {c.Years}y" : "-",
                Status = p.OutShort(today), // the injury itself is on his page (too long for this column at 1280 px)
                G = st?.G ?? 0, Mpg = F(st?.Mpg), Ppg = F(st?.Ppg), Rpg = F(st?.Rpg), Apg = F(st?.Apg), Spg = F(st?.Spg), Bpg = F(st?.Bpg),
                Fg = st is { Fga: > 0 } ? st.FgPct.ToString(".000") : "-", Tp = st is { Tpa: > 0 } ? st.TpPct.ToString(".000") : "-",
            });
        }

        int injured = t.Roster.Count(p => p.IsHurt(today));
        RosterNote = $"{t.Roster.Count} players, most minutes first" + (injured > 0 ? $" · {injured} injured" : "")
            + (IsYours ? "" : " · ability as your scouts believe") + ". Click to flip, double-click to open.";

        // Games in every competition this season.
        Games.Clear();
        foreach (var (book, g) in books.SelectMany(b => b.Games().Where(g => g.Involves(t)).Select(g => (b, g))).OrderBy(x => x.g.Date))
        {
            bool atHome = g.Home == t;
            var opp = atHome ? g.Away : g.Home;
            string result = "";
            if (g.Played)
            {
                int us = atHome ? g.HomePts!.Value : g.AwayPts!.Value, them = atHome ? g.AwayPts!.Value : g.HomePts!.Value;
                result = $"{(us > them ? "W" : "L")} {us}-{them}";
            }
            var box = g.Box;
            Games.Add(new TeamGameRow
            {
                Date = g.Date == DateOnly.MinValue ? "" : g.Date.ToString("ddd MMM d"), Competition = g.Stage == "Regular season" || g.Stage == CompetitionSeasonStage ? book.Name : $"{book.Name} · {g.Stage}",
                Where = atHome ? "vs" : "at", Opponent = opp, Result = result, Box = box,
                Open = box == null ? null : new RelayCommand(() => Shell.ShowBoxScore(box)),
            });
        }
    }

    private const string CompetitionSeasonStage = Hardwood.Core.World.CompetitionSeason.RegularSeason;
    /// <summary>The card's foot (no minutes: they ran under the Flip mark at 180 px): his line this season in this club's league, else his contract, else his age.</summary>
    public static string CardLine(BookStat? st, Player p, DateOnly today)
        => st is { G: > 0 } ? $"{st.Ppg:F1} pts · {st.Rpg:F1} reb · {st.Apg:F1} ast"
         : p.Contract is { } c ? $"{Hardwood.Core.Season.Budgets.Money(c.Salary)} · {(c.Years <= 0 ? "expiring" : c.Years + " yr")}" : $"Age {p.Age(today)}";

    private static string F(double? v) => v is { } x ? x.ToString("0.0") : "-";
    private static string Ordinal(int n) => n + (n % 100 is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });

    /// <summary>Slice 8 (run 199): the program's legacy: its rafters, Hall of Fame and record book (this career's seasons).</summary>
    private void BuildLegacy(Hardwood.Core.College.CollegeWorld? cw, Team t)
    {
        HasLegacy = cw != null;
        if (cw == null) { Rafters = new(); Hall = new(); LegacyTables = new(); LegacyNote = ""; return; }
        var book = cw.LegacyOf(t.Id);
        Rafters = book.Rafters.Select(h => new RaftersRow(h.Jersey!.Value.ToString(), h.Name, h.Years, string.Join(" · ", h.Why.Take(2)))).ToList();
        Hall = book.Hall.OrderByDescending(h => h.Class).ThenByDescending(h => h.Score)
            .Select(h => new HallOfFameRow($"Class of {h.Class}", $"{h.Name} ({h.Pos})", h.Years, h.Line, string.Join(" · ", h.Why.Take(4)), h.Jersey != null)).ToList();
        var tables = new List<LegacyTable>();
        LegacyTable Career(string title, Func<Hardwood.Core.College.LegacyCareer, int> of) =>
            new(title, book.Leaders(of).Select((c, i) => new LegacyRecordRow(i + 1, of(c).ToString("N0"), c.Name, c.Done ? c.Years : $"{Hardwood.Core.College.CollegeAwards.SeasonText(c.First)} on · still playing", !c.Done)).ToList());
        LegacyTable Single(string title, Func<Hardwood.Core.College.LegacySeason, int> of) =>
            new(title, book.BestSeasons(of).Select((x, i) => new LegacyRecordRow(i + 1, of(x).ToString("N0"), x.Name, $"{Hardwood.Core.College.CollegeAwards.SeasonText(x.Year)} · {x.G} games", false)).ToList());
        tables.Add(Career("Career points", c => c.Pts));
        tables.Add(Career("Career rebounds", c => c.Reb));
        tables.Add(Career("Career assists", c => c.Ast));
        tables.Add(Single("Points in a season", x => x.Pts));
        tables.Add(Single("Rebounds in a season", x => x.Reb));
        tables.Add(Single("Assists in a season", x => x.Ast));
        // The team's best seasons by record (the season honours keep every program's record).
        var best = cw.Awards.Where(a => (book.FirstYear == 0 || a.Year >= book.FirstYear) && a.Records.TryGetValue(t.Id, out var r) && r.Length >= 3 && r[0] + r[1] > 0)
            .Select(a => (a.Year, R: a.Records[t.Id])).OrderByDescending(x => x.R[0] - x.R[1]).ThenByDescending(x => x.R[0]).ThenBy(x => x.Year).Take(5).ToList();
        tables.Add(new LegacyTable("Best seasons", best.Select((b, i) => new LegacyRecordRow(i + 1, $"{b.R[0]}-{b.R[1]}", Hardwood.Core.College.CollegeAwards.SeasonText(b.Year),
            b.R[2] >= 1 ? Hardwood.Core.College.CollegeAwardsText.Reached(b.R[2]) : b.R[2] == 0 ? "the First Four" : "", false)).ToList()));
        LegacyTables = tables.Where(x => x.Rows.Count > 0).ToList();
        LegacyNote = book.LastYear == 0
            ? "The record book opens the night of this career's first national final: every season's totals count from then on, and each senior's career is judged as it ends. The best join the program's Hall of Fame; the very best have their jersey retired."
            : $"This career's seasons ({Hardwood.Core.College.ProgramLegacy.Span(book.FirstYear, book.LastYear)}). A senior joins the Hall of Fame on the night of his last national final if his honours, his team's runs and his totals add up; the very best have their jersey retired."
              + (Hall.Count == 0 ? " No one has been inducted yet." : "");
    }
}
