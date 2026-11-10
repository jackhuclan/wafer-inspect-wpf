using WaferInspect.Core.Run;

namespace WaferInspect.Core.Analysis;

/// <summary>
/// 结果导出目标（B-13 / B-14 / R-H08-06）。
/// 复制类（Copy*）仅产出剪贴板内容，不写文件；其余为文件类目标。
/// </summary>
public enum ResultExportTarget
{
    /// <summary>B-14 缺陷详情报表。</summary>
    DefectDetailReport,

    /// <summary>B-14 全局量测报表。</summary>
    GlobalMeasurementReport,

    /// <summary>B-14 2D 量测报表（基于缺陷尺寸聚合）。</summary>
    Measurement2DReport,

    /// <summary>B-14 Klarf（工业专有格式，PRD T-037 文件格式规范待确认）。</summary>
    Klarf,

    /// <summary>B-12 / B-13 Sinf（底层生成唯一实现属 H05，格式 PRD T-037 待确认）。</summary>
    Sinf,

    /// <summary>B-14 复制相对路径。</summary>
    CopyRelativePath,

    /// <summary>B-14 复制 WaferId。</summary>
    CopyWaferId
}

/// <summary>一次导出请求的结果（Core 只生成内容，不接触 UI / 文件系统 / 剪贴板）。</summary>
public sealed record ExportResult(
    /// <summary>是否成功（Sinf / Klarf 在 T-037 未确认时为 false）。</summary>
    bool Success,
    /// <summary>面向用户的结果 / 失败提示。</summary>
    string Message,
    /// <summary>文件类为 CSV 文本；复制类为待写入剪贴板的字符串；失败为 null。</summary>
    string? Content,
    /// <summary>文件类目标的建议文件名；复制类为 null。</summary>
    string? SuggestedFileName,
    /// <summary>true=写文件（需 App 层弹 SaveFileDialog），false=复制剪贴板。</summary>
    bool IsFileTarget);

/// <summary>
/// H08 统一结果导出服务（R-H08-06）：提供全系统唯一的结果输出入口，
/// 不应由其他模块各实现一份导出逻辑（D-11 / AC-P3-05）。
/// 文件类目标的内容生成为自洽内部 CSV（字段来自 <see cref="WaferResult"/> 与 <see cref="DefectRecord"/>），
/// 不冒充 Klarf / Sinf 工业专有格式（PRD T-037 约束）。
/// </summary>
public interface IResultExportService
{
    /// <summary>全部可导出目标（供 UI 枚举）。</summary>
    IReadOnlyList<ResultExportTarget> Targets { get; }

    /// <summary>查询该目标是否已具备可用格式（Sinf / Klarf 在 T-037 未确认时为 false）。</summary>
    bool IsFormatSpecified(ResultExportTarget target);

    /// <summary>对指定 Wafer 生成导出结果（内容生成在 Core，不接触 UI / 文件系统 / 剪贴板）。</summary>
    ExportResult Export(WaferResult wafer, ResultExportTarget target);
}
