using System.Windows.Controls;
using System.Windows.Input;
using WaferInspectHMI.ViewModels.Modules;

namespace WaferInspectHMI.Views;

/// <summary>
/// H08 数据分析主页面（PRD 2.2.9 / M09 · C-02）。
/// 复判全景 Map 复用 H05 的 <see cref="MapGridView"/>（B-06），本视图只做布局与绑定，
/// 不复制任何 Map 绘制 / 坐标 / 着色规则（关闭 AC-H05-04）。
/// 按 Die 查看的方向键导航（R-H08-05 / BR-52）在此拦截：焦点在输入控件时不抢占方向键。
/// </summary>
public partial class AnalysisView : UserControl
{
    public AnalysisView() => InitializeComponent();

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.OriginalSource is TextBox or ComboBox) return;   // 输入控件内方向键留给光标移动
        if (DataContext is not H08AnalysisViewModel vm) return;

        string direction = e.Key switch
        {
            Key.Left => "Left",
            Key.Right => "Right",
            Key.Up => "Up",
            Key.Down => "Down",
            _ => null
        };
        if (direction != null)
        {
            vm.MoveSelectionCommand.Execute(direction);
            e.Handled = true;
        }
    }
}
