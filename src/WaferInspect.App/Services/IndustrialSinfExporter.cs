namespace WaferInspectHMI.Services;

/// <summary>
/// 工业 Sinf 字节流导出器（预留实现位，T-037）。规范待确认前，<see cref="Export"/> 抛诚实异常，
/// 绝不伪造字节流。
/// T-037 规范到位后：实现真实字节序列化（字段 / 偏移 / 字节序 / 校验和），替换内部 CSV 为工业格式——
/// 仅需实现本类 <see cref="Export"/> 并切换 <c>SelectedSinfExporterKind = Industrial</c>，调用方与 UI 不变。
/// </summary>
public sealed class IndustrialSinfExporter : ISinfExporter
{
    public SinfExporterKind Kind => SinfExporterKind.Industrial;

    public bool IsIndustrial => true;

    public string SuggestedFileName(SinfExportContext ctx)
        => $"wafer_{(ctx.TypeId ?? "map")}_v{ctx.Map.RefreshVersion}.sinf";

    public void Export(SinfExportContext ctx, string filePath)
        => throw new SinfFormatNotConfirmedException();
}
