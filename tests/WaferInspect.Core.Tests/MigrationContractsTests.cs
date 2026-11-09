using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WaferInspect.Core.Data;
using WaferInspect.Core.DependencyInjection;
using WaferInspect.Core.Devices;
using WaferInspect.Core.Events;
using WaferInspect.Core.Maps;
using WaferInspect.Core.Security;
using WaferInspect.Core.Simulation;
using Xunit;

namespace WaferInspect.Core.Tests;

public sealed class MigrationContractsTests
{
    [Fact]
    public void CoreHost_ResolvesP0Services_ButDoesNotInventDeviceGateway()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services => services.AddWaferInspectCore())
            .Build();

        Assert.Same(host.Services.GetRequiredService<ISimulationData>(),
            host.Services.GetRequiredService<IDataSource>());
        Assert.NotNull(host.Services.GetRequiredService<IMapRenderer>());
        Assert.NotNull(host.Services.GetRequiredService<IEventSource>());
        Assert.NotNull(host.Services.GetRequiredService<IAuthorizationService>());
        Assert.Null(host.Services.GetService<IDeviceGateway>());
    }

    [Fact]
    public void DataSource_CanBeReplacedWithoutChangingItsConsumerContract()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddWaferInspectCore();
                services.AddSingleton<IDataSource, ReplacementDataSource>();
            })
            .Build();

        var source = host.Services.GetRequiredService<IDataSource>();
        Assert.IsType<ReplacementDataSource>(source);
        Assert.Equal(new[] { 90.0, 91.0 }, source.CreateYieldSeries());
        Assert.Equal(88.5, source.NextYield(100, new Random(1)), 10);
    }

    [Theory]
    [InlineData(0, "PL", 217, 52, 36, 12, 96.2145110410)]
    [InlineData(1, "EL", 204, 66, 30, 17, 94.6372239748)]
    [InlineData(2, "AOI", 221, 49, 34, 13, 95.8990536278)]
    public void MapRenderer_PreservesLegacySnapshots(
        int channelIndex, string channel, int normal, int light, int warning, int bad, double yield)
    {
        var result = new WaferMapRenderer(new SimulationData()).Render(new MapRenderRequest(channelIndex, channel));

        Assert.Equal(317, result.Total);
        Assert.Equal(result.Total, result.Cells.Count);
        Assert.Equal(normal, result.Normal);
        Assert.Equal(light, result.Light);
        Assert.Equal(warning, result.Warning);
        Assert.Equal(bad, result.Bad);
        Assert.Equal(yield, result.YieldPercentage, 9);
        Assert.Equal(new MapDiePoint(115.4, 6.4, result.Cells[0].Level, 0, 10), result.Cells[0]);
        Assert.Equal(115.4, result.Cells[^1].X, 10);
        Assert.Equal(224.4, result.Cells[^1].Y, 10);
    }

    [Fact]
    public async Task DeviceCommandLayer_ForwardsCommandAndResultWithoutRewritingThem()
    {
        var gateway = new RecordingGateway();
        var layer = new DeviceCommandLayer(gateway);
        var command = new DeviceCommand("EFEM-01", DeviceKind.Efem, "Load",
            new Dictionary<string, string> { ["Port"] = "Port1" });

        var result = await layer.SendAsync(command);

        Assert.Same(command, gateway.LastCommand);
        Assert.True(result.Success);
        Assert.Equal("ADAPTER-RESULT", result.Code);
        await Assert.ThrowsAsync<ArgumentException>(() => layer.SendAsync(command with { DeviceId = "" }));
        await Assert.ThrowsAsync<ArgumentException>(() => layer.SendAsync(command with { Name = "" }));
    }

    [Fact]
    public void EventSource_PublishesTheOriginalEvent()
    {
        var source = new InMemoryEventSource();
        SystemEvent received = null;
        source.EventPublished += (_, item) => received = item;
        var item = new SystemEvent("E-1", DateTimeOffset.UnixEpoch, "EFEM-01", "Alarm", "原始等级",
            "原始消息", new Dictionary<string, string> { ["Port"] = "Port1" });

        source.Publish(item);

        Assert.Same(item, received);
    }

    [Fact]
    public void Authorization_DeniesByDefaultAndUsesOnlyExplicitPermissions()
    {
        var service = new AuthorizationService();
        Assert.False(service.IsAuthenticated);
        Assert.False(service.HasPermission("job.edit"));
        Assert.Throws<UnauthorizedAccessException>(() => service.RequirePermission("job.edit"));

        var user = new UserIdentity("U-1", "测试用户", ["OP"], ["job.view"]);
        service.EstablishSession(user);
        Assert.True(service.IsAuthenticated);
        Assert.True(service.HasPermission("job.view"));
        Assert.False(service.HasPermission("job.edit"));
        Assert.False(service.HasPermission("system.settings.edit"));
        Assert.Contains("job.edit", Assert.Throws<UnauthorizedAccessException>(
            () => service.RequirePermission("job.edit")).Message);

        service.ClearSession();
        Assert.False(service.HasPermission("job.view"));
    }

    private sealed class ReplacementDataSource : IDataSource
    {
        public IReadOnlyList<double> CreateYieldSeries() => [90.0, 91.0];

        public double NextYield(double last, Random random) => 88.5;
    }

    private sealed class RecordingGateway : IDeviceGateway
    {
        public DeviceCommand LastCommand { get; private set; }

        public Task<DeviceCommandResult> ExecuteAsync(
            DeviceCommand command,
            CancellationToken cancellationToken = default)
        {
            LastCommand = command;
            return Task.FromResult(new DeviceCommandResult(true, "ADAPTER-RESULT", "适配器原始结果"));
        }
    }
}
