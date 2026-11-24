using Hardwood.Core.College;
using Hardwood.Core.Model;

namespace Hardwood.Core.Season;

/// <summary>
/// K26 (run 179; critic review #22 item 4): the college coach's first day. A college career's Welcome used to call him
/// "head coach and general manager", lead with the NBA champions and explain NBA contracts and salaries. These are the
/// college versions of the three welcomes (a new career, a new job, a new season), written for a college coach: the
/// program and its conference, the athletic director and the goal, the rival, the scholarships open and the recruiting
/// guide, and the first game. Nothing here draws a random number.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>Words a college coach's welcomes never use (the pro front office's).</summary>
    public static readonly string[] ProOnlyWelcomeWords = { "general manager", "salary", "salaries", "NBA champions", "Finals MVP", "contracts", "owner", "wage" };

    /// <summary>"Your rival is the Louisville Cardinals (the Battle of the Bluegrass; at home on Dec 20)." / "Your in-state rival is …":
    /// the program's first rival in a sentence, with the date you meet this season, or null.</summary>
    public string? RivalLine()
    {
        if (!InCollege || World.College is not { } c) return null;
        var (rivalry, rivalId) = c.RivalsOf(HumanTeam.Id).FirstOrDefault();
        if (rivalry == null || c.Programs.FirstOrDefault(t => t.Id == rivalId) is not { } rival) return null;
        var notes = new List<string>();
        if (rivalry.Name != null) notes.Add(rivalry.Label);
        if (MyGames().FirstOrDefault(g => !g.Played && g.Opponent == rival && g.Date >= Today) is { } meet)
            notes.Add($"{(meet.Neutral ? "on a neutral court" : meet.Home ? "at home" : "away")} on {meet.Date:MMM d}");
        return $"Your {(rivalry.InState ? "in-state " : "")}rival is {rival.TheFullName}{(notes.Count > 0 ? $" ({string.Join("; ", notes)})" : "")}.";
    }

    /// <summary>The AD's goal for this season in a sentence: "Athletic director X wants you to make March Madness (around 20 regular-season wins)."</summary>
    private string CollegeGoalLine()
    {
        if (Job.Current is not { } e) return "";
        string goal = e.Survive ? Jobs.Board.SurviveText(e) : Jobs.Board.LowerFirst(Jobs.Board.Describe(e.Goal, ContextOf(HumanTeam)));
        return $"Athletic director {HumanTeam.Owner.Name} wants you to {goal.TrimEnd('.')} (around {e.TargetWins} regular-season wins).";
    }

    /// <summary>The recruiting line: scholarships open in the class you recruit now, signing day, the guide.</summary>
    private string CollegeRecruitingLine()
    {
        if (!TrailOpen) return "";
        var c = World.College!;
        int cls = c.RecruitingClass(Today);
        int open = c.OpenScholarships(HumanTeam, cls);
        string spots = open switch
        {
            0 => $"Every scholarship in the Class of {cls} is already taken, so this year's trail is about next year's class.",
            1 => $"You have one scholarship open in the Class of {cls}.",
            _ => $"You have {open} scholarships open in the Class of {cls}.",
        };
        return $"\n\nRecruiting: {spots} Signing day is {CollegeWorld.SigningDay(cls):MMMM d}. You have {CollegeWorld.WeeklyHours} coach hours a week: "
            + "the first-week guide on the College page (Recruiting) walks you through scouting, offering and a visit. A prospect can only commit to you once you've offered. "
            + "Your assistant spends any hours you leave unused (Staff page, delegation).";
    }

    private string CollegeFirstGameLine(string lead) => FirstGameSentence(lead, NextMyGame);

    /// <summary>"Your first game is at the Kentucky Wildcats on Tue, Nov 3: …", or, with no game left this season (a summer
    /// hire, review of run 179), a line that doesn't repeat itself.</summary>
    public static string FirstGameSentence(string lead, MyGame? g)
    {
        if (g == null) return $"{lead}Your schedule for next season comes out in the fall; until then, the summer is for recruiting and your roster.";
        string where = g.Neutral ? $"against {g.Opponent.TheFullName} on a neutral court" : g.Home ? $"at home to {g.Opponent.TheFullName}" : $"at {g.Opponent.TheFullName}";
        return $"{lead}Your first game is {where} on {g.Date:ddd, MMM d}: set your rotation and tactics, then press Continue.";
    }

    /// <summary>The college career's first message (a new career).</summary>
    public string CollegeWelcomeBody()
    {
        var conf = MyConference;
        var lines = new List<string>
        {
            $"You're the new head coach of {HumanTeam.TheFullName}{(conf != null ? $" ({conf.Name})" : "")}. {CollegeGoalLine()}".TrimEnd(),
        };
        if (RivalLine() is { } rival) lines.Add(rival);
        var text = string.Join(" ", lines) + "\n\n" + CollegeFirstGameLine("");
        text += CollegeRecruitingLine();
        if (World.College?.IsReal == true)
            text += $"\n\nThe world is real: every Division I program in its 2026-27 conference, with its real name, colours and tournament history (checked {Hardwood.Core.RealWorld.RealColleges.Checked}). "
                + "The college players and coaches are made up; the pros your graduates join are the real ones (the History and Leagues pages).";
        return text;
    }

    /// <summary>A new college job (after a move or a sacking).</summary>
    public string CollegeHiredBody()
    {
        var conf = MyConference;
        var text = $"Athletic director {HumanTeam.Owner.Name} has hired you as head coach of {HumanTeam.TheFullName}{(conf != null ? $" ({conf.Name})" : "")}."
            + (RivalLine() is { } rival ? " " + rival : "")
            + "\n\n" + CollegeFirstGameLine("");
        return text + CollegeRecruitingLine();
    }

    /// <summary>A new college season.</summary>
    public string CollegeSeasonWelcomeBody()
    {
        var c = World.College!;
        var champ = c.History.LastOrDefault();
        string lead = champ != null ? $"Last season's national champions: {champ.Champion} (over {champ.RunnerUp}). " : "";
        return CollegeFirstGameLine(lead) + (RivalLine() is { } rival ? "\n\n" + rival : "");
    }
}
