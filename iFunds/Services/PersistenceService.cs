using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using iFunds.Models;

namespace iFunds.Services;

/// <summary>把自选/持仓/提醒/设置持久化到 %LocalAppData%\iFunds\data.json。</summary>
public static class PersistenceService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "iFunds");
    private static string FilePath => Path.Combine(Dir, "data.json");

    public class PersistFund
    {
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public decimal Shares { get; set; }
        public decimal CostPrice { get; set; }
        public bool IsHolding { get; set; }
    }

    public class PersistAlert
    {
        public string Id { get; set; } = "";
        public string FundCode { get; set; } = "";
        public int Direction { get; set; }
        public decimal Threshold { get; set; }
        public bool Enabled { get; set; } = true;
    }

    public class PersistRoot
    {
        public List<string> WatchCodes { get; set; } = new();
        public List<PersistFund> Funds { get; set; } = new();
        public List<PersistAlert> Alerts { get; set; } = new();
        public PersistSettings Settings { get; set; } = new();
    }

    public class PersistSettings
    {
        public int RefreshIntervalSeconds { get; set; } = 60;
        public bool AutoRefresh { get; set; } = true;
        public bool MinimizeToTrayOnClose { get; set; } = true;
        public bool ShowDesktopWidget { get; set; }
        public bool HideAmounts { get; set; }
        public bool EnableAlertNotifications { get; set; } = true;
    }

    private static bool _loaded;

    /// <summary>启动时调用：若存在存档则覆盖 AppState 的默认值。</summary>
    public static void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) { _loaded = true; return; }
            var root = JsonSerializer.Deserialize<PersistRoot>(File.ReadAllText(FilePath), Options);
            if (root is null) { _loaded = true; return; }

            var state = AppState.Current;

            // 自选代码
            state.WatchCodes.Clear();
            foreach (var c in root.WatchCodes.Distinct())
                state.WatchCodes.Add(c);

            // 持仓覆盖信息暂存，待刷新后套用
            state.PendingPersistedFunds = root.Funds
                .ToDictionary(f => f.Code, f => f);

            // 提醒
            state.AlertRules.Clear();
            foreach (var a in root.Alerts)
            {
                state.AlertRules.Add(new AlertRule
                {
                    Id = string.IsNullOrEmpty(a.Id) ? Guid.NewGuid().ToString("N") : a.Id,
                    FundCode = a.FundCode,
                    Direction = (AlertDirection)a.Direction,
                    Threshold = a.Threshold,
                    Enabled = a.Enabled,
                });
            }

            // 设置
            var s = state.Settings;
            s.RefreshIntervalSeconds = root.Settings.RefreshIntervalSeconds;
            s.AutoRefresh = root.Settings.AutoRefresh;
            s.MinimizeToTrayOnClose = root.Settings.MinimizeToTrayOnClose;
            s.ShowDesktopWidget = root.Settings.ShowDesktopWidget;
            s.HideAmounts = root.Settings.HideAmounts;
            s.EnableAlertNotifications = root.Settings.EnableAlertNotifications;
        }
        catch { }
        _loaded = true;
    }

    /// <summary>保存当前状态到磁盘。</summary>
    public static void Save()
    {
        if (!_loaded) return; // 避免加载完成前的中途写入
        try
        {
            Directory.CreateDirectory(Dir);
            var state = AppState.Current;
            var root = new PersistRoot
            {
                WatchCodes = state.WatchCodes.ToList(),
                Funds = state.Funds.Select(f => new PersistFund
                {
                    Code = f.Code,
                    Name = f.Name,
                    Shares = f.Shares,
                    CostPrice = f.CostPrice,
                    IsHolding = f.IsHolding,
                }).ToList(),
                Alerts = state.AlertRules.Select(a => new PersistAlert
                {
                    Id = a.Id,
                    FundCode = a.FundCode,
                    Direction = (int)a.Direction,
                    Threshold = a.Threshold,
                    Enabled = a.Enabled,
                }).ToList(),
                Settings = new PersistSettings
                {
                    RefreshIntervalSeconds = state.Settings.RefreshIntervalSeconds,
                    AutoRefresh = state.Settings.AutoRefresh,
                    MinimizeToTrayOnClose = state.Settings.MinimizeToTrayOnClose,
                    ShowDesktopWidget = state.Settings.ShowDesktopWidget,
                    HideAmounts = state.Settings.HideAmounts,
                    EnableAlertNotifications = state.Settings.EnableAlertNotifications,
                },
            };
            File.WriteAllText(FilePath, JsonSerializer.Serialize(root, Options));
        }
        catch { }
    }
}
