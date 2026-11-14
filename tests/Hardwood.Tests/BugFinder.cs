using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Logging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;

namespace Hardwood.Tests;

/// <summary>
/// Review #14's bug finder (Danny, #551): a bot that opens every page and tab, presses every button it finds,
/// and checks the game's invariants as a career plays. It records findings instead of failing, so one run lists
/// everything; <see cref="BugFinderTests"/> writes the report.
/// </summary>
internal sealed class BugFinder
{
    public sealed record Finding(string Kind, string Career, string Date, string Page, string What, string? Shot);

    private readonly MainViewModel _vm;
    private readonly MainWindow _window;
    private readonly string? _shots;
    public string Career { get; set; } = "";
    public List<Finding> Findings { get; } = new();
    private readonly HashSet<string> _seen = new();
    // Buttons are pressed once per career, page and phase; later visits only look.
    private readonly HashSet<string> _pressed = new();
    public int Pages, Tabs, Buttons, Days, Reloads;

    public BugFinder(MainViewModel vm, MainWindow window, string? shots)
    {
        _vm = vm; _window = window; _shots = shots;
        Dispatcher.UIThread.UnhandledException += (_, e) => { Add("crash", "(dispatcher)", e.Exception.GetType().Name + ": " + e.Exception.Message); e.Handled = true; };
    }

    private GameSession S => _vm.Session!;
    private string Date => _vm.Session is { } s ? s.Today.ToString("MMM d yyyy") : "-";

    public void Add(string kind, string page, string what, bool shot = false)
    {
        // One finding per kind, page and message across all careers (the first career and date to hit it are kept).
        // The "(when)" part is dropped from the key, so a page's problem is reported once, not once per visit.
        string key = $"{kind}|{page}|{Normalise(System.Text.RegularExpressions.Regex.Replace(what, @"\s*\([^()]*\)\s*$", ""))}";
        if (!_seen.Add(key)) return;
        string? file = null;
        if (shot && _shots != null)
        {
            Directory.CreateDirectory(_shots);
            file = $"bug-{Findings.Count + 1:000}-{Slug(Career)}-{Slug(page)}.png";
            try { Settle(); using var frame = _window.CaptureRenderedFrame(); frame?.Save(Path.Combine(_shots, file)); } catch { file = null; }
        }
        Findings.Add(new Finding(kind, Career, Date, page, what, file));
        if (_shots != null) File.AppendAllText(Path.Combine(Path.GetDirectoryName(_shots)!, "findings.txt"), $"{kind} | {Career} | {Date} | {page} | {what} | {file}\n");
    }

    private static string Normalise(string s) => System.Text.RegularExpressions.Regex.Replace(s, @"\d+", "#");
    private static string Slug(string s) => new string(s.ToLower().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');

    public void Settle()
    {
        for (int i = 0; i < 3; i++) { Dispatcher.UIThread.RunJobs(); _window.UpdateLayout(); }
    }

    // ------------------------------------------------------------------------------------------ the UI sweep

    /// <summary>Every visible sidebar page, each of its tabs, and every distinct button on it.</summary>
    public void SweepAll(string when, bool press = true)
    {
        foreach (var nav in _vm.Nav.Where(n => n.IsVisible).ToList())
            SweepPage(nav.Name, when, press && !_pressed.Contains($"{Career}|{nav.Name}|{_vm.Session?.Phase}"));
        foreach (var nav in _vm.Nav) _pressed.Add($"{Career}|{nav.Name}|{_vm.Session?.Phase}");
        // Linked pages: a player, another club and a box score, each with a way back.
        Linked(when);
        _vm.Navigate("Home");
        Settle();
    }

    private void Linked(string when)
    {
        if (_vm.Session == null) return;
        Try("Player page", () =>
        {
            _vm.Navigate("Roster");
            if (_vm.CurrentPage is not RosterViewModel roster) { Add("dead end", "Roster", "the sidebar's Roster didn't open the roster", true); return; }
            if (roster.Rows.Count == 0) { Add("dead end", "Roster", $"{when}: the roster is empty", true); return; }
            roster.Open(roster.Rows[0]);
            CheckWayBack("Player page", "Roster", when);
        });
        Try("Team page", () =>
        {
            _vm.Navigate("Standings");
            var other = S.MyTable.Select(r => r.Team).FirstOrDefault(t => t != S.HumanTeam) ?? S.League.Teams.First(t => t != S.HumanTeam);
            _vm.ShowTeam(other);
            CheckWayBack("Team page", "Standings", when);
        });
        Try("Box score", () =>
        {
            _vm.Navigate("Schedule & Results");
            if (_vm.CurrentPage is not ScheduleViewModel sched) { Add("dead end", "Schedule & Results", "the sidebar's link didn't open the schedule", true); return; }
            var played = sched.Games.FirstOrDefault(g => g.Played);
            if (played == null) return;
            sched.OpenBox(played);
            CheckWayBack("Box score", "Schedule & Results", when);
        });
    }

    /// <summary>A page opened from a link must offer a Back button that returns where you came from.</summary>
    private void CheckWayBack(string page, string from, string when)
    {
        Settle();
        Pages++;
        Scan(page, when);
        var opened = _vm.CurrentPage;
        var back = ContentButtons().FirstOrDefault(b => Label(b).StartsWith("Back", StringComparison.OrdinalIgnoreCase) || Label(b).StartsWith("←"));
        if (back == null) { Add("dead end", page, $"no Back button on the {page.ToLower()} (only the sidebar leads away)", true); return; }
        Press(back);
        Settle();
        if (_vm.CurrentPage == opened) Add("dead end", page, $"Back on the {page.ToLower()} didn't leave the page", true);
        else if (_vm.CurrentPage is PageViewModel p && p.Title is { } t && _vm.Nav.FirstOrDefault(n => n.IsActive)?.Name is { } active && active != from)
            Add("dead end", page, $"Back on the {page.ToLower()} went to {active}, not {from}");
    }

    public void SweepPage(string name, string when, bool press = true)
    {
        Try(name, () =>
        {
            _vm.Navigate(name);
            Settle();
            Pages++;
            Scan(name, when);
            var tabs = Tabs_();
            if (tabs == null || tabs.ItemCount <= 1) { if (press) PressAll(name, null, when); return; }
            for (int i = 0; i < tabs.ItemCount; i++)
            {
                _vm.Navigate(name); Settle();
                tabs = Tabs_();
                if (tabs == null || i >= tabs.ItemCount) break;
                if (tabs.ContainerFromIndex(i) is TabItem { IsVisible: false }) continue;
                tabs.SelectedIndex = i;
                Settle();
                Tabs++;
                string tabName = $"{name} → {TabHeader(tabs, i)}";
                Scan(tabName, when);
                if (press) PressAll(name, i, when);
            }
        });
    }

    private TabControl? Tabs_() => Content()?.GetVisualDescendants().OfType<TabControl>().FirstOrDefault(t => t.IsEffectivelyVisible);

    private static string TabHeader(TabControl tabs, int i) =>
        tabs.ContainerFromIndex(i) is TabItem ti ? (ti.Header as string ?? Text(ti) ?? $"tab {i}") : $"tab {i}";

    // The page's view: the outermost UserControl bound to the current page.
    private Control? Content() => _window.GetVisualDescendants().OfType<UserControl>()
        .FirstOrDefault(c => c.IsEffectivelyVisible && ReferenceEquals(c.DataContext, _vm.CurrentPage));

    /// <summary>Buttons on the page (and the assistant panel), not the sidebar or the top bar's time controls.</summary>
    private List<Button> ContentButtons()
    {
        var shell = new HashSet<object?> { _vm.ContinueCommand, _vm.SimWeekCommand, _vm.SimToNextGameCommand, _vm.BackToStartCommand, _vm.GoBoardCommand };
        foreach (var n in _vm.Nav) shell.Add(n.Command);
        return _window.GetVisualDescendants().OfType<Button>()
            .Where(b => b.IsEffectivelyVisible && b.IsEffectivelyEnabled && !shell.Contains(b.Command) && b is not ToggleButton { Parent: ComboBox })
            .Where(b => b.FindAncestorOfType<ComboBox>() == null && b.FindAncestorOfType<ScrollBar>() == null && b.FindAncestorOfType<CalendarDatePicker>() == null
                        && b.FindAncestorOfType<ButtonSpinner>() == null && b is not RepeatButton)
            .ToList();
    }

    // Buttons the bot leaves alone: they end the career, open the file system or leave the app.
    private static readonly string[] Skip = { "Back to start", "Browse", "Choose folder", "Pick", "Open folder", "Quit", "Retire", "Resign", "Leave the club", "Load", "Export", "Reset", "Main menu" };

    private void PressAll(string page, int? tab, string when)
    {
        var labels = new List<string>();
        foreach (var b in ContentButtons())
        {
            var l = Label(b);
            if (Skip.Any(s => l.StartsWith(s, StringComparison.OrdinalIgnoreCase))) continue;
            if (!labels.Contains(l)) labels.Add(l);
        }
        foreach (var label in labels.Take(30))
        {
            if (_vm.Session == null) return;
            _vm.Navigate(page); Settle();
            if (tab is int t && Tabs_() is { } tc && t < tc.ItemCount) { tc.SelectedIndex = t; Settle(); }
            var b = ContentButtons().FirstOrDefault(x => Label(x) == label);
            if (b == null) continue;
            string where = tab is int ti && Tabs_() is { } tcc ? $"{page} → {TabHeader(tcc, ti)}" : page;
            var before = Snapshot();
            var pageBefore = _vm.CurrentPage;
            var ex = Press(b);
            Settle();
            Buttons++;
            if (ex != null) { Add("crash", where, $"pressing \"{label}\" ({when}): {ex.GetType().Name}: {ex.Message} {Frame(ex)}", true); continue; }
            if (_vm.CurrentPage != pageBefore || _vm.Session == null) continue;
            if (Snapshot() == before) Add("no feedback", where, $"\"{label}\" changes nothing on screen ({when})", true);
            Scan(where, when);
        }
    }

    private static string Frame(Exception ex)
    {
        var line = ex.StackTrace?.Split('\n').FirstOrDefault(l => l.Contains("Hardwood."))?.Trim();
        return line == null ? "" : $"[{line}]";
    }

    private static readonly MethodInfo OnClick = typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static Exception? Press(Button b)
    {
        try { OnClick.Invoke(b, null); Dispatcher.UIThread.RunJobs(); return null; }
        catch (TargetInvocationException e) { return e.InnerException ?? e; }
        catch (Exception e) { return e; }
    }

    public static string Label(Button b)
    {
        if (b.Content is string s && s.Length > 0) return s.Trim();
        var t = Text(b);
        if (!string.IsNullOrWhiteSpace(t)) return t!;
        if (ToolTip.GetTip(b) is string tip) return $"[{tip}]";
        return $"[{b.Name ?? b.Classes.FirstOrDefault() ?? "icon button"}]";
    }

    private static string? Text(Visual v)
    {
        var parts = v.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(t.Text)).Select(t => t.Text!.Trim()).Take(3);
        var s = string.Join(" ", parts);
        return s.Length == 0 ? null : s.Length > 60 ? s[..60] : s;
    }

    /// <summary>What's on screen: the page, every visible text, toggle and selection.</summary>
    private string Snapshot()
    {
        var sb = new StringBuilder();
        sb.Append(_vm.CurrentPage?.GetHashCode()).Append('|');
        foreach (var v in _window.GetVisualDescendants())
        {
            if (v is Visual vis && !vis.IsEffectivelyVisible) continue;
            switch (v)
            {
                case TextBlock tb: sb.Append(tb.Text).Append('\u0001'); break;
                case TextBox tx: sb.Append(tx.Text).Append('\u0002'); break;
                case ToggleButton tg: sb.Append(tg.IsChecked).Append('\u0003'); break;
                case SelectingItemsControl sel: sb.Append(sel.SelectedIndex).Append('\u0004'); break;
                case ItemsControl ic: sb.Append(ic.ItemCount).Append('\u0005'); break;
            }
        }
        sb.Append(_vm.Session?.Inbox.Count);
        return sb.ToString();
    }

    /// <summary>Checks on what the page shows: empty lists with no word of explanation, and placeholder text.</summary>
    private void Scan(string page, string when)
    {
        var root = Content();
        if (root == null) { Add("dead end", page, "the page shows nothing (no content)", true); return; }
        var texts = root.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(t.Text)).ToList();
        if (texts.Count < 2) Add("dead end", page, $"the page is almost blank ({when}): {string.Join(" / ", texts.Select(t => t.Text))}", true);
        foreach (var t in texts)
        {
            var x = t.Text!;
            if (x.Contains("NaN") || x.Contains("Infinity") || x.Contains("{0") || x.Contains("System.") || x.Contains("Hardwood.") || x.Contains("null") && !x.Contains("annul"))
                Add("wrong text", page, $"\"{Cut(x)}\" ({when})", true);
            if (System.Text.RegularExpressions.Regex.IsMatch(x, @"\$-|--\d|\b-\d+ games\b|01/01/0001|Jan 1 0001|\(\)"))
                Add("wrong text", page, $"\"{Cut(x)}\" ({when})", true);
        }
        // An empty list or grid with nothing near it saying why.
        foreach (var ic in root.GetVisualDescendants().OfType<ItemsControl>().Where(i => i.IsEffectivelyVisible && i.ItemCount == 0 && i is not ComboBox && i is not TabControl ))
        {
            if (ic.GetType().Name is "MenuItem" or "TabStrip") continue;
            var parent = ic.GetVisualParent();
            for (int up = 0; up < 2 && parent?.GetVisualParent() is { } pp; up++) parent = pp;
            var near = parent?.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? "").ToList() ?? new();
            bool explained = near.Any(n => System.Text.RegularExpressions.Regex.IsMatch(n, @"\b(No|None|Nothing|nobody|yet|will|when|after|once|until|empty|appear)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
            if (!explained && (ic is ListBox || ic.GetType().Name == "DataGrid"))
                Add("empty", page, $"an empty {ic.GetType().Name} with no explanation near it ({Heading(ic)}; {when})", true);
        }
    }

    private static string Heading(Control c)
    {
        // The nearest text above the list: its heading, more or less.
        var parent = c.GetVisualParent();
        for (int up = 0; up < 3 && parent != null; up++)
        {
            var t = parent.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(x => x.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(x.Text));
            if (t != null) return "near \"" + Cut(t.Text!) + "\"";
            parent = parent.GetVisualParent();
        }
        return "no heading";
    }

    private static string Cut(string s) => s.Length > 90 ? s[..90] + "…" : s;

    private void Try(string page, Action a)
    {
        try { a(); }
        catch (Exception ex) { Add("crash", page, $"{ex.GetType().Name}: {ex.Message} {Frame(ex)}", true); }
    }

    // ------------------------------------------------------------------------------------------ invariants

    /// <summary>Checks that should hold every day of every career.</summary>
    public void Invariants()
    {
        if (_vm.Session is not { } s) return;
        Days++;
        string page = "(game state)";
        try
        {
            // No player on two rosters.
            var teams = s.League.Teams.Concat(s.World.Clubs).Concat(s.World.College?.Programs ?? new List<Team>()).Distinct().ToList();
            var where = new Dictionary<int, Team>();
            foreach (var t in teams)
            foreach (var p in t.Roster)
                if (where.TryGetValue(p.Id, out var other) && other != t) Add("state", page, $"{p.Name} (id {p.Id}) is on two rosters: {other.FullName} and {t.FullName}");
                else where[p.Id] = t;
            foreach (var p in s.League.FreeAgents)
                if (where.TryGetValue(p.Id, out var t)) Add("state", page, $"{p.Name} is a free agent and on the {t.FullName} roster");
            foreach (var t in teams)
                if (t.Roster.Distinct().Count() != t.Roster.Count) Add("state", page, $"{t.FullName}'s roster lists a player twice");

            // Roster limits in the NBA once the season is on.
            if (s.Phase is SessionPhase.RegularSeason)
                foreach (var t in s.League.Teams)
                    if (t.Roster.Count > s.League.Finance.RosterMax || t.Roster.Count < s.League.Finance.RosterMin)
                        Add("state", page, $"{t.FullName} carry {t.Roster.Count} players in the regular season (limits {s.League.Finance.RosterMin}-{s.League.Finance.RosterMax})");
            if (s.Employed && s.Phase is SessionPhase.RegularSeason && s.HumanTeam.Roster.Count < 8)
                Add("state", page, $"your club has only {s.HumanTeam.Roster.Count} players in the regular season");

            // Records: the header, the standings and the schedule agree.
            if (s.Employed)
            {
                var rec = s.MyRecord;
                string header = rec.Games > 0 ? $"{rec.Wins}-{rec.Losses}" : "0-0";
                _vm.RefreshHeader();
                if (_vm.RecordText != header) Add("mismatch", "Top bar", $"the top bar says {_vm.RecordText}, the record is {header}");
                var games = s.MyGames().Where(g => g.Played).ToList();
                if (!s.InCollege)
                {
                    var regular = games.Where(g => g.Group == ScheduleFilter.Regular).ToList();
                    int w = regular.Count(g => g.Won), l = regular.Count(g => !g.Won);
                    if ((w, l) != (rec.Wins, rec.Losses) && regular.Count > 0)
                        Add("mismatch", "Schedule & Results", $"the schedule's regular-season results are {w}-{l}, the standings say {rec.Wins}-{rec.Losses}");
                }
                else
                {
                    int w = games.Count(g => g.Won), l = games.Count(g => !g.Won);
                    if ((w, l) != (rec.Wins, rec.Losses)) Add("mismatch", "Schedule & Results", $"the college schedule's results are {w}-{l}, the record says {rec.Wins}-{rec.Losses}");
                }
            }
            // Every standings table balances: as many wins as losses.
            var tables = new List<(string, Standings)> { (s.League.Name, s.Standings) };
            foreach (var c in s.World.Competitions.Where(c => c.Profile.Kind == Hardwood.Core.World.CompetitionKind.Domestic && c.Plays)) tables.Add((c.Profile.Name, s.TableOf(c)));
            foreach (var (n, st) in tables)
            {
                var all = st.All();
                if (all.Sum(r => r.Wins) != all.Sum(r => r.Losses)) Add("state", "Standings", $"{n}'s table has {all.Sum(r => r.Wins)} wins and {all.Sum(r => r.Losses)} losses");
            }
            // Players: ability inside its range, a sane age, no injury from before the season began.
            foreach (var p in s.HumanTeam.Roster)
            {
                if (p.CurrentAbility < 1 || p.CurrentAbility > 200 || p.Age(s.Today) < 14 || p.Age(s.Today) > 50) Add("state", page, $"{p.Name}: ability {p.CurrentAbility}, age {p.Age(s.Today)}");
                // (An injury stays on the player after his return date until the summer, by design: the pages check IsOut.)
            }
            // Money: no wild numbers.
            foreach (var t in teams.Where(t => t.Money != null))
                if (Math.Abs(t.Money.Balance) > 100_000_000_000L) { Add("state", "Board", $"{t.FullName}'s balance is ${t.Money.Balance / 1e9:F0}B"); break; }
        }
        catch (Exception ex) { Add("crash", page, $"checking the game state: {ex.GetType().Name}: {ex.Message} {Frame(ex)}"); }
    }

    /// <summary>Save, load the save, and compare what matters. The loaded session then carries on (as the player's would).</summary>
    public void SaveAndReload(string when)
    {
        if (_vm.Session is not { } s || s.Save?.Path is not { } path) return;
        Reloads++;
        try
        {
            s.SaveNow();
            string Fingerprint(GameSession x) => string.Join("|",
                x.Today, x.Phase, x.Employed, x.HumanTeam.Id, x.MyRecord.Wins, x.MyRecord.Losses, x.HumanTeam.Money.Balance,
                string.Join(",", x.HumanTeam.Roster.Select(p => p.Id).OrderBy(i => i)),
                x.Inbox.Count, x.League.Teams.Sum(t => x.Standings.Has(t) ? x.Standings[t].Wins : 0),
                x.MyGames().Count, x.MyGames().Count(g => g.Played),
                string.Join(",", x.HumanTeam.Roster.OrderBy(p => p.Id).Select(p => p.Contract?.Salary ?? 0)),
                x.HumanTeam.Tactic.System, x.HumanTeam.DepthChart?.Starters.Count ?? 0);
            var a = Fingerprint(s);
            var loaded = GameSession.Load(path);
            var b = Fingerprint(loaded);
            if (a != b)
            {
                var pa = a.Split('|'); var pb = b.Split('|');
                string[] names = { "date", "phase", "employed", "club", "wins", "losses", "balance", "roster", "inbox", "league wins", "games", "games played", "salaries", "system", "starters" };
                var diffs = Enumerable.Range(0, Math.Min(pa.Length, pb.Length)).Where(i => pa[i] != pb[i]).Select(i => $"{names[i]} {Cut(pa[i])} → {Cut(pb[i])}");
                Add("save", "(save and load)", $"{when}: a save and reload changes {string.Join("; ", diffs)}");
            }
            _vm.AttachSession(loaded);
            Settle();
        }
        catch (Exception ex) { Add("crash", "(save and load)", $"{when}: {ex.GetType().Name}: {ex.Message} {Frame(ex)}"); }
    }

    // ------------------------------------------------------------------------------------------ binding errors

    /// <summary>Avalonia's binding errors (a page binding to something that isn't there) as findings.</summary>
    public sealed class BindingSink : ILogSink
    {
        public readonly List<string> Errors = new();
        public bool IsEnabled(LogEventLevel level, string area) => level >= LogEventLevel.Warning && area is LogArea.Binding or LogArea.Property;
        public void Log(LogEventLevel level, string area, object? source, string messageTemplate) => Log(level, area, source, messageTemplate, Array.Empty<object?>());
        public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
        {
            string msg = messageTemplate;
            try
            {
                int i = 0;
                msg = System.Text.RegularExpressions.Regex.Replace(messageTemplate, @"\{[^}]+\}", m => i < propertyValues.Length ? propertyValues[i++]?.ToString() ?? "null" : m.Value);
            }
            catch { }
            lock (Errors) if (Errors.Count < 2000) Errors.Add($"{area}: {msg} (on {source?.GetType().Name})");
        }
    }
}
