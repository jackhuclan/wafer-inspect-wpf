namespace WaferInspect.Core.Alignment;

/// <summary>
/// H03 参考图像（Golden Die）规划器（`F-3.2.2-60~67`）。B-09：生成实现唯一归属 H03，
/// `H08` 只读展示结果，禁止复制生成逻辑。
/// 确定性规则：
/// <list type="bullet">
/// <item>F-3.2.2-67：从可用 Die 中等距散布选取 N 个参考 Die（确定性，非真随机）；</item>
/// <item>F-3.2.2-60：黄色 = 其余待检测 Die；蓝色 = 预聚焦点位（与参考 Die 同点位）；红色 = 参考 Die（可能没有，本口径为 0）；绿色 = 随机选中的参考 Die；</item>
/// <item>BR-29：耗时指数随参考 Die 数量单调增加；</item>
/// <item>BR-30：视野缩放修改后须重新生成（NeedsRegeneration）。</item>
/// </list>
/// 真实图像拟合（算法在真实采集图像上生成 Golden Die）属 `H06` 采集链路（`T-029` 搁置）；
/// 本规划器先在图谱数据层完成点位规划与分类，拟合结果落地时复用同一规划。
/// </summary>
public sealed class ReferenceImagePlanner
{
    public GoldenDiePlan Plan(int cols, int rows, int referenceDieCount, double fovZoom)
    {
        if (cols <= 0) throw new ArgumentOutOfRangeException(nameof(cols));
        if (rows <= 0) throw new ArgumentOutOfRangeException(nameof(rows));
        if (fovZoom is <= 0 or > 10) throw new ArgumentOutOfRangeException(nameof(fovZoom), "视野缩放必须在 0~10 之间（F-3.2.2-62）");
        if (referenceDieCount < 0) throw new ArgumentOutOfRangeException(nameof(referenceDieCount));
        if (referenceDieCount > cols * rows)
            throw new InvalidOperationException($"参考 Die 数量（{referenceDieCount}）超过可用 Die 总数（{cols * rows}）（F-3.2.2-67）");

        // 确定性等距散布：把 0..(total-1) 均分为 N 段，取每段中点（与 BR-25「分布较远」精神一致）。
        var selected = new List<DieCell>();
        int total = cols * rows;
        if (referenceDieCount > 0)
        {
            for (int i = 0; i < referenceDieCount; i++)
            {
                int index = (int)((i + 0.5) * total / referenceDieCount);
                index = Math.Clamp(index, 0, total - 1);
                selected.Add(new DieCell(index / cols, index % cols));
            }
        }

        int green = selected.Count;
        int blue = green;                     // 预聚焦点位与参考 Die 同点位
        int red = 0;                           // 参考 Die（可能没有）——本口径固定为 0
        int yellow = total - green;

        return new GoldenDiePlan
        {
            ReferenceDieCount = referenceDieCount,
            FovZoom = fovZoom,
            ReferenceDies = selected,
            YellowCount = yellow,
            BlueCount = blue,
            RedCount = red,
            GreenCount = green,
            EstimatedCostIndex = referenceDieCount      // BR-29：数量越大耗时越长（单调）
        };
    }

    /// <summary>BR-30：视野缩放修改后需重新生成参考图像（F-3.2.2-62）。</summary>
    public GoldenDiePlan ChangeFovZoom(GoldenDiePlan plan, double newFovZoom)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (newFovZoom is <= 0 or > 10) throw new ArgumentOutOfRangeException(nameof(newFovZoom));

        return plan with
        {
            FovZoom = newFovZoom,
            NeedsRegeneration = Math.Abs(newFovZoom - plan.FovZoom) > 1e-9
        };
    }

    /// <summary>F-3.2.2-64/66：Golden 生成失败或效果欠佳 → FOV 图谱手动调整（入口标记，不删除已规划点位）。</summary>
    public GoldenDiePlan MarkManualAdjusted(GoldenDiePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan with { ManualAdjusted = true };
    }
}
