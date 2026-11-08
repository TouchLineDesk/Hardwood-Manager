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
    public string Headline { get; private set; } = "";
    public string Bio { get; private set; } = "";
    public string Contract { get; private set; } = "";
    public string InjuryText { get; private set; } = "";
    public string Tendencies { get; private set; } = "";
    public string Honours { get; private set; } = "";
    public List<AttrGroupVm> Groups { get; private set; } = new();
    public ObservableCollection<SeasonLine> Seasons { get; } = new();

    [RelayCommand] public void Back() => Shell.Navigate("Roster");

    public override void Refresh()
    {
        var p = Player; var s = Session;
        var team = p.TeamId is { } id ? s.League.TeamById(id).FullName : "Free agent";
        Headline = $"{p.PrimaryPosition}{(p.SecondaryPosition is { } sp ? "/" + sp : "")} · {team}";
        double k = Scouting.Knowledge(s.HumanTeam, p);
        Bio = $"Age {p.Age(s.Today)} ({p.BirthDate:MMM d, yyyy}) · {PlayerRow.FormatHeight(p.HeightCm)} ({p.HeightCm} cm) · {p.WingspanCm} cm wingspan · {p.WeightKg} kg · Ability {Scouting.Ability(p, k)} / potential {Scouting.Potential(p, k)}" + (k >= 0.999 ? $" · Overall {p.Overall:F1}" : $" · scouting knowledge {k * 100:F0}%");
        Contract = p.Contract is { } c ? $"${c.Salary / 1e6:F2}M per year, {(c.Years <= 0 ? "expiring" : $"{c.Years} season{(c.Years == 1 ? "" : "s")} remaining")}{(c.TeamOption ? ", team option" : "")}" : "No contract";
        InjuryText = p.Injury is { } inj && inj.IsOut(s.Today)
            ? $"{inj.Name} ({inj.Severity}) · out {inj.DaysOut(s.Today)} more days, back {inj.ReturnDate:MMM d}"
            : p.InjuryHistory.Count > 0 ? $"Healthy · {p.InjuryHistory.Count} past injur{(p.InjuryHistory.Count == 1 ? "y" : "ies")}" : "Healthy";
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
        foreach (var st in all.OrderByDescending(x => x.Season))
            Seasons.Add(new SeasonLine
            {
                Season = $"{st.Season}-{(st.Season + 1) % 100:00}", Team = s.League.TeamById(st.TeamId).Abbreviation,
                GP = st.Games, MPG = Math.Round(st.Mpg, 1), PPG = Math.Round(st.Ppg, 1), RPG = Math.Round(st.Rpg, 1), APG = Math.Round(st.Apg, 1),
                FG = st.FGA > 0 ? $"{st.FgPct * 100:F1}%" : "-", TP = st.ThreePA > 0 ? $"{st.ThreePct * 100:F1}%" : "-", FT = st.FTA > 0 ? $"{100.0 * st.FTM / st.FTA:F1}%" : "-",
            });
        OnPropertyChanged(string.Empty);
    }

    private static string Pretty(Attr a) => System.Text.RegularExpressions.Regex.Replace(a.ToString(), "(?<=[a-z])([A-Z])", " $1");
}
