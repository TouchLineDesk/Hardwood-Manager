using Hardwood.Core.College;
using Hardwood.Core.Season;
using Hardwood.Desktop.ViewModels;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Run 82. Bug hunt H49: from signing day until the class enrolls, the class just signed wasn't counted against
/// the next class's scholarships, so the offer limit was too high. H33: a relegation decided by a tiebreak the game never
/// showed (and in the NABL the season review, the summer and Standings broke ties differently). H50: the draft board's age.</summary>
public class Run82TiebreakTests
{
    [Fact]
    public void A_signed_class_not_yet_enrolled_holds_its_scholarships()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run82a-{Guid.NewGuid():N}.hwsave");
        try
        {
            var (probe, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026);
            var s = GameSession.NewGame(2026, probe.Teams.First(t => t.Abbreviation == "BOS").Id, path);
            var c = s.World.College!;
            Assert.True(c.Recruiting);
            int senior = c.SeniorClass;
            var program = c.Programs[0];
            var free = c.ClassOf(senior).Where(p => c.Recruits[p.Id].CommittedTo == null).Take(2).ToList();
            Assert.Equal(2, free.Count);
            int before = c.BoardFor(senior + 1).Open[program.Id];
            foreach (var p in free) c.Recruits[p.Id].CommittedTo = program.Id;   // signed, still in high school
            int after = c.BoardFor(senior + 1).Open[program.Id];
            Assert.Equal(before - 2, after);
            Assert.Equal(Math.Max(0, after), c.OpenScholarships(program, senior + 1));
            // A later class only: the signed class itself counts them once, as commitments (not twice).
            var own = c.BoardFor(senior);
            int returning = program.Roster.Count(x => x.Age(new DateOnly(senior, 7, 1)) < CollegeWorld.GraduateAge && !c.WalkOnIds.Contains(x.Id));
            int committed = c.Commits(program, senior);
            Assert.Equal(CollegeWorld.Scholarships - returning - committed, own.Open[program.Id]);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void The_draft_board_gives_the_July_1_age()
    {
        Assert.Equal(new DateOnly(2027, 7, 1), OffseasonViewModel.DraftAgeDay(new DateOnly(2027, 5, 13)));
        Assert.Equal(new DateOnly(2027, 8, 2), OffseasonViewModel.DraftAgeDay(new DateOnly(2027, 8, 2)));
        var p = new Hardwood.Core.Model.Player { FirstName = "Kobe", LastName = "Test", BirthDate = new DateOnly(2004, 6, 20) };
        Assert.Equal(22, p.Age(new DateOnly(2027, 5, 13)));
        Assert.Equal(23, p.Age(OffseasonViewModel.DraftAgeDay(new DateOnly(2027, 5, 13))));
    }

    [Fact]
    public void A_relegation_on_a_tiebreak_says_so_and_follows_the_table()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run82b-{Guid.NewGuid():N}.hwsave");
        try
        {
            var (probe, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026);
            var s = GameSession.NewGame(2026, probe.Teams.First(t => t.Abbreviation == "BOS").Id, path);
            var me = s.HumanTeam;
            var div = s.League.Teams.Where(t => t.Conference == me.Conference && t.Division == me.Division).ToList();
            Assert.True(div.Count >= 2);
            var rival = div.First(t => t != me);
            foreach (var t in s.League.Teams)
            {
                var r = s.Standings[t];
                r.Wins = 45; r.Losses = 37; r.PointsFor = 9000; r.PointsAgainst = 8900;
            }
            void Set(Hardwood.Core.Model.Team t, int pf, int pa) { var r = s.Standings[t]; r.Wins = 30; r.Losses = 52; r.PointsFor = pf; r.PointsAgainst = pa; }
            Set(me, 8600, 8640);     // −40
            Set(rival, 8600, 8588);  // +12
            Assert.True(s.GoingDown());
            var note = s.RelegationTiebreak();
            Assert.NotNull(note);
            Assert.StartsWith($"Level at 30-52 with {rival.FullName}: point difference decided it", note);
            Assert.Contains($"{me.FullName} −40", note);
            Assert.Contains($"{rival.FullName} +12", note);
            // The other way round: the rival goes down, and before v0.97.2 the review's order (by club id) could disagree.
            Set(me, 8600, 8588); Set(rival, 8600, 8640);
            Assert.False(s.GoingDown());
            Assert.Null(s.RelegationTiebreak());
            // Not level: no tiebreak line.
            Set(me, 8600, 8640); var rr = s.Standings[rival]; rr.Wins = 31; rr.Losses = 51;
            Assert.True(s.GoingDown());
            Assert.Null(s.RelegationTiebreak());
        }
        finally { try { File.Delete(path); } catch { } }
    }
}
