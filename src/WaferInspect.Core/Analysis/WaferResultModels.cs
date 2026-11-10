using WaferInspect.Core.Maps;
using WaferInspect.Core.Run;

namespace WaferInspect.Core.Analysis;

/// <summary>
/// 单枚 Wafer 的检测结果（H08 数据分析 / 人工复判的数据载体）。
/// 数据为**确定性合成**：设备采集（T-029）与 AI 推理（T-035）尚未接入，
/// 界面与导出一律标注「[模拟结果]」，禁止伪造设备成功（E-10 / P1 设备搁置边界）。
/// </summary>
public sealed record WaferResult(
    string WaferId,
    string LotId,
    string ToolId,
    string MapTypeId,
    int Rows,
    int Columns,
    IReadOnlyList<BinKind> BinMatrix,
    IReadOnlyList<DefectRecord> Defects,
    int PreReviewDefectCount,
    int ReviewedCount,
    bool AiReviewed)
{
    /// <summary>设计 Die 数（F-3.9.2-02 统计口径）。</summary>
    public int DesignDieCount => Rows * Columns;

    /// <summary>
    /// F-3.9.2-08：复判后良率。
    /// 未复判（ReviewedCount==0）按手册显示 <c>0.00%</c>（禁止清单项 8：不得统一成空或自定格式）。
    /// </summary>
    public double PostReviewYield =>
        ReviewedCount == 0 ? 0d : (DesignDieCount - PostReviewDefectCount) / (double)DesignDieCount;

    /// <summary>复判后缺陷 Die 数：当前矩阵中 DefectDie / ExceptionDie 之和。</summary>
    public int PostReviewDefectCount =>
        BinMatrix.Count(k => k is BinKind.DefectDie or BinKind.ExceptionDie);

    /// <summary>F-3.9.2-07：复判前良率 = (设计 Die − 复判前缺陷) / 设计 Die。</summary>
    public double PreReviewYield =>
        DesignDieCount == 0 ? 0d : (DesignDieCount - PreReviewDefectCount) / (double)DesignDieCount;

    /// <summary>
    /// F-3.9.2-09：AI 复判后良率 = OK 数量 / 检测数量；AI 模型未部署时按手册显示 <c>0.00%</c>。
    /// </summary>
    public double AiReviewYield =>
        AiReviewed ? (DesignDieCount - PostReviewDefectCount) / (double)DesignDieCount : 0d;

    /// <summary>
    /// 人工 / 快捷键复判：把指定 Die 重新分配为某 Bin。
    /// 同时把该坐标上既有缺陷的复判状态置为「已复判」，保证 Bin 矩阵与缺陷列表（B-23）一致。
    /// </summary>
    public WaferResult WithBin(int column, int row, BinKind kind)
    {
        int idx = row * Columns + column;
        if (idx < 0 || idx >= BinMatrix.Count) return this;

        var matrix = BinMatrix.ToList();
        matrix[idx] = kind;
        var defects = Defects
            .Select(d => d.Row == row && d.Column == column ? d with { ReviewState = "已复判" } : d)
            .ToList();
        return this with { BinMatrix = matrix, Defects = defects, ReviewedCount = ReviewedCount + 1 };
    }

    /// <summary>批量复判：把某一类 Bin 全部改写为另一类，返回实际变更数与新结果。</summary>
    public WaferResult WithBatchRecheck(BinKind from, BinKind to)
    {
        int changed = BinMatrix.Count(k => k == from);
        if (changed == 0) return this;
        var matrix = BinMatrix.Select(k => k == from ? to : k).ToList();
        var defects = Defects
            .Select(d => d.ReviewState == "未复判" ? d with { ReviewState = "已复判" } : d)
            .ToList();
        return this with { BinMatrix = matrix, Defects = defects, ReviewedCount = ReviewedCount + changed };
    }

    /// <summary>AI 复判：仅标记已执行；模型未部署（T-035）时不应调用本方法伪造结果。</summary>
    public WaferResult WithAiReviewed() => this with { AiReviewed = true };
}
