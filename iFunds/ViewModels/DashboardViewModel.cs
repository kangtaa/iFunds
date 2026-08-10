using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iFunds.Models;
using iFunds.Services;

namespace iFunds.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly AppState _state = AppState.Current;

    public ObservableCollection<Fund> Funds => _state.Funds;
    public ObservableCollection<HeatCell> HeatCells { get; } = new();

    [ObservableProperty] private bool _isRefreshing;
    [ObservableProperty] private string _refreshTime = "--:--:--";
    [ObservableProperty] private string _profitLabel = "收益";

    private void UpdateProfitLabel()
    {
        var dateStr = Funds.FirstOrDefault(f => !string.IsNullOrEmpty(f.NetValueDate))
                         ?.NetValueDate ?? "MM-dd";
        ProfitLabel = $"{DateTime.Now.Year}/{dateStr} 收益";
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalProfitText))]
    [NotifyPropertyChangedFor(nameof(TodayRateText))]
    private decimal _totalProfit;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalAmountText))]
    [NotifyPropertyChangedFor(nameof(HoldRateText))]
    private decimal _totalAmount;

    /// <summary>持有总成本（用于算持有收益率）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HoldRateText))]
    private decimal _totalCost;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WatchCountText))]
    private int _watchCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AlertCountText))]
    private int _alertCount;

    /// <summary>小眼睛：隐藏金额</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalProfitText))]
    [NotifyPropertyChangedFor(nameof(TotalAmountText))]
    [NotifyPropertyChangedFor(nameof(WatchCountText))]
    [NotifyPropertyChangedFor(nameof(AlertCountText))]
    [NotifyPropertyChangedFor(nameof(TodayRateText))]
    [NotifyPropertyChangedFor(nameof(HoldRateText))]
    [NotifyPropertyChangedFor(nameof(EyeGlyph))]
    private bool _hideAmounts;

    private const string Mask = "****";

    public string TotalProfitText => HideAmounts ? Mask : (TotalProfit >= 0 ? "+" : "") + TotalProfit.ToString("0.00");
    public string TotalAmountText => HideAmounts ? Mask : TotalAmount.ToString("#,##0.00");
    public string WatchCountText => HideAmounts ? Mask : WatchCount.ToString();
    public string AlertCountText => HideAmounts ? Mask : AlertCount.ToString();

    /// <summary>今日收益率 = 今日收益 / 昨日市值（昨日市值≈今日市值−今日收益）</summary>
    public string TodayRateText
    {
        get
        {
            if (HideAmounts) return "";
            decimal baseAmt = TotalAmount - TotalProfit;
            if (baseAmt <= 0) return "";
            decimal rate = TotalProfit / baseAmt * 100m;
            return (rate >= 0 ? "+" : "") + rate.ToString("0.00") + "%";
        }
    }

    /// <summary>持有收益率 = (市值 − 成本) / 成本</summary>
    public string HoldRateText
    {
        get
        {
            if (HideAmounts) return "";
            if (TotalCost <= 0) return "";
            decimal rate = (TotalAmount - TotalCost) / TotalCost * 100m;
            return (rate >= 0 ? "+" : "") + rate.ToString("0.00") + "%";
        }
    }

    /// <summary>睁眼 / 闭眼 图标</summary>
    public string EyeGlyph => HideAmounts ? "\uED1A" : "\uE7B3"; // 闭眼 / 睁眼

    public DashboardViewModel()
    {
        HideAmounts = _state.Settings.HideAmounts;
        UpdateProfitLabel();
        LoadHeat(AppState.HeatPeriod.Day);
    }

    [RelayCommand]
    private void ToggleHide()
    {
        HideAmounts = !HideAmounts;
        _state.Settings.HideAmounts = HideAmounts;
    }

    private AppState.HeatPeriod _currentPeriod = AppState.HeatPeriod.Day;

    public void LoadHeat(AppState.HeatPeriod period)
    {
        _currentPeriod = period;
        HeatCells.Clear();
        foreach (var c in _state.BuildHeatCells(period))
            HeatCells.Add(c);
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsRefreshing) return;
        IsRefreshing = true;
        try
        {
            await _state.RefreshAsync();
            TotalProfit = Funds.Sum(f => f.TodayProfit);
            TotalAmount = Funds.Sum(f => f.HoldingAmount);
            TotalCost = Funds.Sum(f => f.CostPrice * f.Shares);
            WatchCount = Funds.Count;
            AlertCount = _state.AlertRules.Count;
            RefreshTime = DateTime.Now.ToString("HH:mm:ss");
            UpdateProfitLabel();
            Services.AlertService.CheckAndNotify();
            App.RefreshWidget();

            // 拉真实历史，刷新热力图（网络较慢，放在主数据之后）
            await _state.LoadDailyReturnsAsync();
            LoadHeat(_currentPeriod);
        }
        finally
        {
            IsRefreshing = false;
        }
    }
}
