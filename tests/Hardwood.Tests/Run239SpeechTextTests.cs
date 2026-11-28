using Hardwood.Core.Presentation;
using Hardwood.Core.Season;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Run 239, V1 of <c>claude/studio-voice-plan.md</c>: what the studio shows' voices will say for what the screen
/// shows (<see cref="SpeechText"/>), and who speaks in which voice (<see cref="StudioVoiceCast"/>). Text only: the
/// engine and the model live in the separate voice pack, checked by <c>voice-pack.yml</c>.</summary>
public class Run239SpeechTextTests
{
    [Theory]
    [InlineData("They're 17-15 on the year.", "They're seventeen and fifteen on the year.")]
    [InlineData("Duke leads 16-14.", "Duke leads sixteen to fourteen.")]
    [InlineData("They won 101-99 in overtime.", "They won one hundred one to ninety-nine in overtime.")]
    [InlineData("Up 2-1 in the series 2-1.", "Up two to one in the series two to one.")]
    [InlineData("Castle signed for $41.25M.", "Castle signed for forty-one point two five million dollars.")]
    [InlineData("A $0.9M deal", "A zero point nine million dollars deal")]
    [InlineData("He makes $1,250,000 a year.", "He makes one million two hundred fifty thousand dollars a year.")]
    [InlineData("$12.50 a ticket", "twelve dollars and fifty cents a ticket")]
    [InlineData("3P% is 38.5%", "three-point percentage is thirty-eight point five percent")]
    [InlineData("He shoots .379 from deep.", "He shoots three seventy-nine from deep.")]
    [InlineData("FG% .500", "field-goal percentage five hundred")]
    [InlineData("Q3 4:12", "third quarter, four-twelve left")]
    [InlineData("H1 0:42", "first half, forty-two seconds left")]
    [InlineData("Q4 2:05", "fourth quarter, two-oh-five left")]
    [InlineData("Q2 5:00", "second quarter, five minutes left")]
    [InlineData("It went to 2OT.", "It went to double overtime.")]
    [InlineData("Their 22nd win, 3rd straight, 1st in the league", "Their twenty-second win, third straight, first in the league")]
    [InlineData("The 2027 Final Four", "The twenty twenty-seven Final Four")]
    [InlineData("Back in 2005 and 1985", "Back in two thousand five and nineteen eighty-five")]
    [InlineData("The 2026-27 season", "The twenty twenty-six, twenty-seven season")]
    [InlineData("A 6'8\" forward", "A six-eight forward")]
    [InlineData("24.3 PPG, 8 RPG", "twenty-four point three points a game, eight rebounds a game")]
    [InlineData("Point differential +5.2", "Point differential plus five point two")]
    [InlineData("A -3 night", "A minus three night")]
    [InlineData("DUK @ UNC · Cameron Indoor", "DUK at UNC, Cameron Indoor")]
    [InlineData("No. 3 vs. No. 14", "number three versus number fourteen")]
    [InlineData("The #1 seed", "The number one seed")]
    [InlineData("12,450 fans", "twelve thousand four hundred fifty fans")]
    [InlineData("St. John's", "Saint John's")]
    public void A_screen_line_is_said_the_way_a_broadcaster_says_it(string onScreen, string said) =>
        Assert.Equal(said, SpeechText.Say(onScreen));

    [Fact]
    public void Words_stay_words_and_nothing_is_left_as_digits_or_screen_symbols()
    {
        var lines = new[]
        {
            "The Hornets are 17-15 and lead the season series 2-1 · Q3 4:12 · $41.25M · 3P% 38.5% · .379 · #3 · 2027",
            "Tasha: the numbers say 61% for the home side, a 4.5-point edge.",
            "Reggie Banfield thinks the 19-13 Spartans win by 10.",
        };
        foreach (var line in lines)
        {
            var said = SpeechText.Say(line);
            Assert.DoesNotMatch(@"\d", said);
            Assert.DoesNotContain("$", said);
            Assert.DoesNotContain("%", said);
            Assert.DoesNotContain("·", said);
        }
        Assert.Equal("Reggie Banfield thinks the nineteen and thirteen Spartans win by ten.", SpeechText.Say(lines[2]));
    }

    [Fact]
    public void The_lexicon_turns_abbreviations_and_initials_into_names_first()
    {
        var lex = new SpeechLexicon().Add("DUK", "Duke").Add("UNC", "North Carolina").Add("J. Smith", "Jalen Smith")
            .Add("Wembanyama", "Wem-ban-yama");
        Assert.Equal("Duke at North Carolina: Jalen Smith has twenty.", SpeechText.Say("DUK @ UNC: J. Smith has 20.", lex));
        Assert.Equal("Wem-ban-yama, not DUKE", SpeechText.Say("Wembanyama, not DUKE", lex));
        // Whole words only: an abbreviation inside a longer word is left alone.
        Assert.Equal("UNCW", SpeechText.Say("UNCW", lex));
    }

    [Fact]
    public void Numbers_in_words()
    {
        Assert.Equal("zero", SpeechText.Cardinal(0));
        Assert.Equal("forty-one", SpeechText.Cardinal(41));
        Assert.Equal("one hundred", SpeechText.Cardinal(100));
        Assert.Equal("one thousand two hundred thirty-four", SpeechText.Cardinal(1234));
        Assert.Equal("one hundred sixty-four million two hundred thousand", SpeechText.Cardinal(164_200_000));
        Assert.Equal("twentieth", SpeechText.Ordinal(20));
        Assert.Equal("twenty-first", SpeechText.Ordinal(21));
        Assert.Equal("one hundredth", SpeechText.Ordinal(100));
        Assert.Equal("twelfth", SpeechText.Ordinal(12));
        Assert.Equal("ninth", SpeechText.Ordinal(9));
        Assert.Equal("twenty ten", SpeechText.Year(2010));
        Assert.Equal("two thousand", SpeechText.Year(2000));
        Assert.Equal("nineteen hundred", SpeechText.Year(1900));
        Assert.Equal("nineteen oh-five", SpeechText.Year(1905));
    }

    [Fact]
    public void A_long_line_is_cut_at_its_sentences_and_nothing_is_lost()
    {
        var said = string.Join(" ", Enumerable.Range(1, 12).Select(i => $"This is sentence {SpeechText.Cardinal(i)}, and it runs on for a while."));
        var pieces = SpeechText.Pieces(said);
        Assert.True(pieces.Count > 1);
        Assert.All(pieces, p => Assert.InRange(p.Length, 1, 220));
        Assert.Equal(said.Replace(" ", ""), string.Concat(pieces).Replace(" ", ""));
        var oneLongSentence = string.Join(", ", Enumerable.Repeat("and then another thing happened on the floor", 12)) + ".";
        Assert.All(SpeechText.Pieces(oneLongSentence), p => Assert.InRange(p.Length, 1, 220));
        Assert.Empty(SpeechText.Pieces(""));
    }

    [Fact]
    public void The_cast_is_fixed_and_a_guest_keeps_his_voice()
    {
        Assert.Equal("am_michael", StudioVoiceCast.For(StudioShow.HostDesk));
        Assert.Equal("am_fenrir", StudioVoiceCast.For(StudioShow.BigMan));
        Assert.Equal("af_heart", StudioVoiceCast.For(StudioShow.Numbers));
        Assert.Equal("am_puck", StudioVoiceCast.For(StudioShow.Guard));
        Assert.Equal("am_onyx", StudioVoiceCast.For(StudioShow.Coach));

        var names = new[] { "Curtis Vandermolen", "Andre Kessling", "Terrence Albee", "Malik Oduya", "Jordan Price", "Sam Ortiz", "Lee Hart", "Ben Cole" };
        foreach (var name in names)
        {
            var guest = new StudioAnalyst("guest-x", name, "Guest", "", "#FFFFFF", "hype");
            var voice = StudioVoiceCast.For(guest);
            Assert.Contains(voice, StudioVoiceCast.GuestVoices);
            Assert.Equal(voice, StudioVoiceCast.GuestVoice(" " + name.ToUpperInvariant() + " "));
        }
        // The hash spreads the guests across the four voices rather than piling them onto one.
        Assert.True(names.Select(StudioVoiceCast.GuestVoice).Distinct().Count() >= 3);
        Assert.Equal(9, StudioVoiceCast.All.Distinct().Count());
        Assert.DoesNotContain("am_santa", StudioVoiceCast.All);
        Assert.All(StudioVoiceCast.All, v => Assert.Matches("^a[mf]_", v));
    }
}
