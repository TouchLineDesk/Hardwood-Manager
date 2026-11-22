using NAudio.Wave;

namespace Hardwood.Desktop.Audio;

/// <summary>What the live game asks of the sound (M6, run 153). The view model talks only to this, so tests record it and
/// a PC with no audio device gets <see cref="SilentAudio"/>.</summary>
public interface IGameAudio : IDisposable
{
    void SetBed(BedLevels target);
    void Play(Cue cue, double gain);
    /// <summary>Play one sound, panned (−1 left to 1 right; run 160's court sounds). Unpanned by default.</summary>
    void Play(Cue cue, double gain, double pan) => Play(cue, gain);
    void StopReactions();
    /// <summary>The home band's fight song for this game (fight songs A, run 161); null = no band (the pros).</summary>
    void SetBand(BandSong? song) { }
    /// <summary>A band is coming (its song is rendering): a fight-song cue waits for it instead of playing the drumline.</summary>
    void ExpectBand() { }
    double Volume { get; set; }
    bool Muted { get; set; }
    bool Suspended { get; set; }
    /// <summary>False when no device could be opened: the game plays on, silent.</summary>
    bool HasDevice { get; }
}

/// <summary>No sound: no audio device, sound switched off before the game, or a headless test.</summary>
public sealed class SilentAudio : IGameAudio
{
    public void SetBed(BedLevels target) { }
    public void Play(Cue cue, double gain) { }
    public void StopReactions() { }
    public double Volume { get; set; }
    public bool Muted { get; set; }
    public bool Suspended { get; set; }
    public bool HasDevice => false;
    public void Dispose() { }
}

/// <summary>The mixer on the PC's default output (NAudio's WaveOutEvent, Windows).</summary>
public sealed class DeviceAudio : IGameAudio
{
    private readonly CrowdMixer _mixer;
    private readonly IWavePlayer _out;
    private bool _disposed;

    private DeviceAudio(CrowdMixer mixer, IWavePlayer output) { _mixer = mixer; _out = output; }

    /// <summary>Open the default device, or return null (no device, no driver, not Windows, a broken asset).</summary>
    public static DeviceAudio? TryOpen(Func<IWavePlayer>? output = null)
    {
        CrowdMixer? mixer = null;
        IWavePlayer? player = null;
        try
        {
            if (output == null && !OperatingSystem.IsWindows()) return null;
            mixer = new CrowdMixer();
            player = output?.Invoke() ?? new WaveOutEvent { DesiredLatency = 120, NumberOfBuffers = 3 };
            player.Init(mixer);
            player.Play();
            return new DeviceAudio(mixer, player);
        }
        catch (Exception)
        {
            try { player?.Dispose(); } catch (Exception) { }
            mixer?.Dispose();
            return null;
        }
    }

    public void SetBed(BedLevels target) => _mixer.SetBed(target);
    public void Play(Cue cue, double gain) => _mixer.Play(cue, gain);
    public void Play(Cue cue, double gain, double pan) => _mixer.Play(cue, gain, pan);
    public void StopReactions() => _mixer.StopReactions();
    public void SetBand(BandSong? song) => _mixer.SetBand(song);
    public void ExpectBand() => _mixer.ExpectBand();
    public double Volume { get => _mixer.Volume; set => _mixer.Volume = value; }
    public bool Muted { get => _mixer.Muted; set => _mixer.Muted = value; }
    public bool Suspended { get => _mixer.Suspended; set => _mixer.Suspended = value; }
    public bool HasDevice => true;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Stop the device's thread before the mixer's decoders go.
        try { _out.Stop(); } catch (Exception) { }
        try { _out.Dispose(); } catch (Exception) { }
        _mixer.Dispose();
    }
}

public static class GameAudio
{
    /// <summary>How the live game gets its sound (tests swap in a recorder). Never throws: no device means silence.</summary>
    public static Func<IGameAudio> Factory { get; set; } = Open;

    public static IGameAudio Open()
    {
        try { return (IGameAudio?)DeviceAudio.TryOpen() ?? new SilentAudio(); }
        catch (Exception) { return new SilentAudio(); }
    }
}
