using Hardwood.Core.Model;
using Hardwood.Core.People;

namespace Hardwood.Core.Season;

/// <summary>
/// The press room and the locker room (people part 2): a press conference when there's a story (a streak, an unhappy
/// player, a big game) and now and then a locker-room situation (a feud, a late arrival, a veteran offering to mentor,
/// a team dinner). Each has two or three answers whose effects you can read before you choose; your assistant gives the
/// safe answer if a week goes by. Effects land on morale (fading over a few weeks), the team's spirit (a lasting shift in
/// chemistry's target) and the board.
/// </summary>
public sealed partial class GameSession
{
    public MediaDesk Media => League.Media;

    /// <summary>Days between press conferences, and between locker-room moments.</summary>
    public const int PressGapDays = 13, LockerGapDays = 20, AnswerDays = 7;

    public List<PendingDecision> OpenDecisions => Employed ? Media.OpenFor(HumanTeam.Id).OrderBy(d => d.Expires).ToList() : new();

    /// <summary>Recent answered moments (yours and your assistant's), newest first.</summary>
    public List<PendingDecision> RecentDecisions(int n = 6) =>
        Media.Decisions.Where(d => !d.Open && d.TeamId == HumanTeam.Id).OrderByDescending(d => d.Id).Take(n).ToList();

    /// <summary>Wins (positive) or losses (negative) in a row, most recent first.</summary>
    public int MyStreak()
    {
        int streak = 0;
        foreach (var g in MyGames().Where(g => g.Played).OrderByDescending(g => g.Date))
        {
            int sign = (g.Us ?? 0) > (g.Them ?? 0) ? 1 : -1;
            if (streak != 0 && Math.Sign(streak) != sign) break;
            streak += sign;
        }
        return streak;
    }

    /// <summary>The spirit effects on your team right now, as chemistry factors (the Squad page lists them).</summary>
    public List<(string reason, double effect)> SpiritFactors() =>
        Media.Spirit.Where(s => s.TeamId == HumanTeam.Id && s.Until > Today).Select(s => (s.Reason, s.Amount)).ToList();

    private double Motivation => (Coach[Jobs.CoachAttr.Motivation] - 10) / 25.0;

    /// <summary>Monday: settle overdue moments, nudge chemistry by the spirit effects, maybe start a new one.</summary>
    internal void WeeklyMedia()
    {
        if (!Employed) return;
        foreach (var d in OpenDecisions.Where(d => d.Expires <= Today).ToList())
        {
            Resolve(d, d.SafeOption, auto: true);
            Media.AutoAnswered++;
        }
        // Spirit shifts chemistry's target: the weekly update moves a quarter of the way, so do the same for the shift.
        Media.Spirit.RemoveAll(s => s.Until <= Today.AddDays(-60));
        foreach (var s in Media.Spirit.Where(s => s.TeamId == HumanTeam.Id && s.Until > Today))
            HumanTeam.Chemistry = Math.Clamp(HumanTeam.Chemistry + s.Amount * 0.25, 0, 100);

        if (Media.OpenFor(HumanTeam.Id).Count() >= 2 || HumanTeam.Roster.Count < 5) return;
        PendingDecision? opened = null;
        if (Today.DayNumber - Media.LastPress.DayNumber >= PressGapDays && NewPress() is { } press)
        {
            Media.LastPress = Today;
            Open(opened = press);
        }
        else if (Today.DayNumber - Media.LastLocker.DayNumber >= LockerGapDays && _rng.Chance(0.5) && NewLocker() is { } locker)
        {
            Media.LastLocker = Today;
            Open(opened = locker);
        }
        // Delegated to the assistant coach (staff part 2): he answers on the spot, his way, and the week's message says so.
        if (opened != null && Delegated.Media && HumanTeam.Staff.Any(x => x.Role == StaffRole.AssistantCoach))
        {
            Resolve(opened, DelegatedAnswer(opened), auto: true, who: $"{HumanTeam.Staff.First(x => x.Role == StaffRole.AssistantCoach).Name} answered");
            Delegated.LastMedia = $"{Today:MMM d}: {opened.Title}: \"{opened.Options[opened.Chosen!.Value].Label}\"";
        }
    }

    /// <summary>Days before the same situation can come up again.</summary>
    private static int RepeatDays(string topic) => topic switch { "mentor" => 300, "feud" or "late" or "dinner" => 90, _ => 0 };

    private bool Fresh(string topic) => RepeatDays(topic) == 0 || !Media.LastTopic.TryGetValue(topic, out var last) || Today.DayNumber - last.DayNumber >= RepeatDays(topic);

    private void Open(PendingDecision d)
    {
        Media.LastTopic[d.Topic] = Today;
        d.Id = Media.NextId++;
        d.TeamId = HumanTeam.Id;
        d.Created = Today;
        d.Expires = Today.AddDays(AnswerDays);
        Media.Decisions.Add(d);
        if (Media.Decisions.Count > 80) Media.Decisions.RemoveAll(x => !x.Open && x.Id < Media.NextId - 80);
    }

    /// <summary>
    /// After the week's results go out: this week's press conference or locker-room moment rides on that message instead of
    /// adding its own (inbox volume: 230+ messages in three seasons was already over the target).
    /// </summary>
    internal void AttachPressToResults()
    {
        foreach (var d in Media.Decisions.Where(d => (d.Open || d.Created == Today) && d.MessageId == 0 && d.TeamId == HumanTeam.Id))
        {
            bool press = d.Kind == "press";
            string heading = press ? "PRESS CONFERENCE" : "IN THE LOCKER ROOM";
            string ask = d.Open
                ? $"{d.Text}\nAnswer below. If you haven't by {d.Expires:MMM d}, your assistant will: \"{d.Options[d.SafeOption].Label}\"."
                : $"{d.Text}\n{d.Outcome}"; // delegated: already handled by your assistant coach
            var week = Inbox.FirstOrDefault(m => m.Date == Today && m.Category == "Game" && m.Title.StartsWith("This week") && Media.ForMessage(m.Id) == null);
            if (week != null)
            {
                Inbox[Inbox.IndexOf(week)] = new InboxMessage { Id = week.Id, Date = week.Date, Title = week.Title + (press ? " · press conference" : " · locker room"),
                    Body = week.Body + $"\n\n{heading}: {ask}", Category = week.Category, Read = week.Read };
                d.MessageId = week.Id;
            }
            else
            {
                Post(d.Title, ask, press ? "Game" : "Squad");
                d.MessageId = Inbox[0].Id;
            }
        }
    }

    /// <summary>The same line twice in four months lands softer: each repeat counts for less ("they've heard it before").</summary>
    private double Freshness(PendingDecision d, int option, out bool repeated)
    {
        string key = $"{d.Topic}:{option}";
        int repeats = Media.Said.Count(x => x.Key == key && Today.DayNumber - x.Date.DayNumber < 120);
        Media.Said.Add(new SaidLine { Key = key, Date = Today });
        Media.Said.RemoveAll(x => Today.DayNumber - x.Date.DayNumber >= 120);
        repeated = repeats > 0;
        return 1 / (1 + 0.6 * repeats);
    }

    // ---- the press room

    private PendingDecision? NewPress()
    {
        int streak = MyStreak();
        var stats = MyStats;
        var table = MyTable;
        if (streak <= -4)
            return Press("losing-streak", $"{-streak} straight defeats",
                $"\"That's {-streak} defeats in a row. Is this group good enough?\"", 0, new()
                {
                    O("Back the players", "Squad morale up a little; the board would like more urgency"),
                    O("Demand more", "Professionals respond, thin-skinned players sulk; the board approves"),
                    O("Take the blame", "Pressure off the players and spirit up for three weeks; your standing with the board dips"),
                });
        if (streak >= 4)
            return Press("winning-streak", $"{streak} wins in a row",
                $"\"{streak} straight wins. Are you contenders now?\"", 1, new()
                {
                    O("\"We can beat anyone\"", "Morale up, the big egos most; the board likes the ambition"),
                    O("\"One game at a time\"", "Spirit up for four weeks"),
                    O($"Credit {TopScorer(stats)?.LastName ?? "the players"}", "Your top scorer gets a lasting lift; the rest a little"),
                });
        var last = MyGames().Where(g => g.Played && g.Date > Today.AddDays(-7)).OrderByDescending(g => Math.Abs((g.Us ?? 0) - (g.Them ?? 0))).FirstOrDefault();
        if (last != null && (last.Them ?? 0) - (last.Us ?? 0) >= 22)
            return Press("blowout", $"Beaten by {(last.Them ?? 0) - (last.Us ?? 0)} by {last.Opponent.Abbreviation}",
                $"\"{last.Them}-{last.Us} against {last.Opponent.FullName}. What happened out there?\"", 0, new()
                {
                    O("\"One bad night\"", "Squad morale up a little"),
                    O("\"Unacceptable\"", "Professionals respond, thin-skinned players sulk; the board approves"),
                    O($"Credit {last.Opponent.Abbreviation}", "Spirit up a little for two weeks"),
                });
        var sore = HumanTeam.Roster.Where(p => p.Morale < 35 && !(Media.AskedAbout.TryGetValue(p.Id, out var d) && Today.DayNumber - d.DayNumber < 60))
            .OrderByDescending(p => p.CurrentAbility).FirstOrDefault();
        if (sore != null)
        {
            Media.AskedAbout[sore.Id] = Today;
            var dp = Press("unhappy-player", $"Questions about {sore.Name}",
                $"\"{sore.Name} looks frustrated. Is there a problem?\"", 1, new()
                {
                    O("Back him in public", $"A lasting lift for {sore.LastName}; big egos in the squad notice"),
                    O("Keep it in house", $"No story; {sore.LastName} appreciates the discretion a little"),
                    O("Call him out", $"{sore.LastName} is hurt, the professionals approve, spirit dips"),
                });
            dp.PlayerIds.Add(sore.Id);
            return dp;
        }
        if (NextMyGame is { } next && table.Count > 2 && table.Take(2).Any(r => r.Team == next.Opponent) && next.Date <= Today.AddDays(4) && _rng.Chance(0.5))
            return Press("big-game", $"Next up: {next.Opponent.FullName}",
                $"\"{next.Opponent.FullName} are near the top of the table. How do you see it?\"", 0, new()
                {
                    O("Respect them", "Spirit up a little for three weeks"),
                    O("\"We're the better team\"", "Morale up (more with a motivating coach); the board likes it"),
                    O("No comment", "Nothing changes"),
                });
        return null;
    }

    private static PendingDecision Press(string topic, string title, string text, int safe, List<DecisionOption> options) =>
        new() { Kind = "press", Topic = topic, Title = $"Press conference: {title}", Text = text, SafeOption = safe, Options = options };

    private static DecisionOption O(string label, string hint) => new() { Label = label, Hint = hint };

    private Player? TopScorer(Dictionary<int, PlayerSeasonStats> stats) =>
        HumanTeam.Roster.OrderByDescending(p => stats.TryGetValue(p.Id, out var s) && s.Games > 0 ? (double)s.Points / s.Games : 0).FirstOrDefault();

    // ---- the locker room

    private PendingDecision? NewLocker()
    {
        var rot = HumanTeam.Roster.OrderByDescending(p => p.CurrentAbility).Take(10).ToList();
        var options = new List<Func<PendingDecision?>>
        {
            () =>
            {
                var hot = rot.Where(p => p.Hidden.Ego >= 14 || p.Hidden.Temperament <= 7).OrderByDescending(p => p.Hidden.Ego).Take(2).ToList();
                if (hot.Count < 2) return null;
                var (older, younger) = hot[0].Age(Today) >= hot[1].Age(Today) ? (hot[0], hot[1]) : (hot[1], hot[0]);
                var d = Locker("feud", $"{older.LastName} and {younger.LastName} clash",
                    $"{older.Name} and {younger.Name} had words at practice and it carried into the film session.", 0, new()
                    {
                        O("Sit them down together", "Both calm down; spirit up for three weeks (more with good man-management)"),
                        O($"Back {older.LastName}", $"{older.LastName} is pleased, {younger.LastName} is not"),
                        O("Let them sort it out", "Spirit dips for three weeks"),
                    });
                d.PlayerIds.AddRange(new[] { older.Id, younger.Id });
                return d;
            },
            () =>
            {
                var late = HumanTeam.Roster.Where(p => p.Hidden.Professionalism <= 7).OrderBy(p => p.Hidden.Professionalism).FirstOrDefault();
                if (late == null) return null;
                var d = Locker("late", $"{late.LastName} late again",
                    $"{late.Name} turned up late for practice for the second time this month. The others noticed.", 1, new()
                    {
                        O("Fine him", $"{late.LastName} sulks; the locker room respects it (spirit up)"),
                        O("A word in private", $"{late.LastName} appreciates it; nothing else changes"),
                        O("Let it go", "Spirit dips for three weeks"),
                    });
                d.PlayerIds.Add(late.Id);
                return d;
            },
            () =>
            {
                var vet = HumanTeam.Roster.Where(p => p.Age(Today) >= 30 && p.Hidden.Professionalism >= 13).OrderByDescending(p => p.Hidden.Professionalism).FirstOrDefault();
                int young = HumanTeam.Roster.Count(p => p.Age(Today) <= 23);
                if (vet == null || young == 0) return null;
                var d = Locker("mentor", $"{vet.LastName} offers to mentor",
                    $"{vet.Name} has offered to take the {young} young player{(young == 1 ? "" : "s")} under his wing: extra film, extra shooting, a word after games.", 0, new()
                    {
                        O("Make it official", $"{vet.LastName} and the young players get a lift; spirit up for four weeks"),
                        O("Not now", $"{vet.LastName} is a little disappointed"),
                    });
                d.PlayerIds.Add(vet.Id);
                return d;
            },
            () =>
            {
                if (MyStreak() < 3) return null;
                return Locker("dinner", "A team dinner?",
                    "The players want to celebrate the run with a team dinner, on the club.", 0, new()
                    {
                        O($"Pay for it (${DinnerCost / 1000}K)", "Spirit up for four weeks"),
                        O("Not this time", "Spirit dips a little for two weeks"),
                    });
            },
        };
        // Try them in a random order; the first that fits is this week's moment.
        foreach (var make in options.OrderBy(_ => _rng.NextDouble()))
            if (make() is { } d && Fresh(d.Topic)) return d;
        return null;
    }

    public const long DinnerCost = 40_000;

    private static PendingDecision Locker(string topic, string title, string text, int safe, List<DecisionOption> options) =>
        new() { Kind = "locker", Topic = topic, Title = $"Locker room: {title}", Text = text, SafeOption = safe, Options = options };

    // ---- answers

    /// <summary>Answer an open press question or locker-room moment. Returns what happened.</summary>
    public string Answer(int decisionId, int option)
    {
        var d = Media.Find(decisionId);
        if (d == null || d.TeamId != HumanTeam.Id) return "That moment has passed.";
        if (!d.Open) return d.Outcome ?? "Already answered.";
        if (option < 0 || option >= d.Options.Count) return "Pick one of the answers.";
        Media.Answered++;
        Coach.SeasonAnswers++;
        return Resolve(d, option, auto: false);
    }

    private string Resolve(PendingDecision d, int option, bool auto, string? who = null)
    {
        d.Chosen = option; d.Auto = auto;
        var team = HumanTeam;
        var squad = team.Roster;
        Player? P(int i) => i < d.PlayerIds.Count ? squad.FirstOrDefault(p => p.Id == d.PlayerIds[i]) : null;
        double fresh = Freshness(d, option, out bool repeated);
        double man = Coach.TalkMultiplier(true);
        double mot = 1 + Motivation;
        var notes = new List<string>();

        void Morale(Player? p, double delta) { if (p != null) p.Morale = Math.Clamp(p.Morale + delta * (delta > 0 ? fresh : 1), 0, 100); }
        void All(double delta, Func<Player, bool>? who = null) { foreach (var p in squad.Where(who ?? (_ => true))) Morale(p, delta); }
        void Lift(Player? p, double amount, int days, string reason) { if (p != null) PeopleEngine.Boost(p, amount * fresh, Today.AddDays(days), reason, Today); }
        void Spirit(double amount, int days, string reason) => Media.Spirit.Add(new SpiritEffect { TeamId = team.Id, Amount = Math.Round(amount > 0 ? amount * fresh : amount, 1), Until = Today.AddDays(days), Reason = reason });
        // The board hears the same line less each time too, so a run of the same answer can't cost (or win) you the job.
        void Board(double delta) { if (Job.Employed) Job.Confidence = Math.Clamp(Job.Confidence + delta * fresh, 0, 100); }

        switch (d.Topic, option)
        {
            case ("losing-streak", 0): All(4 * mot); Board(-1); notes.Add("The players appreciated the support."); break;
            case ("losing-streak", 1):
                All(3, p => p.Hidden.Professionalism >= 13); All(-5, p => p.Hidden.Temperament <= 7); Board(3);
                notes.Add("The professionals took it as a challenge; the thin-skinned took it personally."); break;
            case ("losing-streak", 2): All(3); Spirit(3, 21, "You took the heat in the press"); Board(-3); notes.Add("The pressure is on you now, not them."); break;

            case ("winning-streak", 0): All(4 * mot); All(3, p => p.Hidden.Ego >= 14); Board(2); notes.Add("The squad loved it. Now they have to back it up."); break;
            case ("winning-streak", 1): Spirit(3, 28, "Keeping everyone grounded"); notes.Add("Steady, humble, focused."); break;
            case ("winning-streak", 2):
                var top = TopScorer(MyStats); Lift(top, 8, 28, "Praised in the press"); All(2);
                notes.Add($"{top?.LastName ?? "The team"} enjoyed the headlines."); break;

            case ("unhappy-player", 0):
                Lift(P(0), 10 * man, 30, "Backed in public"); All(-1, p => p.Hidden.Ego >= 15 && p.Id != d.PlayerIds[0]);
                notes.Add($"{P(0)?.LastName} was glad of it; a couple of big egos wondered where their praise was."); break;
            case ("unhappy-player", 1): Morale(P(0), 2); notes.Add("No story today."); break;
            case ("unhappy-player", 2):
                Morale(P(0), -12); if (P(0) is { } sore) sore.UnhappyWeeks += 1;
                All(2, p => p.Hidden.Professionalism >= 14 && p.Id != d.PlayerIds[0]); Spirit(-2, 14, "A teammate criticised in public");
                notes.Add($"{P(0)?.LastName} took it badly."); break;

            case ("blowout", 0): All(3 * mot); notes.Add("The squad moved on quickly."); break;
            case ("blowout", 1):
                All(3, p => p.Hidden.Professionalism >= 13); All(-4, p => p.Hidden.Temperament <= 7); Board(2);
                notes.Add("Practice was sharp the next morning; a couple of players took it personally."); break;
            case ("blowout", 2): Spirit(1.5, 14, "Gracious in defeat"); notes.Add("Honest and calm."); break;

            case ("big-game", 0): Spirit(1.5, 21, "Respect for a rival"); notes.Add("Quiet confidence."); break;
            case ("big-game", 1): All(4 * mot); Board(1); notes.Add("Bulletin-board material, but the squad is fired up."); break;
            case ("big-game", 2): notes.Add("Nothing to report."); break;

            case ("feud", 0): Morale(P(0), 3); Morale(P(1), 3); Spirit(3 * man, 21, "A feud settled"); notes.Add("They shook hands."); break;
            case ("feud", 1): Morale(P(0), 6); Morale(P(1), -8); notes.Add($"{P(1)?.LastName} feels you took sides."); break;
            case ("feud", 2): Spirit(-3, 21, "A feud left to fester"); notes.Add("It hasn't gone away."); break;

            case ("late", 0): Morale(P(0), -6); Spirit(2, 21, "Standards enforced"); notes.Add($"{P(0)?.LastName} paid up, grumbling."); break;
            case ("late", 1): Morale(P(0), 3 * man); notes.Add("He promised it won't happen again."); break;
            case ("late", 2): Morale(P(0), 1); Spirit(-3, 21, "Lateness let go"); notes.Add("The others noticed you let it go."); break;

            case ("mentor", 0):
                Lift(P(0), 5, 42, "Mentoring the young players"); All(4, p => p.Age(Today) <= 23); Spirit(2, 28, "A veteran mentoring the kids");
                notes.Add($"{P(0)?.LastName} has taken the young players under his wing."); break;
            case ("mentor", 1): Morale(P(0), -4); notes.Add("He understood, but he was disappointed."); break;

            case ("dinner", 0): team.Money.Balance -= DinnerCost; Spirit(4, 28, "A team dinner"); notes.Add("A good night out."); break;
            case ("dinner", 1): Spirit(-1, 14, "No team dinner"); notes.Add("A few long faces."); break;
        }
        if (repeated) notes.Add("They've heard that one before, so it counted for less.");
        who ??= auto ? "Your assistant answered for you" : "You answered";
        d.Outcome = $"{who}: \"{d.Options[option].Label}\". {string.Join(" ", notes)}";
        return d.Outcome;
    }
}
