using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WaferInspect.Core.Inspection;
using WaferInspect.Core.Recipes;

namespace WaferInspectHMI.ViewModels.Modules;

/// <summary>
/// H04「缺陷分析 · 算法参数调试」页（PRD 5.2 落点：缺陷分析 <c>defects</c>，无独立菜单）。
/// 与配方向导步骤 5/6 共用 <see cref="IRoiScanAreaStore"/> **同一单例**（B-01/B-02、AC-P2-03）：
/// 向导内修改扫描区域，本页与参数验证页立即可见同一区域，不存在第二份配置。
/// 设备采集 / 运动控制仍按用户裁决搁置（T-029），入口保留并显式提示「[搁置] 未下发」。
/// </summary>
public sealed partial class H04InspectionViewModel : ModuleViewModelBase, IMapRegionCanvasHost
{
    private readonly IRoiScanAreaStore _store;
    private readonly IJobRepository _jobs;
    private readonly ISubRecipeRepository _subs;
    private readonly IH04DialogService _dialogs;

    private bool _loading;

    public H04InspectionViewModel(
        IRoiScanAreaStore store,
        IJobRepository jobs,
        ISubRecipeRepository subs,
        IH04DialogService dialogs)
        : base("H04", "检测算法与 ROI 配置", "P2")
    {
        _store = store;
        _jobs = jobs;
        _subs = subs;
        _dialogs = dialogs;

        _store.Changed += (_, recipeId) =>
        {
            if (string.Equals(recipeId, CurrentRecipeId, StringComparison.Ordinal)) Refresh();
        };
        _subs.Changed += (_, _) => ReloadRecipes();
        ReloadRecipes();
    }

    private string CurrentRecipeId => SelectedRecipe?.Id ?? string.Empty;

    /// <summary>共享 Map 圈选画布状态（向导步骤 5/6 与参数验证页使用同一构建规则）。</summary>
    public MapRegionCanvasState Map { get; } = new();

    public ObservableCollection<RecipeOptionRow> Recipes { get; } = new();
    public ObservableCollection<RoiZoneRow> RoiZoneRows { get; } = new();
    public ObservableCollection<ParameterRow> ParameterRows { get; } = new();
    public ObservableCollection<ImageRow> Images { get; } = new();
    public ObservableCollection<DefectResultRow> DefectResults { get; } = new();

    [ObservableProperty] private RecipeOptionRow _selectedRecipe;
    [ObservableProperty] private string _statusText = "请选择子配方";
    [ObservableProperty] private string _syncText = "尚未载入 H04 唯一数据源";
    [ObservableProperty] private bool _scanFullWafer = true;
    [ObservableProperty] private bool _samplingEnabled;
    [ObservableProperty] private DefectResultRow _selectedDefect;
    [ObservableProperty] private string _regionX = "1";
    [ObservableProperty] private string _regionY = "1";
    [ObservableProperty] private string _regionW = "4";
    [ObservableProperty] private string _regionH = "4";
    [ObservableProperty] private string _folderText = string.Empty;

    /// <summary>F-3.8.1-09：选中缺陷记录后显示对应原始图片存储路径。</summary>
    public string OriginalImagePath => SelectedDefect?.SourceImagePath ?? string.Empty;

    /// <summary>F-3.8.1-12：8 步调试闭环（BR-42「分析缺陷 → 调整参数 → 验证效果」重复至满足要求）。</summary>
    public string ClosedLoopText =>
        "① 加扫描区域 → ② 预扫描 → ③ 查看缺陷列表 → ④ 选中记录看原图 → " +
        "⑤ 切到 ROI 参数设置调参 → ⑥ 回参数验证做单幅/全部测试 → ⑦ 重复 → ⑧ 关闭自动保存";

    public string DeviceNote =>
        "[搁置] 未下发（T-029）：预扫描运动、相机采集、光源控制与 AI 推理均未接入设备；" +
        "本页测试结果为确定性离线演算，不伪造设备执行成功。";

    partial void OnSelectedRecipeChanged(RecipeOptionRow value)
    {
        if (_loading) return;
        Refresh();
    }

    partial void OnSelectedDefectChanged(DefectResultRow value)
        => OnPropertyChanged(nameof(OriginalImagePath));

    partial void OnScanFullWaferChanged(bool value)
    {
        if (_loading || string.IsNullOrEmpty(CurrentRecipeId)) return;
        _store.SetFullWafer(CurrentRecipeId, value);
    }

    partial void OnSamplingEnabledChanged(bool value)
    {
        if (_loading || string.IsNullOrEmpty(CurrentRecipeId)) return;
        _store.SetSamplingEnabled(CurrentRecipeId, value);
    }

    private void ReloadRecipes()
    {
        string keepId = CurrentRecipeId;
        _loading = true;
        Recipes.Clear();
        foreach (var job in _jobs.Jobs)
        foreach (var recipe in _subs.ListOf(job.JobId))
            Recipes.Add(new RecipeOptionRow(recipe.Id, recipe.Name, job.Name));

        SelectedRecipe = Recipes.FirstOrDefault(r => r.Id == keepId) ?? Recipes.FirstOrDefault();
        _loading = false;
        Refresh();
    }

    /// <summary>从唯一数据源刷新全部呈现；不写入、不复制配置。</summary>
    private void Refresh()
    {
        if (string.IsNullOrEmpty(CurrentRecipeId))
        {
            _loading = true;
            RoiZoneRows.Clear();
            ParameterRows.Clear();
            DefectResults.Clear();
            SyncText = "尚无子配方：请先在「配方管理」中创建子配方";
            StatusText = "尚无子配方";
            _loading = false;
            return;
        }

        var snapshot = _store.Snapshot(CurrentRecipeId);
        _loading = true;
        Map.Rebuild(snapshot);

        RoiZoneRows.Clear();
        foreach (var zone in snapshot.Zones)
            RoiZoneRows.Add(new RoiZoneRow(zone.Index)
            {
                Name = zone.Name,
                X = zone.X.ToString("0.###"), Y = zone.Y.ToString("0.###"),
                Width = zone.Width.ToString("0.###"), Height = zone.Height.ToString("0.###")
            });

        ParameterRows.Clear();
        foreach (var parameter in snapshot.Parameters) ParameterRows.Add(new ParameterRow(parameter));

        ScanFullWafer = snapshot.FullWafer;
        SamplingEnabled = snapshot.SamplingEnabled;
        FolderText = snapshot.ImageFolderPath;
        string threshold = snapshot.Parameters.FirstOrDefault(p => p.Key == "Detection.Threshold")?.Value ?? "-";
        SyncText = $"H04 唯一数据源 · 扫描区域 {snapshot.ScanRegions.Count} 个 / ROI {snapshot.Zones.Count} 个 / " +
                   $"抽检 Die {snapshot.SamplingDies.Count} 颗 · 全片={snapshot.FullWafer} · " +
                   $"自动保存版本 v{snapshot.SavedVersion} · 检测阈值={threshold}" +
                   (snapshot.LastSavedAt is { } at ? $" · 最近保存 {at:HH:mm:ss}" : string.Empty);
        StatusText = $"已同步子配方「{SelectedRecipe.Name}」的配置（与向导步骤 5/6 同一份数据，AC-P2-03）";
        _loading = false;
    }

    /* ---------------- 共享画布回调（B-01/B-02：只写唯一数据源） ---------------- */

    public void AddRegion(MapRect rect)
    {
        if (string.IsNullOrEmpty(CurrentRecipeId))
        {
            _dialogs.ShowMessage("添加扫描区域", "请先选择子配方（F-3.8.1-03）。");
            return;
        }
        _store.AddScanRegion(CurrentRecipeId, rect, "缺陷分析 · Map 圈选");
        StatusText = $"已添加扫描区域：列 {rect.X:0} 起 {rect.Width:0} 列 / 行 {rect.Y:0} 起 {rect.Height:0} 行（F-3.8.1-03，与向导共用）";
    }

    public void ToggleDie(int row, int column)
    {
        if (string.IsNullOrEmpty(CurrentRecipeId)) return;
        _store.ToggleSamplingDie(CurrentRecipeId, row, column);
        StatusText = $"抽检 Die（行 {row} / 列 {column}）已切换（BR-38，可设多个）";
    }

    /* ---------------- 命令 ---------------- */

    /// <summary>UIA / 键盘等价路径：填写坐标添加扫描区域。</summary>
    [RelayCommand]
    private void AddRegionFromFields()
    {
        if (string.IsNullOrEmpty(CurrentRecipeId))
        {
            _dialogs.ShowMessage("添加扫描区域", "请先选择子配方。");
            return;
        }
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
        if (string.IsNullOrEmpty(CurrentRecipeId)) return;
        if (!_dialogs.Confirm("清除扫描区域（F-3.8.1-04 / BR-39）",
                "清除后将清空全部扫描区域并回到默认全片扫描（BR-37）。是否继续？")) return;
        _store.ClearScanRegions(CurrentRecipeId);
        StatusText = "已清除全部扫描区域并恢复默认全片扫描（BR-37/39）";
    }

    [RelayCommand]
    private void LoadFolder()
    {
        string folder = _dialogs.PickImageFolder(FolderText);
        if (string.IsNullOrWhiteSpace(folder)) return;

        if (string.IsNullOrEmpty(CurrentRecipeId))
        {
            _dialogs.ShowMessage("加载文件夹测试", "请先选择子配方。");
            return;
        }
        _store.SetImageFolder(CurrentRecipeId, folder);
        var images = _dialogs.EnumerateImages(folder);
        Images.Clear();
        foreach (var image in images) Images.Add(new ImageRow(image));
        if (images.Count > 0) _store.SelectImage(CurrentRecipeId, images[0]);
        StatusText = $"已加载本地图片文件夹：{images.Count} 幅（F-3.8.1-02；采集设备未接入，T-029）";
    }

    [RelayCommand]
    private void OpenValidation()
    {
        if (string.IsNullOrEmpty(CurrentRecipeId))
        {
            _dialogs.ShowMessage("参数验证", "请先选择子配方（参数配置写入 Recipe，R-H04-01）。");
            return;
        }

        // 关闭参数页（右上角 ×）由窗口统一调用 IRoiScanAreaStore.Save（BR-40 / AC-P2-05）。
        var outcome = _dialogs.ShowParameterValidation(CurrentRecipeId);
        DefectResults.Clear();
        if (outcome != null)
            foreach (var defect in outcome.Defects) DefectResults.Add(DefectResultRow.From(defect));
        Refresh();
        StatusText = outcome == null
            ? "参数验证页已关闭（参数已自动保存，BR-40）"
            : $"{outcome.Scope.Text()}完成：{outcome.Summary}（{outcome.DeviceActionNote}）";
    }

    [RelayCommand]
    private void RefreshView() => Refresh();

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
