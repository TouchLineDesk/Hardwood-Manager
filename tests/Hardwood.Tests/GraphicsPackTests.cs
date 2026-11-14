using Hardwood.Core;
using Hardwood.Core.Generation;
using Hardwood.Core.Graphics;
using Hardwood.Core.Model;
using Hardwood.Core.Portraits;
using Hardwood.Desktop.Portraits;
using SkiaSharp;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// Danny's graphics packs (#350): prographics / collegegraphics folders read from his PC (nothing shipped), and
/// players in their club's jersey on their cards (#351).
/// </summary>
public class GraphicsPackTests
{
    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hw-gfx-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Png(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var bmp = new SKBitmap(8, 8);
        bmp.Erase(SKColors.Red);
        using var data = bmp.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }

    [Fact]
    public void Real_teams_programs_and_players_match_the_pack_files()
    {
        var root = TempDir();
        try
        {
            Png(Path.Combine(root, "prographics", "logos", "NBA_Boston.png"));
            Png(Path.Combine(root, "prographics", "logos", "NBA_LosAngelesC.png"));
            Png(Path.Combine(root, "prographics", "logos", "NBA_LosAngelesL.png"));
            Png(Path.Combine(root, "prographics", "logos", "banners", "NBA_Boston.png"));
            Png(Path.Combine(root, "prographics", "tokens", "NBA_boston_road.png"));
            Png(Path.Combine(root, "prographics", "tokens", "NBA_boston_home.png"));
            Png(Path.Combine(root, "prographics", "players", "stephen_curry.png"));
            Png(Path.Combine(root, "collegegraphics", "logos", "1CM_duke.png"));
            Png(Path.Combine(root, "collegegraphics", "logos", "1CM_connecticut.png"));
            var g = GraphicsPack.Find(Path.Combine(root, "missing"), root);
            Assert.False(g.IsEmpty);

            var bos = new Team { Id = 1, City = "Boston", Name = "Celtics", Abbreviation = "BOS", Division = "A" };
            var lac = new Team { Id = 2, City = "Los Angeles", Name = "Clippers", Abbreviation = "LAC", Division = "P" };
            var lal = new Team { Id = 3, City = "Los Angeles", Name = "Lakers", Abbreviation = "LAL", Division = "P" };
            var made = new Team { Id = 4, City = "Boston", Name = "Harbor Hawks", Abbreviation = "BHH", Division = "A" };
            Assert.EndsWith("NBA_Boston.png", g.LogoFor(bos));
            Assert.EndsWith("NBA_LosAngelesC.png", g.LogoFor(lac));
            Assert.EndsWith("NBA_LosAngelesL.png", g.LogoFor(lal));
            Assert.Null(g.LogoFor(made));                       // a made-up club in a real city keeps its drawn crest
            Assert.NotNull(g.BannerFor(bos));
            var kits = g.JerseysFor(bos);
            Assert.Equal(2, kits.Count);
            Assert.EndsWith("NBA_boston_home.png", kits[0]);    // home first

            var duke = new Team { Id = 5, City = "Duke", Name = "Blue Devils", Abbreviation = "DUKE", Division = "acc" };
            var uconn = new Team { Id = 6, City = "UConn", Name = "Huskies", Abbreviation = "UCON", Division = "be" };
            Assert.EndsWith("1CM_duke.png", g.LogoFor(duke));
            Assert.EndsWith("1CM_connecticut.png", g.LogoFor(uconn)); // the pack's spelling of a short name
            // A pro club in a college town keeps its own crest and kit (Danny, run 45): school name alone isn't enough.
            var proDuke = new Team { Id = 7, City = "Duke", Name = "Thunder", Abbreviation = "DKT", Division = "Gulf" };
            Assert.Null(g.LogoFor(proDuke));
            Assert.Empty(g.JerseysFor(proDuke));

            Player P(string first, string last) => new() { Id = 9, FirstName = first, LastName = last };
            Assert.NotNull(g.PhotoFor(P("Stephen", "Curry")));
            Assert.Null(g.PhotoFor(P("Stephen", "Currie")));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void In_a_real_career_only_college_programs_take_college_graphics()
    {
        // Danny (run 45): lower-tier pro clubs were pulling college logos. Every real program still matches; no pro club does.
        var s = Hardwood.Core.Season.GameSession.NewGame(31, 5, null, realWorld: true);
        var programs = s.World.College!.Programs.ToList();
        Assert.All(programs, t => Assert.NotNull(GraphicsPack.SchoolKey(t)));
        var pros = s.World.Competitions.Where(c => Hardwood.Core.World.UsaPyramid.IsNational(c.Key)).SelectMany(c => c.Clubs).Concat(s.League.Teams).ToList();
        Assert.NotEmpty(pros);
        Assert.All(pros, t => Assert.Null(GraphicsPack.SchoolKey(t)));
    }

    [Fact]
    public void No_pack_folder_means_an_empty_pack()
    {
        Assert.True(GraphicsPack.Find(TempDir()).IsEmpty);
        Assert.True(GraphicsPack.Find(null, "").IsEmpty);
    }

    [Fact]
    public void Cards_dress_rostered_players_in_their_club_colours_with_a_number_that_stays_theirs()
    {
        var league = new WorldGenerator(new Rng(4)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));
        foreach (var p in league.Teams.SelectMany(t => t.Roster).Concat(league.FreeAgents)) p.FaceFile = "African/x.png";
        var cards = PortraitCard.For(league, null, jerseys: true);
        var t = league.Teams[0];
        var mine = cards.First(c => c.PlayerId == t.Roster[0].Id);
        Assert.Equal(t.PrimaryColor, mine.Jersey!.PrimaryColor);
        Assert.Equal(JerseySpec.NumberFor(t.Roster[0].Id), mine.Jersey.Number);
        Assert.InRange(mine.Jersey.Number, 0, 55);
        if (league.FreeAgents.Count > 0) Assert.Null(cards.First(c => c.PlayerId == league.FreeAgents[0].Id).Jersey); // no club, no jersey
        // A jersey changes the card's key, so existing cards are redrawn once.
        var plain = mine with { Jersey = null };
        Assert.NotEqual(plain.Key(CardRenderer.Version), mine.Key(CardRenderer.Version));
        Assert.Null(PortraitCard.For(league, null).First().Jersey);   // off: no jerseys
    }

    [Fact]
    public void The_jersey_is_drawn_in_the_club_colour_below_the_face()
    {
        using var face = new SKBitmap(256, 256);
        face.Erase(new SKColor(200, 150, 120));
        using var img = CardRenderer.Render(new PortraitCard(1, 120, 130, null, null, "x", new JerseySpec("#00FF00", "#FFFFFF", 7)), face, 512);
        using var bmp = SKBitmap.FromImage(img);
        var shoulder = bmp.GetPixel(230, 470);                   // left shoulder of the shirt
        Assert.True(shoulder.Green > 140 && shoulder.Red < 90 && shoulder.Blue < 90, $"shoulder {shoulder}");
    }
}
