using System;
using System.Collections.Generic;
using System.Linq;
using iFunds.Models;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace iFunds.Services;

/// <summary>检查涨跌幅提醒并弹出系统通知。</summary>
public static class AlertService
{
    private static readonly HashSet<string> _firedToday = new();
    private static bool _registered;

    public static void EnsureRegistered()
    {
        if (_registered) return;
        try
        {
            AppNotificationManager.Default.Register();
            _registered = true;
        }
        catch { }
    }

    public static void Unregister()
    {
        try { AppNotificationManager.Default.Unregister(); } catch { }
    }

    /// <summary>遍历提醒规则，命中则弹通知（同一规则当次会话只弹一次）。</summary>
    public static void CheckAndNotify()
    {
        var state = AppState.Current;
        if (!state.Settings.EnableAlertNotifications) return;
        foreach (var rule in state.AlertRules.ToList())
        {
            var fund = state.Funds.FirstOrDefault(f => f.Code == rule.FundCode);
            if (fund is null) continue;

            bool hit = rule.Direction switch
            {
                AlertDirection.RiseAbove => fund.GrowthRate >= rule.Threshold,
                AlertDirection.FallBelow => fund.GrowthRate <= rule.Threshold,
                _ => false
            };
            if (!hit) continue;

            var key = $"{rule.Id}";
            if (_firedToday.Contains(key)) continue;
            _firedToday.Add(key);

            Notify(fund, rule);
        }
    }

    private static void Notify(Fund fund, AlertRule rule)
    {
        try
        {
            string dir = rule.Direction == AlertDirection.RiseAbove ? "涨幅" : "跌幅";
            string logoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "iFunds_notify.png");

            var builder = new AppNotificationBuilder()
                .AddText($"{fund.Name}")
                .AddText($"当前{fund.GrowthText}，已触发{dir}提醒（阈值 {Math.Abs(rule.Threshold):0.##}%）")
                .SetAudioEvent(AppNotificationSoundEvent.Default)
                .SetTimeStamp(DateTime.Now);

            if (System.IO.File.Exists(logoPath))
                builder.SetAppLogoOverride(new Uri($"file:///{logoPath.Replace('\\', '/')}"), AppNotificationImageCrop.Circle);

            AppNotificationManager.Default.Show(builder.BuildNotification());
        }
        catch { }
    }

    /// <summary>清空已弹记录（如手动刷新后想重新提醒可调用）。</summary>
    public static void ResetFired() => _firedToday.Clear();
}
