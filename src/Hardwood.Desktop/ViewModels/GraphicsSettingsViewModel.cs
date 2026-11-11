using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hardwood.Core.Graphics;
using Hardwood.Core.Model;
using Hardwood.Desktop.Graphics;

namespace Hardwood.Desktop.ViewModels;

/// <summary>Face pack and logo pack folders (app settings), shown on the start screen and the in-game Settings page.</summary>
public sealed partial class GraphicsSettingsViewModel : ViewModelBase
{
    private readonly Func<IEnumerable<(string league, IEnumerable<Team> teams)>> _leagues;
    private readonly Action? _changed;

    [ObservableProperty] private string _faceFolder = AppSettings.Current.FaceFolder;
    [ObservableProperty] private string _faceFolderStatus = "";
    [ObservableProperty] private string _logoFolder = AppSettings.Current.LogoFolder;
    [ObservableProperty] private string _logoFolderStatus = "";
    [ObservableProperty] private string _logoNote = "";

    /// <summary>A sample of clubs, to see the pack at a glance (pack logo or drawn crest).</summary>
    public ObservableCollection<Team> Sample { get; } = new();

    // Name pack (real names for clubs, leagues and conferences; a file the player keeps, never shipped with the game).
    [ObservableProperty] private string _namePackFile = AppSettings.Current.NamePackFile;
    [ObservableProperty] private string _namePackStatus = "";
    [ObservableProperty] private string _namePackNote = "";
    private readonly Func<Hardwood.Core.Season.GameSession?>? _session;
    private readonly Action? _renamed;
    /// <summary>The career's clubs can be renamed only in-game (the start screen applies the pack when the career begins).</summary>
    public bool CanApplyNames => _session?.Invoke() != null;

    /// <param name="leagues">The clubs in the game (or the preview league on the start screen), grouped by league.</param>
    /// <param name="changed">Called after a folder changes (e.g. redraw portraits).</param>
    /// <param name="session">The career in play (in-game Settings), for "Apply to this career".</param>
    /// <param name="renamed">Called after the career's clubs are renamed (refresh every page).</param>
    public GraphicsSettingsViewModel(Func<IEnumerable<(string league, IEnumerable<Team> teams)>> leagues, Action? changed = null,
        Func<Hardwood.Core.Season.GameSession?>? session = null, Action? renamed = null)
    {
        _leagues = leagues;
        _changed = changed;
        _session = session;
        _renamed = renamed;
        UpdateFaceStatus();
        UpdateLogoStatus();
        UpdateNameStatus();
    }

    partial void OnNamePackFileChanged(string value)
    {
        AppSettings.Current.NamePackFile = value;
        AppSettings.Current.Save();
        NamePackService.ApplyStatic();
        UpdateNameStatus();
        _changed?.Invoke();
    }

    public void UpdateNameStatus()
    {
        NamePackService.Current(out var status);
        var applied = _session?.Invoke()?.NamePackApplied;
        NamePackStatus = status + (applied != null ? $" This career wears {applied}." : "");
    }

    /// <summary>Rename this career's clubs from the pack (new careers get it automatically).</summary>
    [RelayCommand]
    private void ApplyNames()
    {
        var s = _session?.Invoke();
        if (s == null) return;
        if (NamePackService.Current(out var status) is not { } pack) { NamePackNote = status; return; }
        int n = pack.ApplyToCareer(s);
        s.SaveNow();
        LogoPackService.UseTeams(s.League.Teams.Concat(s.World.Clubs).Concat(s.World.College?.Programs ?? new List<Team>()));
        LogoPackService.Reload();
        NamePackNote = $"Renamed {n} clubs from {pack.Name}.";
        UpdateNameStatus();
        UpdateLogoStatus();
        _renamed?.Invoke();
    }

    partial void OnFaceFolderChanged(string value)
    {
        AppSettings.Current.FaceFolder = value;
        AppSettings.Current.Save();
        Portraits.PortraitService.Instance.ResetLibrary();
        UpdateFaceStatus();
        _changed?.Invoke();
    }

    partial void OnLogoFolderChanged(string value)
    {
        AppSettings.Current.LogoFolder = value;
        AppSettings.Current.Save();
        LogoPackService.Reload();
        UpdateLogoStatus();
        _changed?.Invoke();
    }

    [RelayCommand]
    private void RescanLogos()
    {
        LogoPackService.Reload();
        UpdateLogoStatus();
        _changed?.Invoke();
    }

    /// <summary>Creates the folder if needed and writes the list of club names the game looks for.</summary>
    [RelayCommand]
    private void WriteTeamList()
    {
        try
        {
            var path = LogoPack.WriteTeamList(LogoFolder, _leagues());
            LogoNote = $"Wrote {Path.GetFileName(path)} to the logo folder: name each image after a club in it.";
        }
        catch (Exception ex) { LogoNote = "Couldn't write the team list: " + ex.Message; }
    }

    public void UpdateFaceStatus()
    {
        if (Core.Portraits.FaceLibrary.LooksLikePack(FaceFolder))
        {
            int folders = Enum.GetValues<Ethnicity>().Count(e => Directory.Exists(Path.Combine(FaceFolder, EthnicityInfo.FolderName(e))));
            FaceFolderStatus = $"Face pack found ({folders} of {Enum.GetValues<Ethnicity>().Length} ethnicity folders). Player cards are drawn automatically as you play.";
        }
        else FaceFolderStatus = "No face pack here. Players keep the drawn avatar until you point this at your FMNEWGANv2 folder.";
    }

    public void UpdateLogoStatus()
    {
        var leagues = _leagues().Select(l => (l.league, teams: l.teams.ToList())).ToList();
        var all = leagues.SelectMany(l => l.teams).DistinctBy(t => t.Id).ToList();
        if (LogoPackService.AllTeams == null || LogoPackService.AllTeams.Count != all.Count) LogoPackService.UseTeams(all);
        var pack = LogoPackService.Pack;
        if (pack.FileCount == 0)
            LogoFolderStatus = Directory.Exists(LogoFolder)
                ? "No images in this folder yet. Clubs use the drawn crests. Write the team list to see the names to use."
                : "Folder not found. Clubs use the drawn crests. Choose a folder of PNG/JPG logos, or write the team list to create it.";
        else
        {
            var parts = leagues.Where(l => l.teams.Count > 0)
                .Select(l => $"{l.league} {LogoPackService.Matched(l.teams)}/{l.teams.Count}");
            int matched = LogoPackService.Matched(all);
            LogoFolderStatus = $"{pack.FileCount} image{(pack.FileCount == 1 ? "" : "s")} found, {matched} of {all.Count} clubs matched ({string.Join(" · ", parts)}). The rest keep drawn crests.";
        }
        Sample.Clear();
        foreach (var t in all.OrderByDescending(t => LogoPackService.FileFor(t) != null).ThenBy(t => t.Id).Take(12)) Sample.Add(t);
    }
}
