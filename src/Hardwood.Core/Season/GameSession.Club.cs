using Hardwood.Core.Engine;
using Hardwood.Core.Jobs;
using Hardwood.Core.Model;
using Hardwood.Core.World;

namespace Hardwood.Core.Season;

/// <summary>One of your club's games in any competition (NABL, an overseas league or a continental one).</summary>
public sealed record MyGame(DateOnly Date, Team Opponent, bool Home, string Competition, string Stage, int? Us, int? Them, GameResult? Result)
{
    public bool Played => Us != null;
    public bool Won => Us > Them;
}

/// <summary>
/// Stage 3, manage any club: the parts of the session that work the same whether your club is in the NABL or in a
/// league abroad: finding clubs, your league's table, your games, your record and your players' stats.
/// The NABL still runs on its own schedule; a club abroad plays its league and continental fixtures from the world.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>Any club in the game: the NABL first, then the world.</summary>
    public Team? FindTeam(int id) => League.Teams.FirstOrDefault(t => t.Id == id) ?? World.TeamById(id);

    /// <summary>Your club plays in the NABL.</summary>
    public bool InNabl => League.Teams.Contains(HumanTeam);
    /// <summary>Your club plays in a league abroad.</summary>
    public bool Abroad => !InNabl;
    /// <summary>The overseas league your club plays in (null in the NABL).</summary>
    public WorldCompetition? HumanLeague => InNabl ? null : World.LeagueOf(HumanTeam);
    public string HumanLeagueName => HumanLeague?.Profile.Name ?? League.Name;

    /// <summary>Your club's level in the USA pyramid: 1 = NABL, 2-5 the tiers below; 0 for leagues abroad.</summary>
    public int MyLevel => InNabl ? 1 : HumanLeague is { } c ? UsaPyramid.Level(c.Key) : 0;
    /// <summary>The pyramid level of a league by key (1 = NABL, 0 = outside the USA).</summary>
    public static int LevelOf(string key) => key == "nabl" ? 1 : UsaPyramid.IsPyramid(key) ? UsaPyramid.Level(key) : 0;

    /// <summary>The league a club plays in, as the board sees it.</summary>
    public LeagueContext ContextOf(Team t) =>
        League.Teams.Contains(t) ? LeagueContext.Nabl(League) : World.LeagueOf(t) is { } c ? LeagueContext.Of(c) : LeagueContext.Nabl(League);

    /// <summary>Box scores of your games abroad this session (the world keeps scores and stat lines; these add the detail).</summary>
    public Dictionary<string, GameResult> AbroadResults { get; } = new();
    private static string Key(WorldCompetition c, Fixture f) => $"{c.Key}:{f.Index}";

    /// <summary>An overseas league's regular-season table built from its results.</summary>
    public Standings TableOf(WorldCompetition comp)
    {
        var st = new Standings(comp.Clubs);
        foreach (var f in comp.Season.Fixtures)
            if (f.Played && f.Stage == CompetitionSeason.RegularSeason && World.TeamById(f.HomeId) is { } h && World.TeamById(f.AwayId) is { } a && st.Has(h) && st.Has(a))
                st.Record(h, a, f.HomePts!.Value, f.AwayPts!.Value);
        return st;
    }

    /// <summary>Any club's regular-season record in its own league (NABL or abroad).</summary>
    public TeamRecord RecordOf(Team t) =>
        League.Teams.Contains(t) ? Standings[t] : World.LeagueOf(t) is { } c ? TableOf(c)[t] : new TeamRecord { Team = t };

    /// <summary>Your league's standings (the NABL's, or your overseas league's).</summary>
    public Standings MyStandings => HumanLeague is { } c ? TableOf(c) : Standings;
    public TeamRecord MyRecord => MyStandings.Has(HumanTeam) ? MyStandings[HumanTeam] : new TeamRecord { Team = HumanTeam };
    /// <summary>The table you're ranked in: your NABL conference, or your whole overseas league.</summary>
    public List<TeamRecord> MyTable => InNabl ? Standings.Conference(HumanTeam.Conference) : MyStandings.All();
    public string MyTableName => InNabl ? HumanTeam.Conference.ToString() : HumanLeagueName;
    public int MyRank => MyTable.FindIndex(r => r.Team == HumanTeam) + 1;

    /// <summary>Your club's games this season in every competition, in date order.</summary>
    public List<MyGame> MyGames()
    {
        var list = new List<MyGame>();
        var us = HumanTeam;
        if (InNabl)
        {
            foreach (var g in Schedule.Where(g => g.Home == us || g.Away == us))
            {
                Results.TryGetValue(g.Index, out var r);
                var (mine, theirs) = r == null ? (null as int?, null as int?) : r.Home.Team == us ? (r.Home.Points, r.Away.Points) : (r.Away.Points, r.Home.Points);
                list.Add(new MyGame(g.Date, g.Home == us ? g.Away : g.Home, g.Home == us, League.Name, "Regular season", mine, theirs, r));
            }
            if (Postseason != null)
                foreach (var se in Postseason.Series.Where(s => s.Higher == us || s.Lower == us))
                foreach (var r in se.Games)
                {
                    bool home = r.Home.Team == us;
                    list.Add(new MyGame(r.Date ?? Today, home ? r.Away.Team : r.Home.Team, home, League.Name, se.Round,
                        home ? r.Home.Points : r.Away.Points, home ? r.Away.Points : r.Home.Points, r));
                }
            return list.OrderBy(g => g.Date).ToList();
        }
        foreach (var (comp, f) in World.FixturesOf(us.Id))
        {
            bool home = f.HomeId == us.Id;
            var opp = World.TeamById(home ? f.AwayId : f.HomeId);
            if (opp == null) continue;
            AbroadResults.TryGetValue(Key(comp, f), out var r);
            list.Add(new MyGame(f.Date, opp, home, comp.Profile.Name, f.Stage,
                f.Played ? (home ? f.HomePts : f.AwayPts) : null, f.Played ? (home ? f.AwayPts : f.HomePts) : null, r));
        }
        return list;
    }

    /// <summary>Your next unplayed game (today's included).</summary>
    public MyGame? NextMyGame => MyGames().FirstOrDefault(g => !g.Played && g.Date >= Today);

    /// <summary>Your players' stats this season: NABL box scores, or every competition's lines abroad.</summary>
    public Dictionary<int, PlayerSeasonStats> MyStats
    {
        get
        {
            if (InNabl) return Stats;
            var stats = new Dictionary<int, PlayerSeasonStats>();
            var mine = HumanTeam.Roster.Select(p => p.Id).ToHashSet();
            foreach (var comp in World.Playing)
            foreach (var (id, l) in comp.Season.Stats)
            {
                if (!mine.Contains(id) || l.TeamId != HumanTeam.Id) continue;
                if (!stats.TryGetValue(id, out var s)) stats[id] = s = new PlayerSeasonStats { PlayerId = id, TeamId = l.TeamId, Season = League.Season };
                s.Games += l.G; s.Minutes += l.Min; s.Points += l.Pts; s.DReb += l.Reb; s.Assists += l.Ast; s.Steals += l.Stl; s.Blocks += l.Blk;
                s.FGM += l.Fgm; s.FGA += l.Fga; s.ThreePM += l.Tpm; s.ThreePA += l.Tpa;
            }
            return stats;
        }
    }

    /// <summary>Your club's season is over: no game left to play in any competition (abroad), or the NABL playoffs are done.</summary>
    public bool HumanSeasonOver =>
        InNabl || !Employed || World.FixturesOf(HumanTeam.Id).All(x => x.fixture.Played)
        && !World.Playing.Any(c => c.Season.Series.Any(s => !s.Complete && (s.HighId == HumanTeam.Id || s.LowId == HumanTeam.Id)));

    /// <summary>The first day of a career year: a week before the NABL opens, or before your overseas league starts if earlier.</summary>
    public DateOnly CareerYearStart()
    {
        var day = OpeningNight.AddDays(-7);
        if (World.LeagueOf(HumanTeam) is { } c)
        {
            var start = new DateOnly(League.Season, c.Profile.Start.Month, c.Profile.Start.Day).AddDays(-3);
            if (start < day) day = start;
        }
        return day;
    }

    private (WorldCompetition comp, Fixture fixture)? _liveWorld;

    /// <summary>Play or start today's game for a club abroad. Returns the live game when asked for one.</summary>
    private GameSimulator.LiveGame? BeginAbroadGame(bool live, List<GameResult> played)
    {
        _liveWorld = null;
        if (!Employed || InNabl || World.GameOn(HumanTeam.Id, Today) is not { } g) return null;
        var home = World.TeamById(g.fixture.HomeId)!; var away = World.TeamById(g.fixture.AwayId)!;
        if (live)
        {
            _liveWorld = g;
            return _sim.StartLive(home, away, g.comp.Profile.Rules, _rng.NextULong(), Today);
        }
        var r = _sim.Simulate(home, away, g.comp.Profile.Rules, _rng.NextULong(), keepLog: true, date: Today);
        RecordAbroad(g.comp, g.fixture, r);
        played.Add(r);
        return null;
    }

    private void RecordAbroad(WorldCompetition comp, Fixture f, GameResult r)
    {
        World.Record(comp, f, r);
        AbroadResults[Key(comp, f)] = r;
        if (AbroadResults.Count > 200) AbroadResults.Remove(AbroadResults.Keys.First());
        ReportGame(r);
    }

    /// <summary>Older saves paid staff abroad a sixth of NABL wages whatever the league; put them on the league's scale.</summary>
    private void FixStaffPay()
    {
        if (!Employed || !Abroad || HumanTeam.Staff.Count == 0) return;
        double limit = Budgets.StaffScale(MySalaryScale) * 1.5;
        if (HumanTeam.Staff.Any(st => st.Salary > Generation.StaffGenerator.SalaryFor(st) * limit + 1000)) Budgets.ScaleStaff(HumanTeam, MySalaryScale);
    }

    /// <summary>A club abroad without staff (the world doesn't give them any) gets a full team when you take over.</summary>
    private void EnsureStaff(Team t)
    {
        if (t.Staff.Count > 0) return;
        var gen = new Generation.StaffGenerator(new Rng(_rng.NextULong()));
        int quality = (int)Math.Round(Math.Clamp(_rng.NextGaussian(9, 2), 5, 14));
        foreach (var role in Enum.GetValues<StaffRole>())
        {
            var st = gen.Generate(League, role, Today, quality);
            st.TeamId = t.Id;
            t.Staff.Add(st);
        }
        Budgets.ScaleStaff(t, MySalaryScale);
    }

    /// <summary>Overseas clubs change coaches too; while you're out of work, their openings are yours to apply for.</summary>
    private void WorldCoachingChanges()
    {
        foreach (var comp in World.Competitions.Where(c => c.Profile.Kind == CompetitionKind.Domestic))
        {
            var table = TableOf(comp);
            foreach (var t in Board.AiSeasonChanges(LeagueContext.Of(comp), t => table[t].Wins, _rng, ControlledTeamId, League.Season))
                CoachLeaves(t, "parted ways with");
        }
    }
}
