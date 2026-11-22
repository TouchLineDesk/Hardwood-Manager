using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Hardwood.Core.College;
using Hardwood.Core.Model;
using Hardwood.Core.Presentation;
using Hardwood.Core.Season;
using Hardwood.Desktop.Controls;
using Hardwood.Desktop.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>Run 151, M4 of the 2D match engine (claude/match-engine-2d-design.md §D; #1034): the arena dressing. The maple
/// floor under every court, cached with the rest of the static layer; home and competition floors for college and the pros
/// (conference tournaments, March Madness, the Final Four, the playoffs, the Finals, a continental cup, a league abroad); the
/// stands filled by the crowd and coloured by its sides; ad and ribbon boards in panels; benches and the scorer's table; and
/// the lines by league. Presentation only: no dress draws a random number or touches a result.</summary>
[Collection(SharedUiState.Name)]
public class Run151ArenaDressingTests
{
    private readonly ITestOutputHelper _out;
    public Run151ArenaDressingTests(ITestOutputHelper output) => _out = output;

    private static Team Club(int id, string city, string name, string abbr, string primary, string secondary, double fans = 50) =>
        new() { Id = id, City = city, Name = name, Abbreviation = abbr, Division = "D", PrimaryColor = primary, SecondaryColor = secondary, Money = new ClubFinance { FanBase = fans } };

    private static readonly Team Duke = Club(9001, "Duke", "Blue Devils", "DUKE", "#00539B", "#FFFFFF");
    private static readonly Team Unc = Club(9002, "North Carolina", "Tar Heels", "UNC", "#7BAFD4", "#13294B");
    private static readonly Team Boston = Club(9101, "Boston", "Harbormen", "BOS", "#007A33", "#BA9653", fans: 80);
    private static readonly Team Denver = Club(9102, "Denver", "Peaks", "DEN", "#0E2240", "#FEC524", fans: 30);

    private static IEnumerable<(string Name, ArenaDress Dress, CourtMarkings Lines)> Floors() => new[]
    {
        ("college-home", ArenaDress.For(Duke, new CollegeGame { Date = new DateOnly(2026, 12, 5), HomeId = 9001, AwayId = 9002, Stage = "Conference" }), CourtMarkings.College),
        ("conference-tournament", ArenaDress.For(Duke, new CollegeGame { Date = new DateOnly(2027, 3, 13), Stage = "Conference tournament", Event = "ACC tournament", Round = 3 }, Unc, null), CourtMarkings.College),
        ("march-madness", ArenaDress.For(Duke, new CollegeGame { Date = new DateOnly(2027, 3, 21), Stage = CollegeWorld.Madness, Event = CollegeWorld.Madness, Round = 2 }, Unc, null), CourtMarkings.College),
        ("final-four", ArenaDress.For(Duke, new CollegeGame { Date = new DateOnly(2027, 4, 5), Stage = CollegeWorld.Madness, Event = CollegeWorld.Madness, Round = 6 }, Unc, null), CourtMarkings.College),
        ("pro-regular", ArenaDress.ForPro(Boston, Denver, "NABL"), CourtMarkings.Nba),
        ("pro-playoffs", ArenaDress.ForPro(Boston, Denver, "NABL", new Series { Round = "East Semifinals", Higher = Boston, Lower = Denver, HigherWins = 1, LowerWins = 1 }), CourtMarkings.Nba),
        ("pro-finals", ArenaDress.ForPro(Boston, Denver, "NABL", new Series { Round = "Finals", Higher = Boston, Lower = Denver, HigherWins = 2, LowerWins = 1 }), CourtMarkings.Nba),
        ("abroad-league", ArenaDress.ForPro(Club(9201, "Madrid", "Reales", "MAD", "#5A2D82", "#F4F1EA"), Denver, "ESP"), CourtMarkings.Fiba),
        ("continental-cup", ArenaDress.ForPro(Club(9201, "Madrid", "Reales", "MAD", "#5A2D82", "#F4F1EA"), Denver, "EEL", cup: "Euro Elite League"), CourtMarkings.Fiba),
    };

    [Fact]
    public void Every_competition_has_its_own_floor_boards_and_crowd()
    {
        var f = Floors().ToDictionary(x => x.Name, x => x.Dress);
        // Campus: the program's colours, its school and nickname on the baselines, its boards; a typical house without a world.
        Assert.Equal(Duke.PrimaryColor, f["college-home"].Paint);
        Assert.Contains("HSN COLLEGE HOOPS", f["college-home"].Boards);
        Assert.Null(f["college-home"].AwayCrowdColour);
        // Events: neutral, the away end in the visitors' colours, a fuller house the deeper the run.
        Assert.True(f["conference-tournament"].Neutral);
        Assert.Contains("ACC TOURNAMENT", f["conference-tournament"].Boards);
        Assert.Equal(Unc.PrimaryColor, f["march-madness"].AwayCrowdColour);
        Assert.Equal("ROUND OF 32", f["march-madness"].Sideline);
        Assert.Equal(1.0, f["final-four"].Crowd);
        Assert.Contains("FINAL FOUR", f["final-four"].Boards);
        Assert.True(f["march-madness"].Crowd < f["final-four"].Crowd);
        // The pros: the home floor, the playoffs' boards and game number, the Finals' gold trim.
        Assert.Equal(Boston.PrimaryColor, f["pro-regular"].Paint);
        Assert.Contains("NABL", f["pro-regular"].Boards);
        Assert.Equal("NABL PLAYOFFS · EAST SEMIFINALS · GAME 3", f["pro-playoffs"].Sideline);
        Assert.Equal(Denver.PrimaryColor, f["pro-playoffs"].AwayCrowdColour);
        Assert.Equal(ArenaDress.FinalsTrim, f["pro-finals"].Trim);
        Assert.Contains("NABL FINALS", f["pro-finals"].Boards);
        Assert.Equal("NABL FINALS · GAME 4", f["pro-finals"].Sideline);
        Assert.Contains("ESP", f["abroad-league"].Boards);
        Assert.Equal(ArenaDress.EventPaint, f["continental-cup"].Paint);
        Assert.Contains("EURO ELITE LEAGUE", f["continental-cup"].Boards);
        // A bigger fan base fills more seats; the playoffs sell out.
        Assert.True(ArenaDress.ForPro(Boston, Denver, "NABL").Crowd > ArenaDress.ForPro(Denver, Boston, "NABL").Crowd);
        Assert.Equal(1.0, f["pro-playoffs"].Crowd);
        // Every crowd in range; no board is blank; two dresses for the same game are equal (the static cache's key).
        Assert.All(f.Values, d => { Assert.InRange(d.Crowd, 0.35, 1.0); Assert.All(d.Boards, b => Assert.False(string.IsNullOrWhiteSpace(b))); });
        Assert.Equal(ArenaDress.ForPro(Boston, Denver, "NABL"), ArenaDress.ForPro(Boston, Denver, "NABL"));
    }

    [Fact]
    public void A_campus_house_fills_with_tradition_size_and_a_big_visitor()
    {
        double small = ArenaDress.CampusCrowd(25, 4500, false), blue = ArenaDress.CampusCrowd(90, 20000, false);
        Assert.True(blue > small + 0.3, $"{small} vs {blue}");
        Assert.True(ArenaDress.CampusCrowd(25, 4500, true) > small);
        Assert.InRange(small, 0.35, 0.6);
        Assert.Equal(1.0, ArenaDress.CampusCrowd(90, 20000, true));
    }

    [AvaloniaFact]
    public void Every_floor_draws_on_maple_from_one_cached_layer_with_crisp_lines()
    {
        Assert.True(MatchCourt.HasMaple, "the maple texture is embedded");
        string? dir = Environment.GetEnvironmentVariable("HW_SCREENSHOT_DIR") is { Length: > 0 } d ? d : null;
        foreach (var (name, dress, lines) in Floors())
        {
            var court = new MatchCourt { Dress = dress, Markings = lines, Width = 1480, Height = 900,
                HomeKit = Color.Parse(dress.CrestTeam?.PrimaryColor ?? "#00539B"), AwayKit = Colors.White };
            var window = new Window { Content = court, Width = 1480, Height = 900 };
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            using (var first = window.CaptureRenderedFrame()) { }
            court.InvalidateVisual();
            using var frame = window.CaptureRenderedFrame();
            Assert.Null(MatchCourt.LastRenderError);
            Assert.True(court.StaticFromCache, $"{name}: the static layer is a cached bitmap");
            Assert.Equal(1, court.StaticBuilds);
            Assert.True(court.FramesDrawn >= 2);
            // The floor reads as maple between the lines: warm and light (a spot on the wing, away from paint and lines).
            var (s, o) = MatchCourt.Fit(court.Bounds.Size, court.Reserve);
            var px = Pixel(frame!, new Point(o.X + 36 * s, o.Y + 10 * s));
            _out.WriteLine($"{name}: floor pixel {px}, crowd {dress.Crowd:0.00}, boards [{string.Join(" | ", dress.Boards)}]");
            Assert.True(px.R > 170 && px.R > px.G && px.G > px.B, $"{name}: maple-coloured floor, got {px}");
            // A line stays white on top of the texture: the half-court line.
            var linePx = Pixel(frame!, new Point(o.X + 47 * s, o.Y + 3 * s));
            Assert.True(linePx.R > 225 && linePx.G > 225 && linePx.B > 225, $"{name}: crisp white half-court line, got {linePx}");
            if (dir != null) { Directory.CreateDirectory(dir); frame!.Save(Path.Combine(dir, $"run151-floor-{name}.png")); }
            window.Close();
        }
    }

    [AvaloniaFact]
    public void The_crowd_rises_on_a_make_and_the_layer_is_not_rebuilt_per_frame()
    {
        var dress = Floors().First(x => x.Name == "march-madness").Dress;
        var court = new MatchCourt { Dress = dress, Markings = CourtMarkings.College, Width = 1480, Height = 900 };
        var window = new Window { Content = court, Width = 1480, Height = 900 };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var players = Enumerable.Range(0, 10).Select(i => new Spot(20 + i * 5, 25)).ToArray();
        var who = Enumerable.Range(0, 10).Select(i => new Player { Id = 100 + i, FirstName = "A", LastName = $"P{i}" }).ToArray();
        ChoreoFrame F(Burst? b, double age, bool homeO) => new(players, new double[10], new bool[10], new Spot(88.75, 25), 0, -1, 300, 2, 0, "", false, b, age, homeO, who);
        court.Frame = F(null, 0, true);
        using (var quiet = window.CaptureRenderedFrame())
        {
            court.Frame = F(new Burst(0, new Spot(88.75, 25), true), 0.1, true);
            court.InvalidateVisual();
            using var loud = window.CaptureRenderedFrame();
            // Somewhere in the home stands is brighter on the make.
            int brighter = 0;
            for (int y = 10; y < 110; y += 3) for (int x = 300; x < 1200; x += 7)
                if (Lum(Pixel(loud!, new Point(x, y))) > Lum(Pixel(quiet!, new Point(x, y))) + 40) brighter++;
            _out.WriteLine($"brighter crowd pixels on a home make: {brighter}");
            Assert.True(brighter > 20);
        }
        for (int i = 0; i < 30; i++) { court.Frame = F(null, 0, i % 2 == 0); court.InvalidateVisual(); using var _ = window.CaptureRenderedFrame(); }
        _out.WriteLine($"static builds after 31 frames: {court.StaticBuilds}");
        Assert.Equal(1, court.StaticBuilds);
        window.Width = 1280; court.Width = 1280; Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        using (var _ = window.CaptureRenderedFrame()) { }
        Assert.Equal(2, court.StaticBuilds);
        window.Close();
    }

    private static double Lum(Color c) => 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;

    private static Color Pixel(Avalonia.Media.Imaging.Bitmap bmp, Point at)
    {
        var buf = new byte[4];
        int x = Math.Clamp((int)at.X, 0, bmp.PixelSize.Width - 1), y = Math.Clamp((int)at.Y, 0, bmp.PixelSize.Height - 1);
        var mem = System.Runtime.InteropServices.Marshal.AllocHGlobal(4);
        try { bmp.CopyPixels(new PixelRect(x, y, 1, 1), mem, 4, 4); System.Runtime.InteropServices.Marshal.Copy(mem, buf, 0, 4); }
        finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(mem); }
        // Rgba8888 from the headless Skia renderer.
        return Color.FromRgb(buf[0], buf[1], buf[2]);
    }
}
