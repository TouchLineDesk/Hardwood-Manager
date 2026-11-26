using Hardwood.Core.International;
using Hardwood.Core.Season;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>International plan I6a (run 214, #108): the national-team job. Federations call once a year from Oct 15, by reputation
/// (smaller nations first); you take one alongside your club job and pick its twelve, which the windows and tournaments then use.</summary>
public class Run214NationalJobTests
{
    private readonly ITestOutputHelper _out;
    public Run214NationalJobTests(ITestOutputHelper output) => _out = output;

    private static GameSession NewCareer(string? path = null, string program = "Virginia Tech")
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(31, 2026, GameSession.DefaultFullSim);
        var me = world.College!.Programs.First(t => t.FullName.StartsWith(program));
        return GameSession.NewGame(31, me.Id, path);
    }

    [Fact]
    public void Reputation_decides_how_high_up_the_ranking_the_calls_come_from()
    {
        Assert.Equal(36, NationalJob.BestRankFor(30));
        Assert.Equal(26, NationalJob.BestRankFor(50));
        Assert.Equal(16, NationalJob.BestRankFor(70));
        Assert.Equal(1, NationalJob.BestRankFor(100));

        var s = NewCareer();
        var ranked = NationalTeams.Ranked(s);
        int last = ranked.Where(t => t.Rank > 0).Max(t => t.Rank);
        var day = new DateOnly(2026, 10, 15);
        foreach (var rep in new[] { 10.0, 30, 50, 70, 90, 100 })
        {
            var offers = NationalJob.OffersFor(ranked, rep, s.Seed, day);
            _out.WriteLine($"rep {rep}: " + string.Join(", ", offers.Select(o => $"{o.Name} ({o.Rank})")));
            Assert.InRange(offers.Count, 1, NationalJob.MaxOffers);
            int best = Math.Min(NationalJob.BestRankFor(rep), last);
            Assert.All(offers, o => Assert.True(o.Rank >= best - NationalJob.Band && o.Rank <= best + NationalJob.Band));
            Assert.All(offers, o => Assert.Equal(day.AddDays(NationalJob.OfferDays), o.Expires));
            Assert.Equal(offers, NationalJob.OffersFor(ranked, rep, s.Seed, day)); // its own stream: the same every time
        }
        // A famous coach hears from a top-10 nation; a new one never does.
        Assert.All(NationalJob.OffersFor(ranked, 100, s.Seed, day), o => Assert.True(o.Rank <= 1 + NationalJob.Band));
        Assert.All(NationalJob.OffersFor(ranked, 30, s.Seed, day), o => Assert.True(o.Rank >= Math.Min(36, last - NationalJob.Band)));
    }

    [Fact]
    public void Offers_come_on_Oct_15_once_a_year_and_lapse_unanswered()
    {
        var s = NewCareer();
        var job = s.NationalJobState;
        _out.WriteLine($"career starts {s.Today:yyyy-MM-dd}");
        while (s.Today < new DateOnly(2026, 10, 16)) s.AdvanceDay();
        Assert.Equal(2026, job.LastOfferYear);
        Assert.NotEmpty(job.Offers);
        Assert.Contains(s.Inbox, m => m.Title.Contains("want you as head coach"));
        var offered = job.Offers.ToList();
        s.AdvanceDay();
        Assert.Equal(offered, job.Offers); // once a year
        while (s.Today <= offered[0].Expires) s.AdvanceDay();
        Assert.Empty(job.Offers);
        Assert.Contains(s.Inbox, m => m.Title == "National team offers lapsed");
        Assert.Null(job.Code);
    }

    [Fact]
    public void Your_twelve_replaces_the_federations_and_survives_a_reload()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hw-run214-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "career.hwsave");
        try
        {
            var s = NewCareer(path);
            var day = s.Today;
            var fed = NationalTeams.For(s, day);
            var job = s.NationalJobState;
            job.Offers = NationalJob.OffersFor(NationalTeams.Ranked(s), s.Coach.Reputation, s.Seed, day);
            var offer = job.Offers[0];
            Assert.True(s.AcceptNationalOffer(offer.Code));
            Assert.Empty(job.Offers);
            Assert.Equal(offer.Code, job.Code);
            var mine = fed.First(t => t.Nation.Code == offer.Code);
            Assert.Equal(mine.Squad.Select(x => x.Player.Id).OrderBy(i => i), job.Picks.OrderBy(i => i));

            // Picks from the federation's twelve build the same team; other nations never change.
            var same = NationalTeams.For(s, day);
            Assert.Equal(mine.Strength, same.First(t => t.Nation.Code == offer.Code).Strength);
            Assert.True(same.First(t => t.Nation.Code == offer.Code).CoachPicked);
            foreach (var t in fed.Where(t => t.Nation.Code != offer.Code))
                Assert.Equal(t.Squad.Select(x => x.Player.Id), same.First(u => u.Nation.Code == t.Nation.Code).Squad.Select(x => x.Player.Id));

            // Drop the best player for the best one left out: the twelve and its strength follow your picks.
            var star = mine.Squad[0].Player.Id;
            var bench = mine.Pool.First(x => !job.Picks.Contains(x.Player.Id)).Player.Id;
            Assert.False(s.ToggleNationalPick(bench)); // twelve is the most
            Assert.True(s.ToggleNationalPick(star));
            Assert.True(s.ToggleNationalPick(bench));
            var picked = NationalTeams.For(s, day).First(t => t.Nation.Code == offer.Code);
            Assert.DoesNotContain(picked.Squad, x => x.Player.Id == star);
            Assert.Contains(picked.Squad, x => x.Player.Id == bench);
            Assert.Equal(NationalTeams.SquadSize, picked.Squad.Count);
            Assert.True(picked.Strength <= mine.Strength);
            _out.WriteLine($"{offer.Name}: strength {mine.Strength} → {picked.Strength} without {mine.Squad[0].Player.Name}");

            // Fewer than twelve picks: the federation's next best fill the places.
            Assert.True(s.ToggleNationalPick(bench));
            var eleven = NationalTeams.For(s, day).First(t => t.Nation.Code == offer.Code);
            Assert.Equal(NationalTeams.SquadSize, eleven.Squad.Count);

            Assert.True(s.ToggleNationalPick(bench));
            var picks = job.Picks.ToList();
            s.SaveNow();
            SqliteConnection.ClearAllPools();
            var back = GameSession.Load(path);
            var bj = back.NationalJobState;
            Assert.Equal(offer.Code, bj.Code);
            Assert.Equal(day, bj.Since);
            Assert.Equal(picks, bj.Picks);
            Assert.Equal(picked.Squad.Select(x => x.Player.Id), NationalTeams.For(back, day).First(t => t.Nation.Code == offer.Code).Squad.Select(x => x.Player.Id));

            // Back to the federation's twelve, then step down: the nation is the federation's again.
            back.ResetNationalPicks();
            Assert.Equal(mine.Squad.Select(x => x.Player.Id).OrderBy(i => i), bj.Picks.OrderBy(i => i));
            Assert.True(back.ResignNationalJob());
            Assert.Null(bj.Code);
            Assert.Empty(bj.Picks);
            Assert.Single(bj.Past);
            Assert.False(NationalTeams.For(back, day).First(t => t.Nation.Code == offer.Code).CoachPicked);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void A_pick_who_is_hurt_on_the_day_is_replaced_for_that_event()
    {
        var s = NewCareer();
        var day = s.Today;
        var job = s.NationalJobState;
        job.Offers = NationalJob.OffersFor(NationalTeams.Ranked(s), 100, s.Seed, day);
        Assert.True(s.AcceptNationalOffer(job.Offers[0].Code));
        var team = NationalTeams.For(s, day).First(t => t.Nation.Code == job.Code);
        var hurt = team.Squad[0].Player;
        hurt.Injury = new Hardwood.Core.Model.Injury { Name = "Sprained ankle", Severity = Hardwood.Core.Model.InjurySeverity.Moderate, Occurred = day, ReturnDate = day.AddDays(30) };
        var onDay = NationalTeams.For(s, day.AddDays(1)).First(t => t.Nation.Code == job.Code);
        Assert.DoesNotContain(onDay.Squad, x => x.Player == hurt);
        Assert.Equal(NationalTeams.SquadSize, onDay.Squad.Count);
        Assert.Contains(hurt.Id, job.Picks); // still your pick for when he's fit

        // Review (run 214): a pick who has left the pool doesn't hold a place, so the picker never locks.
        job.Picks[^1] = -12345;
        var outside = NationalTeams.Ranked(s).First(t => t.Nation.Code == job.Code).Pool.First(x => !job.Picks.Contains(x.Player.Id)).Player.Id;
        Assert.True(s.ToggleNationalPick(outside));
        Assert.DoesNotContain(-12345, job.Picks);
        Assert.Equal(NationalTeams.SquadSize, job.Picks.Count);
    }

    [Fact]
    public void Without_a_job_the_teams_are_the_federations()
    {
        var s = NewCareer();
        Assert.True(s.World.International.Coaching.IsEmpty);
        Assert.Null(s.World.International.Coaching.Overrides);
        var a = NationalTeams.For(s, s.Today);
        var b = NationalTeams.Build(WorldCup.Entries(s, s.Today), s.Today);
        Assert.Equal(a.Select(t => (t.Nation.Code, t.Strength)), b.Select(t => (t.Nation.Code, t.Strength)));
        Assert.All(a, t => Assert.False(t.CoachPicked));
    }
}
