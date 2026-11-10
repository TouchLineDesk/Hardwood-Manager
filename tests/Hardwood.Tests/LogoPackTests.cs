using Avalonia.Headless.XUnit;
using Hardwood.Core;
using Hardwood.Core.Generation;
using Hardwood.Core.Graphics;
using Hardwood.Core.Model;
using Hardwood.Core.Portraits;
using Hardwood.Desktop;
using Hardwood.Desktop.Graphics;
using Hardwood.Desktop.Portraits;
using Hardwood.Desktop.ViewModels;
using SkiaSharp;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Logo packs (decision #77): a folder of club images replaces the drawn crests, on screen and on player cards.</summary>
[Collection(SharedUiState.Name)]
public class LogoPackTests
{
    private static League NewLeague() => new WorldGenerator(new Rng(3)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hw-logos-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Png(string path, SKColor colour, int w = 100, int h = 100)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var bmp = new SKBitmap(w, h);
        bmp.Erase(colour);
        using var data = bmp.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }

    [Fact]
    public void Files_match_by_full_name_id_mapping_and_unique_short_names()
    {
        var league = NewLeague();
        var (a, b, c, d, e) = (league.Teams[0], league.Teams[1], league.Teams[2], league.Teams[3], league.Teams[4]);
        var dir = TempDir();
        try
        {
            Png(Path.Combine(dir, a.FullName.ToUpperInvariant().Replace(' ', '_') + ".png"), SKColors.Red); // case and punctuation ignored
            Png(Path.Combine(dir, "east", $"team-{b.Id}.png"), SKColors.Red);                                 // subfolders and ids
            Png(Path.Combine(dir, c.Abbreviation.ToLowerInvariant() + ".jpg"), SKColors.Red);                // abbreviation
            Png(Path.Combine(dir, "weird name.png"), SKColors.Red);
            File.WriteAllText(Path.Combine(dir, LogoPack.MapFile), $"# my pack\n{d.FullName} = weird name.png\n");
            Png(Path.Combine(dir, "readme.txt.bak"), SKColors.Red); // not an image extension

            var pack = LogoPack.Scan(dir);
            Assert.Equal(4, pack.FileCount);
            Assert.True(LogoPack.LooksLikePack(dir));
            Assert.EndsWith(".png", pack.Find(a, league.Teams));
            Assert.Contains("east", pack.Find(b, league.Teams));
            Assert.NotNull(pack.Find(c, league.Teams));
            Assert.EndsWith("weird name.png", pack.Find(d, league.Teams));
            Assert.Null(pack.Find(e, league.Teams));

            // A short name two clubs share matches neither.
            var twin = new Team { Id = 999, City = "Elsewhere", Name = "Other", Abbreviation = c.Abbreviation, Division = "X" };
            Assert.Null(pack.Find(c, league.Teams.Append(twin).ToList()));

            Assert.False(LogoPack.LooksLikePack(Path.Combine(dir, "missing")));
            Assert.Null(LogoPack.Scan(null).Find(a));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void The_team_list_names_every_club()
    {
        var league = NewLeague();
        var dir = Path.Combine(TempDir(), "new");
        try
        {
            var path = LogoPack.WriteTeamList(dir, new[] { ("NABL", (IEnumerable<Team>)league.Teams) });
            var text = File.ReadAllText(path);
            Assert.All(league.Teams, t => Assert.Contains(t.FullName + ".png", text));
            Assert.Contains(LogoPack.MapFile, text);
        }
        finally { Directory.Delete(Path.GetDirectoryName(dir)!, true); }
    }

    [Fact]
    public void A_pack_logo_replaces_the_drawn_crest_on_cards_and_changing_it_redraws()
    {
        var dir = TempDir();
        try
        {
            var logo = Path.Combine(dir, "logo.png");
            Png(logo, new SKColor(0, 255, 0));
            using var face = new SKBitmap(256, 256);
            face.Erase(SKColors.Transparent);
            var drawn = new PortraitCard(1, 120, 140, new CrestSpec("TST", "#1D428A", "#C8102E", 0), null, "x");
            var packed = drawn with { Crest = drawn.Crest! with { LogoFile = logo } };
            using var img = CardRenderer.Render(packed, face);
            using var bmp = SKBitmap.FromImage(img);
            var px = bmp.GetPixel(48, 210);
            Assert.True(px.Green > 200 && px.Red < 60 && px.Blue < 60, $"crest pixel {px}");
            using var plain = SKBitmap.FromImage(CardRenderer.Render(drawn, face));
            Assert.False(plain.GetPixel(48, 210).Green > 200 && plain.GetPixel(48, 210).Red < 60);

            Assert.NotEqual(drawn.Key(CardRenderer.Version), packed.Key(CardRenderer.Version));
            var before = packed.Key(CardRenderer.Version);
            File.SetLastWriteTimeUtc(logo, DateTime.UtcNow.AddMinutes(5));
            Assert.NotEqual(before, packed.Key(CardRenderer.Version)); // a replaced logo redraws the cards
        }
        finally { Directory.Delete(dir, true); }
    }

    [AvaloniaFact]
    public void Settings_find_the_pack_and_the_crest_control_uses_it()
    {
        var league = NewLeague();
        var dir = TempDir();
        var saved = AppSettings.Current;
        var savedPath = AppSettings.FilePath;
        try
        {
            AppSettings.Use(new AppSettings { LogoFolder = Path.Combine(dir, "none"), FaceFolder = dir });
            AppSettings.FilePath = Path.Combine(dir, "settings.json");
            var vm = new GraphicsSettingsViewModel(() => new[] { ("NABL", (IEnumerable<Team>)league.Teams) });
            Assert.Contains("not found", vm.LogoFolderStatus);
            Assert.Equal(12, vm.Sample.Count);

            Png(Path.Combine(dir, "logos", league.Teams[5].FullName + ".png"), SKColors.Orange, 120, 60);
            vm.LogoFolder = Path.Combine(dir, "logos");
            Assert.Contains("1 of 30 clubs matched", vm.LogoFolderStatus);
            Assert.Equal(league.Teams[5], vm.Sample[0]); // matched clubs shown first
            Assert.NotNull(LogoPackService.BitmapFor(league.Teams[5]));
            Assert.Null(LogoPackService.BitmapFor(league.Teams[6]));
            Assert.Equal(Path.Combine(dir, "logos"), AppSettings.Load().LogoFolder); // saved

            vm.WriteTeamListCommand.Execute(null);
            Assert.True(File.Exists(Path.Combine(dir, "logos", LogoPack.ListFile)));

            // The control draws the pack image (wide logo keeps its shape) without throwing.
            var logoCtl = new Hardwood.Desktop.Controls.TeamLogo { Team = league.Teams[5], Width = 48, Height = 48 };
            var window = new Avalonia.Controls.Window { Content = logoCtl, Width = 100, Height = 100 };
            window.Show();
            window.Close();
        }
        finally
        {
            AppSettings.Use(saved);
            AppSettings.FilePath = savedPath;
            LogoPackService.Reload();
            LogoPackService.UseTeams(null);
            Directory.Delete(dir, true);
        }
    }
}
