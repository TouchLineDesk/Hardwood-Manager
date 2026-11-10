namespace Hardwood.Desktop.ViewModels;

/// <summary>In-game Settings page: face and logo packs.</summary>
public sealed class SettingsViewModel : PageViewModel
{
    public override string Title => "Settings";
    public GraphicsSettingsViewModel Graphics { get; }

    public SettingsViewModel(MainViewModel shell) : base(shell) =>
        Graphics = new GraphicsSettingsViewModel(() => Leagues(shell), () => shell.RequestPortraits());

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
