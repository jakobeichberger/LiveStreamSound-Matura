using System.Globalization;
using System.Windows.Data;
using LiveStreamSound.Shared.Diagnostics;
using Wpf.Ui.Controls;

namespace LiveStreamSound.Host.Converters;

/// <summary>
/// Maps <see cref="QualityLevel"/> to a distinct icon shape so the indicator is readable
/// for color-blind users as well as for the SR (SymbolIcon has AutomationProperties.Name
/// derived from the symbol when not overridden).
/// </summary>
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
