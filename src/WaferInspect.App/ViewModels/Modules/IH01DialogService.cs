using System.Collections.Generic;
using WaferInspect.Core.Run;

namespace WaferInspectHMI.ViewModels.Modules;

/// <summary>
/// H01 的对话框交互边界。ViewModel 只声明「需要用户确认什么」，
/// 具体 WPF 窗口由 App 层实现，便于状态机在不启动 UI 的情况下被测试。
/// </summary>
public interface IH01DialogService
{
    /// <summary>选择 Job（F-3.1.5-06）：多选，返回 null 表示取消。</summary>
    IReadOnlyList<JobSelection> SelectJobs(IReadOnlyList<JobSelection> available, IReadOnlyList<JobSelection> selected);

    /// <summary>EFEM 设置弹窗（F-3.1.2-13/17~25/27）；返回 null 表示取消。</summary>
    EfemSettingsResult EditEfemSettings(EfemSettingsResult current);

    /// <summary>确认晶圆信息（F-3.1.10-11）：可自定义 LotId / WaferId；返回 null 表示取消。</summary>
    WaferInfoResult ConfirmWafer(string lotId, string waferId);

    /// <summary>查看 Job 对应配方全流程（F-3.1.5-08）。</summary>
    void ShowJobInfo(JobSelection job);

    /// <summary>确认类提示（返回是否继续）。</summary>
    bool Confirm(string title, string message);

    /// <summary>信息类提示（含设备动作搁置说明）。</summary>
    void ShowMessage(string title, string message);
}
