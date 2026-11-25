using Hardwood.Core.College;
using Hardwood.Core.Model;

namespace Hardwood.Core.Season;

/// <summary>
/// Slice 10 of the revised launch plan (#1164, run 201): your coaching legacy. Your college seasons are kept in a book
/// (<see cref="CollegeWorld.MyCareer"/>), milestones are told once, your assistants can leave for head jobs (your coaching
/// tree), and a coach who leaves a program after a long, winning spell, you included, joins its Hall of Fame. No random draw.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>Your college coaching book (empty outside college).</summary>
    public CoachBook MyCoachBook => World.College?.MyCareer ?? new CoachBook();

    /// <summary>The season you just finished, counted on the day it's reviewed (before a sacking, so a fired coach's last
    /// season counts too).</summary>
    private void RecordMyCollegeSeason()
    {
        if (!Employed || !InCollege || World.College is not { } cw) return;
        var book = cw.MyCareer;
        if (book.Seasons.Any(x => x.Year == League.Season)) return;
        var all = MyRecord;
        if (all.Wins + all.Losses == 0) return;
        book.Seasons.Add(new CoachSeason
        {
            Year = League.Season, ProgramId = HumanTeam.Id, Program = HumanTeam.FullName, Wins = all.Wins, Losses = all.Losses,
            Seed = cw.SeedOf(HumanTeam.Id), Reached = cw.MadnessReached(HumanTeam.Id),
        });
        ReportMilestones();
    }

    /// <summary>Tells any milestone your career has reached and not yet told: career wins (this season's games count as
    /// they're played), your first NCAA bid, win, Final Four and title, seasons coached and the first branch of your tree.</summary>
    private void ReportMilestones()
    {
        if (!Employed || !InCollege || World.College is not { } cw) return;
        var book = cw.MyCareer;
        bool counted = book.Seasons.Any(x => x.Year == League.Season);
        int wins = book.Wins + (counted ? 0 : MyRecord.Wins);
        int reached = counted ? -1 : cw.MadnessReached(HumanTeam.Id);
        foreach (var (key, title, body) in CoachingLegacy.Reached(book, Coach.Name, HumanTeam.FullName, wins, book.Seasons.Count, reached, book.Bids).ToList())
        {
            if (book.Has(key)) continue;
            book.Milestones.Add(new CoachMilestone { Key = key, Date = Today, Text = title });
            Post(title, body, "Board");
            cw.News.Add(($"{Coach.Name}: {NewsWords(title)}", body));
        }
    }

    /// <summary>"Your first Final Four" → "his first Final Four" for the news.</summary>
    private static string NewsWords(string title) =>
        title.StartsWith("Your ") ? "his " + title[5..] : title;

    /// <summary>Your assistant who takes a computer program's head job this spring, if one is ready (see
    /// <see cref="CoachingLegacy.AssistantReady"/>): never a bigger job than yours, and never a name already on a bench.</summary>
    private Staff? TreeHire(CollegeWorld cw, Team job, int year, HashSet<string> benches)
    {
        if (!Employed || !InCollege || job.Id == HumanTeam.Id || !IsCollegeProgram(HumanTeam)) return null;
        if (CarouselSize(job) > CarouselSize(HumanTeam)) return null;
        int here = cw.MyCareer.Seasons.Count(x => x.ProgramId == HumanTeam.Id);
        return HumanTeam.Staff.Where(x => x.Role == StaffRole.AssistantCoach && !benches.Contains(x.Name))
            .OrderByDescending(x => x.Coaching).ThenBy(x => x.Id)
            .FirstOrDefault(x => CoachingLegacy.AssistantReady(x, year, here));
    }

    /// <summary>The spring's coaching legacy news: coaches into their programs' Halls of Fame, and your assistant's new job.</summary>
    private void PostCoachingLegacy(CollegeWorld cw, List<(Team Program, LegacyInductee Who)> hall, TreeBranch? branch)
    {
        bool mine = Employed && InCollege;
        foreach (var (t, who) in hall)
        {
            string body = $"{who.Name} joins the {t.FullName} Hall of Fame as a head coach ({who.Years}): {who.Line}.";
            cw.News.Add(($"{t.FullName}: {who.Name} into the Hall of Fame", body));
            if (mine && t.Id == HumanTeam.Id) Post($"{who.Name} joins your program's Hall of Fame", body + " His plaque is on the Team page's Legacy tab.", "League");
        }
        if (branch != null && mine)
        {
            int n = cw.MyCareer.Tree.Count;
            string body = $"{branch.Name} leaves your staff to become the head coach at {branch.Program}. "
                + (n == 1 ? "He's the first branch of your coaching tree." : $"He's the {RivalCoaches.Ordinal(n)} branch of your coaching tree.")
                + " His place on your staff is open: hire a new assistant coach on the Staff page.";
            Post($"Your assistant {branch.Name} takes the {branch.Program} job", body, "Board");
            cw.News.Add(($"{branch.Program} hires {branch.Name} from {branch.FromProgram}'s staff", $"{branch.Name}, an assistant to {Coach.Name} at {branch.FromProgram}, is the new head coach at {branch.Program}."));
        }
        ReportMilestones();
    }

    /// <summary>You leave a college program: after a long, winning spell there you join its Hall of Fame (the coaches' rule).</summary>
    private void InductMe(Team club)
    {
        if (World.College is not { } cw || !IsCollegeProgram(club)) return;
        var here = cw.MyCareer.Seasons.Where(x => x.ProgramId == club.Id).ToList();
        if (here.Count == 0) return;
        int w = here.Sum(x => x.Wins), l = here.Sum(x => x.Losses), bids = here.Count(x => x.Seed > 0), titles = here.Count(x => x.Reached >= 7);
        if (cw.InductCoach(club.Id, Coach.Name, here.Min(x => x.Year), here.Max(x => x.Year), here.Count, w, l, bids, titles, here.Max(x => x.Year) + 1) is { } who)
        {
            string body = $"{club.FullName} put you in their Hall of Fame as you leave: {who.Line}.";
            Post($"Into the {club.FullName} Hall of Fame", body, "Board");
            cw.News.Add(($"{club.FullName}: {Coach.Name} into the Hall of Fame", $"{Coach.Name} joins the {club.FullName} Hall of Fame as a head coach ({who.Years}): {who.Line}."));
        }
    }
}
