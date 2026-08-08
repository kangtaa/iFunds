using System.Linq;
using iFunds.Models;
using iFunds.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.UI;

namespace iFunds.Views;

public sealed partial class FundDetailPage : Page
{
    private Fund? _fund;

    public FundDetailPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string code)
        {
            _fund = AppState.Current.Funds.FirstOrDefault(f => f.Code == code);
            Bind();
        }
    }

    private static readonly SolidColorBrush Up = new(Color.FromArgb(255, 0xF1, 0x6A, 0x6D));
    private static readonly SolidColorBrush Down = new(Color.FromArgb(255, 0x3F, 0xC3, 0x85));
    private static Brush PnL(decimal v) => v >= 0 ? Up : Down;

    private void Bind()
    {
        if (_fund is null) return;
        var f = _fund;

        NameText.Text = f.Name;
        CodeText.Text = f.Code + (f.IsHolding ? " · 持有" : "");

        GrowthText.Text = f.GrowthText;
        GrowthText.Foreground = f.GrowthForeground;
        EstTimeText.Text = f.EstimateTime;

        DetailSpark.PointsSource = f.Trend;
        DetailSpark.IsUp = f.IsUp;

        HoldProfitBig.Text = f.HoldingProfitText;
        HoldProfitBig.Foreground = PnL(f.HoldingProfit);
        HoldRateSmall.Text = "收益率 " + f.HoldingProfitRateText;
        HoldRateSmall.Foreground = PnL(f.HoldingProfitRate);

        NetValueText.Text = f.NetValueText;
        CostPriceText.Text = f.CostPriceText;
        SharesText.Text = f.SharesText;
        CostAmountText.Text = f.CostAmountText;
        HoldingAmountText.Text = "¥ " + f.HoldingAmount.ToString("0.00");
        EstProfitText.Text = f.EstimateProfitText;
        EstProfitText.Foreground = PnL(f.EstimateProfit);
        EstTotalText.Text = f.EstimateTotalText;
        HoldRateText.Text = f.HoldingProfitRateText;
        HoldRateText.Foreground = PnL(f.HoldingProfitRate);

        RefreshAlerts();
    }

    private void RefreshAlerts()
    {
        if (_fund is null) return;
        AlertList.ItemsSource = AppState.Current.AlertRules
            .Where(r => r.FundCode == _fund.Code)
            .ToList();
    }

    private void OnAddAlertClick(object sender, RoutedEventArgs e)
    {
        if (_fund is null) return;
        var threshold = (decimal)AlertThresholdBox.Value;
        var dir = AlertDirBox.SelectedIndex == 0 ? AlertDirection.RiseAbove : AlertDirection.FallBelow;
        if (dir == AlertDirection.FallBelow && threshold > 0) threshold = -threshold;

        // 同一只基金同一方向只保留一个：已存在则更新阈值，否则新增
        var existing = AppState.Current.AlertRules
            .FirstOrDefault(r => r.FundCode == _fund.Code && r.Direction == dir);
        if (existing is not null)
        {
            existing.Threshold = threshold;
            AppState.Current.SaveNow();
        }
        else
        {
            AppState.Current.AlertRules.Add(new AlertRule
            {
                FundCode = _fund.Code,
                Direction = dir,
                Threshold = threshold,
            });
        }
        RefreshAlerts();
    }

    private void OnRemoveAlertClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id)
        {
            var rule = AppState.Current.AlertRules.FirstOrDefault(r => r.Id == id);
            if (rule is not null) AppState.Current.AlertRules.Remove(rule);
            RefreshAlerts();
        }
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack) Frame.GoBack();
    }

    private async void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (_fund is null) return;

        var sharesBox = new NumberBox { Header = "持有份额", Value = (double)_fund.Shares, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
        var costBox = new NumberBox { Header = "成本价", Value = (double)_fund.CostPrice, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, SmallChange = 0.01 };
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(sharesBox);
        panel.Children.Add(costBox);

        var dialog = new ContentDialog
        {
            Title = "编辑持仓",
            Content = panel,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            // NumberBox 清空时 Value 为 NaN，按 0 处理
            decimal shares = double.IsNaN(sharesBox.Value) ? 0m : (decimal)sharesBox.Value;
            decimal cost = double.IsNaN(costBox.Value) ? 0m : (decimal)costBox.Value;

            _fund.Shares = shares;
            _fund.CostPrice = cost;
            _fund.IsHolding = shares > 0;

            // 写入常驻持仓并保存（刷新后不会丢）
            AppState.Current.UpdateHolding(_fund.Code, shares, cost, _fund.Name);
            Bind();
        }
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (_fund is null) return;
        AppState.Current.RemoveFund(_fund.Code);
        if (Frame.CanGoBack) Frame.GoBack();
    }
}
