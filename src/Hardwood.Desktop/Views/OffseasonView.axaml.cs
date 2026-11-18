using Avalonia.Controls;
using Avalonia.Interactivity;
using Hardwood.Desktop.ViewModels;

namespace Hardwood.Desktop.Views;

public partial class OffseasonView : UserControl
{
    public OffseasonView()
    {
        InitializeComponent();
        // H93 (run 106): a college roster has no contracts, so its Salary and Yrs columns were all "-". Hide them then.
        if (SummerRosterGrid.ItemsSource is System.Collections.Specialized.INotifyCollectionChanged live)
            live.CollectionChanged += (_, _) => ShowContractColumns(SummerRosterGrid);
        SummerRosterGrid.PropertyChanged += (_, e) =>
        {
            if (e.Property != DataGrid.ItemsSourceProperty) return;
            if (SummerRosterGrid.ItemsSource is System.Collections.Specialized.INotifyCollectionChanged l) l.CollectionChanged += (_, _) => ShowContractColumns(SummerRosterGrid);
            ShowContractColumns(SummerRosterGrid);
        };
    }

    /// <summary>Salary and Yrs show only when some player on the list has a contract (public for the tests).</summary>
    public static void ShowContractColumns(DataGrid grid)
    {
        bool any = grid.ItemsSource is IEnumerable<PlayerRow> rows && rows.Any(r => r.Player.Contract != null);
        foreach (var col in grid.Columns.Where(c => c.Header as string is "Salary" or "Yrs")) col.IsVisible = any;
    }

    /// <summary>Run 67: a card on Contracts up opens talks with him.</summary>
    private void OnExpiringClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is OffseasonViewModel vm && sender is Button { DataContext: ExpiringRow row }) vm.PickExpiring(row);
    }

    /// <summary>Run 67: a free agent's card opens talks with him.</summary>
    private void OnFreeAgentClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is OffseasonViewModel vm && sender is Button { DataContext: FreeAgentRow row }) vm.PickFreeAgent(row);
    }

    /// <summary>Run 67: a graduate's card puts him top of your board.</summary>
    private void OnProspectClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is OffseasonViewModel vm && sender is Button { DataContext: ProspectRow row }) vm.PickProspect(row);
    }
}
