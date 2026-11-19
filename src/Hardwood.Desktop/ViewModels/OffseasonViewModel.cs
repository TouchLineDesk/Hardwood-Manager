using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Core.World;

namespace Hardwood.Desktop.ViewModels;

/// <summary>H114 (run 118): a player's positions as the roster writes them ("SG/SF"), so a summer card and its row agree.</summary>
public static class PosText
{
    public static string Of(Player p) => p.PrimaryPosition + (p.SecondaryPosition is { } s && s != p.PrimaryPosition ? "/" + s : "");
}

public sealed partial class ExpiringRow : ObservableObject
{
    public required Player Player { get; init; }
    public string Name => Player.Name;
    public string Pos => PosText.Of(Player);
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
    public Avalonia.Media.IBrush FitBrush => Fits switch { "Fits on its own" or "Owner stretches" => StandingRow.PlayoffBrush, "Over budget" or "Won't stay" => StandingRow.RelegationBrush, _ => SummerCards.Neutral };
    public string FitTip { get; init; } = "";
    /// <summary>H111 (run 117): the fit chip shows only when the cards differ (every card read "Fits on its own").</summary>
    [ObservableProperty] private bool _showFit = true;
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
    public string Pos => PosText.Of(Player);
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
    /// <summary>H18 (run 100): the side panel's scouting card on the Draft tab.</summary>
    public int Of { get; init; }
    public string BoardLine => $"No. {Rank} of {Of} on your scouts' board · {Pos} · age {Age} · {Height}";
    public string ScoutLine => Knowledge >= 0.999 ? "Your scouts know him well: these are his true ratings."
        : $"Your scouts' read ({Knowledge * 100:0}% known): ratings are ranges until they see more of him.";
}

public sealed class PickRow
{
    public required DraftPick Pick { get; init; }
    public string Where => $"R{Pick.Round} #{Pick.Pick}";
    public string Team => Pick.Team.Abbreviation + (Pick.OriginalTeam != null && Pick.OriginalTeam != Pick.Team ? $" (via {Pick.OriginalTeam.Abbreviation})" : "");
    public string Player => Pick.Player.Name;
    public string Pos => PosText.Of(Pick.Player);
    public string Ability => $"{Pick.Player.CurrentAbility}/{Pick.Player.PotentialAbility}";
    public bool Ours { get; init; }
}

/// <summary>A player under contract overseas whom an NABL team could buy out (decision #53).</summary>
public sealed class OverseasRow
{
    public required Player Player { get; init; }
    public string Name => Player.Name;
    public string Pos => PosText.Of(Player);
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
    public string Pos => PosText.Of(Player);
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
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowProspectPanel), nameof(ShowTalkPanel))] private bool _showDraft;
    [ObservableProperty] private bool _onTheClock;
    [ObservableProperty] private string _draftText = "";
    public ObservableCollection<ProspectRow> DraftPool { get; } = new();
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasProspect))] private ProspectRow? _selectedProspect;
    public bool HasProspect => SelectedProspect != null;
    public ObservableCollection<DraftSlotRow> DraftSlots { get; } = new();
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _capText = "";
    /// <summary>H69: the going-down line under the budget (empty otherwise).</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasRelegationText))] private string _relegationText = "";
    public bool HasRelegationText => RelegationText.Length > 0;
    /// <summary>H112: the relegation cut in full, behind the banner's "How it works".</summary>
    [ObservableProperty] private string _relegationDetail = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowProspectPanel), nameof(ShowTalkPanel))] private int _tab;
    /// <summary>H37 (run 79): a college coach's summer has no contracts, free agents or talks.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowTalk), nameof(ShowProspectPanel), nameof(ShowTalkPanel))] private bool _collegeSummer;
    public bool ShowTalk => !CollegeSummer;
    /// <summary>The Draft tab's index in the summer's tabs.</summary>
    public const int DraftTab = 4;
    /// <summary>H18 (run 100): on the Draft tab the side panel is the picked graduate's scouting card, not the last
    /// contract talk ("Sign Luka Garza" while you were on the clock).</summary>
    public bool ShowProspectPanel => ShowTalk && ShowDraft && Tab == DraftTab;
    public bool ShowTalkPanel => ShowTalk && !ShowProspectPanel;
    /// <summary>The side panel a college coach sees in place of contract talks.</summary>
    [ObservableProperty] private string _collegeSummerText = "";
    /// <summary>Pros release players to free agency; a college program has no one to release to.</summary>
    public bool CanReleaseFromRoster => !CollegeSummer;

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
    /// <summary>The summer's phase when the page last refreshed (H28).</summary>
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
        if (CollegeSummer)
        {
            // A program doesn't pick; a press still says who he is on the pros' board (the bug finder's "no feedback").
            var row = DraftPool.FirstOrDefault(r => r.Player == p);
            DraftHint = row == null ? "" : $"{p.Name}: No. {row.Rank} of {DraftPool.Count} on the pros' board ({row.Pos}, potential {row.PA}).";
            return;
        }
        var mine = Enumerable.Range(o.NextPickIndex, Math.Max(0, o.PickOwners.Count - o.NextPickIndex)).Where(i => o.PickOwners[i] == Session.HumanTeam).Select(i => i + 1).ToList();
        DraftHint = OnTheClock ? $"Press Draft to take {p.Name} with pick {o.NextPickIndex + 1}."
            : mine.Count > 0 ? $"{p.Name} is top of your board. You pick #{mine[0]}; if he's still there, take him then."
            : $"You have no pick left this year, so {p.Name} will go to another club.";
    }

    public OffseasonViewModel(MainViewModel shell) : base(shell) => Talk = new TalkViewModel(shell, Refresh);

    public override void Refresh()
    {
        PendingRelease = null;   // H24: a question left open doesn't carry into the next phase or summer (code review)
        var s = Session; var o = s.Offseason;
        InOffseason = s.Phase == SessionPhase.Offseason && o != null;
        Expiring.Clear(); FreeAgents.Clear(); Roster.Clear(); Overseas.Clear(); DraftPool.Clear(); DraftSlots.Clear();
        FreeAgentCards.Clear(); DraftCards.Clear(); FreeAgentNote = DraftNote = DraftHint = "";
        ShowDraft = InOffseason && o!.CollegeDraftOn && o.Phase is OffseasonPhase.ReSigning or OffseasonPhase.Draft;
        OnTheClock = false; DraftText = "";
        CollegeSummer = false; CollegeSummerText = "";
        OnPropertyChanged(nameof(CanReleaseFromRoster));
        if (!InOffseason || !s.Employed)
        {
            Headline = InOffseason ? "Summer" : "The offseason has not started.";
            Instructions = !InOffseason
                ? (s.Phase == SessionPhase.Postseason ? "The playoffs are under way. Once the season ends, contract talks and free agency happen here."
                                                      : "Play the season out. In the summer you will renew or release players whose deals are up and sign free agents here.")
                  + (s.SummerPreview() is { Length: > 0 } preview ? "\n" + preview : "")
                : "You aren't coaching a club. Press Continue to move through the summer.";
            ShowContracts = ShowFreeAgency = ShowBuyouts = false;
            CapText = ""; RelegationText = ""; RelegationDetail = "";
            return;
        }
        var team = s.HumanTeam; var fin = s.League.Finance;
        if (s.InCollege) { RelegationText = ""; RelegationDetail = ""; RefreshCollegeSummer(s, o!, team); return; }
        bool renewing = s.InNabl ? o!.Phase == OffseasonPhase.ReSigning : true;
        bool freeAgency = s.Abroad || o!.Phase == OffseasonPhase.FreeAgency;
        ShowContracts = s.MyExpiring.Count > 0 || (s.InNabl && renewing);
        ShowFreeAgency = freeAgency;
        ShowBuyouts = s.InNabl && freeAgency;
        long committed = s.MyCommittedWages, budget = team.Money.WageBudget;
        static string M(long v) => TalkViewModel.M(v);   // H84: the talk panel writes money the same way
        CapText = $"Wages committed {M(committed)} of a {M(budget)} budget · {(budget >= committed ? $"{M(budget - committed)} to spend" : $"{M(committed - budget)} over")}"
                  + (s.InCollege ? "" : $" · roster {team.Roster.Count - s.MyExpiring.Count} under contract (needs {s.MyRosterMin}-{s.MyRosterMax})");
        // H69 (run 106): a club going down is told so, with next season's league and what its contracts come to there; the
        // budget above is still this league's until the summer ends.
        // H112 (run 118): one line, with how the cut works behind "How it works" rather than two lines of red text.
        if (s.RelegationOutlook() is { } down)
        {
            string league = down.League.StartsWith("The ") ? down.League : "the " + down.League;
            RelegationText = $"Going down to {league}: contracts cut to about {M(down.PayrollAfter)} when the summer ends.";
            RelegationDetail = $"Relegation wage cut: when the summer ends you play in {league}, and every contract is cut to that league's level, about {M(down.PayrollAfter)} in all."
                + (down.OwnerFunds ? " The owner sets a smaller budget that covers at least them." : " The owner, in debt, sets a smaller budget.")
                + " Deals you sign now are cut the same way. The budget shown above is this league's until then.";
        }
        else { RelegationText = ""; RelegationDetail = ""; }
        Headline = $"Summer {o!.Season + 1} · {team.FullName}";
        Instructions = o.Phase == OffseasonPhase.Draft && o.CollegeDraftOn
            ? "The college draft: one round, in the lottery's order. When you're on the clock, pick a graduate on the Draft tab; press Continue and your scouts pick for you. Free agency opens when the draft ends."
            // H95 (run 109): by phase, not by league: a club outside the NBA (a lower tier or abroad) renews and signs free agents at
            // once, so while its contracts are up in the re-signing step it reads both, the contract decisions first.
            : !s.InNabl && o.Phase == OffseasonPhase.ReSigning && s.MyExpiring.Count > 0
                ? "Contract decisions and free agency: talk to each player whose deal is up (offer a wage and years; he accepts, counters or walks), and make offers to free agents, including this year's college graduates (age 23). The owner won't let you go over the wage budget. Anyone you haven't renewed by the end of the summer leaves."
            : (s.InNabl && renewing
                ? "Contract decisions: talk to each player whose deal is up (offer a wage and years; he accepts, counters or walks), or release him. Then continue to free agency."
                : "Free agency: make offers to free agents, including this year's college graduates (age 23). Players ask more to join a weaker team, and the owner won't let you go over the wage budget.")
            + (s.Abroad ? " Anyone you haven't renewed by the end of the summer leaves." : "");

        foreach (var p in s.MyExpiring)
        {
            s.MyStats.TryGetValue(p.Id, out var st);
            var d = s.PeekDemand(p);
            // H40 (run 79): each ask is checked on its own against the room left, so say so: five asks can each fit and not all together.
            // H122 (run 122): the owner stretches the budget for your own young star; the card says so.
            long stretch = s.MyStretchFor(p);
            var fits = Offseason.WontReSign(p) ? "Won't stay" : s.MyCommittedWages + d.Wage <= team.Money.WageBudget ? "Fits on its own"
                     : stretch > 0 && s.CanKeep(p, d.Wage) ? "Owner stretches" : "Over budget";
            Expiring.Add(new ExpiringRow
            {
                Player = p, Age = p.Age(s.Today), Team = team,   // H38: today's age, as on every other page
                LastSeason = st != null ? $"{st.Ppg:F1} pts · {st.Rpg:F1} reb · {st.Apg:F1} ast · {st.Mpg:F0} min" : "-",
                Ask = $"${d.Wage / 1e6:F2}M × {d.Years}",
                AskChip = SummerCards.AskChip(d),
                Fits = fits,
                FitTip = fits switch
                {
                    "Won't stay" => $"{p.Name} is too unhappy here to sign a new deal",
                    "Owner stretches" => $"His ask is over your wage budget, but he's your own young star (CA {ContractRules.YoungStarAbility}+, under {ContractRules.YoungStarMaxAge + 1}): the owner will go up to {M(stretch)} over the budget to keep him, and raises the budget to cover it.",
                    "Fits on its own" => $"His ask fits your wage budget with the deals you have now ({M(Math.Max(0, team.Money.WageBudget - s.MyCommittedWages))} to spend). Each card is checked on its own: keeping several takes the room of all of them.",
                    _ => stretch > 0
                        ? $"His ask would take you over the wage budget even with the {M(stretch)} the owner adds to keep his own young star"
                        : "His ask would take you over the wage budget; the owner won't allow it",
                },
                Picked = Talk.Player == p,
            });
        }
        if (Expiring.Count > 1 && Expiring.All(x => x.Fits == "Fits on its own"))
        {
            foreach (var x in Expiring) x.ShowFit = false;
            // Review of #181: the hidden chips carried H40's caveat, so the page says it once, with the sum.
            long room = Math.Max(0, team.Money.WageBudget - s.MyCommittedWages);
            long all = s.MyExpiring.Where(p => !Offseason.WontReSign(p)).Sum(p => s.PeekDemand(p).Wage);
            Instructions += all <= room
                ? $" Every ask fits your wage room ({M(room)}), all of them together too ({M(all)})."
                : $" Each ask fits your wage room ({M(room)}) on its own, but keeping them all would take {M(all)}.";
        }
        if (freeAgency)
        {
            long room = s.MyWageRoom;
            foreach (var p in s.League.FreeAgents.OrderByDescending(p => p.CurrentAbility).Take(250))
            {
                var row = SummerCards.FreeAgent(s, p, s.Today, p.RookieSeason == o.Season + 1, room);
                row.Picked = Talk.Player == p;
                FreeAgents.Add(row);
            }
            foreach (var r in FreeAgents.Take(SummerCards.FreeAgentCount)) FreeAgentCards.Add(r);
            // Bid from the table on someone further down: his card is added after the best, not ahead of them (H18, run 100:
            // a CA 118 player you'd talked to sat first, before the 149s), and the note says why he's there.
            FreeAgentRow? extra = null;
            if (Talk.Player is { } tp && !FreeAgentCards.Any(r => r.Player == tp) && FreeAgents.FirstOrDefault(r => r.Player == tp) is { } further)
                FreeAgentCards.Add(extra = further);
            int all = s.League.FreeAgents.Count;
            FreeAgentNote = all == 0 ? "No free agents are left."
                : $"The best {Math.Min(all, SummerCards.FreeAgentCount)} of {all:N0} free agents by ability, with what each asks of your club. Pick a card to talk to him; the full table lists {Math.Min(all, 250)}."
                  + (extra != null ? $" {extra.Player.Name}, whom you're talking to, is last." : "");
        }
        foreach (var p in team.Roster.OrderByDescending(p => p.CurrentAbility)) Roster.Add(PlayerRow.From(p, s, team.Abbreviation));
        if (ShowBuyouts)
            foreach (var p in s.World.Players.Where(p => p.CurrentAbility >= 95).OrderByDescending(p => p.CurrentAbility).Take(200))
            {
                var club = WorldMarket.ClubOf(s.World, p);
                var terms = WorldMarket.Terms(s.World, o, team, p);
                Overseas.Add(new OverseasRow
                {
                    Player = p, Age = p.Age(s.Today), Knowledge = Scouting.Knowledge(team, p),
                    Club = club == null ? "" : $"{club.FullName} ({s.World.HomeOf(club).Profile.Short})",
                    Terms = terms.Possible ? $"Yes: ${terms.NablSalary / 1e6:F1}M a year. {terms.Note}" : terms.Note,
                });
            }
        if (ShowDraft) FillDraft(s, o, team);
        if (Tab == 0 && !ShowContracts) Tab = ShowDraft ? DraftTab : 1;
        if (Tab == DraftTab && !ShowDraft) Tab = 1;
        if (Talk.Player == null || (!s.MyExpiring.Contains(Talk.Player) && !s.League.FreeAgents.Contains(Talk.Player) && !Talk.Signed))
            Talk.Show(Expiring.FirstOrDefault()?.Player ?? FreeAgents.FirstOrDefault()?.Player);
        // H28 (run 75): a renewal panel left open into free agency read "New deal for …" with Release for a player
        // who had already gone to the pool. When the summer moves on and he is no longer yours to renew, the panel
        // reopens on him as he now is: "Sign …" for a free agent, without Release. (A Release press keeps its answer.)
        else if (o != null && o.Phase != _talkPhase && Talk.IsRenewal && !s.MyExpiring.Contains(Talk.Player))
            Talk.Show(Talk.Player);
        _talkPhase = o?.Phase;
        Talk.RefreshBudget();   // H84 (run 103): the panel's budget follows the header after a signing
        MarkTalk();
    }

    /// <summary>
    /// H37 (bug hunt, Nov 16; run 79): a college coach's summer. Programs don't sign professionals or pay wages, so the
    /// pro header, Contracts up, Free agents and the talks panel are replaced by what did happen (seniors graduated, the
    /// signed class enrolled) and what's next (the recruiting trail on the College page).
    /// </summary>
    private void RefreshCollegeSummer(GameSession s, Offseason o, Hardwood.Core.Model.Team team)
    {
        CollegeSummer = true;
        OnPropertyChanged(nameof(CanReleaseFromRoster));
        ShowContracts = ShowFreeAgency = ShowBuyouts = false;
        Headline = $"Summer {o.Season + 1} · {team.FullName}";
        Instructions = "A college summer has no contracts, wages or free agents. Seniors graduate into the pros and the signed class enrolls when the summer begins. "
                     + "The pros' draft and free agency run until the new season: press Continue to move the summer along.";
        var c = s.World.College;
        int walkOns = c == null ? 0 : team.Roster.Count(p => c.WalkOnIds.Contains(p.Id));
        CapText = c is { Recruiting: true }
            ? $"Roster {team.Roster.Count} of {Hardwood.Core.College.CollegeWorld.Squad} · {team.Roster.Count - walkOns} on scholarship (of {Hardwood.Core.College.CollegeWorld.Scholarships})"
              + (walkOns > 0 ? $" · {walkOns} walk-on{(walkOns == 1 ? "" : "s")}" : "")
            : $"Roster {team.Roster.Count} players";
        foreach (var p in team.Roster.OrderByDescending(p => p.CurrentAbility)) Roster.Add(PlayerRow.From(p, s, team.Abbreviation));
        var lines = new List<string>();
        if (c is { Recruiting: true })
        {
            int cls = c.RecruitingClass(s.Today);
            int open = c.OpenScholarships(team, cls), commits = c.Commits(team, cls);
            lines.Add($"Class of {cls}: {commits} committed to you, {open} scholarship{(open == 1 ? "" : "s")} still open.");
            lines.Add("Recruit the class on the College page (Recruiting). Rising seniors can be courted now; they commit from September 1.");
        }
        if (s.Inbox.Any(m => m.Title == "Graduation and enrollment" && m.Date.Year == o.Season + 1))
            lines.Add("Who left and who joined is in your inbox (\"Graduation and enrollment\").");
        if (ShowDraft) lines.Add("The pros' draft is on the Draft tab: watch where your graduates go. College programs don't draft.");
        CollegeSummerText = string.Join("\n\n", lines);
        if (ShowDraft)
        {
            FillDraft(s, o, team);
            DraftText = o.Phase == OffseasonPhase.Draft ? "The pros' draft is under way. College programs don't pick; your graduates can be taken."
                                                        : "The pros' draft follows their contract decisions. College programs don't pick; your graduates can be taken.";
            DraftNote = DraftPool.Count == 0 ? "Every graduate has been drafted." : $"The best {DraftPool.Count} graduates, in the pros' scouts' order (potential first). Pick a card to see where he stands.";
        }
        if (Tab is 0 or 1 or 2) Tab = 3;
        if (Tab == 4 && !ShowDraft) Tab = 3;
        Talk.Show(null);
    }

    /// <summary>H50 (bug hunt, Nov 16; run 82): the draft board gives a graduate's age on July 1, the day classes graduate
    /// and the rules count "turn pro at 22" from, so a May graduate reads 22, not 21 (D80 A). Other Offseason lists keep today's age (H38).</summary>
    public static DateOnly DraftAgeDay(DateOnly today) => today.Month < 7 ? new DateOnly(today.Year, 7, 1) : today;

    private void FillDraft(GameSession s, Offseason o, Hardwood.Core.Model.Team team)
    {
        OnTheClock = o.Phase == OffseasonPhase.Draft && o.OnTheClock == team;
        var mine = Enumerable.Range(0, o.PickOwners.Count).Where(i => o.PickOwners[i] == team).Select(i => i + 1).ToList();
        DraftText = OnTheClock ? $"You're on the clock with pick {o.NextPickIndex + 1}. Choose a graduate and press Draft, or press Continue and your scouts pick."
            : o.Phase == OffseasonPhase.ReSigning ? (mine.Count > 0 ? $"You pick {string.Join(" and ", mine.Select(i => "#" + i))}. The draft follows contract decisions." : (s.InNabl ? GameSession.NoPickReason : GameSession.NoPickReasonOutsideNba) + " The draft follows contract decisions.")
            : "The draft is under way.";
        // H74 (run 106): every graduate is the same age by the rule; say so, so a page of 22-year-olds doesn't look like a bug.
        // D80 A (run 128): four years, so 22 (a career from before it also has 23-year-olds in its first summer after).
        DraftText += " College players turn pro after four years, at 22, so the prospects are 22.";
        int rank = 1;
        var keep = _boardPick;
        foreach (var p in o.Prospects.OrderByDescending(p => Scouting.Potential(p, Scouting.Knowledge(team, p)).Mid).ThenByDescending(p => p.CurrentAbility))
            DraftPool.Add(new ProspectRow { Player = p, Rank = rank++, Of = o.Prospects.Count, Age = p.Age(DraftAgeDay(Session.Today)), Knowledge = Scouting.Knowledge(team, p) });
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
        Status = $"You drafted {p.Name} with pick {pick.Pick}: ${p.Contract!.Salary / 1e6:F2}M a year for {p.Contract.Years} years."
            + (Session.League.Finance.OpenMarket ? " A pick is compulsory, so the owner adds his wage to your budget." : "");   // H89 (run 106)
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

    // H24 (bug hunt, Nov 16): Release selected asks first, as Waive does.
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ConfirmingRelease), nameof(ReleaseQuestion))] private Player? _pendingRelease;
    public bool ConfirmingRelease => PendingRelease != null;
    partial void OnSelectedRosterPlayerChanged(PlayerRow? value) { if (value?.Player != PendingRelease) PendingRelease = null; }
    public string ReleaseQuestion => PendingRelease is { } r
        ? $"Release {r.Name}? He becomes a free agent at once and any club can sign him. You can't undo it."
        : "";

    /// <summary>Release selected: asks first (<see cref="ConfirmRelease"/> does it).</summary>
    [RelayCommand]
    public void WaivePlayer()
    {
        if (SelectedRosterPlayer == null) return;
        PendingRelease = SelectedRosterPlayer.Player;
    }

    [RelayCommand]
    public void CancelRelease()
    {
        if (PendingRelease is { } r) Status = $"{r.Name} stays.";
        PendingRelease = null;
    }

    [RelayCommand]
    public void ConfirmRelease()
    {
        if (PendingRelease is not { } p) return;
        PendingRelease = null;
        if (!Session.Employed || !Session.HumanTeam.Roster.Contains(p)) return;
        if (Session.MyExpiring.Contains(p)) Session.ReleaseExpiring(p);
        else if (Session.InNabl && Session.Offseason is { } summer) summer.Waive(Session.HumanTeam, p);
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
    public string Pos => PosText.Of(Player);
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
