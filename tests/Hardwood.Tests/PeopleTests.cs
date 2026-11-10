using Hardwood.Core;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.People;
using Hardwood.Core.Season;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Players as people (decision #64): morale, chemistry, trade requests, talks, promises, saving.</summary>
public class PeopleTests
{
    private static League NewLeague(ulong seed = 3) => new WorldGenerator(new Rng(seed)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));
    private static readonly DateOnly On = new(2026, 12, 7);

    private static PlayerSeasonStats Played(Player p, Team t, int games, double mpg) =>
        new() { PlayerId = p.Id, TeamId = t.Id, Season = 2026, Games = games, Minutes = games * mpg };

    private static void Weeks(League league, int weeks, Dictionary<int, PlayerSeasonStats> stats, double winPct = 0.5, List<Promise>? promises = null)
    {
        for (int i = 0; i < weeks; i++) PeopleEngine.Weekly(league, On.AddDays(7 * i), _ => winPct, stats, league.Teams[0].Id, promises);
    }

    [Fact]
    public void A_benched_star_gets_unhappy_and_a_regular_stays_content()
    {
        var league = NewLeague();
        var team = league.Teams[0];
        var star = team.Roster.MaxBy(p => p.CurrentAbility)!;
        var regular = team.Roster.OrderByDescending(p => p.CurrentAbility).ElementAt(6);
        star.Hidden.Ego = 16; star.Hidden.Ambition = 16;
        var stats = new Dictionary<int, PlayerSeasonStats> { [star.Id] = Played(star, team, 15, 12), [regular.Id] = Played(regular, team, 15, 22) };
        Weeks(league, 8, stats);
        Assert.True(star.Morale < 40, $"star {star.Morale:F0}");
        Assert.True(regular.Morale >= 50, $"regular {regular.Morale:F0}");
        Assert.Contains(PeopleEngine.Factors(star, team, 0.5, stats[star.Id], league.Finance, On), f => f.reason.StartsWith("Wants more minutes"));
        // Long enough that unhappy, an ambitious star asks out.
        Weeks(league, 6, stats);
        Assert.True(star.TradeRequested);
        Assert.True(Offseason.WontReSign(star));
    }

    [Fact]
    public void Winning_lifts_ambitious_players_and_losing_sinks_them()
    {
        var a = NewLeague(); var b = NewLeague();
        Weeks(a, 8, new(), winPct: 0.75);
        Weeks(b, 8, new(), winPct: 0.25);
        Assert.True(a.AllActivePlayers.Average(p => p.Morale) > b.AllActivePlayers.Average(p => p.Morale) + 8);
    }

    [Fact]
    public void A_settled_group_has_better_chemistry_than_new_faces_and_egos_clash()
    {
        var league = NewLeague();
        var (settled, fresh) = (league.Teams[1], league.Teams[2]);
        foreach (var p in settled.Roster) settled.WeeksTogether[p.Id] = 60;
        foreach (var p in fresh.Roster) fresh.WeeksTogether[p.Id] = 1;
        Assert.True(PeopleEngine.ChemistryFactors(settled).Sum(x => x.effect) > PeopleEngine.ChemistryFactors(fresh).Sum(x => x.effect) + 10);
        double before = PeopleEngine.ChemistryFactors(settled).Sum(x => x.effect);
        foreach (var p in settled.Roster.OrderByDescending(p => p.CurrentAbility).Take(4)) p.Hidden.Ego = 18;
        Assert.True(PeopleEngine.ChemistryFactors(settled).Sum(x => x.effect) < before - 6);
    }

    [Fact]
    public void Happy_connected_teams_play_better()
    {
        var league = NewLeague();
        var (a, b) = (league.Teams[0], league.Teams[1]);
        a.Tactic.Auto = b.Tactic.Auto = false;
        double Net(double morale, double chem)
        {
            foreach (var p in a.Roster) p.Morale = morale;
            a.Chemistry = chem;
            var sim = new GameSimulator(); double net = 0;
            for (int i = 0; i < 150; i++) { var r = sim.Simulate(a, b, league.Rules, (ulong)(500 + i), date: On); net += r.Home.Points - r.Away.Points; }
            return net / 150;
        }
        Assert.True(Net(95, 95) > Net(20, 20) + 1.5);
    }

    [Fact]
    public void Talks_depend_on_personality_and_promises_are_judged()
    {
        var league = NewLeague();
        var team = league.Teams[0];
        var rng = new Rng(1);
        var promises = new List<Promise>();
        var pro = team.Roster[3]; pro.Hidden.Professionalism = 17;
        var hothead = team.Roster[4]; hothead.Hidden.Professionalism = 5; hothead.Hidden.Temperament = 4;
        double proBefore = pro.Morale, hotBefore = hothead.Morale;
        Assert.True(Talks.Hold(pro, team, TalkTopic.Criticise, On, 0.5, null, league.Finance, promises, rng).Positive);
        Assert.False(Talks.Hold(hothead, team, TalkTopic.Criticise, On, 0.5, null, league.Finance, promises, rng).Positive);
        Assert.True(pro.Morale > proBefore && hothead.Morale < hotBefore - 5);
        Assert.NotNull(Talks.Unavailable(pro, On.AddDays(2))); // a week's cooldown
        Assert.Null(Talks.Unavailable(pro, On.AddDays(8)));

        // Promise minutes, then don't give them: broken, and he won't believe the next one.
        var bench = team.Roster.OrderBy(p => p.CurrentAbility).First();
        var st = Played(bench, team, 5, 4);
        var stats = new Dictionary<int, PlayerSeasonStats> { [bench.Id] = st };
        Talks.Hold(bench, team, TalkTopic.PromiseMinutes, On, 0.5, st, league.Finance, promises, rng);
        var promise = Assert.Single(promises);
        st.Games += 10; st.Minutes += 10 * 3; // he kept getting 3 minutes
        Weeks(league, 1, stats, promises: promises);
        Assert.Empty(promises);
        Assert.NotNull(bench.DistrustUntil);
        bench.LastTalk = null;
        Assert.False(Talks.Hold(bench, team, TalkTopic.PromiseMinutes, On.AddDays(14), 0.5, st, league.Finance, promises, rng).Positive);
    }

    [Fact]
    public void Morale_chemistry_and_promises_survive_save_and_load_and_the_squad_talks()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hw-people-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "career.hwsave");
            var s = GameSession.NewGame(2026, 3, path);
            var p = s.HumanTeam.Roster[5];
            p.Morale = 31; p.TradeRequested = true; p.UnhappyWeeks = 3;
            s.HumanTeam.Chemistry = 44;
            var r = s.Talk(p, TalkTopic.PromiseTrade);
            Assert.True(r.Positive);
            Assert.Single(s.Promises);
            var again = s.Talk(p, TalkTopic.AskWhatsWrong);
            Assert.False(again.Positive); // cooldown
            s.SaveNow();

            var loaded = GameSession.Load(path);
            var lp = loaded.HumanTeam.Roster.First(x => x.Id == p.Id);
            Assert.Equal(p.Morale, lp.Morale, 1);
            Assert.True(lp.TradeRequested);
            Assert.Equal(44, loaded.HumanTeam.Chemistry, 1);
            Assert.Equal("trade", Assert.Single(loaded.Promises).Kind);
            Assert.Equal(s.Today, lp.LastTalk);
            // A few weeks of games: weekly updates run and post squad news.
            for (int i = 0; i < 40; i++) loaded.AdvanceDay();
            Assert.Contains(loaded.Inbox, m => m.Category == "Squad");
            Assert.NotEmpty(loaded.MoodHistory);
            loaded.SaveNow();
            Assert.Equal(loaded.MoodHistory.Count, GameSession.Load(path).MoodHistory.Count);
        }
        finally { Directory.Delete(dir, true); }
    }
}
