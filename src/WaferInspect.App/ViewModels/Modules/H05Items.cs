using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using WaferInspect.Core.Maps;
using WaferInspectHMI.Models;

namespace WaferInspectHMI.ViewModels.Modules;

/// <summary>图谱类型下拉行（H05 唯一目录的只读投影）。</summary>
public sealed class MapTypeRow
{
    public MapTypeRow(MapTypeDefinition source) => Source = source;

    public MapTypeDefinition Source { get; }

    public string Id => Source.Id;
    public string Name => Source.Name;
    public int BinCount => Source.Bins.Count;
    public string Display => $"{Name}（{BinCount} 类 Bin）";
    public string MapFilePath => Source.MapFilePath;
}

/// <summary>
/// Bin 代码编辑行（F-3.7.2-05~10 / BR-69 七项）：类型、代码、类别名、等级、颜色、快捷键、备注、数量。
/// 文本字段保存时解析，避免 WPF 数字输入中途触发校验。
/// </summary>
public sealed partial class BinRow : ObservableBase
{
    public BinRow(BinDefinition source) => Load(source);

    public int Index { get; private set; }
    public BinKind Kind { get; private set; }

    public void Load(BinDefinition source)
    {
        Index = source.Index;
        Kind = source.Kind;
        KindText = source.Kind.Text();
        Code = source.Code;
        Name = source.Name;
        Level = source.Level.ToString();
        ColorHex = source.ColorHex;
        Shortcut = source.Shortcut;
        Note = source.Note;
        Quantity = source.Quantity.ToString();
    }

    [ObservableProperty] private string _kindText = string.Empty;
    [ObservableProperty] private string _code = string.Empty;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _level = "0";
    [ObservableProperty] private string _colorHex = "#E9F1FD";
    [ObservableProperty] private string _shortcut = string.Empty;
    [ObservableProperty] private string _note = string.Empty;
    [ObservableProperty] private string _quantity = "0";

    public string Title => $"Bin {Index + 1} · {KindText}（{Code}）";

    public BinDefinition ToDefinition()
    {
        if (!int.TryParse(Level?.Trim(), out int level)) level = 0;
        if (!int.TryParse(Quantity?.Trim(), out int quantity)) quantity = 0;
        return new BinDefinition(Index, Kind, Code?.Trim() ?? string.Empty, Name?.Trim() ?? string.Empty,
            level, ColorHex?.Trim() ?? "#E9F1FD", Shortcut?.Trim() ?? string.Empty,
            Note?.Trim() ?? string.Empty, quantity);
    }

    public void ApplyKind(BinKind kind)
    {
        Kind = kind;
        KindText = kind.Text();
    }
}

/// <summary>H05 界面共用的 8 类 Bin 中文选项（BR-43 固定枚举，唯一来源）。</summary>
public static class H05Options
{
    public static IReadOnlyList<string> Kinds { get; } = BinKindCatalog.FixedKinds.Select(k => k.Text()).ToList();

    public static BinKind ParseKind(string text)
        => BinKindCatalog.FixedKinds.FirstOrDefault(k => k.Text() == text, BinKind.GoodDie);
}
