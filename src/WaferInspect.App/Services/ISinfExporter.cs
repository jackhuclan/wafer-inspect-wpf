namespace WaferInspectHMI.Services;

/// <summary>
/// Sinf 导出器抽象（BR-50 / B-12）。实现可插拔：当前默认 InternalCsv（诚实内部 CSV），
/// 工业字节流实现（Industrial）预留，规范（T-037）到位即 drop-in 替换，不伪造任何格式。
/// 调用方负责文件路径选择（对话框），导出器只负责把 <see cref="SinfExportContext"/> 序列化为目标格式。
/// </summary>
public interface ISinfExporter
{
    /// <summary>导出格式种类。</summary>
    SinfExporterKind Kind { get; }

    /// <summary>是否为工业字节流（真实设备格式）。InternalCsv 为 false；Industrial 为 true。</summary>
    bool IsIndustrial { get; }

    /// <summary>建议的导出文件名（含扩展名），供对话框预填。</summary>
    string SuggestedFileName(SinfExportContext ctx);

    /// <summary>
    /// 将 <paramref name="ctx"/> 写入 <paramref name="filePath"/>。失败时抛异常（由调用方捕获并诚实提示）。
    /// 工业实现在 T-037 未确认时抛 <see cref="SinfFormatNotConfirmedException"/>，绝不伪造字节流。
    /// </summary>
    void Export(SinfExportContext ctx, string filePath);
}
