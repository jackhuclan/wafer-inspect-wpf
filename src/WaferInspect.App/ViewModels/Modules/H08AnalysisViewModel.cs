using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WaferInspect.Core.Analysis;
using WaferInspect.Core.Maps;
using WaferInspect.Core.Run;

namespace WaferInspectHMI.ViewModels.Modules;

/// <summary>
/// H08「数据分析与人工复判」（PRD 2.2.9 / M09 · C-02）：
/// Wafer 数据检索（3.9.2）、复判全景 Map（复用 H05 的 <see cref="MapGridView"/> 与 <see cref="MapGridModel"/>，关闭 AC-H05-04）、
/// 复判前 / 后 / AI 良率（F-3.9.2-07/08/09）、人工 / 批量 / AI 复判（B-16，AI 标 [搁置] T-035）、
/// 缺陷列表多维筛选（B-23，消费 H04 唯一 <see cref="DefectRecord"/> 模型）。
/// 所有 Bin / 颜色 / 快捷键一律来自 <see cref="IMapTypeCatalog"/>（B-11），不各页面自建。
/// </summary>
public sealed partial class H08AnalysisViewModel : ModuleViewModelBase
{
    private readonly IMapTypeCatalog _catalog;
    private readonly IWaferResultStore _store;
    private readonly IRecheckService _recheck;
    private readonly IResultExportService _export;
    private readonly ISinfGenerator _sinf;
    private readonly MapGridModel _map = new();
    private string _selectedWaferId = string.Empty;

    public H08AnalysisViewModel(
        IMapTypeCatalog catalog,
        IWaferResultStore store,
        IRecheckService recheck,
        IResultExportService export,
        ISinfGenerator sinf)
        : base("H08", "数据分析与人工复判", "P3（增量 6：主页面 + 复判全景 Map + 复判执行；增量 7：结果输出 R-H08-06；增量 8：按 Die 查看 R-H08-05 + GoldenDie/回溯图/复判图 + Sinf 生成 B-12 + AI 重新推理 R-H08-07）")
    {
        _catalog = catalog;
        _store = store;
        _recheck = recheck;
        _export = export;
        _sinf = sinf;

        _catalog.Changed += (_, _) => RefreshAvailableBins();
        _store.Changed += (_, _) => RefreshWafers();
        _recheck.Changed += (_, _) => OnRecheckChanged();
        _map.Changed += (_, _) => RefreshMapText();

        foreach (var t in _export.Targets)
            ExportTargets.Add(new ExportTargetItem(t, _export));
        SelectedExportTarget = ExportTargets.FirstOrDefault();

        RefreshWafers();
    }

    /* ---------------- 列表与筛选（F-3.9.2-01~21） ---------------- */

    public ObservableCollection<WaferRow> Wafers { get; } = new();
    public ObservableCollection<DefectResultRow> Defects { get; } = new();
    public ObservableCollection<H08BinOption> AvailableBins { get; } = new();
    public ObservableCollection<string> ReviewStateOptions { get; } = new() { "全部", "未复判", "已复判" };

    // 结果输出（B-13 / B-14 / R-H08-06）
    public ObservableCollection<ExportTargetItem> ExportTargets { get; } = new();
    [ObservableProperty] private ExportTargetItem _selectedExportTarget;
    [ObservableProperty] private string _exportMessage = "尚未导出";
    [ObservableProperty] private bool _isExporting;

    // Sinf 生成（B-12 / T-037）：H05 唯一生成、H08 发起；格式未确认，诚实占位。
    [ObservableProperty] private string _sinfGenMessage = "Sinf 生成：格式规范待确认（T-037）";

    // AI 重新推理（F-3.9.2-16 / R-H08-07 / BR-66）：模型未部署，明确不可用，不静默成功。
    [ObservableProperty] private string _aiReinferMessage = "AI 重新推理：模型未部署（T-035），不可用";
    [ObservableProperty] private bool _isReinfering;

    /* ---------------- 按 Die 查看（R-H08-05） ---------------- */

    public ObservableCollection<DefectResultRow> SelectedDieDefects { get; } = new();
    [ObservableProperty] private bool _hasSelectedDie;
    [ObservableProperty] private string _dieCoordinateText = "[—,—]";
    [ObservableProperty] private string _dieDetailText = "未选中 Die（双击复判全景 Map 任意网格，或方向键导航）";
    [ObservableProperty] private string _selectedDieColor = "#E8EDF5";
    [ObservableProperty] private Brush _selectedDieBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0xED, 0xF5));

    // GoldenDie / 回溯图 / 复判图（F-3.9.3-20 / F-3.9.3-23，诚实搁置）
    [ObservableProperty] private string _goldenDieText = "[搁置] GoldenDie 由 H03 参考图像生成（B-09），本增量未生成，只读展示待 H03 落地";
    [ObservableProperty] private string _backtrackImageText = "[BR-73] 未选中 Die，无回溯图";
    [ObservableProperty] private string _recheckImageText = "[BR-73] 未选中 Die，无复判图";

    [ObservableProperty] private WaferRow _selectedWafer;
    [ObservableProperty] private H08BinOption _recheckBin;
    [ObservableProperty] private H08BinOption _sourceBin;
    [ObservableProperty] private H08BinOption _targetBin;
    [ObservableProperty] private string _lotFilter = string.Empty;
    [ObservableProperty] private string _waferFilter = string.Empty;
    [ObservableProperty] private string _toolFilter = string.Empty;
    [ObservableProperty] private string _categoryFilter = string.Empty;
    [ObservableProperty] private string _reviewStateFilter = "全部";
    [ObservableProperty] private string _shortcutKey = "G";
    [ObservableProperty] private string _jumpX = "3";
    [ObservableProperty] private string _jumpY = "7";

    [ObservableProperty] private string _mapStatusText = "尚未载入 Wafer 全景 Map";
    [ObservableProperty] private string _selectedDieText = "未选中 Die（双击全景 Map 任意网格）";
    [ObservableProperty] private string _revisionText = "刷新版本 v0";
    [ObservableProperty] private string _recheckMessage = "请选择左侧 Wafer 并双击全景 Map 选中 Die 后复判";
    [ObservableProperty] private string _detailText = "未选中 Die：GoldenDie（只读 B-09）/ 回溯图 / 复判图 占位，设备采集未接入（T-029）";
    [ObservableProperty] private string _yieldSummaryText = "复判前 — · 复判后 — · AI 复判后 —";

    /// <summary>明确登记已交付与未迁移项，禁止伪造入口（与 P3 状态文档一致）。</summary>
    public string DeferredNote =>
        "[已交付] 按 Die 查看（R-H08-05）：双击 / 方向键选中 Die → 详情 + 该 Die 缺陷联动 + BR-73 无图提示；" +
        "GoldenDie / 回溯图 / 复判图视图框架（F-3.9.3-20/23）已搭，图片路径 [搁置]（T-029）、GoldenDie 待 H03 生成（B-09）。" +
        "[诚实占位] Sinf 生成（B-12，T-037 格式未确认）与 AI 重新推理（R-H08-07 / BR-66，T-035 未部署）明确不可用，不伪造工业格式 / AI 结果。" +
        "结果输出（B-13/B-14、R-H08-06）已落地。所有结果为确定性合成（T-029 / T-035 未接入），已标注 [模拟结果]，不伪造设备成功。";

    /// <summary>H08 唯一复判全景 Map 模型（B-06），由共享 <c>MapGridView</c> 直接绑定。</summary>
    public MapGridModel Map => _map;

    /* ---------------- 选择联动 ---------------- */

    partial void OnSelectedWaferChanged(WaferRow value)
    {
        _selectedWaferId = value?.WaferId ?? string.Empty;
        RefreshAvailableBins();
        LoadSelectedMap();
        ApplyDefectFilter();
        RefreshYieldText();
        DetailText = "选中 Die 后可在下方「按 Die 查看」查看该 Die 缺陷与图像（设备采集 T-029 未接入）。";
        RefreshDieDetail();
    }

    partial void OnLotFilterChanged(string value) => RefreshWafers();
    partial void OnWaferFilterChanged(string value) => RefreshWafers();
    partial void OnToolFilterChanged(string value) => RefreshWafers();
    partial void OnCategoryFilterChanged(string value) => ApplyDefectFilter();
    partial void OnReviewStateFilterChanged(string value) => ApplyDefectFilter();

    private void RefreshWafers()
    {
        var list = _store.Query(LotFilter, WaferFilter, ToolFilter);
        Wafers.Clear();
        foreach (var w in list) Wafers.Add(new WaferRow(w));

        var keep = Wafers.FirstOrDefault(r => r.WaferId == _selectedWaferId) ?? Wafers.FirstOrDefault();
        SelectedWafer = keep;
    }

    private void RefreshAvailableBins()
    {
        AvailableBins.Clear();
        var w = SelectedWafer?.Source;
        if (w == null) return;
        var type = _catalog.Find(w.MapTypeId);
        if (type == null) return;
        foreach (var bin in type.Bins)
            AvailableBins.Add(new H08BinOption(bin.Kind, bin.Code, bin.Name, bin.Shortcut));
        RecheckBin = AvailableBins.FirstOrDefault();
        SourceBin = AvailableBins.FirstOrDefault(b => b.Kind == BinKind.DefectDie)
                    ?? AvailableBins.FirstOrDefault();
        TargetBin = AvailableBins.FirstOrDefault(b => b.Kind == BinKind.GoodDie)
                    ?? AvailableBins.FirstOrDefault();
    }

    private void LoadSelectedMap()
    {
        var w = SelectedWafer?.Source;
        if (w == null)
        {
            MapStatusText = "尚未选择 Wafer";
            return;
        }
        _map.LoadWafer(w.MapTypeId, _catalog, w.BinMatrix, w.Rows, w.Columns);
        RefreshMapText();
    }

    private void RefreshMapText()
    {
        MapStatusText = _map.StatusText;
        SelectedDieText = _map.Selected?.Tip ?? "未选中 Die（双击全景 Map 任意网格）";
        RevisionText = $"刷新版本 v{_map.RefreshVersion} · " +
                       (_map.NeedsRefresh ? "数据已变化，需点「刷新图谱」（BR-53）" : "颜色已同步");
        RefreshDieDetail();
    }

    /// <summary>
    /// 按 Die 查看（R-H08-05）：根据当前选中 Die 刷新详情、该 Die 缺陷联动与图像视口。
    /// 由 <see cref="MapGridModel.Changed"/>（双击 / 跳转 / 方向键导航）统一驱动。
    /// </summary>
    private void RefreshDieDetail()
    {
        var cell = _map.Selected;
        if (cell == null)
        {
            HasSelectedDie = false;
            DieCoordinateText = "[—,—]";
            DieDetailText = "未选中 Die（双击复判全景 Map 任意网格，或方向键导航）";
            SelectedDieColor = "#E8EDF5";
            SelectedDieBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0xED, 0xF5));
            SelectedDieDefects.Clear();
            BacktrackImageText = "[BR-73] 未选中 Die，无回溯图";
            RecheckImageText = "[BR-73] 未选中 Die，无复判图";
            return;
        }

        HasSelectedDie = true;
        DieCoordinateText = cell.Coordinate;
        DieDetailText = $"{cell.Coordinate} · {cell.Label}（{cell.Code}）· 等级 {cell.Level} · 颜色 {cell.ColorHex}";
        SelectedDieColor = cell.ColorHex;
        try { SelectedDieBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(cell.ColorHex)); }
        catch (FormatException) { SelectedDieBrush = new SolidColorBrush(Colors.LightGray); }

        // 该 Die 的缺陷（R-H08-05：缺陷列表联动）
        var w = SelectedWafer?.Source;
        SelectedDieDefects.Clear();
        if (w != null)
        {
            foreach (var d in w.Defects.Where(d => d.Row == cell.Row && d.Column == cell.Column))
                SelectedDieDefects.Add(DefectResultRow.From(d));
        }

        // 回溯图 / 复判图（F-3.9.3-23）：图片路径来自缺陷记录，当前为 [搁置] T-029
        var img = SelectedDieDefects.FirstOrDefault()?.SourceImagePath;
        if (string.IsNullOrWhiteSpace(img))
        {
            BacktrackImageText = "[BR-73] 该 Die 无回溯图（未保存）";
            RecheckImageText = "[BR-73] 该 Die 无复判图（未保存）";
        }
        else if (img.Contains("[搁置]"))
        {
            BacktrackImageText = $"[搁置] 回溯图未接入（T-029）：{img}";
            RecheckImageText = $"[搁置] 复判图未接入（T-029）：{img}";
        }
        else
        {
            BacktrackImageText = $"回溯图：{img}";
            RecheckImageText = $"复判图：{img}";
        }
    }

    private void RefreshYieldText()
    {
        var w = SelectedWafer?.Source;
        if (w == null) { YieldSummaryText = "复判前 — · 复判后 — · AI 复判后 —"; return; }
        YieldSummaryText = $"复判前 {w.PreReviewYield * 100:F2}% · 复判后 {w.PostReviewYield * 100:F2}% · " +
                           $"AI 复判后 {(w.AiReviewed ? $"{w.AiReviewYield * 100:F2}%" : "0.00%")}（复判数 {w.ReviewedCount}）";
    }

    private void OnRecheckChanged()
    {
        // 复判写回后刷新当前 Wafer 行、缺陷列表与良率。
        var updated = _store.Get(_selectedWaferId);
        if (updated != null) SelectedWafer = new WaferRow(updated);
        ApplyDefectFilter();
        RefreshYieldText();
    }

    private void ApplyDefectFilter()
    {
        Defects.Clear();
        var w = SelectedWafer?.Source;
        if (w == null) return;
        var q = w.Defects.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(CategoryFilter))
            q = q.Where(d => d.Category.Contains(CategoryFilter, System.StringComparison.OrdinalIgnoreCase));
        if (ReviewStateFilter is "未复判" or "已复判")
            q = q.Where(d => d.ReviewState == ReviewStateFilter);
        foreach (var d in q) Defects.Add(DefectResultRow.From(d));
    }

    /* ---------------- 命令 ---------------- */

    [RelayCommand]
    private void SelectWafer(WaferRow row)
    {
        if (row != null) SelectedWafer = row;
    }

    [RelayCommand]
    private void GenerateMap()
    {
        LoadSelectedMap();
        RecheckMessage = _map.StatusText;
    }

    [RelayCommand]
    private void RefreshMap()
    {
        var w = SelectedWafer?.Source;
        if (w == null) return;
        _map.RecolorWafer(w.MapTypeId, _catalog);   // BR-53 显式刷新：复判后颜色唯一更新入口
        RefreshMapText();
        RecheckMessage = _map.StatusText;
    }

    [RelayCommand]
    private void JumpToDie()
    {
        if (!int.TryParse(JumpX?.Trim(), out int x) || !int.TryParse(JumpY?.Trim(), out int y))
        {
            RecheckMessage = "X（列）/ Y（行）必须为整数（BR-52）";
            return;
        }
        _map.JumpTo(x, y);
        RefreshMapText();
        RecheckMessage = _map.StatusText;
    }

    [RelayCommand]
    private void RecheckSelected()
    {
        var w = SelectedWafer?.Source;
        if (w == null) { RecheckMessage = "请先选择 Wafer"; return; }
        if (_map.Selected == null) { RecheckMessage = "请先双击全景 Map 选中一颗 Die"; return; }
        if (RecheckBin == null) { RecheckMessage = "请先选择目标 Bin"; return; }

        var updated = _recheck.ManualRecheck(w.WaferId, _map.Selected.Column, _map.Selected.Row, RecheckBin.Kind);
        if (updated == null) { RecheckMessage = "复判写回失败：Wafer 不存在"; return; }
        // BR-53：复判后颜色不立即更新，逻辑 Kind 已更新，需点「刷新图谱」。
        _map.Reassign(_map.Selected.Column, _map.Selected.Row,
            _catalog.Find(w.MapTypeId)!.Bins.First(b => b.Kind == RecheckBin.Kind));
        RefreshDetail();
        RecheckMessage = $"人工复判 Die {_map.Selected.Coordinate} → {RecheckBin.Code}（{RecheckBin.Name}）；" +
                         "颜色待「刷新图谱」更新（BR-53）";
    }

    [RelayCommand]
    private void BatchRecheck()
    {
        var w = SelectedWafer?.Source;
        if (w == null) { RecheckMessage = "请先选择 Wafer"; return; }
        if (SourceBin == null || TargetBin == null) { RecheckMessage = "请先选择源 / 目标 Bin"; return; }
        if (SourceBin.Kind == TargetBin.Kind) { RecheckMessage = "源 Bin 与目标 Bin 不能相同"; return; }

        var updated = _recheck.BatchRecheck(w.WaferId, SourceBin.Kind, TargetBin.Kind);
        if (updated == null) { RecheckMessage = "批量复判失败：Wafer 不存在"; return; }
        _map.MarkDirty();
        RefreshMapText();
        RecheckMessage = $"批量复判：{SourceBin.Code} → {TargetBin.Code} 已写回；颜色待「刷新图谱」更新（BR-53）";
    }

    [RelayCommand]
    private void ApplyShortcut()
    {
        var w = SelectedWafer?.Source;
        if (w == null) { RecheckMessage = "请先选择 Wafer"; return; }
        if (_map.Selected == null) { RecheckMessage = "请先双击全景 Map 选中一颗 Die"; return; }
        var bin = _catalog.ResolveShortcut(w.MapTypeId, ShortcutKey);
        if (bin == null) { RecheckMessage = $"快捷键「{ShortcutKey}」未绑定任何 Bin（AC-H05-02）"; return; }

        var updated = _recheck.ManualRecheck(w.WaferId, _map.Selected.Column, _map.Selected.Row, bin.Kind);
        if (updated == null) { RecheckMessage = "快捷键复判写回失败"; return; }
        _map.Reassign(_map.Selected.Column, _map.Selected.Row, bin);
        RefreshDetail();
        RecheckMessage = $"快捷键「{ShortcutKey}」→ Bin {bin.Code}（{bin.Name}）复判 Die {_map.Selected.Coordinate}；" +
                         "颜色待「刷新图谱」更新（BR-53）";
    }

    [RelayCommand]
    private void AiRecheck()
    {
        var w = SelectedWafer?.Source;
        if (w == null) { RecheckMessage = "请先选择 Wafer"; return; }
        var (_, message, executed) = _recheck.AiRecheck(w.WaferId);
        // T-035：AI 模型未部署，不伪造结果；界面明确标注 [搁置]。
        RecheckMessage = executed ? "AI 复判已完成" : message;
    }

    [RelayCommand]
    private void AiReinfer()
    {
        var w = SelectedWafer?.Source;
        if (w == null) { AiReinferMessage = "请先选择 Wafer"; return; }
        IsReinfering = true;
        try
        {
            var (_, message, executed) = _recheck.ReInfer(w.WaferId);
            // R-H08-07 / BR-66：模型未部署，明确不可用，不静默成功、不伪造推理结果。
            AiReinferMessage = executed ? "AI 重新推理已完成" : message;
        }
        finally
        {
            IsReinfering = false;
        }
    }

    [RelayCommand]
    private void GenerateSinf()
    {
        var w = SelectedWafer?.Source;
        if (w == null) { SinfGenMessage = "请先选择 Wafer"; return; }
        // B-12 / T-037：格式规范未确认，仅诚实占位，不伪造工业格式。
        var r = _sinf.Generate(w);
        SinfGenMessage = r.Message;
    }

    /// <summary>
    /// 按 Die 查看方向键 / 箭头导航（R-H08-05；BR-52：X=列 Y=行）。
    /// 参数 direction ∈ {Left,Right,Up,Down}；越界时 <see cref="MapGridModel.JumpTo"/> 显式返回 false（不钳制）。
    /// </summary>
    [RelayCommand]
    private void MoveSelection(string direction)
    {
        if (_map.Selected == null) { RecheckMessage = "请先双击全景 Map 选中一颗 Die"; return; }
        int col = _map.Selected.Column, row = _map.Selected.Row;
        int dx = direction == "Left" ? -1 : direction == "Right" ? 1 : 0;
        int dy = direction == "Up" ? -1 : direction == "Down" ? 1 : 0;
        _map.JumpTo(col + dx, row + dy);
        RefreshMapText();
        RecheckMessage = _map.StatusText;
    }

    /* ---------------- 结果输出（B-13 / B-14 / R-H08-06） ---------------- */

    [RelayCommand]
    private void ExportResult()
    {
        var wafer = SelectedWafer?.Source;
        if (wafer == null) { ExportMessage = "请先选择 Wafer"; return; }
        if (SelectedExportTarget == null) { ExportMessage = "请先选择导出目标"; return; }

        var target = SelectedExportTarget.Target;
        IsExporting = true;
        try
        {
            var result = _export.Export(wafer, target);
            if (!result.IsFileTarget)
            {
                TryClipboard(result.Content ?? string.Empty);
                ExportMessage = $"{result.Message}：{result.Content}";
                return;
            }
            if (!result.Success)
            {
                ExportMessage = result.Message;   // T-037 占位：Sinf / Klarf 明确失败提示，不出文件
                return;
            }
            var dlg = new SaveFileDialog
            {
                FileName = result.SuggestedFileName,
                Filter = "CSV 文件 (*.csv)|*.csv|全部文件 (*.*)|*.*",
                Title = "导出结果文件"
            };
            if (dlg.ShowDialog() == true)
            {
                System.IO.File.WriteAllText(dlg.FileName, result.Content ?? string.Empty, System.Text.Encoding.UTF8);
                ExportMessage = $"已导出：{dlg.FileName}";
            }
            else
            {
                ExportMessage = "已取消导出";
            }
        }
        finally
        {
            IsExporting = false;
        }
    }

    [RelayCommand]
    private void CopyWaferId()
    {
        var wafer = SelectedWafer?.Source;
        if (wafer == null) { ExportMessage = "请先选择 Wafer"; return; }
        var r = _export.Export(wafer, ResultExportTarget.CopyWaferId);
        TryClipboard(r.Content ?? string.Empty);
        ExportMessage = $"{r.Message}：{r.Content}";
    }

    [RelayCommand]
    private void CopyRelativePath()
    {
        var wafer = SelectedWafer?.Source;
        if (wafer == null) { ExportMessage = "请先选择 Wafer"; return; }
        var r = _export.Export(wafer, ResultExportTarget.CopyRelativePath);
        TryClipboard(r.Content ?? string.Empty);
        ExportMessage = $"{r.Message}：{r.Content}";
    }

    private void TryClipboard(string content)
    {
        try { Clipboard.SetText(content); }
        catch { /* 剪贴板不可用时降级为仅提示，不抛异常 */ }
    }

    private void RefreshDetail()
    {
        // 复判写回后地图变化已通过 Changed 驱动 RefreshDieDetail；此处仅确保一次刷新。
        RefreshDieDetail();
    }
}
