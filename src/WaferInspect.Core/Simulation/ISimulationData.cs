namespace WaferInspect.Core.Simulation;

/// <summary>
/// 产线模拟数据的确定性算法。纯数值计算，不含 WPF / 设备 / IO 类型，
/// 因此可在 net10.0 下独立单测，也便于后续替换为真实数据源。
/// </summary>
public interface ISimulationData
{
    /// <summary>生成初始良率序列（61 点，含固定的异常点与末值）。</summary>
    IReadOnlyList<double> CreateYieldSeries();

    /// <summary>在 1 Hz 节拍上推进一个良率采样点；随机数由调用方持有以保证序列可复现。</summary>
    double NextYield(double last, Random random);

    /// <summary>良率值 → 图表 Y 坐标（保留历史换算系数）。</summary>
    double ChartY(double value);

    /// <summary>确定性伪随机噪声，供晶圆图谱与良率曲线共用。</summary>
    double Noise(double seed);
}
