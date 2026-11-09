using System;
using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using WaferInspect.Core.Run;
using WaferInspectHMI.Models;

namespace WaferInspectHMI.ViewModels.Modules;

/// <summary>料盒槽位行（F-3.1.2-03/04/05）：WaferId 可编辑、可勾选、带运行状态文本。</summary>
public sealed partial class SlotRow : ObservableBase
{
    public SlotRow(int index) => Index = index;

    public int Index { get; }

    public string IndexText => Index.ToString("00");

    [ObservableProperty] private string _waferId = string.Empty;
    [ObservableProperty] private bool _hasWafer;
    [ObservableProperty] private bool _isChecked;
    [ObservableProperty] private string _stateText = "待机";

    /// <summary>WaferId 由 OCR 自动填充（OCR 设备接入搁置 T-029，本阶段为可编辑值）。</summary>
    public bool CanEditWaferId => HasWafer;

    partial void OnHasWaferChanged(bool value) => OnPropertyChanged(nameof(CanEditWaferId));
}

/// <summary>
/// LoadPort 行（F-3.1.2-01/02/20/21/23/24）：料盒 ID 固定 Port1/Port2（BR-04），
/// 上料盒/下料盒互斥切换并控制仓门（BR-05 相关门禁由 EFEM 执行，此处只维护状态）。
/// </summary>
public sealed partial class PortRow : ObservableBase
{
    public PortRow(string portId, int slotCount)
    {
        if (portId is not ("Port1" or "Port2"))
            throw new ArgumentException("料盒 ID 固定为 Port1 / Port2（BR-04）。", nameof(portId));
        PortId = portId;
        for (int i = 1; i <= slotCount; i++)
            Slots.Add(new SlotRow(i));
    }

    public string PortId { get; }

    public ObservableCollection<SlotRow> Slots { get; } = [];

    /// <summary>料盒感知结果（F-3.1.2-01；真实感知由 EFEM 提供，T-029 搁置）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CassetteText))]
    private bool _cassettePresent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LockButtonText))]
    [NotifyPropertyChangedFor(nameof(DoorText))]
    private bool _isLocked;

    /// <summary>蓝色高亮表示当前选中 LoadPort（F-3.1.2-02）。</summary>
    [ObservableProperty] private bool _isCurrent;

    /// <summary>条码设置仅正面/反面（F-3.1.2-23，BR-03）。</summary>
    [ObservableProperty] private BarcodeSide _barcodeSide = BarcodeSide.Front;

    /// <summary>条码角度仅 0/90/180/270°（F-3.1.2-24，BR-02）。</summary>
    [ObservableProperty] private int _barcodeAngle;

    public string CassetteText => CassettePresent ? "料盒就位" : "无料盒";

    /// <summary>F-3.1.2-20/21：下料盒 → 解除锁定并切换为「上料盒」；上料盒 → 锁定并切换为「下料盒」。</summary>
    public string LockButtonText => IsLocked ? "下料盒" : "上料盒";

    /// <summary>料盒锁定时 EFEM 料盒仓门禁止打开（BR 级互斥规则）。</summary>
    public string DoorText => IsLocked ? "仓门禁用" : "仓门可开";

    public string BarcodeSideText => BarcodeSide == BarcodeSide.Front ? "正面" : "反面";

    partial void OnBarcodeSideChanged(BarcodeSide value) => OnPropertyChanged(nameof(BarcodeSideText));

    /// <summary>可选的条码角度（BR-02）。</summary>
    public static IReadOnlyList<int> AllowedAngles { get; } = [0, 90, 180, 270];

    public int CheckedCount
    {
        get
        {
            int n = 0;
            foreach (var slot in Slots) if (slot.IsChecked && slot.HasWafer) n++;
            return n;
        }
    }
}

/// <summary>日志行（F-3.1.9-01/02，BR-18）：错误级别红色标出。</summary>
public sealed record LogRow(string Time, string Level, string Message, Brush LevelBrush)
{
    public bool IsError => Level is "Error" or "Fatal" or "高等级报警";

    public string LevelText => Level switch
    {
        "Error" or "Fatal" => "错误",
        "Warn" => "警告",
        "高等级报警" => "错误",
        "低等级报警" => "提示",
        _ => "信息"
    };
}

/// <summary>实时缺陷行（F-3.1.8-01~07）；与 H08 共用 <see cref="DefectRecord"/> 模型（B-23）。</summary>
public sealed record DefectRow(
    string Zone, string Category, int Row, int Column, string Length, string Width, string Area)
{
    public static DefectRow From(DefectRecord record) => new(
        record.Zone,
        record.Category,
        record.Row,
        record.Column,
        // 长度/宽度/面积单位按 μm 与 μm² 原样（BR-12；与附录 B 缺省 mm 冲突，T-015 登记）。
        $"{record.Length:F1}",
        $"{record.Width:F1}",
        $"{record.Area:F1}");
}

/// <summary>EFEM 设置弹窗结果（F-3.1.2-17~25/27）。</summary>
public sealed record EfemSettingsResult(
    string PortId,
    bool IsLocked,
    BarcodeSide BarcodeSide,
    int BarcodeAngle,
    bool FlipRequested,
    bool ScanRequested = false);

/// <summary>确认晶圆信息结果（F-3.1.10-11）；取消时对话框返回 null。</summary>
public sealed record WaferInfoResult(string LotId, string WaferId);
