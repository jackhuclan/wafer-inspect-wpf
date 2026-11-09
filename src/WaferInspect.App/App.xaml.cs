using System;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WaferInspect.Core.DependencyInjection;
using WaferInspectHMI.Navigation;
using WaferInspectHMI.Services;
using WaferInspectHMI.ViewModels;
using WaferInspectHMI.Views;

namespace WaferInspectHMI;

/// <summary>
/// 应用组合根：Generic Host 负责服务注册、日志、1 Hz 数据泵的生命周期与释放；
/// 主窗口 / ViewModel 全部由容器解析，不再在视图里 new 依赖。
/// </summary>
public partial class App : Application
{
    private IHost _host;

    public App()
    {
        // 启动期与 UI 线程异常必须可见，避免「构建通过但静默退出」重演。
        DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _host = Host.CreateDefaultBuilder(e.Args)
                // GUI 应用不向控制台写常规日志；告警及以上仍走已注册的 ILogger<T>。
                .ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning))
                .ConfigureServices(services =>
                {
                    services.AddWaferInspectCore();
                    services.AddSingleton<ILiveDataPump, DispatcherTimerLiveDataPump>();
                    services.AddSingleton<INavGroupFactory, NavGroupFactory>();
                    services.AddSingleton<MainViewModel>();
                    services.AddSingleton<MainWindow>();
                    services.AddHostedService<SimulationHostedService>();
                })
                .Build();

            _host.Start();

            // StartupUri 已移除：窗口由容器构造并显式 Show，ShutdownMode 绑定到主窗口。
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            MainWindow = _host.Services.GetRequiredService<MainWindow>();
            MainWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"应用启动失败：{ex.Message}\n\n{ex}",
                "WaferInspect 启动错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_host != null)
        {
            _host.StopAsync().GetAwaiter().GetResult();
            _host.Dispose();
            _host = null;
        }

        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"未处理的界面异常：{e.Exception.Message}",
            "WaferInspect 运行错误",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
