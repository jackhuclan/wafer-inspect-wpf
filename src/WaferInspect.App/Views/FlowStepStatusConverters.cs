using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using WaferInspectHMI.ViewModels.Modules;

namespace WaferInspectHMI.Views;

/// <summary>FlowStepStatus → 中文标签（待执行 / 已完成 / 规划完成 / 搁置）。</summary>
public sealed class FlowStepStatusToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is FlowStepStatus s ? s switch
        {
            FlowStepStatus.Done => "已完成",
            FlowStepStatus.Planned => "规划完成",
            FlowStepStatus.Deferred => "搁置",
            _ => "待执行"
        } : "待执行";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>FlowStepStatus → 徽标画刷（待执行灰 / 已完成蓝 / 规划完成青 / 搁置琥珀）。</summary>
public sealed class FlowStepStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is FlowStepStatus s ? s switch
        {
            FlowStepStatus.Done => new SolidColorBrush(Color.FromRgb(0x1D, 0x4F, 0xD1)),
            FlowStepStatus.Planned => new SolidColorBrush(Color.FromRgb(0x0E, 0x9E, 0x8E)),
            FlowStepStatus.Deferred => new SolidColorBrush(Color.FromRgb(0xC8, 0x8A, 0x00)),
            _ => new SolidColorBrush(Color.FromRgb(0x9A, 0xA3, 0xB2))
        } : new SolidColorBrush(Color.FromRgb(0x9A, 0xA3, 0xB2));

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>bool → 中文「是 / 否」（用于 B-09 规划标记展示）。</summary>
public sealed class BoolToCnConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? "是" : "否";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
