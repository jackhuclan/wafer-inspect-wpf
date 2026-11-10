using System.Windows.Controls;

namespace WaferInspectHMI.Views;

/// <summary>
/// H08 数据分析主页面（PRD 2.2.9 / M09 · C-02）。
/// 复判全景 Map 复用 H05 的 <see cref="MapGridView"/>（B-06），本视图只做布局与绑定，
/// 不复制任何 Map 绘制 / 坐标 / 着色规则（关闭 AC-H05-04）。
/// </summary>
public partial class AnalysisView : UserControl
{
    public AnalysisView() => InitializeComponent();
}
