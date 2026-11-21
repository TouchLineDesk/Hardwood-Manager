using Hardwood.Core.Season;

namespace Hardwood.Core.College;

/// <summary>A side of a matchup on the Selection Show's big screen.</summary>
public sealed record ShowSide(int Seed, int TeamId, string School, string Record, string Tag, bool Mine);

/// <summary>
/// One reveal of the Selection Show (B1, run 139): a First Four game or a first-round game, where and when it's played,
/// the two sides, and the presenter's line. <see cref="Region"/> is -1 for the open, the First Four and the close;
/// <see cref="Pair"/> is the pairing's place in its region in bracket order (0 = 1 v 16 … 7 = 2 v 15).
/// </summary>
public sealed record ShowBeat(
    string Kind, string Site, string Round, string When, int Region, int Pair,
    ShowSide? Top, ShowSide? Bottom, StudioAnalyst Speaker, string Line, bool Mine)
{
    public bool IsGame => Top != null && Bottom != null;
}

/// <summary>
/// The Selection Show on Selection Sunday (B1, broadcast-presentation-design.md): the field revealed game by game on the
/// HSN studio set, the First Four in Dayton first, then each region's eight first-round games in bracket order, each with
/// its site and a line from the desk; the close names the last four in and the first four out. It only reads the season:
/// the bracket is the one Selection Sunday picked, and nothing here draws a random number.
/// </summary>
public static class SelectionShow
{
    public static StudioAnalyst Host => StudioShow.HostDesk;
    public static StudioAnalyst Analyst => StudioShow.Numbers;
    public const string FirstFourSite = "Dayton, Ohio";

    /// <summary>
    /// Cities that take turns hosting the first and second rounds (our own rotation for the game, not a real schedule).
    /// Eight a year; each hosts two pods from different regions.
    /// </summary>
    private static readonly string[] Pool =
    {
        "Pittsburgh", "Greenville", "Spokane", "Omaha", "Charlotte", "Brooklyn", "Salt Lake City", "Columbus",
        "Indianapolis", "Memphis", "Denver", "Sacramento", "Milwaukee", "Raleigh", "Birmingham", "Albany",
        "Tulsa", "Buffalo", "Orlando", "Dallas", "Portland", "Providence", "Lexington", "San Diego",
    };

    /// <summary>The site of a first-round pod: region 0-3, pod 0-3 (1 v 16 with 8 v 9, 5 v 12 with 4 v 13, …).</summary>
    public static string PodSite(int year, int region, int pod)
    {
        int site = (pod + region * 2) % 8;
        int start = ((year * 5) % Pool.Length + Pool.Length) % Pool.Length;
        return Pool[(start + site * 3) % Pool.Length];
    }

    /// <summary>The whole show, in order. Empty before Selection Sunday.</summary>
    public static List<ShowBeat> Build(CollegeWorld c, int? mine)
    {
        var s = c.Season;
        var beats = new List<ShowBeat>();
        if (!s.Selected || s.Bracket.Count < 64) return beats;
        int year = s.Year + 1;
        var auto = new Dictionary<int, string>();
        foreach (var conf in c.Active)
            if (s.ConferenceChampions.TryGetValue(conf.Key, out var champ)) auto.TryAdd(champ, conf.Short);
        string School(int id) => c.Program(id)?.City ?? "?";
        string Rec(int id) { var (w, l) = c.Record(id); return $"{w}-{l}"; }
        double Elo(int id) => s.Elo.GetValueOrDefault(id, 1500);
        string Tag(int id) => s.LastFourIn.Contains(id) ? "LAST FOUR IN" : auto.TryGetValue(id, out var cs) ? $"{cs} CHAMPION" : "";
        ShowSide Side(int seed, int id) => new(seed, id, School(id), Rec(id), Tag(id), id == mine);
        var dates = s.RoundDates.TryGetValue(CollegeWorld.Madness, out var d) ? d : new List<DateOnly>();
        string When(int round) => round < dates.Count ? dates[round].ToString("ddd, MMM d") : "";
        int autos = auto.Count;
        int myRegion = mine is { } m0 ? c.RegionOf(m0) : -1;
        int n = 0;
        StudioAnalyst Next() => n++ % 2 == 0 ? Host : Analyst;

        beats.Add(new("Open", "HSN STUDIOS", "Selection Sunday", "", -1, -1, null, null, Host,
            $"Welcome to the Selection Show. The committee has picked the field of 68: {autos} conference champions and "
            + $"{68 - autos} at-large teams. We'll go game by game, starting in Dayton.", false));
        n = 1;

        // The First Four, in Dayton: the last four at-large teams and the four lowest automatic bids.
        var playIns = s.Games.Where(g => g.Event == CollegeWorld.Madness && g.Round == 0).OrderBy(g => g.Slot).ToList();
        foreach (var g in playIns)
        {
            int seed = s.Seeds.GetValueOrDefault(g.HomeId);
            int region = g.Slot / 16;
            var a = Side(seed, g.HomeId); var b = Side(seed, g.AwayId);
            bool my = a.Mine || b.Mine;
            string line = my
                ? $"You're in, but you'll have to earn it: {(a.Mine ? a.School : b.School)} plays {(a.Mine ? b.School : a.School)} in Dayton for a No. {seed} seed in the {MadnessBracket.Regions[region]}."
                : s.LastFourIn.Contains(g.HomeId) || s.LastFourIn.Contains(g.AwayId)
                ? $"Two of the last teams in: {a.School} and {b.School} meet in Dayton, and the winner is a No. {seed} seed in the {MadnessBracket.Regions[region]}."
                : $"{a.School} ({a.Record}) and {b.School} ({b.Record}) play for a No. {seed} seed in the {MadnessBracket.Regions[region]}.";
            beats.Add(new("First Four", FirstFourSite.ToUpperInvariant(), "First Four", When(0), region, -1, a, b, Next(), line, my));
        }

        for (int r = 0; r < 4; r++)
        {
            var field = MadnessBracket.RegionField(s, r);
            for (int p = 0; p < field.Count; p++)
            {
                var (top, bottom) = field[p];
                if (top.Teams.Count == 0) continue;
                var a = Side(top.Seed, top.Teams[0]);
                ShowSide b = bottom.FirstFour
                    ? new(bottom.Seed, 0, $"{School(bottom.Teams[0])} / {School(bottom.Teams[1])}", "First Four", "", bottom.Teams.Contains(mine ?? -1))
                    : bottom.Teams.Count > 0 ? Side(bottom.Seed, bottom.Teams[0]) : new(bottom.Seed, 0, "To be decided", "", "", false);
                string site = PodSite(year, r, p / 2);
                bool my = a.Mine || b.Mine;
                var speaker = Next();
                beats.Add(new("Game", site.ToUpperInvariant(), $"{MadnessBracket.Regions[r]} · First round", When(1), r, p, a, b,
                    speaker, LineFor(r, p, a, b, site, my, myRegion, Elo, s, auto, School), my));
            }
        }

        // The close: the bubble, and your program if it was left out.
        string close = s.FirstFourOut.Count == 0 ? "That's the field. Brackets are open." :
            $"On the bubble and out: {string.Join(", ", s.FirstFourOut.Select(School))}. "
            + (s.LastFourIn.Count > 0 ? $"The last team in was {School(s.LastFourIn[^1])} at {Rec(s.LastFourIn[^1])}. " : "")
            + "That's your field of 68.";
        if (mine is { } me && myRegion < 0)
            close = (s.FirstFourOut.Contains(me) ? $"Tough night for {School(me)}: among the first four out. "
                : s.NextFourOut.Contains(me) ? $"{School(me)} was in the next four out: close, not close enough. "
                : $"{School(me)} wasn't near the bubble this year. ") + close;
        beats.Add(new("Close", "HSN STUDIOS", "The field is set", "", -1, -1, null, null, Analyst, close, mine is { } && myRegion < 0));
        return beats;
    }

    private static string LineFor(int region, int pair, ShowSide a, ShowSide b, string site, bool my, int myRegion,
        Func<int, double> elo, CollegeSeason s, Dictionary<int, string> auto, Func<int, string> school)
    {
        string reg = MadnessBracket.Regions[region];
        if (my)
        {
            var me = a.Mine ? a : b; var them = a.Mine ? b : a;
            if (b.Mine && b.TeamId == 0)
                return $"And there's your First Four game's prize: the winner is the No. {b.Seed} seed in the {reg}, against No. {a.Seed} {a.School} in {site}.";
            return $"There it is: {me.School} is in! A No. {me.Seed} seed in the {reg}, against No. {them.Seed} {them.School} in {site}.";
        }
        if (pair == 0)
            return $"The No. 1 seed in the {reg}: {a.School} at {a.Record}. They open in {site} against {b.School}.";
        if (pair == 7)
            return $"{a.School} ({a.Record}) is the No. 2 seed in the {reg}, and opens against {b.School} in {site}.";
        bool close = b.TeamId > 0 && Math.Abs(elo(a.TeamId) - elo(b.TeamId)) < 40;
        if (a.Seed == 5 && b.Seed == 12)
            return close
                ? $"Circle this one: {b.School} over {a.School} is my 12-over-5 upset pick. These two are closer than the seeds say."
                : $"The 5-12 game everybody checks first: {a.School} against {b.School} in {site}.";
        if (a.Seed == 8 && b.Seed == 9)
            return $"It will be {a.School} taking on {b.School} in an 8-9 game in {site}. Coin flip, and the winner likely gets the 1-seed.";
        if (b.TeamId > 0 && auto.TryGetValue(b.TeamId, out var conf) && b.Seed >= 13)
            return $"{b.School} punched its ticket by winning the {conf} tournament. Now it's No. {a.Seed} {a.School} in {site}.";
        if (b.TeamId > 0 && s.LastFourIn.Contains(b.TeamId))
            return $"{b.School} was one of the last four in, and gets No. {a.Seed} {a.School} in {site}.";
        if (close)
            return $"Watch {a.Seed}-{b.Seed}: {a.School} against {b.School}. Upset alert in {site}.";
        return $"It will be {a.School} taking on {b.School} in a {a.Seed}-{b.Seed} first-round game in {site}.";
    }
}
