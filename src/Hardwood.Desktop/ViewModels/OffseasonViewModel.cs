using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Core.World;

namespace Hardwood.Desktop.ViewModels;

public sealed class ExpiringRow
{
    public required Player Player { get; init; }
    public string Name => Player.Name;
    public string Pos => Player.PrimaryPosition.ToString();
    public int Age { get; init; }
    public int CA => Player.CurrentAbility;
    public int PA => Player.PotentialAbility;
    public string LastSeason { get; init; } = "";
    public string Ask { get; init; } = "";
    public string Fits { get; init; } = "";
}

public sealed class ProspectRow
{
    public required Player Player { get; init; }
    public int Rank { get; init; }
    public string Name => Player.Name;
    public string Pos => Player.PrimaryPosition.ToString();
    public int Age { get; init; }
    public string Height => PlayerRow.FormatHeight(Player.HeightCm);
    public double Knowledge { get; init; } = 1.0;
    public string CA => Scouting.Ability(Player, Knowledge).ToString();
    public string PA => Scouting.Potential(Player, Knowledge).ToString();
    public string Sho => Scouting.GroupAverage(Player, AttrGroup.Shooting, Knowledge);
    public string Ply => Scouting.GroupAverage(Player, AttrGroup.Playmaking, Knowledge);
    public string Def => Scouting.GroupAverage(Player, AttrGroup.Defense, Knowledge);
    public string Phy => Scouting.GroupAverage(Player, AttrGroup.Physical, Knowledge);
    public string Need { get; init; } = "";
}

public sealed class PickRow
{
    public required DraftPick Pick { get; init; }
    public string Where => $"R{Pick.Round} #{Pick.Pick}";
    public string Team => Pick.Team.Abbreviation + (Pick.OriginalTeam != null && Pick.OriginalTeam != Pick.Team ? $" (via {Pick.OriginalTeam.Abbreviation})" : "");
    public string Player => Pick.Player.Name;
    public string Pos => Pick.Player.PrimaryPosition.ToString();
    public string Ability => $"{Pick.Player.CurrentAbility}/{Pick.Player.PotentialAbility}";
    public bool Ours { get; init; }
}

/// <summary>A player under contract overseas whom an NABL team could buy out (decision #53).</summary>
public sealed class OverseasRow
{
    public required Player Player { get; init; }
    public string Name => Player.Name;
    public string Pos => Player.PrimaryPosition.ToString();
    public int Age { get; init; }
    public double Knowledge { get; init; }
    public string CA => Scouting.Ability(Player, Knowledge).ToString();
    public string PA => Scouting.Potential(Player, Knowledge).ToString();
    public string Club { get; init; } = "";
    public string Salary => Player.Contract is { } c ? $"${c.Salary / 1e6:F2}M × {c.Years}" : "-";
    public string Buyout => Player.Contract is { } c ? $"${c.Buyout / 1e6:F2}M" : "-";
    public string Terms { get; init; } = "";
}

/// <summary>A draft pick this team holds the rights to: this year's picks (which can be stashed) and players stashed abroad.</summary>
public sealed class RightsRow
{
    public required Player Player { get; init; }
    public string Name => Player.Name;
    public string Pos => Player.PrimaryPosition.ToString();
    public int Age { get; init; }
    public string Ability => $"{Player.CurrentAbility}/{Player.PotentialAbility}";
    public string Where { get; init; } = "";
    public string Next { get; init; } = "";
    public bool Abroad { get; init; }
}

/// <summary>
/// The summer (decisions #87-90): renew or release your expiring players, sign free agents (including this year's
/// college graduates) through contract talks within your wage budget, and, in the NABL, buy players out of deals abroad.
/// Works the same for a club abroad. There is no draft on the open market.
/// </summary>
public sealed partial class OffseasonViewModel : PageViewModel
{
    public override string Title => "Offseason";
    public Hardwood.Core.Model.Team HumanTeam => Session.HumanTeam;

    [ObservableProperty] private string _headline = "";
    [ObservableProperty] private string _instructions = "";
    [ObservableProperty] private bool _inOffseason;
    [ObservableProperty] private bool _showContracts;
    [ObservableProperty] private bool _showFreeAgency;
    [ObservableProperty] private bool _showBuyouts;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _capText = "";
    [ObservableProperty] private int _tab;

    public ObservableCollection<ExpiringRow> Expiring { get; } = new();
    [ObservableProperty] private ExpiringRow? _selectedExpiring;
    public ObservableCollection<FreeAgentRow> FreeAgents { get; } = new();
    [ObservableProperty] private FreeAgentRow? _selectedFreeAgent;
    public ObservableCollection<PlayerRow> Roster { get; } = new();
    [ObservableProperty] private PlayerRow? _selectedRosterPlayer;
    /// <summary>Overseas players good enough to interest an NABL team, with what a buyout would take.</summary>
    public ObservableCollection<OverseasRow> Overseas { get; } = new();
    [ObservableProperty] private OverseasRow? _selectedOverseas;

    /// <summary>The talks panel on the right.</summary>
    public TalkViewModel Talk { get; }

    partial void OnSelectedExpiringChanged(ExpiringRow? value) { if (value != null) Talk.Show(value.Player); }
    partial void OnSelectedFreeAgentChanged(FreeAgentRow? value) { if (value != null) Talk.Show(value.Player); }

    public OffseasonViewModel(MainViewModel shell) : base(shell) => Talk = new TalkViewModel(shell, Refresh);

    public override void Refresh()
    {
        var s = Session; var o = s.Offseason;
        InOffseason = s.Phase == SessionPhase.Offseason && o != null;
        Expiring.Clear(); FreeAgents.Clear(); Roster.Clear(); Overseas.Clear();
        if (!InOffseason || !s.Employed)
        {
            Headline = InOffseason ? "Summer" : "The offseason has not started.";
            Instructions = !InOffseason
                ? (s.Phase == SessionPhase.Postseason ? "The playoffs are under way. Once the season ends, contract talks and free agency happen here."
                                                      : "Play the season out. In the summer you will renew or release players whose deals are up and sign free agents here.")
                : "You aren't coaching a club. Press Continue to move through the summer.";
            ShowContracts = ShowFreeAgency = ShowBuyouts = false;
            CapText = "";
            return;
        }
        var team = s.HumanTeam; var fin = s.League.Finance;
        bool renewing = s.InNabl ? o!.Phase == OffseasonPhase.ReSigning : true;
        bool freeAgency = s.Abroad || o!.Phase == OffseasonPhase.FreeAgency;
        ShowContracts = s.MyExpiring.Count > 0 || (s.InNabl && renewing);
        ShowFreeAgency = freeAgency;
        ShowBuyouts = s.InNabl && freeAgency;
        long committed = s.MyCommittedWages, budget = team.Money.WageBudget;
        static string M(long v) => Math.Abs(v) >= 10_000_000 ? $"${v / 1e6:F0}M" : $"${v / 1e6:F1}M";
        CapText = $"Wages committed {M(committed)} of a {M(budget)} budget · {(budget >= committed ? $"{M(budget - committed)} to spend" : $"{M(committed - budget)} over")}"
                  + $" · roster {team.Roster.Count - s.MyExpiring.Count} under contract (needs {fin.RosterMin}-{fin.RosterMax})";
        Headline = $"Summer {o!.Season + 1} · {team.FullName}";
        Instructions = (s.InNabl && renewing
                ? "Contract decisions: talk to each player whose deal is up (offer a wage and years; he accepts, counters or walks), or release him. Then continue to free agency."
                : "Free agency: make offers to free agents, including this year's college graduates (age 23). Players ask more to join a weaker team, and the owner won't let you go over the wage budget.")
            + (s.Abroad ? " Anyone you haven't renewed by the end of the summer leaves." : "");

        double know = 1;
        foreach (var p in s.MyExpiring)
        {
            s.MyStats.TryGetValue(p.Id, out var st);
            var d = s.PeekDemand(p);
            Expiring.Add(new ExpiringRow
            {
                Player = p, Age = p.Age(o.Date),
                LastSeason = st != null ? $"{st.Ppg:F1} pts · {st.Rpg:F1} reb · {st.Apg:F1} ast · {st.Mpg:F0} min" : "-",
                Ask = $"${d.Wage / 1e6:F2}M × {d.Years}",
                Fits = Offseason.WontReSign(p) ? "Won't stay" : s.MyCommittedWages + d.Wage <= team.Money.WageBudget ? "Fits budget" : "Over budget",
            });
        }
        if (freeAgency)
            foreach (var p in s.League.FreeAgents.OrderByDescending(p => p.CurrentAbility).Take(250))
            {
                var d = s.PeekDemand(p);
                know = Scouting.Knowledge(team, p);
                FreeAgents.Add(new FreeAgentRow { Player = p, Age = p.Age(o.Date), Knowledge = know, Ask = d, Graduate = p.RookieSeason == o.Season + 1 });
            }
        foreach (var p in team.Roster.OrderByDescending(p => p.CurrentAbility)) Roster.Add(PlayerRow.From(p, s, team.Abbreviation));
        if (ShowBuyouts)
            foreach (var p in s.World.Players.Where(p => p.CurrentAbility >= 95).OrderByDescending(p => p.CurrentAbility).Take(200))
            {
                var club = WorldMarket.ClubOf(s.World, p);
                var terms = WorldMarket.Terms(s.World, o, team, p);
                Overseas.Add(new OverseasRow
                {
                    Player = p, Age = p.Age(o.Date), Knowledge = Scouting.Knowledge(team, p),
                    Club = club == null ? "" : $"{club.FullName} ({s.World.HomeOf(club).Profile.Short})",
                    Terms = terms.Possible ? $"Yes: ${terms.NablSalary / 1e6:F1}M a year. {terms.Note}" : terms.Note,
                });
            }
        if (Tab == 0 && !ShowContracts) Tab = 1;
        if (Talk.Player == null || (!s.MyExpiring.Contains(Talk.Player) && !s.League.FreeAgents.Contains(Talk.Player) && !Talk.Signed))
            Talk.Show(Expiring.FirstOrDefault()?.Player ?? FreeAgents.FirstOrDefault()?.Player);
    }

    [RelayCommand]
    public void BuyOut()
    {
        var p = SelectedOverseas?.Player;
        if (p == null) return;
        var why = Session.BuyOut(p);
        Status = why == null ? $"{p.Name} is bought out and signed: ${p.Contract!.Salary / 1e6:F1}M × {p.Contract.Years}." : why;
        Shell.AfterTick();
        Refresh();
    }

    [RelayCommand]
    public void WaivePlayer()
    {
        if (SelectedRosterPlayer == null) return;
        var p = SelectedRosterPlayer.Player;
        if (Session.MyExpiring.Contains(p)) Session.ReleaseExpiring(p);
        else if (Session.InNabl) Session.Offseason!.Waive(Session.HumanTeam, p);
        else { Session.HumanTeam.Roster.Remove(p); p.TeamId = null; p.Contract = null; Session.League.FreeAgents.Add(p); Session.SaveNow(); }
        Status = $"Released {p.Name}.";
        Shell.AfterTick();
        Refresh();
    }

    [RelayCommand]
    public void OpenPlayer(Player? p)
    {
        if (p != null) Shell.ShowPlayer(p);
    }
}

/// <summary>A free agent with his ask of your club.</summary>
public sealed class FreeAgentRow
{
    public required Player Player { get; init; }
    public required ContractDemand Ask { get; init; }
    public string Name => Player.Name + (Graduate ? "  (graduate)" : "");
    public string Pos => Player.PrimaryPosition.ToString();
    public int Age { get; init; }
    public double Knowledge { get; init; } = 1;
    public bool Graduate { get; init; }
    public string CA => Scouting.Ability(Player, Knowledge).ToString();
    public string PA => Scouting.Potential(Player, Knowledge).ToString();
    public string Sho => Scouting.GroupAverage(Player, AttrGroup.Shooting, Knowledge);
    public string Ply => Scouting.GroupAverage(Player, AttrGroup.Playmaking, Knowledge);
    public string Def => Scouting.GroupAverage(Player, AttrGroup.Defense, Knowledge);
    public string Phy => Scouting.GroupAverage(Player, AttrGroup.Physical, Knowledge);
    public string Asks => $"${Ask.Wage / 1e6:F2}M × {Ask.Years}";
}
