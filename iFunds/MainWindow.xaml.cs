using System;
using iFunds.Services;
using iFunds.Views;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace iFunds;

public sealed partial class MainWindow : Window
{
    private bool _reallyExit;

    public MainWindow()
    {
        InitializeComponent();
        Title = "iFunds";
        ConfigureWindow();
        SetupTitleBar();

        AppWindow.Closing += OnWindowClosing;
        TrayIcon.LeftClickCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(RestoreWindow);
        MenuShow.Command = new CommunityToolkit.Mvvm.Input.RelayCommand(RestoreWindow);
        MenuExit.Command = new CommunityToolkit.Mvvm.Input.RelayCommand(ExitApp);

        ContentFrame.Navigate(typeof(DashboardPage));
    }

    private void ConfigureWindow()
    {
        double scale = GetDpiScale();
        AppWindow.Resize(new SizeInt32((int)(1120 * scale), (int)(740 * scale)));
        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.IsResizable = true;
            p.IsMaximizable = true;
        }
        // 任务栏 / 标题栏图标
        try { AppWindow.SetIcon("Assets\\iFunds.ico"); } catch { }
    }

    /// <summary>一体化标题栏：延伸内容、透明按钮、自定义拖动区。</summary>
    private void SetupTitleBar()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarDragArea);

        var tb = AppWindow.TitleBar;
        tb.ButtonBackgroundColor = Colors.Transparent;
        tb.ButtonInactiveBackgroundColor = Colors.Transparent;
        tb.ButtonForegroundColor = Windows.UI.Color.FromArgb(255, 0xED, 0xEE, 0xF0);
        tb.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(255, 0x6E, 0x71, 0x78);
        tb.ButtonHoverForegroundColor = Colors.White;
        tb.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(255, 0x2A, 0x2B, 0x33);
        tb.ButtonPressedForegroundColor = Colors.White;
        tb.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(255, 0x33, 0x34, 0x3D);
    }

    private double GetDpiScale()
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            return GetDpiForWindow(hwnd) / 96.0;
        }
        catch { return 1.0; }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private void OnNavChecked(object sender, RoutedEventArgs e)
    {
        if (ContentFrame is null) return;
        if (sender is not RadioButton rb) return;
        switch (rb.Tag as string)
        {
            case "dashboard": ContentFrame.Navigate(typeof(DashboardPage)); break;
            case "funds": ContentFrame.Navigate(typeof(FundsPage)); break;
            case "market": ContentFrame.Navigate(typeof(PlaceholderPage), "行情"); break;
            case "holdings": ContentFrame.Navigate(typeof(PlaceholderPage), "持仓"); break;
            case "analysis": ContentFrame.Navigate(typeof(PlaceholderPage), "分析"); break;
            case "account": ContentFrame.Navigate(typeof(PlaceholderPage), "账户"); break;
            case "settings": ContentFrame.Navigate(typeof(SettingsPage)); break;
        }
    }

    private void OnWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_reallyExit) return;
        if (AppState.Current.Settings.MinimizeToTrayOnClose)
        {
            args.Cancel = true;
            AppWindow.Hide();
        }
    }

    private void RestoreWindow()
    {
        AppWindow.Show();
        if (AppWindow.Presenter is OverlappedPresenter p) p.Restore();
        this.Activate();
        // 置前
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        SetForegroundWindow(hwnd);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private void ExitApp()
    {
        _reallyExit = true;
        App.HideWidget();
        Services.AlertService.Unregister();
        try { TrayIcon.Dispose(); } catch { }
        Microsoft.UI.Xaml.Application.Current.Exit();
    }

    public void NavigateToDetail(string code)
        => ContentFrame.Navigate(typeof(FundDetailPage), code);

    public void ShowFromWidget() => RestoreWindow();

    /// <summary>随系统启动时静默驻留托盘：激活以创建托盘图标，但隐藏窗口。</summary>
    public void StartHiddenToTray()
    {
        this.Activate();
        AppWindow.Hide();
    }
}
