using Hardwood.Core;
using Hardwood.Core.Engine;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Desktop.Controls;
using Hardwood.Desktop.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 112, a polish/backlog run (bug sweep, Danny #883). H87: one name style per bracket.
/// H101: Home's scouting line isn't cut at "St.". H102: a facility says what it does for your club. H103: the empty
/// free-agent pool says when players arrive. H106: the top bar's record counts the game on the FINAL screen.
/// H104, H107, H108 are wording and list fixes checked by the hunter's screenshots.
/// </summary>
public class Run112PolishTests
{
    private readonly ITestOutputHelper _out;
    public Run112PolishTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void A_bracket_uses_one_name_style()
    {
        var hou = new Team { Id = 1, City = "Houston", Name = "Mustangs", Abbreviation = "HOU", Division = "" };
        var ind = new Team { Id = 2, City = "Indianapolis", Name = "Mustangs", Abbreviation = "IND", Division = "" };
        var lal = new Team { Id = 3, City = "Los Angeles", Name = "Lakers", Abbreviation = "LAL", Division = "" };
        var bal = new Team { Id = 4, City = "Baltimore", Name = "Basketball Club", Abbreviation = "BAL", Division = "" };
        // A shared nickname and a generic one: cities for everyone (was "Houston Mustangs", "Indianapolis", "Lakers", "Baltimore").
        var mixed = BracketView.BracketLabels(new[] { hou, ind, lal, bal });
        Assert.Equal(new[] { "Houston", "Indianapolis", "Los Angeles", "Baltimore" }, new[] { mixed[hou], mixed[ind], mixed[lal], mixed[bal] });
        // Distinct, proper nicknames: nicknames for everyone.
        var sas = new Team { Id = 5, City = "San Antonio", Name = "Spurs", Abbreviation = "SAS", Division = "" };
        var gsw = new Team { Id = 6, City = "Golden State", Name = "Warriors", Abbreviation = "GSW", Division = "" };
        var por = new Team { Id = 7, City = "Portland", Name = "Trail Blazers", Abbreviation = "POR", Division = "" };
        var nick = BracketView.BracketLabels(new[] { sas, gsw, por });
        Assert.Equal(new[] { "Spurs", "Warriors", "Trail Blazers" }, new[] { nick[sas], nick[gsw], nick[por] });
        // All short: full names.
        var full = BracketView.BracketLabels(new[] { sas, lal });
        Assert.Equal("San Antonio Spurs", full[sas]);
    }

    [Fact]
    public void The_scouting_headline_keeps_St_Louis_whole()
    {
        var (league, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026, 2026);
        var t = league.Teams.OrderBy(x => x.Id).ToList();
        t[1].City = "St. Louis"; t[1].Name = "FC";
        var head = CoachAi.ScoutingHeadline(t[0], t[1], new DateOnly(2026, 11, 3));
        _out.WriteLine(head);
        Assert.StartsWith("St. Louis FC will likely run", head);
        Assert.EndsWith("against us.", head);
        Assert.StartsWith(head, CoachAi.ScoutingReport(t[0], t[1], new DateOnly(2026, 11, 3)));
    }

    [Fact]
    public void A_facility_below_the_league_says_so_for_your_club()
    {
        Assert.Equal("For you: training quality -0.5", Franchise.ClubEffect(FacilityKind.Practice, -1.3));
        Assert.Equal("For you: training quality +0.4", Franchise.ClubEffect(FacilityKind.Practice, 1.0));
        Assert.Contains("more injuries", Franchise.ClubEffect(FacilityKind.Medical, -1));
        Assert.StartsWith("Level with the league", Franchise.ClubEffect(FacilityKind.Analytics, 0.02));
    }

    [Fact]
    public void The_record_counts_the_game_on_the_final_screen_and_the_empty_pool_says_when()
    {
        Assert.Equal("1-1", LiveGameViewModel.RecordAfter(1, 0, won: false));
        Assert.Equal("2-0", LiveGameViewModel.RecordAfter(1, 0, won: true));
        Assert.Contains("contracts end in the summer", TransactionsViewModel.FreeAgentsEmptyNote);
    }
}
