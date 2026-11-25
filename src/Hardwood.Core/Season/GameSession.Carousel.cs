using Hardwood.Core.College;
using Hardwood.Core.Jobs;
using Hardwood.Core.Model;
using Hardwood.Core.World;

namespace Hardwood.Core.Season;

/// <summary>
/// One change on the college coaching carousel (CS9, run 191): who left a program, why, and who came in.
/// <see cref="Why"/> is "fired", "retired", "left" (for a bigger job, <see cref="ToProgramId"/>) or "you" (let go at the review,
/// or, with <see cref="ToProgramId"/>, you left for that job in the summer; a resignation isn't listed).
/// <see cref="FromProgramId"/> is where the new coach came from when another program's coach was hired away.
/// </summary>
public sealed record CarouselMove(int Year, int ProgramId, string Program, string Out, string In, string Why, string Record = "",
                                  int? FromProgramId = null, int? ToProgramId = null, bool OpenToYou = false, string? FromStaffOf = null);

/// <summary>
/// CS9, the coaching carousel (run 191; `claude/college-depth-plan.md`): college jobs open and close each spring with reasons.
/// Athletic directors who expected more make a change (the board's rule, as before); a few coaches retire; and a big job that
/// opens hires the best coach at a smaller program about half the time, which opens that job in turn (a chain, as in a real
/// spring). The changes are kept for four springs, posted as one "coaching carousel" story, and listed on the Board page.
///
/// Everything draws from the career path's own stream (<see cref="CareerRng"/>), so the games, recruiting and the session's
/// random numbers are untouched; only the computer coaches' names differ from before.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>The college coaching carousel, newest spring first (four springs kept).</summary>
    public List<CarouselMove> Carousel { get; } = new();

    /// <summary>Set while the season-end review that just sacked you runs the carousel (so a mid-season sacking isn't listed as one).</summary>
    private bool _sackedAtReview;

    /// <summary>A computer coach's chance to retire in a spring (about 4 of 365 a year).</summary>
    public const double CarouselRetireChance = 0.012;
    /// <summary>A program's size on the carousel: its prestige (0-100; B+ is about 70), so "a bigger job" means a bigger
    /// program, as the Board page and the recruits read it.</summary>
    public double CarouselSize(Team t) => World.College?.Prestige.GetValueOrDefault(t.Id, 50) ?? 50;
    /// <summary>A big job (B+ and up) on the carousel.</summary>
    public const double CarouselBigJob = 70;
    /// <summary>A vacancy hires another program's coach: at a big job, and elsewhere.</summary>
    public const double CarouselPoachBig = 0.6, CarouselPoachSmall = 0.3;
    /// <summary>A coach is hired away only by a job at least this much bigger than his.</summary>
    public const double CarouselStepUp = 6;
    /// <summary>The most hires from other programs in one spring (so one chain can't run on through the whole country).</summary>
    public const int CarouselMaxPoaches = 14;
    /// <summary>H202 (run 200): a season this far (in 82-game wins) below the target counts as a poor one.</summary>
    public const double CarouselPoorShortfall = 3;

    /// <summary>
    /// H202 (run 200): the chance an athletic director lets his coach go this spring. Never in a coach's first two seasons,
    /// never at .650 or better, nor at .600 or better with a tournament bid. Otherwise a small base chance (an AD's change of
    /// direction, only after a losing season) plus the shortfall against the target, which counts 60% after one poor
    /// season, 120% after two and 160% after three (impatient owners sooner).
    /// </summary>
    public static double CarouselFireChance(int seasonsHere, int wins, int losses, bool bid, double shortfall, int poorRun, int patience)
    {
        double pct = wins + losses == 0 ? 0.5 : wins / (double)(wins + losses);
        if (seasonsHere <= 2 || pct >= 0.65 || (pct >= 0.6 && bid)) return 0;
        double weight = poorRun switch { 0 => 0, 1 => 0.6, 2 => 1.2, _ => 1.6 };
        double p = (pct < 0.5 ? 0.02 : 0) + shortfall * 0.035 * weight * (1.3 - patience / 40.0);
        return Math.Clamp(p, 0, 0.75);
    }

    /// <summary>H203 (run 200): every computer coach in the college and abroad gets his own name (a new career, or an older
    /// save's first load, when the college book is empty), then the college benches get their past. Returns how many were renamed.</summary>
    public int UniqueBenches(ulong seed)
    {
        var fixedTeams = League.Teams.Append(HumanTeam);
        var renamable = World.Clubs.Concat(World.College?.Programs ?? new List<Team>()).Where(t => t != HumanTeam);
        int n = Board.UniqueBenchNames(fixedTeams, renamable, seed ^ 0xBE4C4);
        World.College?.EnsureBenches(seedPast: true);
        return n;
    }

    /// <summary>H203 (run 200): a new computer coach's name, never one already on a bench.</summary>
    internal string FreshCoachName(Rng rng)
    {
        var taken = AllBenchNames();
        return Board.NewCoachName(rng, taken.Contains);
    }

    /// <summary>Every head coach's name on a bench now (college and pro), for H203's uniqueness.</summary>
    private HashSet<string> AllBenchNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in League.Teams) names.Add(t.CoachName);
        foreach (var t in World.Clubs) names.Add(t.CoachName);
        if (World.College is { } cw) foreach (var t in cw.Programs) names.Add(t.CoachName);
        // A vacancy's chosen successor is spoken for too (review of PR #269).
        foreach (var v in Job.Vacancies) if (v.NextCoach is { Length: > 0 } nc) names.Add(nc);
        names.Remove(""); names.Remove("Interim coach");
        return names;
    }

    /// <summary>Springs kept.</summary>
    public const int CarouselSprings = 4;

    /// <summary>The newest spring's changes (empty before the first season ends).</summary>
    public IReadOnlyList<CarouselMove> LatestCarousel =>
        Carousel.Count == 0 ? Array.Empty<CarouselMove>() : Carousel.Where(m => m.Year == Carousel[0].Year).ToList();

    /// <summary>
    /// College programs change coaches at the end of the season (#651, CS9). Each vacancy is filled in order of the job's
    /// size: a step up for you opens to you (Board page), otherwise the program hires an assistant or a winning coach from a
    /// smaller program, whose job then opens too.
    /// </summary>
    private void CollegeCoachingChanges()
    {
        if (World.College is not { } cw || cw.Programs.Count == 0) return;
        var rng = CareerRng(2);
        var ctx = LeagueContext.College(cw, cw.Programs[0]);
        int year = Today.Year;
        // CS2 (run 126): the regular season, as the target covers (review of PR #190).
        var record = cw.Programs.ToDictionary(p => p.Id, p => cw.RegularSeasonRecord(p.Id));
        var moves = new List<CarouselMove>();
        var queue = new List<(Team T, string Why, int? To)>();
        var changed = new HashSet<int>();
        // You, let go at this review (Fire ran just before): your old program is on the carousel, and its new coach isn't
        // judged on your season (the review of PR #260).
        Team? mine = null;
        if (_sackedAtReview && !Employed && Coach.History.LastOrDefault() is { Ended: "Sacked" } last && cw.Program(last.TeamId) is { } m0)
        {
            mine = m0;
            moves.Add(new CarouselMove(year, mine.Id, mine.FullName, Coach.Name, mine.CoachName, "you", $"{last.Wins}-{last.Losses}"));
            changed.Add(mine.Id);
        }
        int? exclude = ControlledTeamId ?? mine?.Id;
        // H202 (run 200): an athletic director's patience. A coach's first two seasons are safe, a winning season with a
        // tournament bid is safe, and one poor season rarely costs a job: a run of them does (see CarouselFireChance).
        cw.EnsureBenches(seedPast: false);
        var poorRun = new Dictionary<int, int>();
        foreach (var t in cw.Programs.Where(t => t.Id != exclude))
        {
            var (w, l) = record[t.Id];
            // An interim job (a vacancy still open) has no coach to let go (review of PR #269); the draw keeps its place.
            if (t.CoachName == "Interim coach") { rng.Chance(0); continue; }
            var e = Board.SetExpectations(ctx, t, League.Season);
            var b = cw.BenchOf(t.Id);
            double shortfall = Math.Max(0, e.TargetWins - w) * 82.0 / Math.Max(1, ctx.SeasonGames);
            int run = shortfall >= CarouselPoorShortfall ? (b?.PoorRun ?? 0) + 1 : 0;
            poorRun[t.Id] = run;
            double p = CarouselFireChance(b?.Seasons + 1 ?? 3, w, l, cw.Season.Seeds.ContainsKey(t.Id), shortfall, run, t.Owner.Patience);
            if (rng.Chance(p)) { queue.Add((t, "fired", null)); changed.Add(t.Id); }
        }
        // Every bench counts the season (the record, bids, a title and the run of poor seasons) before anyone moves.
        // Not your old program, if you were let go at this review: its next coach isn't judged on your season.
        foreach (var t in cw.Programs.Where(t => t != mine))
            if (cw.BenchOf(t.Id) is { } b)
            {
                var (w, l) = record[t.Id];
                b.Seasons++; b.Wins += w; b.Losses += l; b.CareerWins += w; b.CareerLosses += l;
                if (cw.Season.Seeds.ContainsKey(t.Id)) { b.Bids++; b.BidsHere++; }
                if (cw.Season.ChampionId == t.Id) { b.Titles++; b.TitlesHere++; }
                b.PoorRun = poorRun.GetValueOrDefault(t.Id);
            }
        foreach (var t in cw.Programs)
            if (t.Id != exclude && !changed.Contains(t.Id) && t.CoachName != "Interim coach" && rng.Chance(CarouselRetireChance))
            { queue.Add((t, "retired", null)); changed.Add(t.Id); }

        // The biggest jobs move first, so a chain runs downhill.
        queue = queue.OrderByDescending(x => CarouselSize(x.T)).ToList();
        int poaches = 0;
        var benches = AllBenchNames();
        var hall = new List<(Team Program, LegacyInductee Who)>();
        TreeBranch? branch = null;
        for (int i = 0; i < queue.Count; i++)
        {
            var (t, why, to) = queue[i];
            string old = t.CoachName;
            var (w, l) = record[t.Id];
            // Slice 10 (run 201): a coach who leaves a program after a long, winning spell joins its Hall of Fame.
            if (cw.BenchOf(t.Id) is { Seasons: > 0 } gone && gone.Name == old
                && cw.InductCoach(t.Id, old, League.Season - gone.Seasons + 1, League.Season, gone.Seasons, gone.Wins, gone.Losses, gone.BidsHere, gone.TitlesHere, year) is { } ind)
                hall.Add((t, ind));
            // A new name: not the coach he replaces, and not one already on another bench (the name lists are short).
            // H203 (run 200): the name lists are long now, and a name already on any bench (college or pro) isn't used.
            string successor = Board.NewCoachName(rng, n => n == old || benches.Contains(n));
            bool open = Employed ? OpenWhileEmployed(t, successor) : OpenWhileOut(t, successor, old);
            int? from = null;
            BenchCoach? hiredFrom = null;
            string? staffOf = null;
            if (!open)
            {
                double prestige = CarouselSize(t);
                if (poaches < CarouselMaxPoaches && rng.Chance(prestige >= CarouselBigJob ? CarouselPoachBig : CarouselPoachSmall)
                    && PoachTarget(cw, t, prestige, record, changed, rng) is { } q)
                {
                    successor = q.CoachName;
                    from = q.Id;
                    hiredFrom = cw.BenchOf(q.Id);
                    poaches++;
                    changed.Add(q.Id);
                    // His old job opens, in its place in the order (it's smaller than this one, so it comes later).
                    var entry = (q, "left", (int?)t.Id);
                    int at = queue.FindIndex(i + 1, x => CarouselSize(x.T) < CarouselSize(q));
                    if (at < 0) queue.Add(entry); else queue.Insert(at, entry);
                }
                // Slice 10 (run 201): your coaching tree. No poach: the program may hire your assistant (a fixed hash, so no
                // draw moves), at most one a spring.
                if (from == null && branch == null && TreeHire(cw, t, year, benches) is { } asst)
                {
                    successor = asst.Name;
                    staffOf = HumanTeam.FullName;
                    HumanTeam.Staff.Remove(asst); asst.TeamId = null;
                    branch = new TreeBranch { Name = asst.Name, Year = year, FromProgramId = HumanTeam.Id, FromProgram = HumanTeam.FullName, ProgramId = t.Id, Program = t.FullName };
                    cw.MyCareer.Tree.Add(branch);
                }
                t.CoachName = successor;
                benches.Add(successor);
                cw.NewBench(t.Id, successor, hiredFrom);
            }
            moves.Add(new CarouselMove(year, t.Id, t.FullName, old, successor, why, $"{w}-{l}", from, to, open, staffOf));
        }
        PostCoachingLegacy(cw, hall, branch);
        if (moves.Count == 0) return;
        Carousel.RemoveAll(m => m.Year == year);
        Carousel.InsertRange(0, moves);
        var years = Carousel.Select(m => m.Year).Distinct().OrderByDescending(y => y).Take(CarouselSprings).ToHashSet();
        Carousel.RemoveAll(m => !years.Contains(m.Year));
        PostCarousel(cw, moves);
    }

    /// <summary>The coach a vacancy hires away: a winning coach (.620+, 18+ wins) at a clearly smaller program that hasn't
    /// changed this spring, picked among the best four records (never yours).</summary>
    private Team? PoachTarget(Hardwood.Core.College.CollegeWorld cw, Team job, double prestige,
        Dictionary<int, (int wins, int losses)> record, HashSet<int> changed, Rng rng)
    {
        var pool = cw.Programs
            .Where(p => p != job && !changed.Contains(p.Id) && p.Id != ControlledTeamId && p.Coach == null && p.CoachName != "Interim coach")
            .Where(p => record[p.Id] is var (w, l) && w >= 18 && w + l > 0 && w / (double)(w + l) >= 0.62)
            .Where(p => CarouselSize(p) <= prestige - CarouselStepUp)
            .OrderByDescending(p => record[p.Id].wins / (double)Math.Max(1, record[p.Id].wins + record[p.Id].losses) + CarouselSize(p) / 400.0)
            .Take(4).ToList();
        return pool.Count == 0 ? null : pool[rng.Next(pool.Count)];
    }

    /// <summary>One story for the spring: how many changed, your conference's and your rival's first, then the biggest jobs and
    /// the chains ("X leaves Y for Z").</summary>
    private void PostCarousel(Hardwood.Core.College.CollegeWorld cw, List<CarouselMove> moves)
    {
        bool mineCollege = Employed && InCollege;
        string? myConf = mineCollege ? cw.ConferenceOf[HumanTeam.Id] : null;
        var rivals = mineCollege ? cw.RivalsOf(HumanTeam.Id).Select(r => r.RivalId).ToHashSet() : new HashSet<int>();
        var shown = moves;
        var lines = new List<string>();
        var ours = shown.Where(m => rivals.Contains(m.ProgramId) || (myConf != null && cw.ConferenceOf.GetValueOrDefault(m.ProgramId) == myConf)).ToList();
        var big = shown.Except(ours).OrderByDescending(m => FindTeam(m.ProgramId) is { } t ? CarouselSize(t) : 0).Take(Math.Max(3, 8 - ours.Count)).ToList();
        if (ours.Count > 0)
        {
            var confName = myConf == null ? "" : Hardwood.Core.College.CollegeWorld.Conferences.First(c => c.Key == myConf).Short;
            lines.Add(myConf != null ? $"In the {confName}:" : "Your rivals:");
            lines.AddRange(ours.Select(m => "• " + CarouselLine(m) + (rivals.Contains(m.ProgramId) ? " (your rival)" : "")));
            lines.Add("");
            lines.Add("Around the country:");
        }
        lines.AddRange(big.Select(m => "• " + CarouselLine(m)));
        int rest = shown.Count - ours.Count - big.Count;
        if (rest > 0) lines.Add($"• and {rest} more on the Board page");
        int hired = moves.Count(m => m.FromProgramId != null);
        string title = $"The coaching carousel: {moves.Count} programs change coach";
        string body = $"{moves.Count} college programs changed head coach this spring: {moves.Count(m => m.Why is "fired" or "you")} let go, "
            + $"{moves.Count(m => m.Why == "retired")} retired" + (hired > 0 ? $", and {hired} hired another program's coach" : "") + ".\n\n"
            + string.Join("\n", lines)
            + (moves.Any(m => m.OpenToYou) ? "\n\nThe jobs still open to you are on the Board page." : "");
        if (mineCollege) Post(title, body, "World");
        else PostWorld(title, body, college: true);
    }

    /// <summary>"Kentucky: Pat Moore fired after 14-16; hired Dan Hill from Drake (27-6)".</summary>
    public string CarouselLine(CarouselMove m)
    {
        string prog = FindTeam(m.ProgramId)?.FullName ?? m.Program;
        string left = m.Why switch
        {
            "fired" => $"{m.Out} let go after {m.Record}",
            "retired" => $"{m.Out} retired",
            "left" => $"{m.Out} left for {(m.ToProgramId is { } to && FindTeam(to) is { } tt ? tt.FullName : "a bigger job")}",
            "you" when m.ToProgramId is { } yt => $"{m.Out} (you) left for {FindTeam(yt)?.FullName ?? "a bigger job"}",
            "you" => $"{m.Out} (you) let go after {m.Record}",
            _ => $"{m.Out} left",
        };
        bool stillOpen = m.OpenToYou && LiveVacancies.Any(v => v.TeamId == m.ProgramId);
        // An opening that was offered to you and filled without you: the program's coach now (the market draws its own hire;
        // the review of PR #260). Only for the newest spring, before anyone else can have moved on.
        string hired = m.OpenToYou && !stillOpen && Carousel.Count > 0 && m.Year == Carousel[0].Year && FindTeam(m.ProgramId) is { } now
            && now.CoachName != "Interim coach" ? now.CoachName : m.In;
        string came = stillOpen ? "the job is open (interim coach)"
            : m.FromProgramId is { } f && FindTeam(f) is { } ft ? $"hired {hired} from {ft.FullName}"
            : m.FromStaffOf is { } sf ? $"hired {hired}, an assistant at {sf}"
            : $"promoted {hired}";
        return $"{prog}: {left}; {came}";
    }

    /// <summary>A carousel opening you took this summer (an opening filled without you reads the program's coach, in <see cref="CarouselLine"/>).</summary>
    private void CarouselFilled(int programId, string coach)
    {
        if (Carousel.Count == 0 || Phase != SessionPhase.Offseason && Phase != SessionPhase.Postseason) return;
        int year = Carousel[0].Year;
        int i = Carousel.FindIndex(m => m.ProgramId == programId && m.OpenToYou && m.Year == year);
        if (i >= 0) Carousel[i] = Carousel[i] with { In = coach, OpenToYou = false, FromProgramId = null };
    }
}
