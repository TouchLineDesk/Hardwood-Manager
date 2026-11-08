using Hardwood.Core.Model;

namespace Hardwood.Core.Engine;

/// <summary>
/// Possession-by-possession stat-based simulation. No court geometry: each possession is resolved
/// from the ten players on the floor, their tactics, fatigue and a seeded RNG, and produces a full
/// box score plus a short play log.
/// </summary>
public sealed class GameSimulator
{
    private readonly EngineConfig _cfg;

    public GameSimulator(EngineConfig? cfg = null) => _cfg = cfg ?? EngineConfig.Default;

    public GameResult Simulate(Team home, Team away, Rules rules, ulong seed, bool keepLog = false)
    {
        var rng = new Rng(seed);
        var result = new GameResult
        {
            Home = new TeamBox { Team = home },
            Away = new TeamBox { Team = away },
            Seed = seed,
        };
        var h = new Side(home, result.Home, rng, _cfg, isHome: true);
        var a = new Side(away, result.Away, rng, _cfg, isHome: false);
        var game = new GameState(h, a, rules, rng, _cfg, result, keepLog);
        game.Run();
        return result;
    }

    // ------------------------------------------------------------------------------------------
    // Per-team live state
    // ------------------------------------------------------------------------------------------
    private sealed class Side
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
        public int TeamFouls { get; set; }
        public int Possessions { get; set; }

        public Side(Team team, TeamBox box, Rng rng, EngineConfig cfg, bool isHome)
        {
            Team = team; Box = box; IsHome = isHome;
            var available = team.Roster.OrderByDescending(p => p.Overall).ToList();
            foreach (var p in available)
            {
                Box.Lines.Add(new PlayerLine { Player = p });
                Energy[p] = 1.0;
                double sd = cfg.FormSd * (21 - p.Hidden.Consistency) / 10.0;
                Form[p] = rng.NextGaussian(0, sd);
            }
            // Starters: greedy one-per-position by quality, then best remaining.
            var starters = new List<Player>();
            foreach (var pos in Enum.GetValues<Position>())
            {
                var best = available.FirstOrDefault(p => !starters.Contains(p) && (p.PrimaryPosition == pos || p.SecondaryPosition == pos));
                if (best != null) starters.Add(best);
            }
            foreach (var p in available) { if (starters.Count >= 5) break; if (!starters.Contains(p)) starters.Add(p); }
            for (int i = 0; i < 5; i++) { OnCourt[i] = starters[i]; Box.LineFor(starters[i]).Started = true; }
            Starters.AddRange(starters);
            // Rotation: starters first, then the best of the rest.
            Rotation.AddRange(starters.OrderByDescending(p => p.Overall));
            Rotation.AddRange(available.Where(p => !starters.Contains(p)).Take(Math.Max(cfg.RotationSize, 5) - 5));

            // Minutes targets by rotation slot, scaled to the game length.
            double scale = cfg.RegulationMinutes / 48.0;
            for (int i = 0; i < Rotation.Count; i++)
            {
                double minutes = i < cfg.TargetMinutesBySlot.Length ? cfg.TargetMinutesBySlot[i] : 0;
                TargetSeconds[Rotation[i]] = minutes * 60 * scale;
            }
            foreach (var p in available) TargetSeconds.TryAdd(p, 0);
        }

        public PlayerLine Line(Player p) => Box.LineFor(p);
    }

    // ------------------------------------------------------------------------------------------
    // Game state machine
    // ------------------------------------------------------------------------------------------
    private sealed class GameState
    {
        private readonly Side _home, _away;
        private readonly Rules _rules;
        private readonly Rng _rng;
        private readonly EngineConfig _cfg;
        private readonly GameResult _result;
        private readonly bool _log;
        private readonly List<int> _homeByPeriod = new(), _awayByPeriod = new();

        private int _period;
        private double _clock;        // seconds remaining in period
        private double _elapsed;      // seconds played so far in the game
        private Side _offense, _defense;
        private bool _transition;     // next possession starts in transition (after live-ball turnover)

        public GameState(Side home, Side away, Rules rules, Rng rng, EngineConfig cfg, GameResult result, bool log)
        {
            _home = home; _away = away; _rules = rules; _rng = rng; _cfg = cfg; _result = result; _log = log;
            _offense = home; _defense = away;
        }

        private int Score(Side s) => s.Box.Points;

        public void Run()
        {
            for (_period = 1; _period <= _rules.Periods; _period++)
                PlayPeriod(_rules.MinutesPerPeriod * 60);

            while (Score(_home) == Score(_away))
            {
                _period++;
                _result.Overtimes++;
                PlayPeriod(_cfg.OvertimeMinutes * 60);
            }

            _home.Box.PointsByPeriod = _homeByPeriod.ToArray();
            _away.Box.PointsByPeriod = _awayByPeriod.ToArray();
            _home.Box.Possessions = _home.Possessions;
            _away.Box.Possessions = _away.Possessions;
        }

        private void PlayPeriod(int seconds)
        {
            _clock = seconds;
            _home.TeamFouls = 0; _away.TeamFouls = 0;
            int hStart = Score(_home), aStart = Score(_away);

            // Alternate opening possession by period (jump ball simplified).
            if (_period % 2 == 1) { _offense = _home; _defense = _away; } else { _offense = _away; _defense = _home; }
            _transition = false;

            // Fresh legs at period start: substitute tired players at the break.
            CheckSubstitutions(_home, force: true);
            CheckSubstitutions(_away, force: true);

            while (_clock > 0)
            {
                RunPossession();
                (_offense, _defense) = (_defense, _offense);
            }

            _homeByPeriod.Add(Score(_home) - hStart);
            _awayByPeriod.Add(Score(_away) - aStart);
            Log($"End of period {_period}: {_away.Team.Abbreviation} {Score(_away)} - {_home.Team.Abbreviation} {Score(_home)}");
        }

        // --------------------------------------------------------------------------------------
        private void RunPossession()
        {
            var off = _offense; var def = _defense;
            off.Possessions++;

            double pace = (off.Team.Tactic.Pace + def.Team.Tactic.Pace) / 2.0;
            double meanSeconds = _rules.GameMinutes * 60.0 / (2.0 * pace);
            double seconds = _transition
                ? Math.Clamp(_rng.NextGaussian(_cfg.TransitionSeconds, 2.0), 2, 12)
                : Math.Clamp(_rng.NextGaussian(meanSeconds, _cfg.SecondsPerPossessionSd), 3, _rules.ShotClockSeconds);
            bool wasTransition = _transition;
            _transition = false;

            // Last possession of a period: use whatever time is left.
            seconds = Math.Min(seconds, _clock);
            _clock -= seconds;
            _elapsed += seconds;

            DrainEnergy(off, seconds); DrainEnergy(def, seconds);
            int pointsBefore = Score(off);

            bool possessionLive = true;
            int chanceIndex = 0; // 0 = first shot, 1+ = after offensive rebound
            while (possessionLive)
            {
                // 1. Turnover
                if (chanceIndex == 0 && TryTurnover(off, def)) { possessionLive = false; break; }

                // 2. Non-shooting foul (resets the shot clock; bonus free throws if in penalty)
                if (_rng.Chance(_cfg.NonShootingFoulRate))
                {
                    var fouler = PickDefender(def, null, ShotZone.ShortMid);
                    CommitFoul(def, fouler);
                    if (def.TeamFouls >= _rules.TeamFoulBonusThreshold)
                    {
                        var shooter = PickShooter(off, wasTransition);
                        ShootFreeThrows(off, def, shooter, 2);
                        possessionLive = false; break;
                    }
                    // Otherwise play on: a little more clock burns.
                    BurnClock(Math.Clamp(_rng.NextGaussian(8, 3), 1, 14));
                    continue;
                }

                // 3. Shot
                var shooter2 = PickShooter(off, wasTransition);
                var zone = PickZone(off, shooter2, wasTransition);
                var defender = PickDefender(def, shooter2, zone);
                possessionLive = ResolveShot(off, def, shooter2, defender, zone, wasTransition, ref chanceIndex);
                wasTransition = false;
            }

            // Plus/minus for everyone on court
            int delta = Score(off) - pointsBefore;
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
                              + (def.Team.Tactic.DefensiveAggression - 50) / 25.0;
            double p = _cfg.TurnoverBase - _cfg.TurnoverSkillBeta * (security - pressure);
            p = Math.Clamp(p, 0.04, 0.25);
            if (!_rng.Chance(p)) return false;

            // Who turned it over: weighted by usage (handlers more often)
            var weights = off.OnCourt.Select(pl => 1.0 + pl.Attributes[Attr.BallHandling] * 0.3 + pl.Attributes[Attr.ShotCreation] * 0.2).ToArray();
            var culprit = off.OnCourt[_rng.WeightedIndex(weights)];
            off.Line(culprit).Turnovers++;

            if (_rng.Chance(_cfg.LiveBallTurnoverShare))
            {
                var stealWeights = def.OnCourt.Select(pl => Math.Pow(pl.Attributes[Attr.Steal], 2) * (1 + pl.Tendencies.GambleSteals / 100.0)).ToArray();
                var thief = def.OnCourt[_rng.WeightedIndex(stealWeights)];
                def.Line(thief).Steals++;
                _transition = true;
                Log($"{thief.LastName} steals from {culprit.LastName}");
            }
            else Log($"Turnover by {culprit.LastName}");
            return true;
        }

        private Player PickShooter(Side off, bool transition)
        {
            double star = off.Team.Tactic.StarUsage / 50.0; // 1.0 = neutral
            var weights = new double[5];
            for (int i = 0; i < 5; i++)
            {
                var p = off.OnCourt[i];
                var a = p.Attributes;
                double usage = a[Attr.ShotCreation] * 1.2 + a[Attr.ThreePoint] * 0.5 + a[Attr.CloseShot] * 0.5 + a[Attr.BallHandling] * 0.4
                               + (100 - p.Tendencies.PassFirst) * 0.08;
                if (transition) usage += a[Attr.Speed] * 0.5;
                // Star usage sharpens or flattens the distribution.
                weights[i] = Math.Pow(Math.Max(1, usage), 1.6 * star) * (0.75 + 0.25 * off.Energy[p]);
            }
            return off.OnCourt[_rng.WeightedIndex(weights)];
        }

        private ShotZone PickZone(Side off, Player shooter, bool transition)
        {
            var t = shooter.Tendencies;
            var baseW = _cfg.ZoneBaseWeight;
            double threeEmphasis = off.Team.Tactic.ThreePointEmphasis / 50.0;
            var w = new double[5];
            double threeFactor = (t.ThreePointRate / 35.0) * threeEmphasis;
            double driveFactor = (t.DriveRate + t.PostUpRate) / 40.0;
            double midFactor = 1.0 + t.IsolationRate / 40.0 - threeFactor * 0.25;

            w[(int)ShotZone.Rim] = baseW[0] * driveFactor * (transition ? 1.6 : 1.0);
            w[(int)ShotZone.ShortMid] = baseW[1] * Math.Max(0.3, midFactor);
            w[(int)ShotZone.LongMid] = baseW[2] * Math.Max(0.3, midFactor);
            w[(int)ShotZone.CornerThree] = baseW[3] * threeFactor * (transition ? 1.2 : 1.0);
            w[(int)ShotZone.AboveBreakThree] = baseW[4] * threeFactor * (transition ? 1.3 : 1.0);
            return (ShotZone)_rng.WeightedIndex(w);
        }

        private Player PickDefender(Side def, Player? shooter, ShotZone zone)
        {
            // Nearest-position defender most of the time, otherwise anyone (help, switches).
            if (shooter != null && _rng.Chance(0.7))
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
            double foulRate = _cfg.ShootingFoulRateByZone[z] * (1 + (21 - defender.Attributes[Attr.Discipline]) / 20.0 * 0.5);
            bool fouled = _rng.Chance(foulRate);

            double contest = Contest(def, defender, zone);
            if (transition) contest -= 2.5; // open looks in transition
            double logit = _cfg.ZoneBaseLogit[z]
                           + _cfg.SkillBeta * (Skill(shooter, zone) - 11)
                           - _cfg.ContestBeta * (contest - 11)
                           - _cfg.FatigueBeta * (1 - off.Energy[shooter])
                           + (off.IsHome ? _cfg.HomeLogit : 0)
                           + off.Form[shooter];
            double pMake = Sigmoid(logit);

            // Blocks (only on missed shots inside the arc)
            bool made = _rng.Chance(pMake);
            if (!made && !zone.IsThree())
            {
                double blockRate = (zone == ShotZone.Rim ? _cfg.BlockRateRim : _cfg.BlockRateMid)
                                   * (1 + _cfg.BlockSkillBeta * (defender.Attributes[Attr.Block] - 11) * 5);
                if (!fouled && _rng.Chance(blockRate))
                {
                    def.Line(defender).Blocks++;
                    Log($"{defender.LastName} blocks {shooter.LastName}");
                }
            }

            if (fouled)
            {
                var foulerLine = def.Line(defender);
                CommitFoul(def, defender);
                if (made)
                {
                    RecordFieldGoal(off, shooter, zone, made: true, transition);
                    Log($"{shooter.LastName} scores through contact, and one");
                    ShootFreeThrows(off, def, shooter, 1);
                }
                else
                {
                    ShootFreeThrows(off, def, shooter, zone.IsThree() ? 3 : 2);
                }
                return false;
            }

            RecordFieldGoal(off, shooter, zone, made, transition);
            if (made)
            {
                Log($"{shooter.LastName} {(zone.IsThree() ? "hits a three" : zone == ShotZone.Rim ? "scores at the rim" : "hits a jumper")}");
                return false;
            }

            // Rebound
            return Rebound(off, def, zone, ref chanceIndex);
        }

        private void RecordFieldGoal(Side off, Player shooter, ShotZone zone, bool made, bool transition)
        {
            var line = off.Line(shooter);
            line.FGA++;
            line.ZoneAttempts[(int)zone]++;
            if (zone.IsThree()) line.ThreePA++;
            if (!made) return;

            line.FGM++;
            line.ZoneMakes[(int)zone]++;
            int pts = zone.IsThree() ? 3 : 2;
            line.Points += pts;
            if (zone.IsThree()) line.ThreePM++;

            // Assist
            double assistedShare = _cfg.AssistedShareByZone[(int)zone] * (transition ? 1.1 : 1.0);
            if (_rng.Chance(assistedShare))
            {
                var others = off.OnCourt.Where(p => p != shooter).ToArray();
                var w = others.Select(p => Math.Pow(p.Attributes[Attr.Passing] * 0.6 + p.Attributes[Attr.CourtVision] * 0.4, 2.2) * (0.6 + p.Tendencies.PassFirst / 100.0)).ToArray();
                var passer = others[_rng.WeightedIndex(w)];
                off.Line(passer).Assists++;
            }
        }

        private bool Rebound(Side off, Side def, ShotZone zone, ref int chanceIndex)
        {
            // Team rebound / out of bounds: possession simply changes hands.
            if (_rng.Chance(_cfg.TeamReboundShare)) return false;

            double offStrength = off.OnCourt.Sum(p => p.Attributes[Attr.OffensiveRebounding] * (0.5 + p.Tendencies.CrashGlass / 100.0) + (p.HeightCm - 200) * 0.08);
            double defStrength = def.OnCourt.Sum(p => p.Attributes[Attr.DefensiveRebounding] + (p.HeightCm - 200) * 0.08);
            double crash = (off.Team.Tactic.CrashGlass - 50) / 50.0 * 0.05;
            double longRebound = zone.IsThree() ? 0.02 : 0.0; // long misses slightly favour the offence
            double pOff = _cfg.OffensiveReboundBase + _cfg.ReboundSkillBeta * (offStrength - defStrength) / 5.0 + crash + longRebound;
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
                return _clock > 0;
            }
            else
            {
                var w = def.OnCourt.Select(p => Math.Pow(p.Attributes[Attr.DefensiveRebounding] + (p.HeightCm - 195) * 0.12 + p.Attributes[Attr.Strength] * 0.2, 2)).ToArray();
                var r = def.OnCourt[_rng.WeightedIndex(w)];
                def.Line(r).DReb++;
                if (_rng.Chance(0.18)) _transition = true; // outlet and run
                return false;
            }
        }

        private void ShootFreeThrows(Side off, Side def, Player shooter, int count)
        {
            var line = off.Line(shooter);
            double p = Sigmoid(_cfg.FtBaseLogit + _cfg.FtSkillBeta * (shooter.Attributes[Attr.FreeThrow] - 11) + (shooter.Attributes[Attr.Composure] - 11) * 0.02);
            for (int i = 0; i < count; i++)
            {
                line.FTA++;
                if (_rng.Chance(p)) { line.FTM++; line.Points++; }
                else if (i == count - 1)
                {
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

        private void DrainEnergy(Side side, double seconds)
        {
            double possFraction = seconds / 14.0;
            foreach (var p in side.OnCourt)
            {
                double drain = _cfg.EnergyDrainPerPossession * possFraction * (1.6 - p.Attributes[Attr.Stamina] / 20.0);
                side.Energy[p] = Math.Max(0.3, side.Energy[p] - drain);
                side.Line(p).Seconds += seconds;
            }
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
                ? side.Team.Roster.Where(p => side.Rotation.IndexOf(p) is < 0 or >= 8)
                : side.Rotation;
            IEnumerable<Player> candidates = pool
                .Where(p => Array.IndexOf(side.OnCourt, p) < 0 && !side.Line(p).FouledOut);
            if (!force) candidates = candidates.Where(p => side.Energy[p] >= _cfg.SubInMinEnergy || side.Energy[p] > side.Energy[outgoing] + 0.15);

            var list = candidates.ToList();
            if (list.Count == 0 && force)
                list = side.Team.Roster.Where(p => Array.IndexOf(side.OnCourt, p) < 0 && !side.Line(p).FouledOut).ToList();
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

        private void Log(string s)
        {
            if (!_log) return;
            int m = (int)(_clock / 60), sec = (int)(_clock % 60);
            _result.PlayLog.Add($"Q{_period} {m:00}:{sec:00}  {s}");
        }
    }
}
