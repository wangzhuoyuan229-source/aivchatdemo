using System.Globalization;
using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

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

public sealed class FilePathToBitmapConverter : ConverterBase
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            using var stream = File.OpenRead(path);
            return Bitmap.DecodeToWidth(stream, 360, BitmapInterpolationMode.MediumQuality);
        }
        catch { return null; }
    }
}

public sealed class AvatarToBitmapConverter : ConverterBase
{
    private static readonly ConcurrentDictionary<string, Bitmap> Cache = new(StringComparer.Ordinal);

    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string avatar || string.IsNullOrWhiteSpace(avatar)) return null;
        try
        {
            return Cache.GetOrAdd(avatar, static source =>
            {
                if (source.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                {
                    var comma = source.IndexOf(',');
                    if (comma < 0 || !source[..comma].Contains(";base64", StringComparison.OrdinalIgnoreCase))
                        throw new FormatException("头像 Data URI 格式无效。");
                    var bytes = System.Convert.FromBase64String(source[(comma + 1)..]);
                    using var stream = new MemoryStream(bytes, writable: false);
                    return Bitmap.DecodeToWidth(stream, 256, BitmapInterpolationMode.MediumQuality);
                }
                if (!File.Exists(source)) throw new FileNotFoundException("头像文件不存在。", source);
                using var file = File.OpenRead(source);
                return Bitmap.DecodeToWidth(file, 256, BitmapInterpolationMode.MediumQuality);
            });
        }
        catch { return null; }
    }
}

public sealed class AvatarToTextConverter : ConverterBase
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var avatar = value as string;
        if (string.IsNullOrWhiteSpace(avatar)) return "🎭";
        if (avatar.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) || File.Exists(avatar))
            return string.Empty;
        if (avatar.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return "🎭";
        return avatar;
    }
}
