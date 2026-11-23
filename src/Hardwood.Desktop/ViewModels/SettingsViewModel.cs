using Hardwood.Core.Season;

namespace Hardwood.Desktop.ViewModels;

/// <summary>In-game Settings page: the assistant, face and logo packs.</summary>
public sealed partial class SettingsViewModel : PageViewModel
{
    public override string Title => "Settings";
    public GraphicsSettingsViewModel Graphics { get; }
    /// <summary>The assistant's switch and level (decisions #115-118).</summary>
    public GuideViewModel Guide => Shell.Guide;

    public SettingsViewModel(MainViewModel shell) : base(shell) =>
        Graphics = new GraphicsSettingsViewModel(() => Leagues(shell), () => shell.RequestPortraits(), () => shell.Session, () => shell.AfterTick());

    /// <summary>Save now (D74 B, #635): the game also saves every 7 days, at phase changes and on quit.</summary>
    public bool CanSave => Shell.Session?.Save != null && !Shell.IsBusy;
    public string SaveNote
    {
        get
        {
            var s = Shell.Session;
            if (s?.Save == null) return "This career has no save file.";
            if (s.LastSaved == default) return "Not saved yet.";
            return $"Last saved on {s.LastSaved:MMM d, yyyy} in the game ({s.LastSavedAt:h:mm:ss tt} on your clock). The game saves every {GameSession.SaveEveryDays} days, whenever the season moves on, after your decisions, and when you go to the main menu or close the window; a crash loses at most the days since the last save.";
        }
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void SaveCareer()
    {
        if (!CanSave) return;
        Shell.Session!.SaveNow();
        OnPropertyChanged(nameof(SaveNote));
        Shell.RefreshHeader();
    }

    /// <summary>M6 (run 153): the master switch and volume for the live game's crowd (the live view's speaker button
    /// changes the same settings).</summary>
    public bool SoundOn
    {
        get => AppSettings.Current.SoundOn;
        set { if (AppSettings.Current.SoundOn == value) return; AppSettings.Current.SoundOn = value; AppSettings.Current.Save(); OnPropertyChanged(); }
    }
    public double SoundVolume
    {
        get => Math.Round(AppSettings.Current.SoundVolume * 100);
        set
        {
            double v = Math.Clamp(value / 100.0, 0, 1);
            if (Math.Abs(AppSettings.Current.SoundVolume - v) < 0.001) return;
            AppSettings.Current.SoundVolume = v; AppSettings.Current.Save();
            OnPropertyChanged(); OnPropertyChanged(nameof(SoundVolumeText));
        }
    }
    public string SoundVolumeText => $"{SoundVolume:0}%";

    /// <summary>Fight songs A (run 161): Danny's own song files (tier 3), which play instead of the composed songs.</summary>
    public string FightSongsText
    {
        get
        {
            int n = Audio.FightSongLibrary.CustomCount();
            return $"Custom fight songs: {(n == 0 ? "none" : n.ToString())} found";
        }
    }
    public string FightSongsFolder => Audio.FightSongLibrary.CustomFolder;

    /// <summary>Fight songs B (run 167): the real public-domain songs the band knows so far (tier 1).</summary>
    public string RealFightSongsText
    {
        get
        {
            var real = Hardwood.Core.Presentation.RealFightSongs.All.Values.OrderBy(s => s.Program).ToList();
            return real.Count == 0 ? "" : $"Real fight songs so far ({real.Count}): " + string.Join(", ", real.Select(s => $"{s.Program}, {s.Describe()}")) + ".";
        }
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void OpenFightSongs()
    {
        try
        {
            Directory.CreateDirectory(FightSongsFolder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(FightSongsFolder) { UseShellExecute = true });
        }
        catch (Exception) { }
        Audio.FightSongLibrary.Forget();
        OnPropertyChanged(nameof(FightSongsText));
    }

    /// <summary>Text and interface size (roadmap 1): 90-130% of the standard size.</summary>
    public IReadOnlyList<string> SizeChoices { get; } = UiScale.Percents.Select(UiScale.Label).ToList();

    public string Size
    {
        get
        {
            int pct = (int)Math.Round(UiScale.Current * 100);
            int nearest = UiScale.Percents.OrderBy(p => Math.Abs(p - pct)).First();
            return UiScale.Label(nearest);
        }
        set
        {
            int i = SizeChoices.ToList().IndexOf(value);
            if (i < 0) return;
            UiScale.Set(UiScale.Percents[i] / 100.0);
            OnPropertyChanged();
        }
    }

    /// <summary>The top league and every competition in the world, for logo matching and the team list.</summary>
    public static IEnumerable<(string league, IEnumerable<Core.Model.Team> teams)> Leagues(MainViewModel shell)
    {
        if (shell.Session is not { } s) yield break;
        yield return (Hardwood.Core.Names.Brand.Top, s.League.Teams);
        foreach (var c in s.World.Competitions.Where(c => c.Profile.Kind != Core.World.CompetitionKind.Continental))
            yield return (c.Profile.Name, c.Clubs);
    }

    public override void Refresh()
    {
        OnPropertyChanged(nameof(SaveNote));
        OnPropertyChanged(nameof(CanSave));
        Graphics.UpdateFaceStatus();
        Graphics.UpdateLogoStatus();
        Graphics.UpdateNameStatus();
    }
}
