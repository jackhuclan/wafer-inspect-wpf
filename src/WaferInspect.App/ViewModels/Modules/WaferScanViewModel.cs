using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WaferInspect.Core.Maps;
using WaferInspectHMI.Models;

namespace WaferInspectHMI.ViewModels.Modules;

/// <summary>
/// H05「Wafer 扫描」（PRD 4.5.2 / UI-3.7.3，P4-A 增量 1.5.0）：
/// 扫描参数（晶圆大小 BR-46 / 扫描速度 BR-47 / 扩膜 BR-48 / 创建方式 F-3.7.3-09）、
/// Map 编辑工具（Die 笔刷 F-3.7.3-19 / 整行整列 F-3.7.3-20 / 矩形批量 F-3.7.3-22 /
/// 外扩内缩 F-3.7.3-21·23，R-H05-05）、全景图（F-3.7.3-10，H06 采集 T-029 诚实搁置）。
/// 12 步流程编排（B-24 / R-H05-04）与 Sinf 导出（BR-50）属 P4-B 增量，本页仅登记未迁移项。
/// 所有 Bin / 颜色一律来自 <see cref="IMapTypeCatalog"/>（B-11），Map 绘制复用唯一 <see cref="MapGridView"/>（B-06）。
/// </summary>
public sealed partial class WaferScanViewModel : ObservableBase
{
    private readonly IMapTypeCatalog _catalog;
    private readonly MapGridModel _map = new();
    private readonly IH05DialogService _dialogs;
    private string _currentMapTypeId = string.Empty;

    public WaferScanViewModel(IMapTypeCatalog catalog, IH05DialogService dialogs)
    {
        _catalog = catalog;
        _dialogs = dialogs;

        _catalog.Changed += (_, _) => ReloadTypes();
        _map.Changed += (_, _) => RefreshMapText();

        WaferSizeOptions = new ObservableCollection<string> { "2", "3", "4", "5", "6", "8", "10", "12" };
        CreateModeOptions = new ObservableCollection<string> { "无图案（光片晶圆）", "网格匹配（小 Die）", "模板匹配（其他）" };

        foreach (var type in _catalog.Types) Types.Add(new MapTypeRow(type));
        SelectedType = Types.FirstOrDefault();
        LoadBrushBins();
        RefreshMapText();
    }

    /// <summary>Wafer 扫描专用 Map 模型（本页私有实例，与 maps 页预览隔离，B-06 唯一绘制规则）。</summary>
    public MapGridModel Map => _map;

    public ObservableCollection<MapTypeRow> Types { get; } = new();
    public ObservableCollection<BrushBinItem> BrushBins { get; } = new();
    public ObservableCollection<string> WaferSizeOptions { get; }
    public ObservableCollection<string> CreateModeOptions { get; }

    [ObservableProperty] private MapTypeRow _selectedType;
    [ObservableProperty] private BrushBinItem _selectedBrushBin;
    [ObservableProperty] private BinDefinition _brushBinDef;
    [ObservableProperty] private bool _isBatchMode;
    [ObservableProperty] private string _waferSize = "12";
    [ObservableProperty] private int _scanSpeed = 300;
    [ObservableProperty] private bool _expandFilm;
    [ObservableProperty] private string _createMode = "模板匹配（其他）";
    [ObservableProperty] private int _shrinkStep = 1;
    [ObservableProperty] private string _jumpX = "3";
    [ObservableProperty] private string _jumpY = "7";
    [ObservableProperty] private string _statusText = "请选择图谱类型并生成 Map 图";
    [ObservableProperty] private string _mapStatusText = "尚未生成 Map 图";
    [ObservableProperty] private string _selectedDieText = "未选中 Die（在 Map 上双击）";
    [ObservableProperty] private string _revisionText = "刷新版本 v0";
    [ObservableProperty] private string _panoramaText = "[搁置] 全景图需 H06 图像采集（T-029），未接入";
    [ObservableProperty] private string _deferredNote =
        "[未迁移·P4-B] 12 步 Map 创建流程（R-H05-04 / B-24）、Sinf 导出（BR-50 / B-12）属后续增量；" +
        "F-3.7.3-02/03 模板类型/匹配分数复用 H03 单一实现对位能力（B-08）；" +
        "F-3.7.3-04 拉平 / 05 寻找圆心 / 08 扫描执行 / 10 全景图 / 13 创建模板 / 16~18 匹配搜索圆·锁定·Die 匹配 / 14 填充未匹配 " +
        "依赖 H03/H06（T-029 / T-035），本页仅提供参数与本地编辑，不伪造设备/算法结果（E-08 / E-10）。";

    partial void OnSelectedTypeChanged(MapTypeRow value) => LoadBrushBins();

    partial void OnSelectedBrushBinChanged(BrushBinItem value)
        => BrushBinDef = value?.Bin;

    private void ReloadTypes()
    {
        string keepId = SelectedType?.Id ?? string.Empty;
        Types.Clear();
        foreach (var type in _catalog.Types) Types.Add(new MapTypeRow(type));
        SelectedType = Types.FirstOrDefault(t => t.Id == keepId) ?? Types.FirstOrDefault();
        LoadBrushBins();
    }

    private void LoadBrushBins()
    {
        BrushBins.Clear();
        var type = _catalog.Find(SelectedType?.Id ?? string.Empty);
        if (type == null) { SelectedBrushBin = null; BrushBinDef = null; return; }
        foreach (var bin in type.Bins) BrushBins.Add(new BrushBinItem(bin));
        // 默认选中 GoodDie；否则取第一个（F-3.7.3-25 首步即选 GoodDie Bin）。
        SelectedBrushBin = BrushBins.FirstOrDefault(b => b.Bin.Kind == BinKind.GoodDie) ?? BrushBins.FirstOrDefault();
    }

    private void RefreshMapText()
    {
        MapStatusText = _map.StatusText;
        SelectedDieText = _map.Selected?.Tip ?? "未选中 Die（在 Map 上双击）";
        RevisionText = $"刷新版本 v{_map.RefreshVersion}";
    }

    /* ---------------- 扫描参数（BR-46/47/48 / F-3.7.3-09） ---------------- */

    [RelayCommand]
    private void ApplyScanParams()
    {
        if (ScanSpeed < 100 || ScanSpeed > 500)
        {
            _dialogs.ShowMessage("扫描速度（BR-47）", "扫描速度须在 100–500 之间，默认 300。");
            return;
        }
        StatusText = $"已应用扫描参数：晶圆 {WaferSize} 寸 · 速度 {ScanSpeed} · 创建方式「{CreateMode}」" +
                     (ExpandFilm ? " · 扩膜（BR-48）" : " · 非扩膜");
    }

    /* ---------------- Map 生成与编辑（F-3.7.3-11 / R-H05-05） ---------------- */

    [RelayCommand]
    private void GenerateMap()
    {
        if (SelectedType == null) { StatusText = "请先选择图谱类型（F-3.7.3-11）"; return; }
        var type = _catalog.Find(SelectedType.Id);
        if (type == null || string.IsNullOrWhiteSpace(type.MapFilePath))
        {
            StatusText = "该图谱类型无图谱文件路径（BR-45：生成 Map 图需先导入图谱文件）";
            return;
        }
        _map.Generate(SelectedType.Id, _catalog);
        _currentMapTypeId = SelectedType.Id;
        StatusText = _map.StatusText;
    }

    [RelayCommand]
    private void RefreshMap()
    {
        if (string.IsNullOrEmpty(_currentMapTypeId)) { StatusText = "请先生成 Map 图"; return; }
        _map.Refresh(_currentMapTypeId, _catalog);
        StatusText = _map.StatusText;
    }

    [RelayCommand]
    private void EditRow()
    {
        if (BrushBinDef == null) { StatusText = "请先在右侧选择笔刷 Bin"; return; }
        if (_map.Selected == null) { StatusText = "请先在 Map 上双击选中一个 Die（确定行）"; return; }
        _map.EditRow(_map.Selected.Row, BrushBinDef);
        StatusText = _map.StatusText;
    }

    [RelayCommand]
    private void EditColumn()
    {
        if (BrushBinDef == null) { StatusText = "请先在右侧选择笔刷 Bin"; return; }
        if (_map.Selected == null) { StatusText = "请先在 Map 上双击选中一个 Die（确定列）"; return; }
        _map.EditColumn(_map.Selected.Column, BrushBinDef);
        StatusText = _map.StatusText;
    }

    [RelayCommand]
    private void Expand()
    {
        if (BrushBinDef == null) { StatusText = "请先在右侧选择基准 Bin（如 GoodDie）"; return; }
        var blank = BinKind.BlankDie.CreateDefault(0, MapLevelPalette.ColorFor("PL", BinKind.BlankDie.DefaultLevel()));
        _map.ExpandShrink("expand", BrushBinDef, blank, ShrinkStep);
        StatusText = _map.StatusText;
    }

    [RelayCommand]
    private void Shrink()
    {
        if (BrushBinDef == null) { StatusText = "请先在右侧选择基准 Bin（如 GoodDie）"; return; }
        var blank = BinKind.BlankDie.CreateDefault(0, MapLevelPalette.ColorFor("PL", BinKind.BlankDie.DefaultLevel()));
        _map.ExpandShrink("shrink", BrushBinDef, blank, ShrinkStep);
        StatusText = _map.StatusText;
    }

    [RelayCommand]
    private void JumpToDie()
    {
        if (!int.TryParse(JumpX?.Trim(), out int x) || !int.TryParse(JumpY?.Trim(), out int y))
        {
            _dialogs.ShowMessage("X/Y 跳转", "X（列）与 Y（行）必须为整数（BR-52）。");
            return;
        }
        _map.JumpTo(x, y);
        StatusText = _map.StatusText;
    }

    /// <summary>切换矩形批量模式（F-3.7.3-22）：开启后 Map 上拖拽框选区域。</summary>
    [RelayCommand]
    private void ToggleBatch() => IsBatchMode = !IsBatchMode;

    /// <summary>矩形批量选择完成（F-3.7.3-22）：把笔刷 Bin 应用到框选区域。</summary>
    public void OnRectSelected(int c0, int r0, int c1, int r1)
    {
        if (BrushBinDef == null) { StatusText = "请先在右侧选择笔刷 Bin，再框选区域批量修改"; return; }
        _map.EditRect(c0, r0, c1, r1, BrushBinDef);
        StatusText = _map.StatusText;
    }
}

/// <summary>笔刷 Bin 选项（图谱类型 Bin 的只读投影，供 Wafer 扫描选择当前笔刷）。</summary>
public sealed class BrushBinItem
{
    public BrushBinItem(BinDefinition bin) => Bin = bin;

    public BinDefinition Bin { get; }

    public string Code => Bin.Code;

    public string Name => Bin.Name;

    public string ColorHex => Bin.ColorHex;

    public string Display => $"{Name}（{Code}）";
}
