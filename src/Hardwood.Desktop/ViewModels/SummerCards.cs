using System.Globalization;
using Avalonia.Media;
using Hardwood.Core.Model;
using Hardwood.Core.Season;

namespace Hardwood.Desktop.ViewModels;

/// <summary>One skill group on a player card as a 1–20 bar: Sho, Ply, Def, Phy (run 67). <see cref="Text"/> is what
/// your scouts report (a range while they don't know him well); <see cref="Value"/> is its middle, for the bar.</summary>
public sealed record SkillBar(string Label, double Value, string Text, IBrush Brush, string Tip);

/// <summary>Run 67 (review #13 rank 6, Danny's #464): the summer boards as player cards. Contracts up, free agents
/// and the draft board share these pieces: the skill groups as bars, the ask as a chip and whether it fits your budget.</summary>
public static class SummerCards
{
    /// <summary>How many free agents and graduates show as cards; the full table lists everyone.</summary>
    public const int FreeAgentCount = 24;
    public const int DraftCount = 30;

    public static readonly IBrush Neutral = StaffCards.Neutral;
    private static readonly IBrush Grey = new SolidColorBrush(Color.Parse("#5A6573"));
    public static readonly IBrush GraduateBrush = new SolidColorBrush(Color.Parse("#7A4FB3"));

    private static readonly (AttrGroup Group, string Label, string Tip)[] Groups =
    {
        (AttrGroup.Shooting, "Sho", "Shooting"),
        (AttrGroup.Playmaking, "Ply", "Playmaking"),
        (AttrGroup.Defense, "Def", "Defense"),
        (AttrGroup.Physical, "Phy", "Physical"),
    };

    /// <summary>The four skill groups as bars, in a fixed order, coloured by band (15+ green, 8–14 blue, 7 and under red).</summary>
    public static IReadOnlyList<SkillBar> Bars(Player p, double knowledge) =>
        Groups.Select(g =>
        {
            var text = Scouting.GroupAverage(p, g.Group, knowledge);
            double mid = Mid(text);
            return new SkillBar(g.Label, mid, text, StaffCards.BandBrush((int)Math.Round(mid)),
                $"{g.Tip}: {text} of 20" + (text.Contains('-') ? " (your scouts' range; scout him to narrow it)" : ""));
        }).ToList();

    /// <summary>The middle of "12.5" or "11.0-14.0".</summary>
    public static double Mid(string text)
    {
        var parts = text.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var nums = parts.Select(x => double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0).ToList();
        return nums.Count == 0 ? 0 : nums.Average();
    }

    public static string Money(long v) => Math.Abs(v) >= 1_000_000 ? $"${v / 1e6:F2}M" : $"${v / 1e3:F0}K";

    /// <summary>The ask as a chip: "Asks $2.35M × 3".</summary>
    public static string AskChip(ContractDemand d) => $"Asks {Money(d.Wage)} × {d.Years}";

    /// <summary>Whether his ask fits the wage room you have: a green "Fits your budget" or a red "$1.20M over budget".</summary>
    public static (string Text, IBrush Brush, string Tip) Fit(long wage, long room) =>
        wage <= room
            ? ("Fits your budget", StandingRow.PlayoffBrush, $"His ask leaves {Money(room - wage)} of your wage budget")
            : ($"{Money(wage - Math.Max(0, room))} over budget", StandingRow.RelegationBrush,
               room > 0 ? $"You have {Money(room)} of wage budget; the owner won't let you go over it" : "You have no wage budget left; the owner won't let you go over it");

    /// <summary>Why you can't talk to him, as a chip: the whole reason is its hover line.</summary>
    public static string Blocked(string why) =>
        why.Contains("roster is full", StringComparison.OrdinalIgnoreCase) ? "Roster full"
        : why.Contains("aiming higher", StringComparison.OrdinalIgnoreCase) ? "Aims higher"
        : why.Contains("won't talk to you again", StringComparison.OrdinalIgnoreCase) ? "Talks over"
        : "Can't talk now";

    /// <summary>A free agent's card (both the summer's Free agents tab and Transactions → Free agents).</summary>
    public static FreeAgentRow FreeAgent(GameSession s, Player p, DateOnly date, bool graduate, long room)
    {
        var d = s.PeekDemand(p);
        var know = Scouting.Knowledge(s.HumanTeam, p);
        var why = s.CannotTalk(p);
        var fit = why != null ? (Blocked(why), Grey, why) : Fit(d.Wage, room);
        if (why == null && s.AskingFloorNote(p) is { } floor) fit = (fit.Item1, fit.Item2, floor + " " + fit.Item3);   // H61 (run 110)
        return new FreeAgentRow
        {
            Player = p, Age = p.Age(date), Knowledge = know, Ask = d, Graduate = graduate,
            FitText = fit.Item1, FitBrush = fit.Item2, FitTip = fit.Item3,
            Reach = why != null ? 2 : d.Wage <= room ? 0 : 1,
        };
    }

    /// <summary>
    /// H242 (run 236): the free agents shown as cards: those who'd talk and whose asks fit the wage room first, then those
    /// who'd talk but cost more, then the rest, each group best first (<paramref name="rows"/> come in ability order).
    /// A G League club with $0.8M to spend saw 24 stars asking $20M+, all "Aims higher", with Make offer greyed.
    /// </summary>
    /// <remarks>The review of #305: <paramref name="rows"/> is read lazily, only until <see cref="FreeAgentCount"/> rows that
    /// fit have been seen, so a pool's cheaper players further down are found without building a card for everyone.</remarks>
    public static IEnumerable<FreeAgentRow> WithinReach(IEnumerable<FreeAgentRow> rows)
    {
        var seen = new List<FreeAgentRow>();
        int fits = 0;
        foreach (var r in rows)
        {
            seen.Add(r);
            if (r.Reach == 0) fits++;
            if (fits >= FreeAgentCount) break;
        }
        return seen.Select((r, i) => (r, i)).OrderBy(x => x.r.Reach).ThenBy(x => x.i).Take(FreeAgentCount).Select(x => x.r).ToList();
    }

    /// <summary>H242: what the cards are, for the note above them.</summary>
    public static string CardsNote(IReadOnlyCollection<FreeAgentRow> cards, int all, long room)
    {
        int fits = cards.Count(c => c.Reach == 0);
        if (all <= cards.Count && cards.All(c => c.Reach == 0)) return $"All {all} free agent{(all == 1 ? "" : "s")}, best first, with what each asks of your club.";
        if (fits == cards.Count)
            return $"{cards.Count} free agents whose asks fit your wage room ({Money(Math.Max(0, room))}), best first, of {all:N0}.";
        int talk = cards.Count(c => c.Reach == 1);
        if (fits == 0 && talk == 0) return $"None of the best {cards.Count} of {all:N0} free agents will talk to you now (hover a card's chip for why).";
        return fits == 0
            ? $"Nobody of {all:N0} free agents asks within your wage room ({Money(Math.Max(0, room))}): the best {talk} who'd talk come first{(talk < cards.Count ? ", then those who won't" : "")}."
            : $"{fits} free agent{(fits == 1 ? "" : "s")} whose asks fit your wage room ({Money(Math.Max(0, room))}) come first, best first; then the best of the rest of {all:N0}.";
    }
}
