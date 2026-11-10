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

    /// <param name="leagues">The clubs in the game (or the preview league on the start screen), grouped by league.</param>
    /// <param name="changed">Called after a folder changes (e.g. redraw portraits).</param>
    public GraphicsSettingsViewModel(Func<IEnumerable<(string league, IEnumerable<Team> teams)>> leagues, Action? changed = null)
    {
        _leagues = leagues;
        _changed = changed;
        UpdateFaceStatus();
        UpdateLogoStatus();
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
