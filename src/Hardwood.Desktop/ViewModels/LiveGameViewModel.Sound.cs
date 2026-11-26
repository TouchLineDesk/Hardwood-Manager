using CommunityToolkit.Mvvm.Input;
using Hardwood.Desktop.Audio;

namespace Hardwood.Desktop.ViewModels;

/// <summary>
/// M6 of the 2D match engine (run 153, design §F): the crowd. Two beds loop all game, mixed by the house and the
/// momentum; reactions fire from the engine's own events as they land on screen (<see cref="Reactions"/>); the whistle
/// and buzzer are code-made. Presentation only: nothing here reads or moves the engine's random stream.
/// </summary>
public sealed partial class LiveGameViewModel
{
    private IGameAudio? _audio;
    private List<SoundCue> _cues = new();
    private int _cueNext;
    private bool _minimised;
    /// <summary>Set once the page has let the sound go (left, or the game recorded): nothing reopens it.</summary>
    private bool _audioReleased;
    /// <summary>The most the home side has trailed by on screen so far (run 157: a comeback brings the crowd back).</summary>
    private int _worstDeficit;
    /// <summary>The home side's run, for the band's hit (run 161).</summary>
    private readonly BandRun _bandRun = new();

    /// <summary>The game's sound (opened on the first frame played; silent with no device).</summary>
    public IGameAudio Audio => _audio ??= _audioReleased ? new SilentAudio() : OpenAudio();

    private IGameAudio OpenAudio()
    {
        var a = GameAudio.Factory();
        a.Volume = AppSettings.Current.SoundVolume;
        a.Muted = !AppSettings.Current.SoundOn;
        a.Suspended = _minimised;
        // Fight songs A (run 161): a college home side brings its band. The song renders off the UI thread (about a
        // second the first time; then it's kept for the session) and joins in when it's ready.
        if (BandFor() is { } band)
        {
            var (school, spec) = band;
            a.ExpectBand();
            System.Threading.Tasks.Task.Run(() =>
            {
                try { var song = FightSongLibrary.For(school, spec); if (!_audioReleased) a.SetBand(song); }
                catch (Exception) { }
            });
        }
        return a;
    }

    /// <summary>The home band's school and its song, or null when the home side isn't a college program.</summary>
    public (string School, Hardwood.Core.Presentation.FightSongSpec Spec)? BandFor()
    {
        var college = CollegeGameNow ? Session.World.College : null;
        if (college == null || !college.ConferenceOf.TryGetValue(HomeTeam.Id, out var conf)) return null;
        var members = college.InConference(conf).Select(t => t.City);
        return (HomeTeam.City, Hardwood.Core.Presentation.FightSongs.For(HomeTeam.City, members));
    }

    /// <summary>The speaker button: sound on or off (saved in the app settings, like Settings' switch).</summary>
    public bool SoundOn
    {
        get => AppSettings.Current.SoundOn;
        set
        {
            if (AppSettings.Current.SoundOn == value) return;
            AppSettings.Current.SoundOn = value;
            AppSettings.Current.Save();
            if (_audio != null) _audio.Muted = !value;
            OnPropertyChanged(); OnPropertyChanged(nameof(SoundIcon)); OnPropertyChanged(nameof(SoundTip));
        }
    }

    /// <summary>The volume slider, 0–100.</summary>
    public double SoundVolume
    {
        get => Math.Round(AppSettings.Current.SoundVolume * 100);
        set
        {
            double v = Math.Clamp(value / 100.0, 0, 1);
            if (Math.Abs(AppSettings.Current.SoundVolume - v) < 0.001) return;
            AppSettings.Current.SoundVolume = v;
            AppSettings.Current.Save();
            if (_audio != null) _audio.Volume = v;
            OnPropertyChanged(); OnPropertyChanged(nameof(SoundTip));
        }
    }

    public string SoundIcon => SoundOn ? "VolumeHigh" : "VolumeMute";
    public string SoundTip => SoundOn
        ? $"Crowd sound on ({SoundVolume:0}%). Click to mute (M). At 4× and 8× only the biggest moments play."
        : "Crowd sound muted. Click to turn it on (M).";
    [RelayCommand] private void ToggleSound() => SoundOn = !SoundOn;

    /// <summary>The window was minimised or restored: silence while it's down.</summary>
    public void SetMinimised(bool minimised)
    {
        _minimised = minimised;
        if (_audio != null) _audio.Suspended = minimised;
    }

    /// <summary>Close the sound (the game is over and recorded, or the page has gone).</summary>
    public void ReleaseAudio()
    {
        // Review (run 153): leaving mid-game (the main menu) left the frame timer running, and the next frame reopened the
        // device. Stop the timer and never reopen.
        _timer.Stop(); Running = false;
        _audioReleased = true;
        _audio?.Dispose();
        _audio = new SilentAudio();
    }

    /// <summary>A new possession is on screen: plan its reactions.</summary>
    private void PlanSound()
    {
        // The crowd's reactions, then the court and the band (run 160), on one clock.
        _cues = _script == null ? new() : Reactions.Plan(_script).Concat(Reactions.Court(_script)).Concat(Reactions.Band(_script, _bandRun)).OrderBy(q => q.T).ToList();
        _cueNext = 0;
    }

    /// <summary>Fire the cues whose time has come, up to <paramref name="t"/> on the possession's clock.</summary>
    private void FireSound(double t)
    {
        if (_script == null) return;
        bool key = SpeedIndex == KeySpeed && _script.Key;
        var audio = Audio;
        while (_cueNext < _cues.Count && _cues[_cueNext].T <= t)
        {
            var cue = _cues[_cueNext++];
            if (Reactions.Plays(cue, Rate, key)) audio.Play(cue.Kind, cue.Gain, cue.Pan);
        }
    }

    /// <summary>A jump (a step, the rest simmed): the reactions skipped over aren't played late.</summary>
    private void SkipSound()
    {
        _cueNext = _cues.Count;
        _audio?.StopReactions();
        UpdateBed();
    }

    /// <summary>The bed's target now: the house, the momentum, late and close, paused, the speed.</summary>
    private void UpdateBed()
    {
        if (_audio == null) return;
        _audio.SetBed(BedTarget(Running && !Complete));
    }

    /// <summary>Late and close as the screen shows it (the engine is a possession ahead, and has finished while the last
    /// possession still plays).</summary>
    private bool LateAndClose()
    {
        if (Complete) return false;
        double clock = MatchFrame?.Clock ?? _live.Clock;
        int period = _script?.Input.Events.FirstOrDefault()?.Period ?? _live.Period;
        return period >= _live.Result.Periods && clock < 120 && Math.Abs(BarHomeScore - BarAwayScore) <= 6;
    }

    /// <summary>The bed's target as the view model sets it (tests read it too).</summary>
    public BedLevels BedTarget(bool running)
    {
        bool quiet = SpeedIndex is 2 or 3 || (SpeedIndex == KeySpeed && _script is { Key: false });
        int margin = BarHomeScore - BarAwayScore;
        _worstDeficit = Math.Max(_worstDeficit, -margin);
        return CrowdMix.Target(FloorDress.Crowd, Momentum.Swing, LateAndClose(), paused: !running, quiet, margin, _worstDeficit);
    }
}
