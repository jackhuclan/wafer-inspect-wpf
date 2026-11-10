using WaferInspect.Core.Maps;
using WaferInspect.Core.Run;

namespace WaferInspect.Core.Analysis;

/// <summary>H08 检测结果仓库（B-23 数据源之一）：当前为内存确定性种子，
/// 真实落盘随 H07 配置单点确定后替换；复判写回统一走 <see cref="Update"/>。</summary>
public interface IWaferResultStore
{
    /// <summary>所有 Wafer 结果快照（只读副本）。</summary>
    IReadOnlyList<WaferResult> Wafers { get; }

    /// <summary>内容变化通知（复判写回后触发）。</summary>
    event EventHandler? Changed;

    WaferResult? Get(string waferId);

    /// <summary>复判结果写回（新增或替换），触发 <see cref="Changed"/>。</summary>
    WaferResult Update(WaferResult wafer);

    /// <summary>F-3.9.2-01~02：按 LotId / WaferId / 机台号筛选（空条件表示不过滤）。</summary>
    IReadOnlyList<WaferResult> Query(string? lotId = null, string? waferId = null, string? toolId = null);
}

/// <summary>
/// 内存确定性种子实现：同一进程、同一随机种子恒得同一组 Wafer 与 Bin 矩阵，便于回归与 UI 自动化。
/// 数据为合成结果，界面统一标注「[模拟结果] 设备采集 / AI 推理未接入（T-029 / T-035）」。
/// </summary>
public sealed class WaferResultStore : IWaferResultStore
{
    private readonly List<WaferResult> _wafers = new();
    private readonly object _gate = new();

    public WaferResultStore() => Seed();

    public IReadOnlyList<WaferResult> Wafers
    {
        get { lock (_gate) return _wafers.ToList(); }
    }

    public event EventHandler? Changed;

    public WaferResult? Get(string waferId)
    {
        lock (_gate)
            return _wafers.FirstOrDefault(w => string.Equals(w.WaferId, waferId, StringComparison.Ordinal));
    }

    public WaferResult Update(WaferResult wafer)
    {
        lock (_gate)
        {
            int at = _wafers.FindIndex(w => string.Equals(w.WaferId, wafer.WaferId, StringComparison.Ordinal));
            if (at < 0) _wafers.Add(wafer);
            else _wafers[at] = wafer;
        }
        Raise();
        return wafer;
    }

    public IReadOnlyList<WaferResult> Query(string? lotId = null, string? waferId = null, string? toolId = null)
    {
        lock (_gate)
            return _wafers.Where(w =>
                (string.IsNullOrWhiteSpace(lotId) || w.LotId.Contains(lotId, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrWhiteSpace(waferId) || w.WaferId.Contains(waferId, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrWhiteSpace(toolId) || string.Equals(w.ToolId, toolId, StringComparison.Ordinal)))
                .ToList();
    }

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);

    private void Seed()
    {
        // 与 EditableMapTypeCatalog 种子图谱对齐，保证 H08 全景 Map 能取到 Bin 颜色（B-11）。
        const string mapTypeId = "MAP-PL-300";
        const int rows = 20;
        const int columns = 20;
        var rng = new Random(20261110);
        var categories = new[] { "颗粒污染", "划伤", "图形异常", "边缘崩边", "色差" };

        var plan = new (string WaferId, string LotId, string ToolId)[]
        {
            ("WFR-042", "LOT-A-2601", "T1"),
            ("WFR-043", "LOT-A-2601", "T1"),
            ("WFR-044", "LOT-A-2601", "T2"),
            ("WFR-201", "LOT-B-2602", "T1"),
            ("WFR-202", "LOT-B-2602", "T2"),
            ("WFR-203", "LOT-B-2602", "T2"),
        };

        foreach (var p in plan)
        {
            var matrix = new BinKind[rows * columns];
            var defects = new List<DefectRecord>();

            for (int row = 0; row < rows; row++)
            for (int column = 0; column < columns; column++)
            {
                int idx = row * columns + column;
                bool border = row == 0 || column == 0 || row == rows - 1 || column == columns - 1;
                BinKind kind = BinKind.GoodDie;
                if (border)
                {
                    kind = BinKind.ForBiddenDie;            // 外围无效（最外圈）
                }
                else
                {
                    int roll = rng.Next(100);
                    if (roll < 8) kind = BinKind.DefectDie;
                    else if (roll < 11) kind = BinKind.ReferenceDie;
                    else if (roll < 13) kind = BinKind.MatchFailDie;
                    else if (roll < 15) kind = BinKind.ExceptionDie;
                }
                matrix[idx] = kind;

                if (kind is BinKind.DefectDie or BinKind.ExceptionDie)
                {
                    bool preReviewed = rng.Next(100) < 40;
                    defects.Add(new DefectRecord(
                        Zone: $"Z{column / 5 + 1}",
                        Category: categories[rng.Next(categories.Length)],
                        Row: row,
                        Column: column,
                        Length: Math.Round(2 + rng.NextDouble() * 18, 1),
                        Width: Math.Round(2 + rng.NextDouble() * 14, 1),
                        Area: Math.Round(4 + rng.NextDouble() * 220, 1),
                        AiClassification: "",
                        AiBin: "",
                        ReviewState: preReviewed ? "已复判" : "未复判",
                        Note: "",
                        SourceImagePath: "[搁置] 原始图片路径未接入（T-029）"));
                }
            }

            int preReviewDefects = matrix.Count(k => k is BinKind.DefectDie or BinKind.ExceptionDie);
            _wafers.Add(new WaferResult(
                WaferId: p.WaferId,
                LotId: p.LotId,
                ToolId: p.ToolId,
                MapTypeId: mapTypeId,
                Rows: rows,
                Columns: columns,
                BinMatrix: matrix,
                Defects: defects,
                PreReviewDefectCount: preReviewDefects,
                ReviewedCount: 0,
                AiReviewed: false));
        }
    }
}
