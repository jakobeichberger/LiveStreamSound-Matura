using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace LiveStreamSound.Host.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Inverted { get; set; }
    public bool UseHidden { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var b = value is bool x && x;
        if (Inverted) b = !b;
        return b ? Visibility.Visible : (UseHidden ? Visibility.Hidden : Visibility.Collapsed);
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

public sealed class BoolToBrushConverter : IValueConverter
{
    public Brush TrueBrush { get; set; } = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));
    public Brush FalseBrush { get; set; } = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b && b ? TrueBrush : FalseBrush;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class IntToVisibilityConverter : IValueConverter
{
    /// <summary>When true, returns Visible for zero; when false, returns Visible for non-zero.</summary>
    public bool VisibleWhenZero { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isZero = value is int i && i == 0;
        return (VisibleWhenZero ? isZero : !isZero) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>0.0-1.0 float ↔ 0-100 double for sliders. Keeps the model in float while the UI
/// presents percent — also makes the UIA value announcement correct.</summary>
public sealed class FloatToPercentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is float f ? (double)Math.Round(f * 100) : 0d;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double d ? (float)(d / 100.0) : 0f;
}
