using Avalonia.Controls;
using Avalonia.Input;
using Hardwood.Desktop.ViewModels;

namespace Hardwood.Desktop.Views;

public partial class ScheduleView : UserControl
{
    public ScheduleView() => InitializeComponent();

    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ScheduleViewModel vm && vm.Selected != null) vm.OpenBox(vm.Selected);
    }
}
