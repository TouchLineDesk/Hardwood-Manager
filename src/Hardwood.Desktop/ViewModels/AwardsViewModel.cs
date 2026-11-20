using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Hardwood.Core.Model;
using Hardwood.Core.Season;

namespace Hardwood.Desktop.ViewModels;

public sealed class VoteRow
{
    public Player? Player { get; init; }
    public Team? Club { get; init; }
    public double ShareValue { get; init; }
    public int Rank { get; init; }
    public string Name { get; init; } = "";
    public string Team { get; init; } = "";
    public string Share { get; init; } = "";
    public string Points { get; init; } = "";
    public string StatLine { get; init; } = "";
}

/// <summary>Run 75 (review #13 rank 12): one award's winner as a trophy card.</summary>
public sealed class WinnerCard
{
    public string Award { get; init; } = "";
    public string Icon { get; init; } = "Trophy";
    public Player? Player { get; init; }
    public Team? Club { get; init; }
    public string Name { get; init; } = "";
    public string TeamLine { get; init; } = "";
    public string StatLine { get; init; } = "";
    public string Vote { get; init; } = "";
    public bool HasVote => Vote.Length > 0;
    public bool IsCoach { get; init; }
    public bool HasPortrait => !IsCoach && Player != null;
    public bool Mine { get; init; }
    public IBrush Stripe { get; init; } = Brushes.Gray;
    public string Tip => $"{Award}: {Name}{(TeamLine.Length > 0 ? $", {TeamLine}" : "")}{(StatLine.Length > 0 ? $". {StatLine}" : "")}";
}

/// <summary>One line of an award's vote ladder: the share as a bar.</summary>
public sealed class LadderRow
{
    public int Rank { get; init; }
    public string Name { get; init; } = "";
    public string Team { get; init; } = "";
    public string Share { get; init; } = "";
    public double BarWidth { get; init; }
    public bool Winner { get; init; }
    public double BarOpacity => Winner ? 1.0 : 0.55;
    public bool Mine { get; init; }
    public string Tip { get; init; } = "";
}

public sealed class LadderCard
{
    public string Award { get; init; } = "";
    public List<LadderRow> Rows { get; init; } = new();
    public string More { get; init; } = "";
    public bool HasMore => More.Length > 0;
}

/// <summary>A player on an All-League team or an All-Star roster, as a chip.</summary>
public sealed class HonourChip
{
    public Player? Player { get; init; }
    public Team? Club { get; init; }
    public string Name { get; init; } = "";
    public string Team { get; init; } = "";
    public bool Mine { get; init; }
    public bool Star { get; init; }
    /// <summary>What the star means: the All-Star Game MVP, or a conference's player of the year (CS5).</summary>
    public string StarTip { get; init; } = "All-Star Game MVP";
}

public sealed class HonourLine
{
    public string Title { get; init; } = "";
    public List<HonourChip> Players { get; init; } = new();
}

public sealed class HallRow
{
    public required Player Player { get; init; }
    public Team? Club { get; init; }
    public string Initials => string.Concat(Player.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => w[0]));
    public string Inducted => $"Inducted {Year}";
    public string Name => Player.Name;
    public int Year { get; init; }
    public string Career { get; init; } = "";
    public string Honours { get; init; } = "";
    public double Score { get; init; }
}

public sealed partial class AwardsViewModel : PageViewModel
{
    public override string Title => "Awards";
    public ObservableCollection<string> Seasons { get; } = new();
    [ObservableProperty] private string? _season;
    [ObservableProperty] private string _headline = "";
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _allLeague = "";
    [ObservableProperty] private string _allStars = "";
    public ObservableCollection<VoteRow> Mvp { get; } = new();
    public ObservableCollection<VoteRow> Roy { get; } = new();
    public ObservableCollection<VoteRow> Dpoy { get; } = new();
    public ObservableCollection<VoteRow> SixthMan { get; } = new();
    public ObservableCollection<HallRow> Hall { get; } = new();
    [ObservableProperty] private string _hallNote = "";

    // Run 75 (review #13 rank 12): the winners as trophy cards, each vote as a ladder of bars, the teams as chips.
    public string[] Views { get; } = { "Cards", "Full table" };
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowCards), nameof(ShowTable))] private string _view = "Cards";
    public bool ShowCards => View == "Cards";
    public bool ShowTable => !ShowCards;
    public ObservableCollection<WinnerCard> Winners { get; } = new();
    public ObservableCollection<LadderCard> Ladders { get; } = new();
    public ObservableCollection<HonourLine> AllLeagueTeams { get; } = new();
    public ObservableCollection<HonourLine> AllStarTeams { get; } = new();
    [ObservableProperty] private string _allStarLine = "";
    [ObservableProperty] private bool _hasWinners;
    [ObservableProperty] private bool _hasLadders;
    [ObservableProperty] private bool _hasAllLeague;
    [ObservableProperty] private bool _hasAllStars;
    /// <summary>A new real career has no awards of its own yet: the real last season's winners stand in.</summary>
    [ObservableProperty] private string _emptyNote = "";
    [ObservableProperty] private bool _hasEmptyNote;
    [ObservableProperty] private bool _hasAllStarPanel;
    [ObservableProperty] private bool _hasSeason;

    /// <summary>The widest a ladder bar gets (the winner's share is scaled against 100%).</summary>
    public const double LadderBarMax = 120;

    public AwardsViewModel(MainViewModel shell) : base(shell) { }

    // CS5 / K14 (run 135): a college coach's Awards page is the college game's: the national awards, the All-America
    // teams and the all-conference teams, not the NBA's.
    [ObservableProperty, NotifyPropertyChangedFor(nameof(MvpTitle), nameof(RoyTitle), nameof(ShowSixth), nameof(AllLeagueTitle), nameof(AllStarTitle), nameof(ShowHall))]
    private bool _isCollege;
    public string MvpTitle => IsCollege ? "National Player of the Year" : "Most Valuable Player";
    public string RoyTitle => IsCollege ? "Freshman of the Year" : "Rookie of the Year";
    public bool ShowSixth => !IsCollege;
    public string AllLeagueTitle => IsCollege ? "All-America teams" : "All-League teams";
    public string AllStarTitle => IsCollege ? "All-conference first teams" : "All-Star Game";
    public bool ShowHall => !IsCollege;

    partial void OnSeasonChanged(string? value) => Show();

    public override void Refresh()
    {
        var current = Season;
        Seasons.Clear();
        IsCollege = Session.CollegeWords && Session.World.College != null;
        if (IsCollege)
        {
            foreach (var a in Session.World.College!.Awards.OrderByDescending(a => a.Year)) Seasons.Add(Hardwood.Core.College.CollegeAwards.SeasonText(a.Year));
            Season = Seasons.Contains(current ?? "") ? current : Seasons.FirstOrDefault();
            Hall.Clear(); HallNote = "";
            Show();
            return;
        }
        foreach (var h in Session.League.History.OrderByDescending(h => h.Season)) Seasons.Add($"{h.Season}-{(h.Season + 1) % 100:00}");
        if (Session.Midseason != null && Session.Phase != SessionPhase.Offseason) Seasons.Insert(0, $"{Session.League.Season}-{(Session.League.Season + 1) % 100:00} (in progress)");
        Season = Seasons.Contains(current ?? "") ? current : Seasons.FirstOrDefault();
        FillHall();
        Show();
    }

    private void Show()
    {
        Mvp.Clear(); Roy.Clear(); Dpoy.Clear(); SixthMan.Clear();
        Winners.Clear(); Ladders.Clear(); AllLeagueTeams.Clear(); AllStarTeams.Clear(); AllStarLine = ""; EmptyNote = ""; ExtraCards.Clear();
        if (IsCollege) { try { ShowCollege(); } finally { BuildCards(); } return; }
        var s = Session;
        var all = s.League.AllActivePlayers.Concat(s.League.FreeAgents).Concat(s.League.Retired).ToDictionary(p => p.Id);
        string Name(int? id) => id is { } i && all.TryGetValue(i, out var p) ? p.Name : "-";
        Team? ClubOf(int? id) => id is { } i && s.Stats.TryGetValue(i, out var st) ? s.FindTeam(st.TeamId)
            : id is { } j && all.TryGetValue(j, out var p) && p.TeamId is { } t ? s.FindTeam(t) : null;
        string TeamOf(int? id) => ClubOf(id)?.Abbreviation ?? "FA";
        Player? P(int? id) => id is { } i && all.TryGetValue(i, out var p) ? p : null;
        // A past season's club comes from its vote lines (the team he played for then), never today's club:
        // a winner who has since moved must not take your colours or your accent border (code review, run 75).
        var thenClub = new Dictionary<int, Team>();
        bool inProgress = Season?.EndsWith("(in progress)") == true;
        Team? ClubAt(int id, string abbr)
        {
            var t = s.League.Teams.FirstOrDefault(x => x.Abbreviation == abbr) ?? (inProgress ? ClubOf(id) : null);
            if (t != null) thenClub[id] = t;
            return t;
        }
        Team? Known(int id) => thenClub.TryGetValue(id, out var t) ? t : inProgress ? ClubOf(id) : null;

        try
        {
            if (Season == null)
            {
                Headline = "No awards yet"; Summary = "Awards are voted after the regular season; All-Stars are named in February."; AllLeague = ""; AllStars = "";
                EmptyNote = RealLastSeason();
                return;
            }
            SeasonAwards? a; SeasonSummary? summary = null;
            if (Season.EndsWith("(in progress)")) a = s.Midseason;
            else
            {
                int year = int.Parse(Season[..4]);
                summary = s.League.History.First(h => h.Season == year);
                a = summary.Awards;
            }
            if (a == null) { Headline = Season; Summary = "No award data for this season."; AllLeague = ""; AllStars = ""; return; }

            Headline = summary != null ? $"{Season} awards" : $"{Season}: All-Star weekend";
            Summary = summary != null
                ? $"Champions: {s.FindTeam(summary.ChampionTeamId)!.FullName} over {s.FindTeam(summary.RunnerUpTeamId)!.FullName} · Finals MVP {Name(summary.FinalsMvpPlayerId)} · Coach of the Year: {(a.CoachOfYearName.Length > 0 ? a.CoachOfYearName + ", " : "")}{(a.CoachOfYearTeamId is { } c ? s.FindTeam(c)!.FullName : "-")} ({a.CoachOfYearNote})"
                : "Season in progress. Full awards arrive after the Finals.";
            AllLeague = a.AllLeague.Count == 0 ? "" : string.Join("\n", a.AllLeague.Select((t, i) => $"{(i == 0 ? "First" : i == 1 ? "Second" : "Third")} team: {string.Join(", ", t.Select(id => $"{Name(id)} ({TeamOf(id)})"))}"));
            AllStars = a.AllStarsEast.Count == 0 ? "No All-Star Game this season." :
                $"All-Star Game: {a.AllStarGame}. MVP {Name(a.AllStarMvpId)}.\nEast: {string.Join(", ", a.AllStarsEast.Select(id => Name(id)))}\nWest: {string.Join(", ", a.AllStarsWest.Select(id => Name(id)))}";

            void Fill(ObservableCollection<VoteRow> list, List<VoteLine> votes)
            {
                int rank = 1;
                foreach (var v in votes)
                    list.Add(new VoteRow { Rank = rank++, Name = v.Name, Team = v.Team, Share = $"{v.Share * 100:F0}%", ShareValue = v.Share, Points = v.Points.ToString("0"), StatLine = v.StatLine,
                                           Player = P(v.PlayerId), Club = ClubAt(v.PlayerId, v.Team) });
            }
            Fill(Mvp, a.MvpVoting); Fill(Roy, a.RoyVoting); Fill(Dpoy, a.DpoyVoting); Fill(SixthMan, a.SixthManVoting);

            // The honour lines: the three All-League teams and the two All-Star rosters (the game's MVP starred).
            string[] ordinal = { "First team", "Second team", "Third team" };
            for (int i = 0; i < a.AllLeague.Count; i++)
                AllLeagueTeams.Add(new HonourLine { Title = i < 3 ? ordinal[i] : $"Team {i + 1}", Players = a.AllLeague[i].Select(id => Chip(id, false)).ToList() });
            if (a.AllStarsEast.Count > 0)
            {
                AllStarLine = $"{a.AllStarGame}{(a.AllStarGame.Length > 0 ? " · " : "")}MVP {Name(a.AllStarMvpId)}";
                AllStarTeams.Add(new HonourLine { Title = "East", Players = a.AllStarsEast.Select(id => Chip(id, id == a.AllStarMvpId)).ToList() });
                AllStarTeams.Add(new HonourLine { Title = "West", Players = a.AllStarsWest.Select(id => Chip(id, id == a.AllStarMvpId)).ToList() });
            }
            else AllStarLine = "No All-Star Game this season.";

            // Finals MVP and Coach of the Year join the four voted awards as cards.
            if (summary != null)
            {
                var champ = s.FindTeam(summary.ChampionTeamId);
                if (summary.FinalsMvpPlayerId is { } f)
                    ExtraCards.Add(Card("Finals MVP", "Trophy", P(f), champ, Name(f), champ != null ? $"Champions: {champ.FullName}" : "", ""));
                if (a.CoachOfYearTeamId is { } coy && s.FindTeam(coy) is { } ct)
                    {
                    // H58 (run 88): the coach, not the club, under the award; older saves didn't keep his name.
                    ExtraCards.Add(a.CoachOfYearName.Length > 0
                        ? Card("Coach of the Year", "ClipboardList", null, ct, a.CoachOfYearName, $"Head coach, {ct.FullName}", a.CoachOfYearNote, coach: true)
                        : Card("Coach of the Year", "ClipboardList", null, ct, ct.FullName, "Head coach", a.CoachOfYearNote, coach: true));
                }
            }
        }
        finally
        {
            BuildCards();
        }

        HonourChip Chip(int id, bool star) => new()
        {
            Player = P(id), Club = Known(id) ?? ClubOf(id), Name = Name(id), Team = Known(id)?.Abbreviation ?? TeamOf(id), Star = star,
            Mine = Known(id) is { } k && k == s.HumanTeam && s.Employed,
        };
    }

    /// <summary>Cards that don't come from a vote (Finals MVP, Coach of the Year), kept for <see cref="BuildCards"/>.</summary>
    private readonly List<WinnerCard> ExtraCards = new();

    private WinnerCard Card(string award, string icon, Player? p, Team? club, string name, string teamLine, string stat, string vote = "", bool coach = false) => new()
    {
        Award = award, Icon = icon, Player = p, Club = club, IsCoach = coach, Name = name, TeamLine = teamLine, StatLine = stat, Vote = vote,
        Mine = club != null && club == Session.HumanTeam && Session.Employed,
        Stripe = new SolidColorBrush(club != null ? ThemeService.AccentFor(club) : ThemeService.DefaultAccent),
    };

    /// <summary>Builds the winner cards and the vote ladders from the four vote lists (public so the layout check can
    /// rebuild them after seeding the lists with the longest real names).</summary>
    public void BuildCards()
    {
        Winners.Clear(); Ladders.Clear();
        var s = Session;
        foreach (var (award, icon, list) in new[] { (MvpTitle, "Award", Mvp), ("Defensive Player of the Year", "Shield", Dpoy),
                                                     (RoyTitle, "Star", Roy), ("Sixth Man of the Year", "ArrowLeftRight", SixthMan) })
        {
            if (list.Count == 0) continue;
            var w = list[0];
            string teamLine = w.Club?.FullName ?? w.Team;
            Winners.Add(Card(award, icon, w.Player, w.Club, w.Name, teamLine, w.StatLine, $"{w.Share} of the vote"));
            var rows = list.Take(5).Select(v => new LadderRow
            {
                Rank = v.Rank, Name = v.Name, Team = v.Team, Share = v.Share, Winner = v.Rank == 1,
                BarWidth = Math.Max(2, Math.Round(Math.Clamp(v.ShareValue, 0, 1) * LadderBarMax)),
                Mine = v.Club != null && v.Club == s.HumanTeam && s.Employed,
                Tip = $"{v.Rank}. {v.Name} ({v.Team}): {v.Share} of the vote. {v.StatLine}",
            }).ToList();
            Ladders.Add(new LadderCard { Award = award, Rows = rows, More = list.Count > 5 ? $"{list.Count - 5} more got votes (Full table)" : "" });
        }
        foreach (var c in ExtraCards) Winners.Add(c);
        HasWinners = Winners.Count > 0;
        HasLadders = Ladders.Count > 0;
        HasAllLeague = AllLeagueTeams.Count > 0;
        HasAllStars = AllStarTeams.Count > 0;
        HasEmptyNote = EmptyNote.Length > 0;
        HasAllStarPanel = AllStarLine.Length > 0 || HasAllStars;
        HasSeason = Season != null;
    }

    /// <summary>CS5 (run 135): one college season's honours, from <see cref="Hardwood.Core.College.CollegeWorld.Awards"/>.</summary>
    private void ShowCollege()
    {
        var s = Session; var cw = s.World.College!;
        AllLeague = ""; AllStars = "";
        if (Season == null)
        {
            Headline = "No college awards yet";
            Summary = "The national awards are voted the day the champion is crowned in April.";
            EmptyNote = "Each season's honours land here when March Madness ends: National Player of the Year, the Final Four's Most Outstanding Player, "
                      + "Defensive and Freshman of the Year, Coach of the Year, the three All-America teams and every conference's first team.";
            return;
        }
        int year = int.Parse(Season[..4]);
        var a = cw.Awards.FirstOrDefault(x => x.Year == year);
        if (a == null) { Headline = Season; Summary = "No award data for this season."; return; }
        var all = cw.Players.Concat(s.League.AllActivePlayers).Concat(s.League.FreeAgents).Concat(s.League.Retired)
            .Concat(s.World?.Players ?? Enumerable.Empty<Player>()).GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First());
        Player? P(int id) => all.TryGetValue(id, out var p) ? p : null;
        // The program he played for that season (kept with the award), never where he is today.
        Team? ClubOf(int id) => a.ProgramOf.TryGetValue(id, out var t) ? cw.Program(t) : null;
        bool Mine(Team? t) => t != null && t == s.HumanTeam && s.Employed;

        var champ = cw.History.FirstOrDefault(h => h.Year == year);
        Headline = $"{Season} college awards";
        string coach = a.CoachProgramId is { } cp && cw.Program(cp) is { } ct ? $"{(a.CoachName.Length > 0 ? a.CoachName + ", " : "")}{ct.FullName}" : "-";
        Summary = (champ != null ? $"National champions: {champ.Champion} over {champ.RunnerUp}" : "National champions: -")
                + (a.OutstandingId is { } mo ? $" · Most Outstanding Player {a.NameOf(mo)}" : "")
                + $" · Coach of the Year: {coach}";

        void Fill(ObservableCollection<VoteRow> list, List<VoteLine> votes)
        {
            int rank = 1;
            foreach (var v in votes)
                list.Add(new VoteRow { Rank = rank++, Name = v.Name, Team = v.Team, Share = $"{v.Share * 100:F0}%", ShareValue = v.Share, Points = v.Points.ToString("0"),
                                       StatLine = v.StatLine, Player = P(v.PlayerId), Club = ClubOf(v.PlayerId) });
        }
        Fill(Mvp, a.PlayerOfYear); Fill(Dpoy, a.Defensive); Fill(Roy, a.Freshman);

        HonourChip Chip(int id, bool star, string tip = "") => new()
        {
            Player = P(id), Club = ClubOf(id), Name = a.NameOf(id), Team = ClubOf(id)?.Abbreviation ?? "", Star = star, StarTip = tip, Mine = Mine(ClubOf(id)),
        };
        for (int i = 0; i < a.AllAmerica.Count; i++)
            AllLeagueTeams.Add(new HonourLine { Title = i switch { 0 => "First team", 1 => "Second team", 2 => "Third team", _ => $"Team {i + 1}" },
                                                Players = a.AllAmerica[i].Select(id => Chip(id, false)).ToList() });
        AllLeague = string.Join("\n", a.AllAmerica.Select((t, i) => $"{Hardwood.Core.College.CollegeAwardsText.TeamName(i)}: {string.Join(", ", t.Select(id => $"{a.NameOf(id)} ({ClubOf(id)?.Abbreviation ?? "-"})"))}"));

        // All-conference: your conference first, then the power conferences; the full table lists every conference.
        string? mine = s.MyConference?.Key;
        var shown = cw.Active.Where(c => a.AllConference.ContainsKey(c.Key) && (c.Key == mine || c.Power))
            .OrderByDescending(c => c.Key == mine).ThenBy(c => c.Short).ToList();
        foreach (var c in shown)
        {
            int poy = a.ConferencePlayer.GetValueOrDefault(c.Key);
            AllStarTeams.Add(new HonourLine { Title = c.Short, Players = a.AllConference[c.Key].Select(id => Chip(id, id == poy, $"{c.Short} Player of the Year")).ToList() });
        }
        int rest = a.AllConference.Count - shown.Count;
        AllStarLine = "The star marks each conference's player of the year." + (rest > 0 ? $" {rest} more conferences are on the Full table view." : "");
        AllStars = string.Join("\n", cw.Active.Where(c => a.AllConference.ContainsKey(c.Key)).OrderBy(c => c.Short)
            .Select(c => $"{c.Short}: {string.Join(", ", a.AllConference[c.Key].Select(id => a.NameOf(id) + (a.ConferencePlayer.GetValueOrDefault(c.Key) == id ? " (POY)" : "")))}"));

        if (a.OutstandingId is { } mop)
        {
            var club = ClubOf(mop) ?? (champ != null ? cw.Program(champ.ChampionId) : null);
            ExtraCards.Add(Card("Most Outstanding Player", "Trophy", P(mop), club, a.NameOf(mop), champ != null ? $"Champions: {champ.Champion}" : "", a.OutstandingLine));
        }
        if (a.CoachProgramId is { } cid && cw.Program(cid) is { } cprog)
            ExtraCards.Add(Card("Coach of the Year", "ClipboardList", null, cprog, a.CoachName.Length > 0 ? a.CoachName : cprog.FullName, $"Head coach, {cprog.FullName}", a.CoachNote, coach: true));
    }

    /// <summary>In a real career before the first awards: who won them last season, for real.</summary>
    private string RealLastSeason()
    {
        if (!Hardwood.Core.RealWorld.RealWorldData.IsReal(Session.League)) return "";
        var last = Hardwood.Core.RealWorld.RealWorldData.Seasons.Where(x => x.Season < Session.League.Season).OrderByDescending(x => x.Season).FirstOrDefault();
        if (last == null) return "";
        return $"Last season, for real ({last.Label}): MVP {last.Mvp ?? "-"} · {last.Champion} beat {last.RunnerUp} in the Finals · Finals MVP {last.FinalsMvp ?? "-"}. "
             + "This career's first All-Stars are named in February, and its awards are voted after the regular season.";
    }

    private void FillHall()
    {
        Hall.Clear();
        var s = Session;
        var all = s.League.AllActivePlayers.Concat(s.League.FreeAgents).Concat(s.League.Retired).ToDictionary(p => p.Id);
        foreach (var h in s.League.HallOfFame.OrderByDescending(h => h.Year).ThenByDescending(h => h.Score))
        {
            if (!all.TryGetValue(h.PlayerId, out var p)) continue;
            s.League.Careers.TryGetValue(h.PlayerId, out var c);
            Hall.Add(new HallRow
            {
                Player = p, Year = h.Year, Score = h.Score,
                Club = c?.GamesByTeam.Count > 0 ? s.FindTeam(c.GamesByTeam.MaxBy(x => x.Value).Key) : null,
                Career = c == null ? "" : $"{c.Seasons} seasons · {c.Points:N0} pts · {c.Ppg:F1}/{c.Rpg:F1}/{c.Apg:F1}",
                Honours = c == null ? "" : string.Join(" · ", new[] { (c.Mvps, "MVP"), (c.Titles, "title"), (c.FinalsMvps, "Finals MVP"), (c.AllLeague, "All-League"), (c.AllStars, "All-Star"), (c.Dpoys, "DPOY") }.Where(x => x.Item1 > 0).Select(x => $"{x.Item1}× {x.Item2}")),
            });
        }
        HallNote = Hall.Count == 0 ? "No inductees yet. Players are considered when they retire." : $"{Hall.Count} inductee{(Hall.Count == 1 ? "" : "s")}.";
    }
}
