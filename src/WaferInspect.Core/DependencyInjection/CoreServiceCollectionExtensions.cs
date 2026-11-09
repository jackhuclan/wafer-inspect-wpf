using Microsoft.Extensions.DependencyInjection;
using WaferInspect.Core.Navigation;
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
        return services;
    }
}
