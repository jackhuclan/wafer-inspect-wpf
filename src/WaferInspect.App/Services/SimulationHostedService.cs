using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace WaferInspectHMI.Services;

/// <summary>把 1 Hz 模拟数据泵纳入通用主机生命周期：启动即开泵，退出即停泵。</summary>
public sealed class SimulationHostedService : IHostedService
{
    private readonly ILiveDataPump _pump;

    public SimulationHostedService(ILiveDataPump pump) => _pump = pump;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _pump.Start();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _pump.Stop();
        return Task.CompletedTask;
    }
}
