namespace WaferInspect.Core.Simulation;

/// <summary>
/// 从 App 层原样迁移的模拟算法（数值、随机序列顺序、异常点位置均未改变）。
/// 迁移只允许改宿主与调用方式，不允许简化或替换行为。
/// </summary>
public sealed class SimulationData : ISimulationData
{
    private const int SampleCount = 61;

    public IReadOnlyList<double> CreateYieldSeries()
    {
        var values = new List<double>(SampleCount);
        for (int i = 0; i < SampleCount; i++)
        {
            double v = 98.28
                + Math.Sin(i / 6.5) * 0.20
                + Math.Sin(i / 2.3) * 0.07
                + (Noise(i * 3 + 1) - .5) * 0.09;
            values.Add(Math.Clamp(v, 97.5, 99.5));
        }

        values[18] = 97.31;
        values[19] = 97.86;
        values[60] = 98.42;
        return values;
    }

    public double NextYield(double last, Random random)
    {
        double next = Math.Clamp(
            last + (random.NextDouble() - .5) * 0.17 + (98.42 - last) * 0.07,
            97.55, 99.55);
        if (random.NextDouble() < 0.012) next -= 0.75;
        return next;
    }

    public double ChartY(double value) => 118 - (value - 96.5) / 3.5 * 104;

    public double Noise(double seed)
    {
        double x = Math.Sin(seed * 127.1 + 0.73) * 43758.5453;
        return x - Math.Floor(x);
    }
}
