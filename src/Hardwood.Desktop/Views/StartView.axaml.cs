using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Hardwood.Desktop.ViewModels;

namespace Hardwood.Desktop.Views;

public partial class StartView : UserControl
{
    public StartView() => InitializeComponent();

    private async void BrowseFaces(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not StartViewModel vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp) return;
        var start = Directory.Exists(vm.FaceFolder) ? await sp.TryGetFolderFromPathAsync(vm.FaceFolder) : null;
        var picked = await sp.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose your FMNEWGANv2 face pack folder", SuggestedStartLocation = start });
        if (picked.Count > 0 && picked[0].TryGetLocalPath() is { } path) vm.FaceFolder = path;
    }
}
