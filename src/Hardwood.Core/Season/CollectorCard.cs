using Hardwood.Core.Model;

namespace Hardwood.Core.Season;

/// <summary>
/// What the collector card shows (Danny's D58 B, #466–#467): a 2K-style frame whose metal follows the player's ability,
/// three plain-English skill badges on the front, and on the back his season line, what he does well, his career and his
/// status. No tier label and no rating bars by attribute group. Everything here reads the attributes through
/// <see cref="Scouting"/>, so another club's player is shown as your scouts see him.
/// </summary>
public static class CollectorCard
{
    /// <summary>A skill badge: a plain-English name and the attributes it averages.</summary>
    public sealed record Skill(string Name, Attr[] Attrs, double Penalty = 0);

    /// <summary>The badge catalogue, in the order ties are broken. A penalty keeps the badges about how he plays: a
    /// one-attribute badge (Free-throw ace) or a trait (Leader, Iron man, High IQ) shows only when it really stands out.</summary>
    public static readonly Skill[] Skills =
    {
        new("Three-point shooter", new[] { Attr.ThreePoint, Attr.CatchAndShoot }),
        new("Shot creator", new[] { Attr.ShotCreation, Attr.BallHandling, Attr.MidRange }),
        new("Rim finisher", new[] { Attr.Layup, Attr.CloseShot, Attr.Dunk }),
        new("Mid-range scorer", new[] { Attr.MidRange, Attr.ShotCreation }, 1.0),
        new("Floor general", new[] { Attr.Passing, Attr.CourtVision, Attr.Decisions }),
        new("Pick-and-roll maestro", new[] { Attr.PickAndRollHandler, Attr.BallHandling, Attr.Passing }),
        new("Post scorer", new[] { Attr.PostMoves, Attr.CloseShot, Attr.Strength }),
        new("Lockdown defender", new[] { Attr.PerimeterDefense, Attr.Agility, Attr.Steal }),
        new("Rim protector", new[] { Attr.Block, Attr.InteriorDefense, Attr.HelpDefense }),
        new("Glass cleaner", new[] { Attr.DefensiveRebounding, Attr.OffensiveRebounding }),
        new("Pickpocket", new[] { Attr.Steal, Attr.PerimeterDefense }),
        new("High flyer", new[] { Attr.Vertical, Attr.Dunk, Attr.Acceleration }),
        new("Speedster", new[] { Attr.Speed, Attr.Acceleration }),
        new("Free-throw ace", new[] { Attr.FreeThrow }, 2.5),
        new("High IQ", new[] { Attr.BasketballIQ, Attr.Decisions, Attr.OffBallMovement }, 2.5),
        new("Leader", new[] { Attr.Leadership, Attr.Composure }, 2.5),
        new("Iron man", new[] { Attr.Stamina, Attr.NaturalFitness, Attr.Durability }, 2.5),
    };

    /// <summary>A badge is shown only when the player is at least this good at it (1–20).</summary>
    public const double BadgeFloor = 12.0;

    /// <summary>The areas the back's "What he does" list chooses from (the same eight as the player page's radar).</summary>
    public static readonly (string Name, Attr[] Attrs)[] Areas =
    {
        ("Inside", new[] { Attr.CloseShot, Attr.Layup, Attr.Dunk }),
        ("Mid-range", new[] { Attr.MidRange, Attr.ShotCreation }),
        ("Three-point", new[] { Attr.ThreePoint, Attr.CatchAndShoot }),
        ("Playmaking", new[] { Attr.BallHandling, Attr.Passing, Attr.CourtVision }),
        ("Perimeter D", new[] { Attr.PerimeterDefense, Attr.Steal }),
        ("Interior D", new[] { Attr.InteriorDefense, Attr.Block, Attr.HelpDefense }),
        ("Rebounding", new[] { Attr.DefensiveRebounding, Attr.OffensiveRebounding }),
        ("Athleticism", new[] { Attr.Speed, Attr.Acceleration, Attr.Vertical, Attr.Strength }),
    };

    private static double Seen(Player p, Attr a, double knowledge) =>
        knowledge >= 0.999 ? p.Attributes[a] : Scouting.Attribute(p, a, knowledge).Mid;

    private static double Score(Player p, Attr[] attrs, double knowledge) => attrs.Average(a => Seen(p, a, knowledge));

    /// <summary>
    /// Up to <paramref name="count"/> badges, best first. Overlapping badges (Shot creator and Mid-range scorer, Lockdown
    /// defender and Pickpocket) don't both show: a badge sharing two attributes with one already picked is skipped. A
    /// player good at nothing still gets his single best badge, so the card is never blank.
    /// </summary>
    public static IReadOnlyList<string> Badges(Player p, double knowledge, int count = 3)
    {
        var ranked = Skills.Select((s, i) => (s, i, v: Score(p, s.Attrs, knowledge) - s.Penalty))
            .OrderByDescending(x => x.v).ThenBy(x => x.i).ToList();
        var picked = new List<Skill>();
        foreach (var (s, _, v) in ranked)
        {
            if (picked.Count >= count) break;
            if (v < BadgeFloor && picked.Count > 0) break;
            if (picked.Any(q => q.Attrs.Intersect(s.Attrs).Count() >= 2)) continue;
            picked.Add(s);
        }
        return picked.Select(s => s.Name).ToList();
    }

    /// <summary>The back's "What he does": his best <paramref name="count"/> areas with a 1–20 value, best first.</summary>
    public static IReadOnlyList<(string Name, int Value)> Strengths(Player p, double knowledge, int count = 4) =>
        Areas.Select((a, i) => (a.Name, i, v: Score(p, a.Attrs, knowledge)))
            .OrderByDescending(x => x.v).ThenBy(x => x.i).Take(count)
            .Select(x => (x.Name, (int)Math.Round(x.v))).ToList();

    /// <summary>The frame's metal, from the ability your scouts see. Colours only; the card never names a tier.</summary>
    public static string FrameColor(int ability) => ability switch
    {
        >= 160 => "#7FD1FF",   // ice
        >= 135 => "#E3B341",   // gold
        >= 110 => "#C0C7D0",   // silver
        _ => "#B87333",        // bronze
    };

    /// <summary>"POT 158" or "POT 158 · at his ceiling" for the front's bottom line.</summary>
    public static string PotentialLine(Player p, double knowledge)
    {
        var pa = Scouting.Potential(p, knowledge); var ca = Scouting.Ability(p, knowledge);
        return pa.High <= ca.High ? $"POT {pa} · at his ceiling" : $"POT {pa}";
    }

    /// <summary>A coloured chip on the back: text and a kind ("bad", "warn", "good" or "").</summary>
    public sealed record Chip(string Text, string Kind);

    /// <summary>Everything the back shows.</summary>
    public sealed record Back(
        string StatsLabel, double? Ppg, double? Rpg, double? Apg,
        IReadOnlyList<(string Name, int Value)> Strengths,
        string Career, string Honours, IReadOnlyList<Chip> Chips);

    /// <summary>The back of a player's card in this session.</summary>
    public static Back BackOf(GameSession s, Player p)
    {
        double k = Scouting.Knowledge(s.HumanTeam, p);
        string label; double? ppg = null, rpg = null, apg = null;
        var careerTotals = s.League.Careers.GetValueOrDefault(p.Id);
        if (s.Stats.TryGetValue(p.Id, out var cur) && cur.Games > 0)
        {
            label = $"{s.League.Season}-{(s.League.Season + 1) % 100:00} · {cur.Games} games";
            ppg = cur.Ppg; rpg = cur.Rpg; apg = cur.Apg;
        }
        else if (careerTotals is { Games: > 0 } c0)
        {
            label = $"Career · {c0.Games} games";
            ppg = c0.Ppg; rpg = c0.Rpg; apg = c0.Apg;
        }
        else label = "No games yet";

        string career = careerTotals is { Games: > 0 } c
            ? $"{c.Seasons} season{(c.Seasons == 1 ? "" : "s")} · {c.Games} games · {c.Ppg:0.0} ppg"
            : p.RookieSeason is { } rs && rs >= s.League.Season ? $"Rookie season {rs}-{(rs + 1) % 100:00}" : "Career totals count from this season";
        var h = new List<string>();
        if (careerTotals != null)
            foreach (var (n, name) in new[] { (careerTotals.Titles, "title"), (careerTotals.Mvps, "MVP"), (careerTotals.FinalsMvps, "Finals MVP"),
                         (careerTotals.AllLeague, "All-League"), (careerTotals.AllStars, "All-Star"), (careerTotals.Dpoys, "DPOY"), (careerTotals.Roys, "ROY"), (careerTotals.SixthMen, "Sixth Man") })
                if (n > 0) h.Add(n == 1 ? name : $"{n}× {name}");
        if (s.League.HallOfFame.FirstOrDefault(x => x.PlayerId == p.Id) is { } hof) h.Add($"Hall of Fame {hof.Year}");
        if (p.Draft is { } d) h.Add($"Drafted {d.Year}, No. {d.Pick}");
        string honours = h.Count == 0 ? "No honours yet" : string.Join(" · ", h.Take(4));

        var chips = new List<Chip>();
        if (p.Injury is { } inj && inj.IsOut(s.Today)) chips.Add(new($"Out {inj.DaysOut(s.Today)} days", "bad"));
        else chips.Add(new("Healthy", "good"));
        if (p.TeamId != null)
        {
            var mood = People.PeopleEngine.Mood(p.Morale);
            chips.Add(new(p.TradeRequested ? "Wants a trade" : mood, p.TradeRequested || p.Morale < 35 ? "bad" : p.Morale < 50 ? "warn" : "good"));
        }
        if (p.Contract is { } con)
            chips.Add(new($"{Money(con.Salary)} · {(con.Years <= 0 ? "expiring" : $"{con.Years} yr{(con.Years == 1 ? "" : "s")}")}", con.Years <= 0 ? "warn" : ""));
        else chips.Add(new("Free agent", "warn"));

        return new Back(label, ppg, rpg, apg, Strengths(p, k), career, honours, chips);
    }

    /// <summary>$39.6M, $850K.</summary>
    public static string Money(long v) => Math.Abs(v) >= 1_000_000 ? $"${v / 1e6:0.0#}M" : $"${v / 1e3:0}K";
}
