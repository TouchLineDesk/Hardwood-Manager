using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Core.World;

namespace Hardwood.Desktop.ViewModels;

public sealed partial class ExpiringRow : ObservableObject
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
    // Run 67: the card.
    public Hardwood.Core.Model.Team? Team { get; init; }
    [ObservableProperty] private bool _picked;
    public string CardLine => $"{Pos} · {Age} · PA {PA}";
    public string AskChip { get; init; } = "";
    public Avalonia.Media.IBrush FitBrush => Fits switch { "Fits budget" => StandingRow.PlayoffBrush, "Over budget" or "Won't stay" => StandingRow.RelegationBrush, _ => SummerCards.Neutral };
    public string FitTip { get; init; } = "";
    public IReadOnlyList<SkillBar> Bars => SummerCards.Bars(Player, 1);
}

public sealed partial class ProspectRow : ObservableObject
{
    public required Player Player { get; init; }
    public int Rank { get; init; }
    [ObservableProperty] private bool _picked;
    /// <summary>Run 67: the draft board's card.</summary>
    public string CardLine => $"{Pos} · {Age} · {Height}";
    public string RankText => $"#{Rank}";
    public IReadOnlyList<SkillBar> Bars => SummerCards.Bars(Player, Knowledge);
    public string PaTip => Knowledge >= 0.999 ? "Potential: how good he can become" : "Potential as your scouts see him (a range until they know him well)";
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
    /// <summary>Danny's college draft (D40, real careers): the pool and the order, from the lottery until free agency.</summary>
    [ObservableProperty] private bool _showDraft;
    [ObservableProperty] private bool _onTheClock;
    [ObservableProperty] private string _draftText = "";
    public ObservableCollection<ProspectRow> DraftPool { get; } = new();
    [ObservableProperty] private ProspectRow? _selectedProspect;
    public ObservableCollection<DraftSlotRow> DraftSlots { get; } = new();
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

    // Run 67 (review #13 rank 6, Danny's #464): the boards open on player cards; the full tables are one click away.
    public string[] Views { get; } = { "Cards", "Full table" };
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowCards), nameof(ShowTable))] private string _view = "Cards";
    public bool ShowCards => View == "Cards";
    public bool ShowTable => !ShowCards;
    public ObservableCollection<FreeAgentRow> FreeAgentCards { get; } = new();
    public ObservableCollection<ProspectRow> DraftCards { get; } = new();
    [ObservableProperty] private string _freeAgentNote = "";
    [ObservableProperty] private string _draftNote = "";
    /// <summary>What the picked graduate means for you: press Draft now, or he's on your board until your turn.</summary>
    [ObservableProperty] private string _draftHint = "";
    [ObservableProperty] private string _draftButton = "Draft selected";

    partial void OnSelectedExpiringChanged(ExpiringRow? oldValue, ExpiringRow? newValue)
    {
        if (newValue == null) return;   // a cleared list (a refresh) keeps the talks panel and its answer
        Talk.Show(newValue.Player); MarkTalk();
    }
    partial void OnSelectedFreeAgentChanged(FreeAgentRow? oldValue, FreeAgentRow? newValue)
    {
        if (newValue == null) return;
        Talk.Show(newValue.Player); MarkTalk();
    }
    /// <summary>Light the card of the man in talks, on both boards (abroad, Contracts up and Free agents are open at once).</summary>
    private void MarkTalk()
    {
        foreach (var r in Expiring) r.Picked = r.Player == Talk.Player;
        foreach (var r in FreeAgents) r.Picked = r.Player == Talk.Player;
    }
    /// <summary>The graduate on top of your board. Kept apart from <see cref="SelectedProspect"/>, which the table
    /// clears whenever the pool is rebuilt.</summary>
    private Player? _boardPick;
    /// <summary>The summer's phase when the page last refreshed (H21).</summary>
    private OffseasonPhase? _talkPhase;
    partial void OnSelectedProspectChanged(ProspectRow? oldValue, ProspectRow? newValue)
    {
        if (newValue != null) _boardPick = newValue.Player;
        foreach (var r in DraftPool) r.Picked = newValue != null && r.Player == newValue.Player;
        UpdateDraftHint();
    }

    /// <summary>A card press (run 67).</summary>
    public void PickExpiring(ExpiringRow row)
    {
        if (SelectedExpiring == row) { Talk.Show(row.Player); MarkTalk(); }   // back to him after talking to someone else
        else SelectedExpiring = row;
    }
    public void PickFreeAgent(FreeAgentRow row)
    {
        if (SelectedFreeAgent == row) { Talk.Show(row.Player); MarkTalk(); }
        else SelectedFreeAgent = row;
    }
    public void PickProspect(ProspectRow row) => SelectedProspect = row;

    private void UpdateDraftHint()
    {
        var p = SelectedProspect?.Player;
        DraftButton = p != null && OnTheClock ? $"Draft {p.Name}" : "Draft selected";
        var o = Session.Offseason;
        if (p == null || o == null || !ShowDraft) { DraftHint = ""; return; }
        var mine = Enumerable.Range(o.NextPickIndex, Math.Max(0, o.PickOwners.Count - o.NextPickIndex)).Where(i => o.PickOwners[i] == Session.HumanTeam).Select(i => i + 1).ToList();
        DraftHint = OnTheClock ? $"Press Draft to take {p.Name} with pick {o.NextPickIndex + 1}."
            : mine.Count > 0 ? $"{p.Name} is top of your board. You pick #{mine[0]}; if he's still there, take him then."
            : $"You have no pick left this year, so {p.Name} will go to another club.";
    }

    public OffseasonViewModel(MainViewModel shell) : base(shell) => Talk = new TalkViewModel(shell, Refresh);

    public override void Refresh()
    {
        var s = Session; var o = s.Offseason;
        InOffseason = s.Phase == SessionPhase.Offseason && o != null;
        Expiring.Clear(); FreeAgents.Clear(); Roster.Clear(); Overseas.Clear(); DraftPool.Clear(); DraftSlots.Clear();
        FreeAgentCards.Clear(); DraftCards.Clear(); FreeAgentNote = DraftNote = DraftHint = "";
        ShowDraft = InOffseason && o!.CollegeDraftOn && o.Phase is OffseasonPhase.ReSigning or OffseasonPhase.Draft;
        OnTheClock = false; DraftText = "";
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
        Instructions = o.Phase == OffseasonPhase.Draft && o.CollegeDraftOn
            ? "The college draft: one round, in the lottery's order. When you're on the clock, pick a graduate on the Draft tab; press Continue and your scouts pick for you. Free agency opens when the draft ends."
            : (s.InNabl && renewing
                ? "Contract decisions: talk to each player whose deal is up (offer a wage and years; he accepts, counters or walks), or release him. Then continue to free agency."
                : "Free agency: make offers to free agents, including this year's college graduates (age 23). Players ask more to join a weaker team, and the owner won't let you go over the wage budget.")
            + (s.Abroad ? " Anyone you haven't renewed by the end of the summer leaves." : "");

        foreach (var p in s.MyExpiring)
        {
            s.MyStats.TryGetValue(p.Id, out var st);
            var d = s.PeekDemand(p);
            var fits = Offseason.WontReSign(p) ? "Won't stay" : s.MyCommittedWages + d.Wage <= team.Money.WageBudget ? "Fits budget" : "Over budget";
            Expiring.Add(new ExpiringRow
            {
                Player = p, Age = p.Age(o.Date), Team = team,
                LastSeason = st != null ? $"{st.Ppg:F1} pts · {st.Rpg:F1} reb · {st.Apg:F1} ast · {st.Mpg:F0} min" : "-",
                Ask = $"${d.Wage / 1e6:F2}M × {d.Years}",
                AskChip = SummerCards.AskChip(d),
                Fits = fits,
                FitTip = fits switch
                {
                    "Won't stay" => $"{p.Name} is too unhappy here to sign a new deal",
                    "Fits budget" => "His ask fits your wage budget with the deals you have",
                    _ => "His ask would take you over the wage budget; the owner won't allow it",
                },
                Picked = Talk.Player == p,
            });
        }
        if (freeAgency)
        {
            long room = s.MyWageRoom;
            foreach (var p in s.League.FreeAgents.OrderByDescending(p => p.CurrentAbility).Take(250))
            {
                var row = SummerCards.FreeAgent(s, p, o.Date, p.RookieSeason == o.Season + 1, room);
                row.Picked = Talk.Player == p;
                FreeAgents.Add(row);
            }
            foreach (var r in FreeAgents.Take(SummerCards.FreeAgentCount)) FreeAgentCards.Add(r);
            // Bid from the table on someone further down: his card goes first so the picked man is on screen.
            if (Talk.Player is { } tp && !FreeAgentCards.Any(r => r.Player == tp) && FreeAgents.FirstOrDefault(r => r.Player == tp) is { } extra)
                FreeAgentCards.Insert(0, extra);
            int all = s.League.FreeAgents.Count;
            FreeAgentNote = all == 0 ? "No free agents are left."
                : $"The best {Math.Min(all, SummerCards.FreeAgentCount)} of {all:N0} free agents by ability, with what each asks of your club. Pick a card to talk to him; the full table lists {Math.Min(all, 250)}.";
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
        if (ShowDraft) FillDraft(s, o, team);
        if (Tab == 0 && !ShowContracts) Tab = ShowDraft ? 4 : 1;
        if (Tab == 4 && !ShowDraft) Tab = 1;
        if (Talk.Player == null || (!s.MyExpiring.Contains(Talk.Player) && !s.League.FreeAgents.Contains(Talk.Player) && !Talk.Signed))
            Talk.Show(Expiring.FirstOrDefault()?.Player ?? FreeAgents.FirstOrDefault()?.Player);
        // H21 (run 75): a renewal panel left open into free agency read "New deal for …" with Release for a player
        // who had already gone to the pool. When the summer moves on and he is no longer yours to renew, the panel
        // reopens on him as he now is: "Sign …" for a free agent, without Release. (A Release press keeps its answer.)
        else if (o != null && o.Phase != _talkPhase && Talk.IsRenewal && !s.MyExpiring.Contains(Talk.Player))
            Talk.Show(Talk.Player);
        _talkPhase = o?.Phase;
        MarkTalk();
    }

    private void FillDraft(GameSession s, Offseason o, Hardwood.Core.Model.Team team)
    {
        OnTheClock = o.Phase == OffseasonPhase.Draft && o.OnTheClock == team;
        var mine = Enumerable.Range(0, o.PickOwners.Count).Where(i => o.PickOwners[i] == team).Select(i => i + 1).ToList();
        DraftText = OnTheClock ? $"You're on the clock with pick {o.NextPickIndex + 1}. Choose a graduate and press Draft, or press Continue and your scouts pick."
            : o.Phase == OffseasonPhase.ReSigning ? (mine.Count > 0 ? $"You pick {string.Join(" and ", mine.Select(i => "#" + i))}. The draft follows contract decisions." : "You have no pick this year. The draft follows contract decisions.")
            : "The draft is under way.";
        int rank = 1;
        var keep = _boardPick;
        foreach (var p in o.Prospects.OrderByDescending(p => Scouting.Potential(p, Scouting.Knowledge(team, p)).Mid).ThenByDescending(p => p.CurrentAbility))
            DraftPool.Add(new ProspectRow { Player = p, Rank = rank++, Age = p.Age(o.Date), Knowledge = Scouting.Knowledge(team, p) });
        foreach (var r in DraftPool.Take(SummerCards.DraftCount)) DraftCards.Add(r);
        if (keep != null && !DraftCards.Any(r => r.Player == keep) && DraftPool.FirstOrDefault(r => r.Player == keep) is { } further) DraftCards.Add(further);
        DraftNote = DraftPool.Count == 0 ? "Every graduate has been drafted."
            : DraftPool.Count <= SummerCards.DraftCount ? $"All {DraftPool.Count} graduates, in your scouts' order (potential first). Pick a card to put him top of your board."
            : $"Your scouts' top {SummerCards.DraftCount} of {DraftPool.Count} graduates, potential first. Pick a card to put him top of your board; the full table lists them all.";
        var made = o.Report.Draft.ToDictionary(d => d.Pick);
        for (int i = 0; i < o.PickOwners.Count; i++)
        {
            made.TryGetValue(i + 1, out var d);
            bool extra = i == o.PickOwners.Count - 1 && o.Outcome.ThirdPlaceWinner == o.PickOwners[i];
            DraftSlots.Add(new DraftSlotRow
            {
                Pick = i + 1, Team = o.PickOwners[i].FullName + (extra ? " (extra pick)" : ""),
                Player = d != null ? $"{d.Player.Name} ({d.Player.PrimaryPosition})" : i == o.NextPickIndex && o.Phase == OffseasonPhase.Draft ? "On the clock" : "",
                Ours = o.PickOwners[i] == team,
            });
        }
        // Keep your pick across a refresh (the rows are rebuilt); he may have gone to another club meanwhile.
        SelectedProspect = DraftPool.FirstOrDefault(r => r.Player == keep) ?? DraftPool.FirstOrDefault();
        UpdateDraftHint();
    }

    [RelayCommand]
    public void Draft()
    {
        var p = SelectedProspect?.Player;
        if (p == null || !OnTheClock) return;
        var pick = Session.MakePick(p);
        Status = $"You drafted {p.Name} with pick {pick.Pick}: ${p.Contract!.Salary / 1e6:F2}M a year for {p.Contract.Years} years.";
        _boardPick = null;
        SelectedProspect = null;
        Shell.AfterTick();
        Refresh();
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

/// <summary>One pick in the college draft (D40): who holds it and who was taken.</summary>
public sealed class DraftSlotRow
{
    public int Pick { get; init; }
    public string Team { get; init; } = "";
    public string Player { get; init; } = "";
    public bool Ours { get; init; }
    public string Mark => Ours ? "You" : "";
}

/// <summary>A free agent with his ask of your club.</summary>
public sealed partial class FreeAgentRow : ObservableObject
{
    public required Player Player { get; init; }
    public required ContractDemand Ask { get; init; }
    // Run 67: the card (SummerCards.FreeAgent fills these).
    [ObservableProperty] private bool _picked;
    public string CardName => Player.Name;
    public string CardLine => $"{Pos} · {Age} · PA {PA}";
    public string AskChip => SummerCards.AskChip(Ask);
    public string AskTip => $"His agent asks {SummerCards.Money(Ask.Wage)} a year for {Ask.Years} year{(Ask.Years == 1 ? "" : "s")} from your club";
    public string FitText { get; init; } = "";
    public Avalonia.Media.IBrush FitBrush { get; init; } = SummerCards.Neutral;
    public string FitTip { get; init; } = "";
    public IReadOnlyList<SkillBar> Bars => SummerCards.Bars(Player, Knowledge);
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
