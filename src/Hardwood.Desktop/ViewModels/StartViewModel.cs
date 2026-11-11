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
    /// <summary>NABL: conference and division; abroad: the league.</summary>
    public string League { get; init; } = "";
    public string Conference => League.Length > 0 ? League : $"{Team.Conference} · {Team.Division}";
    public double CoreCa { get; init; }
    public double LeagueCoreCa { get; init; }
    /// <summary>A fixed outlook (college programs are ranked nationally); otherwise from the core ability.</summary>
    public string? Outlook { get; init; }
    public string Strength => Outlook ?? (CoreCa - LeagueCoreCa >= 6 ? "Contender" : CoreCa - LeagueCoreCa >= 1.5 ? "Playoff team" : CoreCa - LeagueCoreCa >= -4 ? "Middle of the pack" : "Rebuilding");
    public string Payroll { get; init; } = "";
    public string Star { get; init; } = "";
}

/// <summary>A league outside the NABL and whether it runs the full match engine in the new career (decision #40).</summary>
public sealed partial class LeagueChoice : ObservableObject
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    [ObservableProperty] private bool _fullSim;
}

/// <summary>One coaching attribute on the coach-creation panel, adjusted with − and + inside the points budget.</summary>
public sealed partial class CoachAttrRow : ObservableObject
{
    public required Hardwood.Core.Jobs.CoachAttr Attr { get; init; }
    public required StartViewModel Owner { get; init; }
    public string Label => Hardwood.Core.Jobs.Coach.Label(Attr);
    public string Tip => Hardwood.Core.Jobs.Coach.Describe(Attr);
    [ObservableProperty] private int _value = 10;
    [CommunityToolkit.Mvvm.Input.RelayCommand] private void Up() { if (Value < Hardwood.Core.Jobs.Coach.MaxAttr && Owner.PointsLeft > 0) { Value++; Owner.CoachChanged(); } }
    [CommunityToolkit.Mvvm.Input.RelayCommand] private void Down() { if (Value > Hardwood.Core.Jobs.Coach.MinAttr) { Value--; Owner.CoachChanged(); } }
}

public sealed partial class StartViewModel : ViewModelBase
{
    private readonly MainViewModel _shell;
    [ObservableProperty] private string _seedText = "2026";
    [ObservableProperty] private string _savePath = DefaultSavePath();
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private TeamChoice? _selectedTeam;
    [ObservableProperty] private string _loadPath = DefaultSavePath();
    /// <summary>Face pack and logo pack folders (app settings).</summary>
    public GraphicsSettingsViewModel Graphics { get; }

    public ObservableCollection<TeamChoice> Teams { get; } = new();

    // Your coach (decision #70)
    [ObservableProperty] private string _coachFirst = "Alex";
    [ObservableProperty] private string _coachLast = "Morgan";
    [ObservableProperty] private string _coachNationality = "USA";
    [ObservableProperty] private decimal? _coachAge = 42;
    public List<string> Nationalities { get; } = new() { "USA", "Canada", "Spain", "France", "Serbia", "Greece", "Lithuania", "Turkey", "Australia", "Italy", "Germany", "Argentina", "Brazil", "Nigeria", "China", "Slovenia" };
    public List<Hardwood.Core.Jobs.CoachBackground> Backgrounds { get; } = Enum.GetValues<Hardwood.Core.Jobs.CoachBackground>().ToList();
    public List<Hardwood.Core.Jobs.CoachLicence> Licences { get; } = Enum.GetValues<Hardwood.Core.Jobs.CoachLicence>().ToList();
    [ObservableProperty] private Hardwood.Core.Jobs.CoachBackground _coachBackground = Hardwood.Core.Jobs.CoachBackground.None;
    [ObservableProperty] private Hardwood.Core.Jobs.CoachLicence _coachLicence = Hardwood.Core.Jobs.CoachLicence.B;
    partial void OnCoachBackgroundChanged(Hardwood.Core.Jobs.CoachBackground value) => CoachChanged();
    partial void OnCoachLicenceChanged(Hardwood.Core.Jobs.CoachLicence value) => CoachChanged();
    public ObservableCollection<CoachAttrRow> CoachAttrs { get; } = new();
    public int PointsLeft => Hardwood.Core.Jobs.Coach.Budget(CoachBackground, CoachLicence) - CoachAttrs.Sum(r => r.Value);
    public string PointsText => PointsLeft >= 0 ? $"{PointsLeft} points left · reputation {Hardwood.Core.Jobs.Coach.StartingReputation(CoachBackground, CoachLicence):0}/100"
                                                : $"{-PointsLeft} points over budget: lower some ratings";

    public void CoachChanged()
    {
        // A smaller budget (changed background or licence) trims the highest ratings first.
        while (PointsLeft < 0 && CoachAttrs.MaxBy(r => r.Value) is { } top && top.Value > Hardwood.Core.Jobs.Coach.MinAttr) top.Value--;
        OnPropertyChanged(nameof(PointsLeft)); OnPropertyChanged(nameof(PointsText));
    }

    public Hardwood.Core.Jobs.Coach BuildCoach() => new()
    {
        FirstName = string.IsNullOrWhiteSpace(CoachFirst) ? "Alex" : CoachFirst.Trim(),
        LastName = string.IsNullOrWhiteSpace(CoachLast) ? "Morgan" : CoachLast.Trim(),
        Nationality = CoachNationality,
        BirthDate = new DateOnly(2026 - (int)(CoachAge ?? 42), 6, 1),
        Background = CoachBackground,
        Licence = CoachLicence,
        Attributes = CoachAttrs.ToDictionary(r => r.Attr, r => r.Value),
        Reputation = Hardwood.Core.Jobs.Coach.StartingReputation(CoachBackground, CoachLicence),
    };
    /// <summary>Leagues that can run the full match engine; the rest give quick results.</summary>
    public ObservableCollection<LeagueChoice> Leagues { get; } = new(
        Hardwood.Core.World.Competitions.Current.Where(c => c.Kind != Hardwood.Core.World.CompetitionKind.GuestPool)
            .Select(c => new LeagueChoice { Key = c.Key, Name = c.Name, FullSim = GameSession.DefaultFullSim.Contains(c.Key) }));
    private League? _preview;
    private Hardwood.Core.World.GameWorld? _previewWorld;

    /// <summary>Where you can start (stage 3): the NABL, any league abroad, or a college program (C2, #168).</summary>
    public List<string> PlayLeagues { get; } = new[] { "NABL" }.Concat(Hardwood.Core.World.Competitions.Current
        .Where(c => c.Kind == Hardwood.Core.World.CompetitionKind.Domestic).Select(c => c.Name)).Append(CollegeChoice).ToList();
    public const string CollegeChoice = "College basketball";
    [ObservableProperty] private string _playLeague = "NABL";
    partial void OnPlayLeagueChanged(string value) => FillTeams();
    public string ChooseTitle => PlayLeague == "NABL" ? "Choose your franchise" : PlayLeague == CollegeChoice ? "Choose your program · College basketball" : $"Choose your club · {PlayLeague}";

    public StartViewModel(MainViewModel shell)
    {
        foreach (var at in Enum.GetValues<Hardwood.Core.Jobs.CoachAttr>()) CoachAttrs.Add(new CoachAttrRow { Attr = at, Owner = this });
        _shell = shell;
        Graphics = new GraphicsSettingsViewModel(PreviewLeagues);
        Preview();
    }

    public static string DefaultSavePath()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Hardwood Manager");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "career.hwsave");
    }

    partial void OnSeedTextChanged(string value) { Preview(); Graphics?.UpdateLogoStatus(); }

    [RelayCommand]
    public void RandomSeed() => SeedText = Random.Shared.Next(1, 999_999).ToString();

    private void Preview()
    {
        if (!ulong.TryParse(SeedText, out var seed)) { Status = "Seed must be a number."; return; }
        Status = "";
        _preview = new WorldGenerator(new Rng(seed)).Generate("North American Basketball League", 2026, new DateOnly(2026, 10, 1));
        // The world is built from the same seed as the career's, so club ids match.
        _previewWorld = Hardwood.Core.World.GameWorld.Generate(_preview, seed, GameSession.DefaultFullSim, new DateOnly(2026, 10, 1));
        FillTeams();
    }

    private IEnumerable<(string league, IEnumerable<Team> teams)> PreviewLeagues()
    {
        if (_preview == null) yield break;
        yield return ("NABL", _preview.Teams);
        if (_previewWorld != null)
            foreach (var c in _previewWorld.Competitions.Where(c => c.Profile.Kind != Hardwood.Core.World.CompetitionKind.Continental))
                yield return (c.Profile.Name, c.Clubs);
    }

    private void FillTeams()
    {
        if (_preview == null) return;
        Teams.Clear();
        if (PlayLeague == CollegeChoice) { FillPrograms(); return; }
        bool nabl = PlayLeague == "NABL";
        var clubs = nabl ? _preview.Teams.OrderBy(t => t.Conference).ThenBy(t => t.Division).ThenBy(t => t.City).ToList()
            : _previewWorld?.Competitions.FirstOrDefault(c => c.Profile.Name == PlayLeague)?.Clubs.OrderBy(t => t.City).ToList() ?? new List<Team>();
        if (clubs.Count == 0) return;
        static double Core(Team t) => t.Roster.OrderByDescending(p => p.CurrentAbility).Take(8).Average(p => p.CurrentAbility);
        double leagueCore = clubs.Average(Core);
        foreach (var t in clubs)
        {
            var star = t.Roster.OrderByDescending(p => p.CurrentAbility).First();
            long pay = ContractMarket.Payroll(t);
            Teams.Add(new TeamChoice
            {
                Team = t, CoreCa = Core(t), LeagueCoreCa = leagueCore, League = nabl ? "" : PlayLeague,
                Payroll = pay >= 10_000_000 ? $"${pay / 1e6:F0}M" : $"${pay / 1e6:F1}M",
                Star = $"{star.Name} ({star.PrimaryPosition}, {star.CurrentAbility})",
            });
        }
        SelectedTeam = Teams.FirstOrDefault();
        OnPropertyChanged(nameof(ChooseTitle));
    }

    /// <summary>College programs, best first: their conference, a national outlook, and no payroll (the classic era, #161).</summary>
    private void FillPrograms()
    {
        if (_previewWorld?.College is not { } cw) return;
        static double Core(Team t) => t.Roster.OrderByDescending(p => p.CurrentAbility).Take(8).Select(p => (double)p.CurrentAbility).DefaultIfEmpty(0).Average();
        var ranked = cw.Programs.OrderByDescending(Core).ToList();
        double national = ranked.Average(Core);
        for (int i = 0; i < ranked.Count; i++)
        {
            var t = ranked[i];
            var conf = Hardwood.Core.College.CollegeWorld.Conferences.First(c => c.Key == cw.ConferenceOf[t.Id]);
            var star = t.Roster.OrderByDescending(p => p.CurrentAbility).First();
            Teams.Add(new TeamChoice
            {
                Team = t, CoreCa = Core(t), LeagueCoreCa = national, League = $"{conf.Short} · {(conf.Power ? "power" : "mid-major")}",
                Outlook = i < 8 ? "National contender" : i < 30 ? "Tournament team" : i < 60 ? "On the bubble" : "Rebuilding",
                Payroll = "No wages",
                Star = $"{star.FirstName[0]}. {star.LastName} ({star.PrimaryPosition}, {star.CurrentAbility})",
            });
        }
        SelectedTeam = Teams.FirstOrDefault();
        OnPropertyChanged(nameof(ChooseTitle));
    }

    [RelayCommand]
    public void NewGame()
    {
        if (_preview == null || SelectedTeam == null || !ulong.TryParse(SeedText, out var seed)) return;
        try
        {
            if (File.Exists(SavePath)) File.Delete(SavePath);
            Status = "";
            var session = GameSession.NewGame(seed, SelectedTeam.Team.Id, SavePath, fullSim: Leagues.Where(l => l.FullSim).Select(l => l.Key).ToList(), coach: BuildCoach());
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
