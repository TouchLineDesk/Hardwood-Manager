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

public sealed partial class OffseasonViewModel : PageViewModel
{
    public Player? SelectedProspectPlayer => SelectedProspect?.Player;
    public double ProspectKnowledge => SelectedProspect?.Knowledge ?? 1.0;
    /// <summary>The player shown on the free-agency card: the last one selected in any of the three lists.</summary>
    public Player? SelectedFreeAgentPlayer => _cardPlayer;
    public double FreeAgentKnowledge => _cardKnowledge;
    public Player? SelectedExpiringPlayer => SelectedExpiring?.Player;
    public Hardwood.Core.Model.Team HumanTeam => Session.HumanTeam;
    partial void OnSelectedProspectChanged(ProspectRow? value) { OnPropertyChanged(nameof(SelectedProspectPlayer)); OnPropertyChanged(nameof(ProspectKnowledge)); }
    partial void OnSelectedFreeAgentChanged(PlayerRow? value) { if (value != null) ShowCard(value.Player, value.Knowledge); }
    partial void OnSelectedOverseasChanged(OverseasRow? value) { if (value != null) ShowCard(value.Player, value.Knowledge); }
    partial void OnSelectedRightsChanged(RightsRow? value) { if (value != null) ShowCard(value.Player, Scouting.Knowledge(Session.HumanTeam, value.Player)); }
    private void ShowCard(Player p, double knowledge) { _cardPlayer = p; _cardKnowledge = knowledge; OnPropertyChanged(nameof(SelectedFreeAgentPlayer)); OnPropertyChanged(nameof(FreeAgentKnowledge)); }
    private Player? _cardPlayer; private double _cardKnowledge = 1.0;

    /// <summary>Overseas players good enough to interest an NABL team, with what a buyout would take.</summary>
    public ObservableCollection<OverseasRow> Overseas { get; } = new();
    [ObservableProperty] private OverseasRow? _selectedOverseas;
    /// <summary>This year's picks (stashable) and players abroad whose rights you hold.</summary>
    public ObservableCollection<RightsRow> Rights { get; } = new();
    [ObservableProperty] private RightsRow? _selectedRights;
    partial void OnSelectedExpiringChanged(ExpiringRow? value) => OnPropertyChanged(nameof(SelectedExpiringPlayer));
    public override string Title => "Offseason";
    [ObservableProperty] private string _headline = "";
    [ObservableProperty] private string _instructions = "";
    [ObservableProperty] private bool _inOffseason;
    [ObservableProperty] private bool _isReSigning;
    [ObservableProperty] private bool _isDraft;
    [ObservableProperty] private bool _isFreeAgency;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _capText = "";

    public ObservableCollection<ExpiringRow> Expiring { get; } = new();
    [ObservableProperty] private ExpiringRow? _selectedExpiring;

    public ObservableCollection<ProspectRow> Prospects { get; } = new();
    [ObservableProperty] private ProspectRow? _selectedProspect;
    public ObservableCollection<PickRow> Picks { get; } = new();
    [ObservableProperty] private string _clockText = "";
    [ObservableProperty] private bool _onTheClock;

    public ObservableCollection<PlayerRow> FreeAgents { get; } = new();
    [ObservableProperty] private PlayerRow? _selectedFreeAgent;
    public ObservableCollection<PlayerRow> Roster { get; } = new();
    [ObservableProperty] private PlayerRow? _selectedRosterPlayer;

    public OffseasonViewModel(MainViewModel shell) : base(shell) { }

    public override void Refresh()
    {
        var s = Session; var o = s.Offseason;
        InOffseason = s.Phase == SessionPhase.Offseason && o != null;
        if (!InOffseason)
        {
            Headline = "The offseason has not started.";
            Instructions = s.Phase == SessionPhase.Postseason ? "The playoffs are under way. Once a champion is crowned, contract decisions, the lottery, the draft and free agency happen here."
                                                             : "Play the season out. After the Finals you will make contract decisions, draft, and sign free agents here.";
            IsReSigning = IsDraft = IsFreeAgency = false;
            return;
        }
        var team = s.HumanTeam; var fin = s.League.Finance;
        IsReSigning = o!.Phase == OffseasonPhase.ReSigning;
        IsDraft = o.Phase == OffseasonPhase.Draft;
        IsFreeAgency = o.Phase == OffseasonPhase.FreeAgency;
        long committed = o.CommittedPayroll(team);
        CapText = $"Roster {team.Roster.Count} · " + fin.Describe(committed + o.ReservedForDraft(team))
                  + (IsReSigning && o.ReservedForDraft(team) > 0 ? $" (incl. ${o.ReservedForDraft(team) / 1e6:F1}M reserved for your pick)" : "")
                  + (IsFreeAgency && fin.SoftCap ? (o.MidLevelUsed.Contains(team.Id) ? " · mid-level used" : $" · mid-level exception ${fin.MidLevel / 1e6:F1}M available") : "");

        Expiring.Clear(); Prospects.Clear(); Picks.Clear(); FreeAgents.Clear(); Roster.Clear(); Overseas.Clear(); Rights.Clear();
        switch (o.Phase)
        {
            case OffseasonPhase.ReSigning:
                Headline = $"Summer {o.Season + 1}: contract decisions";
                var exp = o.Expiring[team.Id];
                Instructions = exp.Count == 0 ? "No contracts expired. Continue to the draft when ready."
                    : "Re-sign or release each expiring player. Anyone undecided when you continue becomes a free agent."
                      + (fin.SoftCap ? $" Bird rights: you may re-sign your own players over the ${fin.SalaryCap / 1e6:F0}M cap, up to the ${fin.Apron / 1e6:F0}M apron. Above ${fin.TaxLine / 1e6:F0}M you pay luxury tax." : "");
                foreach (var p in exp)
                {
                    s.Stats.TryGetValue(p.Id, out var st);
                    Expiring.Add(new ExpiringRow
                    {
                        Player = p, Age = p.Age(o.Date), Ask = $"${o.Ask(p) / 1e6:F1}M",
                        LastSeason = st != null ? $"{st.Ppg:F1} pts, {st.Rpg:F1} reb, {st.Apg:F1} ast in {st.Mpg:F1} min" : "did not play",
                        Fits = Offseason.WontReSign(p) ? "Won't re-sign (unhappy)"
                             : !o.CanAfford(team, o.Ask(p)) ? (fin.SoftCap ? "Over apron" : "Over cap")
                             : fin.SoftCap && committed + o.ReservedForDraft(team) + o.Ask(p) > fin.SalaryCap ? "Yes (Bird rights)" : "Yes",
                    });
                }
                SelectedExpiring = Expiring.FirstOrDefault();
                break;

            case OffseasonPhase.Draft:
                OnTheClock = o.OnTheClock == team;
                Headline = $"Draft {o.Season + 1} · round {o.CurrentRound}, pick {o.NextPickIndex + 1}";
                int ourPicks = o.PickOwners.Skip(o.NextPickIndex).Count(t => t == team);
                ClockText = OnTheClock ? "You are on the clock." : o.DraftComplete ? "Draft complete." : $"{o.OnTheClock?.FullName} is on the clock. You have {ourPicks} pick{(ourPicks == 1 ? "" : "s")} left.";
                Instructions = (OnTheClock ? "Select a prospect and make your pick. First-round picks sign rookie-scale deals; second-rounders are unguaranteed and go to the free-agent pool."
                                          : "Continue to run the draft to your next pick.") + $" Your scouts' knowledge of the class: {Scouting.Knowledge(team, o.Prospects.FirstOrDefault() ?? team.Roster[0], prospect: true) * 100:F0}%; ability shows as a range.";
                int rank = 1;
                double know = Scouting.Knowledge(team, o.Prospects.FirstOrDefault() ?? team.Roster[0], prospect: true);
                foreach (var p in o.Prospects)
                    Prospects.Add(new ProspectRow { Player = p, Rank = rank++, Age = p.Age(o.Date), Knowledge = know, Need = team.Roster.Count(x => x.PrimaryPosition == p.PrimaryPosition) <= 1 ? "Need" : "" });
                foreach (var d in o.Report.Draft.OrderByDescending(d => d.Pick)) Picks.Add(new PickRow { Pick = d, Ours = d.Team == team });
                SelectedProspect = Prospects.FirstOrDefault();
                break;

            case OffseasonPhase.FreeAgency:
                Headline = $"Free agency {o.Season + 1}";
                Instructions = fin.SoftCap
                    ? $"Sign players until your roster has {fin.RosterMin} to {fin.RosterMax}. Three ways to sign another team's free agent: cap room under the ${fin.SalaryCap / 1e6:F0}M cap; "
                      + $"the mid-level exception (once a summer, up to ${fin.MidLevel / 1e6:F1}M, even over the cap); or a minimum deal (players asking up to ${fin.MinSalary * 2 / 1e6:F1}M take one). "
                      + $"Nobody may pass the ${fin.Apron / 1e6:F0}M apron. When you start the new season, anything illegal is fixed automatically (cheapest fixes first)."
                    : $"Sign players until your roster has {fin.RosterMin} to {fin.RosterMax} players under the ${fin.HardCap / 1e6:F0}M hard cap. Below {fin.RosterMin} players you may add minimum deals regardless of room. Waive to make space. When you start the new season, anything illegal is fixed automatically (cheapest fixes first).";
                foreach (var p in s.League.FreeAgents.OrderByDescending(p => p.CurrentAbility)) FreeAgents.Add(PlayerRow.From(p, s, "FA"));
                foreach (var p in team.Roster.OrderByDescending(p => p.CurrentAbility)) Roster.Add(PlayerRow.From(p, s, team.Abbreviation));
                foreach (var p in s.World.Players.Where(p => p.CurrentAbility >= 95 || p.DraftRightsTeamId == team.Id).OrderByDescending(p => p.CurrentAbility).Take(200))
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
                foreach (var d in o.Report.Draft.Where(d => d.Team == team && (d.Player.TeamId == team.Id || s.League.FreeAgents.Contains(d.Player))))
                    Rights.Add(new RightsRow
                    {
                        Player = d.Player, Age = d.Player.Age(o.Date), Abroad = false,
                        Where = d.Player.TeamId == team.Id ? $"R{d.Round} #{d.Pick}, on your roster" : $"R{d.Round} #{d.Pick}, unsigned",
                        Next = WorldMarket.CannotStash(o, team, d.Player) ?? "Can be stashed abroad",
                    });
                foreach (var p in WorldMarket.RightsHeld(s.World, s.League, team))
                {
                    var club = WorldMarket.ClubOf(s.World, p);
                    Rights.Add(new RightsRow
                    {
                        Player = p, Age = p.Age(o.Date), Abroad = true,
                        Where = club != null ? $"Abroad: {club.FullName}" : "Free agent (only you can sign him)",
                        Next = club != null ? WorldMarket.Terms(s.World, o, team, p) is var t && t.Possible ? $"Can bring over: {t.Note}" : WorldMarket.Terms(s.World, o, team, p).Note
                                            : "Sign him from the free-agent list",
                    });
                }
                SelectedFreeAgent = FreeAgents.FirstOrDefault();
                break;
        }
    }

    [RelayCommand]
    public void ReSign()
    {
        if (SelectedExpiring == null) return;
        var ok = Session.Offseason!.ReSign(Session.HumanTeam, SelectedExpiring.Player);
        Status = ok ? $"Re-signed {SelectedExpiring.Name}."
            : Offseason.WontReSign(SelectedExpiring.Player) ? $"{SelectedExpiring.Name} is too unhappy here to sign a new deal."
            : $"{SelectedExpiring.Name}'s ask would take you over the {(Session.League.Finance.SoftCap ? "apron" : "cap")}.";
        Shell.AfterTick();
    }

    [RelayCommand]
    public void Release()
    {
        if (SelectedExpiring == null) return;
        Session.Offseason!.Release(Session.HumanTeam, SelectedExpiring.Player);
        Status = $"Released {SelectedExpiring.Name}.";
        Shell.AfterTick();
    }

    [RelayCommand]
    public void MakePick()
    {
        if (SelectedProspect == null || !OnTheClock) return;
        var pick = Session.MakePick(SelectedProspect.Player);
        Status = $"Pick {pick.Pick}: {pick.Player.Name}.";
        Shell.AfterTick();
        if (Session.Phase != SessionPhase.Offseason) Shell.Navigate("Inbox");
    }

    [RelayCommand]
    public void SignFreeAgent()
    {
        if (SelectedFreeAgent == null) return;
        var p = SelectedFreeAgent.Player;
        var o = Session.Offseason!;
        var team = Session.HumanTeam;
        var (route, _) = o.SigningOption(team, ContractMarket.MarketSalary(p.CurrentAbility, p.Age(o.Date), Session.League.Finance));
        var ok = o.SignFreeAgent(team, p);
        string how = route switch { SigningRoute.MidLevel => " with your mid-level exception", SigningRoute.Minimum => " on a minimum deal", _ => "" };
        Status = ok ? $"Signed {p.Name} for ${p.Contract!.Salary / 1e6:F1}M × {p.Contract.Years}{how}."
            : team.Roster.Count >= Session.League.Finance.RosterMax ? "Your roster is full. Waive someone first."
            : Session.League.Finance.SoftCap ? $"{p.Name} doesn't fit: not enough cap room, his ask is above the mid-level exception{(o.MidLevelUsed.Contains(team.Id) ? " (already used)" : "")}, and he won't take a minimum deal."
            : "Doesn't fit: not enough room.";
        Shell.AfterTick();
    }

    [RelayCommand]
    public void BuyOut()
    {
        var p = SelectedOverseas?.Player ?? (SelectedRights is { Abroad: true } r ? r.Player : null);
        if (p == null) return;
        var why = Session.BuyOut(p);
        Status = why == null ? $"{p.Name} is bought out and signed: ${p.Contract!.Salary / 1e6:F1}M × {p.Contract.Years}." : why;
        Shell.AfterTick();
    }

    [RelayCommand]
    public void BringOver()
    {
        if (SelectedRights is not { Abroad: true } r) { Status = "Select a player abroad whose rights you hold."; return; }
        var why = Session.BuyOut(r.Player);
        Status = why == null ? $"{r.Player.Name} comes over: ${r.Player.Contract!.Salary / 1e6:F1}M × {r.Player.Contract.Years}." : why;
        Shell.AfterTick();
    }

    [RelayCommand]
    public void Stash()
    {
        if (SelectedRights is not { Abroad: false } r) { Status = "Select one of this year's picks to stash."; return; }
        var why = Session.Stash(r.Player);
        Status = why ?? $"{r.Player.Name} will play abroad. You keep his rights.";
        Shell.AfterTick();
    }

    [RelayCommand]
    public void WaivePlayer()
    {
        if (SelectedRosterPlayer == null) return;
        Session.Offseason!.Waive(Session.HumanTeam, SelectedRosterPlayer.Player);
        Status = $"Waived {SelectedRosterPlayer.Name}.";
        Shell.AfterTick();
    }

    [RelayCommand]
    public void OpenPlayer(Player? p)
    {
        if (p != null) Shell.ShowPlayer(p);
    }
}
