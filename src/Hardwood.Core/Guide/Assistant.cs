using Hardwood.Core.Jobs;
using Hardwood.Core.Model;
using Hardwood.Core.Season;

namespace Hardwood.Core.Guide;

/// <summary>How much the assistant explains (decision #118). Auto grows with you.</summary>
public enum GuideLevel { Auto, Beginner, Experienced, StrategyOnly }

/// <summary>Where the assistant thinks you are: learning the screens, playing seasons, or past that.</summary>
public enum GuideStage { Beginner, Experienced, Strategy }

/// <summary>Something to do now, most urgent first. <see cref="Page"/> is the page that does it (null: just press Continue).</summary>
public sealed record GuideTask(string Key, int Priority, string Title, string Detail, string? Page);

public sealed record GuideAdvice(GuideStage Stage, string Headline, IReadOnlyList<GuideTask> Tasks, string PageTitle, string PageTip, IReadOnlyList<string> Strategy);

/// <summary>What the player has seen and done, kept with the app settings (it's the person who learns, not the save).</summary>
public sealed class GuideProgress
{
    public HashSet<string> PagesSeen { get; set; } = new();
    public HashSet<string> Dismissed { get; set; } = new();
    public int DaysPlayed { get; set; }
    public int SeasonsPlayed { get; set; }
}

/// <summary>
/// The in-game assistant (decisions #115-118): reads your situation and says what to do next, explains the page you're
/// on, and offers strategy, in the voice of your assistant coach. Everything comes from the save; nothing is hidden
/// information (players' potential is what your scouts believe).
/// </summary>
public static class Assistant
{
    /// <summary>Days of play before Auto moves from beginner help to the experienced level.</summary>
    public const int BeginnerDays = 45;

    public static GuideStage StageFor(GuideLevel level, GuideProgress progress) => level switch
    {
        GuideLevel.Beginner => GuideStage.Beginner,
        GuideLevel.Experienced => GuideStage.Experienced,
        GuideLevel.StrategyOnly => GuideStage.Strategy,
        _ => progress.SeasonsPlayed >= 1 ? GuideStage.Strategy : progress.DaysPlayed < BeginnerDays ? GuideStage.Beginner : GuideStage.Experienced,
    };

    public static GuideAdvice Advise(GameSession s, string page, GuideLevel level, GuideProgress progress)
    {
        var stage = StageFor(level, progress);
        var tasks = Tasks(s, stage, progress).Where(t => !progress.Dismissed.Contains(t.Key)).OrderBy(t => t.Priority).ToList();
        var (title, tip) = PageTip(page, stage);
        return new GuideAdvice(stage, Headline(s, tasks), tasks, title, tip, Strategy(s, stage));
    }

    // ------------------------------------------------------------------------------------------
    // What to do next

    private static string Headline(GameSession s, List<GuideTask> tasks)
    {
        if (!s.Employed) return "We need a job, coach.";
        int urgent = tasks.Count(t => t.Priority < 30);
        return urgent switch
        {
            0 => "Nothing urgent. We're good to play on.",
            1 => "One thing needs you before we play on.",
            _ => $"{urgent} things need you before we play on.",
        };
    }

    public static IEnumerable<GuideTask> Tasks(GameSession s, GuideStage stage, GuideProgress progress)
    {
        var team = s.HumanTeam;
        var today = s.Today;
        if (!s.Employed)
        {
            yield return new("job", 1, "Apply for a job", "The board let us go. Open jobs are on the Board page; clubs choose by your reputation against their prestige, so smaller clubs answer first.", "Board");
            yield break;
        }

        // ---- the offseason has its own steps
        if (s.Phase == SessionPhase.Offseason && s.Offseason is { } off)
        {
            if (off.Phase == OffseasonPhase.ReSigning && s.MyExpiring.Count > 0)
                yield return new($"resign-{today.Year}", 5, $"Decide on {s.MyExpiring.Count} expiring contract{(s.MyExpiring.Count == 1 ? "" : "s")}",
                    $"{Names(s.MyExpiring.OrderByDescending(p => p.CurrentAbility).Take(3))} {(s.MyExpiring.Count > 3 ? "and others are" : s.MyExpiring.Count == 1 ? "is" : "are")} out of contract. Offer what they ask to keep them, counter if it's too much, or let them go. Anyone you don't decide on leaves.", "Offseason");
            int min = RosterMin(s);
            if (off.Phase == OffseasonPhase.FreeAgency && team.Roster.Count < min)
                yield return new($"fa-{today.Year}", 6, $"Sign {min - team.Roster.Count} more player{(min - team.Roster.Count == 1 ? "" : "s")}",
                    $"We have {team.Roster.Count} and need at least {min}. Free agents and college graduates are on the Transactions page; check your wage room on the Board page first.", "Transactions");
            if (off.Phase == OffseasonPhase.FreeAgency && team.Roster.Count >= min)
                yield return new($"summer-done-{today.Year}", 40, "Finish the summer when you're happy",
                    "The roster is legal. Keep shopping if you have money left, or press Continue to start the new season.", null);
        }

        // ---- money and the market
        if (s.IncomingBids.Count > 0)
            yield return new($"bids-{s.IncomingBids.Count}-{today}", 8, $"Answer {s.IncomingBids.Count} transfer bid{(s.IncomingBids.Count == 1 ? "" : "s")}",
                $"Another club wants {Names(s.IncomingBids.Select(b => s.League.AllActivePlayers.Concat(team.Roster).FirstOrDefault(p => p.Id == b.PlayerId)).OfType<Player>().Distinct().Take(2))}. Accept, reject, or let it expire; a fee can fund a replacement.", "Transactions");
        if (team.Money.Ready && s.MyCommittedWages > team.Money.WageBudget)
            yield return new($"budget-{today.Year}", 20, "We're over the wage budget",
                $"Wages of ${s.MyCommittedWages / 1e6:F1}M against a budget of ${team.Money.WageBudget / 1e6:F1}M. The owner notices; trim the bench or sell before signing anyone.", "Board");

        // ---- the roster
        if (s.Phase != SessionPhase.Offseason)
        {
            int min = RosterMin(s);
            if (team.Roster.Count < min)
                yield return new($"short-{today}", 4, $"We're short: {team.Roster.Count} players",
                    $"A club needs at least {min}. Sign a free agent on the Transactions page.", "Transactions");
            var chart = team.DepthChart;
            if (chart != null)
            {
                var hurt = chart.Starters.Select(id => team.Roster.FirstOrDefault(p => p.Id == id)).OfType<Player>().Where(p => !p.IsAvailable(today)).ToList();
                if (hurt.Count > 0)
                    yield return new($"hurt-{string.Join('.', hurt.Select(p => p.Id))}", 10, $"{Names(hurt)} {(hurt.Count == 1 ? "is" : "are")} injured but still starting",
                        "Pick a healthy starter and move the minutes on the Rotation page, or reset to the automatic rotation.", "Rotation");
            }
            var unhappy = team.Roster.Where(p => p.Morale < 35).OrderBy(p => p.Morale).ToList();
            if (unhappy.Count > 0)
                yield return new($"morale-{string.Join('.', unhappy.Select(p => p.Id))}", 15, $"{Names(unhappy.Take(2))} {(unhappy.Count == 1 ? "is" : "are")} unhappy",
                    "Unhappy players play worse and want out. A talk on the Squad page shows why; a promise (minutes, a role) helps if you can keep it.", "Squad");
        }

        // ---- the board
        if (s.Job.Confidence < 40)
            yield return new($"board-{(int)s.Job.Confidence / 5}", 12, "The board is losing patience",
                $"Confidence is {s.Job.Confidence:F0}/100. {Goal(s)} Check the Board page for what else they want.", "Board");

        // ---- staff
        var missing = Enum.GetValues<StaffRole>().Where(r => team.Staff.All(st => st.Role != r)).ToList();
        if (missing.Count > 0)
            yield return new($"staff-{string.Join('.', missing)}", 25, $"Hire a {Staff.RoleName(missing[0]).ToLowerInvariant()}",
                $"{string.Join(", ", missing.Select(Staff.RoleName))} {(missing.Count == 1 ? "is" : "are")} missing. Staff improve training, development, fitness and scouting; hire on the Staff & Training page.", "Staff & Training");

        // ---- inbox
        int unread = s.Inbox.Count(m => !m.Read);
        if (unread >= 5)
            yield return new($"inbox-{unread / 5}", 35, $"{unread} unread messages", "Most are news; the important ones (board, injuries, offers) are worth a look.", "Inbox");

        // ---- game day
        if (s.Phase is SessionPhase.RegularSeason or SessionPhase.Postseason && s.NextMyGame is { } next && next.Date <= today.AddDays(1) && stage != GuideStage.Strategy)
            yield return new($"scout-{next.Date}", 30, $"Next up: {(next.Home ? "vs" : "at")} {next.Opponent.FullName}",
                "The Tactics page has the scouting report and your assistant's plan for this opponent. Then press Continue to play.", "Tactics");
        if (s.Phase == SessionPhase.Preseason)
            yield return new($"preseason-{today.Year}", 20, "Get ready for opening night",
                "Set the rotation (who starts, who plays how long) and the tactics. The automatic choices are sensible if you'd rather play first.", "Rotation");

        // ---- first steps: every page, once
        if (stage == GuideStage.Beginner)
        {
            int order = 50;
            foreach (var (pg, why) in FirstSteps)
                if (!progress.PagesSeen.Contains(pg))
                    yield return new($"visit-{pg}", order++, $"Look at {pg}", why, pg);
        }
    }

    private static readonly (string Page, string Why)[] FirstSteps =
    {
        ("Roster", "Who we have: ability, potential (as your scouts see it), contracts and health."),
        ("Rotation", "Who starts and how many minutes each player gets."),
        ("Tactics", "How we play: the offensive system, the defence, pace, and each player's role."),
        ("Board", "What the owner expects this season, how confident they are in you, and the club's money."),
        ("Squad", "Morale and chemistry: who's happy, who isn't, and why."),
        ("Staff & Training", "Your coaches, physio and scouts, and the training plan."),
        ("Transactions", "Signing free agents, transfer bids and trades."),
        ("Schedule & Results", "The fixture list and every result, with box scores."),
    };

    private static int RosterMin(GameSession s) => s.InNabl ? s.League.Finance.RosterMin : World.GameWorld.ManagedMinimum;

    private static string Names(IEnumerable<Player> ps)
    {
        var l = ps.Select(p => p.Name).ToList();
        return l.Count switch { 0 => "", 1 => l[0], 2 => $"{l[0]} and {l[1]}", _ => string.Join(", ", l.Take(l.Count - 1)) + " and " + l[^1] };
    }

    private static string Goal(GameSession s)
    {
        if (s.Job.Current is not { } e) return "";
        var ctx = s.ContextOf(s.HumanTeam);
        return $"The goal is '{Board.Describe(e.Goal, ctx)}' ({e.TargetWins} wins).";
    }

    // ------------------------------------------------------------------------------------------
    // Strategy: what an assistant would point out about this team right now

    public static IReadOnlyList<string> Strategy(GameSession s, GuideStage stage)
    {
        var tips = new List<string>();
        if (!s.Employed) return tips;
        var team = s.HumanTeam;
        var today = s.Today;
        var rec = s.MyRecord;
        int games = rec.Wins + rec.Losses;
        var ctx = s.ContextOf(team);

        // Pace against the board's target.
        if (s.Job.Current is { } e && games >= 10 && s.Phase == SessionPhase.RegularSeason)
        {
            double pace = rec.Wins / (double)games * ctx.SeasonGames;
            tips.Add(pace >= e.TargetWins
                ? $"We're on pace for {pace:F0} wins; the board wants {e.TargetWins}. Keep the rotation steady."
                : $"We're on pace for {pace:F0} wins and the board wants {e.TargetWins}. Look at the weakest starter, or a tactic that suits our best players.");
        }

        // Young players who need minutes.
        var stats = s.MyStats;
        var young = team.Roster.Where(p => p.Age(today) <= 22)
            .Select(p => (p, pot: Scouting.Potential(p, Scouting.Knowledge(team, p)).Mid, mpg: stats.TryGetValue(p.Id, out var st) ? st.Mpg : 0))
            .Where(x => x.pot >= x.p.CurrentAbility + 15 && x.mpg < 15 && x.p.IsAvailable(today))
            .OrderByDescending(x => x.pot).Take(2).ToList();
        foreach (var (p, pot, mpg) in young)
            tips.Add(s.Phase == SessionPhase.Offseason
                ? $"{p.Name} ({p.Age(today)}) could become a {Level((int)pot)} but played {mpg:F0} minutes a game last season. Plan real minutes for him next year."
                : $"{p.Name} ({p.Age(today)}) could become a {Level((int)pot)} but plays {mpg:F0} minutes. Young players develop faster with real minutes.");

        // Heavy minutes.
        var tired = s.Phase == SessionPhase.Offseason ? new List<Player>()
            : team.Roster.Where(p => stats.TryGetValue(p.Id, out var st) && st.Games >= 5 && st.Mpg > 37).ToList();
        if (tired.Count > 0)
            tips.Add($"{Names(tired.Take(2))} average{(tired.Count == 1 ? "s" : "")} over 37 minutes. Tired legs mean worse shooting late and more injuries.");

        // The weakest position.
        var byPos = team.Roster.GroupBy(p => p.PrimaryPosition).ToDictionary(g => g.Key, g => g.Max(p => p.CurrentAbility));
        var gaps = Enum.GetValues<Position>().Where(pos => !byPos.ContainsKey(pos)).ToList();
        if (gaps.Count > 0) tips.Add($"No natural {gaps[0]} on the roster. Players out of position lose effectiveness.");
        else if (byPos.Count >= 5)
        {
            var weakest = byPos.OrderBy(kv => kv.Value).First();
            var best = byPos.Values.Max();
            if (best - weakest.Value >= 15)
                tips.Add($"Our best {weakest.Key} is well below the rest of the starters. That's the position to upgrade when money allows.");
        }

        // Money.
        if (team.Money.Ready)
        {
            long room = team.Money.WageBudget - s.MyCommittedWages;
            if (room > team.Money.WageBudget * 0.08)
                tips.Add($"There's ${room / 1e6:F1}M of wage room. A free agent or a transfer could use it{(s.WindowOpen ? " while the window is open" : "")}.");
        }

        // Ageing stars on long deals.
        var old = team.Roster.Where(p => p.Age(today) >= 31 && (p.Contract?.Years ?? 0) >= 3).OrderByDescending(p => p.Contract!.Salary).FirstOrDefault();
        if (old != null)
            tips.Add($"{old.Name} is {old.Age(today)} with {old.Contract!.Years} years left. Ability falls after 30; his value is highest now if you ever want to sell.");

        // General principles, one at a time, for players past the basics.
        if (stage != GuideStage.Beginner)
            tips.Add(Principles[(today.DayNumber / 7) % Principles.Length]);
        return tips;
    }

    private static string Level(int ca) => ca switch
    {
        >= 160 => "star",
        >= 140 => "starter in a top league",
        >= 120 => "solid rotation player",
        >= 100 => "pro in a good league",
        _ => "pro in a lower league",
    };

    private static readonly string[] Principles =
    {
        "Fit beats raw talent: a system that suits your best two players is worth more than a slightly better bench.",
        "Morale moves slowly. Promises you can't keep cost more than the talk they fixed.",
        "Contracts in a player's final year are cheapest to sell and riskiest to keep.",
        "Development coaches and playing time do more for young players than training intensity.",
        "The board judges the season against the goal they set, not the table. Know the target.",
        "Depth matters in the playoffs: injuries pile up, and an eight-man rotation needs eight real players.",
        "Pace up when you have the athletes and the depth; slow down to hide a short bench.",
        "Scouts' ranges narrow as they watch a player more. Don't pay for potential you can't see yet.",
    };

    // ------------------------------------------------------------------------------------------
    // This page

    public static (string Title, string Tip) PageTip(string page, GuideStage stage)
    {
        if (!Pages.TryGetValue(page, out var t)) return (page, "");
        return (page, stage == GuideStage.Beginner ? t.Beginner : t.Experienced);
    }

    private static readonly Dictionary<string, (string Beginner, string Experienced)> Pages = new()
    {
        ["Home"] = ("Your dashboard: record, next game, the board's mood and recent news. Press Continue (top right) to move a day forward; Sim week and Sim to next game go faster.",
                    "Glance at the board meter and next opponent, then play on. Sim to next game skips quiet days."),
        ["Inbox"] = ("Messages from the board, your staff, players and the league. Most are news; the coloured ones need a decision.",
                     "Skim for injuries, offers and board warnings; the rest is colour."),
        ["Roster"] = ("Everyone on the team. CA is current ability, PA potential (a range when your scouts aren't sure). Click a player for his full page: attributes, contract, history.",
                      "Sort by CA to see the real pecking order; watch contract years for who to extend or sell."),
        ["Rotation"] = ("Who starts and how many minutes each player plays. Leave it on automatic to let your assistant choose, or set it yourself. About 240 minutes are shared between the players each game.",
                        "Starters around 30-34 minutes, an 8-9 man rotation, and real minutes for prospects you want to grow."),
        ["Tactics"] = ("How we play. Pick an offensive system and a defence; each player gets a role, and the stars show how well he suits it. The scouting report shows the next opponent.",
                       "Match the system to your best two players, then tweak pace and three-point emphasis to the opponent."),
        ["Squad"] = ("Morale (how happy each player is) and chemistry (how well they play together). Talk to players here; promises raise morale but must be kept.",
                     "Fix the unhappy ones early; low morale spreads and costs games."),
        ["Board"] = ("What the owner expects this season, how confident they are in you (let it fall too low and you can be sacked), and the club's money: revenue, wage budget and balance.",
                     "Track the target wins and any extra demands. The wage budget is the ceiling for signings."),
        ["Staff & Training"] = ("Your assistant, development and strength coaches, physio and scout. Better staff mean better training, faster development, fewer injuries and clearer scouting.",
                                "Development coaches pay off with young rosters; a good physio matters for older ones."),
        ["Schedule & Results"] = ("Every game this season. Click a played game for its box score.",
                                  "Look ahead for back-to-backs and tough stretches."),
        ["Standings"] = ("The league table. In the NABL the top teams make the playoffs; in leagues without playoffs the table decides it.",
                         "Watch the teams around you and the playoff line."),
        ["Leagues"] = ("Every league in the game, ours included: standings, schedule and results, stat leaders, the rules and format, and past champions. Pick a region, then a league. Click any team or player name to open their page.",
                     "Scout other clubs' rosters from here: a team's page shows its players as your scouts see them."),
        ["College"] = ("College basketball: the Top 25, conferences, March Madness and prospects. Players turn pro at 23 and join free agency in the summer.",
                       "Track the best 22-year-olds in Prospects; they're free agents next summer."),
        ["League Leaders"] = ("The best players in the league by points, rebounds, assists and more.", "Spot breakout players before their value rises."),
        ["Transactions"] = ("Sign free agents, bid for players at other clubs, answer bids for yours, and propose trades. Free agents need contract talks: offer wage and years, and they accept, counter or walk.",
                            "Bid when the window is open; players with a year left are cheaper. Mind the wage budget."),
        ["Offseason"] = ("The summer, in steps: re-sign or release expiring players, then free agency. Continue moves to the next step.",
                         "Decide on every expiring player, then fill the roster before the season starts."),
        ["Awards"] = ("Season awards: MVP, All-League teams and more.", "Awards lift a player's reputation and his next contract."),
        ["History"] = ("Past champions and seasons.", "Champions and records over the years."),
        ["Settings"] = ("Face and logo packs, and the assistant: turn it off, or choose how much it explains.", "Assistant level, face and logo packs."),
    };
}
