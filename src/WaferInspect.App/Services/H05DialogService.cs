using System.Windows;
using WaferInspectHMI.ViewModels.Modules;
using WaferInspectHMI.Views;

namespace WaferInspectHMI.Services;

/// <summary>H05 对话框服务的 WPF 实现：只负责开窗 / 选文件 / 提示，不承载 MapType 业务规则。</summary>
public sealed class H05DialogService : IH05DialogService
{
    public string PickMapFile(string currentPath)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "导入图谱文件（BR-45 / F-3.7.2-11）",
            Filter = "Map 文件 (*.csv;*.txt;*.xml;*.sinf)|*.csv;*.txt;*.xml;*.sinf|所有文件 (*.*)|*.*",
            CheckFileExists = true
        };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : string.Empty;
    }

    public string PromptTypeName(string title, string initialName)
    {
        var dialog = new RenameJobDialog(title, initialName) { Owner = Owner };
        return dialog.ShowDialog() == true ? dialog.JobName : string.Empty;
    }

    public bool Confirm(string title, string message)
        => MessageBox.Show(Owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
           == MessageBoxResult.Yes;

    public void ShowMessage(string title, string message)
        => MessageBox.Show(Owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    private static Window Owner => Application.Current?.MainWindow;
}
