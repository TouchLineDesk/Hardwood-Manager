// Run 239, V1 of claude/studio-voice-plan.md: checks a voice pack folder and makes one line in every cast voice.
//   dotnet run --project tools/Voice/VoiceCheck -- <pack folder> [<out folder>]
// The pack folder holds voice-pack.json, the model and the voices. Exits non-zero if the pins don't match, a voice is
// missing, or a line comes out silent or the wrong length. Writes one WAV per voice to the out folder when given.
using System.Diagnostics;
using Hardwood.Core.Presentation;
using Hardwood.Core.Season;
using Hardwood.Voice;

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: VoiceCheck <pack folder> [<out folder>]");
    return 2;
}

var folder = args[0];
var outDir = args.Length > 1 ? args[1] : null;
if (outDir != null) Directory.CreateDirectory(outDir);

var (pack, problem) = VoicePack.Check(folder);
if (pack == null)
{
    Console.Error.WriteLine($"FAIL: {problem}");
    return 1;
}
Console.WriteLine($"Pack {pack.Pack}: pins match ({string.Join(", ", pack.Files.Select(f => $"{f.Name} {f.Bytes:N0} bytes"))}).");

var clock = Stopwatch.StartNew();
using var speaker = VoicePack.Open(folder, pack);
Console.WriteLine($"Loaded in {clock.Elapsed.TotalSeconds:F1} s; {speaker.Voices.Count} voices in the file.");

var missing = StudioVoiceCast.All.Where(v => !speaker.Voices.Contains(v)).ToList();
if (missing.Count > 0)
{
    Console.Error.WriteLine($"FAIL: the voice file has no {string.Join(", ", missing)}.");
    return 1;
}

// One line a voice, written as the studio show shows it, then said the broadcaster's way.
const string onScreen = "Welcome to the HSN Tip-Off. The Hornets are 17-15 and lead the season series 2-1. Last time they were up 16-14 at Q3 4:12, and it got away.";
var said = SpeechText.Say(onScreen);
Console.WriteLine($"Said: {said}");
Console.WriteLine($"Phonemes: {speaker.Phonemes(said)}");

var failed = 0;
foreach (var voice in StudioVoiceCast.All)
{
    clock.Restart();
    var audio = speaker.Speak(said, voice);
    var seconds = audio.Length / (double)KokoroSpeaker.SampleRate;
    var rms = Wav.Rms(audio);
    var ok = seconds is > 3 and < 30 && rms > 0.01;
    Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {voice,-11} {seconds,5:F1} s of speech in {clock.Elapsed.TotalSeconds,5:F1} s, level {rms:F3}");
    if (!ok) failed++;
    if (outDir != null) Wav.Write(Path.Combine(outDir, $"{voice}.wav"), audio);
}

if (failed > 0)
{
    Console.Error.WriteLine($"FAIL: {failed} voice(s) came out silent or the wrong length.");
    return 1;
}
Console.WriteLine("Voice pack OK.");
return 0;
