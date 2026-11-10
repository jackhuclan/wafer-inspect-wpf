using System;

namespace WaferInspectHMI.Services;

/// <summary>
/// 工业 Sinf 格式未确认时抛出的诚实异常。用于在 T-037 规范到位前，阻断任何伪造工业字节流的尝试
/// （E-08 / E-10：依赖 T-037 的格式能力必须诚实占位，绝不伪造工业格式 / 设备成功）。
/// </summary>
public sealed class SinfFormatNotConfirmedException : NotSupportedException
{
    public SinfFormatNotConfirmedException()
        : base("T-037 Sinf 工业字节流格式规范待确认；拒绝伪造工业格式。请确认规范后实现 IndustrialSinfExporter.Export，并切换 SelectedSinfExporterKind = Industrial。") { }
}
