using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hardwood.Core;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.Season;

namespace Hardwood.Desktop.ViewModels;

public sealed class TeamChoice
{
    public required Team Team { get; init; }
    public string Name => Team.FullName;
    public string Conference => $"{Team.Conference} · {Team.Division}";
    public double CoreCa { get; init; }
    public double LeagueCoreCa { get; init; }
    public string Strength => CoreCa - LeagueCoreCa >= 6 ? "Contender" : CoreCa - LeagueCoreCa >= 1.5 ? "Playoff team" : CoreCa - LeagueCoreCa >= -4 ? "Middle of the pack" : "Rebuilding";
    public string Payroll { get; init; } = "";
    public string Star { get; init; } = "";
}

public sealed partial class StartViewModel : ViewModelBase
{
    private readonly MainViewModel _shell;
    [ObservableProperty] private string _seedText = "2026";
    [ObservableProperty] private string _savePath = DefaultSavePath();
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private TeamChoice? _selectedTeam;
    [ObservableProperty] private string _loadPath = DefaultSavePath();
    /// <summary>FMNEWGAN face pack folder for automatic player card portraits (app setting).</summary>
    [ObservableProperty] private string _faceFolder = AppSettings.Current.FaceFolder;
    [ObservableProperty] private string _faceFolderStatus = "";

    public ObservableCollection<TeamChoice> Teams { get; } = new();
    private League? _preview;

    public StartViewModel(MainViewModel shell)
    {
        _shell = shell;
        Preview();
        UpdateFaceStatus();
    }

    partial void OnFaceFolderChanged(string value)
    {
        AppSettings.Current.FaceFolder = value;
        AppSettings.Current.Save();
        Portraits.PortraitService.Instance.ResetLibrary();
        UpdateFaceStatus();
    }

    private void UpdateFaceStatus()
    {
        if (Core.Portraits.FaceLibrary.LooksLikePack(FaceFolder))
        {
            int folders = Enum.GetValues<Ethnicity>().Count(e => Directory.Exists(Path.Combine(FaceFolder, EthnicityInfo.FolderName(e))));
            FaceFolderStatus = $"Face pack found ({folders} of 12 ethnicity folders). Player cards are drawn automatically as you play.";
        }
        else FaceFolderStatus = "No face pack here. Players keep the drawn avatar until you point this at your FMNEWGANv2 folder.";
    }

    public static string DefaultSavePath()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Hardwood Manager");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "career.hwsave");
    }

    partial void OnSeedTextChanged(string value) => Preview();

    [RelayCommand]
    public void RandomSeed() => SeedText = Random.Shared.Next(1, 999_999).ToString();

    private void Preview()
    {
        if (!ulong.TryParse(SeedText, out var seed)) { Status = "Seed must be a number."; return; }
        Status = "";
        _preview = new WorldGenerator(new Rng(seed)).Generate("North American Basketball League", 2026, new DateOnly(2026, 10, 1));
        Teams.Clear();
        double leagueCore = _preview.Teams.Average(t => t.Roster.OrderByDescending(p => p.CurrentAbility).Take(8).Average(p => p.CurrentAbility));
        foreach (var t in _preview.Teams.OrderBy(t => t.Conference).ThenBy(t => t.Division).ThenBy(t => t.City))
        {
            var core = t.Roster.OrderByDescending(p => p.CurrentAbility).Take(8).Average(p => p.CurrentAbility);
            var star = t.Roster.OrderByDescending(p => p.CurrentAbility).First();
            Teams.Add(new TeamChoice
            {
                Team = t, CoreCa = core, LeagueCoreCa = leagueCore,
                Payroll = $"${ContractMarket.Payroll(t) / 1e6:F0}M",
                Star = $"{star.Name} ({star.PrimaryPosition}, {star.CurrentAbility})",
            });
        }
        SelectedTeam = Teams.FirstOrDefault();
    }

    [RelayCommand]
    public void NewGame()
    {
        if (_preview == null || SelectedTeam == null || !ulong.TryParse(SeedText, out var seed)) return;
        try
        {
            if (File.Exists(SavePath)) File.Delete(SavePath);
            var session = GameSession.NewGame(seed, SelectedTeam.Team.Id, SavePath);
            _shell.AttachSession(session);
        }
        catch (Exception ex) { Status = ex.Message; }
    }

    [RelayCommand]
    public void LoadGame()
    {
        try
        {
            if (!File.Exists(LoadPath)) { Status = $"No save at {LoadPath}"; return; }
            var session = GameSession.Load(LoadPath);
            _shell.AttachSession(session);
        }
        catch (Exception ex) { Status = ex.Message; }
    }
}
