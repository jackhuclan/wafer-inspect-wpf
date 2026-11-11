using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WaferInspectHMI.Converters;

/// <summary>布尔值 → 可见性（false 时折叠，不占位）。</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Visible;
}

/// <summary>布尔值取反的可见性。</summary>
public sealed class BoolToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not Visibility.Visible;
}

/// <summary>非空字符串 → 可见性（空/空白折叠；B-25：失败原因等非空信息才显示）。</summary>
public sealed class NonEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is string text && text.Trim().Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
    }

/// <summary>字符串 ↔ double? 转换：用于把字符串型 VM 数值属性绑定到 HandyControl 的 NumericUpDown.Value。
/// 健壮处理 null / 空 / 非数字（回退 0），往返均保持字符串或 double 语义。</summary>
public sealed class StringToDoubleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double d) return d;
        if (value is int i) return (double)i;
        if (value is string s && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var r))
            return r;
        return 0d;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // 非 null 的 double? 装箱后为 double；null 的 double? 装箱为 null。
        double v = value is double d ? d : 0d;

        if (targetType == typeof(string))
            return v.ToString(CultureInfo.InvariantCulture);
        if (targetType == typeof(int))
            return (int)Math.Round(v);
        return v;
    }
}
