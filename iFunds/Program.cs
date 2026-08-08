using System;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppLifecycle;

namespace iFunds;

public static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        // 单实例：在创建任何窗口/托盘之前完成判断
        var keyInstance = AppInstance.FindOrRegisterForKey("iFunds-main");
        if (!keyInstance.IsCurrent)
        {
            // 已有实例：把本次激活重定向过去，然后直接退出（不创建 App/托盘）
            var activated = AppInstance.GetCurrent().GetActivatedEventArgs();
            keyInstance.RedirectActivationToAsync(activated).AsTask().Wait();
            return;
        }

        keyInstance.Activated += OnRedirected;

        Microsoft.UI.Xaml.Application.Start((p) =>
        {
            var context = new DispatcherQueueSynchronizationContext(
                DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
    }

    private static void OnRedirected(object? sender, AppActivationArguments e)
    {
        App.Shell?.DispatcherQueue.TryEnqueue(() => App.Shell?.ShowFromWidget());
    }
}
