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

public sealed record GuideAdvice(GuideStage Stage, string Headline, IReadOnlyList<GuideTask> Tasks, string PageTitle, string PageTip, IReadOnlyList<string> Strategy,
    string LastGameTitle = "", IReadOnlyList<string>? LastGameWhy = null);

/// <summary>A strategy note with a key that stays the same while its point does (for "don't repeat yourself").</summary>
public sealed record StrategyNote(string Key, string Text);

/// <summary>What the player has seen and done, kept with the app settings (it's the person who learns, not the save).</summary>
public sealed class GuideProgress
{
    public HashSet<string> PagesSeen { get; set; } = new();
    public HashSet<string> Dismissed { get; set; } = new();
    public int DaysPlayed { get; set; }
    public int SeasonsPlayed { get; set; }
    /// <summary>On how many different days each strategy note has been shown; a note goes quiet after three (critic review #2).</summary>
    public Dictionary<string, int> NoteDays { get; set; } = new();
    public Dictionary<string, int> NoteLastDay { get; set; } = new();
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
        var (title, tip) = PageTip(page, stage, s.Employed && s.InCollege);
        var notes = new List<string>();
        int day = s.Today.DayNumber;
        foreach (var n in StrategyNotes(s, stage))
        {
            int shown = progress.NoteDays.GetValueOrDefault(n.Key);
            if (shown >= MaxNoteDays) continue;
            if (progress.NoteLastDay.GetValueOrDefault(n.Key) != day) { progress.NoteDays[n.Key] = shown + 1; progress.NoteLastDay[n.Key] = day; }
            notes.Add(n.Text);
        }
        var (gameTitle, why) = LastGame(s);
        return new GuideAdvice(stage, Headline(s, tasks), tasks, title, tip, notes, gameTitle, why);
    }

    /// <summary>A strategy note is shown on at most this many days (its key changes when the situation does).</summary>
    public const int MaxNoteDays = 3;

    // ------------------------------------------------------------------------------------------
    // Why the last game went the way it did (critic review #2: "the assistant never says why")

    public static (string title, IReadOnlyList<string> why) LastGame(GameSession s)
    {
        if (!s.Employed) return ("", Array.Empty<string>());
        var g = s.MyGames().LastOrDefault(x => x.Played && x.Result != null);
        if (g?.Result is not { } r || s.Today.DayNumber - g.Date.DayNumber > 6) return ("", Array.Empty<string>());
        var us = r.Home.Team == s.HumanTeam ? r.Home : r.Away;
        var them = us == r.Home ? r.Away : r.Home;
        bool won = us.Points > them.Points;
        var factors = new List<(double weight, string text)>();
        void Add(double edge, string good, string bad) { if (Math.Abs(edge) >= 1) factors.Add((won ? edge : -edge, edge > 0 ? good : bad)); }
        int to = them.Turnovers - us.Turnovers;
        Add(to * 1.1, $"We won the turnover battle ({us.Turnovers} to {them.Turnovers}).", $"Turnovers: we gave it away {us.Turnovers} times to their {them.Turnovers}.");
        int reb = (us.OReb + us.DReb) - (them.OReb + them.DReb);
        Add(reb * 0.8, $"We owned the glass ({(us.OReb + us.DReb)}-{(them.OReb + them.DReb)}, {us.OReb} offensive).", $"They out-rebounded us {(them.OReb + them.DReb)}-{(us.OReb + us.DReb)} ({them.OReb} offensive boards).");
        double our3 = us.ThreePA == 0 ? 0 : (double)us.ThreePM / us.ThreePA, their3 = them.ThreePA == 0 ? 0 : (double)them.ThreePM / them.ThreePA;
        Add((our3 - their3) * 40, $"Shooting from three: {us.ThreePM}/{us.ThreePA} against their {them.ThreePM}/{them.ThreePA}.", $"They shot {them.ThreePM}/{them.ThreePA} from three to our {us.ThreePM}/{us.ThreePA}.");
        Add((us.FTA - them.FTA) * 0.4, $"We got to the line {us.FTA} times to their {them.FTA}.", $"They got to the line {them.FTA} times to our {us.FTA}.");
        var ourTop = us.Lines.OrderByDescending(l => l.Points).First();
        var theirTop = them.Lines.OrderByDescending(l => l.Points).First();
        if (theirTop.Points >= 30) factors.Add((won ? -2 : 4, $"{theirTop.Player.Name} hurt us: {theirTop.Points} points." + (won ? " We survived it." : " Put a stopper on him next time.")));
        if (ourTop.Points >= 28) factors.Add((won ? 4 : 1, $"{ourTop.Player.Name} carried us with {ourTop.Points}."));
        int bench = us.Lines.Where(l => l.Minutes > 0).OrderByDescending(l => l.Minutes).Skip(5).Sum(l => l.Points);
        int theirBench = them.Lines.Where(l => l.Minutes > 0).OrderByDescending(l => l.Minutes).Skip(5).Sum(l => l.Points);
        Add((bench - theirBench) * 0.25, $"The bench outscored theirs {bench}-{theirBench}.", $"Their bench outscored ours {theirBench}-{bench}.");
        if (Math.Abs(us.Points - them.Points) <= 4) factors.Add((3, won ? "A coin flip at the end that went our way." : "Lost a coin flip at the end: nothing to panic about."));
        var why = factors.OrderByDescending(f => f.weight).Take(3).Select(f => f.text).ToList();
        string title = $"Last game: {(won ? "W" : "L")} {us.Points}-{them.Points} {(g.Home ? "vs" : "at")} {g.Opponent.Abbreviation}";
        return (title, why);
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
            if (s.InCollege)
                yield return new($"college-summer-{today.Year}", 40, "Nothing to sign this summer",
                    "College programs don't sign pros. The seniors have graduated and the new class has enrolled (see Roster). Press Continue until the new season starts.", "Roster");
            else if (off.Phase == OffseasonPhase.FreeAgency && team.Roster.Count >= min)
                yield return new($"summer-done-{today.Year}", 40, "Finish the summer when you're happy",
                    "The roster is legal. Keep shopping if you have money left, or press Continue to start the new season.", null);
        }

        // ---- money and the market
        if (s.IncomingBids.Count > 0)
            yield return new($"bids-{s.IncomingBids.Count}-{today}", 8, $"Answer {s.IncomingBids.Count} transfer bid{(s.IncomingBids.Count == 1 ? "" : "s")}",
                $"Another club wants {Names(s.IncomingBids.Select(b => s.League.AllActivePlayers.Concat(team.Roster).FirstOrDefault(p => p.Id == b.PlayerId)).OfType<Player>().Distinct().Take(2))}. Accept, reject, or let it expire; a fee can fund a replacement.", "Transactions");
        if (s.OverWageBudget)
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

        // ---- the recruiting trail (C3, #177): open scholarships with too few offers out, when you're recruiting alone.
        if (s.TrailOpen && s.Phase != SessionPhase.Offseason && s.TrailWorld is { } tw)
        {
            int cls = tw.Season.Year + 1;
            int open = tw.OpenScholarships(team, cls), offers = tw.OffersOut(team, cls);
            if (open > 0 && today < College.CollegeWorld.SigningDay(cls) && today >= College.CollegeWorld.CommitmentsOpen(cls) && offers < open && !s.Delegated.Recruiting)
                yield return new($"trail-{cls}-{open}-{offers}", 18, $"{open} scholarship{(open == 1 ? "" : "s")} open, {offers} offer{(offers == 1 ? "" : "s")} out",
                    $"Signing day is {College.CollegeWorld.SigningDay(cls):MMM d}. Seniors only commit to schools that offered. Best fits on the College page's Recruiting tab, or tick \"My assistant uses the hours I leave\".", "College");
        }

        // ---- the board
        if (s.Job.Confidence < 40)
            yield return new($"board-{(int)s.Job.Confidence / 5}", 12, s.InCollege ? "The AD is losing patience" : "The board is losing patience",
                $"Confidence is {s.Job.Confidence:F0}/100. {Goal(s)} Check the Board page for what else they want.", "Board");

        // ---- staff
        var missing = Enum.GetValues<StaffRole>().Where(r => team.Staff.All(st => st.Role != r)).ToList();
        if (missing.Count > 0)
            yield return new($"staff-{string.Join('.', missing)}", 25, $"Hire a {Staff.RoleName(missing[0]).ToLowerInvariant()}",
                $"{string.Join(", ", missing.Select(Staff.RoleName))} {(missing.Count == 1 ? "is" : "are")} missing. Staff improve training, development, fitness and scouting; hire on the Staff & Training page.", "Staff & Training");

        // Staff part 2: contracts you have to extend yourself, raised from February so there's time.
        if (s.Phase is SessionPhase.RegularSeason or SessionPhase.Postseason && today.Month is >= 2 and <= 6 && s.StaffExpiring is { Count: > 0 } ending)
            yield return new($"staffdeal-{today.Year}-{ending.Count}", 26, $"{ending.Count} staff contract{(ending.Count == 1 ? "" : "s")} end this summer",
                $"{string.Join(", ", ending.Select(x => x.Name))}. Extend on the Staff & Training page or they leave when the season ends.", "Staff & Training");

        // ---- the press room and the locker room (people part 2)
        foreach (var d in s.OpenDecisions.Take(1))
            yield return new($"decision-{d.Id}", 18, d.Title,
                $"Answer in the Inbox by {d.Expires:MMM d}, or I'll say \"{d.Options[d.SafeOption].Label.Trim('"')}\" for you. Each answer shows what it will do.", "Inbox");

        // ---- inbox
        // Only mail that needs you (critic review #2: "26 unread messages" sat at the top of the panel all season).
        // Only the last fortnight's: an unread note from two months ago no longer needs you (dial-in item 4: the task sat on the
        // panel 87 weeks out of 87 because old unread mail kept it alive).
        var important = s.Inbox.Where(m => !m.Read && m.Date.DayNumber >= today.DayNumber - 14 && m.Category is "Board" or "Medical" or "Transactions" or "Squad" or "Staff").ToList();
        if (important.Count > 0)
            yield return new($"inbox-{important.Max(m => m.Id)}", 22, $"{important.Count} message{(important.Count == 1 ? "" : "s")} that need{(important.Count == 1 ? "s" : "")} you",
                $"Latest: {important[0].Title}. Board, medical, squad and transfer news is worth reading; results and world news can wait.", "Inbox");

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
                if (!progress.PagesSeen.Contains(pg) && !(s.InCollege && pg is "Transactions" or "Offseason")) // hidden for a college coach (C2)
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

    private static int RosterMin(GameSession s) => s.InCollege ? 0 : s.InNabl ? s.League.Finance.RosterMin : World.GameWorld.ManagedMinimum;

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

    /// <summary>The notes as plain text (all of them, ignoring what has been shown).</summary>
    public static IReadOnlyList<string> Strategy(GameSession s, GuideStage stage) => StrategyNotes(s, stage).Select(n => n.Text).ToList();

    public static List<StrategyNote> StrategyNotes(GameSession s, GuideStage stage)
    {
        var tips = new List<StrategyNote>();
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
            string boss = s.InCollege ? "the AD" : "the board";
            tips.Add(new($"pace-{s.League.Season}-{(int)(pace - e.TargetWins) / 5}", pace >= e.TargetWins
                ? $"We're on pace for {pace:F0} wins; {boss} wants {e.TargetWins}. Keep the rotation steady."
                : $"We're on pace for {pace:F0} wins and {boss} wants {e.TargetWins}. Look at the weakest starter, or a tactic that suits our best players."));
        }

        // Young players who need minutes.
        var stats = s.MyStats;
        var young = team.Roster.Where(p => p.Age(today) <= 22)
            .Select(p => (p, pot: Scouting.Potential(p, Scouting.Knowledge(team, p)).Mid, mpg: stats.TryGetValue(p.Id, out var st) ? st.Mpg : 0))
            .Where(x => x.pot >= x.p.CurrentAbility + 15 && x.mpg < 15 && x.p.IsAvailable(today))
            .OrderByDescending(x => x.pot).Take(2).ToList();
        foreach (var (p, pot, mpg) in young)
            tips.Add(new($"young-{p.Id}-{s.Phase == SessionPhase.Offseason}", s.Phase == SessionPhase.Offseason
                ? $"{p.Name} ({p.Age(today)}) could become a {Level((int)pot)} but played {mpg:F0} minutes a game last season. Plan real minutes for him next year."
                : $"{p.Name} ({p.Age(today)}) could become a {Level((int)pot)} but plays {mpg:F0} minutes. Young players develop faster with real minutes."));

        // Heavy minutes.
        var tired = s.Phase == SessionPhase.Offseason ? new List<Player>()
            : team.Roster.Where(p => stats.TryGetValue(p.Id, out var st) && st.Games >= 5 && st.Mpg > 37).ToList();
        if (tired.Count > 0)
            tips.Add(new($"tired-{string.Join('.', tired.Select(p => p.Id))}", $"{Names(tired.Take(2))} average{(tired.Count == 1 ? "s" : "")} over 37 minutes. Tired legs mean worse shooting late and more injuries."));

        // The weakest position.
        var byPos = team.Roster.GroupBy(p => p.PrimaryPosition).ToDictionary(g => g.Key, g => g.Max(p => p.CurrentAbility));
        var gaps = Enum.GetValues<Position>().Where(pos => !byPos.ContainsKey(pos)).ToList();
        if (gaps.Count > 0) tips.Add(new($"gap-{gaps[0]}", $"No natural {gaps[0]} on the roster. Players out of position lose effectiveness."));
        else if (byPos.Count >= 5)
        {
            var weakest = byPos.OrderBy(kv => kv.Value).First();
            var best = byPos.Values.Max();
            if (best - weakest.Value >= 15)
                tips.Add(new($"weak-{weakest.Key}-{s.League.Season}", $"Our best {weakest.Key} is well below the rest of the starters. {(s.InCollege ? "That's the position to recruit for." : "That's the position to upgrade when money allows.")}"));
        }

        // Money.
        if (team.Money.Ready)
        {
            long room = team.Money.WageBudget - s.MyCommittedWages;
            if (room > team.Money.WageBudget * 0.08)
                tips.Add(new($"room-{s.League.Season}-{s.WindowOpen}", $"There's ${room / 1e6:F1}M of wage room. A free agent or a transfer could use it{(s.WindowOpen ? " while the window is open" : "")}."));
        }

        // College (C2): this year's graduates, and who replaces them.
        if (s.InCollege && s.World.College is { } cw && s.Phase != SessionPhase.Offseason)
        {
            var summer = new DateOnly(cw.Season.Year + 1, 7, 1);
            var leaving = team.Roster.Where(p => p.Age(summer) >= College.CollegeWorld.GraduateAge).OrderByDescending(p => p.CurrentAbility).ToList();
            var next = team.Roster.Where(p => p.Age(summer) <= 20).OrderByDescending(p => p.PotentialAbility).FirstOrDefault();
            if (leaving.Count > 0)
                tips.Add(new($"grads-{cw.Season.Year}-{leaving.Count}", $"{Names(leaving.Take(2))}{(leaving.Count > 2 ? $" and {leaving.Count - 2} more" : "")} turn pro next summer. "
                    + (next != null ? $"{next.Name} is the one to grow into the gap: give him minutes now." : "Plan who takes their minutes.")));
            if (cw.Season.Selected && !cw.Season.Seeds.ContainsKey(team.Id))
                tips.Add(new($"nit-{cw.Season.Year}", "We missed March Madness. The AD remembers; next year's goal starts here."));
        }

        // Ageing stars on long deals.
        var old = team.Roster.Where(p => p.Age(today) >= 31 && (p.Contract?.Years ?? 0) >= 3).OrderByDescending(p => p.Contract!.Salary).FirstOrDefault();
        if (old != null)
            tips.Add(new($"old-{old.Id}-{s.League.Season}", $"{old.Name} is {old.Age(today)} with {old.Contract!.Years} years left. Ability falls after 30; his value is highest now if you ever want to sell."));

        // General principles, one at a time, for players past the basics.
        if (stage != GuideStage.Beginner)
        {
            var list = s.InCollege ? CollegePrinciples : Principles;
            int k = (today.DayNumber / 7) % list.Length;
            tips.Add(new($"{(s.InCollege ? "college-" : "")}principle-{k}-{today.DayNumber / 7}", list[k]));
        }
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

    /// <summary>College coaching in a sentence each (C2): no money, five-year players, March Madness.</summary>
    private static readonly string[] CollegePrinciples =
    {
        "In college nobody is paid: the roster you have is the roster you coach. Develop it.",
        "Players stay up to five seasons. A freshman's minutes now are next year's senior leader.",
        "The AD judges the season in March. A good conference record gets you seeded; the tournament gets you paid in prestige.",
        "Home court is worth more in college. Win your home games and you're on the bubble's right side.",
        "Fit beats raw talent: a system that suits your best two players is worth more than a slightly better bench.",
    };

    // ------------------------------------------------------------------------------------------
    // This page

    public static (string Title, string Tip) PageTip(string page, GuideStage stage) => PageTip(page, stage, false);

    public static (string Title, string Tip) PageTip(string page, GuideStage stage, bool college)
    {
        if (college && CollegePages.TryGetValue(page, out var c)) return (page, stage == GuideStage.Beginner ? c.Beginner : c.Experienced);
        if (!Pages.TryGetValue(page, out var t)) return (page, "");
        return (page, stage == GuideStage.Beginner ? t.Beginner : t.Experienced);
    }

    /// <summary>Page tips for a college coach (C2), where they differ from the pros'.</summary>
    private static readonly Dictionary<string, (string Beginner, string Experienced)> CollegePages = new()
    {
        ["College"] = ("The Recruiting tab is yours: 24 coach hours a week for high-school prospects. Scout to learn what a kid wants, call, visit, then offer. No offer, no commitment.",
                       "Look at Leaning before you spend hours: a prospect who prefers three schools to you needs an official visit, not a call."),
        ["Home"] = ("Your program at a glance: the overall record, the conference race (from January), the next game and news. Press Continue to move a day forward.",
                    "Non-conference games set your rating; conference games decide the conference tournament's seeds."),
        ["Roster"] = ("Your scholarship players (13, plus up to 2 walk-ons). Nobody is paid. Players stay until they turn 23, then graduate into the pros.",
                      "Watch who graduates next summer (the chip at the top) and who grows into their minutes."),
        ["Board"] = ("Your athletic director: the season goal (judged by March Madness), how confident the AD is in you, and the program's prestige and national rank. ADs fire coaches.",
                     "Track the target wins and the tournament goal; the rank says where the selection committee sees you."),
        ["Standings"] = ("Your conference's table from conference games, and the national Top 25. The top eight in each conference play its tournament; the champion gets an automatic bid.",
                         "Win the conference tournament and you're in March Madness whatever your rating."),
        ["Schedule & Results"] = ("Every game this season: non-conference in November and December, conference play from January, then the conference tournament and March Madness.",
                                  "Invitationals and showcases against big names lift your rating for Selection Sunday."),
    };

    private static readonly Dictionary<string, (string Beginner, string Experienced)> Pages = new()
    {
        ["Home"] = ("Your dashboard: record, next game, the board's mood and recent news. Press Continue (top right) to move a day forward; Sim week and Next game go faster.",
                    "Glance at the board meter and next opponent, then play on. Next game skips quiet days."),
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
        ["Standings"] = ($"The league table. In the {Hardwood.Core.Names.Brand.Top} the top teams make the playoffs; in leagues without playoffs the table decides it.",
                         "Watch the teams around you and the playoff line."),
        ["Leagues"] = ("Every league in the game, ours included: standings, schedule and results, stat leaders, the rules and format, and past champions. Pick a region, then a league. Click any team or player name to open their page.",
                     "Scout other clubs' rosters from here: a team's page shows its players as your scouts see them."),
        ["College"] = ("College basketball: the Top 25, conferences, March Madness, prospects and the high-school recruiting classes. Seniors sign by mid-April, enroll at 18 and turn pro at 23.",
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
