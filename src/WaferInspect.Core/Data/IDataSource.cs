namespace WaferInspect.Core.Data;

/// <summary>
/// P0 实时良率数据边界。视图只依赖该接口读取序列与推进采样，不依赖模拟实现。
/// 设备协议、真实字段与存量数据契约仍受【待确认 T-029】/【待确认 T-030】约束。
/// </summary>
// 【待确认 T-029】真实设备字段与协议由适配器实现。
// 【待确认 T-030】存量结果数据格式与搬迁规则确认前，不扩展持久化假设。
public interface IDataSource
{
    IReadOnlyList<double> CreateYieldSeries();

    double NextYield(double last, Random random);
}
