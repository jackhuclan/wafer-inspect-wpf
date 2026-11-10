using WaferInspect.Core.Recipes;

namespace WaferInspectHMI.ViewModels.Modules;

/// <summary>
/// Job 列表行（H02）：只读投影，供 DataGrid 直接绑定；写操作一律经 <see cref="IJobRepository"/>。
/// </summary>
public sealed class JobRow
{
    public JobRow(JobDefinition source, string mapTypeName)
    {
        Source = source;
        MapTypeName = mapTypeName;
    }

    public JobDefinition Source { get; }

    public string JobId => Source.JobId;

    public string Name => Source.Name;

    public string MapTypeName { get; }

    public string PatternText => Source.PatternType.Text();

    public string SizeText => Source.Size.Text();

    /// <summary>Die 尺寸摘要（F-3.2.1-11/12）。</summary>
    public string DieText => $"{Source.DieXmm:0.###} × {Source.DieYmm:0.###} mm";

    public string MapFileText => string.IsNullOrWhiteSpace(Source.MapFilePath)
        ? "—"
        : System.IO.Path.GetFileName(Source.MapFilePath);

    public string BarcodeText => string.IsNullOrWhiteSpace(Source.BarcodeCode) ? "—" : Source.BarcodeCode;

    /// <summary>回溯图策略摘要（BR-15）：未设置表示继承全局。</summary>
    public string TraceImageText => Source.TraceImage is TraceImagePolicy policy ? policy.Text() : "继承系统";
}
