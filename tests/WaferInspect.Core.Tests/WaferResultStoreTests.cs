#nullable enable
using WaferInspect.Core.Analysis;
using WaferInspect.Core.Maps;
using WaferInspect.Core.Run;
using Xunit;

namespace WaferInspect.Core.Tests;

/// <summary>
/// P3 增量 6（H08 数据分析 / 人工复判）：B-23 缺陷模型载体 <see cref="WaferResultStore"/> 的
/// 确定性种子、检索筛选、写回与变更通知。设备采集（T-029）/ AI 推理（T-035）尚未接入，
/// 缺陷原始图片路径一律标注「[搁置]」，界面层据此向用户明示。
/// </summary>
public sealed class WaferResultStoreTests
{
    [Fact]
    public void Seed_ProducesSixWafers_WithDeterministicIds()
    {
        var store = new WaferResultStore();
        Assert.Equal(6, store.Wafers.Count);
        Assert.Equal(
            new[] { "WFR-042", "WFR-043", "WFR-044", "WFR-201", "WFR-202", "WFR-203" },
            store.Wafers.Select(w => w.WaferId));
    }

    [Fact]
    public void Seed_IsDeterministic_AcrossInstances()
    {
        var a = new WaferResultStore();
        var b = new WaferResultStore();
        Assert.Equal(
            a.Wafers.Select(w => string.Join(",", w.BinMatrix.Select(k => k.ToString()))),
            b.Wafers.Select(w => string.Join(",", w.BinMatrix.Select(k => k.ToString()))));
    }

    [Fact]
    public void Seed_GroupsByLot_AndToolIds()
    {
        var store = new WaferResultStore();
        Assert.Equal(3, store.Query(lotId: "LOT-A-2601").Count);
        Assert.Equal(3, store.Query(lotId: "LOT-B-2602").Count);
        Assert.Contains(store.Wafers, w => w.WaferId == "WFR-044" && w.ToolId == "T2");
        Assert.Equal(3, store.Query(toolId: "T1").Count);
        Assert.Single(store.Query(waferId: "WFR-201"));
    }

    [Fact]
    public void Seed_MarksForbiddenDieOnBorder_AndCountsPreReviewDefects()
    {
        var store = new WaferResultStore();
        foreach (var w in store.Wafers)
        {
            for (int c = 0; c < w.Columns; c++)
            {
                Assert.Equal(BinKind.ForBiddenDie, w.BinMatrix[0 * w.Columns + c]);
                Assert.Equal(BinKind.ForBiddenDie, w.BinMatrix[(w.Rows - 1) * w.Columns + c]);
            }
            int expected = w.BinMatrix.Count(k => k is BinKind.DefectDie or BinKind.ExceptionDie);
            Assert.Equal(expected, w.PreReviewDefectCount);
        }
    }

    [Fact]
    public void Defects_CarryDeferredSourceImagePath()
    {
        var store = new WaferResultStore();
        var defects = store.Wafers.SelectMany(w => w.Defects).ToList();
        Assert.NotEmpty(defects);
        Assert.All(defects, d => Assert.Contains("[搁置]", d.SourceImagePath));
    }

    [Fact]
    public void Get_ReturnsMatchingWafer_OrNull()
    {
        var store = new WaferResultStore();
        Assert.Equal("LOT-A-2601", store.Get("WFR-042")!.LotId);
        Assert.Null(store.Get("NOPE"));
    }

    [Fact]
    public void Update_RaisesChanged_AndReplacesInPlace()
    {
        var store = new WaferResultStore();
        int raised = 0;
        store.Changed += (_, _) => raised++;

        var wafer = store.Get("WFR-042")!;
        var updated = wafer.WithBin(5, 5, BinKind.GoodDie);
        store.Update(updated);

        Assert.Equal(1, raised);
        Assert.Same(updated, store.Get("WFR-042"));
        Assert.Equal(1, store.Get("WFR-042")!.ReviewedCount);
    }

    [Fact]
    public void Query_SupportsLotIdSubstring_AndWaferIdContains()
    {
        var store = new WaferResultStore();
        Assert.Equal(3, store.Query(lotId: "LOT-A").Count);
        Assert.Single(store.Query(waferId: "WFR-201"));
        Assert.Empty(store.Query(lotId: "NON-EXISTENT-LOT"));
    }
}
