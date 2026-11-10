namespace WaferInspect.Core.Run;

/// <summary>上料模式：手动 / 自动 / 半自动三态互斥（F-3.1.2-07）。</summary>
public enum LoadMode
{
    Manual,
    Auto,
    SemiAuto
}

/// <summary>条码设置：仅正面 / 反面（BR-03）。</summary>
public enum BarcodeSide
{
    Front,
    Back
}

/// <summary>料盒槽位；WaferId 由 OCR 自动填充且可编辑（F-3.1.2-03，OCR 设备接入搁置 T-029）。</summary>
public sealed class CassetteSlot
{
    public CassetteSlot(int index) => Index = index;

    /// <summary>槽位行号 1~25（4.1.2 布局；T-021 已确认：Port1/Port2 各 25 槽，固定编号 1~25）。</summary>
    public int Index { get; }

    public string WaferId { get; set; } = string.Empty;

    /// <summary>料盒感知同步的 Wafer 在位状态（F-3.1.2-01，真实感知由 EFEM 提供，模拟数据源替代）。</summary>
    public bool HasWafer { get; set; }

    /// <summary>勾选指定本次任务需检测的晶圆（F-3.1.2-05）。</summary>
    public bool IsChecked { get; set; }

    /// <summary>Wafer 运行状态文本（F-3.1.2-04）。</summary>
    public string StateText { get; set; } = "待机";
}

/// <summary>LoadPort 料盒状态；料盒 ID 固定 Port1 / Port2（BR-04）。</summary>
public sealed class LoadPortState
{
    public LoadPortState(string portId)
    {
        if (portId is not ("Port1" or "Port2"))
            throw new ArgumentException("料盒 ID 固定为 Port1 / Port2（BR-04）。", nameof(portId));
        PortId = portId;
    }

    public string PortId { get; }

    /// <summary>料盒是否就位（料盒感知，模拟数据源）。</summary>
    public bool CassettePresent { get; set; }

    /// <summary>料盒锁定状态（EFEM 上/下料盒互斥切换，F-3.1.2-20/21）。</summary>
    public bool IsLocked { get; set; }

    /// <summary>蓝色高亮表示当前选中 LoadPort（F-3.1.2-02）。</summary>
    public bool IsCurrent { get; set; }

    public BarcodeSide BarcodeSide { get; set; } = BarcodeSide.Front;

    /// <summary>条码角度仅 0/90/180/270°（BR-02）。</summary>
    public int BarcodeAngle { get; set; }

    public List<CassetteSlot> Slots { get; } = [];

    public static LoadPortState CreateWithSlots(string portId, int slotCount, Func<int, bool> hasWafer)
    {
        var port = new LoadPortState(portId);
        for (int i = 1; i <= slotCount; i++)
        {
            var slot = new CassetteSlot(i) { HasWafer = hasWafer(i) };
            if (slot.HasWafer)
                slot.WaferId = $"{portId}_{i:00}";
            port.Slots.Add(slot);
        }
        return port;
    }
}

/// <summary>扫描进度 7 项（F-3.1.3-01~07）。ReviewCount 为彩色相机复判个数，语义独立于 H08 人工复判数。</summary>
public sealed record ScanProgress(
    double Percent,
    TimeSpan Elapsed,
    int ScannedDies,
    int TotalDefects,
    int DefectFreeDies,
    int DefectiveDies,
    int ReviewCount);

/// <summary>批次数据固定三类（F-3.1.7-01，BR-11 不可增删）。</summary>
public sealed record BatchStats(int NormalDies, int DefectDies, int FailedDies)
{
    public int TotalDies => NormalDies + DefectDies + FailedDies;
}

/// <summary>缺陷记录 7 列（F-3.1.8-01~07）；单位 μm/像素按原样（BR-12，T-015 登记）。与 H08 共用模型（B-23）。</summary>
public sealed record DefectRecord(
    string Zone,
    string Category,
    int Row,
    int Column,
    double Length,
    double Width,
    double Area);

/// <summary>待检测 Job 信息（F-3.1.5-07 四项）；真实 Job CRUD 属 P2，本阶段为模拟清单。</summary>
public sealed record JobSelection(
    string JobName,
    string RecipeName,
    double WaferSize,
    double DieSizeX,
    double DieSizeY,
    int WaferCount,
    int Rows,
    int Columns);

/// <summary>运行配方状态机阶段（F-3.1.10）。唯一 Owner 是 H01（B-26）。</summary>
public enum RunPhase
{
    /// <summary>未选择 Job。</summary>
    Idle,

    /// <summary>已应用 Job 队列，可开始运行。</summary>
    Ready,

    /// <summary>扫描中。</summary>
    Running,

    /// <summary>扫描中止（暂停；期间允许调用 H04 调参）。</summary>
    Paused,

    /// <summary>结批：不再开始后续 Recipe，扫完当前批次后结束（BR-06）。</summary>
    BatchEnding,

    /// <summary>结束（含停止中断与正常完成）。</summary>
    Finished
}
