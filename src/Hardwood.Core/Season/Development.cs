using Hardwood.Core.Model;

namespace Hardwood.Core.Season;

/// <summary>Everything that can tune progression lives here.</summary>
public sealed class DevelopmentConfig
{
    /// <summary>Share of the CA→PA gap closed per offseason, by age.</summary>
    public double GapShare(int age) => age switch
    {
        <= 19 => 0.26,
        20 => 0.24,
        21 => 0.22,
        22 => 0.19,
        23 => 0.16,
        24 => 0.13,
        25 => 0.09,
        26 => 0.06,
        27 => 0.03,
        28 => 0.015,
        _ => 0.0,
    };

    /// <summary>Flat CA decline per offseason for veterans, before fitness adjustment.</summary>
    public double Decline(int age) => age switch
    {
        <= 29 => 0.0,
        30 => 1.0,
        31 => 2.0,
        32 => 3.0,
        33 => 4.5,
        34 => 6.0,
        35 => 7.5,
        36 => 9.0,
        _ => 11.0,
    };

    public double NoiseSd { get; set; } = 3.0;
    /// <summary>Minutes per game at which the playing-time modifier reaches its maximum.</summary>
    public double FullMinutes { get; set; } = 30.0;
    public double MinMinutesModifier { get; set; } = 0.65;
    public double MaxMinutesModifier { get; set; } = 1.30;
    /// <summary>Physical attributes lose this much per year from age 30 (31+ accelerates).</summary>
    public double PhysicalDeclineBase { get; set; } = 0.45;
    /// <summary>Mental attributes gain this much per year until age 31.</summary>
    public double MentalGrowth { get; set; } = 0.30;
}

public sealed record DevelopmentReport(Player Player, int Age, int OldCa, int NewCa, int OldPa, int NewPa, double Mpg);

/// <summary>
/// Offseason progression: CA moves toward PA by age curve, minutes played and training quality,
/// veterans decline, attributes follow CA with group-specific shaping, and PA itself can shift for young players.
/// </summary>
public sealed class DevelopmentEngine
{
    private readonly DevelopmentConfig _cfg;
    private readonly Rng _rng;

    public DevelopmentEngine(Rng rng, DevelopmentConfig? cfg = null)
    {
        _rng = rng;
        _cfg = cfg ?? new DevelopmentConfig();
    }

    /// <param name="mpg">Minutes per game in the season just finished (0 if unused).</param>
    /// <param name="trainingQuality">Team training quality 1–20 (stub until staff exist; 10 = average).</param>
    public DevelopmentReport Develop(Player p, DateOnly offseasonDate, double mpg, int trainingQuality = 10)
    {
        int age = p.Age(offseasonDate);
        int oldCa = p.CurrentAbility, oldPa = p.PotentialAbility;

        // --- Potential drift ---
        if (age <= 22 && _rng.Chance(0.18))
            p.PotentialAbility = Math.Clamp(p.PotentialAbility + (int)Math.Round(_rng.NextGaussian(0, 6)), p.CurrentAbility, 200);
        if (age >= 26)
        {
            // Unrealised potential fades: PA drifts toward CA once the growth years are over.
            int gap = p.PotentialAbility - p.CurrentAbility;
            if (gap > 0) p.PotentialAbility -= Math.Min(gap, age >= 29 ? gap : Math.Max(1, gap / 3));
        }

        // --- CA change ---
        double minutesMod = _cfg.MinMinutesModifier + (_cfg.MaxMinutesModifier - _cfg.MinMinutesModifier) * Math.Clamp(mpg / _cfg.FullMinutes, 0, 1);
        double trainingMod = 0.85 + trainingQuality / 20.0 * 0.30;
        double profMod = 0.85 + p.Hidden.Professionalism / 20.0 * 0.30;

        double growth = (p.PotentialAbility - p.CurrentAbility) * _cfg.GapShare(age) * minutesMod * trainingMod * profMod;
        double fitness = (p.Attributes[Attr.NaturalFitness] + p.Attributes[Attr.Durability]) / 2.0;
        double decline = _cfg.Decline(age) * (1.35 - fitness / 20.0 * 0.70) * (2.0 - profMod);
        double delta = growth - decline + _rng.NextGaussian(0, _cfg.NoiseSd) * (age <= 24 ? 1.2 : 0.8);

        int newCa = (int)Math.Round(Math.Clamp(p.CurrentAbility + delta, 20, p.PotentialAbility));
        if (age >= 30) newCa = Math.Min(newCa, p.CurrentAbility + 2); // no late-career leaps
        p.CurrentAbility = newCa;
        if (p.PotentialAbility < p.CurrentAbility) p.PotentialAbility = p.CurrentAbility;

        ApplyToAttributes(p, newCa - oldCa, age);
        return new DevelopmentReport(p, age, oldCa, newCa, oldPa, p.PotentialAbility, mpg);
    }

    private void ApplyToAttributes(Player p, int caDelta, int age)
    {
        var a = p.Attributes;
        // One CA point ≈ 0.07 attribute points on average (200 CA ↔ 14 attribute points).
        double perAttr = caDelta * 14.0 / 200.0;

        foreach (var attr in AttrInfo.All)
        {
            var group = AttrInfo.GroupOf(attr);
            double d = perAttr;

            switch (group)
            {
                case AttrGroup.Mental:
                    // Mental keeps growing slowly regardless of CA until the early 30s, and never declines fast.
                    d = Math.Max(d, 0) * 0.6 + (age <= 31 ? _cfg.MentalGrowth : 0) + Math.Min(d, 0) * 0.25;
                    break;
                case AttrGroup.Physical:
                    // Physical declines with age independently of skill growth; young players gain a bit.
                    if (age >= 30) d = Math.Min(d, 0) * 0.5 - _cfg.PhysicalDeclineBase * (1 + (age - 30) * 0.35);
                    else if (age <= 22) d = Math.Max(d, 0) * 0.8 + 0.15;
                    else d = Math.Max(d, 0) * 0.5;
                    break;
                default:
                    // Skill groups carry most of the CA change; growth is a little uneven across attributes.
                    d = d * 1.25 * (0.6 + _rng.NextDouble() * 0.8);
                    break;
            }

            a[attr] = a[attr] + StochasticRound(d);
        }

        // Anchor the attribute mean to what the CA implies, so engine strength tracks CA over long careers.
        // Group shaping above moves mental up and physical down; the correction lands on the skill groups.
        double target = Generation.PlayerGenerator.MeanAttributeFor(p.CurrentAbility);
        double actual = AttrInfo.All.Average(x => (double)a[x]);
        double drift = target - actual; // positive: attributes lag CA
        if (Math.Abs(drift) > 0.05)
        {
            var skill = AttrInfo.All.Where(x => AttrInfo.GroupOf(x) is AttrGroup.Shooting or AttrGroup.Playmaking or AttrGroup.Defense).ToList();
            double perSkill = drift * AttrInfo.Count / skill.Count;
            foreach (var attr in skill) a[attr] = a[attr] + StochasticRound(perSkill);
        }
    }

    private int StochasticRound(double d)
    {
        int whole = (int)Math.Truncate(d);
        double frac = Math.Abs(d - whole);
        if (_rng.Chance(frac)) whole += Math.Sign(d);
        return whole;
    }

    /// <summary>Rule-based retirement decision, evaluated once per offseason.</summary>
    public bool ShouldRetire(Player p, DateOnly offseasonDate, double mpg)
    {
        int age = p.Age(offseasonDate);
        int ca = p.CurrentAbility;
        double prob = 0;
        if (age >= 40) prob = 0.95;
        else if (age >= 38) prob = 0.70;
        else if (age >= 36) prob = ca < 120 ? 0.60 : 0.30;
        else if (age >= 34) prob = ca < 100 ? 0.50 : ca < 125 ? 0.20 : 0.05;
        else if (age >= 31) prob = ca < 80 ? 0.35 : ca < 95 ? 0.10 : 0.0;
        else if (age >= 28 && ca < 65) prob = 0.30;

        if (mpg < 8 && age >= 30) prob += 0.15;      // nobody is playing him
        prob *= 1.1 - p.Hidden.Ambition / 20.0 * 0.4; // ambitious players hang on
        return _rng.Chance(Math.Clamp(prob, 0, 0.98));
    }
}
