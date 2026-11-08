using Hardwood.Core.Model;

namespace Hardwood.Core.Engine;

/// <summary>
/// Post-game injury rolls. Risk scales with minutes played, fatigue, age and the hidden Injury Proneness trait.
/// Severe injuries also take a permanent bite out of physical attributes.
/// </summary>
public sealed class InjuryEngine
{
    private readonly EngineConfig _cfg;

    public InjuryEngine(EngineConfig? cfg = null) => _cfg = cfg ?? EngineConfig.Default;

    public Injury? Roll(Player p, PlayerLine line, DateOnly gameDate, Rng rng, Team? team = null)
    {
        if (line.Seconds <= 0 || p.Injury?.IsOut(gameDate) == true) return null;
        double staffRisk = team != null ? StaffEffects.InjuryRiskFactor(team) : 1.0;
        double staffRecovery = team != null ? StaffEffects.RecoveryFactor(team) : 1.0;

        int age = p.Age(gameDate);
        double minutesFactor = line.Minutes / 30.0;
        double proneness = 0.6 + p.Hidden.InjuryProneness / 20.0 * 0.8;      // 0.64 .. 1.4
        double ageFactor = age <= 25 ? 0.9 : age <= 30 ? 1.0 : 1.0 + (age - 30) * 0.08;
        double durability = 1.25 - p.Attributes[Attr.Durability] / 20.0 * 0.5; // 1.2 .. 0.75
        double prob = _cfg.InjuryBaseRate * minutesFactor * proneness * ageFactor * durability * staffRisk;
        if (!rng.Chance(prob)) return null;

        var kinds = InjuryCatalogue.Kinds;
        var weights = new double[kinds.Length];
        for (int i = 0; i < kinds.Length; i++)
        {
            weights[i] = kinds[i].Weight;
            // Proneness and age tilt toward the serious end.
            if (kinds[i].Severity >= InjurySeverity.Severe) weights[i] *= proneness * (age >= 30 ? 1.3 : 1.0);
        }
        var kind = kinds[rng.WeightedIndex(weights)];
        int days = Math.Max(1, (int)Math.Round(rng.Next(kind.MinDays, kind.MaxDays + 1) * staffRecovery));

        int loss = 0;
        if (kind.Physical)
        {
            // Permanent toll on explosiveness.
            foreach (var a in new[] { Attr.Speed, Attr.Acceleration, Attr.Vertical, Attr.Agility })
            {
                int d = rng.Chance(0.6) ? 1 : 0;
                if (kind.Severity == InjurySeverity.SeasonEnding && rng.Chance(0.5)) d++;
                p.Attributes[a] = p.Attributes[a] - d;
                loss += d;
            }
        }

        var injury = new Injury
        {
            Name = kind.Name,
            Severity = kind.Severity,
            Occurred = gameDate,
            ReturnDate = gameDate.AddDays(days),
            AttributeLoss = loss,
        };
        p.Injury = injury;
        p.InjuryHistory.Add(injury);
        return injury;
    }
}
