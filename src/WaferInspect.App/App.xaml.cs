using System;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WaferInspect.Core.DependencyInjection;
using WaferInspect.Core.Inspection;
using WaferInspect.Core.Security;
using WaferInspectHMI.Navigation;
using WaferInspectHMI.Services;
using WaferInspectHMI.ViewModels;
using WaferInspectHMI.ViewModels.Modules;
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
                    services.AddSingleton<IH01DialogService, H01DialogService>();
                    services.AddSingleton<IH02DialogService, H02DialogService>();
                    // H04 参数验证页：窗口由工厂组装，对话框服务只负责打开；关闭自动保存在窗口 Closing 中触发。
                    services.AddSingleton<IH04DialogService, H04DialogService>();
                    services.AddSingleton<IH05DialogService, H05DialogService>();
                    services.AddTransient<Func<string, ParameterValidationDialog>>(provider => recipeId =>
                        new ParameterValidationDialog(
                            recipeId,
                            provider.GetRequiredService<IRoiScanAreaStore>(),
                            provider.GetRequiredService<IH04DialogService>()));
                    // 子配方向导（F-3.2.2）：VM 按父 Job 每次新建，窗口由工厂组装，对话框服务只负责打开。
                    services.AddTransient<Func<string, SubRecipeWizardViewModel>>(provider => parentJobId =>
                        ActivatorUtilities.CreateInstance<SubRecipeWizardViewModel>(provider, parentJobId));
                    services.AddTransient<Func<string, SubRecipeWizardDialog>>(provider => parentJobId =>
                        new SubRecipeWizardDialog(
                            provider.GetRequiredService<Func<string, SubRecipeWizardViewModel>>()(parentJobId)));
                    services.AddSingleton<H01RunViewModel>();
                    services.AddSingleton<ModuleViewModelBase>(provider => provider.GetRequiredService<H01RunViewModel>());
                    services.AddSingleton<H02RecipeViewModel>();
                    services.AddSingleton<ModuleViewModelBase>(provider => provider.GetRequiredService<H02RecipeViewModel>());
                    services.AddSingleton<ModuleViewModelBase, H03AlignmentViewModel>();
                    services.AddSingleton<H04InspectionViewModel>();
                    services.AddSingleton<ModuleViewModelBase>(provider =>
                        provider.GetRequiredService<H04InspectionViewModel>());
                    services.AddSingleton<H05MapViewModel>();
                    services.AddSingleton<ModuleViewModelBase>(provider =>
                        provider.GetRequiredService<H05MapViewModel>());
                    services.AddSingleton<ModuleViewModelBase, H06DeviceControlViewModel>();
                    services.AddSingleton<ModuleViewModelBase, H07ConfigurationViewModel>();
                    services.AddSingleton<ModuleViewModelBase, H08AnalysisViewModel>();
                    services.AddSingleton<ModuleViewModelBase, H09EventViewModel>();
                    services.AddSingleton<ModuleViewModelBase, H10IdentityViewModel>();
                    services.AddSingleton<IModuleViewModelRegistry, ModuleViewModelRegistry>();
                    services.AddSingleton<MainViewModel>();
                    services.AddSingleton<MainWindow>();
                    services.AddSingleton<LoginViewModel>();
                    services.AddSingleton<LoginWindow>();
                    services.AddHostedService<SimulationHostedService>();
                })
                .Build();

            _host.Start();

            // 登录门禁（AC-P0-04）：先登录，认证通过后才创建主窗口；
            // 未认证一律不进入主界面（C-03：全系统仅此一个登录入口）。
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var authorization = _host.Services.GetRequiredService<IAuthorizationService>();
            var login = _host.Services.GetRequiredService<LoginWindow>();
            bool? loginResult = login.ShowDialog();
            if (loginResult != true)
            {
                Shutdown(0);
                return;
            }

            // 纵深防御：即使未来登录窗口被改动，主窗口创建前仍强制校验会话。
            LoginGate.EnsureAuthenticated(authorization);

            // StartupUri 已移除：窗口由容器构造并显式 Show，ShutdownMode 绑定到主窗口。
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            MainWindow = _host.Services.GetRequiredService<MainWindow>();
            MainWindow.Show();
        }
        catch (Exception ex)
        {
            // 启动期异常落盘，便于无控制台环境下诊断（保留：主界面静默白屏的教训）。
            try { System.IO.File.WriteAllText("startup-error.log", ex.ToString()); } catch { /* 忽略 */ }
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
        // 界面异常先落盘（无控制台环境下保留调用栈），再异步弹窗：
        // 同步 MessageBox 的嵌套消息循环会让同一异常反复进入本处理器（P2 H04 回归教训）。
        try
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(AppContext.BaseDirectory, "ui-error.log"),
                $"[{DateTime.Now:HH:mm:ss}] {e.Exception}{Environment.NewLine}");
        }
        catch { /* 日志失败不掩盖原始异常 */ }

        string message = e.Exception.Message;
        Dispatcher.BeginInvoke(new Action(() => MessageBox.Show(
            $"未处理的界面异常：{message}",
            "WaferInspect 运行错误",
            MessageBoxButton.OK,
            MessageBoxImage.Error)));
        e.Handled = true;
    }
}
