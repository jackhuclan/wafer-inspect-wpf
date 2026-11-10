using System.Windows.Controls;

namespace WaferInspectHMI.Views;

/// <summary>
/// 「图谱管理」页（PRD 5.2 的 <c>maps</c> 落点，替换 P0 骨架）：
/// 视图不含业务规则，状态与命令来自 <c>H05MapViewModel</c>，
/// Map 绘制复用唯一 <see cref="MapGridView"/>（B-06 / AC-H05-04）。
/// </summary>
public partial class MapManagementView : UserControl
{
    public MapManagementView() => InitializeComponent();
}
