using System.Collections.Generic;
using System.Linq;
using WaferInspect.Core.Analysis;
using WaferInspect.Core.Maps;
using WaferInspect.Core.Run;
using Xunit;

namespace WaferInspect.Core.Tests;

/// <summary>
/// 结果导出服务（B-13 / B-14 / R-H08-06）：报表类生成自洽 CSV，Sinf / Klarf 在 T-037 未确认时显式失败，
/// 复制类返回剪贴板内容。覆盖 BR-65 与 T-037 诚实占位边界。
/// </summary>
public sealed class ResultExportServiceTests
{
    private static WaferResult SampleWafer()
    {
        var defects = new List<DefectRecord>
        {
            new("A", "Particle", 2, 3, 1.2, 0.8, 0.96,
                ReviewState: "未复判", SourceImagePath: "[搁置] 原始图片路径未接入（T-029）"),
            new("B", "Scratch", 4, 5, 3.0, 1.0, 3.0, ReviewState: "已复判")
        };
        var matrix = Enumerable.Repeat(BinKind.GoodDie, 100).ToList();
        return new WaferResult(
            WaferId: "WFR-TEST", LotId: "LOT-T", ToolId: "T1", MapTypeId: "map-a",
            Rows: 10, Columns: 10, BinMatrix: matrix, Defects: defects,
            PreReviewDefectCount: 2, ReviewedCount: 1, AiReviewed: false);
    }

    [Fact]
    public void IsFormatSpecified_SinfAndKlarfFalse_OthersTrue()
    {
        var svc = new ResultExportService();
        Assert.False(svc.IsFormatSpecified(ResultExportTarget.Sinf));
        Assert.False(svc.IsFormatSpecified(ResultExportTarget.Klarf));
        Assert.True(svc.IsFormatSpecified(ResultExportTarget.DefectDetailReport));
        Assert.True(svc.IsFormatSpecified(ResultExportTarget.GlobalMeasurementReport));
        Assert.True(svc.IsFormatSpecified(ResultExportTarget.Measurement2DReport));
        Assert.True(svc.IsFormatSpecified(ResultExportTarget.CopyWaferId));
        Assert.True(svc.IsFormatSpecified(ResultExportTarget.CopyRelativePath));
    }

    [Fact, BusinessRule("BR-65")]
    public void DefectDetailReport_ProducesCsv_WithHeaderAndRows()
    {
        var svc = new ResultExportService();
        var r = svc.Export(SampleWafer(), ResultExportTarget.DefectDetailReport);
        Assert.True(r.Success);
        Assert.True(r.IsFileTarget);
        Assert.EndsWith("_defect_detail.csv", r.SuggestedFileName);
        Assert.Contains("WaferId,LotId,ToolId,DieRow,DieColumn,Zone,Category", r.Content);
        Assert.Contains("WFR-TEST", r.Content);
        Assert.Contains("Particle", r.Content);   // 数据行 1
        Assert.Contains("Scratch", r.Content);    // 数据行 2
    }

    [Fact, BusinessRule("BR-65")]
    public void GlobalMeasurementReport_ContainsYieldColumns()
    {
        var svc = new ResultExportService();
        var r = svc.Export(SampleWafer(), ResultExportTarget.GlobalMeasurementReport);
        Assert.True(r.Success);
        Assert.Contains("设计Die", r.Content);
        Assert.Contains("复判前良率%", r.Content);
        Assert.Contains("WFR-TEST", r.Content);
        Assert.Contains("LOT-T", r.Content);
    }

    [Fact]
    public void Measurement2DReport_AggregatesDefectSizes()
    {
        var svc = new ResultExportService();
        var r = svc.Export(SampleWafer(), ResultExportTarget.Measurement2DReport);
        Assert.True(r.Success);
        Assert.Contains("缺陷数", r.Content);
        Assert.Contains("WFR-TEST", r.Content);
        Assert.Contains(",2,", r.Content); // 缺陷数 = 2
    }

    [Fact, BusinessRule("BR-65")]
    public void Klarf_ReturnsT037DeferredFailure()
    {
        var svc = new ResultExportService();
        var r = svc.Export(SampleWafer(), ResultExportTarget.Klarf);
        Assert.False(r.Success);
        Assert.Contains("[T-037 待确认]", r.Message);
        Assert.Null(r.Content);
        Assert.True(r.IsFileTarget);
    }

    [Fact, BusinessRule("BR-65")]
    public void Sinf_ReturnsT037DeferredFailure()
    {
        var svc = new ResultExportService();
        var r = svc.Export(SampleWafer(), ResultExportTarget.Sinf);
        Assert.False(r.Success);
        Assert.Contains("[T-037 待确认]", r.Message);
        Assert.Null(r.Content);
    }

    [Fact, BusinessRule("BR-65")]
    public void CopyWaferId_ReturnsClipboardContent()
    {
        var svc = new ResultExportService();
        var r = svc.Export(SampleWafer(), ResultExportTarget.CopyWaferId);
        Assert.True(r.Success);
        Assert.False(r.IsFileTarget);
        Assert.Equal("WFR-TEST", r.Content);
        Assert.Contains("复制", r.Message);
    }

    [Fact, BusinessRule("BR-65")]
    public void CopyRelativePath_ReturnsTemplatePath()
    {
        var svc = new ResultExportService();
        var r = svc.Export(SampleWafer(), ResultExportTarget.CopyRelativePath);
        Assert.True(r.Success);
        Assert.False(r.IsFileTarget);
        Assert.StartsWith("results/", r.Content);
        Assert.Contains("LOT-T", r.Content);
        Assert.Contains("WFR-TEST", r.Content);
    }

    [Fact]
    public void Targets_ContainsAllSeven()
    {
        var svc = new ResultExportService();
        Assert.Equal(7, svc.Targets.Count);
    }
}
