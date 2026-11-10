using Microsoft.Extensions.DependencyInjection;
using WaferInspect.Core.Alignment;
using WaferInspect.Core.Data;
using WaferInspect.Core.Devices;
using WaferInspect.Core.Events;
using WaferInspect.Core.Inspection;
using WaferInspect.Core.Maps;
using WaferInspect.Core.Navigation;
using WaferInspect.Core.Recipes;
using WaferInspect.Core.Run;
using WaferInspect.Core.Security;
using WaferInspect.Core.Simulation;

namespace WaferInspect.Core.DependencyInjection;

/// <summary>
/// Core 层自身的 DI 装配：App 的通用主机只需调用一次，
/// 后续新增纯数据服务都在这里登记，避免组合根散落。
/// </summary>
public static class CoreServiceCollectionExtensions
{
    public static IServiceCollection AddWaferInspectCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<INavigationCatalog, NavigationCatalog>();
        services.AddSingleton<ISimulationData, SimulationData>();
        services.AddSingleton<IDataSource>(provider => provider.GetRequiredService<ISimulationData>());
        services.AddSingleton<IMapRenderer, WaferMapRenderer>();
        services.AddSingleton<IEventSource, InMemoryEventSource>();
        services.AddSingleton<IAlarmCenter, InMemoryAlarmCenter>();
        services.AddSingleton<WaferRunController>();
        services.AddSingleton<ManualLoadSequencer>();
        // P1：模拟 Job 清单；H02 配方库（P2）就绪后替换为真实 IJobCatalog 实现。
        services.AddSingleton<IJobCatalog, SimulatedJobCatalog>();

        // P2（H02）：Job 仓库、Map 导入与 MapType 目录；H05 是 MapType 的唯一 Owner。
        services.AddSingleton<IMapTypeCatalog, SimulatedMapTypeCatalog>();
        services.AddSingleton<IJobRepository, InMemoryJobRepository>();
        services.AddSingleton<ISubRecipeRepository, InMemorySubRecipeRepository>();
        services.AddSingleton<MapFileImporter>();
        services.AddSingleton<SelfCheckService>();
        // P2（H03，B-08）：对位与模板匹配能力族——引擎为全系统唯一实现（AC-P2-04），
        // H02 配方向导与 H05 Wafer 扫描必须复用同一单例；H08 对 GoldenDie 只读（B-09）。
        services.AddSingleton<TemplateMatchingEngine>();
        services.AddSingleton<LightCalibrationAnalyzer>();
        services.AddSingleton<MarkAlignmentService>();
        services.AddSingleton<ReferenceImagePlanner>();
        // P2（H04，B-01/B-02/AC-P2-03）：ROI 与扫描区域的唯一数据源。
        // 向导步骤 5/6、缺陷分析的参数验证页、H01 中止调参必须复用同一单例，不得各自维护副本。
        services.AddSingleton<IRoiScanAreaStore, RoiScanAreaStore>();
        services.AddSingleton<IAuthorizationService, AuthorizationService>();
        services.AddSingleton<IAccountStore, InMemoryAccountStore>();
        services.AddSingleton<IAuthenticationService, AuthenticationService>();
        services.AddSingleton<IDeviceCommandLayer, DeviceCommandLayer>();
        // 【待确认 T-029】不注册虚构的 IDeviceGateway 生产实现；由确认协议后的适配器提供。
        return services;
    }
}
