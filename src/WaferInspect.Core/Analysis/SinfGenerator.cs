namespace WaferInspect.Core.Analysis;

/// <summary>Sinf 生成结果（Core 只产出结论与内容，不接触 UI / 文件系统）。</summary>
public sealed record SinfGenerateResult(
    /// <summary>是否成功（T-037 未确认时为 false）。</summary>
    bool Success,
    /// <summary>面向用户的结果 / 失败提示。</summary>
    string Message,
    /// <summary>成功时为 Sinf 文本；失败为 null。</summary>
    string? Content);

/// <summary>
/// Sinf 文件**生成**（H05 唯一实现，B-12 / R-H05-06），导出动作由 H08 发起（B-13）。
/// 生成与导出职责分离：本服务是「生成」的唯一 Owner，结果导出统一走 <see cref="IResultExportService"/>。
/// </summary>
public interface ISinfGenerator
{
    /// <summary>
    /// 生成指定 Wafer 的 Sinf 内容。
    /// PRD <c>T-037</c> 的 Sinf 文件格式规范仍待确认，且底层生成逻辑（H05）本增量未实现，
    /// 故返回 <c>Success=false</c> + <c>[T-037 待确认]</c> 提示，不伪造工业格式（E-06 / E-10 边界）。
    /// </summary>
    SinfGenerateResult Generate(WaferResult wafer);
}

/// <summary>
/// <see cref="ISinfGenerator"/> 的默认实现：诚实占位。
/// 因 T-037 格式规范未确认，绝不臆造 Sinf 字节流；仅返回明确失败提示（与 1.3.0 的 Sinf 导出占位一致）。
/// </summary>
public sealed class SinfGenerator : ISinfGenerator
{
    public SinfGenerateResult Generate(WaferResult wafer)
    {
        ArgumentNullException.ThrowIfNull(wafer);
        return new SinfGenerateResult(false,
            "[T-037 待确认] Sinf 文件格式规范未确认（PRD T-037），且底层生成唯一实现属 H05（B-12 / R-H05-06）" +
            "尚未实现；暂不出文件，不伪造工业格式。", null);
    }
}
