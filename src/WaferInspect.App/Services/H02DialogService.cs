using System;
using System.Windows;
using WaferInspectHMI.ViewModels.Modules;
using WaferInspectHMI.Views;

namespace WaferInspectHMI.Services;

/// <summary>H02 对话框服务的 WPF 实现：只负责开窗与返回值，不承载业务规则。</summary>
public sealed class H02DialogService : IH02DialogService
{
    private readonly Func<string, SubRecipeWizardDialog> _wizardFactory;

    /// <summary>向导窗口由 DI 工厂创建（依赖仓库与图谱目录），服务只负责打开。</summary>
    public H02DialogService(Func<string, SubRecipeWizardDialog> wizardFactory)
        => _wizardFactory = wizardFactory;

    public string PickMapFile(string currentPath)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择 Map 文件（F-3.2.1-17）",
            Filter = "Map 文件 (*.csv;*.txt;*.xml;*.sinf)|*.csv;*.txt;*.xml;*.sinf|所有文件 (*.*)|*.*",
            CheckFileExists = true
        };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : string.Empty;
    }

    public string PickImageFile(string currentPath)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择参考图像（F-3.2.2 参考图像步骤；图像采集属 H06，设备连接搁置 T-029）",
            Filter = "图像文件 (*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff)|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff|所有文件 (*.*)|*.*",
            CheckFileExists = true
        };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : string.Empty;
    }

    public void ShowSubRecipeWizard(string parentJobId)
    {
        var dialog = _wizardFactory(parentJobId);
        dialog.Owner = Owner;
        dialog.ShowDialog();
    }

    public string PromptJobName(string title, string initialName)
    {
        var dialog = new RenameJobDialog(title, initialName) { Owner = Owner };
        return dialog.ShowDialog() == true ? dialog.JobName : string.Empty;
    }

    public bool Confirm(string title, string message)
        => MessageBox.Show(Owner, message, title, MessageBoxButton.OKCancel, MessageBoxImage.Question)
           == MessageBoxResult.OK;

    public void ShowMessage(string title, string message)
        => MessageBox.Show(Owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    private static Window Owner => Application.Current?.MainWindow;
}
