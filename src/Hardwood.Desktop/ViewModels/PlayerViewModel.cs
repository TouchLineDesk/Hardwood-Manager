using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Hardwood.Core.Model;
using Hardwood.Core.Season;

namespace Hardwood.Desktop.ViewModels;

public sealed class AttrCell
{
    public required string Name { get; init; }
    public int Value { get; init; }
    public string Text { get; init; } = "";
    public string Color => Value >= 16 ? "#3FB950" : Value >= 13 ? "#A3D977" : Value >= 10 ? "#E6EAF0" : Value >= 7 ? "#D29922" : "#F85149";
}

public sealed class AttrGroupVm
{
    public required string Name { get; init; }
    public List<AttrCell> Cells { get; init; } = new();
    public string Average { get; init; } = "";
}

public sealed class SeasonLine
{
    public required string Season { get; init; }
    public required string Team { get; init; }
    public int GP { get; init; }
    public double MPG { get; init; }
    public double PPG { get; init; }
    public double RPG { get; init; }
    public double APG { get; init; }
    public string FG { get; init; } = "";
    public string TP { get; init; } = "";
    public string FT { get; init; } = "";
}

public sealed partial class PlayerViewModel : PageViewModel
{
    public Player Player { get; }
    public override string Title => Player.Name;

    public PlayerViewModel(MainViewModel shell, Player player) : base(shell)
    {
        Player = player;
        Refresh();
    }

    public string Name => Player.Name;
    public Hardwood.Core.Model.Team? TeamObj => Player.TeamId is { } id ? Session.FindTeam(id)! : null;
    public double Knowledge => Core.Season.Scouting.Knowledge(Session.HumanTeam, Player);
    public string TierLine { get; private set; } = "";
    public string Headline { get; private set; } = "";
    public string Bio { get; private set; } = "";
    public string Contract { get; private set; } = "";
    public string InjuryText { get; private set; } = "";
    public string MoodText { get; private set; } = "";
    public string Tendencies { get; private set; } = "";
    public string Honours { get; private set; } = "";
    public List<AttrGroupVm> Groups { get; private set; } = new();
    /// <summary>Roadmap 1: the broadcast stat strip (age, size, ability, money, health, mood, this season).</summary>
    public List<Hardwood.Desktop.Controls.Chip> Chips { get; private set; } = new();
    /// <summary>His club's colour (the default accent for a free agent), for the header glow and chip edges.</summary>
    public Avalonia.Media.Color Accent => TeamObj is { } t ? ThemeService.AccentFor(t) : ThemeService.DefaultAccent;
    public string Hint { get; private set; } = "";
    public double[] RadarValues { get; private set; } = Array.Empty<double>();
    public int[] ZoneAttempts { get; private set; } = new int[5];
    public int[] ZoneMakes { get; private set; } = new int[5];
    public IReadOnlyList<Hardwood.Desktop.Controls.LineSeries> Trend { get; private set; } = Array.Empty<Hardwood.Desktop.Controls.LineSeries>();
    public string[] TrendLabels { get; private set; } = Array.Empty<string>();
    public bool HasTrend => TrendLabels.Length >= 2;
    public bool HasShots => ZoneAttempts.Sum() > 0;
    public string[] RadarLabels { get; } = { "Inside", "Mid-range", "Three", "Playmaking", "Perimeter D", "Interior D", "Rebounding", "Athleticism" };
    public ObservableCollection<SeasonLine> Seasons { get; } = new();

    [RelayCommand] public void Back() => Shell.GoBack();

    /// <summary>Your own player: open the Squad page with him selected, ready to talk (roadmap 1; it used to be a text hint).</summary>
    public bool CanTalk => TeamObj != null && TeamObj == Session.HumanTeam;
    [RelayCommand]
    public void TalkToHim()
    {
        var player = Player;
        Shell.Navigate("Squad");
        if (Shell.CurrentPage is SquadViewModel squad)
            squad.Selected = squad.Players.FirstOrDefault(r => r.Player == player) ?? squad.Selected;
    }
    /// <summary>His club (opens its team page), or null for a free agent.</summary>
    public Hardwood.Core.Model.Team? Club => Player.TeamId is { } id ? Session.FindTeam(id) : null;

    public override void Refresh()
    {
        var p = Player; var s = Session;
        var team = p.TeamId is { } id ? s.FindTeam(id)!.FullName : "Free agent";
        Headline = $"{p.PrimaryPosition}{(p.SecondaryPosition is { } sp ? "/" + sp : "")} · {team}";
        // The collector card (D58 B) has no tier label, so the header names his skills instead of a card tier.
        TierLine = string.Join(" · ", CollectorCard.Badges(p, Scouting.Knowledge(s.HumanTeam, p))) + "  ·  click his card to flip it";
        double k = Scouting.Knowledge(s.HumanTeam, p);
        Bio = $"Age {p.Age(s.Today)} ({p.BirthDate:MMM d, yyyy}) · {PlayerRow.FormatHeight(p.HeightCm)} ({p.HeightCm} cm) · {p.WingspanCm} cm wingspan · {p.WeightKg} kg · Ability {Scouting.Ability(p, k)} / potential {Scouting.Potential(p, k)}" + (k >= 0.999 ? $" · Overall {p.Overall:F1}" : $" · scouting knowledge {k * 100:F0}%");
        Contract = p.Contract is { } c ? $"${c.Salary / 1e6:F2}M per year, {(c.Years <= 0 ? "expiring" : $"{c.Years} season{(c.Years == 1 ? "" : "s")} remaining")}{(c.TeamOption ? ", team option" : "")}" : "No contract";
        InjuryText = p.Injury is { } inj && inj.IsOut(s.Today)
            ? $"{inj.Name} ({inj.Severity}) · out {inj.DaysOut(s.Today)} more days, back {inj.ReturnDate:MMM d}"
            : p.InjuryHistory.Count > 0 ? $"Healthy · {p.InjuryHistory.Count} past injur{(p.InjuryHistory.Count == 1 ? "y" : "ies")}" : "Healthy";
        MoodText = p.TeamId == null ? "" : $"Mood: {Hardwood.Core.People.PeopleEngine.Mood(p.Morale)}" + (p.TradeRequested ? " · has asked to be traded" : "")
            ;
        Chips = BuildChips(p, k);
        Hint = "";
        var career = s.League.Careers.GetValueOrDefault(p.Id);
        var hall = s.League.HallOfFame.FirstOrDefault(h => h.PlayerId == p.Id);
        var retiredBy = s.League.Teams.Where(x => x.RetiredNumbers.Contains(p.Id)).Select(x => x.FullName).ToList();
        var bits = new List<string>();
        if (p.RookieSeason is { } rs) bits.Add($"Rookie season {rs}-{(rs + 1) % 100:00}");
        if (career != null)
        {
            bits.Add($"Career: {career.Seasons} seasons, {career.Games} games, {career.Points:N0} pts ({career.Ppg:F1} ppg)");
            var h = new[] { (career.Mvps, "MVP"), (career.Titles, "title"), (career.FinalsMvps, "Finals MVP"), (career.AllLeague, "All-League"), (career.AllStars, "All-Star"), (career.Dpoys, "DPOY"), (career.Roys, "ROY"), (career.SixthMen, "Sixth Man") }
                .Where(x => x.Item1 > 0).Select(x => $"{x.Item1}× {x.Item2}").ToList();
            if (h.Count > 0) bits.Add(string.Join(", ", h));
        }
        if (hall != null) bits.Add($"Hall of Fame ({hall.Year})");
        if (retiredBy.Count > 0) bits.Add($"Number retired by {string.Join(", ", retiredBy)}");
        Honours = bits.Count == 0 ? "" : string.Join(" · ", bits);

        var t = p.Tendencies;
        Tendencies = $"Three-point rate {t.ThreePointRate} · drives {t.DriveRate} · post-ups {t.PostUpRate} · isolation {t.IsolationRate} · pass-first {t.PassFirst} · crash glass {t.CrashGlass}";

        double know = Scouting.Knowledge(s.HumanTeam, p);
        var a = p.Attributes;
        double Avg(params Attr[] xs) => xs.Average(x => know >= 0.999 ? a[x] : Scouting.Attribute(p, x, know).Mid);
        RadarValues = new[]
        {
            Avg(Attr.CloseShot, Attr.Layup, Attr.Dunk), Avg(Attr.MidRange, Attr.ShotCreation), Avg(Attr.ThreePoint, Attr.CatchAndShoot),
            Avg(Attr.BallHandling, Attr.Passing, Attr.CourtVision), Avg(Attr.PerimeterDefense, Attr.Steal), Avg(Attr.InteriorDefense, Attr.Block, Attr.HelpDefense),
            Avg(Attr.DefensiveRebounding, Attr.OffensiveRebounding), Avg(Attr.Speed, Attr.Acceleration, Attr.Vertical, Attr.Strength),
        };
        Groups = Enum.GetValues<AttrGroup>().Select(g => new AttrGroupVm
        {
            Name = g.ToString(),
            Average = Scouting.GroupAverage(p, g, know),
            Cells = AttrInfo.All.Where(a => AttrInfo.GroupOf(a) == g).Select(a =>
            {
                var r = Scouting.Attribute(p, a, know);
                return new AttrCell { Name = Pretty(a), Value = (int)Math.Round(r.Mid), Text = r.ToString() };
            }).ToList(),
        }).ToList();

        Seasons.Clear();
        var all = new List<Core.Season.PlayerSeasonStats>();
        if (s.Save != null) all.AddRange(s.Save.LoadStats().Where(x => x.PlayerId == p.Id && x.Season != s.League.Season));
        if (s.Stats.TryGetValue(p.Id, out var cur)) all.Add(cur);
        var ordered = all.OrderBy(x => x.Season).ToList();
        TrendLabels = ordered.Select(x => $"{x.Season % 100:00}-{(x.Season + 1) % 100:00}").ToArray();
        Trend = new[]
        {
            new Hardwood.Desktop.Controls.LineSeries { Name = "PPG", Values = ordered.Select(x => Math.Round(x.Ppg, 1)).ToArray(), Color = Avalonia.Media.Color.Parse("#F0A33A"), Emphasis = true },
            new Hardwood.Desktop.Controls.LineSeries { Name = "RPG", Values = ordered.Select(x => Math.Round(x.Rpg, 1)).ToArray(), Color = Avalonia.Media.Color.Parse("#58A6FF") },
            new Hardwood.Desktop.Controls.LineSeries { Name = "APG", Values = ordered.Select(x => Math.Round(x.Apg, 1)).ToArray(), Color = Avalonia.Media.Color.Parse("#3FB950") },
        };
        var shotSrc = cur ?? ordered.LastOrDefault();
        ZoneAttempts = shotSrc?.ZoneAttempts ?? new int[5];
        ZoneMakes = shotSrc?.ZoneMakes ?? new int[5];
        foreach (var st in all.OrderByDescending(x => x.Season))
            Seasons.Add(new SeasonLine
            {
                Season = $"{st.Season}-{(st.Season + 1) % 100:00}", Team = s.FindTeam(st.TeamId)!.Abbreviation,
                GP = st.Games, MPG = Math.Round(st.Mpg, 1), PPG = Math.Round(st.Ppg, 1), RPG = Math.Round(st.Rpg, 1), APG = Math.Round(st.Apg, 1),
                FG = st.FGA > 0 ? $"{st.FgPct * 100:F1}%" : "-", TP = st.ThreePA > 0 ? $"{st.ThreePct * 100:F1}%" : "-", FT = st.FTA > 0 ? $"{100.0 * st.FTM / st.FTA:F1}%" : "-",
            });
        OnPropertyChanged(string.Empty);
    }

    private List<Hardwood.Desktop.Controls.Chip> BuildChips(Player p, double k)
    {
        var s = Session;
        var chips = new List<Hardwood.Desktop.Controls.Chip>
        {
            new("Age", p.Age(s.Today).ToString(), $"born {p.BirthDate:MMM d, yyyy}"),
            new("Height", PlayerRow.FormatHeight(p.HeightCm), $"{p.HeightCm} cm · {p.WingspanCm} span · {p.WeightKg} kg"),
            new("Ability", Scouting.Ability(p, k).ToString(), k >= 0.999 ? $"overall {p.Overall:F1}" : $"scouted {k * 100:F0}%", "team"),
            new("Potential", Scouting.Potential(p, k).ToString(), Scouting.Potential(p, k).ToString() == Scouting.Ability(p, k).ToString() ? "at his peak" : "", "team"),
        };
        if (p.Contract is { } c)
            chips.Add(new("Salary", Money(c.Salary), c.Years <= 0 ? "expiring" : $"{c.Years} season{(c.Years == 1 ? "" : "s")} left{(c.TeamOption ? ", team option" : "")}", c.Years <= 0 ? "warn" : ""));
        else chips.Add(new("Contract", "None", "free agent", "warn"));
        if (p.Injury is { } inj && inj.IsOut(s.Today))
            chips.Add(new("Status", $"Out {inj.DaysOut(s.Today)}d", $"{inj.Name}, back {inj.ReturnDate:MMM d}", "bad"));
        else
            chips.Add(new("Status", "Healthy", p.InjuryHistory.Count == 0 ? "no injury history" : $"{p.InjuryHistory.Count} past injur{(p.InjuryHistory.Count == 1 ? "y" : "ies")}", "good"));
        if (p.TeamId != null)
        {
            var mood = Hardwood.Core.People.PeopleEngine.Mood(p.Morale);
            chips.Add(new("Mood", mood, p.TradeRequested ? "has asked to be traded" : $"morale {p.Morale:0}/100",
                p.TradeRequested || p.Morale < 35 ? "bad" : p.Morale < 50 ? "warn" : "good"));
        }
        if (s.Stats.TryGetValue(p.Id, out var cur) && cur.Games > 0)
            chips.Add(new("Points", cur.Ppg.ToString("0.0"), $"{cur.Rpg:0.0} reb · {cur.Apg:0.0} ast · {cur.Games} gp", "info"));
        return chips;
    }

    /// <summary>$39.6M, $850K.</summary>
    public static string Money(long v) => Math.Abs(v) >= 1_000_000 ? $"${v / 1e6:0.0#}M" : $"${v / 1e3:0}K";

    private static string Pretty(Attr a) => System.Text.RegularExpressions.Regex.Replace(a.ToString(), "(?<=[a-z])([A-Z])", " $1");
}
