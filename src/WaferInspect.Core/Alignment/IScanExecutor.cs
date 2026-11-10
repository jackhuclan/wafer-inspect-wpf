namespace WaferInspect.Core.Alignment;

/// <summary>
/// 扫描执行 / 对位抽象（H06）。实现可插拔：当前默认 <see cref="OfflineScanExecutor"/>（确定性离线演算），
/// 真实设备实现（<see cref="DeviceScanExecutor"/>）预留，规范（T-029 相机采集 / T-035 AI 推理）到位即 drop-in 替换，
/// 不伪造任何设备 / 算法结果（E-08 / E-10）。
/// 两种实现都复用同一套 B-08 唯一匹配引擎与 H03 对位服务，仅 FOV 来源与匹配输入不同——
/// 因此真实设备替换仅需改变 FOV 来源（闭环后接入 <c>IDeviceGateway</c>），匹配 / 对位逻辑不变。
/// </summary>
public interface IScanExecutor
{
    /// <summary>执行源种类。</summary>
    ScanExecutorKind Kind { get; }

    /// <summary>是否为离线确定性演算。Offline 为 true；Device 为 false。</summary>
    bool IsOffline { get; }

    /// <summary>
    /// 运行一次扫描演算：取 FOV → B-08 引擎算匹配分 → H03 对位算平台坐标。
    /// 离线实现用合成 FOV；设备实现用真实采集 FOV（待 T-029/T-035 闭环）。
    /// </summary>
    /// <param name="columns">Map 网格列数（需先有 Map）。</param>
    /// <param name="rows">Map 网格行数。</param>
    /// <param name="matchThreshold">匹配分数阈值（0~100，BR-24 高于阈值才判成功）。</param>
    /// <param name="seed">合成 FOV 种子（确定性，可复现；设备实现忽略）。</param>
    /// <param name="dieXmm">单 Die 宽（mm），对位换算用。</param>
    /// <param name="dieYmm">单 Die 高（mm），对位换算用。</param>
    ScanReport Execute(
        int columns, int rows, double matchThreshold = 75, int? seed = null, double dieXmm = 10, double dieYmm = 10);

    /// <summary>步骤 8「降低匹配分数重试」：在<b>同一 FOV 与模板</b>上用更低阈值重跑（不伪造、可复现）。</summary>
    /// <param name="previous">上一次演算报告（取其 FOV 种子与网格，保证同一视窗）。</param>
    /// <param name="lowerThreshold">降低后的阈值（0~100）。</param>
    ScanReport RetryWithLowerThreshold(ScanReport previous, double lowerThreshold);
}
