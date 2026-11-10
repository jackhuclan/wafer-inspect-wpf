using WaferInspect.Core.Maps;

namespace WaferInspect.Core.Analysis;

/// <summary>
/// H08 复判执行（B-16）：人工 / 批量 / AI 复判。
/// AI 复判在模型未部署（T-035）时**只返回明确搁置结果，绝不伪造推理输出**（E-10）。
/// </summary>
public interface IRecheckService
{
    /// <summary>复判写回后通知（供界面刷新良率 / 列表）。</summary>
    event EventHandler? Changed;

    /// <summary>人工 / 快捷键复判：把指定 Die 重新分配为某 Bin。</summary>
    WaferResult? ManualRecheck(string waferId, int column, int row, BinKind kind);

    /// <summary>批量复判：把某一类 Bin 全部改写为另一类。</summary>
    WaferResult? BatchRecheck(string waferId, BinKind from, BinKind to);

    /// <summary>
    /// AI 复判：模型未部署（T-035）时返回 <c>Executed=false</c> 与明确搁置说明，不修改任何数据；
    /// 模型就绪后改为执行并写回。
    /// </summary>
    (WaferResult? Wafer, string Message, bool Executed) AiRecheck(string waferId);
}

public sealed class RecheckService : IRecheckService
{
    private readonly IWaferResultStore _store;

    public RecheckService(IWaferResultStore store) => _store = store;

    public event EventHandler? Changed;

    public WaferResult? ManualRecheck(string waferId, int column, int row, BinKind kind)
    {
        var wafer = _store.Get(waferId);
        if (wafer == null) return null;

        wafer = wafer.WithBin(column, row, kind);
        _store.Update(wafer);
        Raise();
        return wafer;
    }

    public WaferResult? BatchRecheck(string waferId, BinKind from, BinKind to)
    {
        var wafer = _store.Get(waferId);
        if (wafer == null) return null;

        wafer = wafer.WithBatchRecheck(from, to);
        _store.Update(wafer);
        Raise();
        return wafer;
    }

    public (WaferResult? Wafer, string Message, bool Executed) AiRecheck(string waferId)
    {
        var wafer = _store.Get(waferId);
        if (wafer == null) return (null, "Wafer 不存在", false);

        // T-035：AI 模型未部署，禁止伪造推理结果（E-10 / P1 设备与算法搁置边界）。
        return (wafer, "[搁置] AI 模型未部署（T-035），未执行推理，不伪造复判结果", false);
    }

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}
