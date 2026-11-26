using Hardwood.Core.Model;

namespace Hardwood.Core.Engine;

/// <summary>
/// Possession-by-possession stat-based simulation. No court geometry: each possession is resolved
/// from the ten players on the floor, their tactics, fatigue and a seeded RNG, and produces a full
/// box score plus a short play log.
/// </summary>
public sealed class GameSimulator
{
    /// <summary>H105 (run 113): a soft cap on each player's share of the five's shots, before the system tilt and fatigue
    /// (which can still move a share a few points either way, so it is not a hard ceiling). Above his cap
    /// (<paramref name="caps"/>), only <paramref name="keep"/> of the excess share stays (the real NBA's highest usage
    /// is about 35-37%). Without it, ten seasons of maxed-out stars took the scoring leader past 40 ppg.</summary>
    public static void SoftCapShare(double[] w, double[] caps, double keep)
    {
        // Water-fill: trimming the top player raises everyone else's share, so a second star can go over his own cap.
        // The trimmed players' shares are fixed together; the rest share what is left in their own proportions.
        var target = new double[w.Length];
        var trimmed = new bool[w.Length];
        for (int pass = 0; pass < w.Length; pass++)
        {
            double sum = w.Sum();
            if (sum <= 0) return;
            bool more = false;
            for (int i = 0; i < w.Length; i++)
                if (!trimmed[i] && w[i] / sum > caps[i])
                {
                    trimmed[i] = more = true;
                    target[i] = caps[i] + (w[i] / sum - caps[i]) * keep;
                }
            if (!more) return;
            double fixedShare = 0, rest = 0;
            for (int i = 0; i < w.Length; i++) { if (trimmed[i]) fixedShare += target[i]; else rest += w[i]; }
            if (rest <= 0 || fixedShare >= 0.95) return;
            for (int i = 0; i < w.Length; i++)
                if (trimmed[i]) w[i] = target[i] / (1 - fixedShare) * rest;
        }
    }

    /// <summary>H94 (run 109): how likely a defender is to be the one who blocks a shot near the basket: his Block rating
    /// (curved, so a 20 stands out), his position (QuickSim's C 2.6, PF 1.8, SF 0.9, guards 0.5) and his height.</summary>
    public static double BlockWeight(Player p) =>
        Math.Pow(Math.Max(1, p.Attributes[Attr.Block]) / 10.0, 2)
        * (p.PrimaryPosition switch { Position.C => 2.6, Position.PF => 1.8, Position.SF => 0.9, _ => 0.5 })
        * Math.Clamp(1 + (p.HeightCm - 203) / 15.0, 0.4, 2.5);


    /// <summary>How readily a player shoots before the system tilt and fatigue (his attributes and tendencies).</summary>
    private static double BaseUsage(Player p, bool transition = false)
    {
        var a = p.Attributes;
        double usage = a[Attr.ShotCreation] * 1.2 + a[Attr.ThreePoint] * 0.5 + a[Attr.CloseShot] * 0.5 + a[Attr.BallHandling] * 0.4
                       + (100 - p.Tendencies.PassFirst) * 0.08;
        if (transition) usage += a[Attr.Speed] * 0.5;
        return usage;
    }

    /// <summary>A player's shot weight before the system tilt and fatigue. Star usage sharpens or flattens the
    /// distribution; his role tilts it. H105 (run 112): the shot attributes top out at 20, so SGA (all 20s) shared shots evenly with good
    /// teammates while role players on thin teams led the league. A star of the league (CA above the floor) is the go-to
    /// man wherever he plays; it isn't relative to his teammates, so a thin team's best player gains nothing.</summary>
    internal static double ShotWeightOf(EngineConfig cfg, Role role, Player p, bool transition, double star) =>
        Math.Pow(Math.Max(1, BaseUsage(p, transition)), cfg.UsageSharpness * star)
        * Roles.Info(role).Usage
        * (1 + StarTermOf(cfg, p) * star);

    /// <summary>H105: how far above the league's star line a player is (0 below it, at most StarAbilityCap).</summary>
    internal static double StarTermOf(EngineConfig cfg, Player p) =>
        Math.Min(cfg.StarAbilityCap, Math.Max(0, p.CurrentAbility - cfg.StarAbilityFloor) / cfg.StarAbilityScale);

    private static readonly EngineConfig DefaultConfig = EngineConfig.Default;

    /// <summary>Run 116: the go-to man's ranking, the same shot weight the engine uses to pick whom a defence keys on
    /// (role, star term and the team's Star usage and system, in the half court). <see cref="CoachAi.TheirStar"/> ranks by it, so
    /// the scouting report, the stopper and the double team name the player who actually takes the shots.</summary>
    /// <param name="system">The system the team will run tonight, when it isn't the one set yet (an AI club picks its own in
    /// <see cref="CoachAi.Prepare"/>, which may run after the other bench's).</param>
    public static double GoToWeight(Player p, Tactic tac, OffensiveSystem? system = null, EngineConfig? cfg = null)
    {
        double star = tac.StarUsage / 50.0 * Tactics.SharpnessFactor(system ?? tac.System);
        return ShotWeightOf(cfg ?? DefaultConfig, tac.RoleOf(p), p, false, star);
    }

    private readonly EngineConfig _cfg;

    public GameSimulator(EngineConfig? cfg = null) => _cfg = cfg ?? EngineConfig.Default;

    public GameResult Simulate(Team home, Team away, Rules rules, ulong seed, bool keepLog = false, DateOnly? date = null)
    {
        CoachAi.Prepare(home, away, date, rules.OneAndOne); CoachAi.Prepare(away, home, date, rules.OneAndOne); CoachAi.Remember(home); CoachAi.Remember(away);
        var rng = new Rng(seed);
        var result = new GameResult
        {
            Home = new TeamBox { Team = home },
            Away = new TeamBox { Team = away },
            Seed = seed,
            Date = date,
            Neutral = rules.NeutralSite,
        };
        var cfg = _cfg.For(rules);
        var h = new Side(home, result.Home, rng, cfg, isHome: true, date);
        var a = new Side(away, result.Away, rng, cfg, isHome: false, date);
        var game = new GameState(h, a, rules, rng, cfg, result, keepLog);
        game.Run();

        if (date is { } d)
        {
            var injuries = new InjuryEngine(_cfg);
            foreach (var box in new[] { result.Home, result.Away })
            foreach (var line in box.Lines)
            {
                var inj = injuries.Roll(line.Player, line, d, rng, box.Team);
                if (inj != null) result.Injuries.Add((line.Player, inj));
            }
        }
        return result;
    }

    /// <summary>Begin a game that the caller steps through; call <see cref="LiveGame.Finish"/> or step to the end, then read Result.</summary>
    public LiveGame StartLive(Team home, Team away, Rules rules, ulong seed, DateOnly? date = null)
    {
        CoachAi.Prepare(home, away, date, rules.OneAndOne); CoachAi.Prepare(away, home, date, rules.OneAndOne); CoachAi.Remember(home); CoachAi.Remember(away);
        var rng = new Rng(seed);
        var result = new GameResult { Home = new TeamBox { Team = home }, Away = new TeamBox { Team = away }, Seed = seed, Date = date, Neutral = rules.NeutralSite };
        var cfg = _cfg.For(rules);
        var h = new Side(home, result.Home, rng, cfg, isHome: true, date);
        var a = new Side(away, result.Away, rng, cfg, isHome: false, date);
        var game = new GameState(h, a, rules, rng, cfg, result, log: true) { Interactive = true };
        return new LiveGame(game, result, rng, cfg, date);
    }

    /// <summary>A game in progress. Step one possession at a time; tactics and depth-chart changes on the Team objects take effect immediately.</summary>
    public sealed class LiveGame
    {
        private readonly GameState _game;
        private readonly Rng _rng;
        private readonly EngineConfig _cfg;
        private readonly DateOnly? _date;
        private bool _finalised;

        internal LiveGame(GameState game, GameResult result, Rng rng, EngineConfig cfg, DateOnly? date)
        {
            _game = game; Result = result; _rng = rng; _cfg = cfg; _date = date;
        }

        public GameResult Result { get; }
        public bool Complete => _game.Complete;
        public int Period => _game.Period;
        public double Clock => _game.Clock;
        public string ClockText => $"{(int)(_game.Clock / 60):0}:{(int)(_game.Clock % 60):00}";
        /// <summary>Q1-Q4 in the pros, H1-H2 in college (C2: college games showed "Q2" for the second half), then OT.</summary>
        public string PeriodText => _game.Period <= Result.Periods ? $"{(Result.Periods == 2 ? "H" : "Q")}{_game.Period}" : $"OT{_game.Period - Result.Periods}";
        public int HomeScore => _game.HomeScore;
        public int AwayScore => _game.AwayScore;
        public IReadOnlyList<Player> HomeOnCourt => _game.HomeOnCourt;
        public IReadOnlyList<Player> AwayOnCourt => _game.AwayOnCourt;
        public IReadOnlyList<string> Log => Result.PlayLog;
        /// <summary>Team fouls in the current period or half (B2a, run 140: the score bug). Reads state only.</summary>
        public int HomeTeamFouls => _game.HomeTeamFouls;
        public int AwayTeamFouls => _game.AwayTeamFouls;
        /// <summary>Free throws a common foul gives the side with the ball (0, 1 for a one-and-one, 2), from the
        /// defending side's team fouls under this game's rules. Reads state only.</summary>
        public int BonusFor(bool homeShoots) => _game.Rules.BonusShots(homeShoots ? _game.AwayTeamFouls : _game.HomeTeamFouls);
        /// <summary>True under college rules (a one-and-one before the double bonus).</summary>
        public bool OneAndOne => _game.Rules.OneAndOne;
        public int MinutesPerPeriod => _game.Rules.MinutesPerPeriod;
        /// <summary>B2b (run 141): a player's energy for the "On the floor" panels (0.3–1.0). Reads state only.</summary>
        public double EnergyOf(Player p) => _game.EnergyOf(p);
        /// <summary>Fouls that foul a player out, and the energy below which the engine looks to sub him. Read only.</summary>
        public int FoulsToFoulOut => _game.Rules.FoulsToFoulOut;
        public double SubOutEnergy => _cfg.SubOutEnergy;

        /// <summary>Play one possession. Returns false when the game is over (and finalises injuries).</summary>
        public bool Step()
        {
            bool more = _game.Step();
            if (!more) Finalise();
            return more;
        }

        public void Finish() { while (Step()) { } }

        /// <summary>#1064 (run 168): call a timeout at the next dead ball. False when none are left or the game is over.</summary>
        public bool RequestTimeout(Team team) => _game.RequestTimeout(team == Result.Home.Team);
        /// <summary>True while a requested timeout waits for the next dead ball. Reads state only.</summary>
        public bool TimeoutPending(Team team) => _game.TimeoutPending(team == Result.Home.Team);
        /// <summary>Timeouts a side has left right now. Reads state only.</summary>
        public int TimeoutsLeft(bool home) => _game.TimeoutsLeft(home);

        public void RequestLineup(Team team, IEnumerable<Player> five) => _game.RequestLineup(team == Result.Home.Team, five);
        /// <summary>True while a requested five waits for the next dead ball (H55, run 86). Reads state only.</summary>
        public bool LineupPending(Team team) => _game.LineupPending(team == Result.Home.Team);
        public void RefreshTargets(Team team) => _game.RefreshTargets(team == Result.Home.Team);
        /// <summary>
        /// The assistant's halftime read for <paramref name="team"/> (D75 A): the system and scheme that counter what the other
        /// bench is running, worked out exactly as on Continue. A pure read: watching live, applying it is the coach's call.
        /// </summary>
        public (OffensiveSystem system, DefensiveScheme scheme)? HalftimeRead(Team team) => _game.HalftimeRead(team == Result.Home.Team);

        private void Finalise()
        {
            if (_finalised) return;
            _finalised = true;
            if (_date is { } d)
            {
                var injuries = new InjuryEngine(_cfg);
                foreach (var box in new[] { Result.Home, Result.Away })
                foreach (var line in box.Lines)
                {
                    var inj = injuries.Roll(line.Player, line, d, _rng, box.Team);
                    if (inj != null) Result.Injuries.Add((line.Player, inj));
                }
            }
        }
    }

    // ------------------------------------------------------------------------------------------
    // Per-team live state
    // ------------------------------------------------------------------------------------------
    internal sealed class Side
    {
        public Team Team { get; }
        public TeamBox Box { get; }
        public bool IsHome { get; }
        public Player[] OnCourt { get; } = new Player[5];
        public List<Player> Rotation { get; } = new();   // ordered by quality
        public List<Player> Starters { get; } = new();
        /// <summary>Target playing time in seconds for a regulation game, by rotation slot.</summary>
        public Dictionary<Player, double> TargetSeconds { get; } = new();
        public Dictionary<Player, double> Energy { get; } = new();
        public Dictionary<Player, double> Form { get; } = new();
        /// <summary>K21 (run 163): this side's team-wide shooting night (0 outside March Madness).</summary>
        public double TeamForm { get; }
        /// <summary>Team chemistry's effect on shot quality (logit) and turnovers, fixed for the game.</summary>
        public double ChemLogit { get; }
        public double ChemTurnover { get; }
        /// <summary>Run 194: the rotation's full-court press fit, 0 to 1.</summary>
        public double PressFit { get; }
        public int TeamFouls { get; set; }
        public int Possessions { get; set; }
        /// <summary>Timeouts (#1064, run 168): how many are left, the other side's unanswered points, a drawn-up play waiting
        /// for this side's next possession, the last period this bench called one for the last play, and a coach's request.</summary>
        public int TimeoutsLeft { get; set; }
        public int RunAgainst { get; set; }
        public bool AfterTimeout { get; set; }
        public int LastPlayCallPeriod { get; set; }
        public double LastLateCallClock { get; set; } = double.MaxValue;
        public bool TimeoutRequested { get; set; }
        /// <summary>While the game clock (elapsed seconds) is below this, the coach's hand-picked five stays out there barring fouls.</summary>
        public double ManualHoldUntil { get; set; } = -1;
        /// <summary>The tactics in force this possession (the team's, or its late-game preset).</summary>
        public Tactic Tac { get; set; } = new();
        private Tactic? _clutch;
        /// <summary>The second-half tactics after a halftime adjustment (#268), or null to play the team's own.</summary>
        public Tactic? Adjusted;

        public Tactic ClutchTactic(TacticPreset preset)
        {
            var t = Adjusted ?? Team.Tactic;
            _clutch ??= new Tactic();
            _clutch.Roles = t.Roles; _clutch.StarStopperId = t.StarStopperId;
            preset.ApplyTo(_clutch);
            return _clutch;
        }

        public Side(Team team, TeamBox box, Rng rng, EngineConfig cfg, bool isHome, DateOnly? date)
        {
            Tac = team.Tactic;
            Team = team; Box = box; IsHome = isHome;
            // Injured players are not dressed. Below eight healthy bodies, the hurt ones play anyway (the sim must still run).
            var healthy = (date is { } d ? team.Roster.Where(p => p.IsAvailable(d)) : team.Roster).OrderByDescending(p => p.Overall).ToList();
            var available = healthy.Count >= 8 ? healthy : team.Roster.OrderByDescending(p => p.Overall).ToList();
            Dressed.AddRange(available);
            // K21 (run 163): the team's shooting night (March Madness only). No draw when it's off, so other games keep their stream.
            double night = cfg.TeamFormSd > 0 ? rng.NextGaussian(0, cfg.TeamFormSd) : 0;
            TeamForm = night;
            foreach (var p in available)
            {
                Box.Lines.Add(new PlayerLine { Player = p });
                Energy[p] = 1.0;
                double sd = cfg.FormSd * (21 - p.Hidden.Consistency) / 10.0;
                Form[p] = rng.NextGaussian(0, sd) + (p.Morale - People.PeopleEngine.Neutral) / 40.0 * cfg.MoraleLogit + night;
            }
            ChemLogit = (team.Chemistry - People.PeopleEngine.Neutral) / 40.0 * cfg.ChemistryLogit + (team.Coach?.ShotLogit ?? 0);
            ChemTurnover = -(team.Chemistry - People.PeopleEngine.Neutral) / 40.0 * cfg.ChemistryTurnover;
            // Run 194: how well the dressed top nine suit the full-court press (read only when this side presses).
            PressFit = Systems.PressFit(available.OrderByDescending(p => p.CurrentAbility).Take(9).ToList());
            // Players not dressed still get a (zero-minute) line so stats lookups never miss.
            foreach (var p in team.Roster.Except(available)) { Box.Lines.Add(new PlayerLine { Player = p }); Energy[p] = 1.0; Form[p] = 0; }

            var chart = team.DepthChart;
            var starters = new List<Player>();
            if (chart != null && chart.Starters.Count == 5)
                starters.AddRange(chart.Starters.Select(id => available.FirstOrDefault(p => p.Id == id)).Where(p => p != null)!);

            // Fill gaps (no chart, or chart names an injured player): greedy one-per-position by quality, then best remaining.
            foreach (var pos in Enum.GetValues<Position>())
            {
                if (starters.Count >= 5) break;
                if (starters.Any(p => p.PrimaryPosition == pos)) continue;
                var best = available.FirstOrDefault(p => !starters.Contains(p) && (p.PrimaryPosition == pos || p.SecondaryPosition == pos));
                if (best != null) starters.Add(best);
            }
            foreach (var p in available) { if (starters.Count >= 5) break; if (!starters.Contains(p)) starters.Add(p); }
            for (int i = 0; i < 5; i++) { OnCourt[i] = starters[i]; Box.LineFor(starters[i]).Started = true; }
            Starters.AddRange(starters);

            double scale = cfg.RegulationMinutes / 48.0;
            if (chart != null && chart.MinutesTargets.Count > 0)
            {
                // Human rotation: everyone with a minutes target is in the rotation, ordered by target.
                var planned = available.Where(p => chart.MinutesTargets.GetValueOrDefault(p.Id) > 0)
                    .OrderByDescending(p => chart.MinutesTargets[p.Id]).ToList();
                Rotation.AddRange(starters.OrderByDescending(p => chart.MinutesTargets.GetValueOrDefault(p.Id)));
                Rotation.AddRange(planned.Where(p => !starters.Contains(p)));
                // Minutes lost to injured rotation players are shared out so the targets still add up to 240.
                double total = Rotation.Sum(p => chart.MinutesTargets.GetValueOrDefault(p.Id));
                double fill = total < 1 ? 1 : 240 * scale / total;
                foreach (var p in Rotation) TargetSeconds[p] = chart.MinutesTargets.GetValueOrDefault(p.Id) * 60 * scale * fill;
                if (Rotation.Count < 8)
                    foreach (var p in available.Where(p => !Rotation.Contains(p)).Take(8 - Rotation.Count)) { Rotation.Add(p); TargetSeconds[p] = 8 * 60 * scale; }
            }
            else
            {
                // AI rotation: starters first, then the best of the rest; minutes by slot.
                Rotation.AddRange(starters.OrderByDescending(p => p.Overall));
                Rotation.AddRange(available.Where(p => !starters.Contains(p)).Take(Math.Max(cfg.RotationSize, 5) - 5));
                for (int i = 0; i < Rotation.Count; i++)
                {
                    double minutes = i < cfg.TargetMinutesBySlot.Length ? cfg.TargetMinutesBySlot[i] : 0;
                    TargetSeconds[Rotation[i]] = minutes * 60 * scale;
                }
            }
            foreach (var p in team.Roster) TargetSeconds.TryAdd(p, 0);
        }

        public PlayerLine Line(Player p) => Box.LineFor(p);
        /// <summary>Players in uniform tonight (healthy, or everyone if too many are hurt).</summary>
        public List<Player> Dressed { get; } = new();
    }

    // ------------------------------------------------------------------------------------------
    // Game state machine
    // ------------------------------------------------------------------------------------------
    internal sealed class GameState
    {
        private readonly Side _home, _away;
        private readonly Rules _rules;
        private readonly Rng _rng;
        private readonly EngineConfig _cfg;
        private readonly GameResult _result;
        private readonly bool _log;
        /// <summary>The commentary (#614): null when the game keeps no log, so a quick game builds no strings at all.</summary>
        private readonly PlayByPlay? _pbp;
        private readonly List<int> _homeByPeriod = new(), _awayByPeriod = new();
        private readonly TimeoutRules _to;
        /// <summary>K38 (run 208): the next media timeout mark of this half (an index into <see cref="TimeoutRules.MediaMarks"/>).</summary>
        private int _mediaNext;

        private int _period;
        private double _clock;        // seconds remaining in period
        private double _elapsed;      // seconds played so far in the game
        private Side _offense, _defense;
        private bool _transition;     // next possession starts in transition (after live-ball turnover)
        private bool _pressed;        // run 194: this possession comes against a full-court press (until its first shot)

        public GameState(Side home, Side away, Rules rules, Rng rng, EngineConfig cfg, GameResult result, bool log)
        {
            _home = home; _away = away; _rules = rules; _rng = rng; _cfg = cfg; _result = result; _log = log;
            _to = TimeoutRules.For(rules);
            foreach (var side in new[] { home, away }) { side.TimeoutsLeft = _to.AtTip; side.Box.Timeouts = _to.AtTip; }
            if (log) _pbp = new PlayByPlay(result, rules);
            _offense = home; _defense = away;
            result.Periods = rules.Periods; result.PeriodSeconds = rules.MinutesPerPeriod * 60; result.OvertimeSeconds = cfg.OvertimeMinutes * 60;
            if (log)
            {
                // The pregame line, as the quick sim sees it: strength gap plus home court, spread scaled to game length.
                double spread = rules.GameMinutes / 40.0;
                result.PregameEdge = (World.QuickSim.Strength(home.Team) - World.QuickSim.Strength(away.Team)) * 0.55 * spread + rules.HomePoints;
                result.MarginSpread = 11 * spread;
            }
        }

        private int Score(Side s) => s.Box.Points;

        public bool Complete { get; private set; }
        public int Period => _period;
        public double Clock => _clock;
        public int HomeScore => Score(_home);
        public int AwayScore => Score(_away);
        public IReadOnlyList<Player> HomeOnCourt => _home.OnCourt;
        public IReadOnlyList<Player> AwayOnCourt => _away.OnCourt;
        public int LogCount => _result.PlayLog.Count;
        public int HomeTeamFouls => _home.TeamFouls;
        public int AwayTeamFouls => _away.TeamFouls;
        public Rules Rules => _rules;
        /// <summary>A player's energy (0.3–1.0), 1 for anyone the game doesn't know. Reads state only.</summary>
        public double EnergyOf(Player p) =>
            _home.Energy.TryGetValue(p, out var e) ? e : _away.Energy.TryGetValue(p, out var f) ? f : 1.0;

        private bool _periodOpen;
        private int _periodHomeStart, _periodAwayStart;
        private Player[]? _homeRequest, _awayRequest;

        public void Run() { while (Step()) { } }

        /// <summary>Play one possession (opening a period or closing one when the clock runs out). Returns false once the game is over.</summary>
        public bool Step()
        {
            if (Complete) return false;
            if (!_periodOpen) OpenPeriod();

            ApplyLineupRequest(_home, ref _homeRequest);
            ApplyLineupRequest(_away, ref _awayRequest);
            Timeouts();

            RunPossession();
            (_offense, _defense) = (_defense, _offense);
            RecordFlow();

            if (_clock <= 0) ClosePeriod();
            return !Complete;
        }

        private Player? _lastScorer;
        private void RecordFlow()
        {
            _result.Elapsed = _elapsed; _result.CurrentPeriod = _period;
            if (!_log) return;
            var flow = _result.Flow;
            int h = Score(_home), a = Score(_away);
            if (flow.Count == 0 ? h + a > 0 : flow[^1].Home != h || flow[^1].Away != a)
                flow.Add(new FlowPoint(_elapsed, _period, h, a, _lastScorer));
        }

        private void OpenPeriod()
        {
            if (_period == 0) _period = 1;
            int seconds = _period <= _rules.Periods ? _rules.MinutesPerPeriod * 60 : _cfg.OvertimeMinutes * 60;
            if (_period > _rules.Periods) _result.Overtimes++;
            _clock = seconds;
            // K11: in college, team fouls carry from the second half into overtime.
            if (!(_period > _rules.Periods && _rules.FoulsCarryIntoOvertime)) { _home.TeamFouls = 0; _away.TeamFouls = 0; }
            _periodHomeStart = Score(_home); _periodAwayStart = Score(_away);
            _mediaNext = 0;
            foreach (var side in new[] { _home, _away })
            {
                side.TimeoutsLeft = _to.AtPeriodStart(_period, _rules.Periods, side.TimeoutsLeft);
                side.Box.Timeouts = side.TimeoutsLeft;
                side.TimeoutRequested &= side.TimeoutsLeft > 0;
                side.LastLateCallClock = double.MaxValue;
                side.RunAgainst = 0;   // review (run 168): a break ends a run, so no "10-0 run" timeout early in the third
            }

            // Alternate opening possession by period (jump ball simplified).
            if (_period % 2 == 1) { _offense = _home; _defense = _away; } else { _offense = _away; _defense = _home; }
            _transition = false;

            // Fresh legs at period start: substitute tired players at the break.
            CheckSubstitutions(_home, force: true);
            CheckSubstitutions(_away, force: true);
            _periodOpen = true;
        }

        private void ClosePeriod()
        {
            _homeByPeriod.Add(Score(_home) - _periodHomeStart);
            _awayByPeriod.Add(Score(_away) - _periodAwayStart);
            _home.Box.PointsByPeriod = _homeByPeriod.ToArray();
            _away.Box.PointsByPeriod = _awayByPeriod.ToArray();
            if (_log) _result.Plays.Add(new PlayEvent(_period, _clock, PlayKind.PeriodEnd, false, null, null, ShotZone.Rim, false, Score(_home), Score(_away)));
            _pbp?.PeriodEnd(_period, _clock, _home.Team.Abbreviation, _away.Team.Abbreviation, Score(_home), Score(_away),
                            Score(_home) - _periodHomeStart, Score(_away) - _periodAwayStart);
            _periodOpen = false;

            bool regulationDone = _period >= _rules.Periods;
            if (regulationDone && Score(_home) != Score(_away))
            {
                _home.Box.PointsByPeriod = _homeByPeriod.ToArray();
                _away.Box.PointsByPeriod = _awayByPeriod.ToArray();
                _home.Box.Possessions = _home.Possessions;
                _away.Box.Possessions = _away.Possessions;
                Complete = true;
                return;
            }
            if (_period * 2 == _rules.Periods) { HalftimeAdjust(_home, _away); HalftimeAdjust(_away, _home); }
            _period++;
        }

        /// <summary>A game the human is watching live: he makes his own halftime changes.</summary>
        public bool Interactive { get; set; }

        /// <summary>
        /// The halftime read (#268): a manual coach who asked for it gets the counter to what the other bench is really
        /// running, for the second half only. Auto coaches plan before the game and don't adjust.
        /// </summary>
        private void HalftimeAdjust(Side us, Side them)
        {
            var t = us.Team.Tactic;
            if (t.Auto || !t.HalftimeAdjust || Interactive) return;
            if (Read(us, them, _rules.OneAndOne) is not var (sys, sch)) return;
            if (sys == t.System && sch == t.Scheme) return;
            var adj = new Tactic { Auto = false, Roles = t.Roles, StarStopperId = t.StarStopperId, Presets = t.Presets, LateGamePreset = t.LateGamePreset };
            TacticPreset.From(t, "halftime").ApplyTo(adj);
            adj.System = sys; adj.Scheme = sch;
            us.Adjusted = adj;
            Log($"Halftime adjustment: {us.Team.Abbreviation} switch to {Systems.Name(sys).ToLower()} and {Systems.Name(sch).ToLower()} defense.");
        }

        /// <summary>
        /// The assistant's read of the other bench (#268): the counter to what they are really running, for the players
        /// still able to play (the best nine dressed, fouled-out players left out). Null with nobody to play. A pure read.
        /// </summary>
        private static (OffensiveSystem system, DefensiveScheme scheme)? Read(Side us, Side them, bool college)
        {
            var rot = us.Dressed.Where(p => !us.Line(p).FouledOut).OrderByDescending(p => p.CurrentAbility).Take(9).ToList();
            if (rot.Count == 0) return null;
            return CoachAi.Counter(rot, them.Team.Tactic.System, them.Team.Tactic.Scheme, college);
        }

        /// <summary>The halftime read for one side, the same one Continue applies (D75 A, run 69). Draws no random numbers.</summary>
        public (OffensiveSystem system, DefensiveScheme scheme)? HalftimeRead(bool home) =>
            home ? Read(_home, _away, _rules.OneAndOne) : Read(_away, _home, _rules.OneAndOne);

        /// <summary>#1064: the coach watching live asks for a timeout at the next dead ball. False with none left.</summary>
        public bool RequestTimeout(bool home)
        {
            var side = home ? _home : _away;
            if (Complete || side.TimeoutsLeft <= 0) return false;
            side.TimeoutRequested = true;
            return true;
        }

        public bool TimeoutPending(bool home) => (home ? _home : _away).TimeoutRequested;
        public int TimeoutsLeft(bool home) => (home ? _home : _away).TimeoutsLeft;

        /// <summary>
        /// #1064 (run 168): timeouts at the dead ball before a possession. A coach's request first; otherwise a bench calls
        /// one to stop a run of <see cref="TimeoutRules.RunToStop"/> unanswered points (keeping its late-game reserve until
        /// the last 4 minutes; in college, K34 (run 186), keeping one for the last minute of a one-possession game), or, with the ball in the last shot-clock of a close game it trails or is tied in, to set up
        /// the last play (once a period). At most one a dead ball. Rule-based: no random numbers are drawn.
        /// </summary>
        private void Timeouts()
        {
            if (_transition) return;   // a live ball after a steal: no dead ball to call it in
            var off = _offense; var def = _defense;
            foreach (var side in new[] { off, def })
                if (side.TimeoutRequested)
                {
                    side.TimeoutRequested = false;
                    if (side.TimeoutsLeft > 0) { CallTimeout(side, TimeoutReason.Coach, 0); return; }
                }
            bool lateWindow = _period > _rules.Periods || (_period == _rules.Periods && _clock <= 240);
            int full = _period <= _rules.Periods ? _rules.MinutesPerPeriod * 60 : _cfg.OvertimeMinutes * 60;
            if (_clock >= full) return;   // a period just opened: the break was the timeout
            int diff = Score(off) - Score(def);
            // K38 (run 208), college only: the media timeout at the first dead ball under 16, 12, 8 and 4 minutes of each half
            // (a bench's own timeout called under a mark counts as that mark's, as in the real game).
            if (_to.HasMediaTimeouts && _period <= _rules.Periods && _mediaNext < TimeoutRules.MediaMarks.Length
                && _clock < TimeoutRules.MediaMarks[_mediaNext])
            {
                MediaTimeout();
                return;
            }
            // K38: college garbage time. The last two minutes of a 15-point game: neither bench calls one (a coach's own
            // request above still counts).
            if (_rules.Periods == 2 && _period >= _rules.Periods && _clock <= 120 && Math.Abs(diff) >= TimeoutRules.GarbageMargin) return;
            if (_period >= _rules.Periods && _clock <= _rules.ShotClockSeconds + 2 && _clock > 1 && diff is >= -3 and <= 0
                && off.TimeoutsLeft > 0 && off.LastPlayCallPeriod != _period)
            {
                off.LastPlayCallPeriod = _period;
                CallTimeout(off, TimeoutReason.LastPlay, 0);
                return;
            }
            // K34 (run 186): college, the last 4:00 of a one-possession game. A tired five's timeout is about the next play or
            // the clock, not the legs, and the bench keeps one for the last minute.
            bool closeLate = _rules.Periods == 2 && lateWindow && _clock <= 240 && Math.Abs(diff) <= 3;
            int keepForLastMinute = closeLate && _clock > 60 ? 1 : 0;
            // The last two minutes: a side behind by one to six with the ball draws up a play (at most one every 30 s of clock).
            if (_period >= _rules.Periods && _clock <= 120 && diff is >= -6 and <= -1 && off.TimeoutsLeft > keepForLastMinute && off.LastLateCallClock - _clock >= 30)
            {
                off.LastLateCallClock = _clock;
                CallTimeout(off, TimeoutReason.LatePlay, 0);
                return;
            }
            foreach (var side in new[] { off, def })
            {
                int spare = side.TimeoutsLeft - (lateWindow ? keepForLastMinute : _to.Reserve);
                if (side.RunAgainst >= TimeoutRules.RunToStop && spare > 0)
                {
                    CallTimeout(side, TimeoutReason.StopRun, side.RunAgainst);
                    return;
                }
            }
            // A breather: the five on the floor are worn down (below the sub-out line on average) and the bench can spare one.
            foreach (var side in new[] { off, def })
            {
                int spare = side.TimeoutsLeft - (lateWindow ? keepForLastMinute : _to.Reserve);
                if (spare > 0 && side.OnCourt.Average(p => side.Energy[p]) < _cfg.SubOutEnergy + TimeoutRules.BreatherMargin)
                {
                    // K28 (run 177): in the last two minutes a timeout is about the clock and the next play, not the legs
                    // (only the words change; when and whether it is called is as before).
                    var why = TimeoutReason.Breather;
                    if ((_period >= _rules.Periods && _clock <= 120) || closeLate)
                        why = side == off ? TimeoutReason.LatePlay : Score(side) < Score(off) ? TimeoutReason.StopClock : TimeoutReason.SetDefense;
                    CallTimeout(side, why, 0);
                    return;
                }
            }
        }

        /// <summary>Everyone on the floor sits for a stoppage's length (the bench's recovery rate); the clock doesn't move.</summary>
        private void Rest(double seconds)
        {
            double possFraction = seconds / 14.0;
            foreach (var s in new[] { _home, _away })
            foreach (var p in s.OnCourt)
            {
                double rec = _cfg.EnergyRecoverPerPossession * possFraction * (0.7 + p.Attributes[Attr.NaturalFitness] / 20.0);
                s.Energy[p] = Math.Min(1.0, s.Energy[p] + rec);
            }
        }

        /// <summary>K38 (run 208): a college media timeout. Nobody's timeout is used and no run is stopped (a run goes on
        /// after the break); the players rest. No random numbers are drawn.</summary>
        private void MediaTimeout()
        {
            int mark = TimeoutRules.MediaMarks[_mediaNext];
            while (_mediaNext < TimeoutRules.MediaMarks.Length && _clock < TimeoutRules.MediaMarks[_mediaNext]) _mediaNext++;
            Rest(TimeoutRules.MediaSeconds);
            _result.MediaTimeouts.Add(new MediaTimeoutCall(_period, _clock, mark, Score(_home), Score(_away)));
            if (_pbp != null) Log($"Media timeout (under {mark / 60}:00)");
        }

        private void CallTimeout(Side side, TimeoutReason reason, int run)
        {
            side.TimeoutsLeft--; side.Box.Timeouts = side.TimeoutsLeft; side.Box.TimeoutsUsed++;
            side.RunAgainst = 0; side.AfterTimeout = true;
            Rest(_to.Seconds);
            // K38: a bench's timeout under a media mark is that mark's media timeout.
            if (_to.HasMediaTimeouts && _period <= _rules.Periods)
                while (_mediaNext < TimeoutRules.MediaMarks.Length && _clock < TimeoutRules.MediaMarks[_mediaNext]) _mediaNext++;
            var call = new TimeoutCall(_period, _clock, side.IsHome, reason, run, side.TimeoutsLeft, Score(_home), Score(_away));
            _result.Timeouts.Add(call);
            if (_pbp != null) Log($"Timeout, {side.Team.Abbreviation} ({call.Why}; {call.LeftText})");   // a quick game builds no strings (#614)
        }

        /// <summary>Ask for a specific five at the next dead ball. Players who are hurt, fouled out or not on the roster are ignored.</summary>
        public void RequestLineup(bool home, IEnumerable<Player> five)
        {
            var side = home ? _home : _away;
            var chosen = five.Where(p => side.Dressed.Contains(p) && !side.Line(p).FouledOut).Distinct().Take(5).ToArray();
            if (home) _homeRequest = chosen; else _awayRequest = chosen;
        }

        public bool LineupPending(bool home) => (home ? _homeRequest : _awayRequest) is { Length: > 0 };

        /// <summary>Re-read the team's depth chart for minutes targets (tactics are read live every possession).</summary>
        public void RefreshTargets(bool home)
        {
            var side = home ? _home : _away;
            var chart = side.Team.DepthChart;
            if (chart == null || chart.MinutesTargets.Count == 0) return;
            double scale = _cfg.RegulationMinutes / 48.0;
            foreach (var p in side.Dressed) side.TargetSeconds[p] = chart.MinutesTargets.GetValueOrDefault(p.Id) * 60 * scale;
            side.Rotation.Clear();
            side.Rotation.AddRange(side.Dressed.Where(p => side.TargetSeconds[p] > 0).OrderByDescending(p => side.TargetSeconds[p]));
            if (side.Rotation.Count < 8) side.Rotation.AddRange(side.Dressed.Where(p => !side.Rotation.Contains(p)).Take(8 - side.Rotation.Count));
        }

        private void ApplyLineupRequest(Side side, ref Player[]? request)
        {
            if (request == null) return;
            var want = request;
            request = null;
            if (want.Length == 0) return;
            // Put the requested players in; fill any remaining spots with whoever was already out there.
            var current = side.OnCourt.ToList();
            var next = new List<Player>(want);
            foreach (var p in current) { if (next.Count >= 5) break; if (!next.Contains(p)) next.Add(p); }
            for (int i = 0; i < 5 && i < next.Count; i++) side.OnCourt[i] = next[i];
            side.ManualHoldUntil = _elapsed + _cfg.ManualLineupHoldSeconds;
            Log($"Lineup change: {string.Join(", ", want.Select(p => p.LastName))}");
        }

        // --------------------------------------------------------------------------------------
        private void RunPossession()
        {
            var off = _offense; var def = _defense;
            off.Possessions++;
            SetTactics(off); SetTactics(def);
            _offStar = Star(off);
            _fitLogit = FitFor(off, def) * (off.Team.Coach?.FitMultiplier ?? 1);
            // #1064: the play drawn up in a timeout this side called.
            if (off.AfterTimeout) { _fitLogit += TimeoutRules.AfterTimeoutLogit; off.AfterTimeout = false; }
            // Fourth-quarter game flow: a team well ahead eases off (rests starters, runs clock, loses focus) and the
            // team behind presses; leads shrink. Without it overtime came in ~3% of games, half the NBA's rate (critic review #2).
            // The last quarter of the game's length: a whole fourth quarter, but only the last 10 minutes of a college
            // second half (C4 step 0, #187: it ran for all 20, which squeezed every college margin).
            if (_period > _rules.Periods || (_period == _rules.Periods && _clock <= _rules.GameMinutes * 60 / 4.0))
            {
                int lead = Score(off) - Score(def);
                if (lead <= -6) _fitLogit += _cfg.CatchUpLogit;
                else if (lead >= 6) _fitLogit -= _cfg.CatchUpLogit * 0.7;
            }

            double pace = (off.Tac.Pace + def.Tac.Pace) / 2.0 + Tactics.PaceShift(off.Tac.System)
                          + (def.Tac.FullCourtPress ? Tactics.PressPace : 0); // run 194: a pressing defence speeds the game up
            // Pace is possessions per 48 minutes (Tactic.Pace), so a 40-minute FIBA game has 5/6 as many possessions.
            double meanSeconds = 48 * 60.0 / (2.0 * pace * _rules.PaceScale);
            double seconds = _transition
                ? Math.Clamp(_rng.NextGaussian(_cfg.TransitionSeconds, 2.0), 2, 12)
                : Math.Clamp(_rng.NextGaussian(meanSeconds, _cfg.SecondsPerPossessionSd), 3, _rules.ShotClockSeconds);
            bool wasTransition = _transition;
            _transition = false;

            // End of the fourth quarter (and overtime): game management. A team behind by 1-6 in the last 40 seconds
            // fouls straight away to stop the clock; otherwise the team ahead runs the clock. A team that is tied or
            // behind by one to three holds for the last shot so the other side can't answer, and down three it needs a
            // three. Without this, close games rarely ended tied and overtime was a third as common as in the NBA.
            _needThree = false;
            _wantTwo = false;
            bool intentionalFoul = false;
            // K28 (run 177): college benches foul earlier and further back (LateGame). The pros keep the 40-second window.
            bool finalPeriod = _period >= _rules.Periods;
            var lateFoul = wasTransition ? LateGame.Foul.None
                : LateGame.ShouldFoul(_rules.OneAndOne, finalPeriod, _clock, Score(off) - Score(def), _rules.BonusShots(def.TeamFouls + 1) == 1);
            if (finalPeriod && !wasTransition && _rules.OneAndOne && _clock <= LateGame.FoulWindowSeconds && _clock > 2
                && Score(off) - Score(def) is >= LateGame.FoulMin and <= LateGame.FoulMax)
                _result.LateFoulChances++;
            if (lateFoul != LateGame.Foul.None)
            {
                intentionalFoul = true;
                seconds = Math.Min(_clock - 1, 2 + _rng.NextDouble() * 3);
                _result.LateFouls.Add(new LateFoulCall(_period, _clock, !off.IsHome, Score(off) - Score(def), lateFoul));
            }
            else if (_period >= _rules.Periods && _clock <= _rules.ShotClockSeconds + 2 && !wasTransition)
            {
                int diff = Score(off) - Score(def);
                if (diff is >= 1 and <= 6)
                    seconds = Math.Min(_clock, _rules.ShotClockSeconds - _rng.NextDouble() * 3);
                else if (diff is >= -3 and <= 0)
                {
                    seconds = Math.Clamp(_clock - _rng.NextDouble() * 2.5, Math.Min(_clock, 1), _rules.ShotClockSeconds);
                    _needThree = diff == -3;
                    _wantTwo = diff == -2 && _rng.Chance(0.75); // down two: usually play for the tie
                }
            }

            // K28: college, the last minute, down four to ten with the ball: shoot early, and look for a three when it's needed.
            if (!intentionalFoul && !wasTransition)
            {
                var (hurried, three) = LateGame.Hurry(_rules.OneAndOne, finalPeriod, _clock, Score(off) - Score(def), seconds);
                seconds = hurried;
                _needThree |= three;
            }

            // Last possession of a period: use whatever time is left.
            seconds = Math.Min(seconds, _clock);
            _clock -= seconds;
            _elapsed += seconds;

            DrainEnergy(off, seconds); DrainEnergy(def, seconds);
            int pointsBefore = Score(off);

            // Run 194: the full-court press, whenever the offence doesn't come in transition (after a basket, a dead ball, and the
            // 82% of defensive rebounds that aren't run out; not on a late foul). It draws no
            // extra random numbers, so a game without a press plays exactly as before.
            _pressed = def.Tac.FullCourtPress && !wasTransition && !intentionalFoul;
            if (_pressed) { _result.PressedPossessions[off.IsHome ? 0 : 1]++; DrainEnergy(def, seconds * (Tactics.PressEnergyDrain - 1), clockless: true); }

            bool possessionLive = true;
            int chanceIndex = 0; // 0 = first shot, 1+ = after offensive rebound
            if (intentionalFoul)
            {
                // Foul the best free-throw shooter's teammate if possible: in practice, whoever has the ball. Two shots.
                var fouler = PickDefender(def, null, ShotZone.ShortMid);
                CommitFoul(def, fouler);
                // K11: under college rules a foul short of the bonus only gives the ball back, so the trailing side keeps
                // fouling (a second or so each time, credited to the players on court) until the shooter goes to the line.
                // If the clock runs out first, nobody shoots.
                bool ranOut = false;
                while (_rules.OneAndOne && def.TeamFouls < _rules.TeamFoulBonusThreshold && _clock > 0)
                {
                    Play(PlayKind.Foul, def, fouler);
                    BurnClock(0.5 + _rng.NextDouble());
                    if (_clock <= 0) { ranOut = true; break; }
                    fouler = PickDefender(def, null, ShotZone.ShortMid);
                    CommitFoul(def, fouler);
                }
                if (!ranOut)
                {
                    var target = off.OnCourt[_rng.Next(5)];
                    Play(PlayKind.Foul, def, fouler, target);
                    ShootBonus(off, def, target);
                    if (_pbp != null)
                    {
                        // K25's leftover (run 186): two players who share a surname are told apart by a first initial.
                        bool twins = fouler.LastName == target.LastName;
                        string a = twins ? Initialled(fouler) : fouler.LastName, b = twins ? Initialled(target) : target.LastName;
                        Log(lateFoul == LateGame.Foul.OneAndOne
                            ? $"{a} fouls {b}, sending him to the one-and-one"
                            : $"{a} fouls {b} to stop the clock");
                    }
                }
                possessionLive = false;
            }
            while (possessionLive)
            {
                // 1. Turnover
                if (chanceIndex == 0 && TryTurnover(off, def)) { possessionLive = false; break; }

                // 2. Non-shooting foul (resets the shot clock; bonus free throws if in penalty)
                if (_rng.Chance(_cfg.NonShootingFoulRate + (_pressed ? Tactics.PressFoulRate : 0)))
                {
                    var fouler = PickDefender(def, null, ShotZone.ShortMid);
                    CommitFoul(def, fouler);
                    Play(PlayKind.Foul, def, fouler);
                    if (def.TeamFouls >= _rules.TeamFoulBonusThreshold)
                    {
                        var shooter = PickShooter(off, wasTransition);
                        ShootBonus(off, def, shooter);
                        possessionLive = false; break;
                    }
                    // Otherwise play on: a little more clock burns.
                    BurnClock(Math.Clamp(_rng.NextGaussian(8, 3), 1, 14));
                    _pressed = false; // run 194's review: the inbound after a reach-in is in the front court
                    continue;
                }

                // 3. Shot
                var shooter2 = PickShooter(off, wasTransition);
                var zone = PickZone(off, shooter2, wasTransition);
                var defender = PickDefender(def, shooter2, zone);
                possessionLive = ResolveShot(off, def, shooter2, defender, zone, wasTransition, ref chanceIndex);
                wasTransition = false;
                _pressed = false; // only the first shot comes against a broken press
            }

            // Plus/minus for everyone on court
            int delta = Score(off) - pointsBefore;
            if (delta > 0) { def.RunAgainst += delta; off.RunAgainst = 0; }
            if (delta != 0)
            {
                foreach (var p in off.OnCourt) off.Line(p).PlusMinus += delta;
                foreach (var p in def.OnCourt) def.Line(p).PlusMinus -= delta;
            }

            // Dead ball after a possession: substitution window
            CheckSubstitutions(off, false);
            CheckSubstitutions(def, false);
        }

        // --------------------------------------------------------------------------------------
        private bool TryTurnover(Side off, Side def)
        {
            double security = off.OnCourt.Average(p => (p.Attributes[Attr.BallHandling] + p.Attributes[Attr.Decisions]) / 2.0);
            double pressure = def.OnCourt.Average(p => (p.Attributes[Attr.Steal] + p.Attributes[Attr.PerimeterDefense]) / 2.0)
                              + (def.Tac.DefensiveAggression - 50) / 25.0;
            double p = _cfg.TurnoverBase - _cfg.TurnoverSkillBeta * (security - pressure)
                       + Tactics.TurnoverShift(off.Tac, def.Tac) + off.ChemTurnover;
            // Run 194: the press. How much it forces depends on how well the pressers suit it (speed, stamina, hands).
            if (_pressed) p += Tactics.PressTurnover + Tactics.PressFitTurnover * (def.PressFit - 0.5);
            p = Math.Clamp(p, 0.04, 0.25 + _rules.TurnoverShift + (_pressed ? Tactics.PressTurnover : 0)); // K11: the college profile lifts the ceiling with the base
            if (!_rng.Chance(p)) return false;

            // Who turned it over: weighted by usage (handlers more often)
            var weights = off.OnCourt.Select(pl => 1.0 + pl.Attributes[Attr.BallHandling] * 0.3 + pl.Attributes[Attr.ShotCreation] * 0.2).ToArray();
            var culprit = off.OnCourt[_rng.WeightedIndex(weights)];
            off.Line(culprit).Turnovers++;

            if (_pressed) _result.PressTurnovers[off.IsHome ? 0 : 1]++;
            if (_rng.Chance(_pressed ? Tactics.PressLiveBallShare : _cfg.LiveBallTurnoverShare))
            {
                var stealWeights = def.OnCourt.Select(pl => Math.Pow(pl.Attributes[Attr.Steal], 2) * (1 + pl.Tendencies.GambleSteals / 100.0)).ToArray();
                var thief = def.OnCourt[_rng.WeightedIndex(stealWeights)];
                def.Line(thief).Steals++;
                _transition = true;
                Log(_pressed ? $"{thief.LastName} picks it off in the press from {culprit.LastName}" : $"{thief.LastName} steals from {culprit.LastName}");
                Play(PlayKind.Steal, def, thief, culprit);
            }
            else { Log($"Turnover by {culprit.LastName}"); Play(PlayKind.Turnover, off, culprit); }
            return true;
        }

        private Player PickShooter(Side off, bool transition)
        {
            double star = off.Tac.StarUsage / 50.0 * Tactics.SharpnessFactor(off.Tac.System); // 1.0 = neutral
            var weights = new double[5];
            var caps = new double[5];
            for (int i = 0; i < 5; i++)
            {
                var p = off.OnCourt[i];
                weights[i] = ShotWeight(off, p, transition, star);
                // A star of the league may take a bigger share than the best player of a thin team.
                caps[i] = (_cfg.MaxShotShare + _cfg.StarShareBonus * StarTerm(p)) * (0.75 + 0.25 * star);  // the Star usage slider moves the cap too
                // The double team keys on the go-to man and comes before the cap: a doubled role-level scorer loses
                // shots, a capped star only a few (doubling him works mainly through the contest and the open looks).
                if (p == _offStar && _defense.Tac.DoubleStar) weights[i] *= _cfg.DoubleStarUsage;
            }
            SoftCapShare(weights, caps, _cfg.ShotShareOverCap);
            // The system tilt and fatigue come after the cap, so Pace-and-space still feeds its shooters and Post its bigs.
            for (int i = 0; i < 5; i++)
            {
                var p = off.OnCourt[i];
                weights[i] *= Tactics.SystemUsage(off.Tac.System, p) * (0.75 + 0.25 * off.Energy[p]);
            }
            return off.OnCourt[_rng.WeightedIndex(weights)];
        }

        /// <summary>The scorer the other team keys on. H105 (run 112): the go-to man is whoever the shot weights favour (role and the star term included, before the system tilt),
        /// so the double team goes to the player who actually takes the shots.</summary>
        private Player Star(Side s)
        {
            double star = s.Tac.StarUsage / 50.0 * Tactics.SharpnessFactor(s.Tac.System);
            return s.OnCourt.MaxBy(p => ShotWeight(s, p, false, star))!;
        }

        /// <summary>A player's shot weight before the system tilt and fatigue. Star usage sharpens or flattens the
        /// distribution; his role tilts it. H105 (run 112): the shot attributes top out at 20, so SGA (all 20s) shared shots evenly with good
        /// teammates while role players on thin teams led the league. A star of the league (CA above the floor) is the go-to
        /// man wherever he plays; it isn't relative to his teammates, so a thin team's best player gains nothing.</summary>
        private double ShotWeight(Side off, Player p, bool transition, double star) =>
            ShotWeightOf(_cfg, off.Tac.RoleOf(p), p, transition, star);

        /// <summary>H105: how far above the league's star line a player is (0 below it, at most StarAbilityCap).</summary>
        private double StarTerm(Player p) => StarTermOf(_cfg, p);
        private Player? _offStar;
        private readonly Dictionary<(long, long, int), double> _fitCache = new();

        /// <summary>Tactics fit for these two fives and their tactics, cached (lineups repeat all game).</summary>
        private double FitFor(Side off, Side def)
        {
            static long Key(Player[] five) { long k = 0; foreach (var p in five) k += (long)p.Id * 2654435761L; return k; }
            int tac = (int)off.Tac.System * 16 + (int)def.Tac.Scheme;
            var key = (Key(off.OnCourt), Key(def.OnCourt), tac);
            if (!_fitCache.TryGetValue(key, out var v)) _fitCache[key] = v = Tactics.FitLogit(off.Tac, def.Tac, off.OnCourt, def.OnCourt, _cfg, _rules.OneAndOne);
            return v;
        }
        private double _fitLogit;

        private void SetTactics(Side s)
        {
            var t = s.Adjusted ?? s.Team.Tactic;
            if (t.LateGamePreset is { } name && _period >= _rules.Periods && _clock <= 300 && Math.Abs(HomeScore - AwayScore) <= 6
                && t.Presets.FirstOrDefault(p => p.Name == name) is { } preset)
                s.Tac = s.ClutchTactic(preset);
            else s.Tac = t;
        }

        /// <summary>Set for a possession that must end in a three (down three at the end of the game).</summary>
        private bool _needThree;
        /// <summary>Set for a possession going for a tying two (down two at the end of the game).</summary>
        private bool _wantTwo;

        private ShotZone PickZone(Side off, Player shooter, bool transition)
        {
            if (_needThree) return _rng.Chance(0.35) ? ShotZone.CornerThree : ShotZone.AboveBreakThree;
            if (_wantTwo) return _rng.Chance(0.6) ? ShotZone.Rim : ShotZone.ShortMid;
            var t = shooter.Tendencies;
            var baseW = _cfg.ZoneBaseWeight;
            double threeEmphasis = off.Tac.ThreePointEmphasis / 50.0;
            var w = new double[5];
            double rate = t.ThreePointRate <= _cfg.ThreeRateKnee ? t.ThreePointRate
                : _cfg.ThreeRateKnee + (t.ThreePointRate - _cfg.ThreeRateKnee) * _cfg.ThreeRateAboveKnee;
            double threeFactor = (rate / 35.0) * threeEmphasis;
            if (_cfg.ThreeGameKnee > 0 && off.Line(shooter).ThreePA is var tpa && tpa >= _cfg.ThreeGameKnee)
                threeFactor *= Math.Pow(1 - _cfg.ThreeGameDecay, tpa - _cfg.ThreeGameKnee + 1);
            double driveFactor = (t.DriveRate + t.PostUpRate) / 40.0;
            double midFactor = 1.0 + t.IsolationRate / 40.0 - threeFactor * 0.25;

            w[(int)ShotZone.Rim] = baseW[0] * driveFactor * (transition ? 1.6 : 1.0) * (_pressed ? Tactics.PressBrokenRimWeight : 1.0);
            w[(int)ShotZone.ShortMid] = baseW[1] * Math.Max(0.3, midFactor);
            w[(int)ShotZone.LongMid] = baseW[2] * Math.Max(0.3, midFactor);
            w[(int)ShotZone.CornerThree] = baseW[3] * threeFactor * (transition ? 1.2 : 1.0);
            w[(int)ShotZone.AboveBreakThree] = baseW[4] * threeFactor * (transition ? 1.3 : 1.0);
            var roleMix = Roles.Info(off.Tac.RoleOf(shooter)).ZoneMix;
            var sysMix = Tactics.SystemZoneMix(off.Tac.System);
            var defMix = Tactics.SchemeZoneMix(_defense.Tac.Scheme);
            for (int i = 0; i < 5; i++) w[i] *= roleMix[i] * sysMix[i] * defMix[i];
            return (ShotZone)_rng.WeightedIndex(w);
        }

        private Player PickDefender(Side def, Player? shooter, ShotZone zone)
        {
            // The star stopper takes the other team's best scorer.
            if (shooter != null && shooter == _offStar && def.Tac.StarStopperId is { } sid && def.OnCourt.FirstOrDefault(p => p.Id == sid) is { } stopper && _rng.Chance(0.85))
                return stopper;
            // Nearest-position defender most of the time, otherwise anyone (help, switches; switching teams more often).
            if (shooter != null && _rng.Chance(def.Tac.Scheme == DefensiveScheme.SwitchEverything ? 0.6 : 0.7))
            {
                var same = def.OnCourt.Where(p => p.PrimaryPosition == shooter.PrimaryPosition || p.SecondaryPosition == shooter.PrimaryPosition).ToList();
                if (same.Count > 0) return _rng.Pick(same);
            }
            if (zone == ShotZone.Rim)
            {
                var w = def.OnCourt.Select(p => (double)p.Attributes[Attr.InteriorDefense] + p.Attributes[Attr.Block]).ToArray();
                return def.OnCourt[_rng.WeightedIndex(w)];
            }
            return def.OnCourt[_rng.Next(5)];
        }

        private double Contest(Side def, Player defender, ShotZone zone)
        {
            var a = defender.Attributes;
            double individual = zone == ShotZone.Rim
                ? (a[Attr.InteriorDefense] * 0.6 + a[Attr.Block] * 0.4)
                : (a[Attr.PerimeterDefense] * 0.7 + a[Attr.Agility] * 0.3);
            double help = def.OnCourt.Average(p => (double)p.Attributes[Attr.HelpDefense]);
            double length = (defender.WingspanCm - 210) / 10.0; // ±1 per 10 cm
            double c = individual * 0.7 + help * 0.3 + length * 0.4;
            // Roles: rim protectors on the floor protect the rim; 3-and-D wings and glue guys contest their man harder.
            if (zone == ShotZone.Rim) c += Math.Min(1.5, def.OnCourt.Sum(p => Roles.Info(def.Tac.RoleOf(p)).RimDefense));
            else c += Roles.Info(def.Tac.RoleOf(defender)).PerimeterDefense;
            c += Tactics.SchemeContest(def.Tac.Scheme, zone);
            if (_pressed && zone == ShotZone.Rim) c -= Tactics.PressBrokenRimContest; // run 194: beat the press, and it's a layup
            c += def.Team.Coach?.ContestBonus ?? 0; // the coach's defensive teaching
            // Tired defenders contest worse.
            c -= (1 - def.Energy[defender]) * 4.0;
            return c;
        }

        private double Skill(Player shooter, ShotZone zone)
        {
            var a = shooter.Attributes;
            return zone switch
            {
                ShotZone.Rim => a[Attr.Layup] * 0.4 + a[Attr.CloseShot] * 0.3 + a[Attr.Dunk] * 0.15 + a[Attr.Strength] * 0.15,
                ShotZone.ShortMid => a[Attr.MidRange] * 0.6 + a[Attr.CloseShot] * 0.2 + a[Attr.ShotCreation] * 0.2,
                ShotZone.LongMid => a[Attr.MidRange] * 0.7 + a[Attr.ShotCreation] * 0.3,
                ShotZone.CornerThree => a[Attr.ThreePoint] * 0.6 + a[Attr.CatchAndShoot] * 0.4,
                _ => a[Attr.ThreePoint] * 0.65 + a[Attr.CatchAndShoot] * 0.2 + a[Attr.ShotCreation] * 0.15,
            };
        }

        private static double Sigmoid(double x) => 1.0 / (1.0 + Math.Exp(-x));

        /// <summary>Returns true if the possession continues (offensive rebound).</summary>
        private bool ResolveShot(Side off, Side def, Player shooter, Player defender, ShotZone zone, bool transition, ref int chanceIndex)
        {
            var line = off.Line(shooter);
            int z = (int)zone;

            // Shooting foul?
            double foulRate = _cfg.ShootingFoulRateByZone[z] * (1 + (21 - defender.Attributes[Attr.Discipline]) / 20.0 * 0.5) * (def.Team.Coach?.FoulMultiplier ?? 1);
            bool fouled = _rng.Chance(foulRate);

            double contest = Contest(def, defender, zone);
            if (transition) contest -= 2.5; // open looks in transition
            // Doubling the star: he faces two defenders; whoever he finds is open.
            if (def.Tac.DoubleStar && _offStar != null && off.OnCourt.Contains(_offStar))
                contest += shooter == _offStar ? _cfg.DoubleStarContest : -_cfg.DoubleStarOpenLook;
            double logit = _cfg.ZoneBaseLogit[z]
                           + _cfg.SkillBeta * (zone.IsThree() ? _cfg.ThreeSkillShare : 1.0) * (Skill(shooter, zone) - 11)
                           - _cfg.ContestBeta * (contest - 11)
                           - _cfg.FatigueBeta * (1 - off.Energy[shooter])
                           + (off.IsHome ? _cfg.HomeLogit : 0)
                           + off.Form[shooter]
                           // Score effects: a team well ahead eases off, a team well behind pushes, so big leads shrink.
                           - _cfg.ScoreEffectPerPoint * Math.Clamp(Score(off) - Score(def), -25, 25)
                           // Tactics: how well both sides' players suit their system and scheme, and how the two match up.
                           + _fitLogit + off.ChemLogit
                           // A player in a role he suits plays a little better.
                           + _cfg.RoleFitLogit * (Roles.Stars(shooter, off.Tac.RoleOf(shooter)) - 3);
            double pMake = Sigmoid(logit);

            // A fouled shot is much harder to finish: the contact usually wins (NBA: about 2.5 and-ones a team a game).
            if (fouled) pMake *= _cfg.AndOneRate;

            // Blocks (only on missed shots inside the arc)
            bool made = _rng.Chance(pMake);
            Player? blocker = null;
            if (!made && !zone.IsThree() && !fouled)
            {
                // H94 (run 109): most blocks come from the help, not the man guarding the shooter, so any of the five can
                // block, weighted by his Block rating, size and position (QuickSim's weights), the man on the ball a little
                // more (much more on a mid-range pull-up). The chance follows the likely blocker's rating. One draw, in the
                // same place as before, so the random stream and every score are unchanged; only blocks (and what reads them) move.
                var pool = def.OnCourt;
                Span<double> w = stackalloc double[pool.Length];
                double sum = 0, rating = 0;
                for (int i = 0; i < pool.Length; i++)
                {
                    var p = pool[i];
                    w[i] = BlockWeight(p) * (p == defender ? (zone == ShotZone.Rim ? _cfg.BlockOnBallRim : _cfg.BlockOnBallMid) : 1);
                    sum += w[i]; rating += w[i] * p.Attributes[Attr.Block];
                }
                rating = sum > 0 ? rating / sum : defender.Attributes[Attr.Block];
                double blockRate = (zone == ShotZone.Rim ? _cfg.BlockRateRim : _cfg.BlockRateMid)
                                   * (1 + _cfg.BlockSkillBeta * (rating - 11) * 5);
                double u = _rng.NextDouble();
                if (u < blockRate)
                {
                    // The same draw, scaled back to 0-1, picks who.
                    double pick = u / blockRate * sum;
                    blocker = pool[^1];
                    for (int i = 0; i < pool.Length; i++) { pick -= w[i]; if (pick < 0) { blocker = pool[i]; break; } }
                    def.Line(blocker).Blocks++;
                    Log($"{blocker.LastName} blocks {shooter.LastName}");
                }
            }

            if (fouled)
            {
                var foulerLine = def.Line(defender);
                CommitFoul(def, defender);
                if (made)
                {
                    var assister = RecordFieldGoal(off, shooter, zone, made: true, transition);
                    line.AndOnes++;
                    _pbp?.MadeBasket(_period, _clock, off.Team.Abbreviation, Score(off), Score(def), line, shooter, assister, zone, andOne: true);
                    Play(PlayKind.Shot, off, shooter, assister, zone, made: true);
                    Play(PlayKind.Foul, def, defender, shooter, zone);
                    ShootFreeThrows(off, def, shooter, 1);
                }
                else
                {
                    Play(PlayKind.Foul, def, defender, shooter, zone);
                    ShootFreeThrows(off, def, shooter, zone.IsThree() ? 3 : 2);
                }
                return false;
            }

            var assist = RecordFieldGoal(off, shooter, zone, made, transition);
            Play(PlayKind.Shot, off, shooter, made ? assist : blocker, zone, made);
            if (made)
            {
                _pbp?.MadeBasket(_period, _clock, off.Team.Abbreviation, Score(off), Score(def), line, shooter, assist, zone, andOne: false);
                return false;
            }

            // Rebound
            return Rebound(off, def, zone, ref chanceIndex);
        }

        /// <summary>Record a field-goal attempt in the box score. Returns the assister of a made, assisted basket (else null).</summary>
        private Player? RecordFieldGoal(Side off, Player shooter, ShotZone zone, bool made, bool transition)
        {
            var line = off.Line(shooter);
            line.FGA++;
            line.ZoneAttempts[(int)zone]++;
            if (zone.IsThree()) line.ThreePA++;
            if (!made) return null;

            line.FGM++;
            line.ZoneMakes[(int)zone]++;
            _lastScorer = shooter;
            int pts = zone.IsThree() ? 3 : 2;
            line.Points += pts;
            if (zone.IsThree()) line.ThreePM++;

            // Assist
            double assistedShare = _cfg.AssistedShareByZone[(int)zone] * (transition ? 1.1 : 1.0) * Tactics.AssistFactor(off.Tac.System);
            if (_rng.Chance(assistedShare))
            {
                var others = off.OnCourt.Where(p => p != shooter).ToArray();
                var w = others.Select(p => Math.Pow(p.Attributes[Attr.Passing] * 0.6 + p.Attributes[Attr.CourtVision] * 0.4, 2.2) * (0.6 + p.Tendencies.PassFirst / 100.0)
                                             * Roles.Info(off.Tac.RoleOf(p)).Playmaking).ToArray();
                var passer = others[_rng.WeightedIndex(w)];
                off.Line(passer).Assists++;
                return passer;
            }
            return null;
        }

        private bool Rebound(Side off, Side def, ShotZone zone, ref int chanceIndex)
        {
            // Team rebound / out of bounds: possession simply changes hands.
            if (_rng.Chance(_cfg.TeamReboundShare)) return false;

            double offStrength = off.OnCourt.Sum(p => p.Attributes[Attr.OffensiveRebounding] * (0.5 + p.Tendencies.CrashGlass / 100.0) + (p.HeightCm - 200) * 0.08);
            double defStrength = def.OnCourt.Sum(p => p.Attributes[Attr.DefensiveRebounding] + (p.HeightCm - 200) * 0.08);
            double crash = (off.Tac.CrashGlass - 50) / 50.0 * 0.05;
            double longRebound = zone.IsThree() ? 0.02 : 0.0; // long misses slightly favour the offence
            double pOff = _cfg.OffensiveReboundBase + _cfg.ReboundSkillBeta * (offStrength - defStrength) / 5.0 + crash + longRebound
                          + Tactics.ZoneOffRebound(def.Tac.Scheme); // run 194: nobody boxes out the 1-3-1's weak side
            pOff = Math.Clamp(pOff, 0.12, 0.42);

            if (_rng.Chance(pOff))
            {
                var w = off.OnCourt.Select(p => Math.Pow(p.Attributes[Attr.OffensiveRebounding] + (p.HeightCm - 195) * 0.1 + p.Attributes[Attr.Vertical] * 0.3, 2) * (0.5 + p.Tendencies.CrashGlass / 100.0)).ToArray();
                var r = off.OnCourt[_rng.WeightedIndex(w)];
                off.Line(r).OReb++;
                chanceIndex++;
                // A putback chance is quick.
                BurnClock(Math.Clamp(_rng.NextGaussian(6, 3), 1, 14));
                Log($"Offensive rebound {r.LastName}");
                Play(PlayKind.OffensiveRebound, off, r);
                return _clock > 0;
            }
            else
            {
                var w = def.OnCourt.Select(p => Math.Pow(p.Attributes[Attr.DefensiveRebounding] + (p.HeightCm - 195) * 0.12 + p.Attributes[Attr.Strength] * 0.2, 2)).ToArray();
                var r = def.OnCourt[_rng.WeightedIndex(w)];
                def.Line(r).DReb++;
                Play(PlayKind.DefensiveRebound, def, r);
                if (_rng.Chance(0.18)) _transition = true; // outlet and run
                return false;
            }
        }

        /// <summary>
        /// Free throws for a common foul in the bonus: two shots, or under college rules (K11) a one-and-one from the 7th
        /// team foul in a half (the second shot only if the first goes in) and two from the 10th.
        /// </summary>
        private void ShootBonus(Side off, Side def, Player shooter)
        {
            if (_rules.BonusShots(def.TeamFouls) != 1) { ShootFreeThrows(off, def, shooter, 2); return; }
            int made = off.Line(shooter).FTM;
            ShootFreeThrows(off, def, shooter, 1);
            if (off.Line(shooter).FTM > made) ShootFreeThrows(off, def, shooter, 1);
        }

        private void ShootFreeThrows(Side off, Side def, Player shooter, int count)
        {
            var line = off.Line(shooter);
            double p = Sigmoid(_cfg.FtBaseLogit + _cfg.FtSkillBeta * (shooter.Attributes[Attr.FreeThrow] - 11) + (shooter.Attributes[Attr.Composure] - 11) * 0.02);
            for (int i = 0; i < count; i++)
            {
                line.FTA++;
                if (_rng.Chance(p)) { line.FTM++; line.Points++; _lastScorer = line.Player; _pbp?.FreeThrow(line.Player); Play(PlayKind.FreeThrow, off, shooter, made: true); }
                else
                {
                    Play(PlayKind.FreeThrow, off, shooter);
                    if (i < count - 1) continue;
                    // Missed last free throw: live rebound.
                    int dummy = 0;
                    if (Rebound(off, def, ShotZone.Rim, ref dummy))
                    {
                        // Simplify: offensive board on a FT miss becomes a quick putback attempt.
                        var zone = ShotZone.Rim;
                        var r = off.OnCourt[_rng.Next(5)];
                        var d = PickDefender(def, r, zone);
                        int ci = 1;
                        while (ResolveShot(off, def, r, d, zone, false, ref ci) && _clock > 0) { }
                    }
                }
            }
        }

        private void CommitFoul(Side def, Player fouler)
        {
            var l = def.Line(fouler);
            l.Fouls++;
            def.TeamFouls++;
            if (l.Fouls >= _rules.FoulsToFoulOut && !l.FouledOut)
            {
                l.FouledOut = true;
                Log($"{fouler.LastName} fouls out");
                ReplacePlayer(def, fouler, force: true);
            }
        }

        // --------------------------------------------------------------------------------------
        /// <summary>Consume extra game clock inside a possession, crediting minutes and fatigue to both sides.</summary>
        private void BurnClock(double seconds)
        {
            seconds = Math.Min(_clock, seconds);
            if (seconds <= 0) return;
            _clock -= seconds;
            _elapsed += seconds;
            DrainEnergy(_home, seconds);
            DrainEnergy(_away, seconds);
        }

        /// <param name="clockless">Run 194: extra legwork only (the press): drains the five on the floor, adds no minutes and rests nobody.</param>
        private void DrainEnergy(Side side, double seconds, bool clockless = false)
        {
            double possFraction = seconds / 14.0;
            foreach (var p in side.OnCourt)
            {
                double drain = _cfg.EnergyDrainPerPossession * possFraction * (1.6 - p.Attributes[Attr.Stamina] / 20.0);
                side.Energy[p] = Math.Max(0.3, side.Energy[p] - drain);
                if (!clockless) side.Line(p).Seconds += seconds;
            }
            if (clockless) return;
            foreach (var p in side.Team.Roster)
            {
                if (Array.IndexOf(side.OnCourt, p) >= 0) continue;
                double rec = _cfg.EnergyRecoverPerPossession * possFraction * (0.7 + p.Attributes[Attr.NaturalFitness] / 20.0);
                side.Energy[p] = Math.Min(1.0, side.Energy[p] + rec);
            }
        }

        private bool IsClosingTime() =>
            _period >= _rules.Periods && _clock < 360 && Math.Abs(Score(_home) - Score(_away)) <= 8;

        /// <summary>Garbage time: the game is decided, so the deep bench plays.</summary>
        private bool IsBlowout()
        {
            int margin = Math.Abs(Score(_home) - Score(_away));
            if (_period < _rules.Periods) return _period == _rules.Periods - 1 && _clock < 180 && margin >= 28;
            return margin >= _cfg.BlowoutMargin + (int)(_clock / 60.0 * 1.5);
        }

        private void CheckSubstitutions(Side side, bool force)
        {
            if (_elapsed < side.ManualHoldUntil) return; // the coach picked this five; leave it alone for a while
            double regulation = _rules.GameMinutes * 60.0;
            double progress = Math.Min(1.0, _elapsed / regulation);
            bool closing = IsClosingTime();

            if (IsBlowout())
            {
                // Rest the rotation: anyone in the top rotation slots who is still out there comes off for the deep bench.
                for (int i = 0; i < 5; i++)
                {
                    var p = side.OnCourt[i];
                    if (side.Rotation.IndexOf(p) is >= 0 and < 8) ReplacePlayer(side, p, force: true, progress, deepBench: true);
                }
                return;
            }

            if (closing)
            {
                // Closing lineup: get the starters back on the floor if they have legs.
                foreach (var starter in side.Starters)
                {
                    if (Array.IndexOf(side.OnCourt, starter) >= 0 || side.Line(starter).FouledOut || side.Energy[starter] < 0.45) continue;
                    // Replace the weakest non-starter on the floor.
                    var weakest = side.OnCourt.Where(p => !side.Starters.Contains(p)).OrderBy(p => p.Overall).FirstOrDefault();
                    if (weakest == null) break;
                    side.OnCourt[Array.IndexOf(side.OnCourt, weakest)] = starter;
                }
                return;
            }

            for (int i = 0; i < 5; i++)
            {
                var p = side.OnCourt[i];
                var line = side.Line(p);
                bool foulTrouble = (_period <= 2 && line.Fouls >= 3) || (_period == 3 && line.Fouls >= 4);
                double aheadOfPace = line.Seconds - side.TargetSeconds[p] * progress;
                bool overMinutes = aheadOfPace > _cfg.MinutesSlackSeconds && progress < 0.97;
                bool tired = side.Energy[p] < _cfg.SubOutEnergy;
                if (tired || foulTrouble || overMinutes || (force && side.Energy[p] < _cfg.SubOutEnergy + 0.08))
                    ReplacePlayer(side, p, force: false, progress);
            }
        }

        private void ReplacePlayer(Side side, Player outgoing, bool force, double progress = 0, bool deepBench = false)
        {
            int slot = Array.IndexOf(side.OnCourt, outgoing);
            if (slot < 0) return;

            IEnumerable<Player> pool = deepBench
                ? side.Dressed.Where(p => side.Rotation.IndexOf(p) is < 0 or >= 8)
                : side.Rotation;
            IEnumerable<Player> candidates = pool
                .Where(p => Array.IndexOf(side.OnCourt, p) < 0 && !side.Line(p).FouledOut);
            if (!force) candidates = candidates.Where(p => side.Energy[p] >= _cfg.SubInMinEnergy || side.Energy[p] > side.Energy[outgoing] + 0.15);

            var list = candidates.ToList();
            if (list.Count == 0 && force)
                list = side.Dressed.Where(p => Array.IndexOf(side.OnCourt, p) < 0 && !side.Line(p).FouledOut).ToList();
            if (list.Count == 0) return;

            // Prefer the player furthest behind his minutes target, with positional fit and quality as tie-breakers.
            double regulation = _rules.GameMinutes * 60.0;
            var pick = list
                .OrderByDescending(p => (side.TargetSeconds[p] * progress - side.Line(p).Seconds) / 60.0 + PositionFit(p, outgoing) * 2.0 + p.Overall * 0.4)
                .First();
            side.OnCourt[slot] = pick;
        }

        private static int PositionFit(Player sub, Player outgoing)
        {
            if (sub.PrimaryPosition == outgoing.PrimaryPosition) return 3;
            if (sub.SecondaryPosition == outgoing.PrimaryPosition || sub.PrimaryPosition == outgoing.SecondaryPosition) return 2;
            return Math.Abs((int)sub.PrimaryPosition - (int)outgoing.PrimaryPosition) <= 1 ? 1 : 0;
        }

        private void Log(string s) => _pbp?.Line(_period, _clock, s);
        private static string Initialled(Player p) => p.FirstName.Length > 0 ? $"{p.FirstName[0]}. {p.LastName}" : p.LastName;

        /// <summary>Record a play as data for the tracker (logged games only; reads no random numbers, run 80).</summary>
        private void Play(PlayKind kind, Side side, Player? actor, Player? other = null, ShotZone zone = ShotZone.Rim, bool made = false)
        {
            if (_log) _result.Plays.Add(new PlayEvent(_period, _clock, kind, side.IsHome, actor, other, zone, made, Score(_home), Score(_away)));
        }
    }
}
