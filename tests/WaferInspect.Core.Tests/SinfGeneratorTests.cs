#nullable enable
using WaferInspect.Core.Analysis;
using WaferInspect.Core.Maps;
using WaferInspect.Core.Run;
using Xunit;

namespace WaferInspect.Core.Tests;

/// <summary>
/// P3 增量 8（H08）：B-12 Sinf 生成（H05 唯一实现，R-H05-06）。
/// PRD T-037 的 Sinf 文件格式规范仍待确认，本服务仅诚实占位，不伪造工业格式（E-06 / E-10）。
/// </summary>
public sealed class SinfGeneratorTests
{
    private static WaferResult Sample()
    {
        var matrix = new BinKind[] { BinKind.GoodDie, BinKind.DefectDie, BinKind.ForBiddenDie, BinKind.ReferenceDie };
        return new WaferResult(
            WaferId: "WFR-900",
            LotId: "LOT-TEST",
            ToolId: "T9",
            MapTypeId: "MAP-PL-300",
            Rows: 2,
            Columns: 2,
            BinMatrix: matrix,
            Defects: [],
            PreReviewDefectCount: 1,
            ReviewedCount: 0,
            AiReviewed: false);
    }

    [Fact]
    [BusinessRule("B-12")]
    public void Generate_T037Unspecified_ReturnsSuccessFalse_AndDeferredMessage()
    {
        var gen = new SinfGenerator();

        var result = gen.Generate(Sample());

        // B-12 / T-037：格式规范未确认，绝不臆造 Sinf 字节流。
        Assert.False(result.Success);
        Assert.Null(result.Content);
        Assert.Contains("[T-037 待确认]", result.Message);
        Assert.Contains("不伪造工业格式", result.Message);
    }

    [Fact]
    public void Generate_NullWafer_Throws()
    {
        var gen = new SinfGenerator();
        Assert.Throws<System.ArgumentNullException>(() => gen.Generate(null!));
    }
}
