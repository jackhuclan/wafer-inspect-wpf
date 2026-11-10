using CommunityToolkit.Mvvm.ComponentModel;
using WaferInspect.Core.Maps;
using WaferInspect.Core.Recipes;
using WaferInspectHMI.Models;

namespace WaferInspectHMI.ViewModels.Modules;

/// <summary>子配方列表行（向导页顶栏）：只读投影，写操作经 ISubRecipeRepository。</summary>
public sealed class SubRecipeRow
{
    public SubRecipeRow(SubRecipe source) => Source = source;

    public SubRecipe Source { get; }

    public string Id => Source.Id;

    public string Name => Source.Name;

    /// <summary>进度摘要：已完成步骤数 / 7（T-042 顺序）。</summary>
    public string ProgressText
    {
        get
        {
            int done = Source.Steps.Count(s => s.Status == WizardStepStatus.Completed);
            return $"{done}/7 步";
        }
    }

    /// <summary>下一步提示；全部完成时为「已完成」。</summary>
    public string NextText
    {
        get
        {
            var wizard = new SubRecipeWizard(Source);
            return wizard.IsFinished
                ? "已完成"
                : $"下一步：{wizard.FirstIncomplete.Value.Text()}";
        }
    }
}

/// <summary>向导步骤行（左侧步骤列表）：状态由 SubRecipeWizard 判定，行只做呈现。</summary>
public sealed partial class WizardStepRow : ObservableBase
{
    public WizardStepRow(int index, WizardStepKey key)
    {
        Index = index;
        Key = key;
        _title = $"{index + 1}. {key.Text()}";
    }

    public int Index { get; }

    public WizardStepKey Key { get; }

    [ObservableProperty] private string _title;

    [ObservableProperty] private string _statusText = "未开始";

    [ObservableProperty] private bool _isCurrent;

    /// <summary>是否允许点击进入（BR-19/B-25 统一判定结果）。</summary>
    [ObservableProperty] private bool _isEnabled;

    /// <summary>失败原因（B-25：失败必须显式可见；成功后为空）。</summary>
    [ObservableProperty] private string _failureNote = string.Empty;
}

/// <summary>Mark 点编辑行（步骤 3）：三点对位数据，图形化框选属 H03 增量。</summary>
public sealed partial class MarkRow : ObservableBase
{
    public MarkRow(int index) => Index = index;

    public int Index { get; }

    [ObservableProperty] private string _row = string.Empty;

    [ObservableProperty] private string _col = string.Empty;

    [ObservableProperty] private string _platformX = string.Empty;

    [ObservableProperty] private string _platformY = string.Empty;

    public string Title => $"Mark {Index + 1}";
}

/// <summary>ROI 区域编辑行（步骤 5）：H04 单一数据源（AC-P2-03）。</summary>
public sealed partial class RoiZoneRow : ObservableBase
{
    public RoiZoneRow(int index) => Index = index;

    public int Index { get; }

    [ObservableProperty] private string _name = string.Empty;

    [ObservableProperty] private string _x = string.Empty;

    [ObservableProperty] private string _y = string.Empty;

    [ObservableProperty] private string _width = string.Empty;

    [ObservableProperty] private string _height = string.Empty;
}

/// <summary>
/// Map 定位网格单元格（步骤 3，F-3.2.2-45）：双击填入当前 Mark 的行列（F-3.2.2-59）。
/// 拖动网格由向导窗口 code-behind 调用 ViewModel.MoveGrid 实现。
/// </summary>
public sealed class MarkCellRow
{
    public MarkCellRow(int row, int col)
    {
        Row = row;
        Col = col;
    }

    public int Row { get; }

    public int Col { get; }

    /// <summary>Canvas 左偏移（像素，含网格整体偏移）。</summary>
    public double X { get; set; }

    /// <summary>Canvas 上偏移（像素，含网格整体偏移）。</summary>
    public double Y { get; set; }

    /// <summary>单元格填充色（F-3.2.2-60 分类口径，默认黄 = 待检测）。</summary>
    public string Fill { get; set; } = "#F5C518";

    // B-06 / BR-51：[列,行] 显示坐标统一由 H05 MapGridModel 换算，不在向导里另写一套 -1 规则。
    public string Tip => $"{MapGridModel.FormatDieCoordinate(Col, Row)} · 行 {Row} / 列 {Col}（双击填入当前 Mark，F-3.2.2-59）";
}
