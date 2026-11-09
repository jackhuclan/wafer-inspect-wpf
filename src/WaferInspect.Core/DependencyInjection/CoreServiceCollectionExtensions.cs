using Microsoft.Extensions.DependencyInjection;
using WaferInspect.Core.Data;
using WaferInspect.Core.Devices;
using WaferInspect.Core.Events;
using WaferInspect.Core.Maps;
using WaferInspect.Core.Navigation;
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
        services.AddSingleton<IAuthorizationService, AuthorizationService>();
        services.AddSingleton<IDeviceCommandLayer, DeviceCommandLayer>();
        // 【待确认 T-029】不注册虚构的 IDeviceGateway 生产实现；由确认协议后的适配器提供。
        return services;
    }
}
