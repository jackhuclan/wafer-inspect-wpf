namespace WaferInspect.Core.Alignment;

/// <summary>
/// 真实设备扫描执行器（预留实现位，T-029/T-035）。规范待确认前，<see cref="Execute"/> 抛诚实异常，
/// 绝不注册生产 <c>IDeviceGateway</c>、绝不伪造设备采集 / AI 推理结果（E-08 / E-10：AGENTS.md §P2 红线——
/// T-029 未闭环前禁止注册虚构生产网关）。
/// T-029/T-035 闭环后：经已闭环的 <c>IDeviceGateway</c> 采集真实 FOV、接入真实 AI 推理，跑同一套 B-08 引擎 + H03 对位，
/// 并令 <see cref="IsOffline"/> = false；仅需实现本类并切换 <c>SelectedScanExecutorKind = Device</c>，调用方与 UI 不变。
/// </summary>
public sealed class DeviceScanExecutor : IScanExecutor
{
    /// <summary>执行源为真实设备。</summary>
    public ScanExecutorKind Kind => ScanExecutorKind.Device;

    /// <summary>非离线：真实设备执行（E-08/E-10 诚实标注来源）。</summary>
    public bool IsOffline => false;

    /// <summary>T-029/T-035 未闭环：拒绝伪造真实设备扫描结果（E-08/E-10）。</summary>
    public ScanReport Execute(
        int columns, int rows, double matchThreshold = 75, int? seed = null, double dieXmm = 10, double dieYmm = 10)
        => throw new DeviceScanNotConfirmedException();

    /// <summary>T-029/T-035 未闭环：拒绝伪造真实设备扫描结果（E-08/E-10）。</summary>
    public ScanReport RetryWithLowerThreshold(ScanReport previous, double lowerThreshold)
        => throw new DeviceScanNotConfirmedException();
}
