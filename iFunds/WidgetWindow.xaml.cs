using System;
using System.Collections.Generic;
using System.Linq;
using iFunds.Models;
using iFunds.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;

namespace iFunds;

public sealed partial class WidgetWindow : Window
{
    private readonly DispatcherTimer _rotateTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(60) };
    private List<Fund> _holdings = new();
    private int _bigIndex;

    private AppWindow _appWindow = null!;
    private bool _dragging;
    private PointInt32 _winStart;
    private Windows.Foundation.Point _ptrStart;

    public WidgetWindow()
    {
        InitializeComponent();
        ConfigureWindow();
        Reload();

        _rotateTimer.Tick += (_, _) => RotateBig();
        _refreshTimer.Tick += async (_, _) => { await AppState.Current.RefreshAsync(); Reload(); };
        _rotateTimer.Start();
        _refreshTimer.Start();

        // 启动时 Funds 可能还没刷新，主动拉一次填充持仓，确保能轮播
        if (AppState.Current.Funds.Count == 0)
        {
            _ = DispatcherQueue.TryEnqueue(async () =>
            {
                await AppState.Current.RefreshAsync();
                Reload();
            });
        }
    }

    private void ConfigureWindow()
    {
        _appWindow = AppWindow;

        if (_appWindow.Presenter is OverlappedPresenter p)
        {
            p.SetBorderAndTitleBar(false, false);
            p.IsAlwaysOnTop = true;
            p.IsResizable = false;
            p.IsMaximizable = false;
            p.IsMinimizable = false;
        }
        _appWindow.IsShownInSwitchers = false;

        ExtendsContentIntoTitleBar = true;
        SystemBackdrop = null;
        if (Content is FrameworkElement fe)
            fe.RequestedTheme = ElementTheme.Dark;

        double scale = GetDpiScale();
        int w = (int)(260 * scale);
        int h = (int)(34 * scale);
        _appWindow.Resize(new SizeInt32(w, h));

        // 屏幕上方中央（灵动岛位置）
        var area = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
        int x = area.WorkArea.X + (area.WorkArea.Width - w) / 2;
        int y = area.WorkArea.Y + 8;
        _appWindow.Move(new PointInt32(x, y));

        StripWindowBorder();
    }

    /// <summary>清除窗口的系统边框样式（WS_BORDER/WS_DLGFRAME/WS_THICKFRAME），消除 1px 白边。</summary>
    private void StripWindowBorder()
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            int style = GetWindowLong(hwnd, GWL_STYLE);
            style &= ~(WS_BORDER | WS_DLGFRAME | WS_THICKFRAME | WS_CAPTION);
            SetWindowLong(hwnd, GWL_STYLE, style);
            // 通知边框变化生效
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
        }
        catch { }
    }

    private const int GWL_STYLE = -16;
    private const int WS_BORDER = 0x00800000;
    private const int WS_DLGFRAME = 0x00400000;
    private const int WS_CAPTION = 0x00C00000;
    private const int WS_THICKFRAME = 0x00040000;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_FRAMECHANGED = 0x0020;

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int i);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int i, int v);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr h);

    private double GetDpiScale()
    {
        try { return GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0; }
        catch { return 1.0; }
    }

    public void Reload()
    {
        _holdings = AppState.Current.Funds.Where(f => f.IsHolding).ToList();
        // 不重置 _bigIndex，避免每次刷新都跳回第一只、打断轮播
        if (_bigIndex >= _holdings.Count) _bigIndex = 0;
        ShowBig();
    }

    private static readonly Color Up = Color.FromArgb(255, 0xF1, 0x6A, 0x6D);
    private static readonly Color Down = Color.FromArgb(255, 0x3F, 0xC3, 0x85);

    private void ShowBig()
    {
        if (_holdings.Count == 0)
        {
            BigName.Text = "暂无持有基金";
            BigGrowth.Text = "";
            return;
        }
        var f = _holdings[_bigIndex % _holdings.Count];
        BigName.Text = f.Name;
        BigGrowth.Text = f.GrowthText;
        BigGrowth.Foreground = new SolidColorBrush(f.IsUp ? Up : Down);
    }

    private void RotateBig()
    {
        if (_holdings.Count <= 1) return;
        _bigIndex = (_bigIndex + 1) % _holdings.Count;
        ShowBig();
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _dragging = true;
        _winStart = _appWindow.Position;
        GetCursorPos(out var pt);
        _ptrStart = new Windows.Foundation.Point(pt.X, pt.Y);
        RootBorder.CapturePointer(e.Pointer);
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) return;
        GetCursorPos(out var pt);
        int dx = pt.X - (int)_ptrStart.X;
        int dy = pt.Y - (int)_ptrStart.Y;
        _appWindow.Move(new PointInt32(_winStart.X + dx, _winStart.Y + dy));
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _dragging = false;
        RootBorder.ReleasePointerCapture(e.Pointer);
        SnapToEdge();
    }

    private void SnapToEdge()
    {
        try
        {
            var area = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
            var wa = area.WorkArea;
            var pos = _appWindow.Position;
            var size = _appWindow.Size;
            const int threshold = 30;
            const int margin = 6;

            int x = pos.X, y = pos.Y;
            if (x - wa.X <= threshold) x = wa.X + margin;
            else if ((wa.X + wa.Width) - (x + size.Width) <= threshold) x = wa.X + wa.Width - size.Width - margin;
            if (y - wa.Y <= threshold) y = wa.Y + margin;
            else if ((wa.Y + wa.Height) - (y + size.Height) <= threshold) y = wa.Y + wa.Height - size.Height - margin;

            _appWindow.Move(new PointInt32(x, y));
        }
        catch { }
    }

    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        => App.Shell?.ShowFromWidget();

    public void Cleanup()
    {
        _rotateTimer.Stop();
        _refreshTimer.Stop();
    }
}
