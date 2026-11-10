using System.Windows;
using System.Windows.Controls;
using WaferInspect.Core.Run;
using WaferInspectHMI.ViewModels.Modules;

namespace WaferInspectHMI.Views;

/// <summary>
/// EFEM 设置弹窗（F-3.1.2-13 入口；内容对应 F-3.1.2-17~25/27）。
/// 只编辑「运行时单次动作与临时设置」，不写入全局设备配置；设备动作搁置（T-029）。
/// </summary>
public partial class EfemSettingsDialog : Window
{
    private bool _locked;
    private bool _flip;
    private bool _scan;

    public EfemSettingsDialog(EfemSettingsResult current)
    {
        InitializeComponent();

        PortCombo.ItemsSource = new[] { "Port1", "Port2" };
        PortCombo.SelectedItem = current.PortId;

        _locked = current.IsLocked;
        FrontRadio.IsChecked = current.BarcodeSide == BarcodeSide.Front;
        BackRadio.IsChecked = current.BarcodeSide == BarcodeSide.Back;
        Angle0.IsChecked = current.BarcodeAngle == 0;
        Angle90.IsChecked = current.BarcodeAngle == 90;
        Angle180.IsChecked = current.BarcodeAngle == 180;
        Angle270.IsChecked = current.BarcodeAngle == 270;

        UpdateLockButton();
    }

    /// <summary>确定后的结果；取消时为 null。</summary>
    public EfemSettingsResult Result { get; private set; }

    private string SelectedPort => PortCombo.SelectedItem as string ?? "Port1";

    private void UpdateLockButton() => LockButton.Content = _locked ? "下料盒" : "上料盒";

    private void Lock_Click(object sender, RoutedEventArgs e)
    {
        _locked = !_locked;
        UpdateLockButton();
        LockHint.Text = _locked
            ? "下料盒 → 上料盒：锁定料盒，EFEM 料盒仓门禁止打开（F-3.1.2-21）"
            : "上料盒 → 下料盒：解除料盒锁定，切换为上料盒按钮（F-3.1.2-20）";
    }

    private void Unlock_Click(object sender, RoutedEventArgs e)
        => MessageBox.Show(this,
            "Unlock：解锁 EFEM 料盒仓门，不改变料盒状态（F-3.1.2-22）。\n设备动作未下发（T-029）。",
            "Unlock", MessageBoxButton.OK, MessageBoxImage.Information);

    private void Scan_Click(object sender, RoutedEventArgs e)
    {
        _scan = true;
        MessageBox.Show(this,
            "已记录「扫描料盒」请求，确定后按模拟数据源更新槽位状态（真实扫描搁置 T-029）。",
            "扫描料盒", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Flip_Click(object sender, RoutedEventArgs e)
    {
        _flip = true;
        MessageBox.Show(this,
            "已记录「翻面」请求（F-3.1.2-27，T-002 已确认：正/反面互换后重新扫描该面）。\n物理翻面动作未下发（设备连接搁置 T-029）。",
            "翻面", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ClearError_Click(object sender, RoutedEventArgs e)
        => MessageBox.Show(this,
            "EFEM 错误清除归 H09 报警与故障处理（B-18）。本弹窗只保留入口，请在 H09 执行清除。",
            "EFEM 错误清除", MessageBoxButton.OK, MessageBoxImage.Information);

    private void PortCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 首次加载时控件尚未构建完成，忽略。
        if (LockButton is null) return;
        UpdateLockButton();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        int angle = Angle90.IsChecked == true ? 90 : Angle180.IsChecked == true ? 180 : Angle270.IsChecked == true ? 270 : 0;
        Result = new EfemSettingsResult(
            SelectedPort,
            _locked,
            BackRadio.IsChecked == true ? BarcodeSide.Back : BarcodeSide.Front,
            angle,
            _flip,
            _scan);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
