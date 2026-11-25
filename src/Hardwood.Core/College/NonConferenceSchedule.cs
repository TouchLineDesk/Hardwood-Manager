using Hardwood.Core.Model;

namespace Hardwood.Core.College;

/// <summary>Slice 2 of the revised launch plan (run 192): how the coached program fills its non-conference dates.</summary>
public enum ScheduleMix
{
    /// <summary>The conference office fills the dates (the draw every program gets; the default).</summary>
    Balanced,
    /// <summary>Guarantee ("buy") games: low-majors are paid to come to your gym.</summary>
    BuyGames,
    /// <summary>Two-year home-and-home series with power programs: one game this season, the return next season.</summary>
    Tough,
}

/// <summary>Run 192: a two-year home-and-home series. The first game is in <see cref="FirstYear"/>, the return a season later.</summary>
public sealed record HomeAndHome(int OpponentId, int FirstYear, bool HomeFirst);

/// <summary>
/// Run 192: the coached program's non-conference plan, chosen on the Schedule page and drawn when next season's schedule is
/// made at the end of the summer. Kept with the program's books (<see cref="ProgramBooks.Schedule"/>), so it saves with them
/// and needs no format change; an older save has none and plays as before.
/// </summary>
public sealed class NonConferencePlan
{
    public ScheduleMix Mix { get; set; } = ScheduleMix.Balanced;
    /// <summary>The holiday tournament: null takes an invitation if one comes (as before), "" plays none, else the event's name.</summary>
    public string? Holiday { get; set; }
    /// <summary>Ask for a December showcase game against a top-30 program (neutral floor).</summary>
    public bool Showcase { get; set; }
    /// <summary>Series signed and not yet finished.</summary>
    public List<HomeAndHome> Series { get; set; } = new();
    /// <summary>The season the plan was last drawn into, and the mix it was drawn with (the AD's target reads these).</summary>
    public int DrawnYear { get; set; }
    public ScheduleMix DrawnMix { get; set; }
    /// <summary>Whether the showcase was asked for when <see cref="DrawnYear"/>'s schedule was drawn (a later tick waits a season).</summary>
    public bool DrawnShowcase { get; set; }
    /// <summary>What the draw booked in <see cref="DrawnYear"/> (for the inbox and the panel).</summary>
    public int BuyGamesBooked { get; set; }
    public int SeriesGamesBooked { get; set; }
    public string HolidayResult { get; set; } = "";
}

public sealed partial class CollegeWorld
{
    /// <summary>Run 192: buy games booked for a <see cref="ScheduleMix.BuyGames"/> plan, and the prestige under which a program sells one.</summary>
    public const int BuyGameCount = 5;
    public const double BuyGameMaxPrestige = 40;
    /// <summary>Run 192: new home-and-home series a <see cref="ScheduleMix.Tough"/> plan signs a season, and the prestige a partner needs.</summary>
    public const int NewSeriesCount = 3;
    public const double PowerPrestige = 62;
    /// <summary>Run 192: the three biggest holiday events invite only programs of this prestige (B) or more.</summary>
    public const double MajorHolidayPrestige = 62;
    public const int MajorHolidays = 3;
    /// <summary>Run 192: non-conference dates always left to the draw, so the plan never fills a whole slate.</summary>
    public const int DrawLeft = 2;

    /// <summary>The November holiday tournaments (Thanksgiving week), biggest first.</summary>
    public static IReadOnlyList<string> HolidayEvents => Invitationals;
    public static bool IsMajorHoliday(string name) => Array.IndexOf(Invitationals, name) is >= 0 and < MajorHolidays;

    /// <summary>Run 192: the coached program's plan (null: none chosen, the draw as before).</summary>
    public NonConferencePlan? PlanOf(int id) => Program(id)?.Money.Program.Schedule;

    /// <summary>Run 192: why the program can't enter a holiday event (null: it can).</summary>
    public string? HolidayBlocked(int id, string name) =>
        IsMajorHoliday(name) && Prestige.GetValueOrDefault(id, 50) < MajorHolidayPrestige
            ? $"The {name} invites programs of prestige {PrestigeLetter(MajorHolidayPrestige)} or better." : null;

    /// <summary>A stable mix of three numbers (no RNG: the plan's choices never move the world's stream).</summary>
    private static int Stir(int a, int b, int c)
    {
        unchecked
        {
            uint h = (uint)a * 73856093u ^ (uint)b * 19349663u ^ (uint)c * 83492791u;
            h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
            return (int)(h & 0x7fffffff);
        }
    }

    /// <summary>Run 192: the holiday choice changes who is invited, before the non-conference count is set. No random draws.</summary>
    private void ApplyHoliday(List<int> invited, int year)
    {
        if (ManagedProgramId is not { } me || PlanOf(me) is not { Holiday: { } choice } plan) return;
        int at = invited.IndexOf(me);
        if (choice.Length == 0)
        {
            plan.HolidayResult = "No holiday tournament";
            if (at < 0) return;
            var sub = Programs.Select(t => t.Id).Where(i => !invited.Contains(i) && i != me)
                .OrderByDescending(i => Prestige[i]).ThenBy(i => i).First();
            invited[at] = sub;
            return;
        }
        int e = Array.IndexOf(Invitationals, choice);
        if (e < 0 || HolidayBlocked(me, choice) != null) { plan.HolidayResult = at >= 0 ? $"Invited: {Invitationals[at / 8]}" : ""; return; }
        if (at / 8 == e && at >= 0) { plan.HolidayResult = choice; return; }
        // The bracket's smallest name gives up its place (or swaps brackets with us, if we were invited elsewhere).
        int slot = Enumerable.Range(e * 8, 8).OrderBy(k => Prestige[invited[k]]).ThenBy(k => invited[k]).First();
        if (at >= 0) (invited[at], invited[slot]) = (invited[slot], invited[at]);
        else invited[slot] = me;
        plan.HolidayResult = choice;
    }

    /// <summary>
    /// Run 192: the games the plan books before the draw: last season's home-and-home returns, then the new buy games or
    /// series. Opponents come from other conferences, owe games, and are picked by a stable hash of the year (no RNG).
    /// </summary>
    private List<(int Opp, bool Home)> PlannedGames(int me, int year, Dictionary<int, int> left)
    {
        var games = new List<(int Opp, bool Home)>();
        if (PlanOf(me) is not { } plan) return games;
        plan.Series.RemoveAll(s => s.FirstYear < year - 1 || Program(s.OpponentId) == null);
        int budget = left[me] - DrawLeft;
        var used = new HashSet<int> { me };
        foreach (var s in plan.Series.Where(s => s.FirstYear == year - 1).ToList())
        {
            if (budget <= 0 || left[s.OpponentId] <= 0 || ConferenceOf[s.OpponentId] == ConferenceOf[me]) { plan.Series.Remove(s); continue; }
            games.Add((s.OpponentId, !s.HomeFirst)); used.Add(s.OpponentId); budget--;
        }
        int returns = games.Count;
        IEnumerable<int> Candidates(Func<int, bool> fits) => Programs.Select(t => t.Id)
            .Where(i => !used.Contains(i) && ConferenceOf[i] != ConferenceOf[me] && left[i] > 0 && RivalryOf(me, i) == null && fits(i))
            .OrderBy(i => Stir(year, i, me));
        if (plan.Mix == ScheduleMix.BuyGames)
        {
            var low = Candidates(i => Prestige[i] < BuyGameMaxPrestige).Take(Math.Min(BuyGameCount, budget)).ToList();
            foreach (var i in low) { games.Add((i, true)); used.Add(i); }
            plan.BuyGamesBooked = low.Count;
        }
        else plan.BuyGamesBooked = 0;
        if (plan.Mix == ScheduleMix.Tough)
        {
            var big = Candidates(i => Prestige[i] >= PowerPrestige).Take(Math.Min(NewSeriesCount, Math.Max(0, budget - games.Count + returns))).ToList();
            foreach (var i in big)
            {
                bool homeFirst = Stir(year, me, i) % 2 == 0;
                games.Add((i, homeFirst)); used.Add(i);
                plan.Series.Add(new HomeAndHome(i, year, homeFirst));
            }
        }
        plan.SeriesGamesBooked = games.Count - plan.BuyGamesBooked;
        plan.DrawnYear = year; plan.DrawnMix = plan.Mix; plan.DrawnShowcase = plan.Showcase;
        return games;
    }

    /// <summary>Run 192: books the plan's games into the earliest rounds both programs are free (spread through November and
    /// December), in the same list the cross-conference rivals use.</summary>
    private void BookPlanned(int year, int ncRounds, Dictionary<int, int> owed, List<HashSet<int>> booked, List<(int Round, int Home, int Away)> fixedGames)
    {
        if (ManagedProgramId is not { } me || Program(me) == null || PlanOf(me) is not { } plan) return;
        plan.DrawnYear = year; plan.DrawnMix = plan.Mix; plan.DrawnShowcase = plan.Showcase;
        var left = owed.ToDictionary(kv => kv.Key, kv => kv.Value - fixedGames.Count(x => x.Home == kv.Key || x.Away == kv.Key));
        var games = PlannedGames(me, year, left);
        for (int n = 0; n < games.Count; n++)
        {
            var (opp, home) = games[n];
            int start = (int)(n * (ncRounds * 0.8) / Math.Max(1, games.Count));
            int r = -1;
            for (int k = 0; k < ncRounds && r < 0; k++)
            {
                int cand = (start + k) % ncRounds;
                if (cand >= booked.Count || !booked[cand].Contains(me) && !booked[cand].Contains(opp)) r = cand;
            }
            if (r < 0) continue;
            while (booked.Count <= r) booked.Add(new HashSet<int>());
            booked[r].Add(me); booked[r].Add(opp);
            fixedGames.Add((r, home ? me : opp, home ? opp : me));
        }
    }

    /// <summary>Run 192: the AD's target moves with the plan drawn this season: one more win for a slate of buy games, one fewer
    /// for a season of power series.</summary>
    public int ScheduleTargetShift(int id, int year) => PlanOf(id) is { } p && p.DrawnYear == year
        ? p.DrawnMix switch { ScheduleMix.BuyGames when p.BuyGamesBooked >= 3 => 1, ScheduleMix.Tough when p.SeriesGamesBooked >= 2 => -1, _ => 0 }
        : 0;

    /// <summary>Run 192: the AD's line on this season's schedule (null: nothing to say).</summary>
    public string? ScheduleTargetNote(int id, int year) => ScheduleTargetShift(id, year) switch
    {
        1 => $"The schedule has {PlanOf(id)!.BuyGamesBooked} buy games at home, so the AD expects one more win.",
        -1 => $"The schedule has {PlanOf(id)!.SeriesGamesBooked} games against power programs, so the AD allows one win for it.",
        _ => null,
    };

    /// <summary>
    /// Run 192: strength of schedule, the average rating of every regular-season opponent on the schedule (played or to come),
    /// ranked among all programs (1 = the hardest). Null before a schedule exists.
    /// </summary>
    public (int Rank, int Of, int NonConfRank)? ScheduleStrength(int id)
    {
        var s = Season;
        if (s.Games.Count == 0) return null;
        var all = new Dictionary<int, (double Sum, int N, double NcSum, int NcN)>();
        foreach (var g in s.Games)
        {
            if (!IsRegularSeason(g)) continue;
            Add(g.HomeId, g.AwayId, g.Stage != "Conference");
            Add(g.AwayId, g.HomeId, g.Stage != "Conference");
        }
        if (!all.ContainsKey(id)) return null;
        double Avg(int i) => all[i].N == 0 ? 0 : all[i].Sum / all[i].N;
        double NcAvg(int i) => all[i].NcN == 0 ? 0 : all[i].NcSum / all[i].NcN;
        int rank = 1 + all.Keys.Count(i => Avg(i) > Avg(id));
        int ncRank = 1 + all.Keys.Count(i => NcAvg(i) > NcAvg(id));
        return (rank, all.Count, ncRank);

        void Add(int a, int b, bool nonConf)
        {
            double e = s.Elo.GetValueOrDefault(b, 1500);
            var v = all.GetValueOrDefault(a);
            all[a] = (v.Sum + e, v.N + 1, v.NcSum + (nonConf ? e : 0), v.NcN + (nonConf ? 1 : 0));
        }
    }

    /// <summary>Run 192: this season's non-conference slate in one line: games, home / away / neutral, power and low-major opponents.</summary>
    public string NonConferenceSummary(int id)
    {
        var mine = Season.Games.Where(g => (g.HomeId == id || g.AwayId == id) && g.Stage is "Non-conference" or "Invitational" or "Showcase"
                                           && (g.Stage != "Invitational" || g.Round == 0)).ToList();
        if (mine.Count == 0) return "";
        bool Neutral(CollegeGame g) => g.Stage != "Non-conference";
        int home = mine.Count(g => !Neutral(g) && g.HomeId == id), away = mine.Count(g => !Neutral(g) && g.AwayId == id), neutral = mine.Count(Neutral);
        int Opp(CollegeGame g) => g.HomeId == id ? g.AwayId : g.HomeId;
        int power = mine.Count(g => Prestige.GetValueOrDefault(Opp(g), 50) >= PowerPrestige);
        int low = mine.Count(g => Prestige.GetValueOrDefault(Opp(g), 50) < BuyGameMaxPrestige);
        string ev = mine.FirstOrDefault(g => g.Stage == "Invitational")?.Event is { } inv ? $"; the {inv}" : "";
        return $"{mine.Count} non-conference games ({home} home, {away} away, {neutral} neutral): {power} against power programs, {low} against low-majors{ev}.";
    }
}
