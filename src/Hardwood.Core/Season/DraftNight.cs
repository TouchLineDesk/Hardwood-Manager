using Hardwood.Core.Model;

namespace Hardwood.Core.Season;

/// <summary>A club on the lottery's odds board: its chance, and its pick once its envelope is open.</summary>
public sealed record LotteryClub(int TeamId, string Name, string Odds, int? Pick, bool Mine);

/// <summary>
/// One beat of draft night (B3, run 142): the lottery's open, an envelope opening into a club's crest, the order set;
/// then the draft's open, a pick (the club, the graduate, his school and the staff's read), the club on the clock, and
/// the close. <see cref="Pick"/> is 1-based (0 for the opens and closes). <see cref="Announce"/> is the podium's call,
/// <see cref="Line"/> the speech bubble's.
/// </summary>
public sealed record DraftBeat(
    string Kind, int Pick, Team? Team, Player? Player, string School, string Range, string Announce,
    StudioAnalyst Speaker, string Line, bool MyClub, bool MyGraduate)
{
    public bool IsLottery => Kind is DraftNight.LotteryOpen or DraftNight.Envelope or DraftNight.LotteryClose;
    public bool IsPick => Kind == DraftNight.PickIn;
}

/// <summary>
/// Draft night on the HSN stage (B3, broadcast-presentation-design.md): first the lottery, revealed envelope by envelope
/// from the last pick to the first (the third-place tournament's extra pick, which isn't drawn, opens first); then the
/// draft, pick by pick, with a line from the desk on each. It only reads the summer: the order is the one
/// <see cref="CollegeDraft.Lottery"/> drew and the picks are the ones made, and nothing here draws a random number.
/// </summary>
public static class DraftNight
{
    public const string LotteryOpen = "LotteryOpen", Envelope = "Envelope", LotteryClose = "LotteryClose";
    public const string DraftOpen = "DraftOpen", PickIn = "Pick", Clock = "Clock", DraftClose = "DraftClose";

    /// <summary>The league's draft official at the podium (invented, like the HSN desk).</summary>
    public static readonly StudioAnalyst Official = new("official", "Walter Brandt", "Draft official", "Has read out every pick since the open market began.", "#F0A33A");
    public static StudioAnalyst Host => StudioShow.HostDesk;
    public static StudioAnalyst Numbers => StudioShow.Numbers;

    /// <summary>How many lottery picks the stage's four representatives stand for (picks 1–4).</summary>
    public const int OnStage = 4;

    private static string Ordinal(int n) => n % 100 is 11 or 12 or 13 ? $"{n}th" : (n % 10) switch { 1 => $"{n}st", 2 => $"{n}nd", 3 => $"{n}rd", _ => $"{n}th" };

    /// <summary>The draw's clubs (every eligible club but the extra pick's), and the extra pick's holder if any.</summary>
    private static (List<Team> Drawn, Team? Extra) Draw(Offseason o)
    {
        var owners = o.DraftOrder.Count > 0 ? o.DraftOrder : o.PickOwners;
        // The third-place tournament's winner is in the draw like everyone else and also holds the last pick
        // (CollegeDraft.Lottery shuffles distinct clubs and appends it). Read from the order itself: a loaded summer's
        // outcome doesn't carry the third-place winner (review, run 142).
        var extra = owners.Count > 1 && owners.Take(owners.Count - 1).Contains(owners[^1]) ? owners[^1] : null;
        return (extra == null ? owners.ToList() : owners.Take(owners.Count - 1).ToList(), extra);
    }

    /// <summary>"1 in 19": every drawn club's chance at the first pick (Danny's equal odds).</summary>
    public static string Odds(Offseason o) => $"1 in {Math.Max(1, Draw(o).Drawn.Count)}";

    /// <summary>The odds board after <paramref name="shown"/> lottery beats: the clubs by name, each with its pick once revealed.</summary>
    public static List<LotteryClub> Board(Offseason o, IReadOnlyList<DraftBeat> beats, int shown, int? mine)
    {
        // A club can hold two picks (the extra pick's holder is in the draw too): the board shows its best one.
        var revealed = beats.Take(shown).Where(b => b.Kind == Envelope && b.Team != null).GroupBy(b => b.Team!.Id).ToDictionary(g => g.Key, g => g.Min(b => b.Pick));
        var (drawn, extra) = Draw(o);
        string odds = Odds(o);
        return drawn.Distinct().OrderBy(t => t.FullName, StringComparer.Ordinal)
            .Select(t => new LotteryClub(t.Id, t.FullName, t == extra ? odds + " + last" : odds, revealed.TryGetValue(t.Id, out var k) ? k : null, t.Id == mine)).ToList();
    }

    /// <summary>The lottery show. Empty when there is no college draft this summer.</summary>
    public static List<DraftBeat> Lottery(Offseason o, int? mine, IReadOnlyCollection<int>? myGraduates = null, Func<Player, string>? name = null)
    {
        var beats = new List<DraftBeat>();
        var order = o.DraftOrder.Count > 0 ? o.DraftOrder : o.PickOwners;
        if (!o.CollegeDraftOn || order.Count == 0) return beats;
        var (drawn, extra) = Draw(o);
        int n = order.Count;
        beats.Add(new DraftBeat(LotteryOpen, 0, null, null, "", "", "DRAFT LOTTERY", Official,
            $"Good evening. {drawn.Count} clubs are in tonight's lottery, and every one has the same chance at the first pick: {Odds(o)}."
            + (extra != null ? $" {extra.TheFullNameCap} also hold the extra pick, the last of the night." : "")
            + $" One round, {n} picks.", false, false));
        if (extra != null)
            beats.Add(new DraftBeat(Envelope, n, extra, null, "", "", $"PICK {n}", Official,
                $"The extra pick, number {n}, belongs to {extra.TheFullName} for winning the third-place tournament. They're in the draw for the rest too." + (extra.Id == mine ? " That's you." : ""), extra.Id == mine, false));
        for (int k = drawn.Count; k >= 1; k--)
        {
            var t = order[k - 1];
            bool me = t.Id == mine;
            string line = k == 1 ? $"And the first pick in the draft goes to … {t.TheFullName}!"
                : k <= OnStage ? $"The {Ordinal(k)} pick in the draft goes to {t.TheFullName}."
                : $"Pick {k}: {t.TheFullName}.";
            beats.Add(new DraftBeat(Envelope, k, t, null, "", "", $"PICK {k}", Official, line + (me ? " That's you." : ""), me, false));
        }
        var first = order[0];
        string close = $"The order is set: {first.TheFullName} pick first, then {order.Skip(1).Take(2).Select(t => t.FullName).DefaultIfEmpty("nobody").Aggregate((a, b) => a + " and " + b)}.";
        var mineOnBoard = myGraduates == null ? new List<Player>() : o.Prospects.Concat(o.Report.Draft.Select(d => d.Player)).Where(p => myGraduates.Contains(p.Id)).Distinct().ToList();
        if (mineOnBoard.Count > 0)
            close += mineOnBoard.Count == 1 ? $" One of your seniors is in the pool tonight: {N(mineOnBoard[0], name)}."
                : $" {mineOnBoard.Count} of your seniors are in the pool tonight: {string.Join(", ", mineOnBoard.Select(p => N(p, name)))}.";
        else if (mine != null && order.All(t => t.Id != mine)) close += " You have no pick this year.";
        else if (mine != null) close += $" You pick {string.Join(" and ", Enumerable.Range(0, n).Where(i => order[i].Id == mine).Select(i => Ordinal(i + 1)))}.";
        beats.Add(new DraftBeat(LotteryClose, 0, null, null, "", "", "THE ORDER IS SET", Host, close, false, false));
        return beats;
    }

    private static string N(Player p, Func<Player, string>? name) => name?.Invoke(p) ?? p.Name;

    /// <summary>
    /// The draft broadcast: the picks made so far, then the club on the clock (while the draft is still going), or the
    /// close. <paramref name="school"/> names a graduate's program; <paramref name="range"/> is the viewer's read on his
    /// potential (a range in a college career, the pros' number otherwise); <paramref name="myGraduates"/> are the coached
    /// program's seniors of this summer, flagged on their picks.
    /// </summary>
    /// <param name="myClass">The coached program's seniors of this summer as players, drafted or not (they leave the
    /// draft's pool when free agency opens).</param>
    /// <param name="classSize">How many college players graduated this summer (for the close's undrafted count).</param>
    public static List<DraftBeat> Draft(Offseason o, int? mine, Func<Player, string> school, Func<Player, Scouting.Range> range,
        IReadOnlyCollection<int>? myGraduates = null, IReadOnlyCollection<Player>? myClass = null, int? classSize = null)
    {
        var beats = new List<DraftBeat>();
        if (!o.CollegeDraftOn || o.PickOwners.Count == 0) return beats;
        var picks = o.Report.Draft.OrderBy(d => d.Pick).ToList();
        if (picks.Count == 0 && o.Phase != OffseasonPhase.Draft) return beats;
        bool Grad(Player p) => myGraduates != null && myGraduates.Contains(p.Id);
        // The pros' board as the viewer reads it: every graduate in the pool, best read of potential first.
        var pool = picks.Select(d => d.Player).Concat(o.Prospects).Distinct()
            .OrderByDescending(p => range(p).Mid).ThenByDescending(p => p.CurrentAbility).ThenBy(p => p.Id).ToList();
        int Rank(Player p) => pool.IndexOf(p) + 1;
        var ours = (myClass ?? (IReadOnlyCollection<Player>)pool.Where(Grad).ToList()).OrderByDescending(p => range(p).Mid).ToList();
        beats.Add(new DraftBeat(DraftOpen, 0, null, null, "", "", "DRAFT NIGHT", Host,
            $"Welcome to draft night: one round, {o.PickOwners.Count} picks, and the best {pool.Count} college graduates in the country."
            + (ours.Count == 1 ? $" One of yours is waiting: {ours[0].Name}." : ours.Count > 1 ? $" {ours.Count} of yours are waiting: {string.Join(", ", ours.Select(p => p.Name))}." : ""),
            false, false));
        foreach (var d in picks)
        {
            var p = d.Player;
            string sch = school(p);
            var r = range(p);
            int rank = Rank(p);
            bool grad = Grad(p), myClub = d.Team.Id == mine;
            string announce = $"With the {Ordinal(d.Pick)} pick in the draft, {d.Team.TheFullName} select";
            string first = p.Name.Split(' ')[^1];
            StudioAnalyst who; string line;
            if (grad) { who = Host; line = $"One of yours, Coach. {first} leaves {sch} as the {Ordinal(d.Pick)} pick, and recruits will hear about it."; }
            else if (myClub) { who = Host; line = $"That's your pick: {p.Name}, {Pos(p)} out of {sch}."; }
            else if (rank == 1) { who = Numbers; line = d.Pick == 1 ? $"The best graduate on our board goes first. No surprise." : $"The best graduate on our board, and he lasted to {Ordinal(d.Pick)}. {d.Team.FullName} will be thrilled."; }
            else if (rank <= d.Pick - 4) { who = Numbers; line = $"A steal at {d.Pick}: our board had {first} {Ordinal(rank)}."; }
            else if (rank >= d.Pick + 5) { who = Numbers; line = $"A reach: our board had {first} {Ordinal(rank)}. {d.Team.FullName} see something we don't."; }
            else { who = Host; line = $"{Pos(p, true)} out of {sch}; the staff's read on his potential is {r}."; }
            beats.Add(new DraftBeat(PickIn, d.Pick, d.Team, p, sch, r.ToString(), announce, who, line, myClub, grad));
        }
        if (!o.DraftComplete && o.OnTheClock is { } clock)
        {
            int k = o.NextPickIndex + 1;
            bool me = clock.Id == mine;
            beats.Add(new DraftBeat(Clock, k, clock, null, "", "", "ON THE CLOCK", Official,
                me ? $"You're on the clock with pick {k}. Choose on the Draft tab, or press Continue and your scouts pick." : $"{clock.TheFullNameCap} are on the clock with pick {k}.", me, false));
            return beats;
        }
        // Once free agency opens the undrafted are back in the pool, so the class's size counts them.
        int undrafted = classSize is { } size ? Math.Max(0, size - picks.Count) : o.Prospects.Count;
        var picked = picks.Where(d => Grad(d.Player)).ToList();
        string close = $"That's the draft: {picks.Count} picks" + (undrafted > 0 ? $". The other {undrafted:N0} graduates of the class go undrafted into free agency." : ".");
        if (myGraduates != null && ours.Count > 0)
            close += picked.Count == 0 ? $" None of your {ours.Count} seniors was picked; they're free agents, and any club can still sign them."
                : picked.Count == 1 ? (ours.Count == 1 ? $" Your senior went: {picked[0].Player.Name}, {Ordinal(picked[0].Pick)}." : $" 1 of your {ours.Count} seniors went: {picked[0].Player.Name}, {Ordinal(picked[0].Pick)}.")
                : $" {picked.Count} of your {ours.Count} seniors went, the first {Ordinal(picked[0].Pick)}.";
        beats.Add(new DraftBeat(DraftClose, 0, null, null, "", "", "THE DRAFT IS DONE", Host, close, false, false));
        return beats;
    }

    private static string Pos(Player p, bool cap = false)
    {
        string s = p.PrimaryPosition switch
        {
            Position.PG => "a point guard", Position.SG => "a shooting guard", Position.SF => "a small forward",
            Position.PF => "a power forward", Position.C => "a center", _ => "a player",
        };
        return cap ? char.ToUpperInvariant(s[0]) + s[1..] : s;
    }
}
