using System.Windows.Controls;

namespace WaferInspectHMI.Views;

/// <summary>
/// 「缺陷分析」页承载 H04 算法参数调试（PRD 5.2 / 5.6 清单第 6 项）。
/// 视图不含业务规则：所有状态与命令来自 <c>H04InspectionViewModel</c>，
/// Map 圈选复用共享 <see cref="MapRegionCanvas"/>，与向导步骤 5/6 读同一份 H04 数据源。
/// </summary>
public partial class DefectAnalysisView : UserControl
{
    public DefectAnalysisView() => InitializeComponent();
}
