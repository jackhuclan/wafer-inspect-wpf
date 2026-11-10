using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using WaferInspect.Core.Inspection;
using WaferInspect.Core.Maps;
using WaferInspect.Core.Run;
using WaferInspectHMI.Models;

namespace WaferInspectHMI.ViewModels.Modules;

/// <summary>子配方下拉项（缺陷分析页与参数验证页共用同一份选择口径）。</summary>
public sealed class RecipeOptionRow
{
    public RecipeOptionRow(string id, string name, string jobName)
    {
        Id = id;
        Name = name;
        JobName = jobName;
    }

    public string Id { get; }
    public string Name { get; }
    public string JobName { get; }
    public string Display => $"{Name}（{JobName}）";
}

/// <summary>Map 画布的 Die 单元格（20×20 网格，坐标 1-based）。</summary>
public sealed class MapCellRow
{
    public MapCellRow(int row, int column)
    {
        Row = row;
        Column = column;
    }

    public int Row { get; }
    public int Column { get; }
    public double X => (Column - 1) * MapRegionCanvasState.CellSize;
    public double Y => (Row - 1) * MapRegionCanvasState.CellSize;
    public string Fill { get; set; } = "#E8EDF5";
    public string Tip => $"{MapGridModel.FormatDieCoordinate(Column, Row)} · 行 {Row} / 列 {Column}（双击 = 抽检该 Die，BR-38）";
}

/// <summary>Map 画布上的扫描区域叠加层（同一份 H04 数据源的呈现，不另存一份配置）。</summary>
public sealed class ScanRegionRow
{
    public ScanRegionRow(ScanRegion region)
    {
        Name = region.Name;
        Source = region.Source;
        // MapRect 使用 1-based Die 网格坐标，画布像素从 0 起算。
        X = (region.Rect.X - 1) * MapRegionCanvasState.CellSize;
        Y = (region.Rect.Y - 1) * MapRegionCanvasState.CellSize;
        Width = region.Rect.Width * MapRegionCanvasState.CellSize;
        Height = region.Rect.Height * MapRegionCanvasState.CellSize;
    }

    public string Name { get; }
    public string Source { get; }
    public double X { get; }
    public double Y { get; }
    public double Width { get; }
    public double Height { get; }
    public string Tip => $"{Name} · {Source}";
}

/// <summary>
/// Map 区域圈选画布状态：向导步骤 5/6 与参数验证页共用同一状态构建规则，
/// 输入只有 <see cref="RoiScanAreaSnapshot"/>（H04 唯一数据源），因此两处看到的区域必然一致。
/// </summary>
public sealed class MapRegionCanvasState
{
    public const int Columns = 20;
    public const int Rows = 20;
    public const double CellSize = 20;

    public ObservableCollection<MapCellRow> Cells { get; } = new();
    public ObservableCollection<ScanRegionRow> Regions { get; } = new();

    /// <summary>快照重建完成通知：共享画布据此一次性重绘，避免逐项容器重建。</summary>
    public event EventHandler Refreshed;

    public MapRegionCanvasState()
    {
        for (int row = 1; row <= Rows; row++)
        for (int column = 1; column <= Columns; column++)
            Cells.Add(new MapCellRow(row, column));
    }

    public void Rebuild(RoiScanAreaSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var sampling = snapshot.SamplingDies.ToHashSet();

        // 重建单元格（ObservableCollection 不转发元素属性变更通知，重建可保证画布立即刷新）。
        Cells.Clear();
        for (int row = 1; row <= Rows; row++)
        for (int column = 1; column <= Columns; column++)
        {
            bool inRegion = snapshot.ScanRegions.Any(r => r.Rect.Contains(column, row));
            Cells.Add(new MapCellRow(row, column)
            {
                Fill = sampling.Contains(new SamplingDie(row, column))
                    ? "#86EFAC"                          // 抽检 Die（绿）
                    : snapshot.FullWafer
                        ? "#DBEAFE"                      // 全片（淡蓝）
                        : inRegion
                            ? "#93C5FD"                  // 圈选区域内（蓝）
                            : "#E8EDF5"                   // 区域外
            });
        }

        Regions.Clear();
        foreach (var region in snapshot.ScanRegions) Regions.Add(new ScanRegionRow(region));
        Refreshed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>检测设置列表编辑行（F-3.2.2-69：每项为独立检测单元）。</summary>
public sealed partial class RoiItemRow : ObservableBase
{
    public RoiItemRow(string id) => Id = id;

    /// <summary>保存后由 H04 服务返回正式 Id（新建时为空）。</summary>
    public string Id { get; set; }

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _kind = RoiTargetKind.Zone.Text();
    [ObservableProperty] private string _algorithmType = InspectionText.DefaultParameterType;
    [ObservableProperty] private string _parameterType = InspectionText.DefaultParameterType;
    [ObservableProperty] private string _extraction = RegionExtraction.TemplateMatching.Text();
    [ObservableProperty] private string _goldenTone = GoldenImageTone.Bright.Text();
    [ObservableProperty] private string _regionX = "1";
    [ObservableProperty] private string _regionY = "1";
    [ObservableProperty] private string _regionW = "1";
    [ObservableProperty] private string _regionH = "1";
    [ObservableProperty] private int? _roiZoneIndex;
    [ObservableProperty] private bool _saved;
    [ObservableProperty] private string _statusText = "未保存";

    public string SavedText => Saved ? "已保存" : "未保存（需点击保存，F-3.2.2-75）";

    partial void OnSavedChanged(bool value) => OnPropertyChanged(nameof(SavedText));
}

/// <summary>ROI 参数设置页的算法参数行（T-014 动态集合，禁止固定滑块）。</summary>
public sealed partial class ParameterRow : ObservableBase
{
    public ParameterRow(AlgorithmParameter parameter)
    {
        Key = parameter.Key;
        Group = parameter.Group;
        Unit = parameter.Unit;
        ValueType = parameter.ValueType;
        Description = parameter.Description;
        _value = parameter.Value;
    }

    public string Key { get; }
    public string Group { get; }
    public string Unit { get; }
    public string ValueType { get; }
    public string Description { get; }

    [ObservableProperty] private string _value;
}

/// <summary>本地测试图片行（F-3.8.1-02 加载文件夹测试）。</summary>
public sealed class ImageRow
{
    public ImageRow(string path) => Path = path;

    public string Path { get; }
    public string Name => System.IO.Path.GetFileName(Path);
}

/// <summary>缺陷结果列表行（B-23：由唯一 <see cref="DefectRecord"/> 投影，H01/H04/H08 共用）。</summary>
public sealed record DefectResultRow(
    string Zone,
    string Category,
    int Row,
    int Column,
    string Length,
    string Width,
    string Area,
    string ReviewState,
    string SourceImagePath,
    string Note)
{
    public static DefectResultRow From(DefectRecord record) => new(
        record.Zone,
        record.Category,
        record.Row,
        record.Column,
        $"{record.Length:F1}",
        $"{record.Width:F1}",
        $"{record.Area:F1}",
        record.ReviewState,
        record.SourceImagePath,
        record.Note);
}

/// <summary>
/// Map 区域圈选画布与承载 ViewModel 的交互契约：
/// 两个界面的画布控件只调用这两个方法，具体写入统一走 H04 唯一数据源。
/// </summary>
public interface IMapRegionCanvasHost
{
    void AddRegion(MapRect rect);
    void ToggleDie(int row, int column);
}
