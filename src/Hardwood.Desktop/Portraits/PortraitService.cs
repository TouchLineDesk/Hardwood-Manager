using System.Text.Json;
using Hardwood.Core.Model;
using Hardwood.Core.Portraits;
using Hardwood.Core.Season;
using Hardwood.Desktop.Controls;

namespace Hardwood.Desktop.Portraits;

/// <summary>
/// Keeps every player's card portrait up to date while you play, with no manual steps:
/// <list type="bullet">
/// <item>gives each new player a face from the FMNEWGAN pack (by ethnicity; once, never re-picked, never shared),
/// stored in the save with the player;</item>
/// <item>redraws a card only when it would look different: new player, body or badge tier, POT number, team (crest),
/// draft stamp, or a new card design;</item>
/// <item>draws on a background thread and writes <c>&lt;save&gt;-portraits\&lt;id&gt;.png</c>, so the game never waits.</item>
/// </list>
/// Call <see cref="Request"/> on the UI thread whenever the game state may have changed; it is cheap when nothing did.
/// </summary>
public sealed class PortraitService
{
    public static PortraitService Instance { get; } = new();

    public const string StateFile = "portraits.json";

    private FaceLibrary? _library;
    private string? _libraryRoot;
    private bool _running, _again;

    public string Status { get; private set; } = "";
    public event Action? StatusChanged;

    /// <summary>The last run (tests can await it).</summary>
    public Task Current { get; private set; } = Task.CompletedTask;

    /// <summary>Forget the scanned face pack, e.g. after the folder setting changes.</summary>
    public void ResetLibrary() { _library = null; _libraryRoot = null; }

    public void Request(GameSession? session)
    {
        if (session?.Save?.Path is not { } savePath) { SetStatus(""); return; }
        var settings = AppSettings.Current;
        if (!settings.AutoPortraits) { SetStatus(""); return; }
        if (!FaceLibrary.LooksLikePack(settings.FaceFolder)) { SetStatus("Portraits: face pack not found"); return; }
        if (_running) { _again = true; return; }
        _running = true;
        Current = Loop(session, savePath, settings.FaceFolder);
    }

    private async Task Loop(GameSession session, string savePath, string root)
    {
        try
        {
            do
            {
                _again = false;
                await RunOnce(session, savePath, root);
            } while (_again);
        }
        catch (Exception ex)
        {
            SetStatus("Portraits: " + ex.Message);
        }
        finally { _running = false; }
    }

    private async Task RunOnce(GameSession session, string savePath, string root)
    {
        if (_library == null || _libraryRoot != root)
        {
            SetStatus("Portraits: scanning faces…");
            _library = await Task.Run(() => FaceLibrary.Scan(root));
            _libraryRoot = root;
        }
        var lib = _library;

        // UI thread: assign faces and take a snapshot of what each card shows. The game saves the faces with the
        // players on its next save (every day and every offseason step).
        var league = session.League;
        var prospects = session.Phase == SessionPhase.Offseason ? session.Offseason?.Prospects : null;
        var world = session.World;
        var college = world.College?.Programs ?? new List<Core.Model.Team>();
        var shown = league.Teams.SelectMany(t => t.Roster).Concat(league.FreeAgents).Concat(prospects ?? new List<Player>()).Concat(world.Players)
            .Concat(college.SelectMany(t => t.Roster)).ToList();
        lib.AssignMissing(shown.Concat(league.Retired), shown);
        var cards = PortraitCard.For(league, prospects, world.Clubs.Concat(college), Graphics.LogoPackService.FileFor);
        // Your own squad first, then your league, then everyone else: a new world has ~13,000 cards to draw, and at a
        // club abroad your players used to wait behind the whole NABL and every league before yours.
        var mine = session.HumanTeam.Roster.Select(p => p.Id).ToHashSet();
        var myLeague = (session.InNabl ? league.Teams : world.LeagueOf(session.HumanTeam)?.Clubs ?? new List<Core.Model.Team>())
            .SelectMany(t => t.Roster).Select(p => p.Id).ToHashSet();
        cards = cards.OrderBy(c => mine.Contains(c.PlayerId) ? 0 : myLeague.Contains(c.PlayerId) ? 1 : 2).ToList();
        var folder = Controls.Portraits.FolderFor(savePath);

        var progress = new Progress<(int done, int total, int[] ids)>(p =>
        {
            Controls.Portraits.Invalidate(p.ids);
            if (p.total > 0) SetStatus(p.done < p.total ? $"Portraits: drawing {p.done}/{p.total}" : "");
        });
        var (drawn, failed) = await Task.Run(() => RenderChanged(cards, folder, lib, progress));
        SetStatus(failed > 0 ? $"Portraits: {failed} face file{(failed == 1 ? "" : "s")} missing" : "");
    }

    /// <summary>
    /// Render every card whose look changed since the last run (or whose PNG is missing). Runs off the UI thread.
    /// Returns the number drawn and the number that failed (face file missing or unreadable).
    /// </summary>
    public static (int drawn, int failed) RenderChanged(IReadOnlyList<PortraitCard> cards, string folder, FaceLibrary lib,
                                                        IProgress<(int done, int total, int[] ids)>? progress = null)
    {
        Directory.CreateDirectory(folder);
        var statePath = Path.Combine(folder, StateFile);
        var state = LoadState(statePath);
        var todo = cards.Where(c => !state.TryGetValue(c.PlayerId.ToString(), out var k) || k != c.Key(CardRenderer.Version)
                                    || !File.Exists(Path.Combine(folder, c.PlayerId + ".png"))).ToList();
        if (todo.Count == 0) return (0, 0);

        int done = 0, failed = 0;
        var batch = new List<int>();
        foreach (var card in todo)
        {
            try
            {
                var face = lib.FullPath(card.FaceFile);
                if (!File.Exists(face)) { failed++; }
                else
                {
                    CardRenderer.RenderToFile(card, face, Path.Combine(folder, card.PlayerId + ".png"));
                    state[card.PlayerId.ToString()] = card.Key(CardRenderer.Version);
                    batch.Add(card.PlayerId);
                }
            }
            catch (Exception) { failed++; }
            done++;
            if (batch.Count >= 20 || done == todo.Count)
            {
                SaveState(statePath, state);
                progress?.Report((done, todo.Count, batch.ToArray()));
                batch.Clear();
            }
        }
        return (done - failed, failed);
    }

    private static Dictionary<string, string> LoadState(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new();
        }
        catch (Exception) { /* a broken state file just means everything is redrawn */ }
        return new();
    }

    private static void SaveState(string path, Dictionary<string, string> state)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(state));
        File.Move(tmp, path, overwrite: true);
    }

    private void SetStatus(string s)
    {
        if (s == Status) return;
        Status = s;
        StatusChanged?.Invoke();
    }
}
