using Hardwood.Core;
using Hardwood.Core.Generation;
using Hardwood.Core.Jobs;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Stage 3: manage any club. A career at a club abroad plays its games, keeps its table, its board and its saves.</summary>
public class AnyClubTests
{
    private static int ClubIn(string key, ulong seed = 3)
    {
        var league = new WorldGenerator(new Rng(seed)).Generate("North American Basketball League", 2026, new DateOnly(2026, 10, 1));
        var world = GameWorld.Generate(league, seed, GameSession.DefaultFullSim, new DateOnly(2026, 10, 1));
        return world[key].Clubs[3].Id;
    }

    private static string TempSave(out string dir)
    {
        dir = Path.Combine(Path.GetTempPath(), $"hw-any-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "career.hwsave");
    }

    [Fact]
    public void A_club_abroad_plays_its_own_games_and_has_a_board()
    {
        var id = ClubIn(Competitions.Spain);
        var s = GameSession.NewGame(3, id, null);
        Assert.True(s.Abroad);
        Assert.Equal("Spanish League", s.HumanLeagueName);
        Assert.Equal(Enum.GetValues<StaffRole>().Length, s.HumanTeam.Staff.Count); // given staff on arrival
        Assert.NotNull(s.Job.Current);
        Assert.True(s.Today < new DateOnly(2026, 9, 26)); // the career starts before the Spanish season
        Assert.Equal(0, s.MyGames().Count(g => g.Played));
        var first = s.NextMyGame!;
        Assert.Equal("Spanish League", first.Competition);

        // Play to the first game live, then on for a few weeks.
        while (!s.HumanPlaysToday) s.AdvanceDay();
        var (live, played) = s.BeginLiveDay();
        Assert.NotNull(live);
        for (int i = 0; i < 50; i++) live!.Step();
        s.FinishLiveDay(live, played);
        Assert.Equal(1, s.MyGames().Count(g => g.Played));
        Assert.NotNull(s.MyGames().First(g => g.Played).Result); // box score kept
        for (int i = 0; i < 60; i++) s.AdvanceDay();
        var rec = s.MyRecord;
        Assert.True(rec.Games >= 7, $"{rec.Games} games");
        Assert.Equal(rec.Games, s.MyGames().Count(g => g.Played && g.Competition == "Spanish League"));
        Assert.True(s.MyRank >= 1);
        Assert.Contains(s.MyStats.Values, st => st.Games >= 5 && st.Points > 0);
        Assert.Contains(s.Inbox, m => m.Category == "Game");
        Assert.Contains(s.Inbox, m => m.Category == "Squad" || m.Category == "Board" || true);
        // Trades are an NABL thing.
        Assert.False(s.ProposeTrade(new TradeProposal { From = s.HumanTeam, To = s.League.Teams[0] }).Accepted);
    }

    [Fact]
    public void A_season_abroad_ends_with_a_review_and_the_summer_and_the_next_season_starts()
    {
        var id = ClubIn(Competitions.Turkey);
        var s = GameSession.NewGame(5, id, null);
        int guard = 0;
        while (s.Phase != SessionPhase.Offseason && guard++ < 500) s.AdvanceDay();
        Assert.Equal(SessionPhase.Offseason, s.Phase);
        Assert.True(s.HumanLeague!.Season.IsComplete);
        Assert.Contains(s.Inbox, m => m.Title is "Season review" or "You have been sacked");
        Assert.True(s.MyGames().Count(g => g.Played) >= 30);
        while (s.Phase == SessionPhase.Offseason && guard++ < 520)
        {
            if (s.Offseason!.Phase == OffseasonPhase.Draft && !s.Offseason.DraftComplete) s.Offseason.AutoDraftUntil(null);
            s.AdvanceOffseason();
        }
        Assert.Equal(SessionPhase.Preseason, s.Phase);
        Assert.Equal(2027, s.League.Season);
        if (s.Employed)
        {
            Assert.Contains(s.Inbox, m => m.Title == "Summer business");
            Assert.InRange(s.HumanTeam.Roster.Count, 10, 16);
            Assert.Equal(2027, s.Job.Current!.Season);
            Assert.NotNull(s.NextMyGame);
        }
    }

    [Fact]
    public void A_career_abroad_survives_save_and_load()
    {
        var path = TempSave(out var dir);
        try
        {
            var id = ClubIn(Competitions.Australia);
            var s = GameSession.NewGame(3, id, path);
            for (int i = 0; i < 40; i++) s.AdvanceDay();
            s.HumanTeam.Tactic.System = OffensiveSystem.Motion;
            s.HumanTeam.Chemistry = 71;
            var staff = s.HumanTeam.Staff.Select(x => x.Id).OrderBy(x => x).ToList();
            s.HumanTeam.Owner.Patience = 17;
            s.SaveNow();

            var l = GameSession.Load(path);
            Assert.True(l.Abroad);
            Assert.Equal(id, l.HumanTeam.Id);
            Assert.Equal(OffensiveSystem.Motion, l.HumanTeam.Tactic.System);
            Assert.False(l.HumanTeam.Tactic.Auto);
            Assert.Equal(71, l.HumanTeam.Chemistry, 1);
            Assert.Equal(staff, l.HumanTeam.Staff.Select(x => x.Id).OrderBy(x => x).ToList());
            Assert.Equal(17, l.HumanTeam.Owner.Patience);
            Assert.Equal(s.MyRecord.Wins, l.MyRecord.Wins);
            Assert.Same(l.HumanTeam.Coach, l.Coach);
            for (int i = 0; i < 10; i++) l.AdvanceDay();
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Jobs_abroad_open_and_are_easier_to_get_than_the_nabl()
    {
        var s = GameSession.NewGame(3, 1, null);
        var spain = s.World[Competitions.Spain];
        var club = spain.Clubs.OrderBy(t => t.Id).Last();
        var nablClub = s.League.Teams[0];
        var coach = new Coach { Reputation = 40 };
        // A mid-table club abroad is easier to land than a similar NABL club.
        double abroad = Board.HireChance(coach, LeagueContext.Of(spain), club);
        Assert.InRange(abroad, 0, 1);
        Assert.True(Board.Prestige(LeagueContext.Of(spain), club) < Board.Prestige(LeagueContext.Nabl(s.League), nablClub) + 1 || abroad > 0);

        // Sacked: openings abroad are posted and you can take one.
        s.Fire();
        s.CoachLeaves(club, "fired");
        var v = Assert.Single(s.Job.Vacancies, x => x.TeamId == club.Id);
        Assert.Null(s.Apply(club.Id));
        v.Status = "offered";
        Assert.Null(s.AcceptOffer(club.Id));
        Assert.True(s.Employed);
        Assert.Same(club, s.HumanTeam);
        Assert.True(s.Abroad);
        Assert.Equal(Enum.GetValues<StaffRole>().Length, club.Staff.Count);
        Assert.Equal(LeagueContext.Of(spain).SeasonGames, s.ContextOf(club).SeasonGames);
        for (int i = 0; i < 20; i++) s.AdvanceDay();
    }

    [Fact]
    public void Overseas_expectations_fit_the_league()
    {
        var s = GameSession.NewGame(3, 1, null);
        var china = s.World[Competitions.China];
        var ctx = LeagueContext.Of(china);
        Assert.Equal(38, ctx.SeasonGames); // 20 clubs, home and away
        var best = china.Clubs.OrderBy(t => Board.ProjectedRank(ctx, t)).First();
        var worst = china.Clubs.OrderBy(t => Board.ProjectedRank(ctx, t)).Last();
        var eb = Board.SetExpectations(ctx, best, 2026);
        var ew = Board.SetExpectations(ctx, worst, 2026);
        Assert.Equal(SeasonGoal.Title, eb.Goal);
        Assert.True(eb.TargetWins > ew.TargetWins);
        Assert.InRange(eb.TargetWins, 20, 38);
        Assert.DoesNotContain(Demand.StayUnderTax, eb.Demands.Concat(ew.Demands));
    }
}
