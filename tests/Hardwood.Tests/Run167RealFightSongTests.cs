using System.Diagnostics;
using Hardwood.Core.Presentation;
using Hardwood.Core.RealWorld;
using Hardwood.Desktop.Audio;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>Run 167, fight songs B (claude/fight-songs-plan.md, tier 1): real fight songs whose music was published in 1930
/// or earlier, played from new note data in tools/Assets/fightsongs/*.song. Every file parses with each bar adding up, names
/// a real program and a public-domain year, ends on its key's tonic, and arranges into a chorus and a hit the band can play;
/// the library plays it ahead of the composed song, and Danny's own file still beats both. Presentation only.</summary>
[Collection(SharedUiState.Name)]
public class Run167RealFightSongTests
{
    private readonly ITestOutputHelper _out;
    public Run167RealFightSongTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void Every_real_song_parses_names_a_program_and_is_public_domain()
    {
        var all = RealFightSongs.All;
        Assert.True(all.Count >= 4, $"{all.Count} songs");
        var programs = RealColleges.Programs.Select(p => p.School).ToHashSet();
        foreach (var s in all.Values)
        {
            _out.WriteLine($"{s.Program}: {s.Describe()}, {s.Melody.Count} notes, {s.Length} beats at {s.Tempo}, {s.Chords.Count} chords");
            Assert.Contains(s.Program, programs);
            Assert.InRange(s.Published, 1850, 1930);
            Assert.StartsWith("https://", s.Source);
            // The tune ends on the tonic of the key the file names (a check on the key line and the last bar).
            Assert.Equal(s.Key, s.Melody[^1].Midi % 12);
            // The chords cover the tune exactly, and every chord's root is a real pitch class.
            Assert.Equal(s.Length, s.Chords[^1].Beat + s.Chords[^1].Beats, 6);
            Assert.All(s.Chords, c => Assert.InRange(c.Root, 0, 11));
            // The melody sits where a voice sings it (the arranger moves it into the trumpets' range).
            Assert.All(s.Melody, m => Assert.InRange(m.Midi, 55, 81));
        }
    }

    [Fact]
    public void A_bar_that_does_not_add_up_fails_with_its_number()
    {
        const string bad = "program: X\ntitle: T\ncomposer: C\npublished: 1900\nsource: https://x\nmeter: 4\ntempo: 120\nkey: C\nstyle: March\n" +
                           "melody:\nC4:4 | E4:2 G4:1 | C5:4 |\nchords:\nC | C | C |\n";
        var e = Assert.Throws<FormatException>(() => RealFightSongs.Parse(bad, "bad.song"));
        Assert.Contains("bar 2", e.Message);
        const string fewChords = "program: X\ntitle: T\ncomposer: C\npublished: 1900\nsource: https://x\nmeter: 4\ntempo: 120\nkey: C\nstyle: March\n" +
                                 "melody:\nC4:4 | E4:2 G4:2 |\nchords:\nC |\n";
        Assert.Throws<FormatException>(() => RealFightSongs.Parse(fewChords, "few.song"));
        // Names, ties and chords.
        Assert.Equal(63, RealFightSongs.Midi("Eb4"));
        Assert.Equal(66, RealFightSongs.Midi("F#4"));
        Assert.Equal(60, RealFightSongs.Midi("B#3"));
        Assert.Equal(10, RealFightSongs.Chord("Bb7").Root);
        Assert.Equal(new[] { 0, 4, 7, 10 }, RealFightSongs.Chord("Bb7").Tones);
        Assert.Equal(new[] { 0, 3, 7 }, RealFightSongs.Chord("C#m").Tones);
        var tied = RealFightSongs.Parse("program: X\ntitle: T\ncomposer: C\npublished: 1900\nsource: https://x\nmeter: 2\npickup: 1\ntempo: 120\nkey: G\nstyle: March\n" +
                                        "melody:\nD4:1 | G4:2 | _:1 r:1 | G4:2 |\nchords:\nD7 | G | G | G |\n");
        Assert.Equal(new[] { (0.0, 1.0, 62), (1.0, 3.0, 67), (5.0, 2.0, 67) }, tied.Melody.ToArray());
        Assert.Equal(7.0, tied.Length);
    }

    [Fact]
    public void Each_song_arranges_into_a_full_chorus_and_a_short_hit_and_renders_cleanly()
    {
        foreach (var s in RealFightSongs.All.Values)
        {
            var score = RealFightSongs.Arrange(s);
            _out.WriteLine($"{s.Program}: {score.Seconds:0.0} s, hit {score.HitSeconds:0.0} s");
            Assert.InRange(score.Seconds, 25, 55);
            Assert.InRange(score.HitSeconds, 7, 20);
            // Every note of the tune is played by the trumpets (at least once, an octave shift allowed).
            var lead = score.Notes.Where(n => n.Part == BandPart.Lead).ToList();
            Assert.True(lead.Count >= s.Melody.Count, $"{lead.Count} lead notes for {s.Melody.Count}");
            Assert.All(lead, n => Assert.InRange(n.Midi, 58, 86));
            Assert.Contains(score.Notes, n => n.Part == BandPart.Glock && n.Beat >= score.HitFromBeat);
            Assert.Contains(score.Notes, n => n.Part == BandPart.Counter);
        }
        var michigan = RealFightSongs.Arrange(RealFightSongs.For("Michigan")!);
        var sw = Stopwatch.StartNew();
        var pcm = BandRenderer.Render(michigan);
        sw.Stop();
        _out.WriteLine($"Michigan rendered in {sw.ElapsedMilliseconds} ms");
        Assert.True(sw.ElapsedMilliseconds < 8000, $"{sw.ElapsedMilliseconds} ms");
        Assert.InRange(pcm.Max(MathF.Abs), 0.5f, 0.81f);
        Assert.DoesNotContain(pcm, float.IsNaN);
        Assert.Equal(pcm, BandRenderer.Render(michigan));
    }

    [Fact]
    public void The_library_plays_the_real_song_first_and_Danny_s_file_still_wins()
    {
        string old = FightSongLibrary.CustomFolder;
        string dir = Path.Combine(Path.GetTempPath(), "hw-fight167-" + Guid.NewGuid().ToString("N"));
        try
        {
            FightSongLibrary.CustomFolder = dir;
            FightSongLibrary.Forget();
            var spec = FightSongs.SpecFor("Michigan");
            var song = FightSongLibrary.For("Michigan", spec);
            Assert.Equal("\"The Victors\" (Louis Elbel, 1898)", song.Source);
            Assert.True(song.Hit.Length > 0 && song.Hit.Length < song.Song.Length);
            // A program with no real song yet keeps its composed one.
            var duke = FightSongs.SpecFor("Duke");
            Assert.Null(RealFightSongs.For("Duke"));
            Assert.Equal(duke.Describe(), FightSongLibrary.For("Duke", duke).Source);
            // Danny's own file beats the real song.
            Directory.CreateDirectory(dir);
            using (var w = new NAudio.Wave.WaveFileWriter(Path.Combine(dir, "Michigan.wav"), new NAudio.Wave.WaveFormat(44100, 16, 2)))
                for (int i = 0; i < 44100; i++) { float v = (float)(0.2 * Math.Sin(i * 0.05)); w.WriteSample(v); w.WriteSample(v); }
            FightSongLibrary.Forget();
            Assert.Equal("Michigan.wav", FightSongLibrary.For("Michigan", spec).Source);
        }
        finally
        {
            FightSongLibrary.CustomFolder = old;
            FightSongLibrary.Forget();
            try { Directory.Delete(dir, true); } catch (Exception) { }
        }
    }

    /// <summary>Writes the real songs one after another to $HW_FIGHT_DEMO/real-fight-songs-demo.wav (for Danny to listen to).</summary>
    [Fact]
    public void Real_songs_demo_when_asked()
    {
        var dir = Environment.GetEnvironmentVariable("HW_FIGHT_DEMO");
        if (string.IsNullOrEmpty(dir)) return;
        using var w = new NAudio.Wave.WaveFileWriter(Path.Combine(dir, "real-fight-songs-demo.wav"), NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(Synth.Rate, 2));
        foreach (var s in RealFightSongs.All.Values.OrderBy(s => s.Program))
        {
            _out.WriteLine($"{s.Program}: {s.Describe()}");
            var pcm = BandRenderer.Render(RealFightSongs.Arrange(s));
            w.WriteSamples(pcm, 0, pcm.Length);
            var gap = new float[Synth.Rate * 2];
            w.WriteSamples(gap, 0, gap.Length);
        }
    }
}
