using Hardwood.Core;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Xunit;

namespace Hardwood.Tests;

public class StaffTests
{
    [Fact]
    public void Every_team_starts_fully_staffed_and_market_has_candidates()
    {
        var league = new WorldGenerator(new Rng(5)).Generate("L", 2026, new DateOnly(2026, 10, 1));
        Assert.All(league.Teams, t => Assert.Equal(5, t.Staff.Select(s => s.Role).Distinct().Count()));
        Assert.True(league.FreeStaff.Count >= 20);
    }

    [Fact]
    public void Better_development_staff_grows_players_faster()
    {
        var league = new WorldGenerator(new Rng(8)).Generate("L", 2026, new DateOnly(2026, 10, 1));
        var good = league.Teams[0]; var bad = league.Teams[1];
        foreach (var st in good.Staff) { st.Development = 19; st.Coaching = 19; }
        foreach (var st in bad.Staff) { st.Development = 2; st.Coaching = 2; }
        Assert.True(StaffEffects.TrainingQuality(good) > StaffEffects.TrainingQuality(bad) + 8);

        // Same synthetic player developed many times under each quality.
        var rng = new Rng(3); var gen = new PlayerGenerator(rng); var dev = new DevelopmentEngine(rng);
        double gGain = 0, bGain = 0;
        for (int i = 0; i < 300; i++)
        {
            var p1 = gen.Generate(Position.SF, 90, 20, new DateOnly(2026, 10, 1)); p1.PotentialAbility = 150;
            var p2 = gen.Generate(Position.SF, 90, 20, new DateOnly(2026, 10, 1)); p2.PotentialAbility = 150;
            gGain += dev.Develop(p1, new DateOnly(2027, 7, 1), 25, StaffEffects.TrainingQuality(good)).NewCa - 90;
            bGain += dev.Develop(p2, new DateOnly(2027, 7, 1), 25, StaffEffects.TrainingQuality(bad)).NewCa - 90;
        }
        Assert.True(gGain > bGain * 1.15, $"good {gGain / 300:F1} vs bad {bGain / 300:F1}");
    }

    [Fact]
    public void Training_focus_tilts_growth_and_anchor_holds()
    {
        var rng = new Rng(11); var gen = new PlayerGenerator(rng); var dev = new DevelopmentEngine(rng);
        double shootingGainFocused = 0, shootingGainBalanced = 0;
        for (int i = 0; i < 300; i++)
        {
            var a = gen.Generate(Position.SG, 85, 20, new DateOnly(2026, 10, 1)); a.PotentialAbility = 150;
            var b = gen.Generate(Position.SG, 85, 20, new DateOnly(2026, 10, 1)); b.PotentialAbility = 150;
            double aBefore = a.Attributes.Average(AttrGroup.Shooting), bBefore = b.Attributes.Average(AttrGroup.Shooting);
            dev.Develop(a, new DateOnly(2027, 7, 1), 25, 12, AttrGroup.Shooting);
            dev.Develop(b, new DateOnly(2027, 7, 1), 25, 12, null);
            shootingGainFocused += a.Attributes.Average(AttrGroup.Shooting) - aBefore;
            shootingGainBalanced += b.Attributes.Average(AttrGroup.Shooting) - bBefore;
        }
        Assert.True(shootingGainFocused > shootingGainBalanced * 1.2, $"{shootingGainFocused / 300:F2} vs {shootingGainBalanced / 300:F2}");
    }

    [Fact]
    public void Scouting_fog_is_exact_for_own_players_and_ranged_for_others()
    {
        var league = new WorldGenerator(new Rng(2)).Generate("L", 2026, new DateOnly(2026, 10, 1));
        var us = league.Teams[0]; var them = league.Teams[1];
        var ours = us.Roster[0]; var theirs = them.Roster[0];
        Assert.Equal(1.0, Scouting.Knowledge(us, ours));
        var k = Scouting.Knowledge(us, theirs);
        Assert.InRange(k, 0.3, 0.95);
        var r = Scouting.Attribute(theirs, Attr.ThreePoint, k);
        Assert.InRange(theirs.Attributes[Attr.ThreePoint], r.Low, r.High);
        Assert.True(Scouting.Attribute(ours, Attr.ThreePoint, 1.0).Exact);
        // Stable: same range every call.
        Assert.Equal(r, Scouting.Attribute(theirs, Attr.ThreePoint, k));
        // A better scout narrows it.
        var wide = Scouting.Attribute(theirs, Attr.ThreePoint, 0.3); var narrow = Scouting.Attribute(theirs, Attr.ThreePoint, 0.9);
        Assert.True(wide.High - wide.Low > narrow.High - narrow.Low);
        // CA and PA are shown exactly for everyone (Nov 10).
        Assert.True(Scouting.Ability(theirs, 0.3).Exact && Scouting.Potential(theirs, 0.3).Exact);
    }

    [Fact]
    public void Staff_hire_fire_and_persistence()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-staff-{Guid.NewGuid():N}.save");
        try
        {
            var s = GameSession.NewGame(44, humanTeamId: 2, path);
            var scout = s.League.FreeStaff.Where(x => x.Role == StaffRole.Scout).OrderByDescending(x => x.Scouting).First();
            Assert.True(s.HireStaff(scout));
            Assert.Contains(scout, s.HumanTeam.Staff);
            Assert.Equal(5, s.HumanTeam.Staff.Count);
            s.HumanTeam.Training.Intensity = 80;
            s.HumanTeam.Training.Focus[s.HumanTeam.Roster[0].Id] = AttrGroup.Defense;
            s.SaveNow();

            var loaded = GameSession.Load(path);
            Assert.Contains(loaded.HumanTeam.Staff, x => x.Id == scout.Id);
            Assert.Equal(80, loaded.HumanTeam.Training.Intensity);
            Assert.Equal(AttrGroup.Defense, loaded.HumanTeam.Training.Focus[s.HumanTeam.Roster[0].Id]);
            Assert.Equal(s.League.FreeStaff.Count, loaded.League.FreeStaff.Count);

            // A season later every AI team is still fully staffed.
            int guard = 0;
            while (loaded.Phase != SessionPhase.Offseason && guard++ < 400) loaded.AdvanceDay();
            foreach (var p in loaded.Offseason!.Expiring[2].ToList()) loaded.Offseason.Release(loaded.HumanTeam, p);
            loaded.AdvanceOffseason();
            while (loaded.Offseason.Phase == OffseasonPhase.Draft && !loaded.Offseason.DraftComplete) loaded.MakePick(loaded.Offseason.Prospects.First());
            loaded.AdvanceOffseason();
            Assert.All(loaded.League.Teams.Where(t => t != loaded.HumanTeam), t => Assert.Equal(5, t.Staff.Count));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
