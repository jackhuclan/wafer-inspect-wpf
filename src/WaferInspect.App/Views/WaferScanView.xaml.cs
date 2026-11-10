using WaferInspectHMI.ViewModels.Modules;

namespace WaferInspectHMI.Views;

/// <summary>
/// 「Wafer 扫描」页（PRD 5.2 的 <c>waferscan</c> 落点，H05，P4-A 增量 1.5.0）：
/// 视图不含业务规则，状态与命令来自 <c>WaferScanViewModel</c>；
/// Map 绘制复用唯一 <see cref="MapGridView"/>（B-06 / AC-H05-04）。
/// </summary>
public partial class WaferScanView
{
    public WaferScanView()
    {
        InitializeComponent();
        MapControl.RectSelected += MapControl_RectSelected;
    }

    // 矩形批量框选完成（F-3.7.3-22）→ 转交 ViewModel 应用笔刷 Bin。
    private void MapControl_RectSelected(object sender, RectSelectedEventArgs e)
    {
        if (DataContext is WaferScanViewModel vm)
            vm.OnRectSelected(e.C0, e.R0, e.C1, e.R1);
    }
}
