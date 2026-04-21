using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using LiveStreamSound.Shared.Diagnostics;

namespace LiveStreamSound.Client.Converters;

public sealed class QualityLevelToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is QualityLevel level ? level switch
        {
            QualityLevel.Good => new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A)),
            QualityLevel.Degraded => new SolidColorBrush(Color.FromRgb(0xEA, 0xB3, 0x08)),
            QualityLevel.Bad => new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
            QualityLevel.Disconnected => new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
            _ => (Brush)Brushes.Gray,
        } : Brushes.Gray;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class QualityLevelToLocalizedStringConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is QualityLevel q ? q.LocalizedLabel() : "";
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Inverted { get; set; }
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var b = value is bool x && x;
        if (Inverted) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility v && (v == Visibility.Visible ^ Inverted);
}

public sealed class BoolInverterConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b ? !b : true;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b ? !b : true;
}

public sealed class StringToVisibilityConverter : IValueConverter
{
    public bool VisibleWhenEmpty { get; set; }
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isEmpty = string.IsNullOrEmpty(value as string);
        return (VisibleWhenEmpty ? isEmpty : !isEmpty) ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class StringHasContentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        !string.IsNullOrEmpty(value as string);
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class FloatToPercentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is float f ? (double)Math.Round(f * 100) : 0d;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double d ? (float)(d / 100.0) : 0f;
}

public sealed class LogLevelToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        LogLevel.Error or LogLevel.Critical => new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
        LogLevel.Warning => new SolidColorBrush(Color.FromRgb(0xEA, 0xB3, 0x08)),
        LogLevel.Info => new SolidColorBrush(Color.FromRgb(0x60, 0xA5, 0xFA)),
        _ => (Brush)Brushes.Gray,
    };
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
