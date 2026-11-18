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
    /// <summary>Where extra detail goes (the progress log), when the test sets it.</summary>
    public Action<string>? Log { get; set; }
    /// <summary>Sweeps whose label starts with one of these also save a screenshot of every page and tab (HW_BUGFINDER_TOUR),
    /// so the hunter can judge the screens, not only the findings. Empty: no tour.</summary>
    public string[] TourAt { get; set; } = Array.Empty<string>();
    public int TourShots;

    public BugFinder(MainViewModel vm, MainWindow window, string? shots)
    {
        _vm = vm; _window = window; _shots = shots;
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            // Name the page on screen and the layout warnings just before (an "Infinite layout loop" says nothing of where, run 54).
            var cycle = BindingSink.Current?.LayoutWarnings.TakeLast(3) ?? Enumerable.Empty<string>();
            Add("crash", "(dispatcher) on " + ((_vm.CurrentPage as PageViewModel)?.Title ?? _vm.CurrentPage?.GetType().Name ?? "?"), e.Exception.GetType().Name + ": " + e.Exception.Message
                + (cycle.Any() ? " [" + string.Join("; ", cycle) + "]" : ""));
            e.Handled = true;
        };
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
        // Bug-hunt Nov 18: Wembanyama wasn't dressed for the contender's live game and finished a season at 5.6 pts in the
        // finder, while a headless season gave him 35 min. The cause was the sweep itself: Rotation's "−" presses took his
        // target 36 → 20 → 0 over two sweeps. Log the best player's minutes target, name the page whose presses changed it,
        // and put the rotation back after the Rotation page's presses so the season and the live game are played with a
        // real rotation (the presses are still made and checked for feedback).
        var star = BestPlayer();
        string Mins() => star == null || _vm.Session == null ? "" : StarLine(star);
        string before = Mins();
        if (star != null) Log?.Invoke($"{when}: best player {star.Name}: {before}");
        foreach (var nav in _vm.Nav.Where(n => n.IsVisible).ToList())
        {
            var team = _vm.Session is { Employed: true } s0 ? s0.HumanTeam : null;
            var keep = nav.Name == "Rotation" && team != null ? CopyOf(team.DepthChart) : null;
            SweepPage(nav.Name, when, press && !_pressed.Contains($"{Career}|{nav.Name}|{_vm.Session?.Phase}"));
            if (nav.Name == "Rotation" && team != null && _vm.Session?.HumanTeam == team)
            {
                var pressed = Mins();
                team.DepthChart = keep;
                if (star != null && pressed != Mins()) Log?.Invoke($"{when}: Rotation's presses took {star.Name} to {pressed}; the rotation is put back ({Mins()})");
            }
            var after = Mins();
            if (star != null && after != before)
            {
                Log?.Invoke($"{when}: {nav.Name}'s presses changed {star.Name}: {before} → {after}");
                before = after;
            }
        }
        foreach (var nav in _vm.Nav) _pressed.Add($"{Career}|{nav.Name}|{_vm.Session?.Phase}");
        // Linked pages: a player, another club and a box score, each with a way back.
        Linked(when);
        _vm.Navigate("Home");
        Settle();
    }

    private static DepthChart? CopyOf(DepthChart? dc) =>
        dc == null ? null : new DepthChart { Starters = new List<int>(dc.Starters), MinutesTargets = new Dictionary<int, double>(dc.MinutesTargets) };

    /// <summary>The human club's best player by current ability (null out of work or with an empty roster).</summary>
    private Player? BestPlayer() =>
        _vm.Session is { Employed: true } s ? s.HumanTeam.Roster.OrderByDescending(p => p.CurrentAbility).FirstOrDefault() : null;

    /// <summary>"32 min target, starter" for a player on the human club's depth chart ("no depth chart" when it's automatic).</summary>
    private string StarLine(Player p)
    {
        var team = S.HumanTeam;
        if (!team.Roster.Contains(p)) return "left the club";
        var dc = team.DepthChart;
        if (dc == null) return "no depth chart (automatic)";
        double m = dc.MinutesTargets.TryGetValue(p.Id, out var v) ? v : 0;
        return $"{m:F0} min target, {(dc.Starters.Contains(p.Id) ? "starter" : "bench")}";
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
            // A played game whose row has no box score is a lost-data finding, not a missing Back (run 55: bug #18 was #19).
            // H8 (run 65): a game from before you took the job keeps only its score, as the row says; any other is lost data.
            if (sched.Games.FirstOrDefault(g => g.Played && !g.HasBox && !g.NotCoached) is { } lost && sched.Games.Any(g => g.HasBox))
                Add("lost data", "Schedule & Results", $"the played game on {lost.Game.Date:MMM d} has a score but no box score, though other games have one ({S.HumanTeam.FullName})");
            var played = sched.Games.FirstOrDefault(g => g.Played && g.HasBox);
            if (played == null)
            {
                if (sched.Games.FirstOrDefault(g => g.Played && !g.NotCoached) is { } bare)
                    Add("lost data", "Schedule & Results", $"the played game on {bare.Game.Date:MMM d} has a score but no box score ({S.HumanTeam.FullName}, {S.HumanLeagueName}; {(S.Employed ? "employed" : "out of work")}; stored boxes: {S.AbroadResults.Count} below the NBA, {S.CollegeResults.Count} college)");
                return;
            }
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
            if (tabs == null || tabs.ItemCount <= 1) { Tour(name, when); if (press) PressAll(name, null, when); return; }
            for (int i = 0; i < tabs.ItemCount; i++)
            {
                _vm.Navigate(name); Settle();
                tabs = Tabs_();
                if (tabs == null || i >= tabs.ItemCount) break;
                if (tabs.ContainerFromIndex(i) is TabItem { IsVisible: false }) continue;
                // Bug-hunt Nov 17: on College the tab picked straight after Navigate didn't always stay picked (the tour's
                // "Conferences" shot showed Top 25 and its "March Madness" shot showed Conferences), so the Final Four page
                // was never seen. Pick it again until it holds; a tab that won't hold is a finding.
                for (int tries = 0; tries < 4 && tabs.SelectedIndex != i; tries++) { tabs.SelectedIndex = i; Settle(); }
                if (tabs.SelectedIndex != i)
                {
                    Add("dead end", $"{name} → {TabHeader(tabs, i)}", $"the tab didn't stay selected (shows {TabHeader(tabs, tabs.SelectedIndex)})", true);
                    continue;
                }
                Settle();
                Tabs++;
                string tabName = $"{name} → {TabHeader(tabs, i)}";
                Scan(tabName, when);
                Tour(tabName, when);
                if (press) PressAll(name, i, when);
            }
        });
    }

    /// <summary>A screenshot of the page as it stands, when this sweep is on the tour (before any button is pressed).</summary>
    public void Tour(string page, string when)
    {
        if (_shots == null || !TourAt.Any(t => when.StartsWith(t, StringComparison.OrdinalIgnoreCase))) return;
        try
        {
            var dir = Path.Combine(_shots, "tour");
            Directory.CreateDirectory(dir);
            Settle();
            // The first capture renders the frame that's pending; the second is the page as it now stands.
            _window.CaptureRenderedFrame()?.Dispose();
            Settle();
            using var frame = _window.CaptureRenderedFrame();
            frame?.Save(Path.Combine(dir, $"{Slug(Career)}-{_vm.Session?.Today.Year}-{Slug(when)}-{Slug(page)}.png"));
            TourShots++;
        }
        catch { /* a tour shot is a convenience; never a finding */ }
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

    /// <summary>A button that cuts a player from your roster (Waive, Release, Release selected): the sweep leaves these alone.</summary>
    public static bool IsCut(string label) =>
        label.StartsWith("Waive", StringComparison.OrdinalIgnoreCase) || label.StartsWith("Release", StringComparison.OrdinalIgnoreCase);

    private void PressAll(string page, int? tab, string when)
    {
        var labels = new List<string>();
        foreach (var b in ContentButtons())
        {
            var l = Label(b);
            if (Skip.Any(s => l.StartsWith(s, StringComparison.OrdinalIgnoreCase))) continue;
            // Bug-hunt Nov 15: the Board's Accept takes another club's job offer, which turned the "gleague" career into a
            // college one after its first season (and made H8 look like lost data). The fired career takes its job itself.
            if (page == "Board" && l.StartsWith("Accept", StringComparison.OrdinalIgnoreCase)) continue;
            // Bug-hunt Nov 16: Waive (Transactions) and Release / Release selected (Offseason) cut a player for good. The sweep
            // waived Wembanyama from the nba-contender career on its first day and thinned the bottom career's roster to 8,
            // so later seasons no longer played the career they were meant to.
            if (IsCut(l)) continue;
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
            var twiceBefore = InTwoPlaces();
            var pageBefore = _vm.CurrentPage;
            var clubBefore = _vm.Session.HumanTeam;
            var ex = Press(b);
            Settle();
            Buttons++;
            // A press that changes your club is logged: the sweep moved the career and later findings are about the new club.
            if (_vm.Session is { } after && after.HumanTeam != clubBefore)
                Log?.Invoke($"{where}: \"{label}\" ({when}) moved the career from {clubBefore.FullName} to {after.HumanTeam.FullName} ({after.HumanLeagueName})");
            // Bug hunt Nov 16 (H28): a press that leaves a player in two places (a free agent and on a roster, or on two rosters)
            // breaks every later save (UNIQUE constraint failed: player.id); name the press that did it, not just the next save.
            foreach (var w in InTwoPlaces().Except(twiceBefore))
                Add("state", where, $"pressing \"{label}\" ({when}) left {w}", true);
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
    public void Scan(string page, string when)
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

    /// <summary>Players in two places right now: on two rosters, or a free agent and on a roster (one line each).</summary>
    public HashSet<string> InTwoPlaces()
    {
        var found = new HashSet<string>();
        if (_vm.Session is not { } s) return found;
        try
        {
            var where = new Dictionary<int, (Team Team, Player Player)>();
            foreach (var t in s.League.Teams.Concat(s.World.Clubs).Concat(s.World.College?.Programs ?? new List<Team>()).Distinct())
            foreach (var p in t.Roster)
                if (where.TryGetValue(p.Id, out var other) && other.Team != t) found.Add($"{p.Name} (id {p.Id}) on two rosters: {other.Team.FullName} and {t.FullName}{Copies(p, other.Player)}");
                else where[p.Id] = (t, p);
            foreach (var p in s.League.FreeAgents)
                if (where.TryGetValue(p.Id, out var t)) found.Add($"{p.Name} (id {p.Id}) a free agent and on the {t.Team.FullName} roster{Copies(p, t.Player)}");
            foreach (var g in s.League.FreeAgents.GroupBy(p => p.Id).Where(g => g.Count() > 1))
                found.Add($"{g.First().Name} (id {g.Key}) listed {g.Count()} times in the free-agent pool");
        }
        catch { /* the full invariants report a broken world */ }
        return found;
    }

    // Whether the two places hold one Player object or two copies of him (a copy points at a stale object kept from earlier).
    private static string Copies(Player a, Player b) => ReferenceEquals(a, b) ? " (the same object in both)" : " (two different copies of him)";

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
            foreach (var g in s.League.FreeAgents.GroupBy(p => p.Id).Where(g => g.Count() > 1))
                Add("state", page, $"{g.First().Name} is listed {g.Count()} times in the free-agent pool");
            foreach (var t in teams)
                if (t.Roster.Distinct().Count() != t.Roster.Count) Add("state", page, $"{t.FullName}'s roster lists a player twice");

            // Roster limits in the NBA once the season is on.
            if (s.Phase is SessionPhase.RegularSeason)
                foreach (var t in s.League.Teams)
                    if (t.Roster.Count > s.League.Finance.RosterMax || t.Roster.Count < s.League.Finance.RosterMin)
                        Add("state", page, $"{t.FullName} carry {t.Roster.Count} players in the regular season (limits {s.League.Finance.RosterMin}-{s.League.Finance.RosterMax})");
            if (s.Employed && s.Phase is SessionPhase.RegularSeason && s.HumanTeam.Roster.Count < 8)
                Add("state", page, $"your club has only {s.HumanTeam.Roster.Count} players in the regular season");
            // Below the league's own minimum (13 in the NBA, 11 below it), outside college (the old H9: Brooklyn opened a season with 12).
            else if (s.Employed && !s.InCollege && s.Phase is SessionPhase.RegularSeason && s.HumanTeam.Roster.Count < s.MyRosterMin)
                Add("state", page, $"your club carries {s.HumanTeam.Roster.Count} players in the regular season, under the minimum of {s.MyRosterMin} ({s.HumanTeam.FullName}, {s.HumanLeagueName})");

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
        private static string Describe(object? source)
        {
            if (source is not Visual v) return source?.GetType().Name ?? "null";
            // The control and its first few named or typed ancestors, so the page can be found.
            var chain = new List<string>();
            for (Visual? x = v; x != null && chain.Count < 6; x = x.GetVisualParent())
                chain.Add(x is Control { Name: { Length: > 0 } n } ? $"{x.GetType().Name}#{n}" : x.GetType().Name);
            return string.Join(" < ", chain);
        }
        public readonly List<string> Errors = new();
        /// <summary>Layout-cycle warnings (Avalonia logs the control it re-queued before it gives up), kept apart from binding errors.</summary>
        public readonly List<string> LayoutWarnings = new();
        public static BindingSink? Current;
        public BindingSink() => Current = this;
        public bool IsEnabled(LogEventLevel level, string area) => level >= LogEventLevel.Warning && area is LogArea.Binding or LogArea.Property or LogArea.Layout;
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
            if (area == LogArea.Layout) { lock (LayoutWarnings) { LayoutWarnings.Add($"{msg} (on {Describe(source)})"); if (LayoutWarnings.Count > 50) LayoutWarnings.RemoveAt(0); } return; }
            lock (Errors) if (Errors.Count < 2000) Errors.Add($"{area}: {msg} (on {source?.GetType().Name})");
        }
    }
}
