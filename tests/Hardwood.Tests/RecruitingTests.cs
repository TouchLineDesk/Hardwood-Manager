using Hardwood.Core;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Senior recruiting (decision #157, college depth): court seniors, they commit, they sign on graduation day.</summary>
public class RecruitingTests
{
    private static GameSession Season(ulong seed, string? path = null)
    {
        var probe = new WorldGenerator(new Rng(seed)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));
        var id = probe.Teams.OrderByDescending(t => Hardwood.Core.World.QuickSim.Strength(t)).ElementAt(3).Id;
        var s = GameSession.NewGame(seed, id, path);
        while (s.Today < new DateOnly(2026, 12, 1)) s.AdvanceDay();
        return s;
    }

    [Fact]
    public void The_recruiting_note_comes_once_in_your_first_season_at_a_club()
    {
        // Review #11 (run 33, #391): a pro career's inbox gets the senior-recruiting introduction in its first season only.
        const string title = "Recruit college seniors for your club";
        var first = Season(71);
        Assert.Single(first.Inbox, m => m.Title == title);
        Assert.DoesNotContain(first.Inbox, m => m.Title == "Senior recruiting is open");

        var probe = new WorldGenerator(new Rng(71)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));
        var id = probe.Teams.OrderByDescending(t => Hardwood.Core.World.QuickSim.Strength(t)).ElementAt(3).Id;
        var later = GameSession.NewGame(71, id, null);
        later.Job.HiredSeason = later.League.Season - 1; // as if this were his second season at the club
        while (later.Today < new DateOnly(2026, 12, 1)) later.AdvanceDay();
        Assert.True(later.RecruitingOpen);
        Assert.DoesNotContain(later.Inbox, m => m.Title == title);
    }

    [Fact]
    public void Courting_a_senior_wins_his_commitment_and_he_signs_on_graduation_day()
    {
        var s = Season(71);
        Assert.True(s.RecruitingOpen);
        var seniors = s.Seniors();
        Assert.True(seniors.Count >= 60, $"{seniors.Count} seniors");
        Assert.All(seniors, p => Assert.True(p.Age(s.GraduationDay) >= 23));

        // A mid-ranked senior (no star premium on the interest he needs).
        var p = seniors[45];
        var before = s.InterestIn(p);
        Assert.Equal(Math.Clamp(before.Appeal * 0.45 + before.Minutes * 0.25 + before.Effort, 0, 100), before.Total, 6);
        Assert.Equal(55, before.Need);

        s.TopUpRecruitingPoints();
        int points = s.Recruiting!.Points;
        Assert.InRange(points, 6, 20);
        Assert.StartsWith("You spoke to", s.CallRecruit(p));
        Assert.Equal(points - 1, s.Recruiting.Points);
        Assert.True(s.InterestIn(p).Effort > 0);

        // An offer below his ask doesn't win him; enough effort and his ask does.
        long ask = s.RecruitDemand(p).Wage;
        s.OfferRecruit(p, ask / 3, 3);
        Assert.Null(s.Recruiting.Find(p.Id)!.CommittedTeamId);
        for (int i = 0; i < 2; i++) { s.Recruiting.Points = 20; s.VisitRecruit(p); }
        Assert.Contains("twice", s.VisitRecruit(p));
        for (int i = 0; i < 6 && !s.InterestIn(p).Enough; i++) { s.Recruiting.Points = 20; s.CallRecruit(p); }
        Assert.True(s.InterestIn(p).Enough, $"interest {s.InterestIn(p).Total:0} of {s.InterestIn(p).Need:0}");
        var reply = s.OfferRecruit(p, ask, 3);
        Assert.Contains("commits to the", reply);
        Assert.Equal(s.HumanTeam.Id, s.Recruiting.Find(p.Id)!.CommittedTeamId);
        Assert.DoesNotContain(s.Inbox, m => m.Title == $"{p.Name} commits to you"); // you saw his answer on the spot

        // Play to the summer: rivals land some of the top seniors along the way; ours stays ours.
        int guard = 0;
        int rivalCommits = 0;
        while (s.Phase != SessionPhase.Offseason && guard++ < 400)
        {
            rivalCommits = s.Recruiting?.Prospects.Values.Count(x => x.CommittedTeamId != null && x.CommittedTeamId != s.HumanTeam.Id) ?? rivalCommits;
            s.AdvanceDay();
        }
        Assert.True(rivalCommits >= 20, $"{rivalCommits} rival commitments by the end of the season");
        Assert.True(s.HumanTeam.Roster.Contains(p), string.Join(" | ", s.Inbox.Where(m => m.Body.Contains(p.Name) || m.Title.Contains(p.Name)).Select(m => m.Title + ": " + m.Body)) + $" roster {s.HumanTeam.Roster.Count} FA? {s.League.FreeAgents.Contains(p)} team {p.TeamId} age {p.Age(s.Today)} today {s.Today}");
        Assert.Equal(ask, p.Contract!.Salary);
        Assert.Equal(3, p.Contract.Years);
        var rival = s.League.Teams.Where(t => t != s.HumanTeam).SelectMany(t => t.Roster)
            .Count(x => x.RookieSeason == s.League.Season + 1 && seniors.Take(40).Contains(x));
        Assert.True(rival >= 10, $"only {rival} of the top 40 seniors signed with rival NABL clubs on graduation");
    }

    [Fact]
    public void A_committed_senior_cannot_be_courted_and_your_courting_resets_with_a_new_club()
    {
        var s = Season(72);
        var p = s.Seniors()[50];
        var st = s.Recruiting!.For(p.Id);
        st.CommittedTeamId = s.League.Teams.First(t => t != s.HumanTeam).Id;
        Assert.Contains("has committed to", s.CallRecruit(p));
        Assert.NotNull(s.InterestIn(p).Blocked);

        var q = s.Seniors()[51];
        s.Recruiting.Points = 20;
        s.CallRecruit(q);
        Assert.True(s.InterestIn(q).Effort > 0);
        // Effort belongs to the club that put it in.
        s.Recruiting.Find(q.Id)!.TeamId = -1;
        Assert.Equal(0, s.InterestIn(q).Effort);
    }

    [Fact]
    public void The_best_seniors_take_more_courting()
    {
        var s = Season(74);
        var seniors = s.Seniors();
        Player Free(int from) => seniors.Skip(from).First(p => s.Recruiting!.Find(p.Id)?.CommittedTeamId == null && s.InterestIn(p).Blocked == null);
        var (star, good, plain) = (Free(0), Free(15), Free(45));
        Assert.Equal(0.75, s.EffortScale(star));
        Assert.Equal(0.9, s.EffortScale(good));
        Assert.Equal(1.0, s.EffortScale(plain));
        foreach (var p in new[] { star, good, plain }) { s.Recruiting!.Points = 20; s.VisitRecruit(p); }
        Assert.Equal(15 * 0.75, s.InterestIn(star).Effort, 6);
        Assert.Equal(15 * 0.9, s.InterestIn(good).Effort, 6);
        Assert.Equal(15, s.InterestIn(plain).Effort, 6);
        // The same two visits and five calls put in less with a top-10 senior than with an ordinary one.
        s.Recruiting!.Points = 20; s.VisitRecruit(star); s.Recruiting.Points = 20; s.VisitRecruit(plain);
        for (int i = 0; i < 5; i++) { s.Recruiting.Points = 20; s.CallRecruit(star); s.Recruiting.Points = 20; s.CallRecruit(plain); }
        Assert.Equal(s.InterestIn(plain).Effort * 0.75, s.InterestIn(star).Effort, 6);
        Assert.Contains("counts for less", s.CallRecruit(star));
    }

    [Fact]
    public void Seniors_want_a_roster_spot()
    {
        var s = Season(75);
        var others = s.Seniors().Skip(40).Take(MaxTake).ToList();
        int signed = s.HumanTeam.Roster.Count(x => x.Contract is { Years: > 1 });
        int spots = Math.Clamp(s.League.Finance.RosterMax - signed, 1, GameSession.MaxCommitments);
        foreach (var p in others.Take(spots)) s.Recruiting!.For(p.Id).CommittedTeamId = s.HumanTeam.Id;
        Assert.Equal(spots, s.MyCommitments);
        Assert.NotNull(s.NoSpotFor());

        // A keen senior with a good offer still won't commit while every spot is promised.
        var q = others.Skip(spots).First(p => s.Recruiting!.Find(p.Id)?.CommittedTeamId == null);
        s.Recruiting!.For(q.Id).TeamId = s.HumanTeam.Id;
        s.Recruiting.For(q.Id).Effort = 60;
        Assert.True(s.InterestIn(q).Enough);
        var reply = s.OfferRecruit(q, s.RecruitDemand(q).Wage, 2);
        Assert.Contains("roster spot", reply);
        Assert.Null(s.Recruiting.Find(q.Id)!.CommittedTeamId);
    }

    private const int MaxTake = 20;

    [Fact]
    public void The_board_survives_a_save()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-rec-{Guid.NewGuid():N}.hwsave");
        try
        {
            var s = Season(73, path);
            var p = s.Seniors()[30];
            s.Recruiting!.Points = 20;
            s.VisitRecruit(p);
            s.SaveNow();
            var loaded = GameSession.Load(path);
            var st = loaded.Recruiting!.Find(p.Id);
            Assert.NotNull(st);
            Assert.Equal(1, st!.Visits);
            Assert.Equal(17, loaded.Recruiting.Points);
            Assert.True(st.Shortlisted);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
