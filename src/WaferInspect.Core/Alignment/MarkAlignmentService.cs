using WaferInspect.Core.Recipes;

namespace WaferInspect.Core.Alignment;

/// <summary>
/// H03 Mark 点对位服务（`F-3.2.2-40~59`）：
/// <list type="bullet">
/// <item>双击 Die 网格 → 图谱行列坐标；「指定 Die 左上角」→ 写入平台 X/Y（F-3.2.2-50）；</item>
/// <item>BR-26 方向校验：水平拉平 2 点同行、竖直 2 点同列；三点对位 / 2+1 对位按级 C 保留（F-3.2.2-49）；</item>
/// <item>T-024：点位重置（慎用）必须经二次确认，未确认调用直接抛异常；</item>
/// <item>BR-31：点击 Map 任意网格定位到该 Die 左上角切割道中心。</item>
/// </list>
/// </summary>
public sealed class MarkAlignmentService
{
    /// <summary>图谱行列 → 平台（Chuck）坐标映射：列 × Die 宽 + 行 × Die 高 + 网格偏移（F-3.2.2-48/50）。</summary>
    public PlatformPoint DieToPlatform(int row, int col, double dieXmm, double dieYmm, MapGridState grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (dieXmm <= 0) throw new ArgumentOutOfRangeException(nameof(dieXmm), "Die 宽必须大于 0。");
        if (dieYmm <= 0) throw new ArgumentOutOfRangeException(nameof(dieYmm), "Die 高必须大于 0。");

        // 偏移为显示像素相对 die 尺寸的等比换算（比例尺：1 die 显示宽 ↔ DieXmm）。
        double offsetScaleX = grid.Columns > 0 ? dieXmm / grid.Columns : 0;
        double offsetScaleY = grid.Rows > 0 ? dieYmm / grid.Rows : 0;

        return new PlatformPoint(
            Xmm: col * dieXmm + grid.OffsetX * offsetScaleX,
            Ymm: row * dieYmm + grid.OffsetY * offsetScaleY);
    }

    /// <summary>BR-31：Die 左上角切割道中心坐标（切割道宽计入偏移的定位校验口径）。</summary>
    public (double Xmm, double Ymm) LocateDieStreetCenter(int row, int col, double dieXmm, double dieYmm)
        => (col * dieXmm, row * dieYmm);

    /// <summary>
    /// BR-25/26 结构与方向校验。水平要求 2 点同行、竖直要求 2 点同列；
    /// 三点对位 / 2+1 对位（级 C 保留）至少 2 点同行或同列即可。
    /// </summary>
    public IReadOnlyList<SubRecipeValidationIssue> Validate(IReadOnlyList<MarkPoint> marks, MarkAlignmentMethod method)
    {
        ArgumentNullException.ThrowIfNull(marks);

        var issues = new List<SubRecipeValidationIssue>();
        if (marks.Count != 3)
        {
            issues.Add(new("Marks", "必须设置 3 个 Mark 点（BR-25：三点对位）"));
            return issues;
        }

        bool anySameRow = marks.GroupBy(m => m.Row).Any(g => g.Count() >= 2);
        bool anySameCol = marks.GroupBy(m => m.Col).Any(g => g.Count() >= 2);

        switch (method)
        {
            case MarkAlignmentMethod.Horizontal:
                if (!anySameRow)
                    issues.Add(new("Marks", "水平拉平要求 2 个 Mark 点同一行（BR-26 / F-3.2.2-49）"));
                break;
            case MarkAlignmentMethod.Vertical:
                if (!anySameCol)
                    issues.Add(new("Marks", "竖直拉平要求 2 个 Mark 点同一列（BR-26 / F-3.2.2-49）"));
                break;
            case MarkAlignmentMethod.ThreePoint:
            case MarkAlignmentMethod.TwoPlusOne:
                if (!anySameRow && !anySameCol)
                    issues.Add(new("Marks",
                        "三个 Mark 点中至少 2 个须同行或同列（BR-25；三点对位 / 2+1 对位为推荐忽略，级 C 保留）"));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(method));
        }

        return issues;
    }

    /// <summary>
    /// F-3.2.2-56 点位重置（清空 Mark 列表，手册标注「慎用」）。
    /// T-024 已确认：必须经二次确认弹窗，未确认调用直接拒绝。
    /// </summary>
    public IReadOnlyList<MarkPoint> ResetMarks(IReadOnlyList<MarkPoint> marks, bool confirmed)
    {
        ArgumentNullException.ThrowIfNull(marks);
        if (!confirmed)
            throw new InvalidOperationException("点位重置为「慎用」操作，必须经二次确认弹窗（T-024 / F-3.2.2-56）");
        return [];
    }
}
