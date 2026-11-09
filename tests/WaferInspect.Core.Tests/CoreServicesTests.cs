using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WaferInspect.Core.DependencyInjection;
using WaferInspect.Core.Navigation;
using WaferInspect.Core.Simulation;
using Xunit;

namespace WaferInspect.Core.Tests;

/// <summary>
/// 验证 Core 层通过真实 Generic Host 完成装配，并锁定模拟算法的历史数值，
/// 防止「重构顺手简化行为」。
/// </summary>
public sealed class CoreServicesTests
{
    [Fact]
    public void AddWaferInspectCore_ResolvesCatalogAndSimulationData()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services => services.AddWaferInspectCore())
            .Build();

        var catalog = host.Services.GetRequiredService<INavigationCatalog>();
        var simulation = host.Services.GetRequiredService<ISimulationData>();

        Assert.Same(catalog, host.Services.GetRequiredService<INavigationCatalog>());
        Assert.Same(simulation, host.Services.GetRequiredService<ISimulationData>());
        Assert.Equal(4, catalog.Groups.Count);
        Assert.Equal(12, catalog.Groups.Sum(group => group.Items.Count));
        Assert.Equal("overview", catalog.DefaultViewKey);
        Assert.Equal("system", catalog.GroupKeyOf("alarms"));
        Assert.Null(catalog.GroupKeyOf("missing-view"));
    }

    [Fact]
    public void YieldSeries_IsDeterministicAndPreservesFixedPoints()
    {
        var first = new SimulationData().CreateYieldSeries();
        var second = new SimulationData().CreateYieldSeries();

        Assert.Equal(61, first.Count);
        Assert.Equal(first, second);
        // 18 / 19 是历史固定的异常点，刻意落在钳制区间之外，属于必须保留的兼容行为。
        Assert.Equal(97.31, first[18], 2);
        Assert.Equal(97.86, first[19], 2);
        Assert.Equal(98.42, first[60], 2);
        Assert.All(first.Where((_, index) => index != 18 && index != 19),
            value => Assert.InRange(value, 97.5, 99.5));
    }

    [Fact]
    public void NextYield_IsReproducibleWithSameRandomSeed()
    {
        var simulation = new SimulationData();

        double first = simulation.NextYield(98.42, new Random(20261109));
        double second = simulation.NextYield(98.42, new Random(20261109));

        Assert.Equal(first, second, 10);
        Assert.Equal(
            new SimulationData().NextYield(98.42, new Random(20261109)),
            first,
            10);
    }

    [Fact]
    public void Noise_AndChartY_KeepHistoricalFormulas()
    {
        var simulation = new SimulationData();

        Assert.InRange(simulation.Noise(12.5), 0, 1);
        Assert.Equal(simulation.Noise(12.5), simulation.Noise(12.5), 12);
        Assert.Equal(118 - (98.42 - 96.5) / 3.5 * 104, simulation.ChartY(98.42), 10);
    }
}
