using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;

namespace ChatApp.UI.Converters;

public abstract class ConverterBase : IValueConverter
{
    public abstract object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture);
    public virtual object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => AvaloniaProperty.UnsetValue;
}

public sealed class NullToVisibilityConverter : ConverterBase
{
    public static readonly NullToVisibilityConverter Instance = new();
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not null;
}

public sealed class StringToVisibilityConverter : ConverterBase
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string text && !string.IsNullOrWhiteSpace(text);
}

public sealed class BoolToVisibilityConverter : ConverterBase
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true;
    public override object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true;
}

public sealed class InverseNullToVisibilityConverter : ConverterBase
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is null;
}

public sealed class InverseBooleanConverter : ConverterBase
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
    public override object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

public sealed class BooleanToVisibilityInverseConverter : ConverterBase
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

public sealed class AccentIfEqualConverter : ConverterBase
{
    private static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#07C160"));
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase)
            ? Accent
            : Brushes.Transparent;
}

public sealed class BoolToAlignmentConverter : ConverterBase
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? HorizontalAlignment.Right : HorizontalAlignment.Left;
}

public sealed class BoolToTextAlignmentConverter : ConverterBase
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? TextAlignment.Right : TextAlignment.Left;
}

public sealed class BoolToFlowDirectionConverter : ConverterBase
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
}

public sealed class BubbleBrushConverter : ConverterBase
{
    public static readonly BubbleBrushConverter Instance = new();
    private static readonly IBrush User = new SolidColorBrush(Color.Parse("#95EC69"));
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? User : Brushes.White;
}
