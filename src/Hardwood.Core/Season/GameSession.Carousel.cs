using Hardwood.Core.Jobs;
using Hardwood.Core.Model;
using Hardwood.Core.World;

namespace Hardwood.Core.Season;

/// <summary>
/// One change on the college coaching carousel (CS9, run 191): who left a program, why, and who came in.
/// <see cref="Why"/> is "fired", "retired", "left" (for a bigger job, <see cref="ToProgramId"/>) or "you" (you left or were let go).
/// <see cref="FromProgramId"/> is where the new coach came from when another program's coach was hired away.
/// </summary>
public sealed record CarouselMove(int Year, int ProgramId, string Program, string Out, string In, string Why, string Record = "",
                                  int? FromProgramId = null, int? ToProgramId = null, bool OpenToYou = false);

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
        foreach (var t in Board.AiSeasonChanges(ctx, t => record[t.Id].wins, rng, ControlledTeamId, League.Season, Board.CollegeBaseSackChance))
        { queue.Add((t, "fired", null)); changed.Add(t.Id); }
        foreach (var t in cw.Programs)
            if (t.Id != ControlledTeamId && !changed.Contains(t.Id) && t.CoachName != "Interim coach" && rng.Chance(CarouselRetireChance))
            { queue.Add((t, "retired", null)); changed.Add(t.Id); }
        // You, let go at the review (Fire ran before this): your old program is on the carousel too.
        if (!Employed && Coach.History.LastOrDefault() is { Ended: "Sacked" } last && last.ToSeason == League.Season
            && cw.Program(last.TeamId) is { } mine)
        {
            moves.Add(new CarouselMove(year, mine.Id, mine.FullName, Coach.Name, mine.CoachName, "you", $"{last.Wins}-{last.Losses}"));
            changed.Add(mine.Id);
        }

        // The biggest jobs move first, so a chain runs downhill.
        queue = queue.OrderByDescending(x => CarouselSize(x.T)).ToList();
        int poaches = 0;
        var benches = cw.Programs.Select(p => p.CoachName).ToHashSet();
        for (int i = 0; i < queue.Count; i++)
        {
            var (t, why, to) = queue[i];
            string old = t.CoachName;
            var (w, l) = record[t.Id];
            // A new name: not the coach he replaces, and not one already on another bench (the name lists are short).
            string successor = Board.NewCoachName(rng);
            for (int k = 0; k < 8 && (successor == old || benches.Contains(successor)); k++) successor = Board.NewCoachName(rng);
            bool open = Employed ? OpenWhileEmployed(t, successor) : OpenWhileOut(t, successor, old);
            int? from = null;
            if (!open)
            {
                double prestige = CarouselSize(t);
                if (poaches < CarouselMaxPoaches && rng.Chance(prestige >= CarouselBigJob ? CarouselPoachBig : CarouselPoachSmall)
                    && PoachTarget(cw, t, prestige, record, changed, rng) is { } q)
                {
                    successor = q.CoachName;
                    from = q.Id;
                    poaches++;
                    changed.Add(q.Id);
                    // His old job opens, in its place in the order (it's smaller than this one, so it comes later).
                    var entry = (q, "left", (int?)t.Id);
                    int at = queue.FindIndex(i + 1, x => CarouselSize(x.T) < CarouselSize(q));
                    if (at < 0) queue.Add(entry); else queue.Insert(at, entry);
                }
                t.CoachName = successor;
                benches.Add(successor);
            }
            moves.Add(new CarouselMove(year, t.Id, t.FullName, old, successor, why, $"{w}-{l}", from, to, open));
        }
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
        var shown = moves.Where(m => m.Why != "you" || !mineCollege).ToList();
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
        string body = $"{moves.Count} college programs changed head coach this spring: {moves.Count(m => m.Why == "fired")} let go, "
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
            "you" => $"{m.Out} (you) let go after {m.Record}",
            _ => $"{m.Out} left",
        };
        bool stillOpen = m.OpenToYou && LiveVacancies.Any(v => v.TeamId == m.ProgramId);
        string came = stillOpen ? "the job is open (interim coach)"
            : m.FromProgramId is { } f && FindTeam(f) is { } ft ? $"hired {m.In} from {ft.FullName}"
            : $"promoted {m.In}";
        return $"{prog}: {left}; {came}";
    }

    /// <summary>A carousel opening was filled after the spring (by you, or by the successor when you didn't take it).</summary>
    private void CarouselFilled(int programId, string coach)
    {
        if (Carousel.Count == 0) return;
        int year = Carousel[0].Year;
        int i = Carousel.FindIndex(m => m.ProgramId == programId && m.OpenToYou && m.Year == year);
        if (i >= 0) Carousel[i] = Carousel[i] with { In = coach, OpenToYou = false, Why = Carousel[i].Why, FromProgramId = null };
    }
}
