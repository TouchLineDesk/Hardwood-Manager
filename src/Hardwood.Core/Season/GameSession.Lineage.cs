using Hardwood.Core.Model;
using Hardwood.Core.People;

namespace Hardwood.Core.Season;

/// <summary>W3, second generation (run 231): sons of retired players in the new classes, and the news when they sign.</summary>
public sealed partial class GameSession
{
    /// <summary>Every son found so far.</summary>
    public LineageBook Lineage => League.Lineage;

    /// <summary>A player anywhere in the world (rosters, free agents, retired, abroad, college and high school).</summary>
    public Player? AnyPlayer(int id) =>
        League.Teams.SelectMany(t => t.Roster).Concat(League.FreeAgents).Concat(League.Retired).Concat(World.Players)
            .Concat(World.College?.Players ?? Enumerable.Empty<Player>()).FirstOrDefault(p => p.Id == id);

    /// <summary>The son line for a player's page, or null.</summary>
    public string? LineageLine(Player p)
    {
        if (Lineage.SonOf(p.Id) is { } son) return Hardwood.Core.People.Lineage.SonLine(son);
        var kids = Lineage.ChildrenOf(p.Id).Select(s => (s, AnyPlayer(s.SonId)?.Name)).Where(x => x.Name != null).Select(x => (x.s, x.Name!)).ToList();
        return kids.Count > 0 ? Hardwood.Core.People.Lineage.FatherLine(kids) : null;
    }

    /// <summary>
    /// The summer's search (once a summer, at the start of the offseason, after the new classes are made): the newest high-school
    /// class in a college world, and the summer's graduates or draft prospects in a world without one. One inbox message names
    /// what was found and any son who joined a team since the last message.
    /// </summary>
    private void SecondGenerationSummer(int summerYear)
    {
        var found = new List<Son>();
        if (!Lineage.Summers.Contains(summerYear))
        {
            Lineage.Summers.Add(summerYear);
            var summer = new DateOnly(summerYear, 7, 1);
            if (World.College is { Recruiting: true } c)
            {
                var newest = c.Prospects.Where(p => p.Age(summer) <= Hardwood.Core.College.CollegeWorld.YoungestAge).ToList();
                found.AddRange(Hardwood.Core.People.Lineage.Link(League, Lineage, newest, summerYear, "high school", Seed, TeamNameOf));
            }
            if (Offseason?.Prospects is { Count: > 0 } pros && !League.CollegeActive)
                found.AddRange(Hardwood.Core.People.Lineage.Link(League, Lineage, pros, summerYear, "draft", Seed, TeamNameOf));
        }
        PostSecondGeneration(found);
    }

    /// <summary>At the start of the season: sons who joined a team over the summer.</summary>
    private void SecondGenerationSignings() => PostSecondGeneration(new List<Son>());

    private string? TeamNameOf(int id) => FindTeam(id)?.FullName;

    private void PostSecondGeneration(List<Son> found)
    {
        var lines = new List<string>();
        foreach (var s in found)
            if (AnyPlayer(s.SonId) is { } p) lines.Add(FoundLine(s, p));
        foreach (var s in Lineage.Sons.Where(s => s.AnnouncedTeamId == null))
        {
            if (AnyPlayer(s.SonId) is not { TeamId: { } tid } p || FindTeam(tid) is not { } team) continue;
            s.AnnouncedTeamId = tid;
            bool college = World.College?.Programs.Any(x => x.Id == tid) == true;
            bool ours = Employed && tid == HumanTeam.Id;
            lines.Add($"{p.Name}, son of {s.FatherName}, has {(college ? "enrolled at" : "signed with")} {(ours ? "your " + (college ? "program" : "club") : team.FullName)}"
                + (p.Draft is { } d && d.Year == League.Season + (Phase == SessionPhase.Offseason ? 1 : 0) ? $" (pick {d.Pick} in the draft)" : "") + ".");
        }
        if (lines.Count == 0) return;
        Post("Second generation", string.Join("\n\n", lines), "News");
    }

    private string FoundLine(Son s, Player p)
    {
        string where = s.Pool == "high school" && World.College?.Recruits.TryGetValue(p.Id, out var r) == true
            ? $"in the high-school class of {r!.ClassOf}" : "in this summer's " + (League.Finance.OpenMarket ? "graduating class" : "draft class");
        string recruit = InCollege && Employed && s.Pool == "high school" && World.College?.Recruits.TryGetValue(p.Id, out var rc) == true
            ? $" He's yours to recruit in his senior year, {rc!.ClassOf - 1}-{rc.ClassOf % 100:00}." : "";
        return $"{p.Name} ({Hardwood.Core.People.Lineage.PositionName(p.PrimaryPosition)}, {p.Age(new DateOnly(s.Year, 7, 1))}) is {where}. "
             + $"He's the son of {s.FatherName}: {s.FatherCareer}. {char.ToUpper(s.Likeness[0])}{s.Likeness[1..]}.{recruit}";
    }
}
