using CommunityToolkit.Mvvm.ComponentModel;

namespace iFunds.Models;

/// <summary>
/// 全局设置项。后续可序列化到本地文件做持久化。
/// </summary>
public partial class AppSettings : ObservableObject
{
    /// <summary>自动刷新间隔（秒）</summary>
    [ObservableProperty]
    private int _refreshIntervalSeconds = 60;

    /// <summary>是否启用自动刷新</summary>
    [ObservableProperty]
    private bool _autoRefresh = true;

    /// <summary>红涨绿跌（true=A股习惯；false=欧美习惯绿涨红跌）</summary>
    [ObservableProperty]
    private bool _redUpGreenDown = true;

    /// <summary>开机自启</summary>
    [ObservableProperty]
    private bool _runAtStartup;

    /// <summary>关闭按钮最小化到托盘（false=直接退出）</summary>
    [ObservableProperty]
    private bool _minimizeToTrayOnClose = true;

    /// <summary>启用桌面悬浮小组件</summary>
    [ObservableProperty]
    private bool _showDesktopWidget;

    /// <summary>隐藏金额（小眼睛，统一打码收益/金额/数量）</summary>
    [ObservableProperty]
    private bool _hideAmounts;

    /// <summary>启用涨跌幅提醒的系统通知</summary>
    [ObservableProperty]
    private bool _enableAlertNotifications = true;
}
