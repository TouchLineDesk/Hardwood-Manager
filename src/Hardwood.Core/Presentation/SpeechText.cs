using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Hardwood.Core.Presentation;

/// <summary>Words a broadcaster would say for a name or abbreviation on screen (run 239, V1 of
/// <c>claude/studio-voice-plan.md</c>): team abbreviations to club names, initials to full names, and respellings for
/// names the voice gets wrong. Applied first, longest key first, on whole words only, case-sensitive.</summary>
public sealed class SpeechLexicon
{
    private readonly Dictionary<string, string> _say = new(StringComparer.Ordinal);

    public static readonly SpeechLexicon Empty = new();

    public SpeechLexicon Add(string onScreen, string said)
    {
        if (!string.IsNullOrWhiteSpace(onScreen)) _say[onScreen.Trim()] = said;
        return this;
    }

    public IReadOnlyDictionary<string, string> Entries => _say;
}

/// <summary>Turns what a studio-show line shows on screen into what a broadcaster would say, for the voice pack's
/// text-to-speech (run 239, V1). Pure text: no audio, no RNG, nothing saved. "16-14" → "sixteen and fourteen" (a
/// record) or "sixteen to fourteen" (after "leads", "won", …), "$41.25M" → "forty-one point two five million
/// dollars", "3P%" → "three-point percentage", "Q3 4:12" → "third quarter, four-twelve left".</summary>
public static class SpeechText
{
    /// <summary>Stat and screen abbreviations, matched as whole tokens (case-sensitive).</summary>
    private static readonly (string Token, string Said)[] Abbreviations =
    {
        ("3P%", "three-point percentage"), ("FG%", "field-goal percentage"), ("FT%", "free-throw percentage"),
        ("TS%", "true shooting percentage"), ("eFG%", "effective field-goal percentage"),
        ("3PT", "three-point"), ("3PA", "three-point attempts"), ("3PM", "threes made"), ("3P", "three-point"),
        ("PPG", "points a game"), ("RPG", "rebounds a game"), ("APG", "assists a game"), ("SPG", "steals a game"),
        ("BPG", "blocks a game"), ("MPG", "minutes a game"),
        ("PTS", "points"), ("REB", "rebounds"), ("AST", "assists"), ("STL", "steals"), ("BLK", "blocks"),
        ("OVR", "overall"), ("POT", "potential"),
        ("vs.", "versus"), ("vs", "versus"), ("Jr.", "Junior"), ("Sr.", "Senior"),
        ("St.", "Saint"), ("Mt.", "Mount"), ("approx.", "about"),
    };

    /// <summary>Words after which "X-Y" is a score ("leads 16-14" → "sixteen to fourteen"), not a record.</summary>
    private static readonly HashSet<string> ScoreWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "lead", "leads", "led", "leading", "up", "down", "trail", "trails", "trailed", "trailing", "won", "win", "wins",
        "beat", "beats", "lost", "lose", "loses", "final", "score", "scored", "it's", "is", "was", "at", "by", "edged",
        "ahead", "behind", "halftime", "tied", "series",
    };

    private static readonly string[] Ones =
    {
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve",
        "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen",
    };

    private static readonly string[] Tens = { "", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety" };

    /// <summary>What a broadcaster says for <paramref name="onScreen"/>.</summary>
    public static string Say(string onScreen, SpeechLexicon? lexicon = null)
    {
        if (string.IsNullOrWhiteSpace(onScreen)) return "";
        var s = onScreen.Replace('’', '\'').Replace('−', '-').Replace('–', '-');

        // 1. The lexicon (club abbreviations, initials, respellings), longest first, whole words only.
        if (lexicon is { Entries.Count: > 0 })
            foreach (var (key, said) in lexicon.Entries.OrderByDescending(e => e.Key.Length).ThenBy(e => e.Key, StringComparer.Ordinal))
                s = Regex.Replace(s, $@"(?<![\w]){Regex.Escape(key)}(?![\w])", said.Replace("$", "$$"));

        // 2. The game clock with its period: "Q3 4:12" → "third quarter, four-twelve left".
        s = Regex.Replace(s, @"\b(Q[1-4]|H[12]|[2-9]?OT)\s+(\d{1,2}):(\d{2})(?:\.\d)?\b",
            m => $"{Period(m.Groups[1].Value)}, {Clock(int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value))} left");
        s = Regex.Replace(s, @"\b(Q[1-4]|H[12]|[2-9]?OT)\b", m => Period(m.Groups[1].Value));

        // 3. Money: "$41.25M" → "forty-one point two five million dollars"; "$1,234" → "one thousand … dollars".
        s = Regex.Replace(s, @"\$(\d+(?:\.\d+)?)\s?([KMB])\b", m =>
            $"{Decimal(m.Groups[1].Value)} {m.Groups[2].Value switch { "K" => "thousand", "M" => "million", _ => "billion" }} dollars");
        s = Regex.Replace(s, @"\$(\d{1,3}(?:,\d{3})+|\d+)(?:\.(\d{2}))?(?!\d)", m =>
        {
            var dollars = Cardinal(long.Parse(m.Groups[1].Value.Replace(",", ""), CultureInfo.InvariantCulture));
            var cents = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
            return cents > 0 ? $"{dollars} dollars and {Cardinal(cents)} cents" : $"{dollars} dollars";
        });

        // 4. Abbreviations, whole tokens only.
        foreach (var (token, said) in Abbreviations)
            s = Regex.Replace(s, $@"(?<![\w%]){Regex.Escape(token)}(?![\w%])", said);
        s = Regex.Replace(s, @"\bNo\.\s?(?=\d)", "number ");
        s = Regex.Replace(s, @"#(\d+)", m => $"number {Cardinal(long.Parse(m.Groups[1].Value))}");
        s = s.Replace(" @ ", " at ").Replace(" & ", " and ");

        // 5. Seasons and heights before the records: "2026-27" → "twenty twenty-six, twenty-seven"; 6'8" → "six-eight".
        s = Regex.Replace(s, @"\b((?:19|20)\d{2})-(\d{2})\b", m => $"{Year(int.Parse(m.Groups[1].Value))}, {TwoDigits(int.Parse(m.Groups[2].Value))}");
        s = Regex.Replace(s, @"\b([4-7])'(\d{1,2})""", m => $"{Ones[int.Parse(m.Groups[1].Value)]}-{Cardinal(int.Parse(m.Groups[2].Value))}");

        // 6. Records and scores: "17-15" → "seventeen and fifteen"; after a score word, "seventeen to fifteen".
        s = Regex.Replace(s, @"(?<![\w.\-])(\d{1,3})-(\d{1,3})(?![\w\-]|\.\d)", m =>
        {
            var before = s[..m.Index].TrimEnd();
            var lastWord = Regex.Match(before, @"([\w']+)\W*$").Groups[1].Value;
            var joiner = ScoreWords.Contains(lastWord) ? "to" : "and";
            return $"{Cardinal(int.Parse(m.Groups[1].Value))} {joiner} {Cardinal(int.Parse(m.Groups[2].Value))}";
        });

        // 7. Ordinals, percentages, shooting averages, signs.
        s = Regex.Replace(s, @"\b(\d+)(?:st|nd|rd|th)\b", m => Ordinal(long.Parse(m.Groups[1].Value)));
        s = Regex.Replace(s, @"(\d+(?:\.\d+)?)\s?%", m => $"{Decimal(m.Groups[1].Value)} percent");
        s = Regex.Replace(s, @"(?<![\w.])\.(\d{3})\b", m => Average(m.Groups[1].Value));
        s = Regex.Replace(s, @"(?<![\w])\+(\d+(?:\.\d+)?)", m => $"plus {Decimal(m.Groups[1].Value)}");
        s = Regex.Replace(s, @"(?<=^|[\s(])-(\d+(?:\.\d+)?)", m => $"minus {Decimal(m.Groups[1].Value)}");

        // 8. Years, then every other number.
        s = Regex.Replace(s, @"(?<![\d,.$])\b((?:19[5-9]|20\d)\d)\b(?![,.]\d)", m => Year(int.Parse(m.Groups[1].Value)));
        s = Regex.Replace(s, @"\b\d{1,3}(?:,\d{3})+\b", m => Cardinal(long.Parse(m.Value.Replace(",", ""))));
        s = Regex.Replace(s, @"\b\d+\.\d+\b", m => Decimal(m.Value));
        s = Regex.Replace(s, @"\b\d+\b", m => m.Value.Length > 12 ? string.Join(" ", m.Value.Select(c => Ones[c - '0'])) : Cardinal(long.Parse(m.Value)));

        // 9. Screen punctuation to pauses.
        s = s.Replace(" · ", ", ").Replace("·", ", ").Replace(" | ", ", ").Replace(" / ", " or ");
        s = Regex.Replace(s, @"\s+", " ").Trim();
        s = Regex.Replace(s, @"\s+([,.!?;:])", "$1");
        return s;
    }

    /// <summary>A whole number in words: 41 → "forty-one", 1234 → "one thousand two hundred thirty-four".</summary>
    public static string Cardinal(long n)
    {
        if (n < 0) return "minus " + Cardinal(-n);
        if (n < 20) return Ones[n];
        if (n < 100) return Tens[n / 10] + (n % 10 == 0 ? "" : "-" + Ones[n % 10]);
        if (n < 1000) return Ones[n / 100] + " hundred" + (n % 100 == 0 ? "" : " " + Cardinal(n % 100));
        foreach (var (size, word) in new[] { (1_000_000_000_000L, "trillion"), (1_000_000_000L, "billion"), (1_000_000L, "million"), (1_000L, "thousand") })
            if (n >= size) return Cardinal(n / size) + " " + word + (n % size == 0 ? "" : " " + Cardinal(n % size));
        return n.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>1 → "first", 22 → "twenty-second", 100 → "one hundredth".</summary>
    public static string Ordinal(long n)
    {
        var words = Cardinal(n);
        var cut = Math.Max(words.LastIndexOf(' '), words.LastIndexOf('-')) + 1;
        var (head, last) = (words[..cut], words[cut..]);
        var said = last switch
        {
            "one" => "first", "two" => "second", "three" => "third", "five" => "fifth", "eight" => "eighth",
            "nine" => "ninth", "twelve" => "twelfth",
            _ when last.EndsWith('y') => last[..^1] + "ieth",
            _ => last + "th",
        };
        return head + said;
    }

    /// <summary>"41.25" → "forty-one point two five"; "7" → "seven"; "0.5" → "zero point five".</summary>
    public static string Decimal(string number)
    {
        var parts = number.Split('.');
        var whole = Cardinal(long.Parse(parts[0], CultureInfo.InvariantCulture));
        if (parts.Length < 2 || parts[1].TrimEnd('0').Length == 0) return whole;
        return whole + " point " + string.Join(" ", parts[1].Select(c => Ones[c - '0']));
    }

    /// <summary>A year as it's said: 2027 → "twenty twenty-seven", 2005 → "two thousand five", 1985 → "nineteen eighty-five".</summary>
    public static string Year(int y)
    {
        if (y >= 2000 && y < 2010) return Cardinal(y);
        var (hi, lo) = (y / 100, y % 100);
        return lo == 0 ? Cardinal(hi) + " hundred" : $"{Cardinal(hi)} {TwoDigits(lo)}";
    }

    private static string TwoDigits(int n) => n < 10 ? "oh-" + Ones[n] : Cardinal(n);

    /// <summary>A shooting average: ".379" → "three seventy-nine", ".500" → "five hundred", ".405" → "four oh-five".</summary>
    private static string Average(string three)
    {
        var (hi, lo) = (three[0] - '0', int.Parse(three[1..]));
        if (hi == 0) return "point " + string.Join(" ", three.Select(c => Ones[c - '0']));
        return lo == 0 ? $"{Ones[hi]} hundred" : $"{Ones[hi]} {TwoDigits(lo)}";
    }

    /// <summary>"Q3" → "third quarter", "H1" → "first half", "OT" → "overtime", "2OT" → "double overtime".</summary>
    private static string Period(string p) => p switch
    {
        ['Q', var q] => Ordinal(q - '0') + " quarter",
        ['H', var h] => Ordinal(h - '0') + " half",
        "OT" => "overtime",
        "2OT" => "double overtime",
        "3OT" => "triple overtime",
        _ => Ordinal(p[0] - '0') + " overtime",
    };

    /// <summary>"4:12" → "four-twelve", "4:05" → "four-oh-five", "4:00" → "four minutes", "0:42" → "forty-two seconds".</summary>
    private static string Clock(int minutes, int seconds)
    {
        if (minutes == 0) return seconds == 1 ? "one second" : Cardinal(seconds) + " seconds";
        if (seconds == 0) return minutes == 1 ? "one minute" : Cardinal(minutes) + " minutes";
        return $"{Cardinal(minutes)}-{(seconds < 10 ? "oh-" + Ones[seconds] : Cardinal(seconds))}";
    }

    /// <summary>The longest stretch of text the voice is given at once: lines are split at sentence ends so each piece
    /// stays well under the model's 510-token limit.</summary>
    public static IReadOnlyList<string> Pieces(string said, int maxChars = 220)
    {
        var pieces = new List<string>();
        var current = new StringBuilder();
        foreach (Match m in Regex.Matches(said, @"[^.!?;]+[.!?;]*\s*"))
        {
            var sentence = m.Value;
            if (current.Length > 0 && current.Length + sentence.Length > maxChars) { pieces.Add(current.ToString().Trim()); current.Clear(); }
            while (sentence.Length > maxChars)
            {
                var cut = sentence.LastIndexOf(',', maxChars - 1);
                if (cut < maxChars / 3) cut = sentence.LastIndexOf(' ', maxChars - 1);
                if (cut <= 0) cut = maxChars - 1;
                pieces.Add(sentence[..(cut + 1)].Trim());
                sentence = sentence[(cut + 1)..];
            }
            current.Append(sentence);
        }
        if (current.ToString().Trim() is { Length: > 0 } rest) pieces.Add(rest);
        return pieces;
    }
}
