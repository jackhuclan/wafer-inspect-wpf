using System.Windows;
using WaferInspectHMI.ViewModels.Modules;

namespace WaferInspectHMI.Views;

/// <summary>确认晶圆信息弹窗（F-3.1.10-11 / BR-14）。</summary>
public partial class WaferConfirmDialog : Window
{
    public WaferConfirmDialog(string lotId, string waferId)
    {
        InitializeComponent();
        LotBox.Text = lotId == "—" ? string.Empty : lotId;
        WaferBox.Text = waferId == "—" ? string.Empty : waferId;
    }

    public WaferInfoResult Result { get; private set; }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Result = new WaferInfoResult(LotBox.Text.Trim(), WaferBox.Text.Trim());
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
