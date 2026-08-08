using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace DanmuFree.App.Converters;

/// double 0..1 → 半透明深色背景画刷（只背景透明，文字不受影响）
public sealed class OpacityToBackgroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var opacity = value is double d ? d : 1.0;
        var alpha = (byte)(Math.Clamp(opacity, 0.0, 1.0) * 255);
        var brush = new SolidColorBrush(Color.FromArgb(alpha, 0x1E, 0x1E, 0x1E));
        brush.Freeze();
        return brush;
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
