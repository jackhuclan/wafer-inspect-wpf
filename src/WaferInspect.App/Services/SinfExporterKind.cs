namespace WaferInspectHMI.Services;

/// <summary>
/// Sinf 导出格式种类（BR-50 / B-12）。T-037 工业字节流规范待确认前，仅 InternalCsv 可用；
/// Industrial 为预留实现位，当前不可选、不伪造（E-08 / E-10）。
/// </summary>
public enum SinfExporterKind
{
    /// <summary>自洽内部 Map 表示（CSV），诚实标注非工业 Sinf 字节流；当前默认可用。</summary>
    InternalCsv,

    /// <summary>工业 Sinf 字节流（T-037）。规范待确认，预留实现位，当前不可选 / 不伪造。</summary>
    Industrial
}
