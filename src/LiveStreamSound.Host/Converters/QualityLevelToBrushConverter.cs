using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using LiveStreamSound.Shared.Diagnostics;

namespace LiveStreamSound.Host.Converters;

public sealed class QualityLevelToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is QualityLevel level)
        {
            return level switch
            {
                QualityLevel.Good => new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A)),
                QualityLevel.Degraded => new SolidColorBrush(Color.FromRgb(0xEA, 0xB3, 0x08)),
                QualityLevel.Bad => new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
                QualityLevel.Disconnected => new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
                _ => Brushes.Gray,
            };
        }
        return Brushes.Gray;
    }

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
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            LogLevel.Error or LogLevel.Critical => new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
            LogLevel.Warning => new SolidColorBrush(Color.FromRgb(0xEA, 0xB3, 0x08)),
            LogLevel.Info => new SolidColorBrush(Color.FromRgb(0x60, 0xA5, 0xFA)),
            _ => Brushes.Gray,
        };
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
