using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WaferInspect.Core.Maps;
using WaferInspect.Core.Recipes;

namespace WaferInspectHMI.ViewModels.Modules;

/// <summary>
/// H05「图谱管理 · MapType 编辑」（PRD 4.5.1 / R-H05-02/03/07）：
/// 类型 CRUD、Bin 代码增删、8 类 Bin 全字段可编辑、BR-45 生成 Map 图与预览、
/// BR-51/52 坐标与 X/Y 跳转、BR-53 显式刷新、AC-H05-02 快捷键验证。
/// 所有数据只来自 <see cref="IMapTypeCatalog"/>（B-11），Map 绘制只来自 <see cref="MapGridModel"/>（B-06）。
/// </summary>
public sealed partial class H05MapViewModel : ModuleViewModelBase
{
    private readonly IMapTypeCatalog _catalog;
    private readonly MapGridModel _map;
    private readonly IJobRepository _jobs;
    private readonly IH05DialogService _dialogs;
    private bool _loading;

    public H05MapViewModel(
        IMapTypeCatalog catalog,
        MapGridModel map,
        IJobRepository jobs,
        IH05DialogService dialogs)
        : base("H05", "图谱与 Map 数据管理", "P2（MapType / Map 渲染）· P3（Sinf 复判）· P4-A（Wafer 扫描 Map 编辑，1.5.0）· P4-B（12 步流程 / Sinf 导出，1.6.0）")
    {
        _catalog = catalog;
        _map = map;
        _jobs = jobs;
        _dialogs = dialogs;

        _catalog.Changed += (_, _) => ReloadTypes();
        _map.Changed += (_, _) => RefreshGridText();
        ReloadTypes();
    }

    private string CurrentId => SelectedType?.Id ?? string.Empty;

    /// <summary>H05 唯一 Map 网格模型（B-06），由共享 <c>MapGridView</c> 直接绑定。</summary>
    public MapGridModel Map => _map;

    public IReadOnlyList<string> FixedKinds => H05Options.Kinds;

    public ObservableCollection<MapTypeRow> Types { get; } = new();
    public ObservableCollection<BinRow> Bins { get; } = new();
    public ObservableCollection<string> AvailableKindNames { get; } = new();

    [ObservableProperty] private MapTypeRow _selectedType;
    [ObservableProperty] private BinRow _selectedBin;
    [ObservableProperty] private string _typeName = string.Empty;
    [ObservableProperty] private string _mapFilePath = string.Empty;
    [ObservableProperty] private string _newBinKind = string.Empty;
    [ObservableProperty] private string _shortcutKey = "Space";
    [ObservableProperty] private string _shortcutResult = "尚未验证";
    [ObservableProperty] private string _jumpX = "3";
    [ObservableProperty] private string _jumpY = "7";
    [ObservableProperty] private string _statusText = "请选择图谱类型";
    [ObservableProperty] private string _consumerText = "尚未载入消费方状态";
    [ObservableProperty] private string _gridStatusText = "尚未生成 Map 图";
    [ObservableProperty] private string _selectedDieText = "未选中 Die";
    [ObservableProperty] private string _revisionText = "刷新版本 v0";

    /// <summary>R-H05-04（12 步流程）/ R-H05-06（Sinf）已在 P4-B（1.6.0）由独立「Wafer 扫描」页交付；本说明登记诚实边界。</summary>
    public string DeferredNote =>
        "[P4-B·已交付] Wafer 扫描 12 步流程（R-H05-04 / B-24）由独立「Wafer 扫描」页编排真实落地，依赖 H03/H06 的步骤（3~9）诚实 `[搁置]`（E-08/E-10）；" +
        "Sinf 导出（R-H05-06 / BR-50 / B-12）为诚实内部导出（自洽 CSV，标注「非工业 Sinf 字节流，T-037 待确认」）；" +
        "Ink 墨点 / ReferenceDie 笔刷（BR-49）已交付。Die 笔刷 / 整行整列 / 矩形批量 / 外扩内缩（R-H05-05）已在 P4-A（1.5.0）" +
        "由独立「Wafer 扫描」页交付，复用本页同一 MapGridModel 与 MapGridView（B-06）。";

    public string FixedKindsText => string.Join(" / ", BinKindCatalog.FixedKinds.Select(k => k.ToString()));

    partial void OnSelectedTypeChanged(MapTypeRow value)
    {
        if (_loading) return;
        LoadType();
    }

    private void ReloadTypes()
    {
        string keepId = CurrentId;
        _loading = true;
        Types.Clear();
        foreach (var type in _catalog.Types) Types.Add(new MapTypeRow(type));
        SelectedType = Types.FirstOrDefault(t => t.Id == keepId) ?? Types.FirstOrDefault();
        _loading = false;
        LoadType();
    }

    private void LoadType()
    {
        _loading = true;
        Bins.Clear();
        AvailableKindNames.Clear();
        if (SelectedType != null)
        {
            var type = _catalog.Find(SelectedType.Id) ?? SelectedType.Source;
            TypeName = type.Name;
            MapFilePath = type.MapFilePath;
            foreach (var bin in type.Bins) Bins.Add(new BinRow(bin));
            foreach (var kind in _catalog.AvailableKinds(CurrentId)) AvailableKindNames.Add(kind.Text());
            if (AvailableKindNames.Count > 0) NewBinKind = AvailableKindNames[0];
            StatusText = $"已载入图谱「{type.Name}」：{type.Bins.Count} 类 Bin（BR-43 固定枚举）";
        }
        else
        {
            TypeName = string.Empty;
            MapFilePath = string.Empty;
            StatusText = "尚无图谱类型：点击「新增」创建";
        }
        _loading = false;
        UpdateConsumerText();
        RefreshGridText();
    }

    private void UpdateConsumerText()
    {
        if (string.IsNullOrEmpty(CurrentId))
        {
            ConsumerText = "尚无图谱类型";
            return;
        }
        var shortcutBin = _catalog.ResolveShortcut(CurrentId, "Space");
        int referenced = _jobs.Jobs.Count(j => string.Equals(j.MapTypeId, CurrentId, StringComparison.Ordinal));
        ConsumerText = $"B-11 唯一数据源：H02 图谱选择 {_catalog.Types.Count} 套 · " +
                       $"H01 着色 Level0={_catalog.ColorForLevel("PL", 0)} · " +
                       $"H04/H03 坐标 {MapGridModel.FormatCoordinate(0, 0)} · " +
                       $"H08 复判快捷键 Space→{shortcutBin?.Code ?? "未绑定"} · " +
                       $"Job 引用 {referenced} 项（T-043 删除保护）";
    }

    private void RefreshGridText()
    {
        GridStatusText = _map.StatusText;
        SelectedDieText = _map.Selected?.Tip ?? "未选中 Die（双击 Map 网格）";
        RevisionText = $"刷新版本 v{_map.RefreshVersion} · " +
                       (_map.NeedsRefresh ? "数据已变化，需点「刷新图谱」（BR-53）" : "颜色已同步");
    }

    /* ---------------- 图谱类型 CRUD（F-3.7.2-01~02） ---------------- */

    [RelayCommand]
    private void NewType()
    {
        string name = _dialogs.PromptTypeName("新增图谱类型（F-3.7.2-02）", "新图谱类型");
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            var created = _catalog.Create(name);
            SelectTypeById(created.Id);
            StatusText = $"已新增图谱类型「{created.Name}」（8 类 Bin 默认全量启用，BR-43）";
        }
        catch (InvalidOperationException ex) { _dialogs.ShowMessage("新增失败", ex.Message); }
    }

    [RelayCommand]
    private void DuplicateType()
    {
        if (string.IsNullOrEmpty(CurrentId)) return;
        try
        {
            var copy = _catalog.Duplicate(CurrentId);
            SelectTypeById(copy.Id);
            StatusText = $"已复制图谱类型「{copy.Name}」（多套并存，T-043）";
        }
        catch (InvalidOperationException ex) { _dialogs.ShowMessage("复制失败", ex.Message); }
    }

    [RelayCommand]
    private void RenameType()
    {
        if (string.IsNullOrEmpty(CurrentId)) return;
        string name = _dialogs.PromptTypeName("重命名图谱类型（F-3.7.2-02）", TypeName);
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            _catalog.Rename(CurrentId, name);
            TypeName = name;
            StatusText = $"已重命名为「{name}」（H02 图谱选择同步刷新）";
        }
        catch (InvalidOperationException ex) { _dialogs.ShowMessage("重命名失败", ex.Message); }
    }

    [RelayCommand]
    private void DeleteType()
    {
        if (string.IsNullOrEmpty(CurrentId)) return;
        if (!_dialogs.Confirm("删除图谱类型（F-3.7.2-02）",
                $"确认删除「{SelectedType.Name}」？被 Job 引用时系统会拒绝删除（T-043 引用检查）。")) return;
        try
        {
            _catalog.Delete(CurrentId);
            StatusText = "已删除图谱类型";
            ReloadTypes();
        }
        catch (InvalidOperationException ex) { _dialogs.ShowMessage("删除被拒绝", ex.Message); }
    }

    [RelayCommand]
    private void SaveType()
    {
        if (string.IsNullOrEmpty(CurrentId)) return;
        var type = _catalog.Find(CurrentId);
        if (type == null) return;
        try
        {
            _catalog.Update(type with { Name = TypeName, MapFilePath = MapFilePath });
            StatusText = $"已保存图谱「{TypeName}」（AC-H05-01：所有消费方读同一份数据）";
        }
        catch (InvalidOperationException ex) { _dialogs.ShowMessage("保存失败", ex.Message); }
    }

    [RelayCommand]
    private void ImportMapFile()
    {
        string path = _dialogs.PickMapFile(MapFilePath);
        if (string.IsNullOrWhiteSpace(path)) return;
        MapFilePath = path;
        var type = _catalog.Find(CurrentId);
        if (type != null)
        {
            try
            {
                _catalog.Update(type with { MapFilePath = path });
                StatusText = $"已导入图谱文件：{path}（BR-45：现在可生成 Map 图）";
            }
            catch (InvalidOperationException ex) { _dialogs.ShowMessage("导入失败", ex.Message); }
        }
    }

    [RelayCommand]
    private void RefreshTypes()
    {
        _catalog.Refresh();
        StatusText = "已刷新图谱类型列表";
    }

    /* ---------------- Bin 代码编辑（F-3.7.2-03~10） ---------------- */

    [RelayCommand]
    private void AddBinCode()
    {
        if (string.IsNullOrEmpty(CurrentId)) return;
        if (string.IsNullOrWhiteSpace(NewBinKind))
        {
            _dialogs.ShowMessage("增加 Bin 代码", "请先选择要启用的 Bin 类型（BR-43 固定 8 类，不能新增第 9 种）。");
            return;
        }
        try
        {
            var bin = _catalog.AddBin(CurrentId, H05Options.ParseKind(NewBinKind));
            StatusText = $"已增加 Bin 代码「{bin.Code}」（{bin.Kind.Text()}，F-3.7.2-03）";
        }
        catch (InvalidOperationException ex) { _dialogs.ShowMessage("增加 Bin 代码失败", ex.Message); }
    }

    [RelayCommand]
    private void RemoveBinCode(BinRow row)
    {
        if (row == null || string.IsNullOrEmpty(CurrentId)) return;
        try
        {
            _catalog.RemoveBin(CurrentId, row.Index);
            StatusText = $"已移除 Bin 代码 {row.Code}（F-3.7.2-03）";
        }
        catch (InvalidOperationException ex) { _dialogs.ShowMessage("移除 Bin 代码失败", ex.Message); }
    }

    [RelayCommand]
    private void SaveBin(BinRow row)
    {
        if (row == null || string.IsNullOrEmpty(CurrentId)) return;
        // SelectedItem 绑定尚未解析到选项时 KindText 可能暂为空，此时保留原类型，不静默改成默认值。
        if (!string.IsNullOrWhiteSpace(row.KindText)) row.ApplyKind(H05Options.ParseKind(row.KindText));
        try
        {
            _catalog.UpdateBin(CurrentId, row.Index, row.ToDefinition());
            // AC-H05-01：MapType 保存后显式刷新 Map 着色（BR-53 约束的是「复判完成后不自动刷新」）。
            if (_map.Cells.Count > 0) _map.Refresh(CurrentId, _catalog);
            StatusText = $"已保存 Bin「{row.Code}」：等级 {row.Level} / 颜色 {row.ColorHex} / 快捷键 {row.Shortcut}" +
                         "（AC-H05-01 消费方同步）";
        }
        catch (InvalidOperationException ex) { _dialogs.ShowMessage("保存 Bin 失败", ex.Message); }
    }

    /* ---------------- Map 预览（BR-45/51/52/53） ---------------- */

    [RelayCommand]
    private void GenerateMap()
    {
        if (string.IsNullOrEmpty(CurrentId)) return;
        _map.Generate(CurrentId, _catalog);
        StatusText = _map.StatusText;
    }

    [RelayCommand]
    private void RefreshMap()
    {
        if (string.IsNullOrEmpty(CurrentId)) return;
        _map.Refresh(CurrentId, _catalog);
        StatusText = _map.StatusText;
    }

    [RelayCommand]
    private void MarkDirty()
    {
        _map.MarkDirty();
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

    [RelayCommand]
    private void VerifyShortcut()
    {
        if (string.IsNullOrEmpty(CurrentId)) return;
        var bin = _catalog.ResolveShortcut(CurrentId, ShortcutKey);
        ShortcutResult = bin == null
            ? $"快捷键「{ShortcutKey}」未绑定任何 Bin（F-3.7.2-09）"
            : $"快捷键「{ShortcutKey}」→ Bin {bin.Code}（{bin.Name}，等级 {bin.Level}）· H08 人工复判消费口径（AC-H05-02）";
        StatusText = ShortcutResult;
    }

    private void SelectTypeById(string id)
    {
        _loading = true;
        SelectedType = Types.FirstOrDefault(t => t.Id == id) ?? Types.FirstOrDefault();
        _loading = false;
        LoadType();
    }
}
