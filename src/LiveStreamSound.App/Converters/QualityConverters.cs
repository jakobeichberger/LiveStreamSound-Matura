using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using LiveStreamSound.Shared.Diagnostics;
using Wpf.Ui.Controls;

namespace LiveStreamSound.App.Converters;

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

public sealed class QualityLevelToIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is QualityLevel q ? q switch
        {
            QualityLevel.Good => SymbolRegular.CheckmarkCircle24,
            QualityLevel.Degraded => SymbolRegular.AlertUrgent24,
            QualityLevel.Bad => SymbolRegular.ErrorCircle24,
            QualityLevel.Disconnected => SymbolRegular.PlugDisconnected24,
            _ => SymbolRegular.CircleSmall24,
        } : SymbolRegular.CircleSmall24;

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
