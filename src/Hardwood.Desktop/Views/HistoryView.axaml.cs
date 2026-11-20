using System.ComponentModel;
using Avalonia.Controls;
using Hardwood.Desktop.ViewModels;

namespace Hardwood.Desktop.Views;

public partial class HistoryView : UserControl
{
    private HistoryViewModel? _vm;

    public HistoryView()
    {
        InitializeComponent();
        // K14 (run 135): the table's award columns read the college awards in a college career (DataGrid headers don't bind).
        DataContextChanged += (_, _) =>
        {
            if (_vm != null) _vm.PropertyChanged -= OnVmChanged;
            _vm = DataContext as HistoryViewModel;
            if (_vm != null) _vm.PropertyChanged += OnVmChanged;
            Headers();
        };
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HistoryViewModel.MvpHeader)) Headers();
    }

    private void Headers()
    {
        if (_vm == null) return;
        SeasonsTable.Columns[3].Header = _vm.MvpHeader;
        SeasonsTable.Columns[4].Header = _vm.FinalsMvpHeader;
    }
}
