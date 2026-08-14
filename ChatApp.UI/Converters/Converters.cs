using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace ChatApp.UI.Converters;

public class NullToVisibilityConverter : IValueConverter
{
    public static readonly NullToVisibilityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => !string.IsNullOrWhiteSpace(value as string) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility v && v == Visibility.Visible;
}

public class InverseNullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : Binding.DoNothing;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : Binding.DoNothing;
}

public class BooleanToVisibilityInverseConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class AccentIfEqualConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var equal = value?.ToString() == parameter?.ToString();
        if (equal && Application.Current.TryFindResource("AccentBrush") is Brush b) return b;
        return Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class BoolToAlignmentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b ? HorizontalAlignment.Right : HorizontalAlignment.Left;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class BoolToTextAlignmentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b ? TextAlignment.Right : TextAlignment.Left;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class BoolToFlowDirectionConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class BubbleBrushConverter : IValueConverter
{
    public static readonly BubbleBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isUser)
        {
            var key = isUser ? "UserBubbleBrush" : "AssistantBubbleBrush";
            if (Application.Current.TryFindResource(key) is Brush b) return b;
        }
        return Brushes.White;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
