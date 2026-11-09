using WaferInspect.Core.Simulation;

namespace WaferInspect.Core.Maps;

public sealed record MapRenderRequest(int ChannelIndex, string ChannelName);

public sealed record MapDiePoint(double X, double Y, int Level, int Row, int Column);

public sealed record MapRenderResult(
    IReadOnlyList<MapDiePoint> Cells,
    int Total,
    int Normal,
    int Light,
    int Warning,
    int Bad,
    double YieldPercentage);

/// <summary>
/// H05 唯一 Map 布局、等级分布与坐标计算组件。WPF 画刷由 App 按返回 Level 映射，
/// 首页/晶圆地图及后续模块不得再复制这套坐标与统计规则（PRD B-06/B-07）。
/// </summary>
public interface IMapRenderer
{
    MapRenderResult Render(MapRenderRequest request);
}

/// <summary>从 MainViewModel 原样抽取的现有演示 Map 算法；数值、顺序与边界保持不变。</summary>
public sealed class WaferMapRenderer(ISimulationData simulation) : IMapRenderer
{
    public MapRenderResult Render(MapRenderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        const int N = 21;
        const double pitch = 10.9, size = 9.2, cx = 120, cy = 120, R = 113;
        var cells = new List<MapDiePoint>();
        int[] counts = new int[4];

        for (int r = 0; r < N; r++)
        for (int c = 0; c < N; c++)
        {
            double x = cx + (c - (N - 1) / 2.0) * pitch;
            double y = cy + (r - (N - 1) / 2.0) * pitch;
            double dist = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
            if (dist > R - 4) continue;
            double seed = simulation.Noise((r * 31 + c * 7) * 13 + request.ChannelIndex * 97);
            double edge = dist / R;
            int lv = seed < 0.80 - 0.16 * edge ? 0 : seed < 0.925 - 0.09 * edge ? 1
                   : seed < 0.982 - 0.035 * edge ? 2 : 3;
            double cluster = Math.Sqrt((x - (cx + 40)) * (x - (cx + 40)) + (y - (cy - 32)) * (y - (cy - 32)));
            if (cluster < 23 && simulation.Noise((int)(seed * 997) + request.ChannelIndex * 13) > 0.34)
                lv = simulation.Noise(seed * 311) > 0.46 ? 3 : 2;

            counts[lv]++;
            cells.Add(new MapDiePoint(x - size / 2, y - size / 2, lv, r, c));
        }

        int total = counts.Sum();
        double yield = (1 - counts[3] / (double)total) * 100;
        return new MapRenderResult(cells, total, counts[0], counts[1], counts[2], counts[3], yield);
    }
}
