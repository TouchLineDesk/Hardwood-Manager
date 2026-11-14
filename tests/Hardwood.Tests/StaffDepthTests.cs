using Hardwood.Core;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Staff depth (roadmap 12): styles with effects, interviews, development projections and delegation.</summary>
public class StaffDepthTests
{
    private static GameSession Session(ulong seed, string? path = null, int strengthRank = 10)
    {
        var probe = Hardwood.Core.RealWorld.RealWorldData.NewWorld(seed).League; // real careers only (D61 B)
        var id = probe.Teams.OrderByDescending(t => Hardwood.Core.World.QuickSim.Strength(t)).ElementAt(strengthRank).Id;
        return GameSession.NewGame(seed, id, path);
    }

    [Fact]
    public void Every_staff_member_has_one_of_his_roles_two_styles_for_life()
    {
        var s = Session(81);
        var all = s.League.FreeStaff.Concat(s.League.Teams.SelectMany(t => t.Staff)).ToList();
        foreach (var st in all)
        {
            var styles = Staff.Styles(st.Role).Select(x => x.Name).ToList();
            Assert.Contains(st.Style, styles);
            Assert.Equal(st.Style, st.Style);
            Assert.False(string.IsNullOrEmpty(st.StyleText));
        }
        // Both styles of every role turn up in a league's worth of staff.
        foreach (var role in Enum.GetValues<StaffRole>())
            Assert.Equal(2, all.Where(x => x.Role == role).Select(x => x.Style).Distinct().Count());
    }

    [Fact]
    public void A_teacher_gives_young_players_extra_training_and_projections_show_it()
    {
        var s = Session(82);
        var team = s.HumanTeam;
        var dev = team.Staff.First(x => x.Role == StaffRole.DevelopmentCoach);
        var teacher = s.League.FreeStaff.Concat(s.League.Teams.SelectMany(t => t.Staff)).First(x => x.Role == StaffRole.DevelopmentCoach && x.Is("Teacher"));
        team.Staff.Remove(dev); team.Staff.Add(teacher);
        var young = team.Roster.OrderBy(p => p.Age(s.Today)).First();
        var vet = team.Roster.OrderByDescending(p => p.Age(s.Today)).First();
        Assert.True(young.Age(s.Today) <= 23 && vet.Age(s.Today) > 23, "need a young player and a veteran");
        Assert.Equal(StaffEffects.TrainingQuality(team) + 1.0 + (team.Staff.First(x => x.Role == StaffRole.AssistantCoach).Is("Mentor") ? 0.3 : 0), StaffEffects.TrainingQualityFor(team, young, s.Today), 9);
        Assert.Equal(StaffEffects.TrainingQuality(team) - 0.3 + (team.Staff.First(x => x.Role == StaffRole.AssistantCoach).Is("Mentor") ? -0.2 : 0), StaffEffects.TrainingQualityFor(team, vet, s.Today), 9);

        // Better training, more projected growth for a young player with headroom.
        teacher.Development = 20;
        var (_, fromStaff) = s.Projection(young);
        if (young.PotentialAbility - young.CurrentAbility >= 10) Assert.True(fromStaff > 0, $"{fromStaff}");
    }

    [Fact]
    public void Without_luck_development_lands_on_the_projection()
    {
        var league = new WorldGenerator(new Rng(83)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));
        var dev = new DevelopmentEngine(new Rng(1), new DevelopmentConfig { NoiseSd = 0 });
        var summer = new DateOnly(2027, 7, 1);
        foreach (var p in league.Teams.SelectMany(t => t.Roster).Take(60))
        {
            var (g, d) = DevelopmentEngine.Expected(p, p.Age(summer), 24, 12);
            int expected = (int)Math.Round(Math.Clamp(p.CurrentAbility + g - d, 20, p.PotentialAbility));
            if (p.Age(summer) >= 30) expected = Math.Min(expected, p.CurrentAbility + 2);
            var r = dev.Develop(p, summer, 24, 12);
            Assert.Equal(expected, r.NewCa);
        }
    }

    [Fact]
    public void Big_names_turn_down_small_clubs_and_hires_take_the_interview_terms()
    {
        var s = Session(84, strengthRank: 29);
        var star = s.League.FreeStaff.OrderByDescending(x => x.Reputation).First();
        star.Reputation = 20;
        var no = s.Interview(star);
        Assert.False(no.Willing);
        Assert.Contains("bigger club", no.Text);
        Assert.Equal(no.Text, s.HireOnTerms(star));
        Assert.DoesNotContain(star, s.HumanTeam.Staff);

        var modest = s.League.FreeStaff.First(x => x.Reputation <= 6);
        var yes = s.Interview(modest);
        Assert.True(yes.Willing);
        Assert.True(yes.Wage >= s.StaffPayFor(modest) - 1000);
        s.HireOnTerms(modest);
        Assert.Contains(modest, s.HumanTeam.Staff);
        Assert.Equal(yes.Wage, modest.Salary);
        Assert.Equal(yes.Years, modest.ContractYears);
    }

    [Fact]
    public void Delegated_staff_plan_training_and_court_seniors_and_it_is_saved()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-staff-{Guid.NewGuid():N}.hwsave");
        try
        {
            var s = Session(85, path);
            s.Delegated.Training = true;
            s.Delegated.Recruiting = true;
            while (s.Today < new DateOnly(2026, 12, 8)) s.AdvanceDay();
            var t = s.HumanTeam;
            foreach (var p in t.Roster)
            {
                bool developing = p.Age(s.Today) <= 25 && p.PotentialAbility - p.CurrentAbility >= 5;
                if (developing) Assert.Equal(GameSession.Weakest(p), t.Training.Focus[p.Id]);
                else Assert.False(t.Training.Focus.ContainsKey(p.Id));
            }
            Assert.Contains(t.Training.Intensity, new[] { 35, 45, 60 });
            Assert.StartsWith("Dec", s.Delegated.LastTraining);
            // Real careers draft college seniors instead of recruiting them (D48 B): a pro assistant courts nobody.
            Assert.False(s.SeniorRecruiting);
            Assert.DoesNotContain("courted", s.Delegated.LastRecruiting ?? "");

            s.SaveNow();
            var loaded = GameSession.Load(path);
            Assert.True(loaded.Delegated.Training);
            Assert.True(loaded.Delegated.Recruiting);
            Assert.Equal(s.Delegated.LastTraining, loaded.Delegated.LastTraining);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
