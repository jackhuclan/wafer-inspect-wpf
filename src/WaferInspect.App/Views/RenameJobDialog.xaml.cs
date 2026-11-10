using System.Windows;

namespace WaferInspectHMI.Views;

/// <summary>Job 重命名输入弹窗（F-3.2.1-03）。名称唯一性由仓库层校验，本窗口只负责取值。</summary>
public partial class RenameJobDialog : Window
{
    public RenameJobDialog(string title, string initialName)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        NameBox.Text = initialName;
        Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
    }

    public string JobName { get; private set; } = string.Empty;

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        JobName = NameBox.Text.Trim();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
