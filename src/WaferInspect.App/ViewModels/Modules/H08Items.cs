using WaferInspect.Core.Analysis;
using WaferInspect.Core.Maps;

namespace WaferInspectHMI.ViewModels.Modules;

/// <summary>Wafer 检索列表行（F-3.9.2-01~21 投影）：Lot / Wafer / 机台 / 设计 Die / 缺陷 / 复判数 / 三档良率。</summary>
public sealed class WaferRow
{
    public WaferRow(WaferResult source) => Source = source;

    public WaferResult Source { get; }

    public string WaferId => Source.WaferId;
    public string LotId => Source.LotId;
    public string ToolId => Source.ToolId;
    public int DesignDieCount => Source.DesignDieCount;
    public int PreReviewDefectCount => Source.PreReviewDefectCount;
    public int ReviewedCount => Source.ReviewedCount;

    /// <summary>F-3.9.2-07：复判前良率。</summary>
    public string PreReviewYieldText => $"{Source.PreReviewYield * 100:F2}%";

    /// <summary>F-3.9.2-08：复判后良率（未复判显示 0.00%，禁止清单项 8）。</summary>
    public string PostReviewYieldText => $"{Source.PostReviewYield * 100:F2}%";

    /// <summary>F-3.9.2-09：AI 复判后良率（AI 未部署显示 0.00%）。</summary>
    public string AiReviewYieldText => Source.AiReviewed ? $"{Source.AiReviewYield * 100:F2}%" : "0.00%";

    public string Display => $"{WaferId} · {LotId} · {ToolId}";
}

/// <summary>H08 复判目标 Bin 选项（来自 IMapTypeCatalog，B-11 唯一数据源），供人工 / 批量复判下拉选择。</summary>
public sealed class H08BinOption
{
    public H08BinOption(BinKind kind, string code, string name, string shortcut)
    {
        Kind = kind;
        Code = code;
        Name = name;
        Shortcut = shortcut;
    }

    public BinKind Kind { get; }
    public string Code { get; }
    public string Name { get; }
    public string Shortcut { get; }
    public string Display => $"{Code} · {Name}（快捷键 {Shortcut}）";
}

/// <summary>结果导出目标项（R-H08-06 / B-13 / B-14）：供 UI 下拉枚举，T-037 未确认目标显式标注。</summary>
public sealed class ExportTargetItem
{
    public ExportTargetItem(ResultExportTarget target, IResultExportService service)
    {
        Target = target;
        IsFormatSpecified = service.IsFormatSpecified(target);
    }

    public ResultExportTarget Target { get; }
    public bool IsFormatSpecified { get; }

    public string Display => Target switch
    {
        ResultExportTarget.DefectDetailReport => "缺陷详情报表 (CSV)",
        ResultExportTarget.GlobalMeasurementReport => "全局量测报表 (CSV)",
        ResultExportTarget.Measurement2DReport => "2D 量测报表 (CSV)",
        ResultExportTarget.Klarf => "Klarf [T-037 待确认]",
        ResultExportTarget.Sinf => "Sinf [T-037 待确认]",
        ResultExportTarget.CopyRelativePath => "复制相对路径",
        ResultExportTarget.CopyWaferId => "复制 WaferId",
        _ => Target.ToString()
    };
}
