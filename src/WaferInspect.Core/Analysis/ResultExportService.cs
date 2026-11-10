using System.Globalization;
using WaferInspect.Core.Run;

namespace WaferInspect.Core.Analysis;

/// <summary>
/// <see cref="IResultExportService"/> 的默认实现。
/// 报表类目标（缺陷详情 / 全局量测 / 2D 量测）生成自洽内部 CSV，字段来自
/// <see cref="WaferResult"/> 与 <see cref="DefectRecord"/>（B-23 唯一模型）；
/// Sinf / Klarf 因 PRD <c>T-037</c> 文件格式规范未确认，显式返回失败，不伪造工业格式。
/// </summary>
public sealed class ResultExportService : IResultExportService
{
    private static readonly ResultExportTarget[] AllTargets =
    {
        ResultExportTarget.DefectDetailReport,
        ResultExportTarget.GlobalMeasurementReport,
        ResultExportTarget.Measurement2DReport,
        ResultExportTarget.Klarf,
        ResultExportTarget.Sinf,
        ResultExportTarget.CopyRelativePath,
        ResultExportTarget.CopyWaferId
    };

    public IReadOnlyList<ResultExportTarget> Targets => AllTargets;

    /// <summary>
    /// Sinf / Klarf 的格式规范（PRD T-037）仍待确认，标记为未就绪；其余自洽内部格式视为已就绪。
    /// </summary>
    public bool IsFormatSpecified(ResultExportTarget target) =>
        target is not (ResultExportTarget.Klarf or ResultExportTarget.Sinf);

    public ExportResult Export(WaferResult wafer, ResultExportTarget target)
    {
        ArgumentNullException.ThrowIfNull(wafer);

        switch (target)
        {
            case ResultExportTarget.DefectDetailReport:
                return FileResult(wafer, $"{wafer.WaferId}_defect_detail.csv", BuildDefectCsv(wafer));
            case ResultExportTarget.GlobalMeasurementReport:
                return FileResult(wafer, $"{wafer.WaferId}_measurement_global.csv", BuildGlobalCsv(wafer));
            case ResultExportTarget.Measurement2DReport:
                return FileResult(wafer, $"{wafer.WaferId}_measurement_2d.csv", Build2DCsv(wafer));
            case ResultExportTarget.Klarf:
                return Fail("[T-037 待确认] Klarf 文件格式规范未确认（PRD T-037），暂不出文件，不伪造工业格式。");
            case ResultExportTarget.Sinf:
                return Fail("[T-037 待确认] Sinf 文件格式规范未确认（PRD T-037）；且底层生成唯一实现属 H05（B-12）" +
                            "尚未实现。暂不出文件，不伪造工业格式。");
            case ResultExportTarget.CopyRelativePath:
                return Copy($"results/{wafer.LotId}/{wafer.WaferId}/",
                    "已生成相对路径（设备采集 T-029 未接入，路径为结果目录模板）");
            case ResultExportTarget.CopyWaferId:
                return Copy(wafer.WaferId, "已复制 WaferId");
            default:
                return Fail($"未知导出目标：{target}");
        }
    }

    private static ExportResult FileResult(WaferResult wafer, string fileName, string csv) =>
        new(true, $"已生成 {fileName}（内部 CSV 格式，不冒充 Klarf/Sinf 工业标准）", csv, fileName, true);

    private static ExportResult Copy(string content, string message) =>
        new(true, message, content, null, false);

    private static ExportResult Fail(string message) =>
        new(false, message, null, null, true);

    private static string BuildDefectCsv(WaferResult wafer)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("WaferId,LotId,ToolId,DieRow,DieColumn,Zone,Category,Length_um,Width_um,Area_um2,AI分类,AIBin,复判状态,备注,原始图片路径");
        foreach (var d in wafer.Defects)
        {
            sb.AppendLine(string.Join(",",
                Csv(wafer.WaferId), Csv(wafer.LotId), Csv(wafer.ToolId),
                d.Row, d.Column, Csv(d.Zone), Csv(d.Category),
                d.Length.ToString(CultureInfo.InvariantCulture),
                d.Width.ToString(CultureInfo.InvariantCulture),
                d.Area.ToString(CultureInfo.InvariantCulture),
                Csv(d.AiClassification), Csv(d.AiBin), Csv(d.ReviewState), Csv(d.Note), Csv(d.SourceImagePath)));
        }
        return sb.ToString();
    }

    private static string BuildGlobalCsv(WaferResult wafer)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("WaferId,LotId,ToolId,MapType,Rows,Columns,设计Die,复判前缺陷,复判数,复判前良率%,复判后良率%,AI复判后良率%,AI已复判");
        sb.AppendLine(string.Join(",",
            Csv(wafer.WaferId), Csv(wafer.LotId), Csv(wafer.ToolId), Csv(wafer.MapTypeId),
            wafer.Rows, wafer.Columns, wafer.DesignDieCount, wafer.PreReviewDefectCount, wafer.ReviewedCount,
            wafer.PreReviewYield.ToString("F4", CultureInfo.InvariantCulture),
            wafer.PostReviewYield.ToString("F4", CultureInfo.InvariantCulture),
            wafer.AiReviewYield.ToString("F4", CultureInfo.InvariantCulture),
            wafer.AiReviewed ? "是" : "否"));
        return sb.ToString();
    }

    private static string Build2DCsv(WaferResult wafer)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("WaferId,LotId,ToolId,缺陷数,缺陷面积合计_um2,最大长度_um,最大宽度_um,平均面积_um2");
        double areaSum = 0, maxLen = 0, maxWid = 0;
        foreach (var d in wafer.Defects)
        {
            areaSum += d.Area;
            if (d.Length > maxLen) maxLen = d.Length;
            if (d.Width > maxWid) maxWid = d.Width;
        }
        double avg = wafer.Defects.Count == 0 ? 0 : areaSum / wafer.Defects.Count;
        sb.AppendLine(string.Join(",",
            Csv(wafer.WaferId), Csv(wafer.LotId), Csv(wafer.ToolId),
            wafer.Defects.Count,
            areaSum.ToString(CultureInfo.InvariantCulture),
            maxLen.ToString(CultureInfo.InvariantCulture),
            maxWid.ToString(CultureInfo.InvariantCulture),
            avg.ToString(CultureInfo.InvariantCulture)));
        return sb.ToString();
    }

    /// <summary>CSV 单字段转义：含逗号 / 引号 / 换行时整体双引号包裹并转义内部引号。</summary>
    private static string Csv(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }
}
