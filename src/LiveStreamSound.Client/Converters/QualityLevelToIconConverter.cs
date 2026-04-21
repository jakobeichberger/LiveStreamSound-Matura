using System.Globalization;
using System.Windows.Data;
using LiveStreamSound.Shared.Diagnostics;
using Wpf.Ui.Controls;

namespace LiveStreamSound.Client.Converters;

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
