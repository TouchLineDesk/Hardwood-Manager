using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
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
    public int Pool => Team.Pool.Count;
    public string Star => Team.Squad.Count > 0 ? Team.Squad[0].Player.Name : "";
    public string Tip => $"{Name}, {Zone} · best player {Star} · {Pool} eligible players";
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

    private IReadOnlyList<NationalTeam> _teams = Array.Empty<NationalTeam>();

    public InternationalViewModel(MainViewModel shell) : base(shell) { }

    public override void Refresh()
    {
        var s = Session;
        _teams = NationalTeams.Build(s);
        int ranked = _teams.Count(t => t.Rank > 0);
        Summary = $"{ranked} national teams ranked by their best twelve · a country needs {NationalTeams.MinPool} players aged {NationalTeams.MinAge}+ anywhere in the world"
                  + Suspended(_teams.Where(t => t.Rank == 0).Select(t => t.Name).ToList());

        var mine = s.Employed ? s.HumanTeam.Roster.Select(p => p.Id).ToHashSet() : new HashSet<int>();
        var picked = _teams.SelectMany(t => t.Squad.Where(x => mine.Contains(x.Player.Id)).Select(x => (t, x))).ToList();
        Yours = !s.Employed ? ""
            : picked.Count == 0 ? "None of your players is in a national twelve."
            : $"In a national twelve: " + string.Join(", ", picked.Select(z => $"{z.x.Player.Name} ({z.t.Nation.Code})")) + ".";

        Calendar.Clear();
        foreach (var e in FibaCalendar.From(s.Today, 3)) Calendar.Add(new FibaEventRow { Event = e });

        var keep = Selected?.Team.Name;
        Fill();
        Selected = Nations.FirstOrDefault(n => n.Name == keep) ?? Nations.FirstOrDefault(n => n.Mine) ?? Nations.FirstOrDefault();
    }

    partial void OnZoneChanged(string value)
    {
        var keep = Selected?.Team.Name;
        Fill();
        Selected = Nations.FirstOrDefault(n => n.Name == keep) ?? Nations.FirstOrDefault();
    }

    private void Fill()
    {
        Nations.Clear();
        var s = Session;
        var mine = s.Employed ? s.HumanTeam.Roster.Select(p => p.Id).ToHashSet() : new HashSet<int>();
        foreach (var t in _teams.Where(t => Zone == "All zones" || NationalTeams.ZoneName(t.Nation.Zone) == Zone))
            Nations.Add(new NationRow { Team = t, Mine = t.Squad.Any(x => mine.Contains(x.Player.Id)) });
    }

    partial void OnSelectedChanged(NationRow? value)
    {
        Squad.Clear();
        if (value == null) { SquadTitle = ""; SquadNote = ""; return; }
        var s = Session;
        var t = value.Team;
        var today = s.Today;
        bool employed = s.Employed;
        int no = 0;
        foreach (var x in t.Squad)
        {
            double k = Scouting.Knowledge(s.HumanTeam, x.Player, prospect: x.College);
            Squad.Add(new NationPlayerRow
            {
                No = ++no, Player = x.Player, Club = x.Club, Age = x.Age,
                ClubText = x.Club == null ? "Free agent" : x.College ? $"{x.Club.FullName}, college" : x.Club.FullName,
                Ability = Scouting.Ability(x.Player, k).ToString(),
                Mine = employed && x.Club == s.HumanTeam,
            });
        }
        string rank = t.Rank > 0 ? $"No. {t.Rank} in the world, No. {t.ZoneRank} in {NationalTeams.ZoneName(t.Nation.Zone)}" : "suspended by FIBA";
        SquadTitle = $"{t.Name} ({t.Nation.Code}) · {rank}";
        var worldClubs = s.World.Clubs.ToHashSet();
        int abroad = t.Squad.Count(x => x.Club != null && worldClubs.Contains(x.Club));
        int colleges = t.Pool.Count(x => x.College);
        SquadNote = $"The federation's best twelve from {t.Pool.Count} eligible players"
                    + (colleges > 0 ? $" ({colleges} in college)" : "")
                    + (abroad > 0 ? $" · {abroad} play abroad" : "") + $" · strength {t.Strength:F1} · ability as your scouts believe";
    }

    /// <summary>" · Russia is suspended by FIBA", " · Russia and Belarus are suspended by FIBA".</summary>
    internal static string Suspended(IReadOnlyList<string> names) => names.Count switch
    {
        0 => "",
        1 => $" · {names[0]} is suspended by FIBA",
        _ => $" · {string.Join(", ", names.Take(names.Count - 1))} and {names[^1]} are suspended by FIBA",
    };
}
