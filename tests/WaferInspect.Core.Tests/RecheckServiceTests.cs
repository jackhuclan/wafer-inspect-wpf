#nullable enable
using WaferInspect.Core.Analysis;
using WaferInspect.Core.Maps;
using Xunit;

namespace WaferInspect.Core.Tests;

/// <summary>
/// P3 增量 6（H08）：B-16 复判执行（人工 / 批量 / AI）的写回与变更通知。
/// AI 复判在模型未部署（T-035）时**只返回明确搁置结果，绝不伪造推理输出**（E-10 / P1 设备与算法搁置边界）。
/// </summary>
public sealed class RecheckServiceTests
{
    private static (WaferResultStore Store, RecheckService Svc) New()
    {
        var store = new WaferResultStore();
        var svc = new RecheckService(store);
        return (store, svc);
    }

    [Fact]
    public void ManualRecheck_WritesBack_BumpsReviewedCount_AndMarksDefectReviewed()
    {
        var (store, svc) = New();
        var wafer = store.Get("WFR-201")!;
        int idx = wafer.BinMatrix.ToList().FindIndex(k => k is BinKind.DefectDie or BinKind.ExceptionDie);
        int row = idx / wafer.Columns;
        int col = idx % wafer.Columns;
        int before = wafer.ReviewedCount;

        int raised = 0;
        svc.Changed += (_, _) => raised++;

        var result = svc.ManualRecheck("WFR-201", col, row, BinKind.GoodDie);

        Assert.NotNull(result);
        Assert.Equal(1, raised);
        Assert.Equal(BinKind.GoodDie, store.Get("WFR-201")!.BinMatrix[idx]);
        Assert.Equal(before + 1, store.Get("WFR-201")!.ReviewedCount);
        Assert.Contains(
            store.Get("WFR-201")!.Defects,
            d => d.Row == row && d.Column == col && d.ReviewState == "已复判");
    }

    [Fact]
    public void BatchRecheck_RewritesAllOfKind_AndIncrementsReviewedCount()
    {
        var (store, svc) = New();
        var wafer = store.Get("WFR-202")!;
        int forbidden = wafer.BinMatrix.Count(k => k == BinKind.ForBiddenDie);
        Assert.True(forbidden > 0, "种子 Wafer 最外圈固定为 ForBiddenDie，保证本断言确定性");
        int before = wafer.ReviewedCount;

        var result = svc.BatchRecheck("WFR-202", BinKind.ForBiddenDie, BinKind.GoodDie);

        Assert.NotNull(result);
        Assert.Equal(0, store.Get("WFR-202")!.BinMatrix.Count(k => k == BinKind.ForBiddenDie));
        Assert.Equal(before + forbidden, store.Get("WFR-202")!.ReviewedCount);
    }

    [Fact]
    public void AiRecheck_DoesNotFakeResult_ReturnsExecutedFalse_AndDeferredMessage()
    {
        var (store, svc) = New();
        bool raised = false;
        svc.Changed += (_, _) => raised = true;

        var (wafer, message, executed) = svc.AiRecheck("WFR-203");

        Assert.False(executed);
        Assert.NotNull(wafer);
        Assert.Contains("[搁置]", message);
        Assert.Contains("T-035", message);
        Assert.False(store.Get("WFR-203")!.AiReviewed);   // 不伪造、不写回
        Assert.False(raised);                               // 不触发变化通知
    }

    [Fact]
    public void ManualRecheck_UnknownWafer_ReturnsNull()
    {
        var (_, svc) = New();
        Assert.Null(svc.ManualRecheck("NOPE", 1, 1, BinKind.GoodDie));
    }

    [Fact]
    public void BatchRecheck_UnknownWafer_ReturnsNull()
    {
        var (_, svc) = New();
        Assert.Null(svc.BatchRecheck("NOPE", BinKind.DefectDie, BinKind.GoodDie));
    }
}
