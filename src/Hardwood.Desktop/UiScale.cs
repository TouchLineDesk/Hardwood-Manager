using Avalonia.Media;

namespace Hardwood.Desktop;

/// <summary>
/// The text and interface size setting (roadmap 1): one scale for the whole window, so every font, chart and column
/// grows together and pages reflow rather than clip. Stored in <see cref="AppSettings.UiScale"/>.
/// </summary>
public static class UiScale
{
    public const double Min = 0.9, Max = 1.3;
    public static readonly int[] Percents = { 90, 100, 110, 120, 130 };

    /// <summary>Raised when the size changes, so open windows can rescale.</summary>
    public static event Action<double>? Changed;

    public static double Clamp(double scale) => double.IsFinite(scale) ? Math.Clamp(scale, Min, Max) : 1.0;

    public static double Current => Clamp(AppSettings.Current.UiScale);

    public static ITransform Transform(double scale) => new ScaleTransform(Clamp(scale), Clamp(scale));

    public static void Set(double scale)
    {
        scale = Clamp(scale);
        var settings = AppSettings.Current;
        if (Math.Abs(settings.UiScale - scale) < 0.001) return;
        settings.UiScale = scale;
        settings.Save();
        Changed?.Invoke(scale);
    }

    public static string Label(int percent) => percent == 100 ? "100% (standard)" : $"{percent}%";
}
