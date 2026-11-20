using Avalonia.Controls;
using Hardwood.Desktop.ViewModels;

namespace Hardwood.Desktop.Views;

public partial class HistoryView : UserControl
{
    public HistoryView()
    {
        InitializeComponent();
        // K14 (run 135): the table's award columns read the college awards in a college career (DataGrid headers don't bind).
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not HistoryViewModel vm) return;
            void Headers() { SeasonsTable.Columns[3].Header = vm.MvpHeader; SeasonsTable.Columns[4].Header = vm.FinalsMvpHeader; }
            Headers();
            vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(HistoryViewModel.MvpHeader)) Headers(); };
        };
    }
}
