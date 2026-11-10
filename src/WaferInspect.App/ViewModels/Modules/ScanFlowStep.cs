using CommunityToolkit.Mvvm.ComponentModel;

namespace WaferInspectHMI.ViewModels.Modules;

/// <summary>
/// 12 步 Map 创建流程的单步模型（B-24 / F-3.7.3-25）：由 H05 编排，每步能力调用对应 Owner。
/// 纯本地步骤（创建方式 / 选图谱 / Die 笔刷 / Ink·ReferenceDie / Sinf 内部导出）可交付；
/// 依赖 H03 对位 / H06 扫描执行的步骤诚实标记 <see cref="FlowStepStatus.Deferred"/>，不伪造设备成功（E-08 / E-10）。
/// </summary>
public sealed partial class ScanFlowStep : ObservableObject
{
    public ScanFlowStep(int stepNo, string title, string owner, bool deliverable,
                        string dependencyNote = "", string exportNote = "")
    {
        StepNo = stepNo;
        Title = title;
        Owner = owner;
        Deliverable = deliverable;
        DependencyNote = dependencyNote;
        ExportNote = exportNote;
    }

    /// <summary>步骤序号（1~12，对应 F-3.7.3-25 顺序）。</summary>
    public int StepNo { get; }

    /// <summary>步骤标题。</summary>
    public string Title { get; }

    /// <summary>能力归属 Owner：H03（对位）/ H05（Map 数据）/ H06（扫描执行）/ H08（导出动作）。</summary>
    public string Owner { get; }

    /// <summary>是否本增量可真实交付（纯本地）；否则依赖 H03/H06 需诚实搁置。</summary>
    public bool Deliverable { get; }

    /// <summary>未交付时的依赖说明（T-029 / T-035 / T-037 等）。</summary>
    public string DependencyNote { get; }

    /// <summary>可交付但需诚实标注的出口说明（如 Sinf 内部导出 vs 工业字节流）。</summary>
    public string ExportNote { get; }

    /// <summary>运行状态：待执行 / 已完成 / 规划完成（本地）/ 搁置（可通知 UI 刷新徽标）。</summary>
    [ObservableProperty]
    private FlowStepStatus _status = FlowStepStatus.Pending;
}

/// <summary>12 步流程单步运行状态（B-24）。</summary>
public enum FlowStepStatus
{
    /// <summary>尚未执行。</summary>
    Pending,

    /// <summary>本增量已真实执行（纯本地步骤）。</summary>
    Done,

    /// <summary>
    /// 本地规划已完成，但设备执行仍搁置。用于 H03 参考图像（B-09）等
    /// 其规划点位可由本地确定性逻辑真实给出、而真实对位/采集（B-10/T-029/T-035）尚未接入的步骤，
    /// 以诚实区分「规划可交付」与「设备执行待接入」（E-08 / E-10）。
    /// </summary>
    Planned,

    /// <summary>依赖 H03/H06 未接入，诚实搁置（不伪造设备能力）。</summary>
    Deferred
}
