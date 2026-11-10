namespace WaferInspect.Core.Maps;

/// <summary>
/// 图谱类型（MapType）定义：`H05` 是唯一 Owner（B-16/D-05），Job 只持有引用而非副本。
/// `AC-P2-06`（8 类 Bin 可编辑 + 快捷键）与 `AC-P3-04`（复判读取 MapType）都消费本契约。
/// </summary>
public sealed record MapTypeDefinition(string Id, string Name, IReadOnlyList<BinDefinition> Bins);

/// <summary>
/// Bin 定义（`AC-P2-06`：8 类 Bin 可编辑）。`Shortcut` 为历史快捷键，`Level` 为 Map 渲染等级（H05）。
/// </summary>
public sealed record BinDefinition(int Index, string Name, string Shortcut, int Level, string ColorHex);

/// <summary>MapType 目录（H05 唯一提供）：Job 的「图谱选择」下拉数据源。</summary>
public interface IMapTypeCatalog
{
    IReadOnlyList<MapTypeDefinition> Types { get; }

    MapTypeDefinition? Find(string id);
}

/// <summary>
/// 原型阶段的 MapType 目录：8 类 Bin 与历史等级/配色一致（与 H05 渲染统计同源）。
/// 真实图谱库（导入/导出/新增）属 P2 后续增量，此处仅提供只读基线，避免 Job 侧自建第二份定义。
/// </summary>
public sealed class SimulatedMapTypeCatalog : IMapTypeCatalog
{
    private static readonly IReadOnlyList<BinDefinition> StandardBins =
    [
        new(0, "未分类", "0", 0, "#CBD5E1"),
        new(1, "良品", "1", 1, "#3B82F6"),
        new(2, "缺陷品", "2", 2, "#F59E0B"),
        new(3, "边缘失效", "3", 3, "#EF4444"),
        new(4, "对位失败", "4", 3, "#A855F7"),
        new(5, "重复图形", "5", 2, "#14B8A6"),
        new(6, "颗粒污染", "6", 2, "#F97316"),
        new(7, "划伤", "7", 3, "#DC2626")
    ];

    public SimulatedMapTypeCatalog()
    {
        Types =
        [
            new MapTypeDefinition("MAP-PL-300", "PL 标准图谱 · 300mm", StandardBins),
            new MapTypeDefinition("MAP-EL-200", "EL 高增益图谱 · 200mm", StandardBins)
        ];
    }

    public IReadOnlyList<MapTypeDefinition> Types { get; }

    public MapTypeDefinition? Find(string id)
        => Types.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal));
}
