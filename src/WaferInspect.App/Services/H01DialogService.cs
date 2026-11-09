using System;
using System.Collections.Generic;
using System.Windows;
using WaferInspect.Core.Run;
using WaferInspectHMI.ViewModels.Modules;

namespace WaferInspectHMI.Services;

/// <summary>
/// H01 对话框服务的 WPF 实现：只负责开窗与返回用户选择，不持有业务状态。
/// 所有窗口以主窗口为 Owner 居中显示，保证与主界面同一视觉语境。
/// </summary>
public sealed class H01DialogService : IH01DialogService
{
    public IReadOnlyList<JobSelection> SelectJobs(
        IReadOnlyList<JobSelection> available, IReadOnlyList<JobSelection> selected)
    {
        var dialog = new Views.SelectJobDialog(available, selected) { Owner = Owner };
        return dialog.ShowDialog() == true ? dialog.SelectedJobs : null;
    }

    public EfemSettingsResult EditEfemSettings(EfemSettingsResult current)
    {
        var dialog = new Views.EfemSettingsDialog(current) { Owner = Owner };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    public WaferInfoResult ConfirmWafer(string lotId, string waferId)
    {
        var dialog = new Views.WaferConfirmDialog(lotId, waferId) { Owner = Owner };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    public void ShowJobInfo(JobSelection job)
    {
        var dialog = new Views.JobInfoDialog(job) { Owner = Owner };
        dialog.ShowDialog();
    }

    public bool Confirm(string title, string message)
        => MessageBox.Show(Owner, message, title, MessageBoxButton.OKCancel, MessageBoxImage.Question)
           == MessageBoxResult.OK;

    public void ShowMessage(string title, string message)
        => MessageBox.Show(Owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    private static Window Owner => Application.Current?.MainWindow;
}
