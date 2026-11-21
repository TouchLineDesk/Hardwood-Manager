using Hardwood.Core.Model;

namespace Hardwood.Core.College;

/// <summary>K15b (run 147): a starting-role promise made on the trail, kept with the player once he enrolls.</summary>
public sealed class RecruitPledge
{
    public int PlayerId { get; set; }
    /// <summary>The program that made it (it lapses if he, or you, are no longer there).</summary>
    public int ProgramId { get; set; }
    /// <summary>His freshman season (the year it starts: the summer he enrolls).</summary>
    public int Season { get; set; }
    /// <summary>"pending", "kept", "broken" or "lapsed".</summary>
    public string Status { get; set; } = "pending";
}

/// <summary>
/// K15b (run 147, critic review #18 item 7, K15): promises and decommitments.
/// <list type="bullet">
/// <item><b>A promise</b>: the coached program can promise a prospect a starting role as a freshman (1 coach hour, at most
/// <see cref="PromisesPerClass"/> a class). It adds <see cref="PromiseAppeal"/> to your appeal with him. Once he enrolls it
/// is checked in mid-January of his freshman season: he must start at least half his games or play <see cref="PromiseMpg"/>
/// minutes a game. A broken promise makes him unhappy (and slow to trust you), puts a line in the inbox, and the next class
/// hears about it: for a year your appeal is <see cref="BrokenWordPenalty"/> lower with every prospect and your promises
/// sway no one.</item>
/// <item><b>Decommitments</b> (CS6): a commitment is verbal until he signs (in the November early period, or on signing
/// day). Each Monday before then, a verbal commit whose school now trails another that can take him by
/// <see cref="FlipGap"/> or more may back out (<see cref="FlipChance"/> a week) and commit there instead: a rival's commit
/// can flip to you, and yours can flip away. The weekly roll is a hash of the player and the day, not the world's random
/// stream, so a week without a flip plays out exactly as before.</item>
/// </list>
/// </summary>
public sealed partial class CollegeWorld
{
    public const int PromisesPerClass = 2, PromiseHours = 1;
    public const double PromiseAppeal = 4, BrokenWordPenalty = 4, PromiseMpg = 25;
    /// <summary>How far ahead (appeal points, no luck) another school must be before a verbal commit thinks again.</summary>
    public const double FlipGap = 12;
    /// <summary>The weekly chance that a verbal commit so far behind backs out.</summary>
    public const double FlipChance = 0.08;
    /// <summary>Verbal commits ranked this high (or tied to the coached program) are reconsidered; the rest are depth.</summary>
    public const int ReconsiderTop = 400;
    /// <summary>A flip this high in the class makes the news.</summary>
    public const int FlipNewsRank = 25;

    /// <summary>The coached program's broken promise is known on the trail until this day (saved).</summary>
    public DateOnly? BrokenWordUntil { get; set; }
    /// <summary>Promises carried to college with the players they were made to (saved).</summary>
    public List<RecruitPledge> Pledges { get; } = new();

    /// <summary>Recruits have heard you broke your word (as of the recruiting step's day).</summary>
    public bool WordBroken => BrokenWordUntil is { } u && (RecruitingDay ?? DateOnly.MinValue) < u;
    /// <summary>What a broken promise costs the coached program with every prospect (0 or −<see cref="BrokenWordPenalty"/>).</summary>
    public double WordPenalty => WordBroken ? -BrokenWordPenalty : 0;
    /// <summary>The promise's weight in the coached program's appeal (0 without one, for anyone else, or while your word is broken).</summary>
    public double PromiseAppealOf(Recruit r, Team t) => r.Promised && r.CourtedBy == t.Id && !WordBroken ? PromiseAppeal : 0;

    /// <summary>The November early signing period for a class: verbal commitments made by its end sign then.</summary>
    public static DateOnly EarlySigningStart(int classOf) => new(classOf - 1, 11, 8);
    public static DateOnly EarlySigningEnd(int classOf) => new(classOf - 1, 11, 21);

    /// <summary>Whether his commitment binds today: made by the end of the early period and that period has begun, or signing day has come.</summary>
    public static bool Signed(Recruit r, DateOnly today) =>
        r.CommittedOn is { } on && (today >= SigningDay(r.ClassOf) || (on <= EarlySigningEnd(r.ClassOf) && today >= EarlySigningStart(r.ClassOf)));

    /// <summary>"Committed to Duke (verbal: he can still flip until he signs on Nov 8)" for the panel; "Signed with Duke" once binding.</summary>
    public string CommitmentText(Recruit r, DateOnly today, Team me)
    {
        if (r.CommittedTo is not { } to) return "";
        string name = to == me.Id ? "you" : Program(to)?.FullName ?? "another school";
        if (Signed(r, today)) return $"Signed with {name}.";
        var signs = r.CommittedOn is { } on && on <= EarlySigningEnd(r.ClassOf) ? EarlySigningStart(r.ClassOf) : SigningDay(r.ClassOf);
        return $"Committed to {name}, verbally: he can still flip until he signs on {signs:MMM d}.";
    }

    /// <summary>Starting-role promises the program has out in a class (to prospects not committed elsewhere).</summary>
    public int PromisesMade(Team t, int classOf) =>
        Recruits.Values.Count(r => r.ClassOf == classOf && r.CourtedBy == t.Id && r.Promised && (r.CommittedTo == null || r.CommittedTo == t.Id));

    /// <summary>Promise him a starting role as a freshman (1 coach hour). It can't be taken back.</summary>
    public string Promise(Team t, Player p, Recruit r, DateOnly today)
    {
        if (Closed(p, r, today) is { } c) return c;
        Courting(r, t);
        if (r.Promised) return $"You've already promised {p.Name} a starting role. A promise can't be taken back.";
        if (BrokenWordUntil is { } u && today < u)
            return $"Recruits have heard you broke a promise: until {u:MMMM d, yyyy} a promise won't sway {p.Name}.";
        if (PromisesMade(t, r.ClassOf) >= PromisesPerClass)
            return $"You've promised a starting role to {PromisesPerClass} of the Class of {r.ClassOf} already: there are only five places in a starting lineup.";
        if (Spend(PromiseHours, today) is { } no) return no;
        r.Promised = true; r.OnBoard = true;
        return $"You promised {p.Name} a starting role as a freshman: by mid-January of his first season he expects to start at least half his games "
             + $"or play {PromiseMpg:0} minutes a game. It adds {PromiseAppeal:0} to your appeal with him. Break it and he'll feel lied to, and the next class will hear about it.";
    }

    /// <summary>At enrollment: a promise made by the program he signed with goes with him to college.</summary>
    private void KeepPromisesOnEnrollment(List<Player> seniors, int cls)
    {
        Pledges.RemoveAll(x => x.Season < cls - 1);
        foreach (var p in seniors)
            if (Recruits[p.Id] is { Promised: true, CommittedTo: { } at } r && r.CourtedBy == at && !Pledges.Any(x => x.PlayerId == p.Id))
                Pledges.Add(new RecruitPledge { PlayerId = p.Id, ProgramId = at, Season = cls });
    }

    /// <summary>The weekly roll for a flip, from the player and the day (not the world's random stream).</summary>
    public static double FlipRoll(int playerId, DateOnly day)
    {
        ulong h = (ulong)(uint)playerId * 0x9E3779B97F4A7C15UL ^ (ulong)(uint)day.DayNumber * 0xC2B2AE3D27D4EB4FUL;
        h ^= h >> 31; h *= 0xBF58476D1CE4E5B9UL; h ^= h >> 29;
        return (h >> 11) / (double)(1UL << 53);
    }

    /// <summary>
    /// The Monday reconsideration (CS6): each verbal commit who rolls under <see cref="FlipChance"/> weighs his school again
    /// (his own place there doesn't count against its minutes) against every other that can take him; if one leads by
    /// <see cref="FlipGap"/> or more he backs out and commits there. Returns how many flipped.
    /// </summary>
    public int Reconsider(int cls, DateOnly day, ClassBoard board, Dictionary<int, int> ranks)
    {
        int flips = 0;
        foreach (var p in ClassOf(cls).ToList())
        {
            var r = Recruits[p.Id];
            if (r.CommittedTo is not { } at || Signed(r, day)) continue;
            bool ours = at == ManagedProgramId || (r.CourtedBy == ManagedProgramId && r.Offered);
            if (r.Rank > ReconsiderTop && !ours) continue;
            if (FlipRoll(p.Id, day) >= FlipChance) continue;
            var from = Program(at);
            if (from == null) continue;
            var key = (at, p.PrimaryPosition);
            board.Open[at]++;
            board.AtPosition[key] = board.AtPosition.GetValueOrDefault(key) - 1;
            double stay = Appeal(from, p, r, board, null, ranks);
            Team? best = null; double top = double.MinValue;
            foreach (var t in Programs)
            {
                if (t == from || !CouldTake(t, r, board, ranks)) continue;
                double a = Appeal(t, p, r, board, null, ranks);
                if (a > top) { top = a; best = t; }
            }
            if (best == null || top - stay < FlipGap)
            {
                board.Open[at]--;
                board.AtPosition[key] = board.AtPosition.GetValueOrDefault(key) + 1;
                continue;
            }
            r.CommittedTo = null; r.CommittedOn = null;
            Commit(p, r, best, day, board, flippedFrom: from);
            flips++;
        }
        Flips += flips;
        return flips;
    }

    /// <summary>Flips so far in this world (not saved; for the audits and tests).</summary>
    public int Flips { get; set; }
}
