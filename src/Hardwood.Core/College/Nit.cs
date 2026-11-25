namespace Hardwood.Core.College;

/// <summary>A program's record by quadrant (run 193): Quad 1 the best wins (and the most forgivable losses), Quad 4 the worst.</summary>
public sealed record QuadRecord(int[] Wins, int[] Losses)
{
    /// <summary>"3-4" for quadrant 1-4.</summary>
    public string Of(int quad) => $"{Wins[quad - 1]}-{Losses[quad - 1]}";
    /// <summary>"Q1 3-4 · Q2 5-1 · Q3 6-0 · Q4 9-0".</summary>
    public string Line => string.Join(" · ", Enumerable.Range(1, 4).Select(q => $"Q{q} {Of(q)}"));
    /// <summary>Losses in quadrants 3 and 4: the "bad losses" a committee holds against a bubble team.</summary>
    public int BadLosses => Losses[2] + Losses[3];
}

/// <summary>
/// Slice 3 of the revised launch plan (#1164, run 193): the NET, the quadrants and the NIT.
/// <list type="bullet">
/// <item>The NET is the committee's own rating, the one <see cref="Bracketology"/> picks the field by (the rating with half
/// its conference's strength, in a real world), ranked 1 to 365.</item>
/// <item>The quadrants sort every game by the opponent's NET rank and the floor, as the real committee does: Quad 1 is a
/// home game against the top 30, a neutral one against the top 50 or a road game against the top 75.</item>
/// <item>The NIT: the 32 best teams left out of March Madness (by NET, winning records first) on Selection Sunday. The
/// better seed hosts the first three rounds; the semi-finals and the final are in Indianapolis. Its games are played on
/// their own random stream (a hash of the season and the game), so March Madness and the rest of the world draw exactly
/// what they drew before.</item>
/// </list>
/// </summary>
public sealed partial class CollegeWorld
{
    public const string Nit = "NIT";
    public const int NitSize = 32;
    /// <summary>The NIT's semi-finals and final.</summary>
    public const string NitFinalSite = "Hinkle Fieldhouse, Indianapolis";

    private long _netCachedVersion = -1;
    private CollegeSeason? _netCachedSeason;
    private Dictionary<int, int> _netRanks = new();

    /// <summary>The committee's rating for every program (see <see cref="Project"/>): the rating, plus half the gap between
    /// its conference's average and the whole division's in a real world.</summary>
    public Func<int, double> SelectionRating()
    {
        var elo = Season.Elo;
        var confElo = IsReal ? Programs.GroupBy(t => ConferenceOf[t.Id]).ToDictionary(g => g.Key, g => g.Average(t => elo.GetValueOrDefault(t.Id, 1500))) : null;
        double avg = confElo == null ? 1500 : Programs.Average(t => elo.GetValueOrDefault(t.Id, 1500));
        return id => elo.GetValueOrDefault(id, 1500) + (confElo == null ? 0 : ScheduleWeight * (confElo[ConferenceOf[id]] - avg));
    }

    /// <summary>Every program's NET rank (1 = best), re-ranked after every game.</summary>
    public IReadOnlyDictionary<int, int> NetRanks()
    {
        if (_netCachedVersion == _pollVersion && ReferenceEquals(_netCachedSeason, Season) && _netRanks.Count > 0) return _netRanks;
        var rating = SelectionRating();
        _netRanks = Programs.Select(t => t.Id).OrderByDescending(rating).ThenBy(id => id).Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i + 1);
        _netCachedVersion = _pollVersion; _netCachedSeason = Season;
        return _netRanks;
    }

    /// <summary>A program's NET rank (1 = best), or 0 when it isn't a college program.</summary>
    public int NetRank(int id) => NetRanks().TryGetValue(id, out var r) ? r : 0;

    /// <summary>Where a game is played for a side: 'H'ome, 'A'way or 'N'eutral.</summary>
    public static char Venue(CollegeGame g, int side) => g.IsNeutral() ? 'N' : g.HomeId == side ? 'H' : 'A';

    /// <summary>The quadrant (1-4) of a game against an opponent with this NET rank on this floor (the real committee's lines).</summary>
    public static int Quad(int oppNet, char venue) => venue switch
    {
        'H' => oppNet <= 30 ? 1 : oppNet <= 75 ? 2 : oppNet <= 160 ? 3 : 4,
        'N' => oppNet <= 50 ? 1 : oppNet <= 100 ? 2 : oppNet <= 200 ? 3 : 4,
        _ => oppNet <= 75 ? 1 : oppNet <= 135 ? 2 : oppNet <= 240 ? 3 : 4,
    };

    /// <summary>A program's record by quadrant, on today's NET (the quadrant of an old game moves as its opponent's rank does).</summary>
    public QuadRecord Quads(int id)
    {
        var net = NetRanks();
        int[] w = new int[4], l = new int[4];
        foreach (var g in Season.Games)
        {
            if (!g.Played || (g.HomeId != id && g.AwayId != id)) continue;
            int opp = g.HomeId == id ? g.AwayId : g.HomeId;
            if (!net.TryGetValue(opp, out var r)) continue;
            int q = Quad(r, Venue(g, id)) - 1;
            if (g.WinnerId == id) w[q]++; else l[q]++;
        }
        return new QuadRecord(w, l);
    }

    /// <summary>"NET 44 · Q1 3-6": a bubble team's résumé in one line.</summary>
    public string Resume(int id) => $"NET {NetRank(id)} · Q1 {Quads(id).Of(1)}";

    // ------------------------------------------------------------------------------------------
    // The NIT

    /// <summary>The NIT's dates: the first round (two days), the second round, the quarter-finals, the semi-finals and the final.</summary>
    public static List<DateOnly> NitDates(int seasonYear)
    {
        int y = seasonYear + 1;
        return new List<DateOnly> { new(y, 3, 18), new(y, 3, 22), new(y, 3, 25), new(y, 4, 1), new(y, 4, 3) };
    }

    /// <summary>"First round", "Second round", "Quarter-finals", "Semi-finals", "Final".</summary>
    public static string NitRoundName(int round) => round switch
    {
        0 => "First round", 1 => "Second round", 2 => "Quarter-finals", 3 => "Semi-finals", _ => "Final",
    };

    /// <summary>A program's NIT seed (1-8 in one of four brackets of eight; 0 = not in the NIT).</summary>
    public int NitSeedOf(int id) => Season.NitField.IndexOf(id) is var i and >= 0 ? i / 4 + 1 : 0;

    /// <summary>Whether a program is in this season's NIT.</summary>
    public bool InNit(int id) => Season.NitField.Contains(id);

    /// <summary>The NIT field on Selection Sunday: the 32 best by NET of the teams left out (winning records first).</summary>
    private void SelectNit(BracketProjection p)
    {
        var s = Season;
        var net = NetRanks();
        bool Winning(int id) { var (w, l) = Record(id); return w > l; }
        var pool = Programs.Select(t => t.Id).Where(id => !p.In(id)).OrderByDescending(Winning).ThenBy(id => net[id]).Take(NitSize).ToList();
        if (pool.Count < NitSize) return;
        s.NitField = pool;
        var dates = NitDates(s.Year);
        var bracket = Seeded(pool);
        s.RoundDates[Nit] = dates;
        // The first round over two days (the top half first), the better seed at home.
        for (int i = 0; i < bracket.Count; i += 2)
            s.Games.Add(new CollegeGame { Date = dates[0].AddDays(i < 16 ? 0 : 1), HomeId = bracket[i], AwayId = bracket[i + 1], Stage = Nit, Event = Nit, Round = 0, Slot = i / 2 });
        var ones = pool.Take(4).Select(id => Program(id)!.FullName);
        News.Add(("The NIT field is set", $"The 32 best teams left out of March Madness play the NIT. The No. 1 seeds: {string.Join(", ", ones)}. "
            + $"The better seed hosts the first three rounds; the semi-finals and the final are at {NitFinalSite} ({dates[3]:MMMM d} and {dates[4]:MMMM d})."));
    }

    /// <summary>The NIT's next round: the better seed hosts the second round and the quarter-finals; the semi-finals and the final are neutral.</summary>
    private void NitHost(CollegeGame g)
    {
        if (g.Round >= 3) return;
        var field = Season.NitField;
        int h = field.IndexOf(g.HomeId), a = field.IndexOf(g.AwayId);
        if (a >= 0 && (h < 0 || a < h)) (g.HomeId, g.AwayId) = (g.AwayId, g.HomeId);
    }

    /// <summary>An NIT game's own random stream: a hash of the season and the game, so the NIT never moves anyone else's dice.</summary>
    private static Rng NitRng(int year, CollegeGame g)
    {
        ulong x = 0xC0FFEE193UL;
        foreach (int v in new[] { year, g.Round, g.Slot, g.HomeId, g.AwayId }) x = (x ^ (ulong)(uint)v) * 0x100000001B3UL;
        return new Rng(x);
    }

    /// <summary>The NIT's news: the champion.</summary>
    private void NitCrowned(CollegeGame final)
    {
        var w = Program(final.WinnerId!.Value)!;
        int l = final.HomeId == w.Id ? final.AwayId : final.HomeId;
        int wp = Math.Max(final.HomePts!.Value, final.AwayPts!.Value), lp = Math.Min(final.HomePts!.Value, final.AwayPts!.Value);
        News.Add(($"{w.FullName} win the NIT", $"{w.FullName} beat {Program(l)!.FullName} {wp}-{lp} in the NIT final at {NitFinalSite}."));
        if (ManagedProgramId == w.Id)
            CoachNews.Add(("We win the NIT", $"{w.FullName} are NIT champions: {wp}-{lp} over {Program(l)!.FullName} at {NitFinalSite}. Not the tournament we wanted in March, but a trophy, and five more games for the young players."));
    }

    /// <summary>Selection Sunday for a coached program left out of March Madness but picked for the NIT.</summary>
    private string NitInvite(int me)
    {
        var s = Season;
        int seed = NitSeedOf(me);
        var first = s.Games.FirstOrDefault(g => g.Event == Nit && g.Round == 0 && (g.HomeId == me || g.AwayId == me));
        if (first == null) return "";
        int opp = first.HomeId == me ? first.AwayId : first.HomeId;
        string where = first.HomeId == me ? "at home" : "on the road";
        return $"\nThe NIT has picked us instead: a No. {seed} seed. First round, {first.Date:ddd MMM d}, {where} against No. {NitSeedOf(opp)} {Program(opp)!.FullName}.";
    }
}
