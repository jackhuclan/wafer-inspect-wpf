using System;
using System.Windows.Threading;

namespace WaferInspectHMI.Services;

/// <summary>
/// 用 DispatcherTimer 产生 UI 线程上的 1 Hz 节拍（必须在 UI 线程构造，
/// 由 App.OnStartup 中的主机启动时满足）。后续替换真实设备数据源只需换实现。
/// </summary>
public sealed class DispatcherTimerLiveDataPump : ILiveDataPump
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _disposed;

    public event EventHandler Tick;

    public DispatcherTimerLiveDataPump() => _timer.Tick += OnTimerTick;

    private void OnTimerTick(object sender, EventArgs e) => Tick?.Invoke(this, EventArgs.Empty);

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
    }
}
