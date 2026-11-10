using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WaferInspect.Core.Inspection;

namespace WaferInspectHMI.ViewModels.Modules;

/// <summary>
/// 「算法参数调试 · 参数验证」页（F-3.8.1-01~12，R-H04-03/04/06）。
/// 与配方向导步骤 5/6 共用 <see cref="IRoiScanAreaStore"/> 同一单例（AC-P2-03 / AC-H04-01）；
/// 右上角 × 关闭时由窗口调用 <see cref="SaveOnClose"/> 自动保存（BR-40，AC-P2-05 / AC-H04-03）。
/// 真实相机采集、运动控制与 AI 推理按 T-029/T-035 搁置，界面显式标注且不伪造设备执行。
/// </summary>
public sealed partial class H04ParameterValidationViewModel : ObservableObject, IMapRegionCanvasHost
{
    private readonly string _recipeId;
    private readonly IRoiScanAreaStore _store;
    private readonly IH04DialogService _dialogs;
    private bool _loading;
    private bool _suppressRefresh;

    public H04ParameterValidationViewModel(string recipeId, IRoiScanAreaStore store, IH04DialogService dialogs)
    {
        _recipeId = recipeId;
        _store = store;
        _dialogs = dialogs;
        _store.Changed += (_, id) =>
        {
            if (!_suppressRefresh && string.Equals(id, _recipeId, StringComparison.Ordinal)) Refresh();
        };

        HeaderText = $"参数验证 · 子配方 {recipeId}（H04 唯一数据源）";
        Refresh();
    }

    public string RecipeId => _recipeId;

    /// <summary>本次调试最后一条验证结果；窗口关闭后交回缺陷分析页呈现。</summary>
    public VerificationOutcome LastOutcome { get; private set; }

    public MapRegionCanvasState Map { get; } = new();

    public ObservableCollection<ParameterRow> ParameterRows { get; } = new();
    public ObservableCollection<ImageRow> Images { get; } = new();
    public ObservableCollection<DefectResultRow> DefectResults { get; } = new();

    public IReadOnlyList<string> TargetKindOptions { get; } = new[] { RoiTargetKind.Zone.Text(), RoiTargetKind.Pad.Text() };

    [ObservableProperty] private string _headerText;
    [ObservableProperty] private string _statusText = "尚未执行测试";
    [ObservableProperty] private string _syncText = string.Empty;
    [ObservableProperty] private bool _scanFullWafer = true;
    [ObservableProperty] private bool _samplingEnabled;
    [ObservableProperty] private DefectResultRow _selectedDefect;
    [ObservableProperty] private string _folderText = string.Empty;
    [ObservableProperty] private string _regionX = "1";
    [ObservableProperty] private string _regionY = "1";
    [ObservableProperty] private string _regionW = "4";
    [ObservableProperty] private string _regionH = "4";
    [ObservableProperty] private string _newParameterKey = string.Empty;
    [ObservableProperty] private string _newParameterValue = string.Empty;
    [ObservableProperty] private string _newParameterGroup = AlgorithmParameterCatalog.GlobalGroup;
    [ObservableProperty] private string _newParameterUnit = string.Empty;
    [ObservableProperty] private string _loopStepText = "① 加扫描区域";

    public string OriginalImagePath => SelectedDefect?.SourceImagePath ?? string.Empty;

    public string ClosedLoopText =>
        "① 加扫描区域 → ② 预扫描 → ③ 查看缺陷列表 → ④ 选中记录看原图 → " +
        "⑤ 切到 ROI 参数设置调参 → ⑥ 回参数验证做单幅/全部测试 → ⑦ 重复 → ⑧ 关闭自动保存";

    public string DeviceNote =>
        "[搁置] 未下发（T-029）：预扫描运动/相机采集/光源控制未接入；AI 推理未部署（T-035）。" +
        "单幅/全部图片测试与预扫描均为确定性离线演算，结果不冒充设备返回。";

    partial void OnSelectedDefectChanged(DefectResultRow value) => OnPropertyChanged(nameof(OriginalImagePath));

    partial void OnScanFullWaferChanged(bool value)
    {
        if (_loading) return;
        _store.SetFullWafer(_recipeId, value);
    }

    partial void OnSamplingEnabledChanged(bool value)
    {
        if (_loading) return;
        _store.SetSamplingEnabled(_recipeId, value);
    }

    private void Refresh()
    {
        var snapshot = _store.Snapshot(_recipeId);
        _loading = true;
        Map.Rebuild(snapshot);
        ScanFullWafer = snapshot.FullWafer;
        SamplingEnabled = snapshot.SamplingEnabled;
        FolderText = snapshot.ImageFolderPath;

        ParameterRows.Clear();
        foreach (var parameter in snapshot.Parameters) ParameterRows.Add(new ParameterRow(parameter));

        string threshold = snapshot.Parameters.FirstOrDefault(p => p.Key == "Detection.Threshold")?.Value ?? "-";
        SyncText = $"唯一数据源 · 扫描区域 {snapshot.ScanRegions.Count} 个 / 抽检 Die {snapshot.SamplingDies.Count} 颗 / " +
                  $"全片={snapshot.FullWafer} · 自动保存版本 v{snapshot.SavedVersion}" +
                  $" · 参数项 {snapshot.Parameters.Count}（T-014 可扩展集合） · 检测阈值={threshold}";
        _loading = false;
    }

    /* ---------------- 共享画布回调（B-01/B-02） ---------------- */

    public void AddRegion(MapRect rect)
    {
        _store.AddScanRegion(_recipeId, rect, "参数验证 · Map 圈选");
        StatusText = $"已添加扫描区域（列 {rect.X:0} / 行 {rect.Y:0} / {rect.Width:0}×{rect.Height:0} Die）";
        LoopStepText = "② 预扫描";
    }

    public void ToggleDie(int row, int column)
    {
        _store.ToggleSamplingDie(_recipeId, row, column);
        StatusText = $"抽检 Die（行 {row} / 列 {column}）已切换（BR-38，可设多个）";
    }

    /* ---------------- 调试闭环命令 ---------------- */

    [RelayCommand]
    private void LoadFolder()
    {
        string folder = _dialogs.PickImageFolder(FolderText);
        if (string.IsNullOrWhiteSpace(folder)) return;

        _store.SetImageFolder(_recipeId, folder);
        var images = _dialogs.EnumerateImages(folder);
        Images.Clear();
        foreach (var image in images) Images.Add(new ImageRow(image));
        if (images.Count > 0) _store.SelectImage(_recipeId, images[0]);
        FolderText = folder;
        StatusText = $"已加载本地图片文件夹 {images.Count} 幅（F-3.8.1-02）";
        LoopStepText = "① 加扫描区域（已加载图片）";
    }

    [RelayCommand]
    private void AddRegionFromFields()
    {
        if (!TryParseRect(RegionX, RegionY, RegionW, RegionH, out var rect))
        {
            _dialogs.ShowMessage("添加扫描区域", "起始行/列需 >= 1，宽高需 > 0 且为数值（1-based Die 网格）。");
            return;
        }
        AddRegion(rect);
    }

    [RelayCommand]
    private void ClearRegions()
    {
        if (!_dialogs.Confirm("清除扫描区域（F-3.8.1-04 / BR-39）",
                "清除全部扫描区域并回到默认全片扫描（BR-37）。是否继续？")) return;
        _store.ClearScanRegions(_recipeId);
        StatusText = "已清除全部扫描区域（BR-37/39）";
    }

    /// <summary>F-3.8.1-05：单幅图片测试（只作用于底部缺陷图片路径那一幅）。</summary>
    [RelayCommand]
    private void TestSingle()
    {
        ApplyParameters();
        LastOutcome = _store.TestSingle(_recipeId);
        ApplyOutcome("单幅图片测试");
        LoopStepText = "⑥ 单幅/全部测试";
    }

    /// <summary>F-3.8.1-06：全部图片测试（对本次加载的所有图片批量重检）。</summary>
    [RelayCommand]
    private void TestAll()
    {
        ApplyParameters();
        var paths = Images.Count > 0
            ? Images.Select(i => i.Path).ToList()
            : _dialogs.EnumerateImages(FolderText ?? string.Empty);
        LastOutcome = _store.TestAll(_recipeId, paths);
        ApplyOutcome("全部图片测试");
        LoopStepText = "⑥ 单幅/全部测试";
    }

    /// <summary>F-3.8.1-07：预扫描开始；设备动作未下发（T-029），仅覆盖框选区域（BR-41）。</summary>
    [RelayCommand]
    private void Prescan()
    {
        ApplyParameters();
        LastOutcome = _store.Prescan(_recipeId, MapRegionCanvasState.Columns, MapRegionCanvasState.Rows);
        ApplyOutcome("预扫描（离线演算）");
        LoopStepText = "③ 查看缺陷列表";
    }

    /// <summary>F-3.8.1-10：ROI 参数设置页 —— 应用当前动态参数集合（T-014 可扩展，非固定滑块）。</summary>
    [RelayCommand]
    private void ApplyParameters()
    {
        _suppressRefresh = true;
        try
        {
            foreach (var row in ParameterRows) _store.SetParameter(_recipeId, row.Key, row.Value);
        }
        catch (InvalidOperationException ex)
        {
            _dialogs.ShowMessage("应用参数失败", ex.Message);
        }
        finally
        {
            _suppressRefresh = false;
        }
        Refresh();
        StatusText = $"已应用 {ParameterRows.Count} 个算法参数（检测 / 量测 / 全局，T-014 待确认项已显式标注）";
        LoopStepText = "⑤ ROI 参数设置调参";
    }

    [RelayCommand]
    private void AddParameter()
    {
        if (string.IsNullOrWhiteSpace(NewParameterKey))
        {
            _dialogs.ShowMessage("添加算法参数", "参数键必填（T-014：按实机算法文档增删）。");
            return;
        }
        try
        {
            _store.AddParameter(_recipeId, new AlgorithmParameter(
                NewParameterKey.Trim(),
                string.IsNullOrWhiteSpace(NewParameterGroup) ? AlgorithmParameterCatalog.GlobalGroup : NewParameterGroup.Trim(),
                NewParameterValue?.Trim() ?? string.Empty,
                NewParameterUnit?.Trim() ?? string.Empty,
                "文本",
                "用户自定义参数项（T-014 待实机确认）"));
            StatusText = $"已添加算法参数「{NewParameterKey.Trim()}」（可扩展集合，R-H04-06）";
            NewParameterKey = string.Empty;
            NewParameterValue = string.Empty;
        }
        catch (InvalidOperationException ex)
        {
            _dialogs.ShowMessage("添加算法参数失败", ex.Message);
        }
    }

    [RelayCommand]
    private void RemoveParameter(ParameterRow row)
    {
        if (row == null) return;
        if (!_dialogs.Confirm("移除算法参数项", $"确认移除参数「{row.Key}」？")) return;
        _store.RemoveParameter(_recipeId, row.Key);
        StatusText = $"已移除算法参数「{row.Key}」（关闭页面后自动保存，BR-40）";
    }

    /// <summary>
    /// BR-40 / F-3.8.1-11 / AC-P2-05：关闭页面（右上角 ×）时系统保存当前参数配置。
    /// 由窗口 <c>Closing</c> 调用，先应用参数编辑再统一落盘，保证「关闭后重进参数保持」（AC-H04-03）。
    /// </summary>
    public void SaveOnClose()
    {
        _suppressRefresh = true;
        try
        {
            foreach (var row in ParameterRows) _store.SetParameter(_recipeId, row.Key, row.Value);
        }
        catch (InvalidOperationException ex)
        {
            _dialogs.ShowMessage("参数自动保存失败", ex.Message);
        }
        finally
        {
            _suppressRefresh = false;
        }

        bool saved = _store.Save(_recipeId);
        StatusText = saved
            ? "参数已自动保存到子配方（BR-40 / F-3.8.1-11），关闭后重进保持"
            : "参数未能持久化：子配方仓库中不存在该 Id（不伪造保存成功）";
        LoopStepText = "⑧ 关闭自动保存";
        Refresh();
    }

    private void ApplyOutcome(string title)
    {
        DefectResults.Clear();
        foreach (var defect in LastOutcome.Defects) DefectResults.Add(DefectResultRow.From(defect));
        StatusText = $"{title}：{LastOutcome.Summary}；{LastOutcome.DeviceActionNote}";
        SelectedDefect = DefectResults.FirstOrDefault();
        Refresh();
    }

    private static bool TryParseRect(string x, string y, string w, string h, out MapRect rect)
    {
        rect = null;
        if (!double.TryParse(x, out double rx) || !double.TryParse(y, out double ry) ||
            !double.TryParse(w, out double rw) || !double.TryParse(h, out double rh)) return false;
        if (rx < 1 || ry < 1 || rw <= 0 || rh <= 0) return false;
        rect = new MapRect(rx, ry, rw, rh);
        return true;
    }
}
