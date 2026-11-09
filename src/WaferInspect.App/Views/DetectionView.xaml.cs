using System.Windows.Controls;
using System.Windows.Input;
using WaferInspectHMI.ViewModels.Modules;

namespace WaferInspectHMI.Views;

/// <summary>
/// 检测任务主视图（PRD 4.1 / UI-3.1.1~3.1.10）。9 栏同屏常驻，
/// 视图区双击切换图谱 / 图片视窗（F-3.1.4-03），其余交互全部走 ViewModel 命令。
/// </summary>
public partial class DetectionView : UserControl
{
    public DetectionView() => InitializeComponent();

    private void ViewArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && DataContext is H01RunViewModel vm && vm.ToggleViewModeCommand.CanExecute(null))
            vm.ToggleViewModeCommand.Execute(null);
    }
}
