using System;
using Microsoft.Win32;

namespace iFunds.Services;

/// <summary>开机自启：MSIX 用 StartupTask，非打包用注册表 + StartupApproved。</summary>
public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "iFunds";
    private const string TaskId = "iFundsStartup"; // 需与 Package.appxmanifest 中一致

    public static void Set(bool enable)
    {
        if (PackageInfo.IsPackaged)
            SetPackaged(enable);
        else
            SetRegistry(enable);
    }

    public static bool IsEnabled()
        => PackageInfo.IsPackaged ? IsEnabledPackaged() : IsEnabledRegistry();

    // ── MSIX：StartupTask ──
    private static void SetPackaged(bool enable)
    {
        try
        {
            var task = Windows.ApplicationModel.StartupTask.GetAsync(TaskId).GetAwaiter().GetResult();
            if (enable)
            {
                if (task.State == Windows.ApplicationModel.StartupTaskState.Disabled)
                    _ = task.RequestEnableAsync().GetAwaiter().GetResult();
            }
            else
            {
                task.Disable();
            }
        }
        catch { }
    }

    private static bool IsEnabledPackaged()
    {
        try
        {
            var task = Windows.ApplicationModel.StartupTask.GetAsync(TaskId).GetAwaiter().GetResult();
            return task.State is Windows.ApplicationModel.StartupTaskState.Enabled
                or Windows.ApplicationModel.StartupTaskState.EnabledByPolicy;
        }
        catch { return false; }
    }

    // ── 非打包：注册表 ──
    private static void SetRegistry(bool enable)
    {
        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                             ?? Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (key is null) return;
                if (enable)
                {
                    var exe = Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(exe))
                        key.SetValue(ValueName, $"\"{exe}\" --startup");
                }
                else if (key.GetValue(ValueName) is not null)
                {
                    key.DeleteValue(ValueName, throwOnMissingValue: false);
                }
            }
            SyncApproved(enable);
        }
        catch { }
    }

    private static void SyncApproved(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(ApprovedKey);
            if (key is null) return;
            var data = new byte[12];
            data[0] = (byte)(enable ? 0x02 : 0x03);
            key.SetValue(ValueName, data, RegistryValueKind.Binary);
        }
        catch { }
    }

    private static bool IsEnabledRegistry()
    {
        try
        {
            using (var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey))
            {
                if (approved?.GetValue(ValueName) is byte[] b && b.Length > 0 && b[0] == 0x03)
                    return false;
            }
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is not null;
        }
        catch { return false; }
    }
}
