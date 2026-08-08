using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace iFunds.Models;

/// <summary>单只基金的实时数据 + 持仓信息。</summary>
public partial class Fund : ObservableObject
{
    public string Code { get; set; } = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HoldingVisibility))]
    private bool _isHolding;

    /// <summary>最新单位净值</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NetValueText))]
    [NotifyPropertyChangedFor(nameof(HoldingAmount))]
    [NotifyPropertyChangedFor(nameof(HoldingProfit))]
    [NotifyPropertyChangedFor(nameof(HoldingProfitRate))]
    private decimal _netValue;

    /// <summary>前一日单位净值（用于计算真实昨日收益）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TodayProfit))]
    private decimal _prevNetValue;

    /// <summary>净值日期，如 06-03</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NetValueText))]
    private string _netValueDate = string.Empty;

    /// <summary>盘中估算净值</summary>
    [ObservableProperty]
    private decimal _estimateValue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GrowthText))]
    [NotifyPropertyChangedFor(nameof(IsUp))]
    [NotifyPropertyChangedFor(nameof(GrowthForeground))]
    [NotifyPropertyChangedFor(nameof(GrowthBackground))]
    [NotifyPropertyChangedFor(nameof(RowBackground))]
    [NotifyPropertyChangedFor(nameof(TodayProfit))]
    [NotifyPropertyChangedFor(nameof(EstimateProfitText))]
    private decimal _growthRate;

    [ObservableProperty]
    private string _estimateTime = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdatedVisibility))]
    private bool _netValueUpdated;

    /// <summary>持有份额</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HoldingAmount))]
    [NotifyPropertyChangedFor(nameof(HoldingProfit))]
    [NotifyPropertyChangedFor(nameof(HoldingProfitRate))]
    [NotifyPropertyChangedFor(nameof(EstimateTotalText))]
    private decimal _shares;

    /// <summary>成本价（单位成本）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CostAmount))]
    [NotifyPropertyChangedFor(nameof(HoldingProfit))]
    [NotifyPropertyChangedFor(nameof(HoldingProfitRate))]
    private decimal _costPrice;

    /// <summary>盘中分时点（百分比序列），供迷你曲线/小组件使用</summary>
    public List<decimal> Trend { get; set; } = new();

    /// <summary>是否已在自选中（供基金榜按钮显示状态）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WatchButtonText))]
    private bool _watched;

    public string WatchButtonText => Watched ? "✓ 已选" : "+ 自选";

    // ── 实时涨跌 ──
    public string GrowthText => (GrowthRate >= 0 ? "+" : "") + GrowthRate.ToString("0.00") + "%";
    public bool IsUp => GrowthRate >= 0;
    public decimal TodayProfit => Shares * PrevNetValue * (GrowthRate / 100m);

    // ── 净值 / 持仓派生字段 ──
    public string NetValueText => NetValue.ToString("0.0000") + (string.IsNullOrEmpty(NetValueDate) ? "" : $"（{NetValueDate}）");
    public string NetValueOnly => NetValue.ToString("0.0000");
    public decimal HoldingAmount => Shares * NetValue;                 // 持有市值
    public decimal CostAmount => Shares * CostPrice;                   // 成本金额
    public decimal HoldingProfit => (NetValue - CostPrice) * Shares;   // 持有收益
    public decimal HoldingProfitRate => CostPrice > 0 ? (NetValue - CostPrice) / CostPrice * 100m : 0m; // 持有收益率%
    public decimal EstimateProfit => Shares * NetValue * (GrowthRate / 100m); // 估算收益（当日）
    public decimal EstimateTotal => HoldingAmount + EstimateProfit;    // 估算总值

    public string HoldingProfitText => (HoldingProfit >= 0 ? "¥ +" : "¥ ") + HoldingProfit.ToString("0.00");
    public string HoldingProfitRateText => (HoldingProfitRate >= 0 ? "+" : "") + HoldingProfitRate.ToString("0.00") + "%";
    public string CostPriceText => CostPrice.ToString("0.0000");
    public string CostAmountText => "¥ " + CostAmount.ToString("0.00");
    public string EstimateProfitText => (EstimateProfit >= 0 ? "¥ +" : "¥ ") + EstimateProfit.ToString("0.00");
    public string EstimateTotalText => "¥ " + EstimateTotal.ToString("0.00");
    public string SharesText => Shares.ToString("0.00");

    // ── 颜色（红涨绿跌） ──
    private static readonly SolidColorBrush UpBrush = new(Color.FromArgb(255, 0xF1, 0x6A, 0x6D));
    private static readonly SolidColorBrush DownBrush = new(Color.FromArgb(255, 0x3F, 0xC3, 0x85));
    private static readonly SolidColorBrush UpBgBrush = new(Color.FromArgb(255, 0x3A, 0x22, 0x26));
    private static readonly SolidColorBrush DownBgBrush = new(Color.FromArgb(255, 0x1C, 0x33, 0x2A));

    public Brush GrowthForeground => IsUp ? UpBrush : DownBrush;
    public Brush GrowthBackground => IsUp ? UpBgBrush : DownBgBrush;

    /// <summary>整行背景：涨红跌绿，幅度越大越深</summary>
    public Brush RowBackground
    {
        get
        {
            double mag = Math.Min(1.0, (double)Math.Abs(GrowthRate) / 5.0); // 5% 封顶
            byte Lerp(byte from, byte to) => (byte)(from + (to - from) * mag);
            if (IsUp)
            {
                // 由近底色 (#191A20) 向暗红 (#4A2024)
                return new SolidColorBrush(Color.FromArgb(255, Lerp(0x19, 0x4A), Lerp(0x1A, 0x20), Lerp(0x20, 0x24)));
            }
            else
            {
                // 由近底色 (#191A20) 向暗绿 (#173A2C)
                return new SolidColorBrush(Color.FromArgb(255, Lerp(0x19, 0x17), Lerp(0x1A, 0x3A), Lerp(0x20, 0x2C)));
            }
        }
    }

    public Visibility HoldingVisibility => IsHolding ? Visibility.Visible : Visibility.Collapsed;
    public Visibility UpdatedVisibility => NetValueUpdated ? Visibility.Visible : Visibility.Collapsed;
}
