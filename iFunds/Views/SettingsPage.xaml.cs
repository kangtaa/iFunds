using System;
using System.IO;
using System.Threading.Tasks;
using iFunds.Models;
using iFunds.Services;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace iFunds.Views;

public sealed partial class SettingsPage : Page
{
    public AppSettings Settings => AppState.Current.Settings;

    private bool _initializingStartup;

    public SettingsPage()
    {
        InitializeComponent();
        // 用注册表真实状态初始化开关，避免初始化时误写
        _initializingStartup = true;
        StartupToggle.IsOn = StartupService.IsEnabled();
        AppState.Current.Settings.RunAtStartup = StartupToggle.IsOn;
        _initializingStartup = false;
    }

    private void OnStartupToggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_initializingStartup) return;
        if (sender is ToggleSwitch ts)
        {
            StartupService.Set(ts.IsOn);
            AppState.Current.Settings.RunAtStartup = ts.IsOn;
        }
    }

    private void OnWidgetToggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch ts) return;
        if (ts.IsOn) App.ShowWidget();
        else App.HideWidget();
    }

    private nint Hwnd => WinRT.Interop.WindowNative.GetWindowHandle(App.Shell);

    private async void OnDownloadTemplate(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = "ifunds_template",
        };
        picker.FileTypeChoices.Add("JSON 文件", new System.Collections.Generic.List<string> { ".json" });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Hwnd);

        var file = await picker.PickSaveFileAsync();
        if (file is null) return;

        await FileIO.WriteTextAsync(file, FundImportService.BuildTemplateJson());
        await Info("已保存", "模板已保存，可用记事本或编辑器按样例填写后导入。");
    }

    private async void OnImportFile(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeFilter.Add(".json");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        var text = await FileIO.ReadTextAsync(file);
        var parsed = FundImportService.Parse(text);
        if (parsed is null)
        {
            await Info("导入失败", "文件格式不正确，请使用下载的模板格式。");
            return;
        }

        int n = await FundImportService.ApplyAsync(parsed);
        App.RefreshWidget();
        await Info("导入完成", $"成功导入 {n} 只基金，可在首页查看。");
    }

    private async void OnExportBackup(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"ifunds_backup_{DateTime.Now:yyyyMMdd}",
        };
        picker.FileTypeChoices.Add("JSON 文件", new System.Collections.Generic.List<string> { ".json" });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Hwnd);

        var file = await picker.PickSaveFileAsync();
        if (file is null) return;

        await FileIO.WriteTextAsync(file, FundImportService.BuildExportJson());
        await Info("已导出", "当前自选、持仓与提醒已导出为 JSON 备份，可用于以后导入恢复。");
    }

    private async Task Info(string title, string content)
    {
        var dlg = new ContentDialog
        {
            Title = title,
            Content = content,
            CloseButtonText = "好",
            XamlRoot = this.XamlRoot,
        };
        await dlg.ShowAsync();
    }
}
