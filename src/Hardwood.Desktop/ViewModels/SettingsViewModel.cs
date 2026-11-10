namespace Hardwood.Desktop.ViewModels;

/// <summary>In-game Settings page: the assistant, face and logo packs.</summary>
public sealed class SettingsViewModel : PageViewModel
{
    public override string Title => "Settings";
    public GraphicsSettingsViewModel Graphics { get; }
    /// <summary>The assistant's switch and level (decisions #115-118).</summary>
    public GuideViewModel Guide => Shell.Guide;

    public SettingsViewModel(MainViewModel shell) : base(shell) =>
        Graphics = new GraphicsSettingsViewModel(() => Leagues(shell), () => shell.RequestPortraits());

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

    /// <summary>The NABL and every competition in the world, for logo matching and the team list.</summary>
    public static IEnumerable<(string league, IEnumerable<Core.Model.Team> teams)> Leagues(MainViewModel shell)
    {
        if (shell.Session is not { } s) yield break;
        yield return ("NABL", s.League.Teams);
        foreach (var c in s.World.Competitions.Where(c => c.Profile.Kind != Core.World.CompetitionKind.Continental))
            yield return (c.Profile.Name, c.Clubs);
    }

    public override void Refresh()
    {
        Graphics.UpdateFaceStatus();
        Graphics.UpdateLogoStatus();
    }
}
