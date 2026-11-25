using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Core.World;

namespace Hardwood.Core.College;

/// <summary>A college conference: five power conferences of 14, three mid-majors of 16 (decision #82).</summary>
public sealed record ConferenceInfo(string Key, string Name, string Short, bool Power, int Size, bool Real = false);

/// <summary>One college game. Knockout games (invitationals, conference tournaments, March Madness) carry an event, a round and a slot.</summary>
public sealed class CollegeGame
{
    public DateOnly Date { get; set; }
    public int HomeId { get; set; }
    public int AwayId { get; set; }
    public int? HomePts { get; set; }
    public int? AwayPts { get; set; }
    /// <summary>Non-conference · Invitational · Showcase · Conference · Conference tournament · March Madness</summary>
    public string Stage { get; set; } = "";
    /// <summary>The tournament or showcase name (knockouts are grouped by it).</summary>
    public string? Event { get; set; }
    public int Round { get; set; }
    public int Slot { get; set; }
    /// <summary>Each side's Top 25 rank at tip-off (#528), so a result reads "#5 Duke 78, Kansas 71" as it stood. Null: unranked, or played before v0.78.0.</summary>
    public int? HomeRank { get; set; }
    public int? AwayRank { get; set; }
    /// <summary>March Madness (C4 part 2): each side's leading scorer, for the Final Four weekend and the champions' run.
    /// Null for other games and for games played before v0.101.0.</summary>
    public GameStar? HomeStar { get; set; }
    public GameStar? AwayStar { get; set; }
    /// <summary>Overtimes in a March Madness game (0 for other games and older saves).</summary>
    public int Overtimes { get; set; }
    public bool Played => HomePts != null;
    public int? WinnerId => !Played ? null : HomePts > AwayPts ? HomeId : AwayId;
    /// <summary>Whether the game is on a neutral floor: every event (invitationals, showcases, tournaments, March), except the
    /// NIT's first three rounds, which the better seed hosts (run 193). A method, so it isn't saved.</summary>
    public bool IsNeutral() => Event != null && !(Event == CollegeWorld.Nit && Round < 3);
}

/// <summary>A side's leading scorer in a March Madness game (points, then rebounds and assists break ties). The name is kept
/// so the line still reads after he graduates.</summary>
public sealed record GameStar(int PlayerId, string Name, int Pts, int Reb, int Ast)
{
    public string Line => $"{Name} {Pts} pts" + (Reb >= 5 ? $" · {Reb} reb" : "") + (Ast >= 5 ? $" · {Ast} ast" : "");
}

/// <summary>A college season (decision #96) and everything it decides. Saved as JSON with the world.</summary>
public sealed class CollegeSeason
{
    public int Year { get; set; }
    public List<CollegeGame> Games { get; set; } = new();
    /// <summary>Dates of each knockout round by event (round 0 = first round; March Madness round 0 = the First Four).</summary>
    public Dictionary<string, List<DateOnly>> RoundDates { get; set; } = new();
    /// <summary>Elo ratings (rankings and seeding).</summary>
    public Dictionary<int, double> Elo { get; set; } = new();
    public Dictionary<int, StatLine> Stats { get; set; } = new();
    /// <summary>March Madness: the 64 bracket slots (0 = a First Four winner still to come) and each team's seed.</summary>
    public List<int> Bracket { get; set; } = new();
    public Dictionary<int, int> Seeds { get; set; } = new();
    public Dictionary<string, int> ConferenceChampions { get; set; } = new();
    public Dictionary<string, int> EventWinners { get; set; } = new();
    public int? ChampionId { get; set; }
    public int? RunnerUpId { get; set; }
    public bool Selected { get; set; }
    /// <summary>Selection Sunday (C4 part 1): the field best first, and the bubble as it stood. Empty in seasons picked before v0.50.0.</summary>
    public List<int> SelectionOrder { get; set; } = new();
    public List<int> LastFourIn { get; set; } = new();
    public List<int> FirstFourOut { get; set; } = new();
    public List<int> NextFourOut { get; set; } = new();
    /// <summary>Pro clubs courting this season's seniors (roadmap 11, college depth). Older saves start with an empty board.</summary>
    public RecruitingBoard Recruiting { get; set; } = new();
    /// <summary>The Top 25 before the latest day of games (#528): program id → rank, for the poll's up and down arrows. Empty in older saves.</summary>
    public Dictionary<int, int> PollBefore { get; set; } = new();
    /// <summary>The day <see cref="PollBefore"/> was taken (the latest day of games).</summary>
    public DateOnly? PollDay { get; set; }
    /// <summary>K35 (run 193): the Top 25 as released on the latest poll Monday, every program best first (the badges, the
    /// Top 25 page and the tip-off ranks read it all week). Empty before the first release and in older saves (then the live poll).</summary>
    public List<int> PollRelease { get; set; } = new();
    /// <summary>Run 193: the NIT's 32 teams, best first (empty when the season had none, or before v0.181.0).</summary>
    public List<int> NitField { get; set; } = new();
}

public sealed record CollegeChampion(int Year, int ChampionId, string Champion, int RunnerUpId, string RunnerUp);

/// <summary>
/// College basketball (decisions #76, #82-85, #95-98): about 120 computer-run programs whose rosters hold every
/// American player from 14 to 22. Programs recruit 14-year-olds by prestige; players grow toward the ability they'll
/// have at 22; at 22 they graduate into free agency and the whole pyramid can sign them (D80 A, run 128: four college
/// years, Fr/So/Jr/Sr; it was 23 and five seasons before). The season runs from
/// November invitationals and December showcases through conference play and tournaments to a 68-team March Madness,
/// whose games use the full match engine.
/// </summary>
public sealed partial class CollegeWorld
{
    public const int RosterSize = 30, FirstId = 90_001, YoungestAge = 14, GraduateAge = 22;
    /// <summary>The age groups the class calibration was tuned on (the old 14-22 model, #121): each class's top end is drawn
    /// as if the world still had nine groups, so four college years don't add stars (D80 A, run 128).</summary>
    public const int CalibrationGroups = 9;
    public const string Madness = "March Madness";

    public static readonly ConferenceInfo[] Conferences = new ConferenceInfo[]
    {
        new("aca", "Atlantic Coast Alliance", "ACA", true, 14), new("bmw", "Big Midwest", "BMW", true, 14),
        new("sac", "Southern Athletic Conference", "SAC", true, 14), new("pcu", "Pacific Union", "PCU", true, 14),
        new("htl", "Heartland Conference", "HTL", true, 14),
        new("coa", "Coastal Conference", "COA", false, 16), new("fro", "Frontier Conference", "FRO", false, 16),
        new("grc", "Great Rivers Conference", "GRC", false, 16),
        // Danny (Nov 10, decision #121): more big programs. Older careers keep their eight conferences (see Active).
        new("mpc", "Mountain Plains Conference", "MPC", true, 14),
        new("lvc", "Lakes & Valleys Conference", "LVC", false, 16),
    }.Concat(Hardwood.Core.RealWorld.RealColleges.ConferenceInfos()).ToArray(); // R3: the real 2026-27 conferences (a real career's)

    /// <summary>The programs in the original eight conferences: the elite share of each recruiting class is scaled to keep
    /// the number of NABL-ready graduates as it was, so extra programs add depth for the pyramid rather than more stars.</summary>
    public const int BasePrograms = 118;

    /// <summary>Conferences this world actually has (a career started before #121 has eight).</summary>
    public IEnumerable<ConferenceInfo> Active => Conferences.Where(c => ConferenceOf.ContainsValue(c.Key));

    public List<Team> Programs { get; } = new();
    public Dictionary<int, string> ConferenceOf { get; } = new();
    public Dictionary<int, double> Prestige { get; } = new();
    /// <summary>R3: a real program's long-run standing (its starting prestige); prestige drifts back toward it rather than a
    /// conference's level. Empty in fictional worlds and older saves, which keep the conference levels (68 power, 42 others).</summary>
    public Dictionary<int, double> Tradition { get; } = new();
    /// <summary>
    /// CS7 part 2b (run 173, #166 "prestige with a long memory"): each program's last <see cref="PrestigeMemory"/> season marks,
    /// oldest first. K27 (run 180): prestige moves each summer by their weighted sum (<see cref="FormWeights"/>, newest first; a
    /// missing season counts as 0). The weights add to 1, so each season still moves prestige by its whole mark over five
    /// summers and a program has to keep winning to climb, but the newest season counts most, so a coach's first seasons show.
    /// Empty in a fresh world and in saves before v0.161.0.
    /// </summary>
    public Dictionary<int, List<double>> Form { get; } = new();
    /// <summary>The seasons prestige remembers.</summary>
    public const int PrestigeMemory = 5;
    /// <summary>K27 (run 180): what each remembered season counts for, newest first (they add to 1). Was a flat fifth each.</summary>
    public static readonly double[] FormWeights = { 0.40, 0.25, 0.15, 0.12, 0.08 };
    /// <summary>
    /// A season's mark: the record against .500 (±5), +1.5 a March Madness win and +4 for the title. K27 (run 180): with a
    /// conference schedule of 8+ games the record part is 40% the overall record and 60% the conference record, so a power
    /// program that goes 6-13 in its league after a soft November isn't marked as a .500 team (Virginia Tech went 16-14
    /// with 6-13 in the ACC, marked +0.3, and its prestige read 69 → 69 → 69).
    /// </summary>
    public static double SeasonMark(int wins, int losses, int confWins, int confLosses, int madnessWins, bool champion)
    {
        double Pct(int w, int l) => w / (double)(w + l) - 0.5;
        double record = wins + losses == 0 ? 0
            : confWins + confLosses >= 8 ? Pct(wins, losses) * 4 + Pct(confWins, confLosses) * 6
            : Pct(wins, losses) * 10;
        return record + madnessWins * 1.5 + (champion ? 4 : 0);
    }
    /// <summary>The mark without a conference record (the overall record only): run 173's formula.</summary>
    public static double SeasonMark(int wins, int losses, int madnessWins, bool champion) => SeasonMark(wins, losses, 0, 0, madnessWins, champion);
    /// <summary>A program's five-season form: what its remembered marks move prestige by each summer.</summary>
    public double FormOf(int id) => Form.TryGetValue(id, out var m) ? FormOfMarks(m) : 0;
    /// <summary>The weighted form of a list of marks, oldest first.</summary>
    public static double FormOfMarks(IReadOnlyList<double> marks)
    {
        double sum = 0;
        for (int i = 0; i < Math.Min(marks.Count, PrestigeMemory); i++) sum += marks[marks.Count - 1 - i] * FormWeights[i];
        return sum;
    }
    /// <summary>A program's regular-season conference record this season (conference games only).</summary>
    public (int wins, int losses) ConferenceRecord(int id)
    {
        int w = 0, l = 0;
        foreach (var g in Season.Games)
        {
            if (!g.Played || g.Stage != "Conference" || (g.HomeId != id && g.AwayId != id)) continue;
            if (g.WinnerId == id) w++; else l++;
        }
        return (w, l);
    }
    /// <summary>This season's mark for a program, read from the season so far (the summer reads it once the season is over).</summary>
    public double SeasonMarkOf(int id)
    {
        var (w, l) = Record(id);
        var (cw, cl) = ConferenceRecord(id);
        int runs = Season.Games.Count(g => g.Event == Madness && g.Round >= 1 && g.WinnerId == id);
        return SeasonMark(w, l, cw, cl, runs, Season.ChampionId == id);
    }
    /// <summary>
    /// K27 (run 180): what this summer does to a program's prestige, without changing anything: this season's mark joins the
    /// memory, the form moves it (gains shrink near the top), then it drifts 8% toward its baseline. <see cref="Summer"/>
    /// applies exactly this, and the Season review (posted the day before) quotes it.
    /// </summary>
    public (double Before, double Mark, double Form, double After) PrestigeMove(int id)
    {
        var t = Program(id)!;
        var conf = Conferences.First(x => x.Key == ConferenceOf[id]);
        double before = Prestige[id], mark = SeasonMarkOf(id);
        var marks = Form.TryGetValue(id, out var m) ? new List<double>(m) : new List<double>();
        marks.Add(mark);
        if (marks.Count > PrestigeMemory) marks.RemoveRange(0, marks.Count - PrestigeMemory);
        double form = FormOfMarks(marks), change = form;
        // C4 step 0 (#186): gains shrink near the top, so only a run of titles holds a program at the very top
        // (review #7: after 8 seasons eight programs sat at 99, and the Elo prestige bonus seeded them all alike).
        if (change > 0) change *= Math.Clamp((100 - before) / 30.0, 0, 1);
        double after = Math.Clamp(before + change + (Baseline(t, conf) - before) * 0.08, 10, 99);
        return (before, mark, form, after);
    }
    /// <summary>A signed number with one decimal and a real minus sign ("+1.2", "−0.4", "0.0").</summary>
    public static string Signed(double v) => v >= 0.05 ? $"+{v:0.0}" : v <= -0.05 ? $"\u2212{-v:0.0}" : "0.0";
    /// <summary>
    /// K27 (run 180): the Season review's prestige line: before → after (with the letter when it changes), the five-season
    /// form that moved it, and this season's mark with the conference record behind it.
    /// </summary>
    public string PrestigeReviewLine(int id)
    {
        var (before, mark, form, after) = PrestigeMove(id);
        string lb = PrestigeLetter(before), la = PrestigeLetter(after);
        string letters = lb == la ? $"({la})" : $"({lb} \u2192 {la})";
        var conf = Conferences.First(x => x.Key == ConferenceOf[id]);
        var (cw, cl) = ConferenceRecord(id);
        string league = cw + cl > 0 ? $", {cw}-{cl} in the {conf.Name}" : "";
        return $"Program prestige: {before:0.0} \u2192 {after:0.0} {letters} this summer. Five-season form {Signed(form)}; this season's mark is {Signed(mark)}{league}, "
            + "and it counts most (40%), with the four seasons before it at 25, 15, 12 and 8%.";
    }
    /// <summary>Where prestige drifts back to each summer.</summary>
    public double Baseline(Team t, ConferenceInfo conf) => Tradition.TryGetValue(t.Id, out var b) ? b : conf.Power ? 68 : 42;
    /// <summary>A real world's programs (R3).</summary>
    public bool IsReal => Active.Any(c => c.Real);
    /// <summary>The ability each player is heading for at 22 (his development path).</summary>
    public Dictionary<int, int> Destined { get; } = new();
    public CollegeSeason Season { get; set; } = new();
    public List<CollegeChampion> History { get; } = new();
    public List<(string Title, string Body)> News { get; } = new();

    /// <summary>Every college player and high-school prospect.</summary>
    public IEnumerable<Player> Players => Programs.SelectMany(t => t.Roster).Concat(Prospects);
    public Team? Program(int id) => Programs.FirstOrDefault(t => t.Id == id);
    public IEnumerable<Team> InConference(string key) => Programs.Where(t => ConferenceOf[t.Id] == key);

    // ------------------------------------------------------------------------------------------
    // Players: ability at each age on the way to 22.

    // D80 A (run 128): college is four years, so the path runs 14..22. Freshmen (18) are where they were (0.70) and seniors
    // (21) where the old fifth-year seniors were (0.94), so the college game and each graduate's ability are unchanged;
    // he just turns pro a year younger.
    private static readonly double[] Path = { 0.45, 0.52, 0.58, 0.64, 0.70, 0.78, 0.86, 0.94, 1.0 }; // ages 14..22
    public static double Share(int age) => Path[Math.Clamp(age, YoungestAge, GraduateAge) - YoungestAge];

    // ------------------------------------------------------------------------------------------
    // CP2 (run 131, `claude/college-pipeline-design.md`; Danny, Nov 19: "a real 'development' pipeline"): college growth
    // responds to the coach. Destined is the expected ability at 22 at an average program; each summer the season's minutes,
    // the program's coaching, staff and practice facility and the player's professionalism tilt that summer's step,
    // so a prospect who plays 30 minutes for a good staff graduates several points better than one buried on a weak bench.
    // Prestige isn't an input, so it doesn't compound: top programs already recruit the best.

    /// <summary>How much playing time swings growth (the share of 30 minutes, against the program's own growing players).</summary>
    public const double MinutesSwing = 0.14;
    /// <summary>Each training-quality point (1-20) and professionalism point (1-20) moves growth this much.</summary>
    public const double QualityStep = 0.012, ProfessionalismStep = 0.008;
    /// <summary>An average program's development quality: what a program without a staff model trains at, and about what a
    /// program you take over trains at with the staff it comes with (quality 9 on average, `EnsureStaff`).</summary>
    public const double AverageQuality = 9;
    /// <summary>A program's typical minutes by depth (best player first), for a season with no box scores.</summary>
    private static readonly double[] DepthMinutes = { 32, 30, 28, 26, 24, 20, 16, 12, 7, 4 };

    /// <summary>The playing-time term: the share of 30 minutes he played, against <paramref name="pivot"/> (the program's
    /// growing players' average share, so minutes move growth between teammates without shrinking the whole class).</summary>
    public static double MinutesFactor(double mpg, double pivot) => 1 + MinutesSwing * (Math.Clamp(mpg / 30.0, 0, 1) - pivot);

    /// <summary>How much faster (above 1) or slower than an average program's this summer's growth runs.</summary>
    public static double GrowthFactor(double mpg, double quality, int professionalism, double pivot = 0.5) =>
        MinutesFactor(mpg, pivot)
        * (1 + QualityStep * (Math.Clamp(quality, 1, 20) - AverageQuality))
        * (1 + ProfessionalismStep * (Math.Clamp(professionalism, 1, 20) - 10));

    /// <summary>
    /// A program's development quality (1-20, 9 average): its staff, practice facility and head coach's Development rating
    /// where it has them (your program), <see cref="AverageQuality"/> otherwise. Prestige adds nothing here: the big programs
    /// already recruit the best, and a prestige lift made March Madness chalkier (run 131: 5v12 77.7% with it, 72.7% without).
    /// </summary>
    public double ProgramQuality(Team t, Player p, DateOnly on) =>
        Math.Clamp(t.Staff.Count > 0 || t.Coach != null ? StaffEffects.TrainingQualityFor(t, p, on) : AverageQuality, 1, 20);

    /// <summary>Players growing this summer: in college last season (19-21 on July 1) with a path.</summary>
    private bool Growing(Player p, DateOnly summer) => p.Age(summer) is > YoungestAge + 4 and < GraduateAge && Destined.ContainsKey(p.Id);

    /// <summary>
    /// His minutes a game last season, counted over the program's games (a starter hurt in November played few): from the box
    /// scores once the program has played 8 games, otherwise his place in its depth by ability among its college players.
    /// </summary>
    public double SeasonMpg(Team t, Player p, DateOnly summer)
    {
        int teamGames = Season.Games.Count(g => g.Played && (g.HomeId == t.Id || g.AwayId == t.Id));
        if (teamGames >= 8)
            return Season.Stats.TryGetValue(p.Id, out var st) && st.TeamId == t.Id ? st.Min / Math.Max(teamGames, st.G) : 0;
        var depth = t.Roster.Where(x => x.Age(summer) > YoungestAge + 4).OrderByDescending(x => x.CurrentAbility).ThenBy(x => x.Id).ToList();
        int i = depth.IndexOf(p);
        return i < 0 ? 0 : i < DepthMinutes.Length ? DepthMinutes[i] : 2;
    }

    /// <summary>The program's growing players' average share of 30 minutes, weighted by their step (the minutes pivot).</summary>
    public double MinutesPivot(Team t, DateOnly summer, IReadOnlyDictionary<int, double> mpg)
    {
        double w = 0, sum = 0;
        foreach (var p in t.Roster.Where(p => Growing(p, summer)))
        {
            double step = Math.Max(1, PathStep(p, p.Age(summer)));
            w += step; sum += step * Math.Clamp(mpg.GetValueOrDefault(p.Id) / 30.0, 0, 1);
        }
        return w > 0 ? sum / w : 0.5;
    }

    /// <summary>Every roster player's minutes and the program's pivot, read before the seniors leave.</summary>
    public (Dictionary<int, double> Mpg, double Pivot) Minutes(Team t, DateOnly summer)
    {
        var mpg = t.Roster.ToDictionary(p => p.Id, p => SeasonMpg(t, p, summer));
        return (mpg, MinutesPivot(t, summer, mpg));
    }

    /// <summary>This summer's step along his path at an average program (CA points).</summary>
    public int PathStep(Player p, int age) => Destined.TryGetValue(p.Id, out var g) ? (int)Math.Round(g * (Share(age) - Share(age - 1))) : 0;

    /// <summary>The CA points this summer's growth gains (or loses) against an average program, at these minutes.</summary>
    public double ProgramEdge(Team t, Player p, DateOnly summer, double mpg, double pivot)
    {
        if (!Growing(p, summer)) return 0;
        return PathStep(p, p.Age(summer)) * (GrowthFactor(mpg, ProgramQuality(t, p, summer), p.Hidden.Professionalism, pivot) - 1);
    }

    /// <summary>
    /// The Staff page's and player page's projection for one of your college players (before luck), read exactly as the summer
    /// will: the CA change at his minutes so far, and how much of it the program adds against average coaching and staff.
    /// Zero for a player who turns pro this summer.
    /// </summary>
    public (double Change, double FromProgram) ExpectedGrowth(Team t, Player p, DateOnly summer)
    {
        if (!Growing(p, summer)) return (0, 0);
        var (mpg, pivot) = Minutes(t, summer);
        int g = Destined[p.Id], age = p.Age(summer);
        double edge = ProgramEdge(t, p, summer, mpg[p.Id], pivot);
        double change = Math.Max(0, Math.Min(g * Share(age) + edge, p.PotentialAbility) - p.CurrentAbility);
        double average = PathStep(p, age) * (GrowthFactor(mpg[p.Id], AverageQuality, p.Hidden.Professionalism, pivot) - 1);
        return (change, edge - average);
    }

    /// <summary>The summer your college players' growth is heading for: the next July 1.</summary>
    public static DateOnly NextSummer(DateOnly today) => new(SchoolYear(today) + 1, 7, 1);

    /// <summary>
    /// How good a recruit will be at 22. Calibrated so each year's graduates look like the old draft classes at the top
    /// (a handful ready for the NABL, a few dozen for the tiers below) with most heading for the lower leagues or out.
    /// </summary>
    private static int DestinedAbility(Rng rng, double eliteScale = 1, double goodScale = 1)
    {
        double u = rng.NextDouble(), elite = 0.012 * eliteScale;
        // R3: in a world of more than 148 programs the class is capped at the thousand tracked, so the 90-110 band is
        // scaled too (the fictional world's 148 keep exactly the old draw).
        // (0.065 with the elite share; written as elite + 0.053 so CP1's draws without the elite share don't move it into 90-110.)
        double good = elite + 0.053 * Math.Min(1, goodScale);
        return u < elite ? rng.Next(110, 131) : u < good ? rng.Next(90, 110) : u < 0.35 ? rng.Next(75, 90) : rng.Next(50, 75);
    }

    /// <summary>The 90-110 band's share against a 148-program world (1 there and below).</summary>
    private double GoodScale => Math.Min(1, 148.0 / Math.Max(1, Programs.Count));

    private Player NewRecruit(PlayerGenerator gen, Rng rng, int age, DateOnly on, int? destined = null, int? potential = null)
    {
        int g = destined ?? DestinedAbility(rng, Math.Min(1, BasePrograms / (double)Math.Max(1, Programs.Count)), GoodScale);
        int ca = Math.Max(20, (int)Math.Round(g * Share(age) + rng.NextGaussian(0, 2)));
        var p = gen.Generate((Position)rng.Next(0, 5), ca, age, on, Heritages.All.First(h => h.Key == "us"), "USA");
        p.PotentialAbility = potential is { } pa ? Math.Max(ca, pa)
            : Math.Max(ca, g + (int)Math.Round(Math.Max(0, rng.NextGaussian(g >= 100 ? 10 : 5, 6))));
        Destined[p.Id] = g;
        return p;
    }

    /// <summary>
    /// CP1 (run 130, `claude/college-pipeline-design.md`): the top of every class, drawn per class rather than as a share of
    /// it, so it doesn't shrink as the world gains programs. One or two franchise prospects (two in
    /// <see cref="FranchisePairChance"/> of classes) heading for 132-145 at 22 with potential 165-190, and 8-15 five-star-level prospects
    /// heading for 118-135 with potential 148-170. Before, the best graduate's potential was about 140-150 and no graduate
    /// could become a star (CA 160+). The gap to potential is closed, or not, in the pros: some of them bust.
    /// </summary>
    public static List<(int Destined, int Potential)> TopBand(Rng rng)
    {
        var band = new List<(int, int)>();
        int franchise = rng.Chance(FranchisePairChance) ? 2 : 1, fiveStar = rng.Next(8, 16);
        for (int i = 0; i < franchise; i++)
        {
            int g = rng.Next(132, 146);
            band.Add((g, Math.Max(g + 25, rng.Next(165, 191))));
        }
        for (int i = 0; i < fiveStar; i++)
        {
            int g = rng.Next(118, 136);
            band.Add((g, Math.Max(g + 12, rng.Next(148, 171))));
        }
        return band;
    }

    /// <summary>The chance a class has two franchise prospects rather than one (CP1).</summary>
    public const double FranchisePairChance = 0.45;

    /// <summary>A class's recruits with the CP1 top band first: the first <paramref name="core"/> are drawn from the class
    /// calibration (the band takes the first places), the rest are depth at <paramref name="depth"/>.</summary>
    private List<Player> NewCohort(PlayerGenerator gen, Rng rng, int age, DateOnly on, int count, int core, Func<int>? depth = null)
    {
        var band = TopBand(rng);
        // A small intake (the legacy summer's few vacancies, a test world) keeps the band to a twentieth of it (review).
        if (band.Count > Math.Max(1, count / 20)) band = band.Take(Math.Max(1, count / 20)).ToList();
        var list = new List<Player>(count);
        for (int i = 0; i < count; i++)
            list.Add(i < band.Count && i < core ? NewRecruit(gen, rng, age, on, band[i].Destined, band[i].Potential)
                   // The band replaces the old elite share (110-130 at 22, about four a class), so the class's top end is the
                   // band and the league's depth doesn't inflate (the audit's top-13 row).
                   : NewRecruit(gen, rng, age, on, i < core || depth == null ? DestinedAbility(rng, 0, GoodScale) : depth()));
        return list;
    }

    /// <summary>Recruits go to programs by prestige (decision #84): the best players to the biggest names, with some luck.</summary>
    private void Recruit(List<Player> recruits, Rng rng)
    {
        foreach (var p in recruits.OrderByDescending(p => Destined[p.Id]))
        {
            var open = Programs.Where(t => t.Roster.Count < RosterSize).ToList();
            if (open.Count == 0) break;
            var pick = open.OrderByDescending(t => Prestige[t.Id] + rng.NextGaussian(0, 18)).First();
            pick.Roster.Add(p);
            p.TeamId = pick.Id;
        }
    }

    // ------------------------------------------------------------------------------------------
    // Generation

    private static readonly string[] States =
    {
        "Ohio", "Kentucky", "Carolina", "Texas", "Michigan", "Indiana", "Kansas", "Virginia", "Georgia", "Tennessee", "Oregon", "Arizona",
        "Florida", "Iowa", "Wisconsin", "Illinois", "Missouri", "Alabama", "Louisiana", "Oklahoma", "Nebraska", "Colorado", "Utah", "Nevada",
        "Washington", "Maryland", "Connecticut", "Massachusetts", "Pennsylvania", "Minnesota", "Arkansas", "Mississippi", "Montana", "Idaho",
        "Wyoming", "Dakota", "Vermont", "Delaware", "Jersey", "Carolina Coastal", "Gulf", "Lakeshore", "Bluegrass", "Ozark", "Sierra", "Cascade",
        "Prairie", "Piedmont", "Tidewater", "Big Sky", "Red River", "Rio Grande", "Smoky Mountain", "Great Plains", "Bayou", "Palmetto",
        "Keystone", "Granite", "Golden Valley", "High Desert",
    };
    private static readonly string[] Forms = { "{0} State", "University of {0}", "{0} Tech", "{0} A&M", "Northern {0}", "Southern {0}", "{0} Christian", "Central {0}", "Western {0}", "Eastern {0}" };
    private static readonly string[] Mascots =
    {
        "Wildcats", "Bulldogs", "Tigers", "Eagles", "Cougars", "Bears", "Panthers", "Hornets", "Owls", "Rams", "Cardinals", "Huskies", "Aggies",
        "Lions", "Spartans", "Mustangs", "Hawkeyes", "Wolverines", "Seminoles", "Gophers", "Badgers", "Bobcats", "Mountaineers", "Rebels",
        "Volunteers", "Hoosiers", "Sooners", "Longhorns", "Razorbacks", "Gamecocks", "Terrapins", "Buckeyes", "Bruins", "Trojans", "Ducks",
        "Beavers", "Sun Devils", "Utes", "Buffaloes", "Cyclones", "Jayhawks", "Wolfpack", "Blue Devils", "Tar Heels", "Cavaliers", "Hokies",
    };

    /// <summary>Build every program with a full roster (new careers): the recruiting model (#162) unless asked for the old one.</summary>
    public static CollegeWorld Generate(PlayerGenerator gen, Rng rng, DateOnly today, bool recruiting = true, bool real = false)
    {
        var c = new CollegeWorld();
        var used = new HashSet<string>();
        int id = FirstId, n = 0;
        if (real) c.AddRealPrograms(rng, ref id);
        foreach (var conf in Conferences.Where(x => !x.Real && !real))
        {
            for (int i = 0; i < conf.Size; i++, n++)
            {
                string school;
                do school = string.Format(Forms[rng.Next(Forms.Length)], States[rng.Next(States.Length)]);
                while (!used.Add(school));
                string abbr = new string(school.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w != "of" && w != "University").Select(w => char.ToUpperInvariant(w[0])).ToArray());
                abbr = abbr.Length >= 2 ? abbr : school[..3].ToUpperInvariant();
                while (!used.Add("abbr:" + abbr)) abbr += (char)('A' + rng.Next(26));
                double hue = rng.NextDouble() * 360;
                var team = new Team
                {
                    Id = id++, City = school, Name = rng.Pick(Mascots), Abbreviation = abbr, Conference = Conference.East, Division = conf.Short,
                    ArenaCapacity = rng.Next(conf.Power ? 9000 : 4000, conf.Power ? 22000 : 12000),
                    PrimaryColor = Hsv(hue, 0.7, 0.6), SecondaryColor = rng.Chance(0.5) ? "#F4F4F4" : Hsv((hue + 180) % 360, 0.6, 0.85),
                    LogoStyle = rng.Next(0, 5), Tactic = new Tactic { Pace = Math.Round(rng.NextGaussian(90, 3), 1) },
                };
                c.Programs.Add(team);
                c.ConferenceOf[team.Id] = conf.Key;
                c.Prestige[team.Id] = Math.Clamp(rng.NextGaussian(conf.Power ? 68 : 42, 11), 15, 95);
            }
        }
        if (recruiting)
        {
            c.GenerateRecruiting(gen, rng, today);
            c.StartSeason(today.Month >= 7 ? today.Year : today.Year - 1, rng);
            return c;
        }
        // Rosters: each age group recruited in turn, so the best of every year go to the biggest programs.
        // Ages 14-21 today: everyone who turns 22 before next summer graduates then, so every graduate is 22
        // (a 22-year-old cohort here would graduate at 23 and double the first class). The spare places go to the oldest.
        int groups = GraduateAge - YoungestAge, perAge = RosterSize / groups, spare = RosterSize - perAge * groups;
        for (int age = GraduateAge - 1; age >= YoungestAge; age--)
        {
            int each = perAge + (GraduateAge - 1 - age < spare ? 1 : 0);
            var cohort = c.NewCohort(gen, rng, age, today, c.Programs.Count * each, int.MaxValue);
            c.Recruit(cohort, rng);
        }
        c.StartSeason(today.Month >= 7 ? today.Year : today.Year - 1, rng);
        return c;
    }

    /// <summary>R3: every Division I program in its 2026-27 conference, with real names, nicknames, colours and starting
    /// prestige from real standing (<see cref="Hardwood.Core.RealWorld.RealColleges"/>). Arenas are sized from prestige.</summary>
    private void AddRealPrograms(Rng rng, ref int id)
    {
        var abbrs = Hardwood.Core.RealWorld.RealColleges.Abbreviations();
        foreach (var rp in Hardwood.Core.RealWorld.RealColleges.Programs)
        {
            var conf = Conferences.First(x => x.Key == Hardwood.Core.RealWorld.RealColleges.KeyPrefix + rp.Conference);
            double prestige = Hardwood.Core.RealWorld.RealColleges.Prestige(rp);
            var team = new Team
            {
                Id = id++, City = rp.School, Name = rp.Nickname, Abbreviation = abbrs[rp.School], Conference = Conference.East, Division = conf.Short,
                ArenaCapacity = (int)Math.Round(Math.Clamp(1500 + prestige * 190 + rng.NextGaussian(0, 1500), 1500, 22000) / 100) * 100,
                PrimaryColor = rp.Primary, SecondaryColor = rp.Secondary,
                LogoStyle = rng.Next(0, 5), Tactic = new Tactic { Pace = Math.Round(rng.NextGaussian(90, 3), 1) },
            };
            Programs.Add(team);
            ConferenceOf[team.Id] = conf.Key;
            Prestige[team.Id] = prestige;
            Tradition[team.Id] = prestige;
        }
    }

    private static string Hsv(double h, double s, double v)
    {
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        var (r, g, b) = h switch { < 60 => (c, x, 0.0), < 120 => (x, c, 0.0), < 180 => (0.0, c, x), < 240 => (0.0, x, c), < 300 => (x, 0.0, c), _ => (c, 0.0, x) };
        return $"#{(int)((r + m) * 255):X2}{(int)((g + m) * 255):X2}{(int)((b + m) * 255):X2}";
    }

    // ------------------------------------------------------------------------------------------
    // The summer: graduation, growth, recruiting, prestige.

    /// <summary>
    /// One college player's summer: CP2 (run 131) moves where he's heading by the season just played (minutes, the program's
    /// coaching, staff and facility, and his professionalism), then he grows toward that path, with a little luck.
    /// </summary>
    public void Grow(Team t, Player p, int age, DateOnly on, double mpg, double pivot, DevelopmentEngine dev, Rng rng)
    {
        int g = Destined.TryGetValue(p.Id, out var d) ? d : p.CurrentAbility;
        if (d > 0 && age > YoungestAge + 4)
        {
            double shift = ProgramEdge(t, p, on, mpg, pivot) / Share(age);
            int whole = (int)Math.Floor(shift);
            int moved = whole + (rng.NextDouble() < shift - whole ? 1 : 0);
            if (moved != 0) Destined[p.Id] = g = Math.Clamp(g + moved, 20, Math.Max(20, Math.Max(g, p.PotentialAbility)));
        }
        int target = (int)Math.Round(g * Share(age));
        int delta = target - p.CurrentAbility + (int)Math.Round(rng.NextGaussian(0, 2));
        if (delta > 0) dev.Nudge(p, delta, age);
    }

    /// <summary>
    /// Players who are 22 graduate into free agency (decision #98; D80 A); everyone else grows along his path; programs
    /// recruit 14-year-olds to refill their rosters; prestige follows results. Returns the graduates.
    /// </summary>
    public List<Player> Summer(DateOnly on, List<Player> freeAgents, PlayerGenerator gen, DevelopmentEngine dev, Rng rng)
    {
        var grads = new List<Player>();
        foreach (var t in Programs)
        {
            // CP2: last season's minutes, read before the seniors leave (they were part of the depth chart).
            var (minutes, pivot) = Minutes(t, on);
            foreach (var p in t.Roster.ToList())
            {
                int age = p.Age(on);
                if (age >= GraduateAge)
                {
                    t.Roster.Remove(p);
                    Alumni[p.Id] = new Alumnus(t.Id, on.Year); // CP3: the alumni tracker and the recruiting pitch
                    p.TeamId = null; p.Contract = null; p.RookieSeason = on.Year;
                    Destined.Remove(p.Id); WalkOnIds.Remove(p.Id);
                    freeAgents.Add(p);
                    grads.Add(p);
                    continue;
                }
                Grow(t, p, age, on, minutes[p.Id], pivot, dev, rng);
            }
        }
        // Prestige: winning and tournament runs raise it; it drifts back toward the conference's level.
        // CS7 part 2b (run 173): with a long memory. This season's mark joins the last four, and prestige moves by their
        // five-season average (it moved by this season's mark alone, so one Final Four run lifted a program 12+ points at once).
        // K27 (run 180): weighted toward the newest season, with the conference record in the mark (PrestigeMove).
        foreach (var t in Programs)
        {
            var move = PrestigeMove(t.Id);
            if (!Form.TryGetValue(t.Id, out var marks)) Form[t.Id] = marks = new List<double>();
            marks.Add(move.Mark);
            if (marks.Count > PrestigeMemory) marks.RemoveRange(0, marks.Count - PrestigeMemory);
            Prestige[t.Id] = move.After;
        }
        // CS7 (run 170): the coached program's boosters give, and a project paid for during the season opens. No RNG.
        foreach (var t in Programs) if (t.Id != ManagedProgramId) ProgramBuilding.OpenPending(t); // a program you left
        if (ManagedProgramId is { } me && Program(me) is { } mine) ProgramBuilding.Summer(mine, this);
        if (Recruiting)
        {
            // High-school prospects grow along their path too; then the seniors enroll (decision #162).
            foreach (var p in Prospects)
            {
                int age = p.Age(on);
                int g = Destined.TryGetValue(p.Id, out var d) ? d : p.CurrentAbility;
                int delta = (int)Math.Round(g * Share(age)) - p.CurrentAbility + (int)Math.Round(rng.NextGaussian(0, 2));
                if (delta > 0) dev.Nudge(p, delta, age);
            }
            SummerRecruiting(on, gen, rng);
        }
        else
        {
        // Recruit the new class of 14-year-olds.
        int needed = Programs.Sum(t => RosterSize - t.Roster.Count);
        Recruit(NewCohort(gen, rng, YoungestAge, on, needed, int.MaxValue), rng);
        }
        if (grads.Count > 0)
        {
            var top = grads.OrderByDescending(p => p.CurrentAbility).Take(5).ToList();
            News.Add(($"{grads.Count} college graduates turn pro", "The best of the class: " + string.Join(", ", top.Select(p => $"{p.Name} ({p.PrimaryPosition}, {p.CurrentAbility})")) + ". They're free agents now."));
        }
        return grads;
    }

    /// <summary>
    /// CS2 (run 126): the regular season, the part the athletic director's win target covers: non-conference games, the
    /// November invitationals, the showcases and the conference schedule. Conference tournaments and March Madness are the
    /// postseason, judged by how far the program went (a 33-5 record with five tournament wins isn't "3 wins past" a 24-win target).
    /// </summary>
    public static bool IsRegularSeason(CollegeGame g) => g.Stage is not ("Conference tournament" or Madness or Nit);

    /// <summary>CS2: a program's regular-season record (<see cref="IsRegularSeason"/>).</summary>
    public (int wins, int losses) RegularSeasonRecord(int id)
    {
        int w = 0, l = 0;
        foreach (var g in Season.Games)
        {
            if (!g.Played || (g.HomeId != id && g.AwayId != id) || !IsRegularSeason(g)) continue;
            if (g.WinnerId == id) w++; else l++;
        }
        return (w, l);
    }

    /// <summary>CS2: a program's March Madness seed this season (0 = not in the field).</summary>
    public int SeedOf(int id) => Season.Seeds.GetValueOrDefault(id);

    /// <summary>
    /// CS2 (#166): prestige as a letter, A+ to F, the way the program is talked about. Blue bloods (75+) are A grades, the known
    /// names (55+) Bs, then C for a solid program, D for a small one, F for the bottom.
    /// </summary>
    public static string PrestigeLetter(double p) => p switch
    {
        >= 88 => "A+", >= 80 => "A", >= 75 => "A-",
        >= 70 => "B+", >= 62 => "B", >= 55 => "B-",
        >= 50 => "C+", >= 44 => "C", >= 38 => "C-",
        >= 33 => "D+", >= 27 => "D", >= 22 => "D-",
        _ => "F",
    };

    public (int wins, int losses) Record(int id)
    {
        int w = 0, l = 0;
        foreach (var g in Season.Games)
        {
            if (!g.Played || (g.HomeId != id && g.AwayId != id)) continue;
            if (g.WinnerId == id) w++; else l++;
        }
        return (w, l);
    }

    // ------------------------------------------------------------------------------------------
    // The season (decision #96).

    private static readonly string[] Invitationals = { "Island Classic", "Empire Invitational", "Bayou Shootout", "Desert Showdown", "Pacific Rim Classic", "Lakeshore Invitational", "Smoky Mountain Classic", "Sunshine Showcase" };
    private static readonly string[] Showcases = { "Liberty Bowl Showcase", "Gateway Classic", "Peach Showcase", "Lone Star Classic", "Rose Garden Showcase", "Motor City Classic", "Crescent City Showcase", "Bluegrass Classic", "Golden Gate Showcase", "Windy City Classic", "Mile High Showcase", "Music City Classic" };

    public void StartSeason(int year, Rng rng)
    {
        var s = new CollegeSeason { Year = year };
        foreach (var t in Programs)
            s.Elo[t.Id] = 1500 + (QuickSim.Strength(t) - 85) * 8 + (Prestige[t.Id] - 50) * 2;
        var ids = Programs.Select(t => t.Id).ToList();
        // Invitationals (Thanksgiving week): eight brackets of eight, invited by prestige with some luck. H159 (run 156): drawn
        // before the non-conference rounds, so an invited program's non-conference count can allow for its bracket games.
        var invited = ids.OrderByDescending(i => Prestige[i] + rng.NextGaussian(0, 15)).Take(64).OrderBy(_ => rng.NextDouble()).ToList();
        ApplyHoliday(invited, year); // run 192: the coached program's holiday choice (no random draws)
        var invitedSet = invited.ToHashSet();
        // H159 (run 156): non-conference games fill each program's season to about 30 regular-season games (real D-I plays
        // 29-32), not a flat ten (a small conference's season was 24-28 games). Dates: Tuesdays and Fridays from November 3
        // to December 18 (not Thanksgiving week), then the days between the December showcases and conference play.
        var owed = ids.ToDictionary(i => i, i => NonConferenceTarget(ConferenceGames(InConference(ConferenceOf[i]).Count()), invitedSet.Contains(i)));
        var ncDates = NonConferenceDates(year);
        int ncRounds = ncDates.Count;
        // C5 part 2: listed rivals in different conferences meet every season, in the earliest round both are free
        // (no random numbers); home alternates by year.
        var booked = new List<HashSet<int>>();
        var rivalGames = new List<(int Round, int Home, int Away)>();
        foreach (var rv in NonConferenceRivalries())
        {
            int r = 0;
            while (r < ncRounds && r < booked.Count && (booked[r].Contains(rv.A) || booked[r].Contains(rv.B))) r++;
            if (r >= ncRounds) continue;
            while (booked.Count <= r) booked.Add(new HashSet<int>());
            booked[r].Add(rv.A); booked[r].Add(rv.B);
            bool aHome = (year + rv.A + rv.B) % 2 == 0;
            rivalGames.Add((r, aHome ? rv.A : rv.B, aHome ? rv.B : rv.A));
        }
        BookPlanned(year, ncRounds, owed, booked, rivalGames); // run 192: the coached program's buy games and home-and-homes
        for (int round = 0; round < ncRounds; round++)
        {
            var day = ncDates[round];
            var taken = round < booked.Count ? booked[round] : null;
            foreach (var (_, h, a) in rivalGames.Where(x => x.Round == round))
            {
                s.Games.Add(new CollegeGame { Date = day, HomeId = h, AwayId = a, Stage = "Non-conference" });
                owed[h]--; owed[a]--;
            }
            int left = ncRounds - round;
            // Who plays today: a program that must play every remaining day to reach its count always does; the rest play
            // in proportion to the games they still need, leaning to November (as the real season does), so it runs to New Year.
            var pool = ids.Where(i => taken?.Contains(i) != true && owed[i] > 0)
                .Select(i => (id: i, urgent: owed[i] - (left - RivalDaysLeft(rivalGames, i, round)), roll: rng.NextDouble()))
                .Where(x => x.urgent >= 0 || x.roll < FrontLoad * owed[x.id] / left)
                .OrderByDescending(x => x.urgent >= 0).ThenBy(x => x.roll).Select(x => x.id).ToList();
            while (pool.Count >= 2)
            {
                int a = pool[0]; pool.RemoveAt(0);
                int j = pool.FindIndex(b => ConferenceOf[b] != ConferenceOf[a]);
                if (j < 0) continue; // nobody from another conference is free today: he waits for the next round
                int b = pool[j]; pool.RemoveAt(j);
                // H159: a power program hosts a low-major (Duke opened the season at Queens). Home odds lean to the bigger name.
                double aHome = 0.5 + Math.Clamp((Prestige[a] - Prestige[b]) / 50.0, -0.45, 0.45);
                bool home = rng.Chance(aHome);
                s.Games.Add(new CollegeGame { Date = day, HomeId = home ? a : b, AwayId = home ? b : a, Stage = "Non-conference" });
                owed[a]--; owed[b]--;
            }
        }
        for (int e = 0; e < Invitationals.Length; e++)
        {
            var field = invited.Skip(e * 8).Take(8).OrderByDescending(i => s.Elo[i]).ToList();
            var idates = new List<DateOnly> { new(year, 11, 25), new(year, 11, 26), new(year, 11, 27) };
            AddKnockout(s, Invitationals[e], "Invitational", Seeded(field), idates);
        }
        // Conference play: a single round robin, Tuesdays and Saturdays from early January. R3: a real conference of ten
        // or fewer plays home and away (as the Ivy and most small leagues do), so every program plays about 30 games.
        foreach (var conf in Active)
        {
            var members = InConference(conf.Key).Select(t => t.Id).ToList();
            var rounds = RoundRobin(members);
            var reversed = rounds.Select(r => r.Select(g => (g.Item2, g.Item1)).ToList()).ToList();
            if (members.Count <= 10)
            {
                rounds = rounds.Concat(reversed).ToList();
                var d = new DateOnly(year + 1, 1, 3);
                foreach (var r in rounds)
                {
                    while (d.DayOfWeek is not (DayOfWeek.Tuesday or DayOfWeek.Saturday)) d = d.AddDays(1);
                    foreach (var (h, a) in r) s.Games.Add(new CollegeGame { Date = d, HomeId = h, AwayId = a, Stage = "Conference" });
                    d = d.AddDays(1);
                }
                continue;
            }
            // H162 (run 138): a big conference played a single round robin (Providence's Big East season was 21 games, the
            // SEC's 15 conference games). Now an 11-team league plays home and away (20 games, as the Big East does) and a
            // bigger one adds a partial second round, 18-19 games each (the SEC plays 18, the ACC and Big Ten 20).
            // The rounds run from early January to the day before the conference tournaments.
            rounds = rounds.Concat(reversed.Take(BigConferenceRounds(members.Count) - rounds.Count)).ToList();
            var dates = ConferenceDates(year, rounds.Count);
            for (int i = 0; i < rounds.Count; i++)
                foreach (var (h, a) in rounds[i]) s.Games.Add(new CollegeGame { Date = dates[i], HomeId = h, AwayId = a, Stage = "Conference" });
        }
        Season = s;
    }

    /// <summary>H159 (run 156): the regular season a program is scheduled to (non-conference plus conference plus its bracket).</summary>
    public const int TargetRegularSeason = 30;
    /// <summary>H159: the invitational games an invited program counts on (one to three in a bracket of eight). H171 (run 163):
    /// 3, the most it can play, so a deep run (and a top-30 program's December showcase) no longer pushes a champion to 42
    /// games: a 20-game conference's invited program now books 7 non-conference games and plays 30-31 before March.</summary>
    /// H159 (run 176): 2, the bracket's expected games rounded up (1 + 1/2 + 1/4 ≈ 1.75). With 3, a first-round loser played
    /// 28 (Virginia Tech 12-16, Providence's 20 Big East + 7 + 1); now an invited program books one more non-conference game
    /// and plays 29 (out first) to 31 (champion), 32 with a December showcase.
    public const int InvitationalAllowance = 2;
    /// <summary>H159: how much a program's non-conference games lean to the early days (1 = evenly spread).</summary>
    public const double FrontLoad = 1.6;

    /// <summary>H159 (run 156): non-conference games for a program with <paramref name="conferenceGames"/> conference games,
    /// filling its season to <see cref="TargetRegularSeason"/> (7 to 16; H171, run 163; an invited program books 8 in a 20-game conference from run 176).</summary>
    public static int NonConferenceTarget(int conferenceGames, bool invited) =>
        Math.Clamp(TargetRegularSeason - conferenceGames - (invited ? InvitationalAllowance : 0), 7, 16); // H171: 7 (was 8)

    /// <summary>H159 (run 156): non-conference game days. Tuesdays and Fridays from November 3 to December 18 (and November's
    /// Saturdays), skipping
    /// Thanksgiving week (the invitationals) and before the December showcases (19-21); then December 22, 23, 28, 29 and 30.
    /// Every year has at least 19 (the most a program needs is 16, with room for a day it sits out).</summary>
    public static List<DateOnly> NonConferenceDates(int year)
    {
        var list = new List<DateOnly>();
        for (var d = new DateOnly(year, 11, 3); d <= new DateOnly(year, 12, 18); d = d.AddDays(1))
            if ((d.DayOfWeek is DayOfWeek.Tuesday or DayOfWeek.Friday || d.DayOfWeek == DayOfWeek.Saturday && d.Month == 11) && !(d.Month == 11 && d.Day >= 23 && d.Day <= 29)) list.Add(d);
        foreach (int day in new[] { 22, 23, 28, 29, 30 }) list.Add(new DateOnly(year, 12, day)); // run 156 review: 19+ days every year
        return list;
    }

    /// <summary>The rival games a program has booked after <paramref name="round"/> (days it plays anyway).</summary>
    private static int RivalDaysLeft(List<(int Round, int Home, int Away)> rivals, int id, int round) =>
        rivals.Count(x => x.Round > round && (x.Home == id || x.Away == id));

    /// <summary>H166 (run 144): conference games a program plays in a league of <paramref name="members"/> (the schedule above):
    /// home and away up to 11 (2 × (n − 1)), 19 for an even big league, and an odd big league's 20 rounds less its byes.</summary>
    public static int ConferenceGames(int members)
    {
        if (members <= 1) return 0;
        if (members <= 11) return 2 * (members - 1);
        if (members % 2 == 0) return BigConferenceRounds(members);
        int second = BigConferenceRounds(members) - members; // the reversed rounds after a full round robin of n rounds
        return members - 1 + (int)Math.Round(second * (members - 1) / (double)members);
    }

    /// <summary>
    /// H166 (run 144): the regular-season games a program plays this season (<see cref="IsRegularSeason"/>), the count the
    /// athletic director's target and pace use. From the schedule once it exists: its non-conference and conference games,
    /// the invitational games actually played (3, <see cref="InvitationalAllowance"/>, while an invited program's bracket is still going) and a showcase once drawn.
    /// Before a schedule exists: <see cref="NonConferenceTarget"/> plus <see cref="ConferenceGames"/> (about 30; H159, run 156).
    /// </summary>
    public int RegularSeasonGames(int id, int? conferenceSize = null)
    {
        int confSize = conferenceSize ?? (ConferenceOf.TryGetValue(id, out var key) ? InConference(key).Count() : 14);
        var mine = Season?.Games.Where(g => (g.HomeId == id || g.AwayId == id) && IsRegularSeason(g)).ToList();
        if (mine == null || !mine.Any(g => g.Stage == "Conference")) return NonConferenceTarget(ConferenceGames(confSize), false) + ConferenceGames(confSize); // H159: about 30
        int games = mine.Count(g => g.Stage is "Non-conference" or "Conference" or "Showcase");
        var inv = mine.Where(g => g.Stage == "Invitational").ToList();
        if (inv.Count > 0)
        {
            bool settled = inv.All(g => g.Played) && (inv.Any(g => g.WinnerId != id) || Season!.EventWinners.ContainsKey(inv[0].Event ?? ""));
            games += settled ? inv.Count : Math.Max(inv.Count, InvitationalAllowance); // H171 (run 163): what the schedule was built on
        }
        return games;
    }

    /// <summary>H162 (run 138): conference rounds for a league of 11 or more: home and away for 11 (22 rounds with a bye each,
    /// 20 games), otherwise 19 for an even league (19 games each) and 20 for an odd one (a bye a round: 18-19 games each).</summary>
    public static int BigConferenceRounds(int members) => members == 11 ? 22 : members % 2 == 0 ? 19 : 20;

    /// <summary>H162 (run 138): a big conference's game days, January 2 to March 9 (the conference tournaments start March 10):
    /// Tuesdays and Saturdays, and the Thursdays of the earliest weeks when a league needs more days.</summary>
    public static List<DateOnly> ConferenceDates(int year, int rounds)
    {
        var first = new DateOnly(year + 1, 1, 2);
        var last = new DateOnly(year + 1, 3, 9);
        var all = Enumerable.Range(0, last.DayNumber - first.DayNumber + 1).Select(first.AddDays).ToList();
        var dates = all.Where(d => d.DayOfWeek is DayOfWeek.Tuesday or DayOfWeek.Saturday).ToList();
        dates.AddRange(all.Where(d => d.DayOfWeek == DayOfWeek.Thursday).Take(Math.Max(0, rounds - dates.Count)));
        dates.Sort();
        // Drop the earliest days a league doesn't need.
        return dates.Skip(Math.Max(0, dates.Count - rounds)).ToList();
    }

    private static List<List<(int, int)>> RoundRobin(List<int> teams)
    {
        var list = teams.ToList();
        if (list.Count % 2 == 1) list.Add(-1);
        int n = list.Count;
        var rounds = new List<List<(int, int)>>();
        for (int r = 0; r < n - 1; r++)
        {
            var round = new List<(int, int)>();
            for (int i = 0; i < n / 2; i++)
            {
                int a = list[i], b = list[n - 1 - i];
                if (a >= 0 && b >= 0) round.Add((r + i) % 2 == 0 ? (a, b) : (b, a));
            }
            rounds.Add(round);
            list.Insert(1, list[^1]); list.RemoveAt(list.Count - 1);
        }
        return rounds;
    }

    /// <summary>Bracket order for a seeded field (1 v 8, 4 v 5, 3 v 6, 2 v 7 ...).</summary>
    private static List<int> Seeded(List<int> bySeed)
    {
        var order = new List<int> { 0 };
        while (order.Count < bySeed.Count) { int m = order.Count * 2; order = order.SelectMany(x => new[] { x, m - 1 - x }).ToList(); }
        return order.Select(i => bySeed[i]).ToList();
    }

    private static void AddKnockout(CollegeSeason s, string name, string stage, List<int> bracket, List<DateOnly> dates)
    {
        s.RoundDates[name] = dates;
        for (int i = 0; i < bracket.Count; i += 2)
            s.Games.Add(new CollegeGame { Date = dates[0], HomeId = bracket[i], AwayId = bracket[i + 1], Stage = stage, Event = name, Round = 0, Slot = i / 2 });
    }

    // ------------------------------------------------------------------------------------------
    // Playing

    /// <summary>Play every game up to and including <paramref name="date"/>, adding knockout rounds and stages as they come.</summary>
    public void PlayThrough(DateOnly date, GameSimulator sim, Rng rng)
    {
        RecruitingThrough(date, rng);
        var s = Season;
        while (true)
        {
            bool added = Advance(rng);
            var next = s.Games.Where(g => !g.Played).OrderBy(g => g.Date).FirstOrDefault();
            if (next == null || next.Date > date)
            {
                if (!added)
                {
                    // K35 (run 193): a Monday without games still brings the new poll (once the season has its first).
                    if (s.PollRelease.Count > 0) ReleasePoll(date);
                    return;
                }
                continue;
            }
            var day = s.Games.Where(g => !g.Played && g.Date == next.Date).ToList();
            // The poll before the day's games, for the arrows (#528); each game records both sides' ranks at tip-off.
            BeforeGameDay(next.Date);
            foreach (var g in day) TipOff(g); // games added to the day after its first tip-off (none today, but cheap)
            foreach (var g in day) Play(g, sim, rng);
        }
    }

    private void Play(CollegeGame g, GameSimulator sim, Rng rng)
    {
        var home = Program(g.HomeId)!; var away = Program(g.AwayId)!;
        // March Madness gets the full match engine (decision #95), and so does every game of a program you coach (C2, #168).
        // Run 193: the NIT plays on its own stream (a hash of the game), so it never moves March Madness or anyone else's dice.
        if (g.Event == Nit) rng = NitRng(Season.Year, g);
        if (g.Event == Madness || g.HomeId == ManagedProgramId || g.AwayId == ManagedProgramId)
            Record(g, sim.Simulate(home, away, GameRules(g), rng.NextULong(), false, g.Date));
        else
        {
            var (hp, ap) = QuickSim.Play(home, away, g.Date, rng, Season.Stats, Rules.College, 72, neutral: g.IsNeutral(), homePoints: HomeCourtPoints(g.HomeId), totalScale: StyleOf(g).PointsScale); // NCAA ~72 a side
            g.HomePts = hp; g.AwayPts = ap;
            UpdateElo(g);
            NoteRivalry(g); // C5 part 1: no random numbers
        }
    }

    /// <summary>College rules for a game: events (invitationals, showcases, tournaments) are on a neutral court.</summary>
    public static Rules RulesFor(CollegeGame g) => g.Event == Madness ? Rules.CollegeMadness : g.IsNeutral() ? Rules.CollegeNeutral : Rules.College; // K21 (run 163)

    /// <summary>College rules for a game in this world: a neutral court for events, else the home program's own arena (C5 part 2).</summary>
    public Rules GameRules(CollegeGame g) =>
        StyleOf(g).Apply(g.Event == Madness ? Rules.CollegeMadness // K21 (run 163): March's single-game spread
            : g.IsNeutral() ? Rules.CollegeNeutral : Rules.College with { HomeScale = HomeCourtPoints(g.HomeId) / 2.5 }); // C5: + the conference's style

    /// <summary>The program you coach (college career, slice C2): its games always use the full engine. Not saved; the session sets it.</summary>
    public int? ManagedProgramId { get; set; }

    /// <summary>CP3 (run 132): every graduate's program and the summer he turned pro (player id → record), kept for the
    /// alumni tracker and the recruiting pitch. Saved in the college world_meta row; careers from before v0.120.0 start
    /// with none and gain each class as it graduates.</summary>
    public Dictionary<int, Alumnus> Alumni { get; } = new();

    /// <summary>CP3: the coached program's pro record as recruits hear it (set by the session, not saved): appeal points
    /// added for the coached program only, and the words behind them.</summary>
    public double ProPitch { get; set; }
    public string ProPitchText { get; set; } = "";

    /// <summary>A program's unplayed game on a date (null when it doesn't play).</summary>
    public CollegeGame? GameOn(int teamId, DateOnly date) =>
        Season.Games.FirstOrDefault(g => !g.Played && g.Date == date && (g.HomeId == teamId || g.AwayId == teamId));

    /// <summary>Every game of a program this season, in date order.</summary>
    public List<CollegeGame> GamesOf(int teamId) =>
        Season.Games.Where(g => g.HomeId == teamId || g.AwayId == teamId).OrderBy(g => g.Date).ThenBy(g => g.Round).ToList();

    /// <summary>Record a game played on the full engine: the score, every stat line and the ratings.</summary>
    public void Record(CollegeGame g, GameResult r)
    {
        BeforeGameDay(g.Date);
        TipOff(g);
        g.HomePts = r.Home.Points; g.AwayPts = r.Away.Points;
        foreach (var box in new[] { r.Home, r.Away })
        foreach (var l in box.Lines.Where(l => l.Seconds > 0))
        {
            var st = Season.Stats.TryGetValue(l.Player.Id, out var x) ? x : Season.Stats[l.Player.Id] = new StatLine();
            st.TeamId = box.Team.Id; st.G++; if (l.Started) st.Gs++; st.Min += l.Minutes; st.Pts += l.Points; st.Reb += l.Rebounds; st.Ast += l.Assists;
            st.Stl += l.Steals; st.Blk += l.Blocks; st.Fgm += l.FGM; st.Fga += l.FGA; st.Tpm += l.ThreePM; st.Tpa += l.ThreePA;
        }
        if (g.Event == Madness)
        {
            static GameStar? Star(TeamBox box) => box.Lines.Where(l => l.Seconds > 0)
                .OrderByDescending(l => l.Points).ThenByDescending(l => l.Rebounds + l.Assists).ThenBy(l => l.Player.Id)
                .Select(l => new GameStar(l.Player.Id, l.Player.Name, l.Points, l.Rebounds, l.Assists)).FirstOrDefault();
            g.HomeStar = Star(r.Home); g.AwayStar = Star(r.Away); g.Overtimes = r.Overtimes;
        }
        UpdateElo(g);
        NoteRivalry(g);
    }

    /// <summary>The round a program reached in this season's March Madness (-1 = not in the field; 0 = lost in the First Four ... 7 = champion).</summary>
    public int MadnessReached(int teamId)
    {
        var s = Season;
        if (s.ChampionId == teamId) return 7;
        var mine = s.Games.Where(g => g.Event == Madness && (g.HomeId == teamId || g.AwayId == teamId)).ToList();
        if (mine.Count == 0) return s.Seeds.ContainsKey(teamId) && s.Bracket.Contains(teamId) ? 1 : -1;
        return mine.Max(g => g.Round);
    }

    private void UpdateElo(CollegeGame g)
    {
        var elo = Season.Elo;
        double rh = elo.GetValueOrDefault(g.HomeId, 1500), ra = elo.GetValueOrDefault(g.AwayId, 1500);
        bool neutral = g.IsNeutral();
        double expected = 1 / (1 + Math.Pow(10, (ra - rh - (neutral ? 0 : 60 * HomeCourtPoints(g.HomeId) / 2.5)) / 400));
        double result = g.HomePts > g.AwayPts ? 1 : 0;
        double margin = Math.Log(Math.Abs(g.HomePts!.Value - g.AwayPts!.Value) + 1);
        double k = 24 * margin * 0.6;
        elo[g.HomeId] = rh + k * (result - expected);
        elo[g.AwayId] = ra - k * (result - expected);
        _pollVersion++;
    }

    /// <summary>Add whatever is now due: next knockout rounds, showcases, conference tournaments, selection, the champion. True if anything was added.</summary>
    private bool Advance(Rng rng)
    {
        var s = Season;
        bool changed = false;
        // Next rounds of every knockout whose current round is finished.
        foreach (var (name, dates) in s.RoundDates)
        {
            var games = s.Games.Where(g => g.Event == name).ToList();
            if (games.Count == 0) continue;
            int round = games.Max(g => g.Round);
            var current = games.Where(g => g.Round == round).OrderBy(g => g.Slot).ToList();
            if (current.Any(g => !g.Played)) continue;
            if (name == Madness && round == 0 && !games.Any(g => g.Round == 1))
            {
                // First Four winners take their slots in the bracket; the Round of 64 begins.
                foreach (var g in current) s.Bracket[g.Slot] = g.WinnerId!.Value;
                MadnessRoundNews(0, current);
                for (int i = 0; i < 64; i += 2)
                    s.Games.Add(new CollegeGame { Date = dates[1].AddDays(i < 32 ? 0 : 1), HomeId = s.Bracket[i], AwayId = s.Bracket[i + 1], Stage = Madness, Event = Madness, Round = 1, Slot = i / 2 });
                changed = true;
                continue;
            }
            if (current.Count == 1)
            {
                if (!s.EventWinners.ContainsKey(name)) { s.EventWinners[name] = current[0].WinnerId!.Value; changed = true; Crowned(name, current[0]); }
                continue;
            }
            int next = round + 1;
            if (name == Madness) MadnessRoundNews(round, current);
            int dateIndex = name == Madness ? next : next;
            var date = dateIndex < dates.Count ? dates[dateIndex] : current.Max(g => g.Date).AddDays(1);
            for (int i = 0; i < current.Count; i += 2)
            {
                var ng = new CollegeGame { Date = date, HomeId = current[i].WinnerId!.Value, AwayId = current[i + 1].WinnerId!.Value, Stage = current[i].Stage, Event = name, Round = next, Slot = i / 2 };
                if (name == Nit) NitHost(ng);
                s.Games.Add(ng);
            }
            changed = true;
        }
        if (changed) return true;

        // December showcases once the non-conference season and invitationals are done.
        // H159 (run 156): non-conference games now run on after the showcases (December 22-30), so only those before them count.
        if (!s.Games.Any(g => g.Stage == "Showcase") && s.Games.Where(g => g.Stage is "Non-conference" or "Invitational" && g.Date < new DateOnly(s.Year, 12, 19)).All(g => g.Played))
        {
            var top = Programs.Select(t => t.Id).OrderByDescending(i => s.Elo[i]).Take(30).ToList();
            // Run 192: a coached program that asked for a showcase when the schedule was drawn gets a top-30 opponent (neutral floor).
            // Run 192 review: moved near the front, so it is paired whatever its rating (only 24 of the 30 get a game).
            if (ManagedProgramId is { } asker && PlanOf(asker) is { DrawnShowcase: true } ask && ask.DrawnYear == s.Year && Program(asker) != null
                && top.IndexOf(asker) is var at && (at < 0 || at >= 2 * Showcases.Length - 2))
            {
                if (at >= 0) top.RemoveAt(at); else top.RemoveAt(top.Count - 1);
                top.Insert(1, asker);
            }
            var day = new DateOnly(s.Year, 12, 19);
            int n = 0;
            while (top.Count >= 2 && n < Showcases.Length)
            {
                int a = top[0]; top.RemoveAt(0);
                int j = top.FindIndex(b => ConferenceOf[b] != ConferenceOf[a]);
                if (j < 0) j = 0;
                int b = top[j]; top.RemoveAt(j);
                s.Games.Add(new CollegeGame { Date = day.AddDays(n % 3), HomeId = a, AwayId = b, Stage = "Showcase", Event = Showcases[n], Round = 0 });
                n++;
            }
            return true;
        }
        // Conference tournaments: the top eight of each conference, after the regular season.
        if (!s.Games.Any(g => g.Stage == "Conference tournament") && s.Games.Where(g => g.Stage == "Conference").All(g => g.Played) && s.Games.Any(g => g.Stage == "Conference"))
        {
            var dates = new List<DateOnly> { new(s.Year + 1, 3, 10), new(s.Year + 1, 3, 11), new(s.Year + 1, 3, 12) };
            foreach (var conf in Active)
            {
                var table = ConferenceTable(conf.Key).Take(8).Select(r => r.TeamId).ToList();
                AddKnockout(s, $"{conf.Short} tournament", "Conference tournament", Seeded(table), dates);
            }
            return true;
        }
        // Selection Sunday: 68 teams, the First Four, then the bracket.
        if (!s.Selected && s.Games.Any(g => g.Stage == "Conference tournament") && Active.All(c => s.EventWinners.ContainsKey($"{c.Short} tournament")))
        {
            foreach (var conf in Active)
                if (s.EventWinners.TryGetValue($"{conf.Short} tournament", out var w)) s.ConferenceChampions[conf.Key] = w;
            Select(rng);
            return true;
        }
        if (s.Selected && s.ChampionId == null && s.EventWinners.TryGetValue(Madness, out var champ))
        {
            var final = s.Games.Where(g => g.Event == Madness).OrderBy(g => g.Round).Last();
            s.ChampionId = champ; s.RunnerUpId = final.HomeId == champ ? final.AwayId : final.HomeId;
            var c = Program(champ)!; var r = Program(s.RunnerUpId.Value)!;
            History.Add(new CollegeChampion(s.Year, champ, c.FullName, r.Id, r.FullName));
            News.Add(ChampionNews(final));
            // CS5 (run 135): the season's honours are voted with the title decided (their own random stream, #972).
            // Review (run 136): the honours are voted on the poll as the regular season left it, not the final (March) poll,
            // which Coach of the Year's March term already counts; then the final poll takes over (H154).
            _preMarchPoll = true; InvalidatePoll();
            var awards = VoteAwards();
            _preMarchPoll = false; InvalidatePoll();
            Awards.RemoveAll(a => a.Year == s.Year);
            Awards.Add(awards);
            if (awards.PlayerOfYearId != null) News.Add(AwardsNews(awards));
            return true;
        }
        return false;
    }

    /// <summary>"12 Vermont" — a team with its seed, as the bracket shows it.</summary>
    public string WithSeed(int id) => $"{Season.Seeds.GetValueOrDefault(id)} {Program(id)!.FullName}";

    private string Score(CollegeGame g)
    {
        int w = g.WinnerId!.Value, l = g.HomeId == w ? g.AwayId : g.HomeId;
        int wp = Math.Max(g.HomePts!.Value, g.AwayPts!.Value), lp = Math.Min(g.HomePts!.Value, g.AwayPts!.Value);
        return $"{WithSeed(w)} beat {WithSeed(l)} {wp}-{lp}";
    }

    /// <summary>A win by a seed at least four lines worse (the bracket's own rule).</summary>
    public bool IsUpset(CollegeGame g)
    {
        if (g.WinnerId is not { } w) return false;
        int l = g.HomeId == w ? g.AwayId : g.HomeId;
        return Season.Seeds.GetValueOrDefault(w) - Season.Seeds.GetValueOrDefault(l) >= 4;
    }

    /// <summary>A double-digit seed still alive in the Sweet 16 or later (or a 7-seed or worse in the Final Four).</summary>
    public static bool IsCinderella(int seed, int round) => round >= 5 ? seed >= 7 : seed >= 10;

    /// <summary>
    /// The news after each round of March Madness (C4 part 2, #303): who went through, the upsets with their scores, and
    /// a Cinderella story when a double-digit seed goes deep. World news goes to the digest unless it names your team.
    /// </summary>
    private void MadnessRoundNews(int round, List<CollegeGame> games)
    {
        var s = Season;
        int y = s.Year + 1;
        var winners = games.Select(g => g.WinnerId!.Value).ToList();
        var upsets = games.Where(IsUpset).ToList();
        string UpsetLines() => upsets.Count == 0 ? "" : "\nUpsets: " + string.Join("; ", upsets.Select(Score)) + ".";
        switch (round)
        {
            case 0:
                News.Add(("March Madness: the First Four", $"Through to the round of 64: {string.Join(", ", winners.Select(WithSeed))}."
                    + "\n" + string.Join("\n", games.Select(Score))));
                break;
            case 1:
            {
                var big = upsets.Where(g => s.Seeds.GetValueOrDefault(g.WinnerId!.Value) >= 15).ToList();
                foreach (var g in big)
                {
                    int w = g.WinnerId!.Value; int sw = s.Seeds.GetValueOrDefault(w);
                    int l = g.HomeId == w ? g.AwayId : g.HomeId;
                    News.Add(($"{char.ToUpper(Article(sw)[0])}{Article(sw)[1..]}-seed beats {Article(s.Seeds.GetValueOrDefault(l))}-seed: {Program(w)!.FullName} stun {Program(l)!.FullName}",
                        $"{Score(g)} in the round of 64, one of the biggest upsets the tournament can produce."));
                }
                string title = upsets.Count switch
                {
                    0 => "March Madness: the favourites hold in the round of 64",
                    1 => "March Madness: one upset in the round of 64",
                    _ => $"March Madness: {upsets.Count} upsets in the round of 64",
                };
                News.Add((title, $"32 teams go through to the round of 32.{UpsetLines()}"));
                break;
            }
            case 2:
                News.Add(("March Madness: the Sweet 16 is set", RegionLines(winners, 4) + UpsetLines()));
                CinderellaNews(winners, 3);
                break;
            case 3:
                News.Add(("March Madness: the Elite Eight is set", RegionLines(winners, 2) + UpsetLines()));
                CinderellaNews(winners, 4);
                break;
            case 4:
            {
                var ff = winners.Select((w, i) => $"{WithSeed(w)} ({MadnessBracket.Regions[i]})");
                News.Add(($"The Final Four: {string.Join(", ", winners.Select(w => Program(w)!.FullName))}",
                    $"The {y} Final Four: {string.Join(", ", ff)}. The semi-finals are on {s.RoundDates[Madness][5]:MMMM d} in {MadnessBracket.FinalFourHost(y)}." + UpsetLines()));
                CinderellaNews(winners, 5);
                break;
            }
            case 5:
            {
                var (a, b) = (winners[0], winners[1]);
                News.Add(($"The final: {Program(a)!.FullName} v {Program(b)!.FullName}",
                    $"{string.Join(". ", games.Select(Score))}. The {y} championship game is on {s.RoundDates[Madness][6]:MMMM d}."));
                break;
            }
        }
    }

    /// <summary>The teams still in, region by region ("East: 1 Duke, 4 Purdue, ...").</summary>
    private string RegionLines(List<int> winners, int perRegion) =>
        string.Join("\n", winners.Chunk(perRegion).Select((c, r) => $"{MadnessBracket.Regions[r]}: {string.Join(", ", c.Select(WithSeed))}."));

    private void CinderellaNews(List<int> alive, int reached)
    {
        string stage = reached switch { 3 => "the Sweet 16", 4 => "the Elite Eight", _ => "the Final Four" };
        foreach (var id in alive.Where(i => IsCinderella(Season.Seeds.GetValueOrDefault(i), reached)))
        {
            var t = Program(id)!;
            var beaten = Season.Games.Where(g => g.Event == Madness && g.Round >= 1 && g.WinnerId == id)
                .Select(g => WithSeed(g.HomeId == id ? g.AwayId : g.HomeId)).ToList();
            News.Add(($"Cinderella: {Season.Seeds.GetValueOrDefault(id)}-seed {t.FullName} reach {stage}",
                $"{t.FullName}, {Article(Season.Seeds.GetValueOrDefault(id))}-seed from the {ConferenceShort(id)}, are into {stage} after beating {string.Join(", then ", beaten)}."));
        }
    }

    /// <summary>A March Madness story's title (the field, each round, a Cinderella, the champion).</summary>
    public static bool IsMadnessHeadline(string title) =>
        title.StartsWith("March Madness:") || title.StartsWith("The Final Four:") || title.StartsWith("The final: ")
        || title.StartsWith("Cinderella") || title.EndsWith("win March Madness") || title.Contains("-seed beats ");

    /// <summary>"an 11", "an 8", "a 12".</summary>
    public static string Article(int n) => (n is 8 or 11 or 18 ? "an " : "a ") + n;

    private string ConferenceShort(int id) =>
        ConferenceOf.TryGetValue(id, out var key) && Active.FirstOrDefault(c => c.Key == key) is { } c ? c.Short : "field";

    /// <summary>The champion's story: the seed, the final's score and the road there.</summary>
    private (string, string) ChampionNews(CollegeGame final)
    {
        var s = Season;
        int champ = final.WinnerId!.Value;
        var c = Program(champ)!;
        int seed = s.Seeds.GetValueOrDefault(champ);
        var road = s.Games.Where(g => g.Event == Madness && g.Round >= 1 && g.Round <= 5 && g.WinnerId == champ).OrderBy(g => g.Round)
            .Select(g => WithSeed(g.HomeId == champ ? g.AwayId : g.HomeId)).ToList();
        string title = seed >= 7 ? $"Cinderella's crown: {seed}-seed {c.FullName} win March Madness" : $"{c.FullName} win March Madness";
        return (title, $"{Score(final)} in the final to win the {s.Year + 1} college championship as {Article(seed)}-seed."
            + (road.Count > 0 ? $"\nThe road: {string.Join(", ", road)}." : ""));
    }

    private void Crowned(string name, CollegeGame final)
    {
        if (name == Madness) return; // the champion is announced with the final
        if (name == Nit) { NitCrowned(final); return; }
        var w = Program(final.WinnerId!.Value)!;
        if (name.EndsWith("tournament")) News.Add(($"{w.FullName} win the {name}", $"{w.FullName} take the {name} and the automatic bid to March Madness."));
    }

    /// <summary>Selection: one automatic bid per conference (its tournament champion) and the rest of the 68 at-large by rating; the bottom eight play the First Four.</summary>
    private void Select(Rng rng)
    {
        var s = Season;
        var auto = new Dictionary<int, string>();
        foreach (var conf in Active)
            if (s.ConferenceChampions.TryGetValue(conf.Key, out var champ)) auto.TryAdd(champ, conf.Short);
        var p = Project(auto, final: true, auto.Count);
        var field = p.Field;
        s.SelectionOrder = field.ToList(); s.LastFourIn = p.LastFourIn; s.FirstFourOut = p.FirstFourOut; s.NextFourOut = p.NextFourOut;
        // Seed lines 1-15: four teams each, snaked across four regions; line 16: the First Four winners.
        int[] order = { 1, 16, 8, 9, 5, 12, 4, 13, 6, 11, 3, 14, 7, 10, 2, 15 };
        int y = s.Year + 1;
        var dates = new List<DateOnly> { new(y, 3, 17), new(y, 3, 19), new(y, 3, 21), new(y, 3, 26), new(y, 3, 28), new(y, 4, 4), new(y, 4, 6) };
        s.RoundDates[Madness] = dates;
        // Seed lines from the projection, best first; a line of six (the real First Four, #305) puts its two direct teams
        // in the regions the snake gives them and its two First Four games in the other two.
        var lines = Enumerable.Range(1, 16).Select(l => field.Where(i => p.Seeds[i] == l).ToList()).ToList();
        if (lines.Take(15).All(l => l.Count == 4))
        {
            // Lines 1-15 hold four teams each; the bottom eight are the First Four's 16-seeds (as before v0.59.0).
            var regions = new List<int>[4];
            for (int r = 0; r < 4; r++) regions[r] = new List<int>();
            for (int line = 0; line < 15; line++)
            {
                var four = lines[line].ToList();
                if (line % 2 == 1) four.Reverse();
                for (int r = 0; r < 4; r++) { regions[r].Add(four[r]); s.Seeds[four[r]] = line + 1; }
            }
            var firstFour = lines[15];
            foreach (var t in firstFour) s.Seeds[t] = 16;
            s.Bracket = new List<int>();
            for (int r = 0; r < 4; r++)
                foreach (int seed in order) s.Bracket.Add(seed == 16 ? 0 : regions[r][seed - 1]);
            var slots = Enumerable.Range(0, 64).Where(i => s.Bracket[i] == 0).ToList();
            for (int i = 0; i < 4; i++)
                s.Games.Add(new CollegeGame { Date = dates[0], HomeId = firstFour[i * 2], AwayId = firstFour[i * 2 + 1], Stage = Madness, Event = Madness, Round = 0, Slot = slots[i] });
        }
        else
        {
            var grid = new int[4, 16];
            var playIns = new List<(int A, int B, int Region, int Seed)>();
            for (int line = 0; line < 16; line++)
            {
                var regionOrder = line % 2 == 1 ? new[] { 3, 2, 1, 0 } : new[] { 0, 1, 2, 3 };
                var teams = lines[line];
                foreach (var t in teams) s.Seeds[t] = line + 1;
                if (teams.Count == 4) { for (int k = 0; k < 4; k++) grid[regionOrder[k], line] = teams[k]; continue; }
                // Six on the line: the direct pair first, then the First Four's two games (the last four in on line 11,
                // the weakest automatic qualifiers on line 16).
                var playIn = line == 10 ? teams.Where(p.LastFourIn.Contains).ToList() : teams.Skip(teams.Count - 4).ToList();
                var direct = teams.Where(t => !playIn.Contains(t)).ToList();
                grid[regionOrder[0], line] = direct[0]; grid[regionOrder[1], line] = direct[1];
                playIns.Add((playIn[0], playIn[1], regionOrder[2], line + 1));
                playIns.Add((playIn[2], playIn[3], regionOrder[3], line + 1));
            }
            s.Bracket = new List<int>();
            for (int r = 0; r < 4; r++)
                foreach (int seed in order) s.Bracket.Add(grid[r, seed - 1]);
            foreach (var (a, b, r, seed) in playIns.OrderBy(x => x.Seed))
                s.Games.Add(new CollegeGame { Date = dates[0], HomeId = a, AwayId = b, Stage = Madness, Event = Madness, Round = 0, Slot = r * 16 + Array.IndexOf(order, seed) });
        }
        s.Selected = true;
        var ones = field.Take(4).Select(i => Program(i)!.FullName);
        string Names(List<int> ids) => string.Join(", ", ids.Select(i => Program(i)!.FullName));
        News.Add(("March Madness: the field is set", $"68 teams are in. The No. 1 seeds: {string.Join(", ", ones)}."
            + (p.LastFourIn.Count > 0 ? $"\nLast four in: {Names(p.LastFourIn)}.\nFirst four out: {Names(p.FirstFourOut)}." : "")));
        SelectNit(p);
        SelectionNews(p);
    }

    /// <summary>A conference's regular-season table.</summary>
    public List<(int TeamId, int Wins, int Losses)> ConferenceTable(string key)
    {
        var members = InConference(key).Select(t => t.Id).ToHashSet();
        var rows = members.ToDictionary(i => i, _ => (w: 0, l: 0));
        foreach (var g in Season.Games.Where(g => g.Stage == "Conference" && g.Played && members.Contains(g.HomeId) && members.Contains(g.AwayId)))
        {
            var w = g.WinnerId!.Value; var l = w == g.HomeId ? g.AwayId : g.HomeId;
            rows[w] = (rows[w].w + 1, rows[w].l); rows[l] = (rows[l].w, rows[l].l + 1);
        }
        return rows.Select(kv => (kv.Key, kv.Value.w, kv.Value.l)).OrderByDescending(r => r.Item2 - r.Item3).ThenByDescending(r => Season.Elo[r.Key]).ToList();
    }

    /// <summary>The Top 25 by rating: re-ranked after every game (#528).</summary>
    public List<Team> Top25() => PollOrder().Select(id => Program(id)!).ToList();

    public string StageText
    {
        get
        {
            var s = Season;
            if (s.ChampionId != null) return "Season complete";
            var next = s.Games.Where(g => !g.Played).OrderBy(g => g.Date).FirstOrDefault();
            if (next == null) return "Between stages";
            return next.Event == Madness ? $"March Madness: {RoundName(next.Round)}" : next.Stage;
        }
    }

    public static string RoundName(int round) => round switch
    {
        0 => "First Four", 1 => "Round of 64", 2 => "Round of 32", 3 => "Sweet 16", 4 => "Elite Eight", 5 => "Final Four", _ => "Championship",
    };
}

/// <summary>One box in the March Madness bracket: two teams (or "to be decided"), seeds, the score once played.</summary>
public sealed record BracketBox(int Round, int Slot, int? TeamA, int SeedA, int? TeamB, int SeedB, int? ScoreA, int? ScoreB, DateOnly? Date)
{
    public bool Played => ScoreA != null;
    public int? Winner => !Played ? null : ScoreA > ScoreB ? TeamA : TeamB;
    /// <summary>A win by a seed at least four lines worse.</summary>
    public bool Upset => Played && (Winner == TeamA ? SeedA - SeedB : SeedB - SeedA) >= 4;
}

/// <summary>One seed line of a region on Selection Sunday: the seed and its team, or both First Four teams.</summary>
public sealed record SelectionLine(int Seed, IReadOnlyList<int> Teams, bool FirstFour);

public static class MadnessBracket
{
    public static readonly string[] Regions = { "East", "West", "South", "Midwest" };

    /// <summary>Every box of rounds 1-6 (Round of 64 to the final), filled from the games so far; slots not yet reached are empty.</summary>
    public static List<BracketBox> Boxes(CollegeSeason s)
    {
        var boxes = new List<BracketBox>();
        if (!s.Selected || s.Bracket.Count < 64) return boxes;
        var games = s.Games.Where(g => g.Event == CollegeWorld.Madness).ToList();
        int Seed(int? id) => id is { } x ? s.Seeds.GetValueOrDefault(x) : 0;
        for (int round = 1; round <= 6; round++)
        {
            int n = 64 >> round;
            for (int slot = 0; slot < n; slot++)
            {
                var g = games.FirstOrDefault(x => x.Round == round && x.Slot == slot);
                if (g != null) { boxes.Add(new BracketBox(round, slot, g.HomeId, Seed(g.HomeId), g.AwayId, Seed(g.AwayId), g.HomePts, g.AwayPts, g.Date)); continue; }
                int? a = null, b = null;
                if (round == 1) { a = s.Bracket[slot * 2] is var x and > 0 ? x : null; b = s.Bracket[slot * 2 + 1] is var y and > 0 ? y : null; }
                else
                {
                    a = games.FirstOrDefault(x => x.Round == round - 1 && x.Slot == slot * 2)?.WinnerId;
                    b = games.FirstOrDefault(x => x.Round == round - 1 && x.Slot == slot * 2 + 1)?.WinnerId;
                }
                var date = s.RoundDates.TryGetValue(CollegeWorld.Madness, out var d) && round < d.Count ? d[round] : (DateOnly?)null;
                boxes.Add(new BracketBox(round, slot, a, Seed(a), b, Seed(b), null, null, date));
            }
        }
        return boxes;
    }

    /// <summary>
    /// Where the Final Four is played: a neutral site, chosen years ahead. The real hosts for 2027-2031 (Detroit, San Antonio,
    /// Indianapolis, Las Vegas, Arlington), then a rotation of past hosts. <paramref name="year"/> is the spring of the final.
    /// </summary>
    public static string FinalFourHost(int year) => year switch
    {
        2027 => "Detroit, Michigan", 2028 => "San Antonio, Texas", 2029 => "Indianapolis, Indiana", 2030 => "Las Vegas, Nevada", 2031 => "Arlington, Texas",
        _ => Hosts[((year % Hosts.Length) + Hosts.Length) % Hosts.Length],
    };

    private static readonly string[] Hosts =
        { "Indianapolis, Indiana", "Houston, Texas", "Glendale, Arizona", "Atlanta, Georgia", "New Orleans, Louisiana", "San Antonio, Texas", "Minneapolis, Minnesota", "Detroit, Michigan" };

    /// <summary>Seeds in bracket order within a region: slot i of the region's 16 holds seed <c>Order[i]</c>.</summary>
    public static readonly int[] Order = { 1, 16, 8, 9, 5, 12, 4, 13, 6, 11, 3, 14, 7, 10, 2, 15 };

    /// <summary>
    /// A region as Selection Sunday reveals it (C4 part 2): its eight first-round pairings in bracket order (1 v 16,
    /// 8 v 9 …). A line filled by a First Four game lists both teams. Empty for seasons not yet picked.
    /// </summary>
    public static List<(SelectionLine Top, SelectionLine Bottom)> RegionField(CollegeSeason s, int region)
    {
        var pairs = new List<(SelectionLine, SelectionLine)>();
        if (!s.Selected || s.Bracket.Count < 64 || region is < 0 or > 3) return pairs;
        var playIns = s.Games.Where(g => g.Event == CollegeWorld.Madness && g.Round == 0).ToList();
        SelectionLine Line(int i)
        {
            int slot = region * 16 + i;
            var ff = playIns.FirstOrDefault(g => g.Slot == slot);
            return ff != null ? new SelectionLine(Order[i], new[] { ff.HomeId, ff.AwayId }, true)
                              : new SelectionLine(Order[i], s.Bracket[slot] > 0 ? new[] { s.Bracket[slot] } : Array.Empty<int>(), false);
        }
        for (int i = 0; i < 16; i += 2) pairs.Add((Line(i), Line(i + 1)));
        return pairs;
    }

    /// <summary>Which region a box belongs to (rounds 1-4), or -1 for the Final Four and the final.</summary>
    public static int RegionOf(BracketBox b) => b.Round >= 5 ? -1 : b.Slot / (8 >> (b.Round - 1));
}
