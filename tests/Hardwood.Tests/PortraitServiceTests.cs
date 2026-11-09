using System.Diagnostics;
using System.Text.Json;
using Hardwood.Core.Model;
using Hardwood.Core.Portraits;
using Hardwood.Core.Season;
using Hardwood.Desktop;
using Hardwood.Desktop.Portraits;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

public class PortraitServiceTests
{
    private readonly ITestOutputHelper _out;
    public PortraitServiceTests(ITestOutputHelper output) => _out = output;

    private static void MakePack(string root, int perFolder)
    {
        using var bmp = new SKBitmap(64, 64);
        using (var c = new SKCanvas(bmp)) { c.Clear(SKColors.Transparent); using var p = new SKPaint { Color = SKColors.SaddleBrown }; c.DrawCircle(32, 30, 22, p); }
        using var data = bmp.Encode(SKEncodedImageFormat.Png, 100);
        var bytes = data.ToArray();
        foreach (var e in Enum.GetValues<Ethnicity>())
        {
            var folder = EthnicityInfo.FolderName(e);
            Directory.CreateDirectory(Path.Combine(root, folder));
            for (int i = 1; i <= perFolder; i++) File.WriteAllBytes(Path.Combine(root, folder, $"{folder}{i * 3}.png"), bytes);
        }
    }

    [Fact]
    public async Task Game_draws_every_card_itself_and_only_redraws_what_changed()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hw-auto-{Guid.NewGuid():N}");
        var pack = Path.Combine(dir, "FMNEWGANv2");
        Directory.CreateDirectory(dir);
        MakePack(pack, 400);
        var savePath = Path.Combine(dir, "career.hwsave");
        AppSettings.Use(new AppSettings { FaceFolder = pack });
        var svc = PortraitService.Instance;
        svc.ResetLibrary();
        try
        {
            var session = GameSession.NewGame(2026, 1, savePath);
            var league = session.League;
            var shown = league.Teams.SelectMany(t => t.Roster).Concat(league.FreeAgents).ToList();

            var sw = Stopwatch.StartNew();
            svc.Request(session);
            await svc.Current;
            _out.WriteLine($"first build: {shown.Count} cards in {sw.Elapsed.TotalSeconds:F1}s; status '{svc.Status}'");

            // Every shown player got a face of his own ethnicity, no face twice, and a card on disk.
            Assert.All(shown, p => Assert.StartsWith(EthnicityInfo.FolderName(p.Ethnicity) + "/", p.FaceFile));
            Assert.Equal(shown.Count, shown.Select(p => p.FaceFile).Distinct().Count());
            var folder = Desktop.Controls.Portraits.FolderFor(savePath);
            Assert.All(shown, p => Assert.True(File.Exists(Path.Combine(folder, p.Id + ".png")), $"card {p.Id}"));
            Assert.Equal("", svc.Status);

            // Nothing changed → nothing redrawn.
            var stamps = shown.ToDictionary(p => p.Id, p => File.GetLastWriteTimeUtc(Path.Combine(folder, p.Id + ".png")));
            await Task.Delay(30);
            svc.Request(session);
            await svc.Current;
            Assert.All(shown, p => Assert.Equal(stamps[p.Id], File.GetLastWriteTimeUtc(Path.Combine(folder, p.Id + ".png"))));

            // A player moves up a tier → only his card is redrawn.
            var riser = shown.First(p => p.CurrentAbility < 84);
            riser.CurrentAbility = 120;
            var drifter = shown.First(p => p != riser && p.CurrentAbility is > 100 and < 110);
            drifter.CurrentAbility += 2; // same Silver tier: no redraw
            svc.Request(session);
            await svc.Current;
            Assert.NotEqual(stamps[riser.Id], File.GetLastWriteTimeUtc(Path.Combine(folder, riser.Id + ".png")));
            Assert.Equal(stamps[drifter.Id], File.GetLastWriteTimeUtc(Path.Combine(folder, drifter.Id + ".png")));
            var state = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(folder, PortraitService.StateFile)))!;
            Assert.Contains("|Gold|", state[riser.Id.ToString()]);

            // Faces are stored in the save and survive a reload unchanged.
            var faces = shown.ToDictionary(p => p.Id, p => p.FaceFile);
            session.SaveNow();
            var reloaded = GameSession.Load(savePath);
            foreach (var p in reloaded.League.Teams.SelectMany(t => t.Roster).Concat(reloaded.League.FreeAgents))
                Assert.Equal(faces[p.Id], p.FaceFile);
        }
        finally
        {
            AppSettings.Use(null);
            svc.ResetLibrary();
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Without_a_face_pack_players_keep_the_avatar()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hw-nopack-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        AppSettings.Use(new AppSettings { FaceFolder = Path.Combine(dir, "missing") });
        try
        {
            var session = GameSession.NewGame(7, 1, Path.Combine(dir, "career.hwsave"));
            PortraitService.Instance.Request(session);
            Assert.Equal("Portraits: face pack not found", PortraitService.Instance.Status);
            Assert.All(session.League.Teams.SelectMany(t => t.Roster), p => Assert.Null(p.FaceFile));
            Assert.False(Directory.Exists(Desktop.Controls.Portraits.FolderFor(Path.Combine(dir, "career.hwsave"))));
        }
        finally
        {
            AppSettings.Use(null);
            Directory.Delete(dir, true);
        }
    }
}
