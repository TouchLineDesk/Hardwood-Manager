using Hardwood.Core.Engine;
using Hardwood.Core.Model;

namespace Hardwood.Core.Season;

/// <summary>Run 223: the in-season cup in the career (<see cref="NbaCup"/>).</summary>
public sealed partial class GameSession
{
    private CupTie? _liveCup;
    /// <summary>The cup knockout game being played live, or null (the floor's dressing reads it).</summary>
    public CupTie? LiveCup => _liveCup;

    /// <summary>This season's cup, or null (an older save's season in progress, or a league without the cup's shape).</summary>
    public CupSeason? Cup => League.Cup.Current is { } c && c.Season == League.Season ? c : null;

    /// <summary>Your club's knockout game tonight, if any.</summary>
    public CupTie? MyCupTieToday => Cup?.Ties.FirstOrDefault(t => t.Date == Today && !t.Played && (t.HomeId == HumanTeam.Id || t.AwayId == HumanTeam.Id));

    /// <summary>The group your club is in, or null.</summary>
    public CupGroup? MyCupGroup => Cup?.Groups.FirstOrDefault(g => g.TeamIds.Contains(HumanTeam.Id));

    /// <summary>True for a schedule game that is also a cup group game.</summary>
    public bool IsCupGame(ScheduledGame g) => Cup is { } c && c.GroupGames.Contains(g.Index);

    private static string CupClubs(CupSeason cup) => cup.Groups.Sum(g => g.TeamIds.Count) switch { 30 => "Thirty", 32 => "Thirty-two", var n => n.ToString() };

    /// <summary>The draw's order: last season's win share when every club has a record, else strength (a new world).</summary>
    private Func<Team, double> CupRank()
    {
        var st = Standings;
        if (st != null && League.Teams.All(st.Has) && League.Teams.All(t => st[t].Games > 0)) return t => st[t].WinPct + st[t].PointDiff / 1000.0;
        return t => Hardwood.Core.World.QuickSim.Strength(t);
    }

    /// <summary>Tonight's knockout games: everyone else's played now, yours held back as the live game when it is live.</summary>
    private GameSimulator.LiveGame? PlayCupTies(bool clubLive, List<GameResult> played)
    {
        if (Cup is not { } cup) return null;
        GameSimulator.LiveGame? live = null;
        foreach (var tie in cup.Ties.Where(t => t.Date == Today && !t.Played).ToList())
        {
            var home = League.TeamById(tie.HomeId); var away = League.TeamById(tie.AwayId);
            var rules = tie.Neutral ? League.Rules with { NeutralSite = true } : League.Rules;
            bool human = home == HumanTeam || away == HumanTeam;
            if (human && clubLive)
            {
                _liveCup = tie;
                live = _sim.StartLive(home, away, rules, _rng.NextULong(), Today);
                continue;
            }
            var r = _sim.Simulate(home, away, rules, _rng.NextULong(), keepLog: human, date: Today);
            RecordCupTie(tie, r);
            played.Add(r);
        }
        return live;
    }

    /// <summary>A knockout game's score (no standings, no season statistics: an extra game, like the real cup final).</summary>
    private void RecordCupTie(CupTie tie, GameResult r)
    {
        tie.Result = r; tie.HomePoints = r.Home.Points; tie.AwayPoints = r.Away.Points; tie.Overtimes = r.Overtimes;
        var winBox = r.Home.Points > r.Away.Points ? r.Home : r.Away;
        if (winBox.Lines.Count > 0)
        {
            var top = winBox.Lines.OrderByDescending(l => l.Points).ThenBy(l => l.Player.Id).First();
            tie.TopScorer = $"{top.Player.Name} ({top.Points} points)";
        }
        if (!Employed || (r.Home.Team != HumanTeam && r.Away.Team != HumanTeam)) return;
        var us = r.Home.Team == HumanTeam ? r.Home : r.Away;
        var them = us == r.Home ? r.Away : r.Home;
        bool won = us.Points > them.Points;
        string where = tie.Neutral ? $"in {NbaCup.Venue}" : us == r.Home ? "at home" : $"in {them.Team.City}";
        string next = !won ? (tie.Round == NbaCup.Final ? " Runners-up: the cup goes to them." : " Out of the cup; the league season goes on.")
            : tie.Round == NbaCup.Final ? " The cup is yours."
            : tie.Round == NbaCup.SemiFinal ? $" On to the final in {NbaCup.Venue}." : $" On to the semi-finals in {NbaCup.Venue}.";
        Post($"{NbaCup.Name} {tie.Round.ToLowerInvariant()}: {(won ? "W" : "L")} {us.Points}-{them.Points} v {them.Team.Abbreviation}",
            $"{(won ? "You beat" : "You lost to")} {them.Team.TheFullName} {Math.Max(us.Points, them.Points)}-{Math.Min(us.Points, them.Points)} {where}" +
            $"{(r.Overtimes > 0 ? " after overtime" : "")}.{next}" + (tie.TopScorer != null ? $" Top scorer for the winners: {tie.TopScorer}." : "") +
            "\n\nCup knockout games don't count in the standings or the season's statistics.", "Game");
        // Injuries from the game still reach you.
        foreach (var (p, inj) in r.Injuries.Where(x => x.player.TeamId == HumanTeam.Id))
            Post($"{p.Name} injured: {inj.Name}", $"Out approximately {inj.DaysOut(Today)} days (expected return {inj.ReturnDate:MMM d}).", "Medical");
    }

    /// <summary>The cup's calendar, at the end of each day: the groups on opening night, the quarter-finals once the group
    /// games are in, each next round, and the winner.</summary>
    private void NbaCupDay()
    {
        if (Phase != SessionPhase.RegularSeason || Cup is not { } cup) return;
        var groupNights = NbaCup.GroupNights(cup.Season);
        if (!cup.GroupsPosted)
        {
            cup.GroupsPosted = true;
            var mine = MyCupGroup;
            int size = cup.Groups[0].TeamIds.Count;
            var lastNight = groupNights[size == 4 ? 2 : 4];
            var ko = NbaCup.KnockoutNights(cup.Season);
            static string Word(int n) => n switch { 3 => "three", 4 => "four", 5 => "five", 6 => "six", 8 => "eight", _ => n.ToString() };
            string Group(CupGroup g) => $"{g.Name}: {string.Join(", ", g.TeamIds.Select(id => League.TeamById(id).FullName))}";
            PostPro($"The {NbaCup.Name}: the groups",
                (mine != null && Employed ? $"You're in {mine.Name}. " : "") +
                $"{CupClubs(cup)} clubs in {Word(cup.Groups.Count)} groups of {Word(size)} play {Word(NbaCup.GroupGamesEach(size))} group games each on the cup nights, {groupNights[0]:MMM d} to {lastNight:MMM d}; they count in the regular season too. " +
                (cup.Groups.Count == 6 ? "The three group winners and the best runner-up in each conference" : "The group winners") +
                $" reach the quarter-finals ({ko.qf1:MMM d} and {ko.qf2:MMM d}, at the better seed's arena); " +
                $"the semi-finals ({ko.sf:MMM d}) and the final ({ko.final:MMM d}) are in {NbaCup.Venue}. Knockout games don't count in the standings.\n\n" +
                string.Join("\n", cup.Groups.Select(Group)));
        }
        if (cup.Ties.Count == 0)
        {
            if (cup.GroupGames.Count == 0 || !cup.GroupGames.All(Results.ContainsKey)) return;
            cup.Ties.AddRange(NbaCup.QuarterFinals(League, cup, Schedule, Results));
            string Table(CupGroup g) => g.Name + ": " + string.Join(", ", NbaCup.Table(League, cup, g, Schedule, Results).Select(r => $"{r.Team.Abbreviation} {r.Wins}-{r.Losses}"));
            var mineTie = cup.Ties.FirstOrDefault(t => t.HomeId == HumanTeam.Id || t.AwayId == HumanTeam.Id);
            string you = !Employed || Abroad ? "" : mineTie != null
                ? $"You're through: {NbaCup.Line(League, mineTie)}.\n\n"
                : MyCupGroup != null ? "You're out of the cup.\n\n" : "";
            PostPro($"The {NbaCup.Name}: the quarter-finals", you + string.Join("\n", cup.Ties.Select(t => NbaCup.Line(League, t))) +
                "\n\nThe groups:\n" + string.Join("\n", cup.Groups.Select(Table)));
            return;
        }
        if (cup.WinnerId != null || cup.Ties.Any(t => !t.Played)) return;
        var next = NbaCup.NextRound(cup);
        if (next.Count > 0)
        {
            var done = cup.Ties.Where(t => t.Round == (next[0].Round == NbaCup.Final ? NbaCup.SemiFinal : NbaCup.QuarterFinal)).ToList();
            cup.Ties.AddRange(next);
            PostPro($"The {NbaCup.Name}: the {(next[0].Round == NbaCup.Final ? "final" : "semi-finals")}",
                string.Join("\n", done.Select(t => NbaCup.Line(League, t))) + "\n\nNext, in " + NbaCup.Venue + ":\n" + string.Join("\n", next.Select(t => NbaCup.Line(League, t))));
            return;
        }
        if (cup.Ties.FirstOrDefault(t => t.Round == NbaCup.Final) is { Played: true } final)
        {
            cup.WinnerId = final.WinnerId;
            var w = League.TeamById(final.WinnerId!.Value); var ru = League.TeamById(final.LoserId!.Value);
            string score = $"{Math.Max(final.HomePoints!.Value, final.AwayPoints!.Value)}-{Math.Min(final.HomePoints!.Value, final.AwayPoints!.Value)}";
            League.Cup.Winners.Add(new CupWinner(cup.Season, w.Id, ru.Id, score, final.TopScorer));
            int titles = League.Cup.Winners.Count(x => x.WinnerId == w.Id);
            PostPro($"{w.FullName} win the {NbaCup.Name}",
                $"{w.FullName} beat {ru.FullName} {score} in the final in {NbaCup.Venue}" + (final.TopScorer != null ? $"; {final.TopScorer} led the winners" : "") + "." +
                (titles > 1 ? $" Their {Presentation.GameCalls.Ordinal(titles)} cup." : "") +
                (Employed && w == HumanTeam ? " Silverware in December." : ""));
        }
    }
}
