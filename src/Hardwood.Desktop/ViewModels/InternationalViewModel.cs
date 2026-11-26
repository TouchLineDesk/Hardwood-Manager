using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hardwood.Core.International;
using Hardwood.Core.Model;
using Hardwood.Core.Season;

namespace Hardwood.Desktop.ViewModels;

/// <summary>One national team in the ranking.</summary>
public sealed class NationRow
{
    public required NationalTeam Team { get; init; }
    public string Rank => Team.Rank > 0 ? Team.Rank.ToString() : "–";
    public string Code => Team.Nation.Code;
    public string Name => Team.Name;
    public string Zone => NationalTeams.ZoneName(Team.Nation.Zone);
    public string Strength => Team.Strength.ToString("F1");
    /// <summary>I3 (run 210): FIBA ranking points, and the move since the last window or tournament.</summary>
    public string Points => Team.Rank > 0 ? Team.Points.ToString("F0") : "–";
    public string Move { get; init; } = "";
    public int Pool => Team.Pool.Count;
    public string Star => Team.Squad.Count > 0 ? Team.Squad[0].Player.Name : "";
    public string Tip => $"{Name}, {Zone} · best player {Star} · {Pool} eligible players · strength {Strength}" + (Move == "" ? "" : $" ·{Move}");
    public bool Mine { get; init; }
}

/// <summary>One player in the selected nation's twelve, as your scouts see him.</summary>
public sealed class NationPlayerRow
{
    public int No { get; init; }
    public required Player Player { get; init; }
    public Team? Club { get; init; }
    public string ClubText { get; init; } = "";
    public bool HasClub => Club != null;
    public bool NoClub => Club == null;
    public string Pos => Player.PrimaryPosition.ToString();
    public int Age { get; init; }
    public string Ability { get; init; } = "";
    /// <summary>"Philadelphia 76ers · SG · 25".</summary>
    public string Line => $"{ClubText} · {Pos} · {Age}";
    public bool Mine { get; init; }
}

/// <summary>I6a (run 214): a federation's offer of its head coach's job.</summary>
public sealed class NationalOfferRow
{
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
    public required IRelayCommand Accept { get; init; }
    public required IRelayCommand Decline { get; init; }
}

/// <summary>I6a (run 214): one player in your nation's pool, with the button that puts him in your twelve or takes him out.</summary>
public sealed class NationPoolRow
{
    public required Player Player { get; init; }
    public Team? Club { get; init; }
    public bool HasClub => Club != null;
    public string Line { get; init; } = "";
    public string Ability { get; init; } = "";
    public bool Picked { get; init; }
    public bool CanPick { get; init; }
    public string ButtonText => Picked ? "Drop" : "Pick";
    public string ButtonTip => Picked ? "Take him out of your twelve" : CanPick ? "Put him in your twelve" : "Your twelve is full: drop someone first";
    public bool ButtonEnabled => Picked || CanPick;
    public required IRelayCommand Toggle { get; init; }
}

public sealed class FibaEventRow
{
    public required FibaEvent Event { get; init; }
    public string Name => Event.Name;
    public string Dates => Event.Dates;
    public string Where => Event.Where;
    public string Note => Event.Note;
    public string When => $"{Event.Dates} · {Event.Where}";
    public bool Major => Event.Kind is FibaEventKind.WorldCup or FibaEventKind.Olympics;
}

/// <summary>
/// International plan I1 (run 207): national teams picked from the whole world and ranked, the selected nation's twelve, and the
/// FIBA calendar. Read-only: <see cref="NationalTeams.Build(GameSession)"/> draws nothing and writes nothing.
/// </summary>
public sealed partial class InternationalViewModel : PageViewModel
{
    public override string Title => "International";
    public List<string> Zones { get; } = new() { "All zones", "Africa", "Americas", "Asia-Pacific", "Europe" };
    [ObservableProperty] private string _zone = "All zones";
    public ObservableCollection<NationRow> Nations { get; } = new();
    [ObservableProperty] private NationRow? _selected;
    public ObservableCollection<NationPlayerRow> Squad { get; } = new();
    public ObservableCollection<FibaEventRow> Calendar { get; } = new();
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _squadTitle = "";
    [ObservableProperty] private string _squadNote = "";
    [ObservableProperty] private string _yours = "";

    // I2 (run 209): the latest World Cup played, read from the saved history.
    [ObservableProperty] private bool _hasCup;
    [ObservableProperty] private string _cupTitle = "";
    [ObservableProperty] private string _cupMedals = "";
    [ObservableProperty] private string _cupMvp = "";
    [ObservableProperty] private string _cupFive = "";
    public ObservableCollection<string> CupKnockouts { get; } = new();
    public ObservableCollection<string> CupGroups { get; } = new();
    public ObservableCollection<string> CupScorers { get; } = new();

    // I4 (run 211): every tournament played (newest first) and the medal table of the World Cups and Olympics.
    [ObservableProperty] private bool _hasHonours;
    [ObservableProperty] private string _medalNote = "";
    public ObservableCollection<string> Tournaments { get; } = new();
    public ObservableCollection<string> Medals { get; } = new();

    // I3 (run 210): the current qualifying cycle's groups and the latest window.
    [ObservableProperty] private bool _hasQualifying;
    [ObservableProperty] private string _qualTitle = "";
    [ObservableProperty] private string _qualNote = "";
    public ObservableCollection<string> QualGroups { get; } = new();

    // I6a (run 214): the national-team job: offers, your nation, and your twelve.
    [ObservableProperty] private string _jobTitle = "";
    [ObservableProperty] private string _jobNote = "";
    [ObservableProperty] private string _jobStatus = "";
    [ObservableProperty] private bool _coachingNation;
    [ObservableProperty] private bool _editingMine;
    [ObservableProperty] private string _poolNote = "";
    public ObservableCollection<NationalOfferRow> Offers { get; } = new();
    public ObservableCollection<NationPoolRow> Pool { get; } = new();
    /// <summary>How many of the pool the picker lists (the best by ability, plus every pick).</summary>
    public const int PoolShown = 30;

    private IReadOnlyList<NationalTeam> _teams = Array.Empty<NationalTeam>();

    public InternationalViewModel(MainViewModel shell) : base(shell) { }

    public override void Refresh()
    {
        var s = Session;
        _teams = NationalTeams.Ranked(s);
        int ranked = _teams.Count(t => t.Rank > 0);
        var hist = s.World.International;
        string basis = hist.Ranking.Count == 0
            ? "the FIBA world ranking (seeded from the real ranking and each twelve's strength; results move it from the first window)"
            : $"the FIBA world ranking, from results ({hist.Windows.Count} window{(hist.Windows.Count == 1 ? "" : "s")}, {Plural(hist.WorldCups.Count + hist.Olympics.Count + hist.Continentals.Count, "tournament")})";
        Summary = $"{ranked} national teams in {basis} · a country needs {NationalTeams.MinPool} players aged {NationalTeams.MinAge}+ anywhere in the world"
                  + Suspended(_teams.Where(t => t.Rank == 0).Select(t => t.Name).ToList());

        var mine = s.Employed ? s.HumanTeam.Roster.Select(p => p.Id).ToHashSet() : new HashSet<int>();
        var picked = _teams.SelectMany(t => t.Squad.Where(x => mine.Contains(x.Player.Id)).Select(x => (t, x))).ToList();
        Yours = !s.Employed ? ""
            : picked.Count == 0 ? "None of your players is in a national twelve."
            : $"In a national twelve: " + string.Join(", ", picked.Select(z => $"{z.x.Player.Name} ({z.t.Nation.Code})")) + ".";

        FillJob();

        Calendar.Clear();
        foreach (var e in FibaCalendar.From(s.Today, 3)) Calendar.Add(new FibaEventRow { Event = e });
        FillCup(hist.Majors.LastOrDefault());
        FillHonours(hist);
        FillQualifying(hist);

        var keep = Selected?.Team.Name;
        Fill();
        Selected = Nations.FirstOrDefault(n => n.Name == keep)
                   ?? Nations.FirstOrDefault(n => n.Code == s.NationalJobState.Code)
                   ?? Nations.FirstOrDefault(n => n.Mine) ?? Nations.FirstOrDefault();
    }

    /// <summary>I6a (run 214): the job panel: the nation you coach (and the Resign and Reset buttons), the open offers, or who
    /// would call a coach of your reputation.</summary>
    private void FillJob()
    {
        var s = Session;
        var job = s.NationalJobState;
        Offers.Clear();
        CoachingNation = job.Code != null;
        if (job.Code != null)
        {
            var t = _teams.FirstOrDefault(x => x.Nation.Code == job.Code);
            string rank = t is { Rank: > 0 } ? $"No. {t.Rank} in the world" : "unranked";
            JobTitle = $"Head coach of {job.Name}";
            // I6b (run 215): you coach the window games yourself on a day your club is idle (your assistant on a club day).
            string next = s.MyNationalGameToday is { } f ? $" · today: {(f.Home == job.Code ? "v" : "at")} {(f.Home == job.Code ? f.Away : f.Home)} ({f.Group}), live when you press Continue" : "";
            string earned = job.ReputationEarned == 0 ? "" : $" · national results have {(job.ReputationEarned > 0 ? "added" : "cost")} {Math.Abs(job.ReputationEarned):0.0} reputation";
            string verdict = job.Verdicts.Count > 0 ? $"\nLast verdict: {job.Verdicts[^1]}." : "";
            JobNote = $"Since {job.Since:MMM d, yyyy} · {rank}, alongside your club job{next}{earned}. "
                      + "Your twelve (pick them in the nation's panel below) play the windows' qualifiers, which you coach live on days your club is idle "
                      + "(your assistant takes them on club days), and the summer tournaments, played in the full engine. A tournament far below the seeding can cost you the job."
                      + verdict;
            return;
        }
        foreach (var o in job.Offers)
        {
            string code = o.Code;
            Offers.Add(new NationalOfferRow
            {
                Title = $"{o.Name} (No. {o.Rank} in the world)",
                Detail = $"Offered {o.Made:MMM d} · answer by {o.Expires:MMM d} · coach them alongside " + (s.Employed ? "your club job" : "your next club job"),
                Accept = new RelayCommand(() => { JobStatus = s.AcceptNationalOffer(code) ? $"You're the head coach of {o.Name}." : "That offer is no longer open."; Refresh(); }),
                Decline = new RelayCommand(() => { s.DeclineNationalOffer(code); JobStatus = $"You turned down {o.Name}."; Refresh(); }),
            });
        }
        int best = NationalJob.BestRankFor(s.Coach.Reputation);
        JobTitle = Offers.Count > 0 ? "National team offers" : "National team job";
        string sacked = job.Past.LastOrDefault() is { Sacked: true } last ? $" {last.Name} let you go on {last.To:MMM d, yyyy}." : "";
        JobNote = Offers.Count > 0
            ? "A national team is coached alongside your club job: you pick its twelve for the windows and the summer tournaments."
            : $"No federation is waiting on you. Federations call once a year, from Oct 15, by reputation: at {s.Coach.Reputation:0}/100, "
              + $"nations ranked about {Ordinal(best)} and below would call. Win, and bigger nations will." + sacked;
    }

    [RelayCommand]
    private void ResignNation()
    {
        string name = Session.NationalJobState.Name;
        if (Session.ResignNationalJob()) JobStatus = $"You stepped down as head coach of {name}.";
        Refresh();
    }

    [RelayCommand]
    private void ResetPicks()
    {
        Session.ResetNationalPicks();
        JobStatus = "Your twelve is the federation's best twelve again.";
        Refresh();
    }

    internal static string Ordinal(int n) => n + ((n % 100) is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });

    partial void OnZoneChanged(string value)
    {
        var keep = Selected?.Team.Name;
        Fill();
        FillQualifying(Session.World.International);
        Selected = Nations.FirstOrDefault(n => n.Name == keep) ?? Nations.FirstOrDefault();
    }

    private void Fill()
    {
        Nations.Clear();
        var s = Session;
        var mine = s.Employed ? s.HumanTeam.Roster.Select(p => p.Id).ToHashSet() : new HashSet<int>();
        foreach (var t in _teams.Where(t => Zone == "All zones" || NationalTeams.ZoneName(t.Nation.Zone) == Zone))
            Nations.Add(new NationRow { Team = t, Mine = t.Squad.Any(x => mine.Contains(x.Player.Id)), Move = GameSession.Move(s.World.International, t) });
    }

    partial void OnSelectedChanged(NationRow? value)
    {
        Squad.Clear();
        FillPool(value);
        if (value == null) { SquadTitle = ""; SquadNote = ""; return; }
        var s = Session;
        var t = value.Team;
        var today = s.Today;
        bool employed = s.Employed;
        int no = 0;
        foreach (var x in t.Squad)
        {
            double k = employed ? Scouting.Knowledge(s.HumanTeam, x.Player, prospect: x.College) : 1.0;
            Squad.Add(new NationPlayerRow
            {
                No = ++no, Player = x.Player, Club = x.Club, Age = x.Age,
                ClubText = x.Club == null ? "Free agent" : x.College ? $"{x.Club.FullName}, college" : x.Club.FullName,
                Ability = Scouting.Ability(x.Player, k).ToString(),
                Mine = employed && x.Club == s.HumanTeam,
            });
        }
        string rank = t.Rank > 0 ? $"No. {t.Rank} in the world ({t.Points:F0} points), No. {t.ZoneRank} in {NationalTeams.ZoneName(t.Nation.Zone)}" : "suspended by FIBA";
        SquadTitle = $"{t.Name} ({t.Nation.Code}) · {rank}";
        int abroad = t.Squad.Count(x => x.Club != null && !PlaysAtHome(s, x.Club, x.College, t.Nation.Name));
        int colleges = t.Pool.Count(x => x.College);
        SquadNote = (t.CoachPicked ? $"Your twelve (you're the head coach) from {t.Pool.Count} eligible players" : $"The federation's best twelve from {t.Pool.Count} eligible players")
                    + (colleges > 0 ? $" ({colleges} in college)" : "")
                    + (abroad > 0 ? $" · {abroad} play abroad" : "") + $" · strength {t.Strength:F1}" + CupLine(t.Nation.Code) + " · ability as your scouts believe";
    }

    /// <summary>I6a (run 214): when the selected nation is yours, its pool (the best <see cref="PoolShown"/> and every pick) with
    /// Pick and Drop buttons.</summary>
    private void FillPool(NationRow? value)
    {
        Pool.Clear();
        var s = Session;
        var job = s.NationalJobState;
        EditingMine = value != null && job.Code != null && value.Code == job.Code;
        if (!EditingMine) { PoolNote = ""; return; }
        var t = value!.Team;
        var picks = job.Picks.ToHashSet();
        bool employed = s.Employed;
        int inPool = t.Pool.Count(x => picks.Contains(x.Player.Id));
        foreach (var x in t.Pool.Where((x, i) => i < PoolShown || picks.Contains(x.Player.Id)))
        {
            double k = employed ? Scouting.Knowledge(s.HumanTeam, x.Player, prospect: x.College) : 1.0;
            int id = x.Player.Id;
            string club = x.Club == null ? "Free agent" : x.College ? $"{x.Club.FullName}, college" : x.Club.FullName;
            Pool.Add(new NationPoolRow
            {
                Player = x.Player, Club = x.Club, Picked = picks.Contains(id), CanPick = inPool < NationalTeams.SquadSize,
                Line = $"{club} · {x.Player.PrimaryPosition} · {x.Age}" + (x.Player.Injury is { } inj && inj.ReturnDate > s.Today ? $" · injured until {inj.ReturnDate:MMM d}" : ""),
                Ability = Scouting.Ability(x.Player, k).ToString(),
                Toggle = new RelayCommand(() => { JobStatus = ""; s.ToggleNationalPick(id); Refresh(); }),
            });
        }
        int gone = job.Picks.Count - inPool;
        PoolNote = $"{inPool} of {NationalTeams.SquadSize} picked"
                   + (inPool < NationalTeams.SquadSize ? " · the federation's next best fill the empty places" : "")
                   + (gone > 0 ? $" · {gone} of your picks {(gone == 1 ? "is" : "are")} no longer eligible (their places are open)" : "")
                   + " · the best " + Math.Min(PoolShown, t.Pool.Count) + $" of {t.Pool.Count} shown, ability as your scouts believe";
    }

    /// <summary>I2 (run 209): the latest World Cup: medals, the knockouts, the group tables, the top scorers and the all-star five.</summary>
    private void FillCup(WorldCupRecord? rec)
    {
        CupKnockouts.Clear(); CupGroups.Clear(); CupScorers.Clear();
        HasCup = rec?.Final != null;
        if (rec?.Final == null) { CupTitle = CupMedals = CupMvp = CupFive = ""; return; }
        CupTitle = $"{rec.Title} · {rec.Host}";
        CupMedals = $"Gold {rec.NameOf(rec.Gold)} · Silver {rec.NameOf(rec.Silver)} · Bronze {rec.NameOf(rec.Bronze)}";
        CupMvp = rec.Mvp is { } m ? $"MVP: {m.Name} ({m.Code}), {m.Ppg:0.0} pts, {m.Rpg:0.0} reb, {m.Apg:0.0} ast a game" : "";
        CupFive = rec.AllStarFive.Count > 0 ? "All-star five: " + string.Join(", ", rec.AllStarFive.Select(p => $"{p.Name} ({p.Code})")) : "";
        foreach (var g in rec.Games.Where(g => g.Round is Tournament.RoundOf16 or WorldCup.QuarterRound or WorldCup.SemiRound or WorldCup.BronzeRound or WorldCup.FinalRound))
            CupKnockouts.Add($"{g.Round}, {g.Day}: {rec.NameOf(g.Home)} {g.HomePts}-{g.AwayPts} {rec.NameOf(g.Away)}");
        for (int i = 0; i < rec.Groups.Count; i++)
        {
            string round = WorldCup.GroupName(i);
            CupGroups.Add($"{round}: " + string.Join(", ", WorldCup.Table(rec, rec.Groups[i], g => g.Round == round)
                .Select(c => { var r = WorldCup.Record(rec, c, g => g.Round == round); return $"{c} {r.W}-{r.L}"; })));
        }
        int n = 0;
        foreach (var p in rec.TopScorers.Take(5)) CupScorers.Add($"{++n}. {p.Name} ({p.Code}) {p.Ppg:0.0} a game in {p.Games}");
    }

    /// <summary>I4 (run 211): one line for every tournament played, newest first, and the medal table (World Cups and Olympics:
    /// gold, then silver, then bronze).</summary>
    private void FillHonours(InternationalHistory h)
    {
        Tournaments.Clear(); Medals.Clear();
        var lines = new List<(int Year, int Order, string Text)>();
        foreach (var r in h.Majors.Where(r => r.Final != null))
            lines.Add((r.Year, 0, $"{r.Short}, {r.Host}: gold {r.NameOf(r.Gold)}, silver {r.NameOf(r.Silver)}, bronze {r.NameOf(r.Bronze)}"));
        foreach (var g in h.OlympicQualifiers.GroupBy(q => q.Year))
            lines.Add((g.Key, 1, $"Olympic qualifying {g.Key}: " + Joined(g.Select(q => $"{q.NameOf(q.Gold)} (in {q.Host})").ToList()) + " went through"));
        foreach (var r in h.Continentals.Where(r => r.Final != null))
            lines.Add((r.Year, 2, $"{r.Short}: gold {r.NameOf(r.Gold)}, silver {r.NameOf(r.Silver)}, bronze {r.NameOf(r.Bronze)} ({r.Teams.Count} teams)"));
        foreach (var r in h.U23Cups.Where(r => r.Final != null)) // I5 (run 213): a game event, not in the medal table
            lines.Add((r.Year, 3, $"{r.Short} (under-23s): gold {r.NameOf(r.Gold)}, silver {r.NameOf(r.Silver)}, bronze {r.NameOf(r.Bronze)}"));
        foreach (var l in lines.OrderByDescending(l => l.Year).ThenBy(l => l.Order)) Tournaments.Add(l.Text);
        var table = h.Majors.Where(r => r.Final != null)
            .SelectMany(r => new[] { (r.NameOf(r.Gold), 0), (r.NameOf(r.Silver), 1), (r.NameOf(r.Bronze), 2) })
            .Where(x => x.Item1 != "")
            .GroupBy(x => x.Item1)
            .Select(g => (Name: g.Key, G: g.Count(x => x.Item2 == 0), S: g.Count(x => x.Item2 == 1), B: g.Count(x => x.Item2 == 2)))
            .OrderByDescending(x => x.G).ThenByDescending(x => x.S).ThenByDescending(x => x.B).ThenBy(x => x.Name, StringComparer.Ordinal).ToList();
        foreach (var m in table.Take(10)) Medals.Add($"{m.Name}: {m.G} gold, {m.S} silver, {m.B} bronze");
        HasHonours = Tournaments.Count > 0;
        int majors = h.Majors.Count();
        MedalNote = majors == 0 ? ""
            : $"Medal table: {h.WorldCups.Count} World Cup{(h.WorldCups.Count == 1 ? "" : "s")} and {h.Olympics.Count} Olympics played in this career";
    }

    private static string Plural(int n, string word) => $"{n} {word}{(n == 1 ? "" : "s")}";

    private static string Joined(IReadOnlyList<string> items) => items.Count <= 1 ? string.Join("", items)
        : string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1];

    /// <summary>I3 (run 210): the latest qualifying cycle's group tables (W-L and point difference, in order), with the last window's
    /// results for each group.</summary>
    private void FillQualifying(InternationalHistory h)
    {
        QualGroups.Clear();
        var last = h.Windows.LastOrDefault();
        var cycle = last == null ? null : h.Cycles.FirstOrDefault(c => c.Target == last.Target);
        HasQualifying = cycle != null;
        if (last == null || cycle == null) { QualTitle = QualNote = ""; return; }
        int windows = h.Windows.Count(w => w.Target == last.Target);
        QualTitle = $"Qualifying for the {last.Target}";
        QualNote = $"{windows} window{(windows == 1 ? "" : "s")} played, the latest {last.Dates}. Quick results; each zone's places go to its group winners first, then the runners-up."
                   + (last.Target.EndsWith("World Cup", StringComparison.Ordinal) ? " Europe has 12 places, Asia-Pacific 8 (with the host), the Americas 7 and Africa 5." : "");
        var games = Qualifying.GamesOf(h, last.Target);
        foreach (var (name, codes) in cycle.Groups.OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            if (Zone != "All zones" && !name.StartsWith(Zone + " ", StringComparison.Ordinal)) continue;
            var table = WorldCup.Table(games, codes, g => g.Round == name);
            QualGroups.Add($"{name}: " + string.Join(", ", table.Select(c => { var r = WorldCup.Record(games, c, g => g.Round == name); return $"{c} {r.W}-{r.L} ({r.Diff:+0;-0;0})"; })));
        }
    }

    /// <summary>The selected nation's last World Cup and Olympics (I4): " · World Cup 2027: silver · Olympics 2028: gold".</summary>
    internal string CupLine(string code)
    {
        var h = Session.World.International;
        string line = "";
        foreach (var w in new[] { h.WorldCups.LastOrDefault(w => w.Teams.Any(t => t.Code == code)), h.Olympics.LastOrDefault(w => w.Teams.Any(t => t.Code == code)) })
            if (w != null) line += $" · {w.Short}: {GameSession.Placing(w, code)}";
        return line;
    }

    /// <summary>Review (run 207): a club in the nation's own country. The NBA, the US pyramid and college are home for the USA;
    /// a club abroad is home when its league's country (or local nationalities) is the nation's.</summary>
    internal static bool PlaysAtHome(GameSession s, Team club, bool college, string nation)
    {
        bool usa = nation == "USA";
        if (college || s.League.Teams.Contains(club)) return usa;
        var comp = s.World.Competitions.FirstOrDefault(c => c.Clubs.Contains(club));
        if (comp == null) return false;
        if (Hardwood.Core.World.UsaPyramid.IsPyramid(comp.Key)) return usa;
        return comp.Profile.Country == nation || comp.Profile.LocalNationalities.Contains(nation);
    }

    /// <summary>" · Russia is suspended by FIBA", " · Russia and Belarus are suspended by FIBA".</summary>
    internal static string Suspended(IReadOnlyList<string> names) => names.Count switch
    {
        0 => "",
        1 => $" · {names[0]} is suspended by FIBA",
        _ => $" · {string.Join(", ", names.Take(names.Count - 1))} and {names[^1]} are suspended by FIBA",
    };
}
