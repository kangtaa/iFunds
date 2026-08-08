using System;
using iFunds.Services;
using iFunds.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace iFunds.Views;

public sealed partial class DashboardPage : Page
{
    public DashboardViewModel ViewModel { get; } = new();

    public DashboardPage()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            await ViewModel.RefreshAsync();
            Heatmap.Cells = System.Linq.Enumerable.ToList(ViewModel.HeatCells);
        };
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.RefreshAsync();
        Heatmap.Cells = System.Linq.Enumerable.ToList(ViewModel.HeatCells);
    }

    private void OnToggleHide(object sender, RoutedEventArgs e)
        => ViewModel.ToggleHideCommand.Execute(null);

    private void OnPeriodChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb || rb.Tag is not string tag) return;
        if (Heatmap is null) return;
        if (Enum.TryParse<AppState.HeatPeriod>(tag, out var period))
        {
            ViewModel.LoadHeat(period);
            Heatmap.Cells = System.Linq.Enumerable.ToList(ViewModel.HeatCells);
        }
    }

    private void OnFundTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string code && App.Shell is not null)
            App.Shell.NavigateToDetail(code);
    }
}
