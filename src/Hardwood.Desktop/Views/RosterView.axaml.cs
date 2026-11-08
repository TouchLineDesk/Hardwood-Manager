using Avalonia.Controls;
using Avalonia.Input;
using Hardwood.Desktop.ViewModels;

namespace Hardwood.Desktop.Views;

public partial class RosterView : UserControl
{
    public RosterView() => InitializeComponent();

    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is RosterViewModel vm && vm.Selected != null) vm.Open(vm.Selected);
    }
}
