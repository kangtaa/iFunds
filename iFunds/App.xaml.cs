using System;
using System.Linq;
using Microsoft.UI.Xaml;

namespace iFunds;

public partial class App : Application
{
    public static MainWindow? Shell { get; private set; }
    public static WidgetWindow? Widget { get; private set; }

    public static bool LaunchedAtStartup { get; private set; }

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // 检测是否随系统启动：非打包看 --startup 参数；打包看 StartupTask 激活类型
        var cmd = Environment.GetCommandLineArgs();
        LaunchedAtStartup = cmd.Any(a => a.Contains("--startup", StringComparison.OrdinalIgnoreCase));
        if (Services.PackageInfo.IsPackaged)
        {
            try
            {
                var kind = Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent()
                    .GetActivatedEventArgs()?.Kind;
                if (kind == Microsoft.Windows.AppLifecycle.ExtendedActivationKind.StartupTask)
                    LaunchedAtStartup = true;
            }
            catch { }
        }

        Services.PersistenceService.Load();
        Services.AlertService.EnsureRegistered();

        Shell = new MainWindow();
        if (LaunchedAtStartup && Services.AppState.Current.Settings.MinimizeToTrayOnClose)
            Shell.StartHiddenToTray();
        else
            Shell.Activate();

        if (Services.AppState.Current.Settings.ShowDesktopWidget)
            ShowWidget();

        // 后台预热：提前拉好"全部"榜单缓存，用户打开基金页时直接用，加载更快
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try { await Services.AppState.Current.GetMarketAsync("all"); }
            catch { }
        });
    }

    public static void ShowWidget()
    {
        if (Widget is not null) return;
        Widget = new WidgetWindow();
        Widget.Closed += (_, _) => Widget = null;
        Widget.Activate();
    }

    public static void HideWidget()
    {
        if (Widget is null) return;
        Widget.Cleanup();
        Widget.Close();
        Widget = null;
    }

    public static void RefreshWidget() => Widget?.Reload();
}
