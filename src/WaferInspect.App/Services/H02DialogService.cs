using System.Windows;
using WaferInspectHMI.ViewModels.Modules;
using WaferInspectHMI.Views;

namespace WaferInspectHMI.Services;

/// <summary>H02 对话框服务的 WPF 实现：只负责开窗与返回值，不承载业务规则。</summary>
public sealed class H02DialogService : IH02DialogService
{
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
