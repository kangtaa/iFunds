using System.Linq;
using iFunds.Services;
using iFunds.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace iFunds.Views;

public sealed partial class FundsPage : Page
{
    public FundRankViewModel ViewModel { get; } = new();

    public FundsPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadAsync();
    }

    private async void OnCategoryChecked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb || rb.Tag is not string ft) return;
        if (!IsLoaded) return; // 初始 IsChecked=True 触发时页面还没好，跳过
        await ViewModel.LoadAsync(ft);
    }

    private async void OnWatchClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string code) return;
        if (AppState.Current.IsWatched(code)) return;

        bool ok = await AppState.Current.AddFundAsync(code);
        if (ok)
        {
            var f = ViewModel.Items.FirstOrDefault(x => x.Code == code);
            if (f is not null) f.Watched = true;
            App.RefreshWidget();
        }
    }
}
