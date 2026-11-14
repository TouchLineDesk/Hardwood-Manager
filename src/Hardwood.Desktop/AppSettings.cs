using System.Text.Json;

namespace Hardwood.Desktop;

/// <summary>App-wide settings (not per save), stored in %APPDATA%\Hardwood Manager\settings.json.</summary>
public sealed class AppSettings
{
    /// <summary>Folder of the FMNEWGAN face pack (one subfolder per ethnicity). Defaults to Desktop\FMNEWGANv2.</summary>
    public string FaceFolder { get; set; } = DefaultFaceFolder();

    /// <summary>Folder of team logo images (a logo pack); empty = drawn crests only. See <see cref="Core.Graphics.LogoPack"/>.</summary>
    public string LogoFolder { get; set; } = DefaultLogoFolder();

    public static string DefaultLogoFolder() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Hardwood Manager", "Logos");

    /// <summary>A name pack file or folder (real names for clubs, leagues and conferences); empty = Documents\Hardwood Manager\Names.</summary>
    public string NamePackFile { get; set; } = "";

    /// <summary>Read Danny's graphics packs (prographics / collegegraphics folders inside the face folder) for logos, banners and jerseys (#350).</summary>
    public bool UseGraphicsPack { get; set; } = true;

    /// <summary>Use the graphics pack's photos for real NBA players' cards instead of a generated face (#350).</summary>
    public bool RealPlayerPhotos { get; set; } = true;

    /// <summary>Player cards show the player in his club's jersey (#351).</summary>
    public bool CardJerseys { get; set; } = true;

    /// <summary>The pre-game studio show before a game you watch (#446); "Skip the show" turns it off.</summary>
    public bool StudioShow { get; set; } = true;

    /// <summary>Let the game draw player card portraits automatically.</summary>
    public bool AutoPortraits { get; set; } = true;

    /// <summary>The assistant panel (decisions #115-118): on by default, how much it explains, and what you've learned so far.</summary>
    public bool GuideOn { get; set; } = true;
    public bool GuideCollapsed { get; set; }
    public Core.Guide.GuideLevel GuideLevel { get; set; } = Core.Guide.GuideLevel.Auto;
    public Core.Guide.GuideProgress GuideProgress { get; set; } = new();

    /// <summary>Text and interface size (roadmap 1): 0.9-1.3, applied to the whole window. 1.0 = 100%.</summary>
    public double UiScale { get; set; } = 1.0;

    public static string DefaultFaceFolder() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "FMNEWGANv2");

    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    /// <summary>Overridable for tests.</summary>
    public static string FilePath { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hardwood Manager", "settings.json");

    private static AppSettings? _current;
    public static AppSettings Current => _current ??= Load();

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Opts) ?? new AppSettings();
        }
        catch (Exception) { /* a broken settings file falls back to defaults */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Opts));
        }
        catch (Exception) { /* settings are a convenience; never crash over them */ }
    }

    /// <summary>Tests: use these settings instead of the file.</summary>
    public static void Use(AppSettings? settings) => _current = settings;
}
