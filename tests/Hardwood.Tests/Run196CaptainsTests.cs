using System.Text.Json;
using Hardwood.Core;
using Hardwood.Core.College;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.People;
using Hardwood.Core.Season;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>Slice 6 of the revised launch plan (run 196): captains and the senior farewell.</summary>
public class Run196CaptainsTests
{
    private readonly ITestOutputHelper _out;
    public Run196CaptainsTests(ITestOutputHelper output) => _out = output;

    private static (CollegeWorld World, GameSimulator Sim, Rng Rng) NewWorld(ulong seed)
    {
        var rng = new Rng(seed);
        var gen = new PlayerGenerator(new Rng(seed ^ 0x51), 2_000_000);
        return (CollegeWorld.Generate(gen, rng, new DateOnly(2026, 10, 1)), new GameSimulator(), rng);
    }

    [Fact]
    public void Every_program_names_two_captains_a_week_before_the_first_game_and_they_are_the_leaders()
    {
        var (c, sim, rng) = NewWorld(41);
        var day = c.CaptainsDay!.Value;
        Assert.Equal(c.FirstGameDay!.Value.AddDays(-Captaincy.LeadDays), day);
        c.PlayThrough(day.AddDays(-1), sim, rng);
        Assert.Empty(c.Captains);
        Assert.All(c.Programs, t => Assert.Empty(t.CaptainIds));

        c.PlayThrough(day, sim, rng);
        Assert.Equal(c.Programs.Count(t => t.Roster.Count >= 2), c.Captains.Count);
        foreach (var t in c.Programs.Take(40))
        {
            var rec = c.CaptainsOf(t.Id)!;
            Assert.Equal(Captaincy.Count, rec.PlayerIds.Count);
            Assert.Equal(rec.PlayerIds.ToHashSet(), t.CaptainIds);
            // Nobody left off stands higher than a captain.
            double lowest = rec.PlayerIds.Min(id => Captaincy.Standing(t.Roster.First(p => p.Id == id), c.Season.Year));
            Assert.All(t.Roster.Where(p => !rec.PlayerIds.Contains(p.Id)), p => Assert.True(Captaincy.Standing(p, c.Season.Year) <= lowest));
        }
        // Mostly upperclassmen: leadership and seniority.
        var all = c.Captains.SelectMany(r => r.PlayerIds.Select(id => c.Program(r.TeamId)!.Roster.First(p => p.Id == id))).ToList();
        double upper = all.Count(p => CollegeWorld.ClassYearOf(p, c.Season.Year)!.Year >= 3) / (double)all.Count;
        _out.WriteLine($"{c.Captains.Count} programs, {all.Count} captains, {upper:P0} juniors or seniors, leadership {all.Average(p => p.Attributes[Attr.Leadership]):0.0}");
        Assert.True(upper >= 0.6, $"upperclassmen {upper:P0}");

        // Naming happens once: a later day changes nothing, and the honours read it.
        var before = JsonSerializer.Serialize(c.Captains);
        c.PlayThrough(day.AddDays(3), sim, rng);
        Assert.Equal(before, JsonSerializer.Serialize(c.Captains));
        var first = c.Captains[0];
        Assert.Contains($"Captain {CollegeAwards.SeasonText(c.Season.Year)}, {c.Program(first.TeamId)!.FullName}", c.HonoursOf(first.PlayerIds[0]));
        var back = JsonSerializer.Deserialize<List<CaptainSeason>>(JsonSerializer.Serialize(c.Captains))!;
        Assert.Equal(before, JsonSerializer.Serialize(back));
    }

    [Fact]
    public void The_captains_lead_the_college_locker_room_and_the_pros_keep_the_old_leader_line()
    {
        var (c, _, _) = NewWorld(7);
        var t = c.Programs.First(x => x.Roster.Count >= 10);
        var caps = t.Roster.OrderBy(p => p.Id).Take(2).ToList();
        foreach (var p in t.Roster) p.Morale = 60;

        // No captains: both read the old line.
        Assert.Null(Captaincy.ChemistryLine(t));
        Assert.Equal(PeopleEngine.ChemistryFactors(t, college: false).Select(f => f.reason), PeopleEngine.ChemistryFactors(t, college: true).Select(f => f.reason));

        caps[0].Attributes[Attr.Leadership] = 15; caps[1].Attributes[Attr.Leadership] = 14;
        c.SetCaptains(t, caps);
        var good = PeopleEngine.ChemistryFactors(t, college: true);
        var line = good.Single(f => f.reason.StartsWith("Captains"));
        Assert.Equal((14.5 - Captaincy.LeadershipPar) * Captaincy.LeadershipWeight, line.effect, 6);
        Assert.DoesNotContain(good, f => f.reason == "A strong leader in the locker room");
        // The pros: unchanged.
        Assert.DoesNotContain(PeopleEngine.ChemistryFactors(t, college: false), f => f.reason.StartsWith("Captain"));

        // A poor pick costs chemistry (capped), and an unhappy captain costs more.
        caps[0].Attributes[Attr.Leadership] = 3; caps[1].Attributes[Attr.Leadership] = 4;
        caps[1].Morale = 20;
        var bad = PeopleEngine.ChemistryFactors(t, college: true);
        Assert.Equal(Captaincy.ChemistryMin, bad.Single(f => f.reason.StartsWith("Captains")).effect, 6);
        Assert.Contains(bad, f => f.reason == $"Captain {caps[1].Name} is unhappy" && f.effect == -Captaincy.UnhappyCaptainCost);

        // The captain himself is proud of it (college only).
        var today = new DateOnly(2026, 11, 20);
        var fin = new Finance(140_000_000, 1_000_000, 15, 13, 10_000_000);
        Assert.Contains(PeopleEngine.Factors(caps[0], t, 0.5, null, fin, today, college: true), f => f.reason == Captaincy.CaptainReason && f.effect == Captaincy.CaptainMorale);
        Assert.DoesNotContain(PeopleEngine.Factors(caps[0], t, 0.5, null, fin, today, college: false), f => f.reason == Captaincy.CaptainReason);
        Assert.DoesNotContain(PeopleEngine.Factors(t.Roster.First(p => !caps.Contains(p)), t, 0.5, null, fin, today, college: true), f => f.reason == Captaincy.CaptainReason);
    }

    [Fact]
    public void A_college_career_names_captains_lets_the_coach_change_them_and_says_farewell_to_the_seniors()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run196-{Guid.NewGuid():N}.hwsave");
        try
        {
            var (_, world0) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(9, 2026, GameSession.DefaultFullSim);
            int id = world0.College!.Programs.First(p => p.FullName.StartsWith("George Mason")).Id;
            var s = GameSession.NewGame(9, id, path);
            var c = s.World.College!;
            var day = c.CaptainsDay!.Value;
            Assert.Empty(s.MyCaptains);
            Assert.NotNull(s.CaptaincyBlocked(s.HumanTeam.Roster[0])); // not before the day

            int guard = 0;
            while (s.Today <= day && guard++ < 120) s.AdvanceDay();
            var caps = s.MyCaptains;
            Assert.Equal(2, caps.Count);
            var msg = s.Inbox.Single(m => m.Title.StartsWith("Captains: "));
            _out.WriteLine(msg.Title + "\n" + msg.Body);
            Assert.Contains(caps[0].Name, msg.Title);
            Assert.Contains("Squad page", msg.Body);

            // Two captains already: a third must wait. Taking one away hurts him; then another can be named.
            var other = s.HumanTeam.Roster.First(p => !caps.Contains(p));
            Assert.Contains("take the captaincy from one", s.CaptaincyBlocked(other));
            PeopleEngine.Boost(caps[1], 10, s.Today.AddDays(50), "Trusts you after a kept promise", s.Today);
            string reply = s.ToggleCaptain(caps[1]);
            Assert.Equal(10, caps[1].MoodBoost);
            _out.WriteLine(reply);
            Assert.False(s.IsCaptain(caps[1]));
            // Review: its own line, so another lift he had is kept.
            Assert.Contains(PeopleEngine.Factors(caps[1], s.HumanTeam, 0.5, null, s.League.Finance, s.Today, college: true), f => f.reason == Captaincy.LostReason && f.effect == Captaincy.LostCaptaincy);
            s.ToggleCaptain(other);
            Assert.True(s.IsCaptain(other));
            Assert.True(c.CaptainsOf(id)!.Chosen);

            // The choice survives a save and reload, flags and all.
            s.SaveNow();
            var back = GameSession.Load(path);
            Assert.Equal(JsonSerializer.Serialize(c.Captains), JsonSerializer.Serialize(back.World.College!.Captains));
            Assert.Equal(s.HumanTeam.CaptainIds.OrderBy(x => x), back.HumanTeam.CaptainIds.OrderBy(x => x));
            Assert.True(back.IsCaptain(other));
            Assert.Single(back.Inbox, m => m.Title.StartsWith("Captains: ")); // not announced twice

            // Senior Night lifts the seniors; the night of the national final, one farewell.
            var seniors = c.FinalYear(s.HumanTeam);
            guard = 0;
            while (s.Phase != SessionPhase.Offseason && s.Today < new DateOnly(2027, 4, 20) && guard++ < 400) s.AdvanceDay();
            if (seniors.Count > 0)
            {
                Assert.Contains(s.Inbox, m => m.Title.StartsWith("Senior Night tomorrow"));
                Assert.All(seniors, p => Assert.Contains(PeopleEngine.Factors(p, s.HumanTeam, 0.5, null, s.League.Finance, new DateOnly(2027, 3, 20), college: true),
                    f => f.reason == Captaincy.HonouredReason && f.effect == Captaincy.SeniorNightLift));
                Assert.NotNull(s.CaptaincyBlocked(seniors[0])); // the season is over
                var fare = s.Inbox.Single(m => m.Title.StartsWith("Farewell"));
                _out.WriteLine(fare.Title + "\n" + fare.Body);
                Assert.All(seniors, p => Assert.Contains(p.Name, fare.Body));
                Assert.Contains("finished", fare.Body);
                Assert.Equal(2026, s.HumanTeam.Money.Program.FarewellYear);
            }
            else Assert.DoesNotContain(s.Inbox, m => m.Title.StartsWith("Farewell"));
        }
        finally { try { File.Delete(path); } catch { } }
    }
}
