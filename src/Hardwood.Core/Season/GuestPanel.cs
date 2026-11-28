using Hardwood.Core.Model;
using Hardwood.Core.People;

namespace Hardwood.Core.Season;

/// <summary>
/// Studio show part 2c (run 234, `danny-broadcast-nov27`; `claude/studio-show-design.md`): the guest chair. Each calendar
/// month a former player joins the pre-game desk beside the three regulars, with a voice set by his career (old school,
/// numbers, hype or contrarian), his own take on the night and his own tracked pick record.
/// The pool is the save's own retirees: generated players who retired with at least <see cref="MinSeasons"/> seasons and
/// <see cref="MinGames"/> games in this save, best Hall score first. Real players are never guests, so the show never puts
/// invented words in a real person's mouth (#1244). Until the save has enough retirees, invented former players fill the
/// chair (<see cref="Invented"/>), and they make way as career retirees arrive.
/// No random draw: who sits in the chair comes from a hash of the save's seed and the month, so nothing in the sim moves.
/// On the big nights (the playoffs, the Finals, the third-place tournament) Vern Haskell takes the chair, as before.
/// </summary>
public static class GuestPanel
{
    public const int MinSeasons = 5, MinGames = 250, PoolSize = 8, MinPool = 4;

    public const string OldSchool = "oldschool", NumbersVoice = "numbers", Hype = "hype", Contrarian = "contrarian";

    /// <summary>A guest's colour comes from his voice (never a team's), a shade apart from the regular of that voice.</summary>
    public static string ColourOf(string voice) => voice switch
    {
        OldSchool => "#E3B341",
        NumbersVoice => "#2EA8A0",
        Hype => "#FF7B72",
        _ => "#D2A8FF",
    };

    public static string VoiceName(string voice) => voice switch
    {
        OldSchool => "old school", NumbersVoice => "numbers", Hype => "hype", _ => "contrarian",
    };

    /// <summary>The voice his career gives him: a big who rebounded or blocked is old school; a playmaker reads the numbers;
    /// a scorer or All-Star is hype; everyone else argues the other side.</summary>
    public static string VoiceFor(Player p, CareerTotals c)
    {
        bool big = p.PrimaryPosition is Position.C or Position.PF;
        if (big && (c.Rpg >= 6.5 || c.Games > 0 && (double)c.Blocks / c.Games >= 1.2)) return OldSchool;
        if (c.Apg >= 5.5) return NumbersVoice;
        if (c.Ppg >= 17 || c.AllStars >= 2 || c.Mvps > 0) return Hype;
        return Contrarian;
    }

    /// <summary>The invented guests who fill the chair until the save has its own retirees (the test checks none is real).</summary>
    public static readonly StudioAnalyst[] Invented =
    {
        new("guest-vandermolen", "Curtis Vandermolen", "Guest · former forward, 12 seasons", "Twelve seasons as a stretch four. Says the thing nobody else at the desk will.", ColourOf(Contrarian), Contrarian),
        new("guest-kessling", "Andre Kessling", "Guest · former point guard, 9 seasons", "Nine seasons running offenses. Watches the spacing and the shot chart, not the highlights.", ColourOf(NumbersVoice), NumbersVoice),
        new("guest-albee", "Terrence Albee", "Guest · former wing, 10 seasons, 2 All-Star games", "A two-time All-Star scorer. Believes in stars, nerve and the big moment.", ColourOf(Hype), Hype),
        new("guest-oduya", "Hollis Oduya", "Guest · former center, 13 seasons", "Thirteen seasons on the block. Rebounding, defense and toughness, in that order.", ColourOf(OldSchool), OldSchool),
    };

    /// <summary>Can this retiree sit in the chair (generated, retired, a long enough career in this save)?</summary>
    public static bool CanGuest(League league, Player p)
    {
        if (!league.Careers.TryGetValue(p.Id, out var c) || c.Seasons < MinSeasons || c.Games < MinGames) return false;
        if (RealWorld.RealNames.IsRealPlayer(p.Name) || RealWorld.RealCareers.Players.ContainsKey(p.Name)) return false;
        return true;
    }

    /// <summary>A career retiree as a guest analyst.</summary>
    public static StudioAnalyst ForPlayer(Player p, CareerTotals c, Func<int, string?> teamName)
    {
        string voice = VoiceFor(p, c);
        string pos = p.PrimaryPosition switch { Position.PG => "point guard", Position.SG or Position.SF => "wing", Position.PF => "forward", _ => "center" };
        string role = $"Guest · former {pos}, {c.Seasons} seasons";
        if (c.Mvps > 0) role += $", {c.Mvps} MVP{(c.Mvps == 1 ? "" : "s")}";
        else if (c.AllStars > 0) role += $", {c.AllStars} All-Star game{(c.AllStars == 1 ? "" : "s")}";
        else if (c.Titles > 0) role += $", {c.Titles} title{(c.Titles == 1 ? "" : "s")}";
        string bio = $"{Lineage.CareerLine(c, teamName)}. Brings the {VoiceName(voice)} view to the desk.";
        return new StudioAnalyst($"guest-p{p.Id}", p.Name, role, bio, ColourOf(voice), voice);
    }

    /// <summary>The month's guest pool: the save's best retirees, topped up with invented guests to <see cref="MinPool"/>.</summary>
    public static List<StudioAnalyst> Pool(League league, Func<int, string?> teamName)
    {
        var mine = league.Retired.Where(p => CanGuest(league, p))
            .OrderByDescending(p => league.Careers[p.Id].HallScore).ThenBy(p => p.Id)
            .Take(PoolSize).Select(p => ForPlayer(p, league.Careers[p.Id], teamName)).ToList();
        foreach (var inv in Invented)
        {
            if (mine.Count >= MinPool) break;
            mine.Add(inv);
        }
        return mine;
    }

    /// <summary>Who sits in the chair in a month: the pool in an order shuffled by the seed, one guest a month in turn, so
    /// two months running never have the same guest when the pool has another, and everyone in the pool gets the call.</summary>
    public static StudioAnalyst ForMonth(IReadOnlyList<StudioAnalyst> pool, ulong seed, DateOnly date)
    {
        var order = pool.OrderBy(g => MediaCast.Hash($"{seed}:guest:{g.Key}")).ThenBy(g => g.Key, StringComparer.Ordinal).ToList();
        int month = date.Year * 12 + date.Month - 1;
        return order[(int)((ulong)month % (ulong)order.Count)];
    }

    /// <summary>The guest chair's segment: the host's introduction, the guest's take on tonight in his voice, and a regular
    /// pushing back. <paramref name="star"/> is the best player on the floor tonight (or null).</summary>
    public static StudioSegment Segment(StudioAnalyst guest, StudioShow show, Team fav, double favChance, string? star,
                                        int bigEdge, (Team team, int streak)? hot)
    {
        var host = show.Host;
        var dog = fav == show.Home ? show.Away : show.Home;
        var lines = new List<StudioLine>();
        string who = guest.Key.StartsWith("guest-p") ? guest.Bio.Split(". Brings")[0] : guest.Role.Replace("Guest · ", "");
        lines.Add(new(host, $"In the guest chair this month: {guest.Name}. {Cap(who)}."));
        int pct = (int)Math.Round(favChance * 100);
        switch (guest.Voice)
        {
            case OldSchool:
                var bigger = bigEdge >= 0 ? show.Home : show.Away;
                lines.Add(new(guest, $"Forget the threes. {bigger.Name} have the size tonight, and the team that owns the glass owns the night."));
                lines.Add(new(StudioShow.Guard, $"That's the 1995 answer. {(star != null ? $"{star} doesn't care who's bigger." : "This league is spacing now.")}"));
                break;
            case NumbersVoice:
                lines.Add(new(guest, pct >= 60
                    ? $"{fav.Name} at {pct}% is fair. Watch the second unit: that's where the gap is."
                    : $"{pct}% for {fav.Name} is close to a coin flip. Shot quality decides this, not the names."));
                lines.Add(new(StudioShow.BigMan, "Numbers don't box out. Somebody still has to want it more."));
                break;
            case Hype:
                lines.Add(new(guest, star != null
                    ? $"{star} is the best player on the floor and he knows it. Big night coming. Book it."
                    : hot is { } h ? $"{h.team.Name} have won {h.streak} straight. Ride the hot team." : $"{fav.Name}'s stars are better. That's the whole show."));
                lines.Add(new(StudioShow.Numbers, "One player doesn't beat a rotation for 48 minutes. The depth says otherwise."));
                break;
            default:
                lines.Add(new(guest, pct >= 75
                    ? $"Everybody likes {fav.Name}, and so do I. But it's closer than {pct}% says. {dog.Name} keep it within six."
                    : $"Everybody's on {fav.Name}. I'm not. {dog.Name} steal this one late."));
                lines.Add(new(StudioShow.Numbers, $"The model has {fav.Name} at {pct}%. Picking against that is a habit, not a take."));
                break;
        }
        return new StudioSegment("THE GUEST CHAIR", $"{guest.Name} joins the desk", lines);
    }

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}

public sealed partial class GameSession
{
    /// <summary>The guest in the studio chair on a date (run 234, 2c): read-only, from the save's retirees and the seed.</summary>
    public StudioAnalyst MonthGuest(DateOnly date) => GuestPanel.ForMonth(GuestPanel.Pool(League, TeamNameOf), Seed, date);
}
