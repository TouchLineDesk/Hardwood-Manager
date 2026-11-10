using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Hardwood.Desktop.ViewModels;

namespace Hardwood.Desktop.Views;

public partial class GraphicsSettingsView : UserControl
{
    public GraphicsSettingsView() => InitializeComponent();

    private async Task<string?> Pick(string current, string title)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp) return null;
        var start = Directory.Exists(current) ? await sp.TryGetFolderFromPathAsync(current) : null;
        var picked = await sp.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, SuggestedStartLocation = start });
        return picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
    }

    private async void BrowseFaces(object? sender, RoutedEventArgs e)
    {
        if (DataContext is GraphicsSettingsViewModel vm && await Pick(vm.FaceFolder, "Choose your FMNEWGANv2 face pack folder") is { } path) vm.FaceFolder = path;
    }

    private async void BrowseLogos(object? sender, RoutedEventArgs e)
    {
        if (DataContext is GraphicsSettingsViewModel vm && await Pick(vm.LogoFolder, "Choose your logo pack folder") is { } path) vm.LogoFolder = path;
    }
}
