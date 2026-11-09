using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace WaferInspectHMI;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;
    public void Raise([CallerMemberName] string name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; Raise(name); return true;
    }
}

public sealed class NavItem : Observable
{
    public string Key { get; init; }
    public string Label { get; init; }
    public Geometry Icon { get; init; }
    public string Badge { get; set; }
    public Visibility BadgeVisibility => string.IsNullOrEmpty(Badge) ? Visibility.Collapsed : Visibility.Visible;

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { if (Set(ref _isSelected, value)) Raise(nameof(BadgeVisibility)); }
    }
}

public sealed class NavGroup
{
    public string Title { get; init; }
    public List<NavItem> Items { get; init; }
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

public sealed class TaskItem : Observable
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

    private double _progress;
    public double Progress
    {
        get => _progress;
        set { if (Set(ref _progress, value)) Raise(nameof(ProgressText)); }
    }
    public string ProgressText => $"{Math.Round(_progress)}%";
    public bool IsRunning { get; init; }
}

public sealed class AlarmItem : Observable
{
    public string Id { get; init; }
    public string Severity { get; init; }
    public string Title { get; init; }
    public string Description { get; init; }
    public string Time { get; init; }
    public Brush SeverityBrush { get; init; }
    public Brush SeverityBack { get; init; }

    private bool _acked;
    public bool Acked
    {
        get => _acked;
        set { if (Set(ref _acked, value)) { Raise(nameof(AckVisibility)); Raise(nameof(AckedVisibility)); } }
    }
    public Visibility AckVisibility => _acked ? Visibility.Collapsed : Visibility.Visible;
    public Visibility AckedVisibility => _acked ? Visibility.Visible : Visibility.Collapsed;
}
