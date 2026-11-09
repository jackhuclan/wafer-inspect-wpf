using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WaferInspectHMI.Models;

public sealed partial class NavItem : ObservableBase
{
    public string Key { get; init; }
    public string Label { get; init; }
    public Geometry Icon { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BadgeVisibility))]
    private string _badge;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BadgeVisibility))]
    private bool _isSelected;

    public Visibility BadgeVisibility => string.IsNullOrEmpty(Badge) ? Visibility.Collapsed : Visibility.Visible;
}

/// <summary>可独立展开 / 收起的导航组；折叠只影响可见性，不改变组内选中态。</summary>
public sealed partial class NavGroup : ObservableBase
{
    public string Key { get; init; }
    public string Title { get; init; }
    public List<NavItem> Items { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExpansionHint))]
    [NotifyPropertyChangedFor(nameof(SelectionHint))]
    private bool _isExpanded = true;

    /// <summary>组内是否存在当前选中项：折叠时由标题上的蓝点继续提示。</summary>
    public bool HasSelection => Items?.Any(item => item.IsSelected) == true;

    public string ItemCountText => $"{Items?.Count ?? 0} 项";

    public string ExpansionHint => IsExpanded ? "点击收起此分组" : "点击展开此分组";

    public string SelectionHint => HasSelection ? "当前选中项在本组" : string.Empty;
}

public sealed class DieItem
{
    public double X { get; init; }
    public double Y { get; init; }
    public int Level { get; init; }
    public Brush Fill { get; init; }
    public string Tip { get; init; }
}

public sealed class DeviceItem
{
    public string Name { get; init; }
    public string Code { get; init; }
    public string Status { get; init; }
    public string Metrics { get; init; }
    public Brush StatusBrush { get; init; }
    public Brush StatusBack { get; init; }
}

public sealed class DefectItem
{
    public string Name { get; init; }
    public string Count { get; init; }
    public double Pct { get; init; }
    public string PctText { get; init; }
    public Brush BarBrush { get; init; }
}

public sealed partial class TaskItem : ObservableBase
{
    public string Id { get; init; }
    public string Lot { get; init; }
    public string Wafer { get; init; }
    public string Recipe { get; init; }
    public string Chamber { get; init; }
    public string Start { get; init; }
    public string StatusText { get; init; }
    public Brush StatusBrush { get; init; }
    public Brush StatusBack { get; init; }
    public bool IsRunning { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    private double _progress;

    public string ProgressText => $"{Math.Round(Progress)}%";
}

public sealed partial class AlarmItem : ObservableBase
{
    public string Id { get; init; }
    public string Severity { get; init; }
    public string Title { get; init; }
    public string Description { get; init; }
    public string Time { get; init; }
    public Brush SeverityBrush { get; init; }
    public Brush SeverityBack { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AckVisibility))]
    [NotifyPropertyChangedFor(nameof(AckedVisibility))]
    private bool _acked;

    public Visibility AckVisibility => Acked ? Visibility.Collapsed : Visibility.Visible;
    public Visibility AckedVisibility => Acked ? Visibility.Visible : Visibility.Collapsed;
}
