using System.Text.Json.Serialization;
using Hardwood.Core.Model;

namespace Hardwood.Core.College;

/// <summary>A computer head coach's style (slice 9, run 200): who he is on the sideline and on the recruiting trail. It colours
/// what he says in the news and how the game describes him; it doesn't change a result.</summary>
public enum CoachStyle { Recruiter, Tactician, DefensiveMind, Developer, Motivator, UpTempo }

/// <summary>
/// Slice 9 of the revised launch plan (#1164, run 200): one computer head coach at one college program, with his record there
/// and in his career. Programs' coaches have a history from the first day (seeded from the program, with no random draw), and
/// each spring adds the season. The carousel (H202) reads <see cref="Seasons"/> and <see cref="PoorRun"/>: an athletic director
/// gives a new coach two seasons and lets a coach go after a run of poor ones, not after one 21-win season.
/// </summary>
public sealed class BenchCoach
{
    public int ProgramId { get; init; }
    public string Name { get; set; } = "";
    /// <summary>Completed seasons at this program.</summary>
    public int Seasons { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    /// <summary>His whole head-coaching career (this program and earlier ones).</summary>
    public int CareerWins { get; set; }
    public int CareerLosses { get; set; }
    /// <summary>NCAA tournament bids and national titles in his career.</summary>
    public int Bids { get; set; }
    public int Titles { get; set; }
    /// <summary>Seasons in a row below his athletic director's target (the carousel's patience).</summary>
    public int PoorRun { get; set; }

    [JsonIgnore] public CoachStyle Style => RivalCoaches.StyleOf(Name);
    [JsonIgnore] public string Record => $"{Wins}-{Losses}";
    [JsonIgnore] public string CareerRecord => $"{CareerWins}-{CareerLosses}";
    /// <summary>"in his 9th season", "in his first season".</summary>
    [JsonIgnore] public string SeasonText => Seasons == 0 ? "in his first season" : $"in his {RivalCoaches.Ordinal(Seasons + 1)} season";
}

/// <summary>Slice 9 (run 200): the college world's computer head coaches as characters: a book of who coaches where, with a
/// style, a reputation and a record, plus the lines they say in the news. Nothing here draws a random number.</summary>
public static class RivalCoaches
{
    /// <summary>A stable hash (FNV-1a), so a coach's style and seeded past never depend on the order things were made in.</summary>
    public static ulong Hash(string s)
    {
        ulong h = 14695981039346656037UL;
        foreach (char c in s) { h ^= c; h *= 1099511628211UL; }
        return h;
    }

    public static CoachStyle StyleOf(string name) => (CoachStyle)(Hash(name) % 6);

    /// <summary>"the recruiter", "a defensive mind" as a noun phrase ("Phil Hollis, a defensive mind").</summary>
    public static string StyleText(CoachStyle s) => s switch
    {
        CoachStyle.Recruiter => "a recruiter",
        CoachStyle.Tactician => "a tactician",
        CoachStyle.DefensiveMind => "a defensive mind",
        CoachStyle.Developer => "a player developer",
        CoachStyle.Motivator => "a motivator",
        _ => "an up-tempo coach",
    };

    /// <summary>A short label for a chip: "Recruiter", "Defensive mind".</summary>
    public static string StyleLabel(CoachStyle s) => s switch
    {
        CoachStyle.DefensiveMind => "Defensive mind",
        CoachStyle.Developer => "Player developer",
        CoachStyle.UpTempo => "Up-tempo",
        _ => s.ToString(),
    };

    public static string Ordinal(int n) => n switch
    {
        1 => "first", 2 => "second", 3 => "third", 4 => "fourth", 5 => "fifth", 6 => "sixth", 7 => "seventh", 8 => "eighth", 9 => "ninth", 10 => "10th",
        _ => n % 100 is 11 or 12 or 13 ? $"{n}th" : (n % 10) switch { 1 => $"{n}st", 2 => $"{n}nd", 3 => $"{n}rd", _ => $"{n}th" },
    };

    /// <summary>A coach's standing in the game (1-99): his winning, his bids and titles, and the size of his job.</summary>
    public static int Reputation(BenchCoach c, double prestige)
    {
        int g = c.CareerWins + c.CareerLosses;
        double pct = g == 0 ? 0.5 : c.CareerWins / (double)g;
        double r = 22 + (pct - 0.5) * 90 * Math.Min(1, g / 60.0) + Math.Min(18, c.Bids * 1.5) + Math.Min(24, c.Titles * 8) + prestige * 0.25;
        return (int)Math.Clamp(Math.Round(r), 1, 99);
    }

    /// <summary>A word for the reputation, as the Board page reads it.</summary>
    public static string ReputationWord(int rep) => rep switch
    {
        >= 80 => "a legend",
        >= 65 => "one of the best",
        >= 50 => "well regarded",
        >= 35 => "solid",
        _ => "unproven",
    };

    /// <summary>
    /// The history a program's first coach brings to a new world: up to 13 seasons there, a winning share that follows the
    /// program's size, bids and (at the biggest programs) a title. Seeded from the program and his name, with no random draw.
    /// </summary>
    public static BenchCoach Seeded(Team program, double prestige)
    {
        ulong h = Hash(program.CoachName + "|" + program.Id);
        int seasons = (int)(h % 14);
        double pct = Math.Clamp(0.36 + prestige / 100 * 0.36 + ((h >> 8) % 11 - 5) / 100.0, 0.25, 0.8);
        int earlier = (int)((h >> 16) % 3 == 0 ? (h >> 20) % 8 : 0); // some came from another head-coaching job
        int games = (seasons + earlier) * 31;
        int wins = (int)Math.Round(seasons * 31 * pct);
        int careerWins = wins + (int)Math.Round(earlier * 31 * Math.Max(0.45, pct - 0.05));
        double bidShare = Math.Clamp((prestige - 45) / 50, 0.02, 0.9);
        int bids = (int)Math.Round((seasons + earlier) * bidShare);
        int titles = prestige >= 85 && seasons >= 6 && (h >> 28) % 4 == 0 ? 1 : 0;
        return new BenchCoach
        {
            ProgramId = program.Id, Name = program.CoachName, Seasons = seasons, Wins = wins, Losses = seasons * 31 - wins,
            CareerWins = careerWins, CareerLosses = games - careerWins, Bids = bids, Titles = titles,
            // Some arrive on the hot seat: up to two poor seasons already behind them (not in their first two seasons).
            PoorRun = seasons >= 2 ? (int)((h >> 40) % 3) : 0,
        };
    }

    /// <summary>What a rival coach says after beating you, or losing to you, in a rivalry game (in his style's voice).</summary>
    public static string RivalryQuote(BenchCoach c, bool heWon, string usCity, string rivalryLabel)
    {
        int pick = (int)(Hash(c.Name + usCity + c.Wins) % 2);
        string[] won = c.Style switch
        {
            CoachStyle.Recruiter => new[] { $"\"Kids watch these games. They saw who owns this rivalry tonight.\"", $"\"Every recruit in the state was watching. Good night for us.\"" },
            CoachStyle.Tactician => new[] { $"\"We had a plan for their sets and the guys ran it for forty minutes.\"", $"\"It came down to a couple of adjustments at the half. We made ours.\"" },
            CoachStyle.DefensiveMind => new[] { $"\"We guarded. That's what wins {rivalryLabel}, and it always will.\"", $"\"Make them take tough shots, and keep them off the glass. We did both.\"" },
            CoachStyle.Developer => new[] { $"\"Our young guys grew up tonight. That's the whole job.\"", $"\"You saw what three years in this program does for a player.\"" },
            CoachStyle.Motivator => new[] { $"\"Our guys wanted it more. You could feel it from the tip.\"", $"\"I told them this one's for the fans. They played like it.\"" },
            _ => new[] { $"\"We ran them out of the gym. That's how we play.\"", $"\"Push, push, push. They couldn't keep up in the second half.\"" },
        };
        string[] lost = c.Style switch
        {
            CoachStyle.Recruiter => new[] { $"\"They've got good players. So do we, and we'll have more of them next year.\"", $"\"Credit to {usCity}. We'll see them on the trail this summer.\"" },
            CoachStyle.Tactician => new[] { $"\"They beat our coverage a few times too many. We'll look at the film.\"", $"\"We lost the chess match tonight. It won't happen twice.\"" },
            CoachStyle.DefensiveMind => new[] { $"\"We didn't guard. Simple as that.\"", $"\"You give up that many points in {rivalryLabel}, you lose.\"" },
            CoachStyle.Developer => new[] { $"\"We're young. Nights like this are how you learn.\"", $"\"I'll take our group in two years. Tonight was theirs.\"" },
            CoachStyle.Motivator => new[] { $"\"That one stings, and it should. We'll remember it.\"", $"\"Our locker room is hurting. Good. Use it.\"" },
            _ => new[] { $"\"They slowed it down and we let them. That's on me.\"", $"\"We never found our pace. Credit to {usCity}.\"" },
        };
        return (heWon ? won : lost)[pick];
    }

    /// <summary>What a rival coach's staff tells a recruit you lost to him (the "picks" message).</summary>
    public static string RecruitLine(BenchCoach c) => c.Style switch
    {
        CoachStyle.Recruiter => "He's the best closer on the trail, and he was in the living room twice.",
        CoachStyle.Developer => "His pitch was the players he has sent to the pros.",
        CoachStyle.UpTempo => "He sold him on the pace: shots, minutes and highlights.",
        CoachStyle.Motivator => "The family loved him.",
        CoachStyle.Tactician => "He showed him exactly where he'd fit in the offence.",
        _ => "He promised him a role from day one.",
    };
}

public sealed partial class CollegeWorld
{
    /// <summary>Slice 9 (run 200): the computer head coaches' book (saved; missing before v0.188.0, so an older save seeds it
    /// on load).</summary>
    public List<BenchCoach> Benches { get; } = new();

    /// <summary>The bench entry for a program's computer coach (none for yours or an interim). Read-only: a program with no
    /// entry, or one whose coach has changed since, reads a temporary entry that isn't kept (review of PR #269: the pages call
    /// this while the day runs, so only <see cref="EnsureBenches"/> writes the book, on the sim's side).</summary>
    public BenchCoach? BenchOf(int programId)
    {
        if (Program(programId) is not { } t || t.Coach != null || t.CoachName is "" or "Interim coach") return null;
        var b = Benches.FirstOrDefault(x => x.ProgramId == programId);
        if (b != null && b.Name == t.CoachName) return b;
        return b == null ? RivalCoaches.Seeded(t, Prestige.GetValueOrDefault(t.Id, 50)) : new BenchCoach { ProgramId = programId, Name = t.CoachName };
    }

    /// <summary>Every program's computer coach gets a bench entry. <paramref name="seedPast"/> (a new world, or an older save's
    /// first load) gives a coach with no entry his past; otherwise (the carousel) a coach who came in some other way since the
    /// last spring, such as your successor when you left, starts at 0-0.</summary>
    public void EnsureBenches(bool seedPast)
    {
        foreach (var t in Programs)
        {
            if (t.Coach != null || t.CoachName is "" or "Interim coach") continue;
            var b = Benches.FirstOrDefault(x => x.ProgramId == t.Id);
            if (b != null && b.Name == t.CoachName) continue;
            if (b == null && seedPast) Benches.Add(RivalCoaches.Seeded(t, Prestige.GetValueOrDefault(t.Id, 50)));
            else NewBench(t.Id, t.CoachName, null);
        }
    }

    /// <summary>A coach's reputation at his program (1-99).</summary>
    public int ReputationOf(BenchCoach c) => RivalCoaches.Reputation(c, Prestige.GetValueOrDefault(c.ProgramId, 50));

    /// <summary>"Phil Hollis, a defensive mind (ninth season, 187-102)".</summary>
    public string CoachLine(int programId) => BenchOf(programId) is { } c
        ? $"{c.Name}, {RivalCoaches.StyleText(c.Style)} ({(c.Seasons == 0 ? "first season" : RivalCoaches.Ordinal(c.Seasons + 1) + " season")}, {c.Record})"
        : "";

    /// <summary>A new coach takes over a program: his career comes with him if he was hired from another bench.</summary>
    public void NewBench(int programId, string name, BenchCoach? from)
    {
        Benches.RemoveAll(x => x.ProgramId == programId);
        Benches.Add(new BenchCoach
        {
            ProgramId = programId, Name = name,
            CareerWins = from?.CareerWins ?? 0, CareerLosses = from?.CareerLosses ?? 0, Bids = from?.Bids ?? 0, Titles = from?.Titles ?? 0,
        });
    }
}
