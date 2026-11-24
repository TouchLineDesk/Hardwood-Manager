using System.Text.RegularExpressions;
using Hardwood.Core.College;
using Hardwood.Core.Season;
using Xunit;

namespace Hardwood.Tests;

/// <summary>K26 (run 179; critic review #22 item 4): the college coach's first day is written for a college coach.</summary>
public class Run179CollegeWelcomeTests
{
    private static void NoProWords(string text, string where)
    {
        foreach (var w in GameSession.ProOnlyWelcomeWords)
            Assert.False(Regex.IsMatch(text, $@"\b{Regex.Escape(w)}\b", RegexOptions.IgnoreCase), $"{where} uses \"{w}\": {text}");
    }

    private static GameSession Career(ulong seed, string school)
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(seed, 2026, GameSession.DefaultFullSim);
        var id = world.College!.Programs.First(t => (t.City + " " + t.Name).Contains(school)).Id;
        return GameSession.NewGame(seed, id, null);
    }

    [Fact]
    public void Virginia_Techs_first_day_names_the_program_the_AD_the_goal_the_rival_and_the_first_game()
    {
        var s = Career(31, "Virginia Tech");
        var welcome = s.Inbox.Single(m => m.Title == "Welcome to the job").Body;
        Assert.StartsWith($"You're the new head coach of {s.HumanTeam.TheFullName} (", welcome);
        Assert.Contains(s.MyConference!.Name, welcome);
        Assert.Contains($"Athletic director {s.HumanTeam.Owner.Name} wants you to", welcome);
        Assert.Contains($"around {s.Job.Current!.TargetWins} regular-season wins", welcome);
        Assert.Contains("Virginia", welcome[(welcome.IndexOf("rival is", StringComparison.Ordinal))..]);
        Assert.Contains("Your first game is", welcome);
        Assert.Contains(s.NextMyGame!.Opponent.TheFullName, welcome);
        Assert.Matches(@"scholarships? open in the Class of 20\d\d|Every scholarship", welcome);
        Assert.Contains("first-week guide", welcome);
        Assert.Contains("college players and coaches are made up", welcome);
        NoProWords(welcome, "Virginia Tech's Welcome");
    }

    [Fact]
    public void No_college_first_week_inbox_reads_like_a_GM_and_every_welcome_variant_is_college()
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026, 2026, GameSession.DefaultFullSim);
        var cw = world.College!;
        // The strongest, the weakest, a program with a named rivalry, one with an in-state rival only, and one with none.
        var picks = new List<int>
        {
            cw.Programs.OrderByDescending(p => cw.Prestige[p.Id]).First().Id,
            cw.Programs.OrderBy(p => cw.Prestige[p.Id]).First().Id,
            cw.Programs.First(p => cw.RivalsOf(p.Id).Any(r => r.Rivalry.Name != null)).Id,
            cw.Programs.First(p => cw.RivalsOf(p.Id) is { Count: > 0 } r && r.All(x => x.Rivalry.InState)).Id,
        };
        if (cw.Programs.FirstOrDefault(p => cw.RivalsOf(p.Id).Count == 0) is { } lone) picks.Add(lone.Id);
        foreach (var id in picks)
        {
            var s = GameSession.NewGame(2026, id, null);
            string who = s.HumanTeam.FullName;
            for (int d = 0; d < 7; d++) s.AdvanceDay();
            var board = s.Inbox.Where(m => m.Category == "Board").ToList();
            Assert.Contains(board, m => m.Title == "Welcome to the job");
            foreach (var m in board) NoProWords(m.Title + " " + m.Body, $"{who}'s \"{m.Title}\"");
            NoProWords(s.CollegeWelcomeBody(), $"{who}'s Welcome");
            NoProWords(s.CollegeHiredBody(), $"{who}'s new-job welcome");
            NoProWords(s.CollegeSeasonWelcomeBody(), $"{who}'s new-season welcome");
            Assert.Contains("head coach of", s.CollegeHiredBody());
            Assert.Contains(s.RivalLine() == null ? "Your first game" : "rival is", s.CollegeWelcomeBody());
        }
    }

    [Fact]
    public void A_pro_career_keeps_its_GM_welcome()
    {
        var s = GameSession.NewGame(2026, 1, null);
        Assert.Contains(s.Inbox, m => m.Title == "Welcome to the job" && m.Body.Contains("general manager") && m.Body.Contains("New York Knicks"));
        Assert.Null(s.RivalLine());
    }

    [Fact]
    public void A_summer_hire_with_no_game_left_reads_once()
    {
        var line = GameSession.FirstGameSentence("", null);
        Assert.DoesNotContain("press Continue", line);
        Assert.Contains("next season", line);
        NoProWords(line, "the summer hire's line");
    }
}
